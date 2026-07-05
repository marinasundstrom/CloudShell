import json
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from unittest.mock import patch

from cloudshell_sdk import (
    ConfigurationStoreClient,
    DefaultCloudShellCredential,
    EnvironmentTokenCredential,
    IdentityCredential,
    ProfileCredential,
    SecretsVaultClient,
    StaticTokenCredential,
)


class CloudShellPythonSdkTests(unittest.TestCase):
    def test_configuration_client_sends_bearer_token_and_reads_settings(self):
        with TestServer() as server:
            client = ConfigurationStoreClient(
                f"{server.url}/settings",
                StaticTokenCredential("test-token"))

            settings = client.get_settings()
            setting = client.get_setting("Sample--Message")

            self.assertEqual("Bearer test-token", server.requests[0]["authorization"])
            self.assertEqual("Sample--Message", settings[0].name)
            self.assertEqual("Hello", setting.value)

    def test_configuration_client_maps_portable_hierarchy_separator(self):
        with TestServer() as server:
            client = ConfigurationStoreClient(
                f"{server.url}/settings",
                StaticTokenCredential("test-token"))

            values = client.to_dict(map_portable_hierarchy_separator=True)

            self.assertEqual("Hello", values["Sample:Message"])

    def test_secrets_client_reads_secret_values(self):
        with TestServer() as server:
            client = SecretsVaultClient(
                f"{server.url}/secrets",
                StaticTokenCredential("test-token"))

            secrets = client.get_secrets()
            secret = client.get_secret("Sample--ApiKey", version="v1")

            self.assertEqual("Sample--ApiKey", secrets[0].name)
            self.assertEqual("secret-value", secret.value)
            self.assertEqual("/secrets/Sample--ApiKey?version=v1", server.requests[-1]["path"])

    def test_identity_credential_requests_client_credentials_token(self):
        with TestServer() as server:
            credential = IdentityCredential(
                environment={
                    "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT": f"{server.url}/token",
                    "CLOUDSHELL_IDENTITY_CLIENT_ID": "application.python-app:api/api",
                    "CLOUDSHELL_IDENTITY_CLIENT_SECRET": "secret",
                })

            token = credential.get_token(["ControlPlane.Access"])

            self.assertEqual("identity-token", token.token)
            self.assertIn("client_id=application.python-app%3Aapi%2Fapi", server.requests[-1]["body"])

    def test_profile_credential_reads_relative_token_file(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "tokens").mkdir()
            (root / "tokens" / "local.token").write_text("profile-token\n", encoding="utf-8")
            (root / "config.json").write_text(json.dumps({
                "activeProfile": "local",
                "profiles": {
                    "local": {
                        "credential": {
                            "kind": "staticBearer",
                            "accessTokenPath": "tokens/local.token",
                        }
                    }
                },
            }), encoding="utf-8")

            token = ProfileCredential(config_directory=root).get_token()

            self.assertEqual("profile-token", token.token)

    def test_default_credential_prefers_identity_before_environment_token(self):
        with TestServer() as server:
            credential = DefaultCloudShellCredential([
                IdentityCredential(environment={
                    "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT": f"{server.url}/token",
                    "CLOUDSHELL_IDENTITY_CLIENT_ID": "application.python-app:api/api",
                    "CLOUDSHELL_IDENTITY_CLIENT_SECRET": "secret",
                }),
                EnvironmentTokenCredential(environment={"CLOUDSHELL_TOKEN": "environment-token"}),
            ])

            token = credential.get_token()

            self.assertEqual("identity-token", token.token)

    def test_clients_discover_endpoints_from_environment(self):
        with patch.dict("os.environ", {
            "CLOUDSHELL_CONFIGURATION_SETTINGS_ENDPOINT": "http://localhost/settings",
            "CLOUDSHELL_SECRETS_VAULT_ENDPOINT": "http://localhost/secrets",
        }, clear=True):
            configuration = ConfigurationStoreClient.from_environment("settings", StaticTokenCredential("token"))
            secrets = SecretsVaultClient.from_environment("vault", StaticTokenCredential("token"))

        self.assertEqual("http://localhost/settings", configuration.settings_endpoint)
        self.assertEqual("http://localhost/secrets", secrets.secrets_endpoint)


class TestServer:
    def __enter__(self):
        self.requests = []
        requests = self.requests

        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                length = int(self.headers.get("content-length", "0"))
                body = self.rfile.read(length).decode("utf-8")
                requests.append({"method": "POST", "path": self.path, "body": body})
                self.send_response(200)
                self.send_header("content-type", "application/json")
                self.end_headers()
                self.wfile.write(json.dumps({
                    "access_token": "identity-token",
                    "expires_in": 3600,
                }).encode("utf-8"))

            def do_GET(self):
                requests.append({
                    "method": "GET",
                    "path": self.path,
                    "authorization": self.headers.get("authorization"),
                })
                if self.path == "/settings":
                    self._json([{"name": "Sample--Message", "value": "Hello"}])
                elif self.path == "/settings/Sample--Message":
                    self._json({"name": "Sample--Message", "value": "Hello"})
                elif self.path == "/secrets":
                    self._json([{"name": "Sample--ApiKey", "version": "v1"}])
                elif self.path.startswith("/secrets/Sample--ApiKey"):
                    self._json({"name": "Sample--ApiKey", "value": "secret-value", "version": "v1"})
                else:
                    self.send_response(404)
                    self.end_headers()

            def _json(self, value):
                self.send_response(200)
                self.send_header("content-type", "application/json")
                self.end_headers()
                self.wfile.write(json.dumps(value).encode("utf-8"))

            def log_message(self, format, *args):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.url = f"http://127.0.0.1:{self.server.server_port}"
        return self

    def __exit__(self, exc_type, exc, tb):
        self.server.shutdown()
        self.thread.join(timeout=5)
        self.server.server_close()


if __name__ == "__main__":
    unittest.main()

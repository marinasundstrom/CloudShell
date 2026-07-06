from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import sys


sdk_path = Path(__file__).resolve().parents[3] / "sdk" / "python" / "cloudshell"
if sdk_path.exists():
    sys.path.insert(0, str(sdk_path))


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path in ("/healthz", "/alive"):
            self.send_json({"status": "ok"})
            return

        if self.path == "/configuration":
            self.send_json({
                **read_configuration(),
                "resourceId": os.environ.get("CLOUDSHELL_RESOURCE_ID", ""),
                "resourceName": os.environ.get("CLOUDSHELL_RESOURCE_NAME", ""),
            })
            return

        self.send_json({
            "name": "python-container-api",
            "message": "CloudShell Python container app sample",
            "configuration": "/configuration",
            "health": "/healthz",
        })

    def send_json(self, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):
        print(format % args, flush=True)


def main():
    port = int(os.environ.get("PORT", "8080"))
    server = ThreadingHTTPServer(("0.0.0.0", port), Handler)
    print(f"Python container sample listening on http://0.0.0.0:{port}", flush=True)
    server.serve_forever()


def read_configuration():
    try:
        from cloudshell_sdk import ConfigurationStoreClient, SecretsVaultClient

        configuration = ConfigurationStoreClient.from_environment("python-container-settings")
        secrets = SecretsVaultClient.from_environment("python-container-secrets")
        setting = configuration.get_setting("Sample--Message")
        secret = secrets.get_secret("Sample--ApiKey")
        return {
            "source": "cloudshell-sdk",
            "message": setting.value if setting else "",
            "hasApiKey": bool(secret and secret.value),
            "secretName": secret.name if secret else "",
        }
    except Exception as exception:
        return {
            "source": "environment",
            "message": os.environ.get("Sample__Message", ""),
            "hasApiKey": bool(os.environ.get("Sample__ApiKey")),
            "sdkError": str(exception),
        }


if __name__ == "__main__":
    main()

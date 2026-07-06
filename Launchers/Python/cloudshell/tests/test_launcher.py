import json
import tempfile
import unittest
from pathlib import Path

from cloudshell.launcher import CloudShellDistributedApplication, LauncherOptions
from cloudshell.launcher import build_template_apply_command


class CloudShellPythonLauncherTests(unittest.TestCase):
    def test_builds_python_app_template(self):
        app = (
            CloudShellDistributedApplication.create_builder("python-app")
            .with_metadata("cloudshell.source", "python")
        )

        def define(resources):
            settings = (
                resources.add_configuration_store("settings")
                .with_display_name("Settings")
                .with_endpoint("http://localhost:5108")
                .with_seed(lambda seed: seed.setting("Sample--Message", "Hello"))
            )
            secrets = (
                resources.add_secrets_vault("vault")
                .with_endpoint("http://localhost:6108")
                .with_seed(lambda seed: seed.secret("Sample--ApiKey", "secret", "v1"))
            )
            (
                resources.add_python_app("api", "/workspace/app")
                .with_display_name("Python API")
                .with_service_discovery()
                .with_reference(settings)
                .with_reference(secrets)
                .depends_on(settings)
                .depends_on(secrets)
                .with_http_endpoint(host="localhost", port=5188, target_port=5188)
                .with_environment_variable("PORT", "5188")
                .with_environment_variable("Sample__Message", settings.setting("Sample--Message"))
                .with_environment_variable("Sample__ApiKey", secrets.secret("Sample--ApiKey"))
                .with_http_health_check("/healthz", endpoint_name="http")
                .with_http_liveness_check("/alive", endpoint_name="http")
                .require_identity(name="api")
                .provision_identity_on_startup()
            )
            settings.allow_resource_identity(
                "application.python-app:api",
                "CloudShell.Configuration/stores/settings/read/action",
                identity_name="api",
            )
            secrets.allow_resource_identity(
                "application.python-app:api",
                "CloudShell.Secrets/vaults/secrets/read/action",
                identity_name="api",
            )

        app.define_resources(define)

        template = app.build_template()
        self.assertEqual("python-app", template["name"])
        self.assertEqual("python", template["metadata"]["cloudshell.source"])
        resource_ids = [resource["resourceId"] for resource in template["resources"]]
        self.assertEqual(
            [
                "configuration.store:settings",
                "secrets.vault:vault",
                "application.python-app:api",
                "network:host",
            ],
            resource_ids,
        )

        api = template["resources"][2]
        self.assertEqual("application.python-app", api["type"])
        self.assertEqual("applications.python-app", api["providerId"])
        self.assertEqual("python3", api["python"]["command"])
        self.assertEqual("app.py", api["python"]["scriptPath"])
        self.assertEqual("/workspace/app", api["project"]["path"])
        self.assertEqual("api", api["project"]["serviceDiscoveryName"])
        self.assertEqual(
            {"value": "5188"},
            api["project"]["environmentVariables"]["PORT"],
        )
        self.assertEqual(
            "configuration.store:settings",
            api["project"]["environmentVariables"]["Sample__Message"]
            ["configurationSettingRef"]["storeResourceId"],
        )
        self.assertEqual(
            "secrets.vault:vault",
            api["project"]["environmentVariables"]["Sample__ApiKey"]
            ["secretRef"]["vaultResourceId"],
        )
        self.assertEqual("required", api["attributes"]["identity.kind"])
        self.assertEqual("api", api["attributes"]["identity.name"])
        self.assertTrue(api["attributes"]["identity.provisionOnStartup"])
        settings = template["resources"][0]
        self.assertEqual(
            "CloudShell.Configuration/stores/settings/read/action",
            settings["attributes"]["access.grants"][0]["permission"],
        )
        self.assertEqual(
            "application.python-app:api/identities/api",
            settings["attributes"]["access.grants"][0]["principal"]["id"],
        )
        self.assertEqual(2, len(api["health"]["checks"]))
        self.assertEqual("network:host", api["project"]["endpointRequests"][0]["network"]["resourceId"])

    def test_builds_javascript_app_template(self):
        app = CloudShellDistributedApplication.create_builder("python-declared-js")
        js = (
            app.add_javascript_app("frontend", "/workspace/frontend")
            .with_package_manager("pnpm")
            .with_script("dev")
            .with_http_endpoint(host="localhost", port=5175, target_port=5175)
        )

        template = app.build_template()
        self.assertEqual("application.javascript-app", js.resource_type)
        frontend = template["resources"][0]
        self.assertEqual("applications.javascript-app", frontend["providerId"])
        self.assertEqual("node", frontend["javascript"]["engine"])
        self.assertEqual("pnpm", frontend["javascript"]["packageManager"])
        self.assertEqual("dev", frontend["javascript"]["script"])

    def test_builds_python_app_as_container_app_template(self):
        app = CloudShellDistributedApplication.create_builder("python-container")
        network = app.default_network()
        (
            app.add_python_app("api", "samples/PythonAppHost/App")
            .with_http_endpoint(
                host="localhost",
                port=5188,
                target_port=8080,
                network=network,
            )
            .as_container_app(tag="dev", dockerfile="Dockerfile")
        )

        template = app.build_template()
        api = next(resource for resource in template["resources"] if resource["name"] == "api")

        self.assertEqual("application.container-app", api["type"])
        self.assertEqual("applications.container-app", api["providerId"])
        self.assertEqual("application.container-app:api", api["resourceId"])
        self.assertEqual("cloudshell-python-api:dev", api["container"]["image"])
        self.assertEqual(1, api["container"]["replicas"])
        self.assertEqual("samples/PythonAppHost/App", api["container"]["buildContext"])
        self.assertEqual("Dockerfile", api["container"]["dockerfile"])
        self.assertEqual(1, len(api["container"]["endpointRequests"]))
        self.assertNotIn("endpointRequests", api["project"])

    def test_writes_json_template(self):
        app = CloudShellDistributedApplication.create_builder("write-template")
        app.add_configuration_store("settings")
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "resources.json"
            written = app.write_template(path)

            self.assertEqual(str(path), written)
            document = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual("write-template", document["name"])

    def test_builds_dotnet_cli_apply_command(self):
        command, args = build_template_apply_command(
            "/tmp/resources.json",
            LauncherOptions(
                cli_project="/repo/CloudShell.Cli/CloudShell.Cli.csproj",
                control_plane_url="http://127.0.0.1:5107",
                state_dir="/tmp/state",
                no_build=True,
            ),
            start_host=True,
        )

        self.assertEqual("dotnet", command)
        self.assertEqual("run", args[0])
        self.assertIn("--start", args)
        self.assertIn("--no-build", args)
        self.assertIn("--control-plane", args)

    def test_javascript_app_matches_launcher_parity_fixture(self):
        app = (
            CloudShellDistributedApplication.create_builder("launcher-parity-javascript")
            .with_metadata("cloudshell.parity", "javascript-app")
        )

        def define(resources):
            settings = (
                resources.add_configuration_store("settings")
                .with_display_name("Settings")
                .with_endpoint("http://localhost:5101")
                .with_seed(lambda seed: seed.setting(
                    "Sample--Message",
                    "Hello from launcher parity",
                ))
            )
            secrets = (
                resources.add_secrets_vault("secrets")
                .with_display_name("Secrets")
                .with_endpoint("http://localhost:6101")
                .with_seed(lambda seed: seed.secret(
                    "Sample--ApiKey",
                    "parity-secret",
                    "v1",
                ))
            )
            (
                resources.add_javascript_app("frontend", "samples/LauncherParity/App")
                .with_display_name("Frontend")
                .with_service_discovery()
                .with_reference(settings)
                .with_reference(secrets)
                .depends_on(settings)
                .depends_on(secrets)
                .with_environment_variable("PORT", "5173")
                .with_environment_variable("Sample__Message", settings.setting("Sample--Message"))
                .with_environment_variable("Sample__ApiKey", secrets.secret("Sample--ApiKey"))
                .with_http_endpoint(host="localhost", port=5173, target_port=5173)
                .with_http_health_check("/healthz", endpoint_name="http")
                .with_http_liveness_check("/alive", endpoint_name="http")
            )

        app.define_resources(define)

        self.assertEqual(
            _normalize_template(_load_parity_fixture("javascript-app-parity.json")),
            _normalize_template(app.build_template()),
        )


def _load_parity_fixture(name):
    path = Path(__file__).resolve().parents[3] / "testdata" / name
    return json.loads(path.read_text(encoding="utf-8"))


def _normalize_template(template):
    normalized = _normalize_value(template)
    normalized["resources"] = sorted(
        normalized["resources"],
        key=lambda resource: resource["resourceId"],
    )
    return normalized


def _normalize_value(value):
    if isinstance(value, dict):
        if "resourceId" in value and "name" not in value and "type" not in value:
            return {"resourceId": value["resourceId"]}
        return {
            item_key: _normalize_value(item_value)
            for item_key, item_value in value.items()
        }
    if isinstance(value, list):
        return [_normalize_value(item) for item in value]
    return value


if __name__ == "__main__":
    unittest.main()

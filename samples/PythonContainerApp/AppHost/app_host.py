from pathlib import Path
from urllib.parse import urlparse
import os
import sys

from cloudshell.launcher import CloudShellDistributedApplication, LauncherOptions


app_host_dir = Path(__file__).resolve().parent
sample_dir = app_host_dir.parent
repo_root = sample_dir.parent.parent

app = (
    CloudShellDistributedApplication.create_builder(
        "python-container-app",
        sys.argv[1:],
        base_path=app_host_dir,
    )
    .with_metadata("cloudshell.source", "python")
    .with_metadata("cloudshell.sample", "PythonContainerApp")
)

python_app_path = app.resolve_path("..", "App")

settings_endpoint = os.getenv("CLOUDSHELL_SETTINGS_ENDPOINT", "http://localhost:5111")
secrets_endpoint = os.getenv("CLOUDSHELL_SECRETS_ENDPOINT", "http://localhost:6111")
app_endpoint = urlparse(os.getenv("CLOUDSHELL_APP_ENDPOINT", "http://localhost:5190"))


def define_resources(resources):
    network = resources.default_network()
    settings = (
        resources.add_configuration_store("python-container-settings")
        .with_display_name("Python Container Settings")
        .with_endpoint(settings_endpoint)
        .with_seed(
            lambda seed: seed.setting(
                "Sample--Message",
                "Hello from Python container app configuration",
            )
        )
    )
    secrets = (
        resources.add_secrets_vault("python-container-secrets")
        .with_display_name("Python Container Secrets")
        .with_endpoint(secrets_endpoint)
        .with_seed(
            lambda seed: seed.secret(
                "Sample--ApiKey",
                "python-container-secret",
                "v1",
            )
        )
    )

    api = (
        resources.add_python_app("python-container-api", python_app_path)
        .with_display_name("Python Container API")
        .with_service_discovery()
        .with_reference(settings)
        .with_reference(secrets)
        .depends_on(settings)
        .depends_on(secrets)
        .with_environment_variable("PORT", "8080")
        .with_environment_variable("OTEL_SERVICE_NAME", "python-container-api")
        .with_http_endpoint(
            host=app_endpoint.hostname,
            port=app_endpoint.port,
            target_port=8080,
            network=network,
        )
        .with_http_health_check("/healthz", endpoint_name="http")
        .with_http_liveness_check("/alive", endpoint_name="http")
        .as_container_app(
            tag="dev",
            build_context=repo_root,
            dockerfile="samples/PythonContainerApp/App/Dockerfile",
            replicas=2,
        )
        .require_identity(name="python-container-api")
        .provision_identity_on_startup()
    )
    settings.allow_resource_identity(
        api,
        "CloudShell.Configuration/stores/settings/read/action",
        identity_name="python-container-api",
    )
    secrets.allow_resource_identity(
        api,
        "CloudShell.Secrets/vaults/secrets/read/action",
        identity_name="python-container-api",
    )


app.define_resources(define_resources)

defaults = LauncherOptions(
    cli_project=os.getenv(
        "CLOUDSHELL_CLI_PROJECT",
        str(repo_root / "CloudShell.Cli" / "CloudShell.Cli.csproj"),
    ),
    template_path=os.getenv(
        "CLOUDSHELL_TEMPLATE_PATH",
        str(sample_dir / ".cloudshell" / "resources.json"),
    ),
    control_plane_url=os.getenv(
        "CLOUDSHELL_CONTROL_PLANE_URL",
        "http://127.0.0.1:5110",
    ),
    state_dir=os.getenv("CLOUDSHELL_STATE_DIR", str(sample_dir / ".cloudshell")),
    data_dir=os.getenv(
        "CLOUDSHELL_DATA_DIR",
        str(sample_dir / ".cloudshell" / "data"),
    ),
    host_project=os.getenv(
        "CLOUDSHELL_HOST_PROJECT",
        str(
            repo_root
            / "CloudShell.LocalDevelopmentHost"
            / "CloudShell.LocalDevelopmentHost.csproj"
        ),
    ),
    host_url=os.getenv(
        "CLOUDSHELL_CONTROL_PLANE_URL",
        "http://127.0.0.1:5110",
    ),
)

raise SystemExit(app.run(options=defaults))

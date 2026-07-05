from pathlib import Path
from urllib.parse import quote, urlparse
import os
import sys

from cloudshell.launcher import CloudShellDistributedApplication, LauncherOptions


app_host_dir = Path(__file__).resolve().parent
sample_dir = app_host_dir.parent
repo_root = sample_dir.parent.parent

app = (
    CloudShellDistributedApplication.create_builder(
        "python-app-host",
        sys.argv[1:],
        base_path=app_host_dir,
    )
    .with_metadata("cloudshell.source", "python")
    .with_metadata("cloudshell.sample", "PythonAppHost")
)

python_app_path = app.resolve_path("..", "App")

settings_endpoint = (
    app.configuration.get("PythonAppHost:SettingsEndpoint")
    or os.getenv("CLOUDSHELL_SETTINGS_ENDPOINT")
    or "http://localhost:5108"
)
secrets_endpoint = (
    app.configuration.get("PythonAppHost:SecretsEndpoint")
    or os.getenv("CLOUDSHELL_SECRETS_ENDPOINT")
    or "http://localhost:6108"
)
settings_resource_id = "configuration.store:python-app-settings"
settings_api_endpoint = (
    f"{settings_endpoint.rstrip('/')}"
    f"/api/configuration/stores/{quote(settings_resource_id, safe='')}/settings"
)

app_endpoint = urlparse(
    app.configuration.get("PythonAppHost:AppEndpoint")
    or os.getenv("CLOUDSHELL_APP_ENDPOINT")
    or "http://localhost:5188"
)


def define_resources(resources):
    settings = (
        resources.add_configuration_store("python-app-settings")
        .with_display_name("Settings")
        .with_endpoint(settings_endpoint)
        .with_seed(
            lambda seed: seed.setting(
                "Sample--Message",
                "Hello from the Python app configuration store",
            )
        )
    )

    secrets = (
        resources.add_secrets_vault("python-app-secrets")
        .with_display_name("Secrets")
        .with_endpoint(secrets_endpoint)
        .with_seed(
            lambda seed: seed.secret(
                "Sample--ApiKey",
                "python-local-development-secret",
                "v1",
            )
        )
    )

    (
        resources.add_python_app("python-api", python_app_path)
        .with_display_name("Python API")
        .with_service_discovery()
        .with_reference(settings)
        .with_reference(secrets)
        .depends_on(settings)
        .depends_on(secrets)
        .with_http_endpoint(
            host=app_endpoint.hostname,
            port=app_endpoint.port,
            target_port=app_endpoint.port,
        )
        .with_environment_variable("PORT", str(app_endpoint.port))
        .with_environment_variable(
            "CLOUDSHELL_SETTINGS_ENDPOINT",
            settings_api_endpoint,
        )
        .with_environment_variable(
            "Sample__Message",
            settings.setting("Sample--Message"),
        )
        .with_environment_variable(
            "Sample__ApiKey",
            secrets.secret("Sample--ApiKey"),
        )
        .with_environment_variable("OTEL_SERVICE_NAME", "python-api")
        .with_http_health_check("/healthz", endpoint_name="http")
        .with_http_liveness_check("/alive", endpoint_name="http")
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
        "http://127.0.0.1:5107",
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
        "http://127.0.0.1:5107",
    ),
)

raise SystemExit(app.run(options=defaults))

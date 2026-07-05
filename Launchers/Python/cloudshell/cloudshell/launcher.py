from __future__ import annotations

import asyncio
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Iterable


ResourceCallback = Callable[["CloudShellDistributedApplication"], None]


def _require(value: str | Path | None, name: str) -> str:
    text = "" if value is None else str(value).strip()
    if not text:
        raise ValueError(f"{name} is required")
    return text


def _prune(value: Any) -> Any:
    if isinstance(value, dict):
        return {
            key: _prune(item)
            for key, item in value.items()
            if item is not None and item != {} and item != []
        }
    if isinstance(value, list):
        return [_prune(item) for item in value if item is not None]
    return value


def _add_option(args: list[str], name: str, value: str | int | None) -> None:
    if value is None:
        return
    text = str(value).strip()
    if text:
        args.extend([name, text])


def _reference(
    resource: ResourceHandle | str,
    relationship: str,
    type_id: str | None = None,
    provider_id: str | None = None,
) -> dict[str, Any]:
    if isinstance(resource, ResourceHandle):
        return _prune(
            {
                "resourceId": resource.effective_resource_id,
                "relationship": relationship,
                "addressingMode": "resourceId",
                "typeId": resource.resource_type,
                "providerId": resource.provider_id,
            }
        )

    return _prune(
        {
            "resourceId": _require(resource, "resource id"),
            "relationship": relationship,
            "addressingMode": "resourceId",
            "typeId": type_id,
            "providerId": provider_id,
        }
    )


@dataclass
class CommandResult:
    command: str
    args: list[str]
    exit_code: int
    template_path: str | None = None


@dataclass
class LauncherOptions:
    cli_project: str | None = None
    cloudshell_command: str = "cloudshell"
    template_path: str | None = None
    control_plane_url: str | None = None
    state_dir: str | None = None
    data_dir: str | None = None
    host_project: str | None = None
    host_url: str | None = None
    no_build: bool = False
    timeout_seconds: int = 60
    mode: str = "create-or-update"
    bearer_token: str | None = None
    working_directory: str | None = None
    inherit_io: bool = True

    @classmethod
    def from_args(
        cls,
        args: Iterable[str],
        defaults: "LauncherOptions | None" = None,
    ) -> "LauncherOptions":
        options = cls(**(defaults.__dict__ if defaults else {}))
        values = list(args)
        index = 0
        while index < len(values):
            arg = values[index]
            if arg == "--no-build":
                options.no_build = True
                index += 1
                continue
            if arg == "--pipe":
                options.inherit_io = False
                index += 1
                continue

            index += 1
            if index >= len(values):
                raise ValueError(f"{arg} requires a value")
            value = values[index]
            if arg == "--cli-project":
                options.cli_project = value
            elif arg == "--cloudshell-command":
                options.cloudshell_command = value
            elif arg == "--template-path":
                options.template_path = value
            elif arg == "--control-plane":
                options.control_plane_url = value
            elif arg == "--state-dir":
                options.state_dir = value
            elif arg == "--data-dir":
                options.data_dir = value
            elif arg == "--host-project":
                options.host_project = value
            elif arg == "--url":
                options.host_url = value
            elif arg == "--timeout-seconds":
                options.timeout_seconds = int(value)
            elif arg == "--mode":
                options.mode = value
            elif arg == "--bearer-token":
                options.bearer_token = value
            elif arg == "--cwd":
                options.working_directory = value
            else:
                raise ValueError(f"unknown option: {arg}")
            index += 1

        return options


class Configuration:
    def __init__(self, base_path: Path) -> None:
        self._values: dict[str, str] = {}
        self._load(base_path / "appsettings.json")

    def get(self, key: str, default: str | None = None) -> str | None:
        return self._values.get(key, default)

    def _load(self, path: Path) -> None:
        if not path.exists():
            return
        data = json.loads(path.read_text(encoding="utf-8"))
        self._flatten("", data)

    def _flatten(self, prefix: str, value: Any) -> None:
        if isinstance(value, dict):
            for key, item in value.items():
                next_key = f"{prefix}:{key}" if prefix else str(key)
                self._flatten(next_key, item)
            return
        self._values[prefix] = str(value)


class ResourceHandle:
    name: str
    resource_type: str
    provider_id: str | None
    effective_resource_id: str


class ResourceBuilder(ResourceHandle):
    def __init__(
        self,
        name: str,
        resource_type: str,
        provider_id: str | None = None,
    ) -> None:
        self.name = _require(name, "resource name")
        self.resource_type = _require(resource_type, "resource type")
        self.provider_id = provider_id
        self.effective_resource_id = f"{self.resource_type}:{self.name}"
        self.display_name: str | None = None
        self.dependencies: list[dict[str, Any]] = []
        self.metadata: dict[str, str] = {}
        self.auto_start: bool | None = None
        self.dependency_auto_start: bool | None = None
        self._graph: CloudShellDistributedApplication | None = None

    def _use_graph(self, graph: "CloudShellDistributedApplication") -> None:
        self._graph = graph

    def with_resource_id(self, resource_id: str) -> "ResourceBuilder":
        self.effective_resource_id = _require(resource_id, "resource id")
        return self

    def with_display_name(self, display_name: str) -> "ResourceBuilder":
        self.display_name = display_name
        return self

    def with_metadata(self, name: str, value: str) -> "ResourceBuilder":
        self.metadata[_require(name, "metadata name")] = value
        return self

    def with_auto_start(self, enabled: bool) -> "ResourceBuilder":
        self.auto_start = bool(enabled)
        return self

    def with_dependency_auto_start(self, enabled: bool) -> "ResourceBuilder":
        self.dependency_auto_start = bool(enabled)
        return self

    def depends_on(self, resource: ResourceHandle | str) -> "ResourceBuilder":
        self.dependencies.append(_reference(resource, "dependsOn"))
        return self

    def _common_document(self) -> dict[str, Any]:
        return _prune(
            {
                "name": self.name,
                "type": self.resource_type,
                "resourceId": self.effective_resource_id,
                "providerId": self.provider_id,
                "displayName": self.display_name,
                "dependsOn": self.dependencies,
                "metadata": self.metadata,
            }
        )

    def build(self) -> dict[str, Any]:
        return self._common_document()


class NetworkResource(ResourceBuilder):
    def __init__(self, name: str) -> None:
        super().__init__(name, "cloudshell.network", "cloudshell.network")
        self.kind: str | None = None
        self.host_readiness: str | None = None

    def with_network_kind(self, kind: str) -> "NetworkResource":
        self.kind = kind
        return self

    def with_host_readiness(self, host_readiness: str) -> "NetworkResource":
        self.host_readiness = host_readiness
        return self

    def build(self) -> dict[str, Any]:
        document = self._common_document()
        document["network"] = _prune(
            {
                "kind": self.kind,
                "hostReadiness": self.host_readiness,
            }
        )
        return _prune(document)


class ConfigurationStoreSeed:
    def __init__(self) -> None:
        self.settings: list[dict[str, str]] = []

    def setting(self, name: str, value: str) -> "ConfigurationStoreSeed":
        self.settings.append({"name": _require(name, "setting name"), "value": value})
        return self


class ConfigurationStoreResource(ResourceBuilder):
    def __init__(self, name: str) -> None:
        super().__init__(name, "configuration.store", "configuration")
        self.endpoint: str | None = None
        self.settings: list[dict[str, str]] = []

    def with_endpoint(self, endpoint: str) -> "ConfigurationStoreResource":
        self.endpoint = endpoint
        return self

    def with_seed(
        self,
        configure: Callable[[ConfigurationStoreSeed], Any],
    ) -> "ConfigurationStoreResource":
        seed = ConfigurationStoreSeed()
        configure(seed)
        self.settings = list(seed.settings)
        return self

    def setting(self, name: str, version: str | None = None) -> dict[str, Any]:
        return _prune(
            {
                "configurationSettingRef": {
                    "storeResourceId": self.effective_resource_id,
                    "name": _require(name, "setting name"),
                    "version": version,
                }
            }
        )

    def build(self) -> dict[str, Any]:
        document = self._common_document()
        document["endpoint"] = self.endpoint
        if self.settings:
            document["seed"] = {"settings": self.settings}
        return _prune(document)


class SecretsVaultSeed:
    def __init__(self) -> None:
        self.secrets: list[dict[str, str]] = []
        self.certificates: list[dict[str, str]] = []

    def secret(
        self,
        name: str,
        value: str,
        version: str | None = None,
    ) -> "SecretsVaultSeed":
        self.secrets.append(
            _prune(
                {
                    "name": _require(name, "secret name"),
                    "value": value,
                    "version": version,
                }
            )
        )
        return self

    def certificate(
        self,
        name: str,
        value: str,
        version: str | None = None,
        content_type: str | None = None,
    ) -> "SecretsVaultSeed":
        self.certificates.append(
            _prune(
                {
                    "name": _require(name, "certificate name"),
                    "value": value,
                    "version": version,
                    "contentType": content_type,
                }
            )
        )
        return self


class SecretsVaultResource(ResourceBuilder):
    def __init__(self, name: str) -> None:
        super().__init__(name, "secrets.vault", "secrets-vault")
        self.endpoint: str | None = None
        self.secrets: list[dict[str, str]] = []
        self.certificates: list[dict[str, str]] = []

    def with_endpoint(self, endpoint: str) -> "SecretsVaultResource":
        self.endpoint = endpoint
        return self

    def with_seed(
        self,
        configure: Callable[[SecretsVaultSeed], Any],
    ) -> "SecretsVaultResource":
        seed = SecretsVaultSeed()
        configure(seed)
        self.secrets = list(seed.secrets)
        self.certificates = list(seed.certificates)
        return self

    def secret(self, name: str, version: str | None = None) -> dict[str, Any]:
        return _prune(
            {
                "secretRef": {
                    "vaultResourceId": self.effective_resource_id,
                    "name": _require(name, "secret name"),
                    "version": version,
                }
            }
        )

    def build(self) -> dict[str, Any]:
        document = self._common_document()
        document["endpoint"] = self.endpoint
        seed: dict[str, Any] = {}
        if self.secrets:
            seed["secrets"] = self.secrets
        if self.certificates:
            seed["certificates"] = self.certificates
        if seed:
            document["seed"] = seed
        return _prune(document)


class ProjectApplicationResource(ResourceBuilder):
    language_key = "project"

    def __init__(self, name: str, resource_type: str, provider_id: str) -> None:
        super().__init__(name, resource_type, provider_id)
        self.project_path: str | None = None
        self.service_discovery_name: str | None = None
        self.environment_variables: dict[str, dict[str, Any]] = {}
        self.references: list[dict[str, Any]] = []
        self.endpoints: list[dict[str, Any]] = []
        self.health_checks: list[dict[str, Any]] = []
        self.console_logs = False
        self.attributes: dict[str, Any] = {}

    def with_project_path(self, project_path: str | Path) -> "ProjectApplicationResource":
        self.project_path = _require(project_path, "project path")
        return self

    def with_service_discovery(
        self,
        name: str | None = None,
    ) -> "ProjectApplicationResource":
        self.service_discovery_name = name or self.name
        return self

    def with_reference(
        self,
        resource: ResourceHandle | str,
        type_id: str | None = None,
        provider_id: str | None = None,
    ) -> "ProjectApplicationResource":
        self.references.append(_reference(resource, "reference", type_id, provider_id))
        return self

    def with_environment_variable(
        self,
        name: str,
        value: str | dict[str, Any],
    ) -> "ProjectApplicationResource":
        variable_name = _require(name, "environment variable name")
        self.environment_variables[variable_name] = (
            {"value": value} if isinstance(value, str) else _prune(value)
        )
        return self

    def with_http_endpoint(
        self,
        host: str | None = None,
        port: int | None = None,
        target_port: int | None = None,
        name: str = "http",
        exposure: str = "Local",
        ip_address: str | None = None,
        network: ResourceHandle | str | None = None,
        assignment: str | None = None,
    ) -> "ProjectApplicationResource":
        effective_network = network
        if effective_network is None and self._graph is not None:
            effective_network = self._graph.default_network()
        self.endpoints.append(
            _prune(
                {
                    "name": name,
                    "protocol": "http",
                    "targetPort": target_port,
                    "host": host,
                    "port": port,
                    "exposure": exposure,
                    "ipAddress": ip_address,
                    "assignment": assignment,
                    "network": _reference(effective_network, "reference")
                    if effective_network is not None
                    else None,
                }
            )
        )
        return self

    def with_http_health_check(
        self,
        path: str,
        endpoint_name: str | None = None,
        name: str = "health",
        timeout_milliseconds: int | None = None,
        interval_seconds: int | None = None,
    ) -> "ProjectApplicationResource":
        return self.with_http_probe(
            "health",
            path,
            endpoint_name,
            name,
            timeout_milliseconds,
            interval_seconds,
        )

    def with_http_liveness_check(
        self,
        path: str,
        endpoint_name: str | None = None,
        name: str = "alive",
        timeout_milliseconds: int | None = None,
        interval_seconds: int | None = None,
    ) -> "ProjectApplicationResource":
        return self.with_http_probe(
            "liveness",
            path,
            endpoint_name,
            name,
            timeout_milliseconds,
            interval_seconds,
        )

    def with_http_probe(
        self,
        probe_type: str,
        path: str,
        endpoint_name: str | None = None,
        name: str | None = None,
        timeout_milliseconds: int | None = None,
        interval_seconds: int | None = None,
    ) -> "ProjectApplicationResource":
        self.health_checks.append(
            _prune(
                {
                    "name": name or probe_type,
                    "type": _require(probe_type, "probe type"),
                    "source": {
                        "kind": "http",
                        "http": {
                            "path": _require(path, "probe path"),
                            "endpointName": endpoint_name,
                            "timeoutMilliseconds": timeout_milliseconds,
                        },
                    },
                    "intervalSeconds": interval_seconds,
                }
            )
        )
        return self

    def with_default_console_log_source(
        self,
        log_format: str = "plainText",
    ) -> "ProjectApplicationResource":
        self.console_logs = True
        self.attributes["consoleLogFormat"] = log_format
        return self

    def build(self) -> dict[str, Any]:
        document = self._common_document()
        project = _prune(
            {
                "path": self.project_path,
                "serviceDiscoveryName": self.service_discovery_name,
                "environmentVariables": self.environment_variables,
                "references": self.references,
                "endpointRequests": self.endpoints,
            }
        )
        if project:
            document["project"] = project
        if self.health_checks:
            document["health"] = {"checks": self.health_checks}
        if self.console_logs:
            document["logs"] = {
                "sources": [
                    {
                        "id": "console",
                        "name": "Console logs",
                        "kind": "processOutput",
                        "format": self.attributes["consoleLogFormat"],
                        "capabilities": ["read", "stream"],
                        "description": "Provider-captured process console output.",
                        "origin": "providerDefault",
                        "purpose": "default",
                        "availability": "resourceRunning",
                    }
                ]
            }
        return _prune(document)


class JavaScriptAppResource(ProjectApplicationResource):
    def __init__(self, name: str, project_path: str | Path) -> None:
        super().__init__(
            name,
            "application.javascript-app",
            "applications.javascript-app",
        )
        self.with_project_path(project_path)
        self.with_engine("node")
        self.with_package_manager("npm")
        self.with_script("dev")
        self.with_default_console_log_source()

    def with_engine(self, engine: str) -> "JavaScriptAppResource":
        self.attributes["engine"] = _require(engine, "JavaScript engine")
        return self

    def with_package_manager(self, package_manager: str) -> "JavaScriptAppResource":
        self.attributes["packageManager"] = _require(
            package_manager,
            "JavaScript package manager",
        )
        return self

    def with_script(self, script: str) -> "JavaScriptAppResource":
        self.attributes["script"] = _require(script, "JavaScript script")
        return self

    def with_arguments(self, arguments: str) -> "JavaScriptAppResource":
        self.attributes["arguments"] = arguments
        return self

    def build(self) -> dict[str, Any]:
        document = super().build()
        document["javascript"] = _prune(
            {
                "engine": self.attributes.get("engine"),
                "packageManager": self.attributes.get("packageManager"),
                "script": self.attributes.get("script"),
                "arguments": self.attributes.get("arguments"),
            }
        )
        return _prune(document)


class PythonAppResource(ProjectApplicationResource):
    def __init__(self, name: str, project_path: str | Path, script_path: str = "app.py") -> None:
        super().__init__(
            name,
            "application.python-app",
            "applications.python-app",
        )
        self.with_project_path(project_path)
        self.with_command("python3")
        self.with_script_path(script_path)
        self.with_default_console_log_source()

    def with_command(self, command: str) -> "PythonAppResource":
        self.attributes["command"] = _require(command, "Python command")
        return self

    def with_script_path(self, script_path: str) -> "PythonAppResource":
        self.attributes["scriptPath"] = _require(script_path, "Python script path")
        self.attributes.pop("module", None)
        return self

    def with_module(self, module: str) -> "PythonAppResource":
        self.attributes["module"] = _require(module, "Python module")
        self.attributes.pop("scriptPath", None)
        return self

    def with_arguments(self, arguments: str) -> "PythonAppResource":
        self.attributes["arguments"] = arguments
        return self

    def build(self) -> dict[str, Any]:
        document = super().build()
        document["python"] = _prune(
            {
                "command": self.attributes.get("command"),
                "scriptPath": self.attributes.get("scriptPath"),
                "module": self.attributes.get("module"),
                "arguments": self.attributes.get("arguments"),
            }
        )
        return _prune(document)


class CloudShellDistributedApplication:
    def __init__(self, name: str, args: list[str] | None = None, base_path: Path | None = None) -> None:
        self.name = _require(name, "app name")
        self.args = list(sys.argv[1:] if args is None else args)
        self.base_path = (base_path or Path.cwd()).resolve()
        self.configuration = Configuration(self.base_path)
        self.environment_id: str | None = None
        self.metadata: dict[str, str] = {}
        self.resources: list[ResourceBuilder] = []

    @classmethod
    def create_builder(
        cls,
        name: str,
        args: list[str] | None = None,
        base_path: Path | None = None,
    ) -> "CloudShellDistributedApplication":
        return cls(name, args, base_path)

    def with_environment_id(self, environment_id: str) -> "CloudShellDistributedApplication":
        self.environment_id = environment_id.strip()
        return self

    def with_metadata(self, name: str, value: str) -> "CloudShellDistributedApplication":
        self.metadata[_require(name, "metadata name")] = value
        return self

    def resolve_path(self, *parts: str | Path) -> str:
        return str(self.base_path.joinpath(*map(Path, parts)).resolve())

    def add(self, resource: ResourceBuilder) -> ResourceBuilder:
        for existing in self.resources:
            if existing.effective_resource_id.lower() == resource.effective_resource_id.lower():
                raise ValueError(f"resource {resource.effective_resource_id!r} is already defined")
        resource._use_graph(self)
        self.resources.append(resource)
        return resource

    def add_network(self, name: str) -> NetworkResource:
        return self.add(NetworkResource(name))  # type: ignore[return-value]

    def default_network(self) -> NetworkResource:
        for resource in self.resources:
            if resource.effective_resource_id.lower() == "network:host":
                if not isinstance(resource, NetworkResource):
                    raise ValueError("resource 'network:host' is already defined with another type")
                return resource
        return (
            self.add_network("host")
            .with_resource_id("network:host")
            .with_display_name("Host network")
            .with_network_kind("Host")
            .with_host_readiness("hostReady")
        )

    def add_configuration_store(self, name: str) -> ConfigurationStoreResource:
        return self.add(ConfigurationStoreResource(name))  # type: ignore[return-value]

    def add_secrets_vault(self, name: str) -> SecretsVaultResource:
        return self.add(SecretsVaultResource(name))  # type: ignore[return-value]

    def add_javascript_app(self, name: str, project_path: str | Path) -> JavaScriptAppResource:
        return self.add(JavaScriptAppResource(name, project_path))  # type: ignore[return-value]

    def add_python_app(
        self,
        name: str,
        project_path: str | Path,
        script_path: str = "app.py",
    ) -> PythonAppResource:
        return self.add(PythonAppResource(name, project_path, script_path))  # type: ignore[return-value]

    def define_resources(self, configure: ResourceCallback) -> "CloudShellDistributedApplication":
        configure(self)
        return self

    def build_template(self) -> dict[str, Any]:
        return _prune(
            {
                "name": self.name,
                "resources": [resource.build() for resource in self.resources],
                "environmentId": self.environment_id,
                "metadata": self.metadata,
            }
        )

    def to_json(self) -> str:
        return json.dumps(self.build_template(), indent=2) + "\n"

    def write_template(self, path: str | Path) -> str:
        template_path = Path(path)
        template_path.parent.mkdir(parents=True, exist_ok=True)
        template_path.write_text(self.to_json(), encoding="utf-8")
        return str(template_path)

    def apply(self, options: LauncherOptions | None = None) -> CommandResult:
        effective = options or LauncherOptions()
        template_path = self._write_template_for_launcher(effective)
        command, args = build_template_apply_command(template_path, effective, start_host=False)
        exit_code = run_command(command, args, effective)
        return CommandResult(command, args, exit_code, template_path)

    def start(self, options: LauncherOptions | None = None) -> CommandResult:
        effective = options or LauncherOptions()
        template_path = self._write_template_for_launcher(effective)
        command, args = build_template_apply_command(template_path, effective, start_host=True)
        exit_code = run_command(command, args, effective)
        return CommandResult(command, args, exit_code, template_path)

    def foreground_run(self, options: LauncherOptions | None = None) -> CommandResult:
        effective = options or LauncherOptions()
        template_path = self._write_template_for_launcher(effective)
        host_url = (effective.host_url or effective.control_plane_url or "").rstrip("/")
        if not host_url:
            raise ValueError("a host URL or Control Plane URL is required for foreground run")
        if not effective.host_project:
            raise ValueError("a host project is required for foreground run")

        host_command, host_args = build_host_run_command(effective, host_url)
        host = start_command(host_command, host_args, effective)
        try:
            wait_for_ready(host, host_url, effective.bearer_token, effective.timeout_seconds)
            apply_options = LauncherOptions(
                cli_project=effective.cli_project,
                cloudshell_command=effective.cloudshell_command,
                control_plane_url=host_url,
                timeout_seconds=effective.timeout_seconds,
                mode=effective.mode,
                bearer_token=effective.bearer_token,
                inherit_io=effective.inherit_io,
                working_directory=effective.working_directory,
            )
            apply_command, apply_args = build_template_apply_command(
                template_path,
                apply_options,
                start_host=False,
            )
            apply_exit_code = run_command(apply_command, apply_args, apply_options)
            if apply_exit_code != 0:
                host.terminate()
                return CommandResult(apply_command, apply_args, apply_exit_code, template_path)

            print(format_host_url_message(host_url))
            exit_code = host.wait()
            return CommandResult(host_command, host_args, exit_code, template_path)
        finally:
            if host.poll() is None:
                host.terminate()

    def run(
        self,
        args: list[str] | None = None,
        options: LauncherOptions | None = None,
    ) -> int:
        command_args = list(self.args if args is None else args)
        command = "run"
        legacy_flags = {
            "--apply": "apply",
            "--start": "start",
            "--run": "run",
        }
        if command_args and command_args[0] in legacy_flags:
            command = legacy_flags[command_args.pop(0)]
        elif command_args and not command_args[0].startswith("-"):
            command = command_args.pop(0)
        effective = LauncherOptions.from_args(command_args, options)
        try:
            if command in {"template", "toJson", "to_json", "json"}:
                print(self.to_json(), end="")
                return 0
            if command == "apply":
                return self.apply(effective).exit_code
            if command == "start":
                return self.start(effective).exit_code
            if command == "run":
                return self.foreground_run(effective).exit_code
        except (OSError, RuntimeError, TimeoutError, ValueError) as error:
            print(error, file=sys.stderr)
            return 1

        print(f"unknown launcher command: {command}", file=sys.stderr)
        return 2

    async def launch_async(
        self,
        args: list[str] | None = None,
        options: LauncherOptions | None = None,
    ) -> int:
        return await asyncio.to_thread(self.run, args, options)

    def launch(
        self,
        args: list[str] | None = None,
        options: LauncherOptions | None = None,
    ) -> int:
        return self.run(args, options)

    def _write_template_for_launcher(self, options: LauncherOptions) -> str:
        template_path = options.template_path
        if not template_path:
            directory = options.state_dir or tempfile.mkdtemp(prefix="cloudshell-template-")
            template_path = str(Path(directory) / "resources.json")
        return self.write_template(template_path)


def build_template_apply_command(
    template_path: str,
    options: LauncherOptions,
    start_host: bool,
) -> tuple[str, list[str]]:
    args = ["template", "apply", template_path]
    _add_option(args, "--control-plane", options.control_plane_url)
    _add_option(args, "--state-dir", options.state_dir)
    _add_option(args, "--host-project", options.host_project)
    _add_option(args, "--data-dir", options.data_dir)
    _add_option(args, "--url", options.host_url)
    if options.timeout_seconds > 0:
        _add_option(args, "--timeout-seconds", options.timeout_seconds)
    _add_option(args, "--mode", options.mode)
    _add_option(args, "--bearer-token", options.bearer_token)
    if start_host:
        args.append("--start")
    if options.no_build:
        args.append("--no-build")
    if options.cli_project:
        return "dotnet", ["run", "--project", options.cli_project, "--", *args]
    return options.cloudshell_command, args


def build_host_run_command(options: LauncherOptions, host_url: str) -> tuple[str, list[str]]:
    args = ["run"]
    if options.no_build:
        args.append("--no-build")
    args.extend(["--project", _require(options.host_project, "host project"), "--", "--urls", host_url])
    if options.data_dir:
        args.extend(["--CloudShell:DataDirectory", options.data_dir])
    return "dotnet", args


def run_command(command: str, args: list[str], options: LauncherOptions) -> int:
    process = start_command(command, args, options)
    return process.wait()


def start_command(
    command: str,
    args: list[str],
    options: LauncherOptions,
) -> subprocess.Popen[Any]:
    stdout: Any = None if options.inherit_io else subprocess.PIPE
    stderr: Any = None if options.inherit_io else subprocess.PIPE
    executable = shutil.which(command) or command
    return subprocess.Popen(
        [executable, *args],
        cwd=options.working_directory or None,
        stdout=stdout,
        stderr=stderr,
        text=True,
    )


def wait_for_ready(
    process: subprocess.Popen[Any],
    host_url: str,
    bearer_token: str | None,
    timeout_seconds: int,
) -> None:
    deadline = time.time() + timeout_seconds
    headers = {"Authorization": f"Bearer {bearer_token}"} if bearer_token else {}
    url = f"{host_url.rstrip('/')}/api/control-plane/v1/resources"
    last_error: Exception | None = None
    while time.time() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f"CloudShell host exited with code {process.returncode}")
        try:
            request = urllib.request.Request(url, headers=headers)
            with urllib.request.urlopen(request, timeout=2) as response:
                if 200 <= response.status < 500:
                    return
        except (urllib.error.URLError, TimeoutError, OSError) as error:
            last_error = error
        time.sleep(0.25)
    raise TimeoutError(f"CloudShell host was not ready within {timeout_seconds}s: {last_error}")


def format_host_url_message(host_url: str) -> str:
    return f"CloudShell UI: {host_url.rstrip('/')}"

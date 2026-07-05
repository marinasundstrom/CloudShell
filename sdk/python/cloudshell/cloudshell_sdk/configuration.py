from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable
from urllib.parse import quote

from .credential import DEFAULT_SCOPE, DefaultCloudShellCredential, TokenCredential
from .environment import find_endpoint
from .http import get_json


@dataclass(frozen=True)
class CloudShellConfigurationSetting:
    name: str
    value: str


class ConfigurationStoreClient:
    def __init__(
        self,
        settings_endpoint: str,
        credential: TokenCredential | None = None,
        scopes: Iterable[str] | None = None,
    ) -> None:
        self.settings_endpoint = settings_endpoint.rstrip("/")
        self.credential = credential or DefaultCloudShellCredential()
        self.scopes = list(scopes or [DEFAULT_SCOPE])

    @classmethod
    def from_environment(
        cls,
        service_name: str | None = None,
        credential: TokenCredential | None = None,
    ) -> "ConfigurationStoreClient":
        endpoint = find_endpoint("CLOUDSHELL_CONFIGURATION_", service_name)
        if not endpoint:
            raise RuntimeError("No CloudShell configuration store endpoint was found in the environment.")
        return cls(endpoint, credential)

    def get_settings(self) -> list[CloudShellConfigurationSetting]:
        values = get_json(self.settings_endpoint, self.credential, self.scopes)
        return [
            CloudShellConfigurationSetting(str(item.get("name", "")), str(item.get("value", "")))
            for item in values
        ]

    def get_setting(self, name: str) -> CloudShellConfigurationSetting | None:
        if not name.strip():
            raise ValueError("Configuration setting name is required.")
        value = get_json(
            f"{self.settings_endpoint}/{quote(name, safe='')}",
            self.credential,
            self.scopes,
            allow_not_found=True)
        if value is None:
            return None
        return CloudShellConfigurationSetting(str(value.get("name", "")), str(value.get("value", "")))

    def to_dict(self, map_portable_hierarchy_separator: bool = False) -> dict[str, str]:
        result: dict[str, str] = {}
        for setting in self.get_settings():
            name = setting.name.replace("--", ":") if map_portable_hierarchy_separator else setting.name
            result[name] = setting.value
        return result

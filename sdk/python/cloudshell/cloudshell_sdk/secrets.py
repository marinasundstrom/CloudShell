from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable
from urllib.parse import quote, urlencode

from .credential import DEFAULT_SCOPE, DefaultCloudShellCredential, TokenCredential
from .environment import find_endpoint
from .http import get_json


@dataclass(frozen=True)
class SecretProperties:
    name: str
    version: str | None = None


@dataclass(frozen=True)
class SecretValue:
    name: str
    value: str
    version: str | None = None


class SecretsVaultClient:
    def __init__(
        self,
        secrets_endpoint: str,
        credential: TokenCredential | None = None,
        scopes: Iterable[str] | None = None,
    ) -> None:
        self.secrets_endpoint = secrets_endpoint.rstrip("/")
        self.credential = credential or DefaultCloudShellCredential()
        self.scopes = list(scopes or [DEFAULT_SCOPE])

    @classmethod
    def from_environment(
        cls,
        vault_name: str | None = None,
        credential: TokenCredential | None = None,
    ) -> "SecretsVaultClient":
        endpoint = find_endpoint("CLOUDSHELL_SECRETS_", vault_name)
        if not endpoint:
            raise RuntimeError("No CloudShell Secrets Vault endpoint was found in the environment.")
        return cls(endpoint, credential)

    def get_secrets(self) -> list[SecretProperties]:
        values = get_json(self.secrets_endpoint, self.credential, self.scopes)
        return [
            SecretProperties(str(item.get("name", "")), optional_string(item.get("version")))
            for item in values
        ]

    def get_secret(self, name: str, version: str | None = None) -> SecretValue | None:
        if not name.strip():
            raise ValueError("Secret name is required.")
        endpoint = f"{self.secrets_endpoint}/{quote(name, safe='')}"
        if version and version.strip():
            endpoint = f"{endpoint}?{urlencode({'version': version.strip()})}"
        value = get_json(endpoint, self.credential, self.scopes, allow_not_found=True)
        if value is None:
            return None
        return SecretValue(
            str(value.get("name", "")),
            str(value.get("value", "")),
            optional_string(value.get("version")))


def optional_string(value: object) -> str | None:
    if value is None:
        return None
    text = str(value)
    return text if text else None

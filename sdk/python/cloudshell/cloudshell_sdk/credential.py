from __future__ import annotations

import json
import os
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable
from urllib.parse import urlencode
from urllib.request import Request, urlopen


DEFAULT_SCOPE = "ControlPlane.Access"

IDENTITY_TOKEN_ENDPOINT_ENV = "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT"
IDENTITY_CLIENT_ID_ENV = "CLOUDSHELL_IDENTITY_CLIENT_ID"
IDENTITY_CLIENT_SECRET_ENV = "CLOUDSHELL_IDENTITY_CLIENT_SECRET"
IDENTITY_SCOPE_ENV = "CLOUDSHELL_IDENTITY_SCOPE"
CONFIG_DIRECTORY_ENV = "CLOUDSHELL_CONFIG_DIR"
PROFILE_ENV = "CLOUDSHELL_PROFILE"


class CloudShellCredentialUnavailable(Exception):
    pass


@dataclass(frozen=True)
class AccessToken:
    token: str
    expires_on: float | None = None


class TokenCredential:
    def get_token(self, scopes: Iterable[str] | None = None) -> AccessToken:
        raise NotImplementedError


class DefaultCloudShellCredential(TokenCredential):
    def __init__(self, credentials: Iterable[TokenCredential] | None = None) -> None:
        self._credentials = list(credentials) if credentials is not None else [
            IdentityCredential(),
            EnvironmentTokenCredential(),
            ProfileCredential(),
        ]

    def get_token(self, scopes: Iterable[str] | None = None) -> AccessToken:
        errors: list[str] = []
        for credential in self._credentials:
            try:
                token = credential.get_token(scopes)
            except CloudShellCredentialUnavailable as exception:
                errors.append(str(exception))
                continue
            if token.token.strip():
                return AccessToken(token.token.strip(), token.expires_on)

        detail = "; ".join(error for error in errors if error)
        raise CloudShellCredentialUnavailable(
            "No CloudShell credential could provide a token." +
            (f" {detail}" if detail else ""))


class StaticTokenCredential(TokenCredential):
    def __init__(self, token: str) -> None:
        self._token = token

    def get_token(self, scopes: Iterable[str] | None = None) -> AccessToken:
        token = self._token.strip()
        if not token:
            raise CloudShellCredentialUnavailable("Static token is empty.")
        return AccessToken(token)


class EnvironmentTokenCredential(TokenCredential):
    def __init__(
        self,
        variable_names: Iterable[str] | None = None,
        environment: dict[str, str] | None = None,
    ) -> None:
        self._variable_names = list(variable_names) if variable_names is not None else [
            "CLOUDSHELL_CONFIGURATION_TOKEN",
            "CLOUDSHELL_SECRETS_TOKEN",
            "CLOUDSHELL_CONTROL_PLANE_TOKEN",
            "CLOUDSHELL_TOKEN",
        ]
        self._environment = environment

    def get_token(self, scopes: Iterable[str] | None = None) -> AccessToken:
        environment = os.environ if self._environment is None else self._environment
        for variable_name in self._variable_names:
            token = environment.get(variable_name, "").strip()
            if token:
                return AccessToken(token)
        raise CloudShellCredentialUnavailable("No CloudShell token environment variable was set.")


class IdentityCredential(TokenCredential):
    def __init__(
        self,
        *,
        token_endpoint: str | None = None,
        client_id: str | None = None,
        client_secret: str | None = None,
        scope: str | None = None,
        environment: dict[str, str] | None = None,
    ) -> None:
        self._token_endpoint = token_endpoint
        self._client_id = client_id
        self._client_secret = client_secret
        self._scope = scope
        self._environment = environment
        self._cached_token: AccessToken | None = None

    def get_token(self, scopes: Iterable[str] | None = None) -> AccessToken:
        if self._cached_token and self._cached_token.expires_on:
            if self._cached_token.expires_on > time.time() + 60:
                return self._cached_token

        environment = os.environ if self._environment is None else self._environment
        token_endpoint = first_non_empty(
            self._token_endpoint,
            environment.get(IDENTITY_TOKEN_ENDPOINT_ENV))
        client_id = first_non_empty(self._client_id, environment.get(IDENTITY_CLIENT_ID_ENV))
        client_secret = first_non_empty(
            self._client_secret,
            environment.get(IDENTITY_CLIENT_SECRET_ENV))
        if not token_endpoint or not client_id or not client_secret:
            raise CloudShellCredentialUnavailable("CloudShell identity environment is incomplete.")

        body = urlencode({
            "grant_type": "client_credentials",
            "client_id": client_id,
            "client_secret": client_secret,
            "scope": self._resolve_scope(scopes, environment),
        }).encode("utf-8")
        request = Request(
            token_endpoint,
            data=body,
            method="POST",
            headers={"content-type": "application/x-www-form-urlencoded"})

        with urlopen(request, timeout=10) as response:
            payload = json.loads(response.read().decode("utf-8"))

        access_token = str(payload.get("access_token", "")).strip()
        if not access_token:
            raise RuntimeError("CloudShell identity token endpoint returned no access token.")

        expires_in = int(payload.get("expires_in", 0) or 0)
        token = AccessToken(access_token, time.time() + expires_in if expires_in > 0 else None)
        self._cached_token = token
        return token

    def _resolve_scope(
        self,
        scopes: Iterable[str] | None,
        environment: dict[str, str],
    ) -> str:
        values = [scope for scope in (scopes or []) if str(scope).strip()]
        if values:
            return " ".join(values)
        return first_non_empty(self._scope, environment.get(IDENTITY_SCOPE_ENV), DEFAULT_SCOPE) or DEFAULT_SCOPE


class ProfileCredential(TokenCredential):
    def __init__(
        self,
        *,
        config_directory: str | Path | None = None,
        config_path: str | Path | None = None,
        profile_name: str | None = None,
        environment: dict[str, str] | None = None,
    ) -> None:
        self._config_directory = Path(config_directory) if config_directory is not None else None
        self._config_path = Path(config_path) if config_path is not None else None
        self._profile_name = profile_name
        self._environment = environment

    def get_token(self, scopes: Iterable[str] | None = None) -> AccessToken:
        config_path = self._resolve_config_path()
        if not config_path.exists():
            raise CloudShellCredentialUnavailable("CloudShell profile config was not found.")

        configuration = json.loads(config_path.read_text(encoding="utf-8"))
        environment = os.environ if self._environment is None else self._environment
        profile_name = first_non_empty(
            self._profile_name,
            environment.get(PROFILE_ENV),
            configuration.get("activeProfile"))
        if not profile_name:
            raise CloudShellCredentialUnavailable("No active CloudShell profile was selected.")

        profiles = configuration.get("profiles") or {}
        profile = find_profile(profiles, profile_name)
        credential = (profile or {}).get("credential") or {}
        if str(credential.get("kind", "")).lower() != "staticbearer":
            raise CloudShellCredentialUnavailable("CloudShell profile credential is not staticBearer.")

        expires_on = parse_expires_on(credential.get("expiresOn"))
        if expires_on is not None and expires_on <= time.time():
            raise CloudShellCredentialUnavailable("CloudShell profile credential is expired.")

        token = str(credential.get("accessToken", "")).strip()
        if not token and credential.get("accessTokenPath"):
            token_path = Path(str(credential["accessTokenPath"]))
            if not token_path.is_absolute():
                token_path = config_path.parent / token_path
            if not token_path.exists():
                raise CloudShellCredentialUnavailable("CloudShell profile token file was not found.")
            token = token_path.read_text(encoding="utf-8").strip()

        if not token:
            raise CloudShellCredentialUnavailable("CloudShell profile credential has no token.")
        return AccessToken(token, expires_on)

    def _resolve_config_path(self) -> Path:
        if self._config_path is not None:
            return self._config_path
        return self._resolve_config_directory() / "config.json"

    def _resolve_config_directory(self) -> Path:
        if self._config_directory is not None:
            return self._config_directory
        environment = os.environ if self._environment is None else self._environment
        configured = environment.get(CONFIG_DIRECTORY_ENV, "").strip()
        return Path(configured) if configured else Path.home() / ".cloudshell"


def first_non_empty(*values: str | None) -> str | None:
    for value in values:
        if value and value.strip():
            return value.strip()
    return None


def find_profile(profiles: dict[str, object], profile_name: str) -> dict[str, object] | None:
    if profile_name in profiles and isinstance(profiles[profile_name], dict):
        return profiles[profile_name]  # type: ignore[return-value]
    for name, profile in profiles.items():
        if name.lower() == profile_name.lower() and isinstance(profile, dict):
            return profile  # type: ignore[return-value]
    return None


def parse_expires_on(value: object) -> float | None:
    if not value:
        return None
    text = str(value).strip()
    if not text:
        return None
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    return datetime.fromisoformat(text).astimezone(timezone.utc).timestamp()

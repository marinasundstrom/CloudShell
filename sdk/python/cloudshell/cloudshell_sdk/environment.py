from __future__ import annotations

import os
import re
from urllib.parse import urlparse


def find_endpoint(
    prefix: str,
    service_name: str | None = None,
    environment: dict[str, str] | None = None,
) -> str | None:
    values = dict(os.environ if environment is None else environment)
    normalized_service_name = normalize_environment_segment(service_name or "")
    candidates: list[str] = []

    for name, value in values.items():
        key = name.upper()
        if not value or not key.startswith(prefix) or not key.endswith("_ENDPOINT"):
            continue
        if normalized_service_name and f"{prefix}{normalized_service_name}_" not in key:
            continue
        parsed = urlparse(value)
        if parsed.scheme and parsed.netloc:
            candidates.append(name)

    if not candidates:
        return None

    return values[sorted(candidates, key=str.casefold)[0]]


def normalize_environment_segment(value: str) -> str:
    return re.sub(r"_+", "_", re.sub(r"[^A-Za-z0-9]", "_", value.strip()).upper()).strip("_")

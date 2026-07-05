from __future__ import annotations

import json
from typing import Iterable
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from .credential import TokenCredential


def get_json(
    endpoint: str,
    credential: TokenCredential,
    scopes: Iterable[str],
    *,
    allow_not_found: bool = False,
):
    token = credential.get_token(scopes)
    request = Request(endpoint, method="GET")
    request.add_header("authorization", f"Bearer {token.token}")
    try:
        with urlopen(request, timeout=10) as response:
            body = response.read().decode("utf-8")
    except HTTPError as exception:
        if allow_not_found and exception.code == 404:
            return None
        detail = exception.read().decode("utf-8")
        raise RuntimeError(
            f"CloudShell service returned {exception.code}." +
            (f" {detail}" if detail else "")) from exception
    return json.loads(body)

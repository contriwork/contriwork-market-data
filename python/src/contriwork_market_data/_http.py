"""Shared HTTP plumbing for REST-based adapters.

Provides a tiny indirection over ``httpx`` so per-adapter code stays focused
on endpoint shape and response parsing. The orchestrator handles retry,
rate limiting, and caching above this layer.
"""

from __future__ import annotations

import json
from typing import Any

import httpx

from .errors import (
    AdapterUnavailableError,
    RateLimitedError,
)

__all__ = ["build_async_client", "request_json", "translate_http_error"]


def build_async_client(*, timeout_s: float, http_proxy: str | None = None) -> httpx.AsyncClient:
    """Create an ``httpx.AsyncClient`` with shared defaults.

    Adapters may inject their own client (for testing or shared connection
    pooling); when they don't, this builder produces a sane default.
    """
    return httpx.AsyncClient(
        timeout=httpx.Timeout(timeout_s),
        proxy=http_proxy,
        follow_redirects=False,
    )


def translate_http_error(
    *,
    adapter_id: str,
    exc: Exception,
) -> AdapterUnavailableError:
    """Map a low-level network exception into ``AdapterUnavailableError``."""
    return AdapterUnavailableError(
        f"adapter {adapter_id} network error: {exc.__class__.__name__}: {exc}",
        adapter_id=adapter_id,
    )


async def request_json(
    client: httpx.AsyncClient,
    method: str,
    url: str,
    *,
    adapter_id: str,
    headers: dict[str, str] | None = None,
    params: dict[str, Any] | None = None,
    json_body: dict[str, Any] | None = None,
) -> Any:
    """Issue an HTTP request and return parsed JSON.

    Maps low-level networking issues to ``AdapterUnavailableError`` and
    treats HTTP 429 as ``RateLimitedError`` so the orchestrator's retry
    runner can react. All other 4xx/5xx codes are surfaced via
    ``AdapterUnavailableError`` — adapter-specific logic that needs to
    inspect the response body should call the lower-level
    :func:`httpx.AsyncClient.request` directly.
    """
    try:
        response = await client.request(
            method,
            url,
            headers=headers,
            params=params,
            json=json_body,
        )
    except httpx.TimeoutException as exc:
        raise AdapterUnavailableError(
            f"adapter {adapter_id} timed out: {exc}",
            adapter_id=adapter_id,
        ) from exc
    except httpx.RequestError as exc:
        raise translate_http_error(adapter_id=adapter_id, exc=exc) from exc

    if response.status_code == 429:
        raise RateLimitedError(
            f"adapter {adapter_id} returned HTTP 429",
            adapter_id=adapter_id,
        )
    if response.status_code >= 400:
        raise AdapterUnavailableError(
            f"adapter {adapter_id} returned HTTP {response.status_code}",
            adapter_id=adapter_id,
        )
    try:
        return response.json()
    except json.JSONDecodeError as exc:
        raise AdapterUnavailableError(
            f"adapter {adapter_id} returned non-JSON body",
            adapter_id=adapter_id,
        ) from exc

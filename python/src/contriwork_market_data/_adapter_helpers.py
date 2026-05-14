"""Shared adapter-side helpers.

Internal — adapter authors import from here to keep boilerplate minimal.
"""

from __future__ import annotations

from collections.abc import AsyncIterator, Awaitable, Callable

from .errors import AdapterFeatureNotSupportedError, MissingCredentialsError
from .types import Ticker

__all__ = [
    "resolve_api_key",
    "streaming_not_supported",
]


async def resolve_api_key(
    *,
    adapter_id: str,
    api_key: str | None,
    api_key_provider: Callable[[], Awaitable[str | None]] | None,
    required: bool,
) -> str | None:
    """Lazy-resolve credentials. CONTRACT.md §6 (SCOPE.md §6).

    If both ``api_key`` and ``api_key_provider`` are set, the provider wins
    so callers can rotate keys at runtime (e.g. DB-backed stores). When
    ``required`` is True and neither source yields a non-empty value,
    raise ``MissingCredentialsError`` lazily on first call.
    """
    resolved: str | None
    if api_key_provider is not None:
        resolved = await api_key_provider()
    else:
        resolved = api_key
    if required and not resolved:
        raise MissingCredentialsError(
            f"adapter {adapter_id} requires authentication but no api_key or "
            "api_key_provider resolved a usable value",
            adapter_id=adapter_id,
        )
    return resolved


def streaming_not_supported(adapter_id: str) -> AsyncIterator[Ticker]:
    """Return an async iterator that raises on the first iteration.

    Used by adapters whose ``Capability.supports_native_streaming`` is
    False to satisfy the :class:`Adapter` protocol. The orchestrator never
    invokes this when polling fallback is active; this path exists so
    callers using the adapter directly fail fast with a clear error.
    """

    async def _gen() -> AsyncIterator[Ticker]:
        # An ``async for`` over an empty tuple makes this function an async
        # generator at type-check time without producing any value. The
        # raise below always fires on the first ``__anext__`` call.
        for _ in ():  # pragma: no cover - never iterates
            yield  # type: ignore[misc]
        raise AdapterFeatureNotSupportedError(
            f"adapter {adapter_id} does not implement native streaming; "
            "use polling fallback via MarketDataClient.subscribe_ticker",
            adapter_id=adapter_id,
        )

    return _gen()

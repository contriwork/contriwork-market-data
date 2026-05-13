"""Polling emulation helpers — CONTRACT.md §3.4 fallback path.

When an adapter has ``Capability.supports_native_streaming = False`` and the
caller asked for ``polling_fallback = True``, the orchestrator builds a
synthetic ticker stream by re-invoking ``get_spot`` on a fixed cadence.

Cancellation: callers cancel by closing the async generator (``aclose()`` or
exiting the ``async for`` loop). The internal sleep yields control on each
tick so cancellation propagates promptly.
"""

from __future__ import annotations

from collections.abc import AsyncIterator

from ._adapter import Adapter
from ._clock import Clock
from .errors import MarketDataError, StreamDisconnectedError
from .types import Ticker

__all__ = ["polling_ticker_iterator"]


async def polling_ticker_iterator(
    adapter: Adapter,
    *,
    symbol: str,
    quote_currency: str,
    polling_interval_s: float,
    clock: Clock,
    max_consecutive_failures: int = 3,
) -> AsyncIterator[Ticker]:
    """Yield a :class:`Ticker` every ``polling_interval_s`` by calling
    ``adapter.get_spot``. After ``max_consecutive_failures`` back-to-back
    failures, raise :class:`StreamDisconnectedError`.
    """
    failures = 0
    while True:
        try:
            spot = await adapter.get_spot(symbol, quote_currency)
        except MarketDataError as exc:
            failures += 1
            if failures >= max_consecutive_failures:
                raise StreamDisconnectedError(
                    (
                        f"polling emulation exhausted after {failures} "
                        f"consecutive failures (last code={exc.code})"
                    ),
                    adapter_id=adapter.adapter_id,
                ) from exc
            await clock.sleep(polling_interval_s)
            continue
        failures = 0
        yield Ticker(
            symbol=spot.symbol,
            price=spot.last,
            quote_currency=spot.quote_currency,
            timestamp=spot.timestamp,
            source_adapter=spot.source_adapter,
        )
        await clock.sleep(polling_interval_s)

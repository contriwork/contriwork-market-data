"""Adapter protocol — INTERNAL. Adapter authors implement this.

CONTRACT.md §4. Every adapter (CoinGecko, Binance, …) must implement these
four operations plus ``capability`` and ``adapter_id``. The orchestrator
(:class:`~contriwork_market_data.client.MarketDataClient`) wraps adapter
calls with cache, rate limiting, and chain fallback so adapters can stay
narrow and provider-focused.

Adapter conformance is structural (``Protocol``), so test fakes and stub
adapters do not need to subclass anything — they just need matching
attributes and method signatures.
"""

from __future__ import annotations

from collections.abc import AsyncIterator
from datetime import datetime
from typing import Protocol, runtime_checkable

from .types import Candle, Capability, Interval, OrderBook, SpotPrice, Ticker

__all__ = ["Adapter"]


@runtime_checkable
class Adapter(Protocol):
    adapter_id: str
    capability: Capability

    async def get_spot(self, symbol: str, quote_currency: str) -> SpotPrice: ...

    async def get_ohlcv(
        self,
        symbol: str,
        interval: Interval,
        since: datetime | None,
        limit: int,
    ) -> list[Candle]: ...

    async def get_order_book(self, symbol: str, depth: int) -> OrderBook: ...

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]: ...

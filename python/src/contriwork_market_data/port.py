"""Public port — mirror of CONTRACT.md §2.

This is the consumer-facing surface. The concrete production implementation
is :class:`~contriwork_market_data.client.MarketDataClient`; tests may
satisfy this protocol with their own fakes. Method names align with C#
(``PascalCaseAsync``) and TypeScript (``camelCase``) implementations.
"""

from __future__ import annotations

from collections.abc import AsyncIterator
from datetime import datetime
from typing import Protocol, runtime_checkable

from .types import Candle, Interval, OrderBook, SpotPrice, Ticker

__all__ = ["MarketDataPort"]


@runtime_checkable
class MarketDataPort(Protocol):
    async def get_spot(
        self,
        symbol: str,
        market: str,
        quote_currency: str = "USD",
    ) -> SpotPrice: ...

    async def get_ohlcv(
        self,
        symbol: str,
        market: str,
        interval: Interval,
        since: datetime | None = None,
        limit: int = 100,
    ) -> list[Candle]: ...

    async def get_order_book(
        self,
        symbol: str,
        market: str,
        depth: int = 20,
    ) -> OrderBook: ...

    def subscribe_ticker(
        self,
        symbol: str,
        market: str,
        polling_fallback: bool = True,
        polling_interval_s: float = 4.0,
    ) -> AsyncIterator[Ticker]: ...

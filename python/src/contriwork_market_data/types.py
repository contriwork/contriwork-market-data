"""Public data types — mirror of CONTRACT.md §5.

All types are frozen dataclasses with provider-specific extension via the
``extra`` MappingProxyType. Numeric fields are ``Decimal`` to avoid float
precision loss across providers and across the wire. ``timestamp`` fields are
timezone-aware UTC ``datetime``.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass, field
from datetime import datetime
from decimal import Decimal
from enum import Enum
from types import MappingProxyType
from typing import Any, Literal

__all__ = [
    "BookLevel",
    "Candle",
    "Capability",
    "Interval",
    "OrderBook",
    "SpotPrice",
    "Ticker",
    "TickerSide",
]

_EMPTY_EXTRA: Mapping[str, Any] = MappingProxyType({})


class Interval(Enum):
    """Time interval for OHLCV candles. CONTRACT v1 §5.5 — names invariant."""

    M1 = "M1"
    M5 = "M5"
    M15 = "M15"
    M30 = "M30"
    H1 = "H1"
    H4 = "H4"
    D1 = "D1"
    W1 = "W1"
    MN1 = "MN1"


def _freeze(extra: Mapping[str, Any] | None) -> Mapping[str, Any]:
    """Return an immutable view of ``extra``; reuse the shared empty singleton
    when no caller-supplied data is present to keep allocations cheap."""
    if extra is None or len(extra) == 0:
        return _EMPTY_EXTRA
    return MappingProxyType(dict(extra))


@dataclass(frozen=True, slots=True)
class SpotPrice:
    symbol: str
    last: Decimal
    quote_currency: str
    timestamp: datetime
    source_adapter: str
    bid: Decimal | None = None
    ask: Decimal | None = None
    high_24h: Decimal | None = None
    low_24h: Decimal | None = None
    volume_24h: Decimal | None = None
    change_24h_pct: Decimal | None = None
    market_cap: Decimal | None = None
    previous_close: Decimal | None = None
    extra: Mapping[str, Any] = field(default_factory=lambda: _EMPTY_EXTRA)


@dataclass(frozen=True, slots=True)
class Candle:
    timestamp: datetime
    open: Decimal
    high: Decimal
    low: Decimal
    close: Decimal
    volume: Decimal
    quote_volume: Decimal | None = None
    trade_count: int | None = None
    extra: Mapping[str, Any] = field(default_factory=lambda: _EMPTY_EXTRA)


@dataclass(frozen=True, slots=True)
class BookLevel:
    price: Decimal
    size: Decimal


@dataclass(frozen=True, slots=True)
class OrderBook:
    symbol: str
    bids: tuple[BookLevel, ...]
    asks: tuple[BookLevel, ...]
    timestamp: datetime
    source_adapter: str
    sequence: int | None = None
    extra: Mapping[str, Any] = field(default_factory=lambda: _EMPTY_EXTRA)


TickerSide = Literal["bid", "ask", "trade"]


@dataclass(frozen=True, slots=True)
class Ticker:
    symbol: str
    price: Decimal
    quote_currency: str
    timestamp: datetime
    source_adapter: str
    side: TickerSide | None = None
    size: Decimal | None = None
    bid: Decimal | None = None
    ask: Decimal | None = None
    extra: Mapping[str, Any] = field(default_factory=lambda: _EMPTY_EXTRA)


@dataclass(frozen=True, slots=True)
class Capability:
    """Static description of what an adapter supports. See CONTRACT.md §5.7."""

    supported_markets: tuple[str, ...]
    supported_intervals: tuple[Interval, ...]
    supported_quote_currencies: tuple[str, ...] | Literal["ANY"]
    supports_order_book: bool
    supports_native_streaming: bool
    rate_limit_per_minute: int
    requires_auth: bool
    tier_options: tuple[str, ...] = ()

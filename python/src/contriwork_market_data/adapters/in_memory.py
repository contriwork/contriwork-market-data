"""InMemoryAdapter — test fixture + reference implementation.

Drives contract-test fixtures (``contract-tests/test_cases.json``) and
also serves as the canonical example for how to implement the adapter
protocol against a real provider.

Symbol-level dataset shape::

    {
      "BTCUSDT": {
        "spot": {"last": "65000", "quote_currency": "USDT", ...},
        "ohlcv": {"M1": [{"timestamp": "...", "open": "...", ...}, ...]},
        "order_book": {
          "bids": [["price", "size"], ...],
          "asks": [["price", "size"], ...]
        },
        "ticker_stream": [{"price": "...", "timestamp": "..."}, ...]
      }
    }

Numeric values may be provided as strings (preferred — exact ``Decimal``)
or numbers. Timestamps must be ISO-8601 strings with explicit timezone.
"""

from __future__ import annotations

from collections.abc import AsyncIterator, Awaitable, Callable
from dataclasses import dataclass, field
from datetime import UTC, datetime
from decimal import Decimal
from typing import Any, Literal

from .._clock import Clock, SystemClock
from ..errors import (
    AdapterFeatureNotSupportedError,
    InvalidIntervalError,
    MissingCredentialsError,
    SymbolNotFoundError,
    UnsupportedQuoteCurrencyError,
    error_for_code,
)
from ..types import (
    BookLevel,
    Candle,
    Capability,
    Interval,
    OrderBook,
    SpotPrice,
    Ticker,
)

__all__ = ["InMemoryAdapter", "InMemoryFailMode"]


_ALL_INTERVALS: tuple[Interval, ...] = tuple(Interval)


@dataclass
class InMemoryFailMode:
    """Force a specific error code for operations on ``symbol``.

    ``fail_first_n`` makes the adapter raise only on the first N calls and
    succeed after that — useful for retry-then-success test cases.
    """

    symbol: str
    code: str
    fail_first_n: int | None = None
    _remaining: int = field(init=False, default=0)

    def __post_init__(self) -> None:
        # ``KeyError`` here surfaces test mis-spellings immediately.
        error_for_code(self.code)
        self._remaining = self.fail_first_n if self.fail_first_n is not None else -1

    def consume(self) -> bool:
        if self._remaining == 0:
            return False
        if self._remaining > 0:
            self._remaining -= 1
        return True


def _to_decimal(value: Any) -> Decimal:
    if isinstance(value, Decimal):
        return value
    if isinstance(value, str):
        return Decimal(value)
    if isinstance(value, (int, float)):
        return Decimal(str(value))
    raise TypeError(f"cannot coerce {type(value).__name__} to Decimal: {value!r}")


def _to_optional_decimal(value: Any) -> Decimal | None:
    if value is None:
        return None
    return _to_decimal(value)


def _to_datetime(value: Any) -> datetime:
    if isinstance(value, datetime):
        return value if value.tzinfo else value.replace(tzinfo=UTC)
    if isinstance(value, str):
        text = value.rstrip("Z")
        suffix = "+00:00" if value.endswith("Z") else ""
        return datetime.fromisoformat(text + suffix)
    raise TypeError(f"cannot coerce {type(value).__name__} to datetime: {value!r}")


class InMemoryAdapter:
    """Adapter backed by pre-seeded in-memory data."""

    def __init__(
        self,
        *,
        adapter_id: str,
        data: dict[str, dict[str, Any]] | None = None,
        capability: Capability | None = None,
        fail_modes: list[InMemoryFailMode] | None = None,
        api_key: str | None = None,
        api_key_provider: Callable[[], Awaitable[str | None]] | None = None,
        clock: Clock | None = None,
    ) -> None:
        if not adapter_id:
            raise ValueError("adapter_id must be a non-empty string")
        self.adapter_id = adapter_id
        self._data = data or {}
        self._fail_modes: dict[tuple[str, str], InMemoryFailMode] = {
            (fm.symbol, fm.code): fm for fm in (fail_modes or [])
        }
        self._api_key = api_key
        self._api_key_provider = api_key_provider
        self._clock = clock or SystemClock()
        self._call_counts: dict[str, int] = {}
        if capability is not None:
            self.capability = capability
        else:
            self.capability = Capability(
                supported_markets=("*",),
                supported_intervals=_ALL_INTERVALS,
                supported_quote_currencies="ANY",
                supports_order_book=True,
                supports_native_streaming=False,
                rate_limit_per_minute=9999,
                requires_auth=False,
            )

    # ---- introspection helpers (test-only) -------------------------------

    @property
    def call_counts(self) -> dict[str, int]:
        return dict(self._call_counts)

    # ---- adapter operations ----------------------------------------------

    async def get_spot(self, symbol: str, quote_currency: str) -> SpotPrice:
        await self._gate("spot", symbol, quote_currency=quote_currency)
        record = self._symbol(symbol).get("spot")
        if record is None:
            raise SymbolNotFoundError(
                f"adapter {self.adapter_id} has no spot for symbol {symbol!r}",
                adapter_id=self.adapter_id,
            )
        return SpotPrice(
            symbol=symbol,
            last=_to_decimal(record["last"]),
            quote_currency=record.get("quote_currency", quote_currency),
            timestamp=_to_datetime(record.get("timestamp", self._clock.now())),
            source_adapter=self.adapter_id,
            bid=_to_optional_decimal(record.get("bid")),
            ask=_to_optional_decimal(record.get("ask")),
            high_24h=_to_optional_decimal(record.get("high_24h")),
            low_24h=_to_optional_decimal(record.get("low_24h")),
            volume_24h=_to_optional_decimal(record.get("volume_24h")),
            change_24h_pct=_to_optional_decimal(record.get("change_24h_pct")),
            market_cap=_to_optional_decimal(record.get("market_cap")),
            previous_close=_to_optional_decimal(record.get("previous_close")),
        )

    async def get_ohlcv(
        self,
        symbol: str,
        interval: Interval,
        since: datetime | None,
        limit: int,
    ) -> list[Candle]:
        await self._gate("ohlcv", symbol)
        if interval not in self.capability.supported_intervals:
            raise InvalidIntervalError(
                f"adapter {self.adapter_id} does not support interval {interval.value}",
                adapter_id=self.adapter_id,
            )
        ohlcv = self._symbol(symbol).get("ohlcv", {})
        candles_raw = ohlcv.get(interval.value, [])
        if not candles_raw:
            raise SymbolNotFoundError(
                f"adapter {self.adapter_id} has no ohlcv for {symbol!r}/{interval.value}",
                adapter_id=self.adapter_id,
            )
        candles: list[Candle] = []
        for c in candles_raw:
            ts = _to_datetime(c["timestamp"])
            if since is not None and ts < since:
                continue
            candles.append(
                Candle(
                    timestamp=ts,
                    open=_to_decimal(c["open"]),
                    high=_to_decimal(c["high"]),
                    low=_to_decimal(c["low"]),
                    close=_to_decimal(c["close"]),
                    volume=_to_decimal(c["volume"]),
                    quote_volume=_to_optional_decimal(c.get("quote_volume")),
                    trade_count=c.get("trade_count"),
                )
            )
            if len(candles) >= limit:
                break
        candles.sort(key=lambda x: x.timestamp)
        return candles

    async def get_order_book(self, symbol: str, depth: int) -> OrderBook:
        await self._gate("order_book", symbol)
        if not self.capability.supports_order_book:
            raise AdapterFeatureNotSupportedError(
                f"adapter {self.adapter_id} does not support order book",
                adapter_id=self.adapter_id,
            )
        book = self._symbol(symbol).get("order_book")
        if book is None:
            raise SymbolNotFoundError(
                f"adapter {self.adapter_id} has no order book for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        bids = tuple(
            BookLevel(price=_to_decimal(p), size=_to_decimal(s))
            for p, s in book.get("bids", [])[:depth]
        )
        asks = tuple(
            BookLevel(price=_to_decimal(p), size=_to_decimal(s))
            for p, s in book.get("asks", [])[:depth]
        )
        bids_sorted = tuple(sorted(bids, key=lambda level: level.price, reverse=True))
        asks_sorted = tuple(sorted(asks, key=lambda level: level.price))
        return OrderBook(
            symbol=symbol,
            bids=bids_sorted,
            asks=asks_sorted,
            timestamp=self._clock.now(),
            source_adapter=self.adapter_id,
        )

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]:
        # Native streaming. Yields the pre-seeded ``ticker_stream`` events
        # one by one. Returning an async-generator directly (vs. ``async
        # def`` here) lets callers ``async for`` without an intermediate
        # ``await``.
        return self._native_stream(symbol)

    async def _native_stream(self, symbol: str) -> AsyncIterator[Ticker]:
        await self._gate("ticker", symbol)
        events = self._symbol(symbol).get("ticker_stream", [])
        for event in events:
            yield Ticker(
                symbol=symbol,
                price=_to_decimal(event["price"]),
                quote_currency=event.get("quote_currency", "USD"),
                timestamp=_to_datetime(event["timestamp"]),
                source_adapter=self.adapter_id,
                side=event.get("side"),
                size=_to_optional_decimal(event.get("size")),
                bid=_to_optional_decimal(event.get("bid")),
                ask=_to_optional_decimal(event.get("ask")),
            )

    # ---- gating ----------------------------------------------------------

    async def _gate(
        self,
        op: Literal["spot", "ohlcv", "order_book", "ticker"],
        symbol: str,
        *,
        quote_currency: str | None = None,
    ) -> None:
        """Run pre-op checks: auth + fail-modes; bump call counter."""
        self._call_counts[op] = self._call_counts.get(op, 0) + 1
        if self.capability.requires_auth:
            key = await self._resolve_api_key()
            if not key:
                raise MissingCredentialsError(
                    f"adapter {self.adapter_id} requires authentication but no "
                    "api_key or api_key_provider resolved a usable value",
                    adapter_id=self.adapter_id,
                )
        if (
            quote_currency is not None
            and self.capability.supported_quote_currencies != "ANY"
            and quote_currency not in self.capability.supported_quote_currencies
        ):
            raise UnsupportedQuoteCurrencyError(
                f"adapter {self.adapter_id} does not support quote_currency {quote_currency!r}",
                adapter_id=self.adapter_id,
            )
        for fm in self._fail_modes.values():
            if fm.symbol != symbol:
                continue
            if not fm.consume():
                continue
            cls = error_for_code(fm.code)
            raise cls(
                f"adapter {self.adapter_id} forced {fm.code} on symbol {symbol!r}",
                adapter_id=self.adapter_id,
            )

    async def _resolve_api_key(self) -> str | None:
        if self._api_key_provider is not None:
            return await self._api_key_provider()
        return self._api_key

    def _symbol(self, symbol: str) -> dict[str, Any]:
        entry = self._data.get(symbol)
        if entry is None:
            raise SymbolNotFoundError(
                f"adapter {self.adapter_id} has no data for symbol {symbol!r}",
                adapter_id=self.adapter_id,
            )
        return entry

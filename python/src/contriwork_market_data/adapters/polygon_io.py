"""Polygon.io adapter — stocks_us + forex.

Endpoints used:
- ``/v2/last/trade/{ticker}`` — latest trade as spot.
- ``/v2/aggs/ticker/{ticker}/range/{multiplier}/{timespan}/{from}/{to}`` —
  historical aggregate bars.

Auth: ``apiKey`` query parameter. Free tier: 5 req/min, 2-year history.
"""

from __future__ import annotations

from collections.abc import AsyncIterator, Awaitable, Callable
from datetime import UTC, datetime
from decimal import Decimal
from typing import Any

import httpx

from .._adapter_helpers import resolve_api_key, streaming_not_supported
from .._http import build_async_client, request_json
from ..errors import (
    AdapterFeatureNotSupportedError,
    AdapterUnavailableError,
    InvalidIntervalError,
    SymbolNotFoundError,
)
from ..types import (
    Candle,
    Capability,
    Interval,
    OrderBook,
    SpotPrice,
    Ticker,
)

__all__ = ["PolygonIOAdapter"]


_DEFAULT_BASE = "https://api.polygon.io"
_RATE_LIMIT_PER_MINUTE = 5

# Polygon aggs use (multiplier, timespan). Map our Interval enum to (m, span).
_INTERVAL_MAP: dict[Interval, tuple[int, str]] = {
    Interval.M1: (1, "minute"),
    Interval.M5: (5, "minute"),
    Interval.M15: (15, "minute"),
    Interval.M30: (30, "minute"),
    Interval.H1: (1, "hour"),
    Interval.H4: (4, "hour"),
    Interval.D1: (1, "day"),
    Interval.W1: (1, "week"),
    Interval.MN1: (1, "month"),
}

_SUPPORTED_INTERVALS: tuple[Interval, ...] = tuple(_INTERVAL_MAP.keys())


class PolygonIOAdapter:
    adapter_id = "polygon-io"

    def __init__(
        self,
        *,
        api_key: str | None = None,
        api_key_provider: Callable[[], Awaitable[str | None]] | None = None,
        base_url: str | None = None,
        timeout_s: float = 15.0,
        http_proxy: str | None = None,
        http_client: httpx.AsyncClient | None = None,
    ) -> None:
        self._api_key = api_key
        self._api_key_provider = api_key_provider
        self._base_url = (base_url or _DEFAULT_BASE).rstrip("/")
        self._timeout_s = timeout_s
        self._http_proxy = http_proxy
        self._client = http_client
        self._owns_client = http_client is None
        self.capability = Capability(
            supported_markets=("stocks_us", "forex"),
            supported_intervals=_SUPPORTED_INTERVALS,
            supported_quote_currencies=("USD",),
            supports_order_book=False,
            supports_native_streaming=False,
            rate_limit_per_minute=_RATE_LIMIT_PER_MINUTE,
            requires_auth=True,
        )

    async def aclose(self) -> None:
        if self._owns_client and self._client is not None:
            await self._client.aclose()
            self._client = None

    async def _ensure_client(self) -> httpx.AsyncClient:
        if self._client is None:
            self._client = build_async_client(
                timeout_s=self._timeout_s,
                http_proxy=self._http_proxy,
            )
        return self._client

    async def _api_key_value(self) -> str:
        key = await resolve_api_key(
            adapter_id=self.adapter_id,
            api_key=self._api_key,
            api_key_provider=self._api_key_provider,
            required=True,
        )
        assert key is not None
        return key

    async def get_spot(self, symbol: str, quote_currency: str) -> SpotPrice:
        client = await self._ensure_client()
        key = await self._api_key_value()
        try:
            payload = await request_json(
                client,
                "GET",
                f"{self._base_url}/v2/last/trade/{symbol}",
                adapter_id=self.adapter_id,
                params={"apiKey": key},
            )
        except AdapterUnavailableError as exc:
            if "HTTP 404" in str(exc):
                raise SymbolNotFoundError(
                    f"polygon-io does not know ticker {symbol!r}",
                    adapter_id=self.adapter_id,
                ) from exc
            raise
        if not isinstance(payload, dict) or "results" not in payload:
            raise AdapterUnavailableError(
                "polygon-io trade returned unexpected payload",
                adapter_id=self.adapter_id,
            )
        results = payload["results"]
        price = _opt_decimal(results.get("p"))
        if price is None:
            raise SymbolNotFoundError(
                f"polygon-io returned no last trade for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        # Polygon timestamps are in nanoseconds.
        ts_ns = int(results.get("t", 0)) or 0
        return SpotPrice(
            symbol=symbol,
            last=price,
            quote_currency=quote_currency,
            timestamp=datetime.fromtimestamp(ts_ns / 1e9, tz=UTC),
            source_adapter=self.adapter_id,
            volume_24h=_opt_decimal(results.get("s")),
        )

    async def get_ohlcv(
        self,
        symbol: str,
        interval: Interval,
        since: datetime | None,
        limit: int,
    ) -> list[Candle]:
        if interval not in _SUPPORTED_INTERVALS:
            raise InvalidIntervalError(
                f"polygon-io does not support interval {interval.value}",
                adapter_id=self.adapter_id,
            )
        client = await self._ensure_client()
        key = await self._api_key_value()
        multiplier, span = _INTERVAL_MAP[interval]
        # Default lookback: 30 days when no since is supplied.
        end = datetime.now(UTC)
        start = since or end.replace(year=max(1970, end.year - 1))
        try:
            payload = await request_json(
                client,
                "GET",
                (
                    f"{self._base_url}/v2/aggs/ticker/{symbol}/range/"
                    f"{multiplier}/{span}/{start:%Y-%m-%d}/{end:%Y-%m-%d}"
                ),
                adapter_id=self.adapter_id,
                params={"apiKey": key, "limit": min(limit, 5000)},
            )
        except AdapterUnavailableError as exc:
            if "HTTP 404" in str(exc):
                raise SymbolNotFoundError(
                    f"polygon-io does not know ticker {symbol!r}",
                    adapter_id=self.adapter_id,
                ) from exc
            raise
        if not isinstance(payload, dict):
            raise AdapterUnavailableError(
                "polygon-io aggs returned unexpected payload",
                adapter_id=self.adapter_id,
            )
        results = payload.get("results") or []
        candles: list[Candle] = []
        for row in results[:limit]:
            ts_ms = int(row.get("t", 0))
            candles.append(
                Candle(
                    timestamp=datetime.fromtimestamp(ts_ms / 1000, tz=UTC),
                    open=Decimal(str(row["o"])),
                    high=Decimal(str(row["h"])),
                    low=Decimal(str(row["l"])),
                    close=Decimal(str(row["c"])),
                    volume=Decimal(str(row.get("v", 0))),
                    trade_count=row.get("n"),
                )
            )
        return candles

    async def get_order_book(self, symbol: str, depth: int) -> OrderBook:
        raise AdapterFeatureNotSupportedError(
            "polygon-io order book requires the L2 paid tier and is out of v0.1.0 scope",
            adapter_id=self.adapter_id,
        )

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]:
        return streaming_not_supported(self.adapter_id)


def _opt_decimal(value: Any) -> Decimal | None:
    if value is None or value == "":
        return None
    return Decimal(str(value))

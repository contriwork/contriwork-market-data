"""Kraken public-data adapter.

Endpoints used (v0 public API):
- ``/0/public/Ticker`` — spot price + 24h stats.
- ``/0/public/OHLC`` — historical candles by interval (minutes).
- ``/0/public/Depth`` — order book.

Symbols use Kraken pair notation (``"XXBTZUSD"``, ``"ETHUSDT"``). The
unauthenticated REST surface allows ~60 req/minute IP-wise.
"""

from __future__ import annotations

from collections.abc import AsyncIterator
from datetime import UTC, datetime
from decimal import Decimal
from typing import Any

import httpx

from .._adapter_helpers import streaming_not_supported
from .._http import build_async_client, request_json
from ..errors import (
    AdapterUnavailableError,
    InvalidIntervalError,
    SymbolNotFoundError,
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

__all__ = ["KrakenAdapter"]


_INTERVAL_MAP: dict[Interval, int] = {
    Interval.M1: 1,
    Interval.M5: 5,
    Interval.M15: 15,
    Interval.M30: 30,
    Interval.H1: 60,
    Interval.H4: 240,
    Interval.D1: 1440,
    Interval.W1: 10080,
    Interval.MN1: 21600,
}

_SUPPORTED_INTERVALS: tuple[Interval, ...] = tuple(_INTERVAL_MAP.keys())

_DEFAULT_BASE = "https://api.kraken.com"
_RATE_LIMIT_PER_MINUTE = 60


class KrakenAdapter:
    adapter_id = "kraken"

    def __init__(
        self,
        *,
        base_url: str | None = None,
        timeout_s: float = 15.0,
        http_proxy: str | None = None,
        http_client: httpx.AsyncClient | None = None,
    ) -> None:
        self._base_url = (base_url or _DEFAULT_BASE).rstrip("/")
        self._timeout_s = timeout_s
        self._http_proxy = http_proxy
        self._client = http_client
        self._owns_client = http_client is None
        self.capability = Capability(
            supported_markets=("crypto",),
            supported_intervals=_SUPPORTED_INTERVALS,
            supported_quote_currencies="ANY",
            supports_order_book=True,
            supports_native_streaming=False,
            rate_limit_per_minute=_RATE_LIMIT_PER_MINUTE,
            requires_auth=False,
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

    async def _check_errors(self, payload: object, symbol: str) -> dict[str, Any]:
        if not isinstance(payload, dict):
            raise AdapterUnavailableError(
                "kraken returned non-object payload",
                adapter_id=self.adapter_id,
            )
        errors = payload.get("error") or []
        if errors:
            # Unknown asset pair errors look like ``"EQuery:Unknown asset pair"``.
            joined = ";".join(str(e) for e in errors)
            if "Unknown asset pair" in joined or "Unknown asset" in joined:
                raise SymbolNotFoundError(
                    f"kraken does not know symbol {symbol!r}",
                    adapter_id=self.adapter_id,
                )
            raise AdapterUnavailableError(
                f"kraken error: {joined}",
                adapter_id=self.adapter_id,
            )
        result = payload.get("result")
        if not isinstance(result, dict):
            raise AdapterUnavailableError(
                "kraken returned no result block",
                adapter_id=self.adapter_id,
            )
        return result

    async def get_spot(self, symbol: str, quote_currency: str) -> SpotPrice:
        client = await self._ensure_client()
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/0/public/Ticker",
            adapter_id=self.adapter_id,
            params={"pair": symbol},
        )
        result = await self._check_errors(payload, symbol)
        # Kraken may rename the requested pair (e.g. ``XBTUSD`` -> ``XXBTZUSD``).
        if not result:
            raise SymbolNotFoundError(
                f"kraken returned empty result for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        body = next(iter(result.values()))
        return SpotPrice(
            symbol=symbol,
            last=Decimal(body["c"][0]),
            quote_currency=quote_currency,
            timestamp=datetime.now(UTC),
            source_adapter=self.adapter_id,
            bid=Decimal(body["b"][0]),
            ask=Decimal(body["a"][0]),
            high_24h=Decimal(body["h"][1]),
            low_24h=Decimal(body["l"][1]),
            volume_24h=Decimal(body["v"][1]),
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
                f"kraken does not support interval {interval.value}",
                adapter_id=self.adapter_id,
            )
        client = await self._ensure_client()
        params: dict[str, object] = {
            "pair": symbol,
            "interval": _INTERVAL_MAP[interval],
        }
        if since is not None:
            params["since"] = int(since.timestamp())
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/0/public/OHLC",
            adapter_id=self.adapter_id,
            params=params,
        )
        result = await self._check_errors(payload, symbol)
        # OHLC result shape:
        #   {"<pair>": [[time, open, high, low, close, vwap, volume, count], ...], "last": ts}
        rows: list[list[object]] = []
        for key, value in result.items():
            if key == "last":
                continue
            if isinstance(value, list):
                rows = value
                break
        if not rows:
            raise SymbolNotFoundError(
                f"kraken returned no candles for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        candles: list[Candle] = []
        for row in rows[:limit]:
            ts = datetime.fromtimestamp(int(str(row[0])), tz=UTC)
            candles.append(
                Candle(
                    timestamp=ts,
                    open=Decimal(str(row[1])),
                    high=Decimal(str(row[2])),
                    low=Decimal(str(row[3])),
                    close=Decimal(str(row[4])),
                    volume=Decimal(str(row[6])),
                    trade_count=int(str(row[7])),
                )
            )
        return candles

    async def get_order_book(self, symbol: str, depth: int) -> OrderBook:
        client = await self._ensure_client()
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/0/public/Depth",
            adapter_id=self.adapter_id,
            params={"pair": symbol, "count": min(depth, 500)},
        )
        result = await self._check_errors(payload, symbol)
        if not result:
            raise SymbolNotFoundError(
                f"kraken returned no depth for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        body = next(iter(result.values()))
        bids = tuple(
            BookLevel(price=Decimal(str(p)), size=Decimal(str(s)))
            for p, s, *_ in body.get("bids", [])[:depth]
        )
        asks = tuple(
            BookLevel(price=Decimal(str(p)), size=Decimal(str(s)))
            for p, s, *_ in body.get("asks", [])[:depth]
        )
        return OrderBook(
            symbol=symbol,
            bids=tuple(sorted(bids, key=lambda level: level.price, reverse=True)),
            asks=tuple(sorted(asks, key=lambda level: level.price)),
            timestamp=datetime.now(UTC),
            source_adapter=self.adapter_id,
        )

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]:
        return streaming_not_supported(self.adapter_id)

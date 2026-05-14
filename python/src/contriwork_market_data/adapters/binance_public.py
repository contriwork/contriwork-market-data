"""Binance public-data adapter — no API key required.

Endpoints used:
- ``/api/v3/ticker/24hr`` — spot + 24h stats (single or batch).
- ``/api/v3/klines`` — historical candles with explicit interval.
- ``/api/v3/depth`` — limit order book (top-N per side).

Symbols are pair strings (``"BTCUSDT"``, ``"ETHUSDT"``). The Binance
public REST surface is permissive (1200 req/min). Authenticated
trading endpoints live in the separate ``contriwork-exchange`` package.
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

__all__ = ["BinancePublicAdapter"]


_INTERVAL_MAP: dict[Interval, str] = {
    Interval.M1: "1m",
    Interval.M5: "5m",
    Interval.M15: "15m",
    Interval.M30: "30m",
    Interval.H1: "1h",
    Interval.H4: "4h",
    Interval.D1: "1d",
    Interval.W1: "1w",
    Interval.MN1: "1M",
}

_SUPPORTED_INTERVALS: tuple[Interval, ...] = tuple(_INTERVAL_MAP.keys())

_DEFAULT_BASE = "https://api.binance.com"

# Binance public REST: ~1200 weight/minute for ip; we conservatively model
# 1000 calls/minute since some endpoints cost more than 1 weight.
_RATE_LIMIT_PER_MINUTE = 1000


class BinancePublicAdapter:
    adapter_id = "binance-public"

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

    async def get_spot(self, symbol: str, quote_currency: str) -> SpotPrice:
        # ``quote_currency`` is informational here — Binance pairs already
        # bake the quote asset into the symbol (e.g. ``BTCUSDT``). The
        # caller is responsible for symbol/quote consistency.
        client = await self._ensure_client()
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/api/v3/ticker/24hr",
            adapter_id=self.adapter_id,
            params={"symbol": symbol},
        )
        if not isinstance(payload, dict):
            raise AdapterUnavailableError(
                "binance returned an unexpected payload type",
                adapter_id=self.adapter_id,
            )
        if "code" in payload:
            # Binance error envelope; -1121 = invalid symbol.
            if payload.get("code") == -1121:
                raise SymbolNotFoundError(
                    f"binance does not know symbol {symbol!r}",
                    adapter_id=self.adapter_id,
                )
            raise AdapterUnavailableError(
                f"binance error {payload.get('code')}: {payload.get('msg')}",
                adapter_id=self.adapter_id,
            )
        return SpotPrice(
            symbol=symbol,
            last=Decimal(str(payload["lastPrice"])),
            quote_currency=quote_currency,
            timestamp=datetime.fromtimestamp(int(payload.get("closeTime", 0)) / 1000, tz=UTC),
            source_adapter=self.adapter_id,
            bid=_opt_decimal(payload.get("bidPrice")),
            ask=_opt_decimal(payload.get("askPrice")),
            high_24h=_opt_decimal(payload.get("highPrice")),
            low_24h=_opt_decimal(payload.get("lowPrice")),
            volume_24h=_opt_decimal(payload.get("quoteVolume")),
            change_24h_pct=_opt_decimal(payload.get("priceChangePercent")),
            previous_close=_opt_decimal(payload.get("prevClosePrice")),
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
                f"binance does not support interval {interval.value}",
                adapter_id=self.adapter_id,
            )
        client = await self._ensure_client()
        params: dict[str, Any] = {
            "symbol": symbol,
            "interval": _INTERVAL_MAP[interval],
            "limit": min(limit, 1000),
        }
        if since is not None:
            params["startTime"] = int(since.timestamp() * 1000)
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/api/v3/klines",
            adapter_id=self.adapter_id,
            params=params,
        )
        if not isinstance(payload, list):
            if isinstance(payload, dict) and payload.get("code") == -1121:
                raise SymbolNotFoundError(
                    f"binance does not know symbol {symbol!r}",
                    adapter_id=self.adapter_id,
                )
            raise AdapterUnavailableError(
                "binance klines returned unexpected payload",
                adapter_id=self.adapter_id,
            )
        candles: list[Candle] = []
        for row in payload:
            # [open_time, open, high, low, close, volume, close_time, quote_volume,
            #  trades, taker_buy_volume, taker_buy_quote_volume, ignore]
            ts = datetime.fromtimestamp(row[0] / 1000, tz=UTC)
            candles.append(
                Candle(
                    timestamp=ts,
                    open=Decimal(str(row[1])),
                    high=Decimal(str(row[2])),
                    low=Decimal(str(row[3])),
                    close=Decimal(str(row[4])),
                    volume=Decimal(str(row[5])),
                    quote_volume=Decimal(str(row[7])),
                    trade_count=int(row[8]),
                )
            )
        return candles

    async def get_order_book(self, symbol: str, depth: int) -> OrderBook:
        client = await self._ensure_client()
        # Binance accepts 5/10/20/50/100/500/1000/5000; pick the smallest cap
        # that fits the request.
        bin_limit = next((b for b in (5, 10, 20, 50, 100) if b >= depth), 100)
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/api/v3/depth",
            adapter_id=self.adapter_id,
            params={"symbol": symbol, "limit": bin_limit},
        )
        if not isinstance(payload, dict):
            raise AdapterUnavailableError(
                "binance depth returned unexpected payload",
                adapter_id=self.adapter_id,
            )
        if payload.get("code") == -1121:
            raise SymbolNotFoundError(
                f"binance does not know symbol {symbol!r}",
                adapter_id=self.adapter_id,
            )
        bids_raw = payload.get("bids", [])[:depth]
        asks_raw = payload.get("asks", [])[:depth]
        bids = tuple(BookLevel(price=Decimal(p), size=Decimal(s)) for p, s in bids_raw)
        asks = tuple(BookLevel(price=Decimal(p), size=Decimal(s)) for p, s in asks_raw)
        return OrderBook(
            symbol=symbol,
            bids=tuple(sorted(bids, key=lambda level: level.price, reverse=True)),
            asks=tuple(sorted(asks, key=lambda level: level.price)),
            timestamp=datetime.now(UTC),
            source_adapter=self.adapter_id,
            sequence=payload.get("lastUpdateId"),
        )

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]:
        return streaming_not_supported(self.adapter_id)


def _opt_decimal(value: Any) -> Decimal | None:
    if value is None or value == "":
        return None
    return Decimal(str(value))

"""Coinbase Exchange public-data adapter.

Endpoints used (Exchange REST API):
- ``/products/{product_id}/ticker`` — current price + bid/ask.
- ``/products/{product_id}/stats`` — 24h high/low/volume.
- ``/products/{product_id}/candles`` — historical candles.
- ``/products/{product_id}/book?level={1,2,3}`` — order book.

Symbols use Coinbase product IDs (``"BTC-USD"``, ``"ETH-USD"``).
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

__all__ = ["CoinbaseAdapter"]


_INTERVAL_MAP: dict[Interval, int] = {
    Interval.M1: 60,
    Interval.M5: 300,
    Interval.M15: 900,
    Interval.H1: 3600,
    Interval.H4: 21600,
    Interval.D1: 86400,
}

_SUPPORTED_INTERVALS: tuple[Interval, ...] = tuple(_INTERVAL_MAP.keys())

_DEFAULT_BASE = "https://api.exchange.coinbase.com"
_RATE_LIMIT_PER_MINUTE = 600  # 10 req/sec public default


class CoinbaseAdapter:
    adapter_id = "coinbase"

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

    async def _request(self, path: str, params: dict[str, Any] | None = None) -> Any:
        client = await self._ensure_client()
        return await request_json(
            client,
            "GET",
            f"{self._base_url}{path}",
            adapter_id=self.adapter_id,
            headers={"Accept": "application/json"},
            params=params,
        )

    async def get_spot(self, symbol: str, quote_currency: str) -> SpotPrice:
        try:
            ticker = await self._request(f"/products/{symbol}/ticker")
            stats = await self._request(f"/products/{symbol}/stats")
        except AdapterUnavailableError as exc:
            # Coinbase returns 404 for unknown products; surface as SYMBOL_NOT_FOUND
            # so the orchestrator falls through to the next adapter rather than
            # treating it as a transient outage.
            if "HTTP 404" in str(exc):
                raise SymbolNotFoundError(
                    f"coinbase does not know product {symbol!r}",
                    adapter_id=self.adapter_id,
                ) from exc
            raise
        if not isinstance(ticker, dict) or "price" not in ticker:
            raise AdapterUnavailableError(
                "coinbase ticker returned unexpected payload",
                adapter_id=self.adapter_id,
            )
        last = Decimal(str(ticker["price"]))
        ts_raw = ticker.get("time")
        timestamp = _parse_iso(ts_raw) if isinstance(ts_raw, str) else datetime.now(UTC)
        high = _opt_decimal(stats.get("high")) if isinstance(stats, dict) else None
        low = _opt_decimal(stats.get("low")) if isinstance(stats, dict) else None
        volume = _opt_decimal(stats.get("volume")) if isinstance(stats, dict) else None
        return SpotPrice(
            symbol=symbol,
            last=last,
            quote_currency=quote_currency,
            timestamp=timestamp,
            source_adapter=self.adapter_id,
            bid=_opt_decimal(ticker.get("bid")),
            ask=_opt_decimal(ticker.get("ask")),
            high_24h=high,
            low_24h=low,
            volume_24h=volume,
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
                f"coinbase does not support interval {interval.value}",
                adapter_id=self.adapter_id,
            )
        params: dict[str, Any] = {"granularity": _INTERVAL_MAP[interval]}
        if since is not None:
            params["start"] = since.isoformat()
        try:
            payload = await self._request(f"/products/{symbol}/candles", params=params)
        except AdapterUnavailableError as exc:
            if "HTTP 404" in str(exc):
                raise SymbolNotFoundError(
                    f"coinbase does not know product {symbol!r}",
                    adapter_id=self.adapter_id,
                ) from exc
            raise
        if not isinstance(payload, list):
            raise AdapterUnavailableError(
                "coinbase candles returned unexpected payload",
                adapter_id=self.adapter_id,
            )
        candles: list[Candle] = []
        # Coinbase returns descending; reverse for our ascending contract.
        for row in reversed(payload[:limit]):
            # [time, low, high, open, close, volume]
            ts = datetime.fromtimestamp(int(row[0]), tz=UTC)
            candles.append(
                Candle(
                    timestamp=ts,
                    open=Decimal(str(row[3])),
                    high=Decimal(str(row[2])),
                    low=Decimal(str(row[1])),
                    close=Decimal(str(row[4])),
                    volume=Decimal(str(row[5])),
                )
            )
        return candles

    async def get_order_book(self, symbol: str, depth: int) -> OrderBook:
        # ``level=2`` returns up to 50 aggregated levels per side.
        try:
            payload = await self._request(f"/products/{symbol}/book", params={"level": 2})
        except AdapterUnavailableError as exc:
            if "HTTP 404" in str(exc):
                raise SymbolNotFoundError(
                    f"coinbase does not know product {symbol!r}",
                    adapter_id=self.adapter_id,
                ) from exc
            raise
        if not isinstance(payload, dict):
            raise AdapterUnavailableError(
                "coinbase book returned unexpected payload",
                adapter_id=self.adapter_id,
            )
        bids_raw = payload.get("bids", [])[:depth]
        asks_raw = payload.get("asks", [])[:depth]
        bids = tuple(
            BookLevel(price=Decimal(str(p)), size=Decimal(str(s))) for p, s, *_ in bids_raw
        )
        asks = tuple(
            BookLevel(price=Decimal(str(p)), size=Decimal(str(s))) for p, s, *_ in asks_raw
        )
        return OrderBook(
            symbol=symbol,
            bids=tuple(sorted(bids, key=lambda level: level.price, reverse=True)),
            asks=tuple(sorted(asks, key=lambda level: level.price)),
            timestamp=datetime.now(UTC),
            source_adapter=self.adapter_id,
            sequence=payload.get("sequence"),
        )

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]:
        return streaming_not_supported(self.adapter_id)


def _opt_decimal(value: Any) -> Decimal | None:
    if value is None or value == "":
        return None
    return Decimal(str(value))


def _parse_iso(value: str) -> datetime:
    raw = value
    if raw.endswith("Z"):
        raw = raw[:-1] + "+00:00"
    return datetime.fromisoformat(raw)

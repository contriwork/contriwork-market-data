"""Finnhub adapter — US-stocks oriented free tier.

Endpoints used:
- ``/api/v1/quote`` — spot price for a US stock (incl. high/low/prev close).
- ``/api/v1/stock/candle`` — historical candles by resolution (M1, M5,
  M15, M30, H1, D1, W1, M).

Free tier: 60 req/minute.
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

__all__ = ["FinnhubAdapter"]

_DEFAULT_BASE = "https://finnhub.io"
_RATE_LIMIT_PER_MINUTE = 60

_RESOLUTION_MAP: dict[Interval, str] = {
    Interval.M1: "1",
    Interval.M5: "5",
    Interval.M15: "15",
    Interval.M30: "30",
    Interval.H1: "60",
    Interval.D1: "D",
    Interval.W1: "W",
    Interval.MN1: "M",
}

_SUPPORTED_INTERVALS: tuple[Interval, ...] = tuple(_RESOLUTION_MAP.keys())


class FinnhubAdapter:
    adapter_id = "finnhub"

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
            supported_markets=("stocks_us",),
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
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/api/v1/quote",
            adapter_id=self.adapter_id,
            params={"symbol": symbol, "token": key},
        )
        if not isinstance(payload, dict) or payload.get("c") in (None, 0):
            raise SymbolNotFoundError(
                f"finnhub has no quote for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        last = Decimal(str(payload["c"]))
        timestamp = datetime.fromtimestamp(int(payload.get("t", 0)) or 0, tz=UTC)
        return SpotPrice(
            symbol=symbol,
            last=last,
            quote_currency=quote_currency,
            timestamp=timestamp,
            source_adapter=self.adapter_id,
            high_24h=_opt_decimal(payload.get("h")),
            low_24h=_opt_decimal(payload.get("l")),
            previous_close=_opt_decimal(payload.get("pc")),
            change_24h_pct=_opt_decimal(payload.get("dp")),
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
                f"finnhub does not support interval {interval.value}",
                adapter_id=self.adapter_id,
            )
        client = await self._ensure_client()
        key = await self._api_key_value()
        # Finnhub requires both ``from`` and ``to`` for the candle endpoint.
        to_ts = int(datetime.now(UTC).timestamp())
        from_ts = int(since.timestamp()) if since else (to_ts - 60 * 60 * 24 * 30)
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/api/v1/stock/candle",
            adapter_id=self.adapter_id,
            params={
                "symbol": symbol,
                "resolution": _RESOLUTION_MAP[interval],
                "from": from_ts,
                "to": to_ts,
                "token": key,
            },
        )
        if not isinstance(payload, dict) or payload.get("s") != "ok":
            raise SymbolNotFoundError(
                f"finnhub has no candle data for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        timestamps = payload.get("t") or []
        opens = payload.get("o") or []
        highs = payload.get("h") or []
        lows = payload.get("l") or []
        closes = payload.get("c") or []
        volumes = payload.get("v") or []
        candles: list[Candle] = []
        for i, ts in enumerate(timestamps[:limit]):
            candles.append(
                Candle(
                    timestamp=datetime.fromtimestamp(int(ts), tz=UTC),
                    open=Decimal(str(opens[i])),
                    high=Decimal(str(highs[i])),
                    low=Decimal(str(lows[i])),
                    close=Decimal(str(closes[i])),
                    volume=Decimal(str(volumes[i])),
                )
            )
        return candles

    async def get_order_book(self, symbol: str, depth: int) -> OrderBook:
        raise AdapterFeatureNotSupportedError(
            "finnhub does not expose order book on the free tier",
            adapter_id=self.adapter_id,
        )

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]:
        return streaming_not_supported(self.adapter_id)


def _opt_decimal(value: Any) -> Decimal | None:
    if value is None or value == "":
        return None
    return Decimal(str(value))

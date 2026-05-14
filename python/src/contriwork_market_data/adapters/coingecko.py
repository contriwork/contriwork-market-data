"""CoinGecko adapter — public market data for crypto.

Endpoints used (v3 API):
- ``/simple/price`` — spot price, optional 24h stats.
- ``/coins/{id}/ohlc`` — OHLC candles (no volume; volume comes back as 0).

Symbols are CoinGecko coin IDs (``"bitcoin"``, ``"ethereum"``, …).

Auth: optional. The free tier works without a key; the demo plan accepts
``x-cg-demo-api-key``; the pro plan uses ``x-cg-pro-api-key``. Choose via
``tier``.

Order book is **not supported** by the free / demo / pro public REST
surface — the orchestrator falls through to the next adapter in the
chain when ``get_order_book`` is invoked on this adapter.
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

__all__ = ["CoinGeckoAdapter"]


_DEFAULT_BASE = {
    "demo": "https://api.coingecko.com/api/v3",
    "free": "https://api.coingecko.com/api/v3",
    "pro": "https://pro-api.coingecko.com/api/v3",
}

_AUTH_HEADER = {
    "demo": "x-cg-demo-api-key",
    "free": None,
    "pro": "x-cg-pro-api-key",
}

# CoinGecko ``/coins/{id}/ohlc`` supports a ``days`` query that controls
# candle granularity. Map our Interval enum to the smallest valid ``days``
# that returns candles of the requested resolution.
_DAYS_FOR_INTERVAL: dict[Interval, str] = {
    Interval.M30: "1",
    Interval.H1: "1",
    Interval.H4: "7",
    Interval.D1: "30",
    Interval.W1: "365",
}

_SUPPORTED_INTERVALS: tuple[Interval, ...] = tuple(_DAYS_FOR_INTERVAL.keys())

_RATE_LIMIT_PER_MINUTE = {
    "free": 10,
    "demo": 30,
    "pro": 500,
}


class CoinGeckoAdapter:
    adapter_id = "coingecko"

    def __init__(
        self,
        *,
        api_key: str | None = None,
        api_key_provider: Callable[[], Awaitable[str | None]] | None = None,
        tier: str = "demo",
        base_url: str | None = None,
        timeout_s: float = 15.0,
        http_proxy: str | None = None,
        http_client: httpx.AsyncClient | None = None,
    ) -> None:
        if tier not in _DEFAULT_BASE:
            raise ValueError(f"unknown tier {tier!r}; expected one of {list(_DEFAULT_BASE)}")
        self._api_key = api_key
        self._api_key_provider = api_key_provider
        self._tier = tier
        self._base_url = (base_url or _DEFAULT_BASE[tier]).rstrip("/")
        self._timeout_s = timeout_s
        self._http_proxy = http_proxy
        self._client = http_client
        self._owns_client = http_client is None
        self.capability = Capability(
            supported_markets=("crypto",),
            supported_intervals=_SUPPORTED_INTERVALS,
            supported_quote_currencies="ANY",
            supports_order_book=False,
            supports_native_streaming=False,
            rate_limit_per_minute=_RATE_LIMIT_PER_MINUTE[tier],
            requires_auth=tier == "pro",
            tier_options=("demo", "free", "pro"),
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

    async def _headers(self) -> dict[str, str]:
        header_name = _AUTH_HEADER[self._tier]
        if header_name is None:
            return {}
        key = await resolve_api_key(
            adapter_id=self.adapter_id,
            api_key=self._api_key,
            api_key_provider=self._api_key_provider,
            required=self.capability.requires_auth,
        )
        return {header_name: key} if key else {}

    async def get_spot(self, symbol: str, quote_currency: str) -> SpotPrice:
        client = await self._ensure_client()
        vs = quote_currency.lower()
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/simple/price",
            adapter_id=self.adapter_id,
            headers=await self._headers(),
            params={
                "ids": symbol,
                "vs_currencies": vs,
                "include_24hr_change": "true",
                "include_24hr_vol": "true",
                "include_market_cap": "true",
                "include_last_updated_at": "true",
                "precision": "full",
            },
        )
        if not isinstance(payload, dict) or symbol not in payload:
            raise SymbolNotFoundError(
                f"coingecko has no spot for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        body = payload[symbol]
        try:
            price = Decimal(str(body[vs]))
        except (KeyError, ValueError) as exc:
            raise AdapterUnavailableError(
                f"coingecko returned unexpected payload for {symbol!r}",
                adapter_id=self.adapter_id,
            ) from exc
        timestamp = (
            datetime.fromtimestamp(int(body["last_updated_at"]), tz=UTC)
            if "last_updated_at" in body
            else datetime.now(UTC)
        )
        return SpotPrice(
            symbol=symbol,
            last=price,
            quote_currency=quote_currency,
            timestamp=timestamp,
            source_adapter=self.adapter_id,
            change_24h_pct=_opt_decimal(body.get(f"{vs}_24h_change")),
            volume_24h=_opt_decimal(body.get(f"{vs}_24h_vol")),
            market_cap=_opt_decimal(body.get(f"{vs}_market_cap")),
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
                f"coingecko does not support interval {interval.value}; "
                f"choose one of {[i.value for i in _SUPPORTED_INTERVALS]}",
                adapter_id=self.adapter_id,
            )
        client = await self._ensure_client()
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/coins/{symbol}/ohlc",
            adapter_id=self.adapter_id,
            headers=await self._headers(),
            params={"vs_currency": "usd", "days": _DAYS_FOR_INTERVAL[interval]},
        )
        if not isinstance(payload, list):
            raise SymbolNotFoundError(
                f"coingecko has no ohlcv for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        candles: list[Candle] = []
        for row in payload:
            ts_ms, o, h, low, c = row[0], row[1], row[2], row[3], row[4]
            ts = datetime.fromtimestamp(ts_ms / 1000, tz=UTC)
            if since is not None and ts < since:
                continue
            candles.append(
                Candle(
                    timestamp=ts,
                    open=Decimal(str(o)),
                    high=Decimal(str(h)),
                    low=Decimal(str(low)),
                    close=Decimal(str(c)),
                    volume=Decimal("0"),
                )
            )
            if len(candles) >= limit:
                break
        candles.sort(key=lambda candle: candle.timestamp)
        return candles

    async def get_order_book(self, symbol: str, depth: int) -> OrderBook:
        # CoinGecko's order book endpoint is paid-tier only.
        raise AdapterFeatureNotSupportedError(
            "coingecko does not support order book",
            adapter_id=self.adapter_id,
        )

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]:
        return streaming_not_supported(self.adapter_id)


def _opt_decimal(value: Any) -> Decimal | None:
    if value is None:
        return None
    return Decimal(str(value))

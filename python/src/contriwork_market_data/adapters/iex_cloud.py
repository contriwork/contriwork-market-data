"""IEX Cloud adapter — REST surface against the v1 API.

The original IEX Cloud service was sunset in August 2024; the surface
shape is preserved here so callers running compatible providers (e.g.
mirror deployments or replacement vendors who kept the path scheme) can
swap base_url to redirect.

Endpoints used:
- ``/stable/stock/{symbol}/quote`` — spot.
- ``/stable/stock/{symbol}/chart/{range}`` — historical candles.

Auth header: ``token`` query parameter or ``Authorization`` header.
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

__all__ = ["IEXCloudAdapter"]

_DEFAULT_BASE = "https://cloud.iexapis.com"
_RATE_LIMIT_PER_MINUTE = 100

_RANGE_FOR_INTERVAL: dict[Interval, str] = {
    Interval.D1: "1m",
    Interval.W1: "1y",
    Interval.MN1: "max",
}

_SUPPORTED_INTERVALS: tuple[Interval, ...] = tuple(_RANGE_FOR_INTERVAL.keys())


class IEXCloudAdapter:
    adapter_id = "iex-cloud"

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
            tier_options=("sandbox", "standard"),
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
                f"{self._base_url}/stable/stock/{symbol}/quote",
                adapter_id=self.adapter_id,
                params={"token": key},
            )
        except AdapterUnavailableError as exc:
            if "HTTP 404" in str(exc):
                raise SymbolNotFoundError(
                    f"iex-cloud does not know symbol {symbol!r}",
                    adapter_id=self.adapter_id,
                ) from exc
            raise
        if not isinstance(payload, dict) or "latestPrice" not in payload:
            raise AdapterUnavailableError(
                "iex-cloud returned unexpected payload",
                adapter_id=self.adapter_id,
            )
        return SpotPrice(
            symbol=symbol,
            last=Decimal(str(payload["latestPrice"])),
            quote_currency=quote_currency,
            timestamp=datetime.fromtimestamp(int(payload.get("latestUpdate", 0)) / 1000, tz=UTC),
            source_adapter=self.adapter_id,
            bid=_opt_decimal(payload.get("iexBidPrice") or payload.get("bidPrice")),
            ask=_opt_decimal(payload.get("iexAskPrice") or payload.get("askPrice")),
            high_24h=_opt_decimal(payload.get("high")),
            low_24h=_opt_decimal(payload.get("low")),
            volume_24h=_opt_decimal(payload.get("latestVolume")),
            change_24h_pct=_opt_decimal(payload.get("changePercent")),
            previous_close=_opt_decimal(payload.get("previousClose")),
            market_cap=_opt_decimal(payload.get("marketCap")),
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
                f"iex-cloud does not support interval {interval.value}",
                adapter_id=self.adapter_id,
            )
        client = await self._ensure_client()
        key = await self._api_key_value()
        range_value = _RANGE_FOR_INTERVAL[interval]
        try:
            payload = await request_json(
                client,
                "GET",
                f"{self._base_url}/stable/stock/{symbol}/chart/{range_value}",
                adapter_id=self.adapter_id,
                params={"token": key, "chartCloseOnly": "false"},
            )
        except AdapterUnavailableError as exc:
            if "HTTP 404" in str(exc):
                raise SymbolNotFoundError(
                    f"iex-cloud does not know symbol {symbol!r}",
                    adapter_id=self.adapter_id,
                ) from exc
            raise
        if not isinstance(payload, list):
            raise AdapterUnavailableError(
                "iex-cloud chart returned unexpected payload",
                adapter_id=self.adapter_id,
            )
        candles: list[Candle] = []
        for row in payload[:limit]:
            ts = datetime.strptime(row["date"], "%Y-%m-%d").replace(tzinfo=UTC)
            if since is not None and ts < since:
                continue
            candles.append(
                Candle(
                    timestamp=ts,
                    open=Decimal(str(row["open"])),
                    high=Decimal(str(row["high"])),
                    low=Decimal(str(row["low"])),
                    close=Decimal(str(row["close"])),
                    volume=Decimal(str(row.get("volume", 0))),
                )
            )
        return candles

    async def get_order_book(self, symbol: str, depth: int) -> OrderBook:
        raise AdapterFeatureNotSupportedError(
            "iex-cloud does not expose a public order book endpoint",
            adapter_id=self.adapter_id,
        )

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]:
        return streaming_not_supported(self.adapter_id)


def _opt_decimal(value: Any) -> Decimal | None:
    if value is None or value == "":
        return None
    return Decimal(str(value))

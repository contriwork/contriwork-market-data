"""Tiingo adapter — US stocks (free tier).

Endpoints used:
- ``/iex/{ticker}`` — latest IEX-routed quote.
- ``/iex/{ticker}/prices`` — historical intraday prices.

Auth header: ``Authorization: Token <api_key>`` or ``token`` query.
Free tier: 50 unique-symbol limit / day, 1000 req/hour.
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

__all__ = ["TiingoAdapter"]


_DEFAULT_BASE = "https://api.tiingo.com"
_RATE_LIMIT_PER_MINUTE = 60

_RESAMPLE_FREQ: dict[Interval, str] = {
    Interval.M1: "1min",
    Interval.M5: "5min",
    Interval.M15: "15min",
    Interval.M30: "30min",
    Interval.H1: "1hour",
    Interval.D1: "daily",
}

_SUPPORTED_INTERVALS: tuple[Interval, ...] = tuple(_RESAMPLE_FREQ.keys())


class TiingoAdapter:
    adapter_id = "tiingo"

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
        try:
            payload = await request_json(
                client,
                "GET",
                f"{self._base_url}/iex/{symbol}",
                adapter_id=self.adapter_id,
                headers={"Authorization": f"Token {key}"},
            )
        except AdapterUnavailableError as exc:
            if "HTTP 404" in str(exc):
                raise SymbolNotFoundError(
                    f"tiingo does not know ticker {symbol!r}",
                    adapter_id=self.adapter_id,
                ) from exc
            raise
        if not isinstance(payload, list) or not payload:
            raise SymbolNotFoundError(
                f"tiingo returned no quote for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        item = payload[0]
        last = _opt_decimal(item.get("last") or item.get("tngoLast"))
        if last is None:
            raise SymbolNotFoundError(
                f"tiingo returned empty quote for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        timestamp = (
            _parse_iso(item.get("timestamp")) if item.get("timestamp") else datetime.now(UTC)
        )
        return SpotPrice(
            symbol=symbol,
            last=last,
            quote_currency=quote_currency,
            timestamp=timestamp,
            source_adapter=self.adapter_id,
            bid=_opt_decimal(item.get("bidPrice")),
            ask=_opt_decimal(item.get("askPrice")),
            high_24h=_opt_decimal(item.get("high")),
            low_24h=_opt_decimal(item.get("low")),
            volume_24h=_opt_decimal(item.get("volume")),
            previous_close=_opt_decimal(item.get("prevClose")),
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
                f"tiingo does not support interval {interval.value}",
                adapter_id=self.adapter_id,
            )
        client = await self._ensure_client()
        key = await self._api_key_value()
        params: dict[str, Any] = {"resampleFreq": _RESAMPLE_FREQ[interval]}
        if since is not None:
            params["startDate"] = since.strftime("%Y-%m-%d")
        try:
            payload = await request_json(
                client,
                "GET",
                f"{self._base_url}/iex/{symbol}/prices",
                adapter_id=self.adapter_id,
                headers={"Authorization": f"Token {key}"},
                params=params,
            )
        except AdapterUnavailableError as exc:
            if "HTTP 404" in str(exc):
                raise SymbolNotFoundError(
                    f"tiingo does not know ticker {symbol!r}",
                    adapter_id=self.adapter_id,
                ) from exc
            raise
        if not isinstance(payload, list):
            raise AdapterUnavailableError(
                "tiingo prices returned unexpected payload",
                adapter_id=self.adapter_id,
            )
        candles: list[Candle] = []
        for row in payload[:limit]:
            ts = _parse_iso(row.get("date") or row.get("timestamp"))
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
            "tiingo does not expose order book",
            adapter_id=self.adapter_id,
        )

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]:
        return streaming_not_supported(self.adapter_id)


def _opt_decimal(value: Any) -> Decimal | None:
    if value is None or value == "":
        return None
    return Decimal(str(value))


def _parse_iso(value: Any) -> datetime:
    if not isinstance(value, str):
        return datetime.now(UTC)
    raw = value
    if raw.endswith("Z"):
        raw = raw[:-1] + "+00:00"
    try:
        return datetime.fromisoformat(raw)
    except ValueError:
        # Fallback for date-only strings (``"2026-04-30"``).
        try:
            return datetime.strptime(raw[:10], "%Y-%m-%d").replace(tzinfo=UTC)
        except ValueError:
            return datetime.now(UTC)

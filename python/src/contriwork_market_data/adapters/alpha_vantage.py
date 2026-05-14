"""Alpha Vantage adapter — covers crypto + US stocks + BIST + forex.

Endpoints used:
- ``function=GLOBAL_QUOTE`` — spot for stocks (US + BIST via ``.IST`` suffix).
- ``function=CURRENCY_EXCHANGE_RATE`` — spot for crypto / forex.
- ``function=TIME_SERIES_INTRADAY|DAILY|WEEKLY|MONTHLY`` — historical OHLCV.

Free tier is heavily throttled (5 req/min, 500 req/day). The adapter sets
``rate_limit_per_minute=5`` accordingly so the orchestrator's token
bucket throttles without bursting through the daily cap.
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
    RateLimitedError,
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

__all__ = ["AlphaVantageAdapter"]

_DEFAULT_BASE = "https://www.alphavantage.co"
_RATE_LIMIT_PER_MINUTE = 5

# Intraday intervals → ``interval`` query value
_INTRADAY_INTERVAL: dict[Interval, str] = {
    Interval.M1: "1min",
    Interval.M5: "5min",
    Interval.M15: "15min",
    Interval.M30: "30min",
    Interval.H1: "60min",
}

_SUPPORTED_INTERVALS: tuple[Interval, ...] = (
    Interval.M1,
    Interval.M5,
    Interval.M15,
    Interval.M30,
    Interval.H1,
    Interval.D1,
    Interval.W1,
    Interval.MN1,
)


class AlphaVantageAdapter:
    adapter_id = "alpha-vantage"

    def __init__(
        self,
        *,
        api_key: str | None = None,
        api_key_provider: Callable[[], Awaitable[str | None]] | None = None,
        base_url: str | None = None,
        timeout_s: float = 20.0,
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
            supported_markets=("crypto", "stocks_us", "stocks_tr", "forex"),
            supported_intervals=_SUPPORTED_INTERVALS,
            supported_quote_currencies="ANY",
            supports_order_book=False,
            supports_native_streaming=False,
            rate_limit_per_minute=_RATE_LIMIT_PER_MINUTE,
            requires_auth=True,
            tier_options=("free", "premium"),
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

    @staticmethod
    def _check_throttle(payload: object, adapter_id: str) -> None:
        """Alpha Vantage returns 200 with a ``Note`` or ``Information`` field
        when the caller is throttled; surface that as RATE_LIMITED so the
        orchestrator's retry runner can react."""
        if isinstance(payload, dict):
            text = " ".join(str(payload.get(k, "")) for k in ("Note", "Information")).lower()
            if "thank you for using" in text or "rate" in text or "throttle" in text:
                raise RateLimitedError(
                    f"alpha-vantage throttled: {payload.get('Note') or payload.get('Information')}",
                    adapter_id=adapter_id,
                )

    async def get_spot(self, symbol: str, quote_currency: str) -> SpotPrice:
        client = await self._ensure_client()
        key = await self._api_key_value()
        # Heuristic: a symbol containing "/" or that is exactly 3 chars is
        # treated as a fiat/crypto pair (CURRENCY_EXCHANGE_RATE); everything
        # else (including 4-char US tickers like ``AAPL``) is a stock ticker
        # for GLOBAL_QUOTE.
        if "/" in symbol or (len(symbol) == 3 and symbol.isalpha()):
            payload = await request_json(
                client,
                "GET",
                f"{self._base_url}/query",
                adapter_id=self.adapter_id,
                params={
                    "function": "CURRENCY_EXCHANGE_RATE",
                    "from_currency": symbol.split("/")[0],
                    "to_currency": quote_currency,
                    "apikey": key,
                },
            )
            self._check_throttle(payload, self.adapter_id)
            block = (payload or {}).get("Realtime Currency Exchange Rate")
            if not block:
                raise SymbolNotFoundError(
                    f"alpha-vantage has no exchange rate for {symbol!r}/{quote_currency!r}",
                    adapter_id=self.adapter_id,
                )
            return SpotPrice(
                symbol=symbol,
                last=Decimal(str(block["5. Exchange Rate"])),
                quote_currency=quote_currency,
                timestamp=datetime.now(UTC),
                source_adapter=self.adapter_id,
                bid=_opt_decimal(block.get("8. Bid Price")),
                ask=_opt_decimal(block.get("9. Ask Price")),
            )
        # Stock path
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/query",
            adapter_id=self.adapter_id,
            params={"function": "GLOBAL_QUOTE", "symbol": symbol, "apikey": key},
        )
        self._check_throttle(payload, self.adapter_id)
        quote = (payload or {}).get("Global Quote") or {}
        if not quote.get("05. price"):
            raise SymbolNotFoundError(
                f"alpha-vantage has no quote for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        change_pct = str(quote.get("10. change percent", "0")).rstrip("%")
        return SpotPrice(
            symbol=symbol,
            last=Decimal(str(quote["05. price"])),
            quote_currency=quote_currency,
            timestamp=datetime.now(UTC),
            source_adapter=self.adapter_id,
            high_24h=_opt_decimal(quote.get("03. high")),
            low_24h=_opt_decimal(quote.get("04. low")),
            volume_24h=_opt_decimal(quote.get("06. volume")),
            change_24h_pct=Decimal(change_pct) if change_pct else None,
            previous_close=_opt_decimal(quote.get("08. previous close")),
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
                f"alpha-vantage does not support interval {interval.value}",
                adapter_id=self.adapter_id,
            )
        client = await self._ensure_client()
        key = await self._api_key_value()
        if interval in _INTRADAY_INTERVAL:
            function = "TIME_SERIES_INTRADAY"
            params = {
                "function": function,
                "symbol": symbol,
                "interval": _INTRADAY_INTERVAL[interval],
                "outputsize": "full",
                "apikey": key,
            }
            time_series_key = f"Time Series ({_INTRADAY_INTERVAL[interval]})"
        else:
            function_map = {
                Interval.D1: ("TIME_SERIES_DAILY", "Time Series (Daily)"),
                Interval.W1: ("TIME_SERIES_WEEKLY", "Weekly Time Series"),
                Interval.MN1: ("TIME_SERIES_MONTHLY", "Monthly Time Series"),
            }
            function, time_series_key = function_map[interval]
            params = {"function": function, "symbol": symbol, "apikey": key}
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/query",
            adapter_id=self.adapter_id,
            params=params,
        )
        self._check_throttle(payload, self.adapter_id)
        series = (payload or {}).get(time_series_key)
        if not isinstance(series, dict) or not series:
            raise SymbolNotFoundError(
                f"alpha-vantage has no time series for {symbol!r}/{interval.value}",
                adapter_id=self.adapter_id,
            )
        candles: list[Candle] = []
        for ts_str, row in series.items():
            ts = _parse_alpha_ts(ts_str)
            if since is not None and ts < since:
                continue
            candles.append(
                Candle(
                    timestamp=ts,
                    open=Decimal(str(row["1. open"])),
                    high=Decimal(str(row["2. high"])),
                    low=Decimal(str(row["3. low"])),
                    close=Decimal(str(row["4. close"])),
                    volume=Decimal(str(row.get("5. volume", "0"))),
                )
            )
        candles.sort(key=lambda candle: candle.timestamp)
        return candles[-limit:] if len(candles) > limit else candles

    async def get_order_book(self, symbol: str, depth: int) -> OrderBook:
        raise AdapterFeatureNotSupportedError(
            "alpha-vantage does not expose order book",
            adapter_id=self.adapter_id,
        )

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]:
        return streaming_not_supported(self.adapter_id)


def _opt_decimal(value: Any) -> Decimal | None:
    if value is None or value == "":
        return None
    return Decimal(str(value))


def _parse_alpha_ts(text: str) -> datetime:
    raw = text.strip()
    fmt = "%Y-%m-%d %H:%M:%S" if " " in raw else "%Y-%m-%d"
    return datetime.strptime(raw, fmt).replace(tzinfo=UTC)

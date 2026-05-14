"""CoinMarketCap adapter — Pro API key required.

Endpoints used (v2 Pro API):
- ``/v2/cryptocurrency/quotes/latest`` — spot price + 24h stats.

Historical OHLCV requires the Pro tier ``/v2/cryptocurrency/ohlcv/historical``
endpoint; only the latest-only path is implemented in v0.1.0 since the
free tier does not include historical candles.

Order book and streaming are not provided by the free or basic plans —
both methods raise ``ADAPTER_FEATURE_NOT_SUPPORTED``.

Symbols use CoinMarketCap symbols (``"BTC"``, ``"ETH"``). The adapter
defaults to looking them up by symbol; pass numeric ``cmc_id`` via the
``extra`` parameter once that mapping is exposed.
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

__all__ = ["CoinMarketCapAdapter"]


_DEFAULT_BASE = "https://pro-api.coinmarketcap.com"
_RATE_LIMIT_PER_MINUTE = 30  # ``Basic`` tier default


class CoinMarketCapAdapter:
    adapter_id = "coinmarketcap"

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
            supported_markets=("crypto",),
            supported_intervals=(),  # OHLCV is paid-tier in v0.1.0 scope
            supported_quote_currencies="ANY",
            supports_order_book=False,
            supports_native_streaming=False,
            rate_limit_per_minute=_RATE_LIMIT_PER_MINUTE,
            requires_auth=True,
            tier_options=("basic", "hobbyist", "startup", "standard", "professional", "enterprise"),
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
        key = await resolve_api_key(
            adapter_id=self.adapter_id,
            api_key=self._api_key,
            api_key_provider=self._api_key_provider,
            required=True,
        )
        # ``required=True`` guarantees ``key`` is non-empty.
        assert key is not None
        return {"X-CMC_PRO_API_KEY": key, "Accept": "application/json"}

    async def get_spot(self, symbol: str, quote_currency: str) -> SpotPrice:
        client = await self._ensure_client()
        payload = await request_json(
            client,
            "GET",
            f"{self._base_url}/v2/cryptocurrency/quotes/latest",
            adapter_id=self.adapter_id,
            headers=await self._headers(),
            params={"symbol": symbol, "convert": quote_currency.upper()},
        )
        if not isinstance(payload, dict):
            raise AdapterUnavailableError(
                "coinmarketcap returned non-object payload",
                adapter_id=self.adapter_id,
            )
        data = payload.get("data") or {}
        entries = data.get(symbol) or data.get(symbol.upper())
        if not entries:
            raise SymbolNotFoundError(
                f"coinmarketcap does not know symbol {symbol!r}",
                adapter_id=self.adapter_id,
            )
        # ``data[<symbol>]`` is a list of candidate ids; pick the first.
        entry = entries[0] if isinstance(entries, list) else entries
        quote_block = (entry.get("quote") or {}).get(quote_currency.upper())
        if not quote_block:
            raise AdapterUnavailableError(
                f"coinmarketcap returned no quote for {symbol!r}/{quote_currency!r}",
                adapter_id=self.adapter_id,
            )
        return SpotPrice(
            symbol=symbol,
            last=Decimal(str(quote_block["price"])),
            quote_currency=quote_currency,
            timestamp=_parse_iso(quote_block.get("last_updated")),
            source_adapter=self.adapter_id,
            volume_24h=_opt_decimal(quote_block.get("volume_24h")),
            change_24h_pct=_opt_decimal(quote_block.get("percent_change_24h")),
            market_cap=_opt_decimal(quote_block.get("market_cap")),
        )

    async def get_ohlcv(
        self,
        symbol: str,
        interval: Interval,
        since: datetime | None,
        limit: int,
    ) -> list[Candle]:
        # ``/v2/cryptocurrency/ohlcv/historical`` is paid-tier; v0.1.0 keeps
        # the adapter REST-spot-only. PR 4.x will expand on paid surfaces.
        raise InvalidIntervalError(
            "coinmarketcap historical OHLCV is paid-tier and out of v0.1.0 scope",
            adapter_id=self.adapter_id,
        )

    async def get_order_book(self, symbol: str, depth: int) -> OrderBook:
        raise AdapterFeatureNotSupportedError(
            "coinmarketcap does not expose order book on the supported tiers",
            adapter_id=self.adapter_id,
        )

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]:
        return streaming_not_supported(self.adapter_id)


def _opt_decimal(value: Any) -> Decimal | None:
    if value is None:
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
        return datetime.now(UTC)

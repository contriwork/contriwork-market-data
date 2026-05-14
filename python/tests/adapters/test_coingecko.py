"""CoinGeckoAdapter unit tests — respx-mocked HTTP."""

from __future__ import annotations

from datetime import UTC
from decimal import Decimal

import httpx
import pytest
import respx

from contriwork_market_data import (
    AdapterFeatureNotSupportedError,
    AdapterUnavailableError,
    Interval,
    InvalidIntervalError,
    RateLimitedError,
    SymbolNotFoundError,
)
from contriwork_market_data.adapters import CoinGeckoAdapter

_BASE = "https://api.coingecko.com/api/v3"


@pytest.mark.asyncio
async def test_get_spot_happy_path() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/simple/price").respond(
            200,
            json={
                "bitcoin": {
                    "usd": 65000.5,
                    "usd_24h_change": 1.23,
                    "usd_24h_vol": 12345.6,
                    "usd_market_cap": 1_300_000_000_000.0,
                    "last_updated_at": 1714492800,
                }
            },
        )
        adapter = CoinGeckoAdapter(api_key="demo-key")
        spot = await adapter.get_spot("bitcoin", "USD")
        await adapter.aclose()
        assert spot.symbol == "bitcoin"
        assert spot.last == Decimal("65000.5")
        assert spot.change_24h_pct == Decimal("1.23")
        assert spot.source_adapter == "coingecko"


@pytest.mark.asyncio
async def test_get_spot_unknown_symbol() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/simple/price").respond(200, json={})
        adapter = CoinGeckoAdapter()
        with pytest.raises(SymbolNotFoundError):
            await adapter.get_spot("definitely-not-a-coin", "USD")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_get_spot_rate_limited() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/simple/price").respond(429, json={"error": "too many"})
        adapter = CoinGeckoAdapter()
        with pytest.raises(RateLimitedError):
            await adapter.get_spot("bitcoin", "USD")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_get_ohlcv_filters_by_since() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/coins/bitcoin/ohlc").respond(
            200,
            json=[
                [1714492800000, 100, 101, 99, 100],
                [1714496400000, 100, 102, 99, 101],
                [1714500000000, 101, 105, 100, 104],
            ],
        )
        adapter = CoinGeckoAdapter()
        # since = second candle's timestamp
        from datetime import datetime

        since = datetime.fromtimestamp(1714496400, tz=UTC)
        candles = await adapter.get_ohlcv("bitcoin", Interval.H1, since, 100)
        await adapter.aclose()
        assert len(candles) == 2
        assert candles[0].timestamp >= since


@pytest.mark.asyncio
async def test_get_ohlcv_invalid_interval() -> None:
    adapter = CoinGeckoAdapter()
    with pytest.raises(InvalidIntervalError):
        await adapter.get_ohlcv("bitcoin", Interval.M5, None, 10)
    await adapter.aclose()


@pytest.mark.asyncio
async def test_order_book_not_supported() -> None:
    adapter = CoinGeckoAdapter()
    with pytest.raises(AdapterFeatureNotSupportedError):
        await adapter.get_order_book("bitcoin", 20)
    await adapter.aclose()


@pytest.mark.asyncio
async def test_subscribe_ticker_raises_not_supported() -> None:
    adapter = CoinGeckoAdapter()
    with pytest.raises(AdapterFeatureNotSupportedError):
        async for _ in adapter.subscribe_ticker("bitcoin"):
            pass
    await adapter.aclose()


@pytest.mark.asyncio
async def test_network_error_becomes_adapter_unavailable() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/simple/price").mock(side_effect=httpx.ConnectError("dns"))
        adapter = CoinGeckoAdapter()
        with pytest.raises(AdapterUnavailableError):
            await adapter.get_spot("bitcoin", "USD")
        await adapter.aclose()

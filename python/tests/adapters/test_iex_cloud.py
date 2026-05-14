"""IEXCloudAdapter unit tests — respx-mocked HTTP."""

from __future__ import annotations

from decimal import Decimal

import pytest
import respx

from contriwork_market_data import (
    AdapterFeatureNotSupportedError,
    Interval,
    SymbolNotFoundError,
)
from contriwork_market_data.adapters import IEXCloudAdapter

_BASE = "https://cloud.iexapis.com"


@pytest.mark.asyncio
async def test_get_spot_happy_path() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/stable/stock/AAPL/quote").respond(
            200,
            json={
                "symbol": "AAPL",
                "latestPrice": 199.99,
                "latestUpdate": 1714492800000,
                "iexBidPrice": 199.97,
                "iexAskPrice": 200.01,
                "high": 200.10,
                "low": 198.50,
                "latestVolume": 1234567,
                "changePercent": 0.0024,
                "previousClose": 199.50,
                "marketCap": 3e12,
            },
        )
        adapter = IEXCloudAdapter(api_key="test")
        spot = await adapter.get_spot("AAPL", "USD")
        await adapter.aclose()
        assert spot.last == Decimal("199.99")
        assert spot.market_cap == Decimal("3E+12")


@pytest.mark.asyncio
async def test_404_maps_to_symbol_not_found() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/stable/stock/ZZZZ/quote").respond(404, json={"message": "Unknown"})
        adapter = IEXCloudAdapter(api_key="test")
        with pytest.raises(SymbolNotFoundError):
            await adapter.get_spot("ZZZZ", "USD")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_get_ohlcv_daily_chart() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/stable/stock/AAPL/chart/1m").respond(
            200,
            json=[
                {
                    "date": "2026-04-29",
                    "open": 199,
                    "high": 200,
                    "low": 198,
                    "close": 199.5,
                    "volume": 1100000,
                },
                {
                    "date": "2026-04-30",
                    "open": 199.5,
                    "high": 200.5,
                    "low": 199,
                    "close": 200,
                    "volume": 1200000,
                },
            ],
        )
        adapter = IEXCloudAdapter(api_key="test")
        candles = await adapter.get_ohlcv("AAPL", Interval.D1, None, 10)
        await adapter.aclose()
        assert len(candles) == 2


@pytest.mark.asyncio
async def test_order_book_not_supported() -> None:
    adapter = IEXCloudAdapter(api_key="test")
    with pytest.raises(AdapterFeatureNotSupportedError):
        await adapter.get_order_book("AAPL", 10)
    await adapter.aclose()

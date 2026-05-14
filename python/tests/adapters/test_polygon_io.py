"""PolygonIOAdapter unit tests — respx-mocked HTTP."""

from __future__ import annotations

from decimal import Decimal

import pytest
import respx

from contriwork_market_data import (
    AdapterFeatureNotSupportedError,
    Interval,
    SymbolNotFoundError,
)
from contriwork_market_data.adapters import PolygonIOAdapter

_BASE = "https://api.polygon.io"


@pytest.mark.asyncio
async def test_get_spot_happy_path() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/v2/last/trade/AAPL").respond(
            200,
            json={
                "status": "OK",
                "results": {
                    "p": 199.99,
                    "s": 100,
                    "t": 1714492800_000_000_000,  # ns
                },
            },
        )
        adapter = PolygonIOAdapter(api_key="test")
        spot = await adapter.get_spot("AAPL", "USD")
        await adapter.aclose()
        assert spot.last == Decimal("199.99")


@pytest.mark.asyncio
async def test_404_maps_to_symbol_not_found() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/v2/last/trade/ZZZZ").respond(404, json={"status": "NOT_FOUND"})
        adapter = PolygonIOAdapter(api_key="test")
        with pytest.raises(SymbolNotFoundError):
            await adapter.get_spot("ZZZZ", "USD")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_get_ohlcv_aggs() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get(url__regex=r".*/v2/aggs/ticker/AAPL/range/1/day/.*").respond(
            200,
            json={
                "status": "OK",
                "results": [
                    {
                        "t": 1714492800000,
                        "o": 199,
                        "h": 200,
                        "l": 198,
                        "c": 199.5,
                        "v": 1100000,
                        "n": 5000,
                    },
                    {
                        "t": 1714579200000,
                        "o": 199.5,
                        "h": 200.5,
                        "l": 199,
                        "c": 200,
                        "v": 1200000,
                        "n": 6000,
                    },
                ],
            },
        )
        adapter = PolygonIOAdapter(api_key="test")
        candles = await adapter.get_ohlcv("AAPL", Interval.D1, None, 10)
        await adapter.aclose()
        assert len(candles) == 2
        assert candles[0].trade_count == 5000


@pytest.mark.asyncio
async def test_order_book_not_supported() -> None:
    adapter = PolygonIOAdapter(api_key="test")
    with pytest.raises(AdapterFeatureNotSupportedError):
        await adapter.get_order_book("AAPL", 10)
    await adapter.aclose()

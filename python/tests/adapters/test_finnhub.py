"""FinnhubAdapter unit tests — respx-mocked HTTP."""

from __future__ import annotations

from decimal import Decimal

import pytest
import respx

from contriwork_market_data import (
    Interval,
    SymbolNotFoundError,
)
from contriwork_market_data.adapters import FinnhubAdapter

_BASE = "https://finnhub.io"


@pytest.mark.asyncio
async def test_get_spot_happy_path() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/api/v1/quote").respond(
            200,
            json={
                "c": 199.99,
                "h": 200.10,
                "l": 198.50,
                "o": 199.50,
                "pc": 199.50,
                "dp": 0.24,
                "t": 1714492800,
            },
        )
        adapter = FinnhubAdapter(api_key="test")
        spot = await adapter.get_spot("AAPL", "USD")
        await adapter.aclose()
        assert spot.last == Decimal("199.99")
        assert spot.change_24h_pct == Decimal("0.24")


@pytest.mark.asyncio
async def test_unknown_symbol() -> None:
    async with respx.mock(base_url=_BASE) as router:
        # Finnhub returns ``c=0`` for unknown symbols.
        router.get("/api/v1/quote").respond(200, json={"c": 0, "t": 0})
        adapter = FinnhubAdapter(api_key="test")
        with pytest.raises(SymbolNotFoundError):
            await adapter.get_spot("ZZZZ", "USD")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_get_ohlcv() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/api/v1/stock/candle").respond(
            200,
            json={
                "s": "ok",
                "c": [199.5, 199.8],
                "h": [200.0, 200.2],
                "l": [199.0, 199.4],
                "o": [199.0, 199.5],
                "t": [1714492800, 1714492860],
                "v": [1000, 1500],
            },
        )
        adapter = FinnhubAdapter(api_key="test")
        candles = await adapter.get_ohlcv("AAPL", Interval.M1, None, 10)
        await adapter.aclose()
        assert len(candles) == 2
        assert candles[0].close == Decimal("199.5")


@pytest.mark.asyncio
async def test_ohlcv_no_data() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/api/v1/stock/candle").respond(200, json={"s": "no_data"})
        adapter = FinnhubAdapter(api_key="test")
        with pytest.raises(SymbolNotFoundError):
            await adapter.get_ohlcv("AAPL", Interval.M1, None, 10)
        await adapter.aclose()

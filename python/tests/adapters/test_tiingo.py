"""TiingoAdapter unit tests — respx-mocked HTTP."""

from __future__ import annotations

from decimal import Decimal

import pytest
import respx

from contriwork_market_data import (
    Interval,
    SymbolNotFoundError,
)
from contriwork_market_data.adapters import TiingoAdapter

_BASE = "https://api.tiingo.com"


@pytest.mark.asyncio
async def test_get_spot_happy_path() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/iex/AAPL").respond(
            200,
            json=[
                {
                    "ticker": "AAPL",
                    "last": 199.99,
                    "tngoLast": 199.99,
                    "bidPrice": 199.97,
                    "askPrice": 200.01,
                    "high": 200.10,
                    "low": 198.50,
                    "volume": 1234567,
                    "prevClose": 199.50,
                    "timestamp": "2026-04-30T16:00:00.000Z",
                }
            ],
        )
        adapter = TiingoAdapter(api_key="test")
        spot = await adapter.get_spot("AAPL", "USD")
        await adapter.aclose()
        assert spot.last == Decimal("199.99")


@pytest.mark.asyncio
async def test_unknown_symbol_empty_list() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/iex/ZZZZ").respond(200, json=[])
        adapter = TiingoAdapter(api_key="test")
        with pytest.raises(SymbolNotFoundError):
            await adapter.get_spot("ZZZZ", "USD")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_get_ohlcv_intraday() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/iex/AAPL/prices").respond(
            200,
            json=[
                {
                    "date": "2026-04-30T15:59:00.000Z",
                    "open": 199,
                    "high": 200,
                    "low": 199,
                    "close": 199.5,
                    "volume": 5000,
                },
                {
                    "date": "2026-04-30T16:00:00.000Z",
                    "open": 199.5,
                    "high": 200.1,
                    "low": 199.3,
                    "close": 200.0,
                    "volume": 6000,
                },
            ],
        )
        adapter = TiingoAdapter(api_key="test")
        candles = await adapter.get_ohlcv("AAPL", Interval.M1, None, 10)
        await adapter.aclose()
        assert len(candles) == 2

"""AlphaVantageAdapter unit tests — respx-mocked HTTP."""

from __future__ import annotations

from decimal import Decimal

import pytest
import respx

from contriwork_market_data import (
    Interval,
    MissingCredentialsError,
    RateLimitedError,
    SymbolNotFoundError,
)
from contriwork_market_data.adapters import AlphaVantageAdapter

_BASE = "https://www.alphavantage.co"


@pytest.mark.asyncio
async def test_global_quote_happy_path() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/query").respond(
            200,
            json={
                "Global Quote": {
                    "01. symbol": "AAPL",
                    "03. high": "200.10",
                    "04. low": "198.50",
                    "05. price": "199.99",
                    "06. volume": "1234567",
                    "08. previous close": "199.50",
                    "10. change percent": "0.24%",
                }
            },
        )
        adapter = AlphaVantageAdapter(api_key="test")
        spot = await adapter.get_spot("AAPL", "USD")
        await adapter.aclose()
        assert spot.last == Decimal("199.99")
        assert spot.change_24h_pct == Decimal("0.24")


@pytest.mark.asyncio
async def test_missing_credentials() -> None:
    adapter = AlphaVantageAdapter()
    with pytest.raises(MissingCredentialsError):
        await adapter.get_spot("AAPL", "USD")
    await adapter.aclose()


@pytest.mark.asyncio
async def test_currency_exchange_rate_path() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/query").respond(
            200,
            json={
                "Realtime Currency Exchange Rate": {
                    "1. From_Currency Code": "BTC",
                    "3. To_Currency Code": "USD",
                    "5. Exchange Rate": "65000.0",
                    "8. Bid Price": "64990.0",
                    "9. Ask Price": "65010.0",
                }
            },
        )
        adapter = AlphaVantageAdapter(api_key="test")
        spot = await adapter.get_spot("BTC", "USD")
        await adapter.aclose()
        assert spot.last == Decimal("65000.0")
        assert spot.bid == Decimal("64990.0")


@pytest.mark.asyncio
async def test_throttle_note_raises_rate_limited() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/query").respond(
            200,
            json={
                "Note": "Thank you for using Alpha Vantage! Our standard API rate limit "
                "is 5 calls per minute."
            },
        )
        adapter = AlphaVantageAdapter(api_key="test")
        with pytest.raises(RateLimitedError):
            await adapter.get_spot("AAPL", "USD")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_unknown_symbol_raises() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/query").respond(200, json={"Global Quote": {}})
        adapter = AlphaVantageAdapter(api_key="test")
        with pytest.raises(SymbolNotFoundError):
            await adapter.get_spot("ZZZZ", "USD")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_get_ohlcv_daily() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/query").respond(
            200,
            json={
                "Meta Data": {"1. Information": "Daily"},
                "Time Series (Daily)": {
                    "2026-04-30": {
                        "1. open": "199.0",
                        "2. high": "200.0",
                        "3. low": "198.0",
                        "4. close": "199.5",
                        "5. volume": "1234567",
                    },
                    "2026-04-29": {
                        "1. open": "198.0",
                        "2. high": "199.0",
                        "3. low": "197.0",
                        "4. close": "198.5",
                        "5. volume": "1100000",
                    },
                },
            },
        )
        adapter = AlphaVantageAdapter(api_key="test")
        candles = await adapter.get_ohlcv("AAPL", Interval.D1, None, 100)
        await adapter.aclose()
        assert len(candles) == 2
        assert candles[0].timestamp < candles[1].timestamp

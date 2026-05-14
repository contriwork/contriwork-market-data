"""KrakenAdapter unit tests — respx-mocked HTTP."""

from __future__ import annotations

from decimal import Decimal

import pytest
import respx

from contriwork_market_data import (
    AdapterFeatureNotSupportedError,
    Interval,
    SymbolNotFoundError,
)
from contriwork_market_data.adapters import KrakenAdapter

_BASE = "https://api.kraken.com"


@pytest.mark.asyncio
async def test_get_spot_happy_path() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/0/public/Ticker").respond(
            200,
            json={
                "error": [],
                "result": {
                    "XXBTZUSD": {
                        "a": ["65010.00", "1", "1.000"],
                        "b": ["64990.00", "1", "1.000"],
                        "c": ["65000.00", "0.50"],
                        "v": ["1.0", "10.0"],
                        "p": ["64500.00", "64750.00"],
                        "t": [5, 100],
                        "l": ["64000.00", "63500.00"],
                        "h": ["66000.00", "66200.00"],
                        "o": "64800.00",
                    }
                },
            },
        )
        adapter = KrakenAdapter()
        spot = await adapter.get_spot("XXBTZUSD", "USD")
        await adapter.aclose()
        assert spot.last == Decimal("65000.00")
        assert spot.high_24h == Decimal("66200.00")
        assert spot.bid == Decimal("64990.00")


@pytest.mark.asyncio
async def test_unknown_pair_raises_symbol_not_found() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/0/public/Ticker").respond(
            200, json={"error": ["EQuery:Unknown asset pair"], "result": {}}
        )
        adapter = KrakenAdapter()
        with pytest.raises(SymbolNotFoundError):
            await adapter.get_spot("NOPENOPE", "USD")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_get_ohlcv_returns_candles() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/0/public/OHLC").respond(
            200,
            json={
                "error": [],
                "result": {
                    "XXBTZUSD": [
                        [
                            1714492800,
                            "65000.0",
                            "65100.0",
                            "64900.0",
                            "65050.0",
                            "65010.0",
                            "10.5",
                            1234,
                        ],
                        [
                            1714492860,
                            "65050.0",
                            "65200.0",
                            "65000.0",
                            "65180.0",
                            "65100.0",
                            "12.0",
                            1500,
                        ],
                    ],
                    "last": 1714492860,
                },
            },
        )
        adapter = KrakenAdapter()
        candles = await adapter.get_ohlcv("XXBTZUSD", Interval.M1, None, 2)
        await adapter.aclose()
        assert len(candles) == 2
        assert candles[0].close == Decimal("65050.0")
        assert candles[0].trade_count == 1234


@pytest.mark.asyncio
async def test_get_order_book_sorted() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/0/public/Depth").respond(
            200,
            json={
                "error": [],
                "result": {
                    "XXBTZUSD": {
                        "bids": [["64999.0", "1.0", 1714492800], ["64998.0", "2.0", 1714492800]],
                        "asks": [["65001.0", "1.0", 1714492800], ["65002.0", "0.5", 1714492800]],
                    }
                },
            },
        )
        adapter = KrakenAdapter()
        book = await adapter.get_order_book("XXBTZUSD", 2)
        await adapter.aclose()
        assert book.bids[0].price == Decimal("64999.0")
        assert book.asks[0].price == Decimal("65001.0")


@pytest.mark.asyncio
async def test_subscribe_ticker_raises_not_supported() -> None:
    adapter = KrakenAdapter()
    with pytest.raises(AdapterFeatureNotSupportedError):
        async for _ in adapter.subscribe_ticker("XXBTZUSD"):
            pass
    await adapter.aclose()

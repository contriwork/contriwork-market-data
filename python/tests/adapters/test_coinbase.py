"""CoinbaseAdapter unit tests — respx-mocked HTTP."""

from __future__ import annotations

from decimal import Decimal

import pytest
import respx

from contriwork_market_data import (
    AdapterFeatureNotSupportedError,
    Interval,
    InvalidIntervalError,
    SymbolNotFoundError,
)
from contriwork_market_data.adapters import CoinbaseAdapter

_BASE = "https://api.exchange.coinbase.com"


@pytest.mark.asyncio
async def test_get_spot_happy_path() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/products/BTC-USD/ticker").respond(
            200,
            json={
                "price": "65000.10",
                "bid": "64999.50",
                "ask": "65000.50",
                "time": "2026-04-30T12:00:00.000Z",
            },
        )
        router.get("/products/BTC-USD/stats").respond(
            200,
            json={"high": "66000", "low": "64000", "volume": "12345.6"},
        )
        adapter = CoinbaseAdapter()
        spot = await adapter.get_spot("BTC-USD", "USD")
        await adapter.aclose()
        assert spot.last == Decimal("65000.10")
        assert spot.high_24h == Decimal("66000")
        assert spot.source_adapter == "coinbase"


@pytest.mark.asyncio
async def test_get_spot_unknown_product_maps_to_symbol_not_found() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/products/ZZZ-USD/ticker").respond(404, json={"message": "not found"})
        adapter = CoinbaseAdapter()
        with pytest.raises(SymbolNotFoundError):
            await adapter.get_spot("ZZZ-USD", "USD")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_get_ohlcv_reverses_to_ascending() -> None:
    async with respx.mock(base_url=_BASE) as router:
        # Coinbase returns descending by time
        router.get("/products/BTC-USD/candles").respond(
            200,
            json=[
                [1714492920, 65000, 65200, 65050, 65180, 12.0],
                [1714492860, 64900, 65100, 65000, 65050, 10.5],
            ],
        )
        adapter = CoinbaseAdapter()
        candles = await adapter.get_ohlcv("BTC-USD", Interval.M1, None, 2)
        await adapter.aclose()
        assert len(candles) == 2
        assert candles[0].timestamp < candles[1].timestamp


@pytest.mark.asyncio
async def test_get_ohlcv_invalid_interval() -> None:
    adapter = CoinbaseAdapter()
    with pytest.raises(InvalidIntervalError):
        await adapter.get_ohlcv("BTC-USD", Interval.W1, None, 10)
    await adapter.aclose()


@pytest.mark.asyncio
async def test_get_order_book_top_levels() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/products/BTC-USD/book").respond(
            200,
            json={
                "sequence": 42,
                "bids": [["64999.0", "1.0", 1], ["64998.0", "2.0", 1]],
                "asks": [["65001.0", "1.0", 1], ["65002.0", "0.5", 1]],
            },
        )
        adapter = CoinbaseAdapter()
        book = await adapter.get_order_book("BTC-USD", 2)
        await adapter.aclose()
        assert book.sequence == 42
        assert book.bids[0].price == Decimal("64999.0")
        assert book.asks[0].price == Decimal("65001.0")


@pytest.mark.asyncio
async def test_subscribe_ticker_raises_not_supported() -> None:
    adapter = CoinbaseAdapter()
    with pytest.raises(AdapterFeatureNotSupportedError):
        async for _ in adapter.subscribe_ticker("BTC-USD"):
            pass
    await adapter.aclose()

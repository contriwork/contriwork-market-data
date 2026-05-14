"""BinancePublicAdapter unit tests — respx-mocked HTTP."""

from __future__ import annotations

from decimal import Decimal

import pytest
import respx

from contriwork_market_data import (
    AdapterFeatureNotSupportedError,
    Interval,
    RateLimitedError,
    SymbolNotFoundError,
)
from contriwork_market_data.adapters import BinancePublicAdapter

_BASE = "https://api.binance.com"


@pytest.mark.asyncio
async def test_get_spot_happy_path() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/api/v3/ticker/24hr").respond(
            200,
            json={
                "symbol": "BTCUSDT",
                "lastPrice": "65000.10",
                "bidPrice": "64999.99",
                "askPrice": "65000.21",
                "highPrice": "66000.00",
                "lowPrice": "64000.00",
                "quoteVolume": "1234567.89",
                "priceChangePercent": "1.45",
                "prevClosePrice": "64999.00",
                "closeTime": 1714492800000,
            },
        )
        adapter = BinancePublicAdapter()
        spot = await adapter.get_spot("BTCUSDT", "USDT")
        await adapter.aclose()
        assert spot.last == Decimal("65000.10")
        assert spot.bid == Decimal("64999.99")
        assert spot.source_adapter == "binance-public"


@pytest.mark.asyncio
async def test_get_spot_unknown_symbol_via_error_envelope() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/api/v3/ticker/24hr").respond(
            200, json={"code": -1121, "msg": "Invalid symbol."}
        )
        adapter = BinancePublicAdapter()
        with pytest.raises(SymbolNotFoundError):
            await adapter.get_spot("ZZZUSDT", "USDT")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_rate_limited_propagates() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/api/v3/ticker/24hr").respond(429, json={"code": -1003})
        adapter = BinancePublicAdapter()
        with pytest.raises(RateLimitedError):
            await adapter.get_spot("BTCUSDT", "USDT")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_get_ohlcv_returns_candles() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/api/v3/klines").respond(
            200,
            json=[
                [
                    1714492800000,
                    "65000",
                    "65100",
                    "64900",
                    "65050",
                    "10.5",
                    1714492859999,
                    "682500",
                    1234,
                    "5.2",
                    "338000",
                    "0",
                ],
                [
                    1714492860000,
                    "65050",
                    "65200",
                    "65000",
                    "65180",
                    "12.0",
                    1714492919999,
                    "780000",
                    1500,
                    "6.0",
                    "390000",
                    "0",
                ],
            ],
        )
        adapter = BinancePublicAdapter()
        candles = await adapter.get_ohlcv("BTCUSDT", Interval.M1, None, 2)
        await adapter.aclose()
        assert len(candles) == 2
        assert candles[0].volume == Decimal("10.5")
        assert candles[0].quote_volume == Decimal("682500")
        assert candles[0].trade_count == 1234


@pytest.mark.asyncio
async def test_get_order_book_sorted() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/api/v3/depth").respond(
            200,
            json={
                "lastUpdateId": 42,
                # Binance returns descending bids and ascending asks already;
                # we still sort defensively in the adapter.
                "bids": [["64999.0", "1.0"], ["64998.0", "2.0"]],
                "asks": [["65001.0", "1.0"], ["65002.0", "0.5"]],
            },
        )
        adapter = BinancePublicAdapter()
        book = await adapter.get_order_book("BTCUSDT", 2)
        await adapter.aclose()
        assert book.sequence == 42
        assert book.bids[0].price > book.bids[1].price
        assert book.asks[0].price < book.asks[1].price


@pytest.mark.asyncio
async def test_subscribe_ticker_raises_not_supported() -> None:
    adapter = BinancePublicAdapter()
    with pytest.raises(AdapterFeatureNotSupportedError):
        async for _ in adapter.subscribe_ticker("BTCUSDT"):
            pass
    await adapter.aclose()

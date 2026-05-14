"""CoinMarketCapAdapter unit tests — respx-mocked HTTP."""

from __future__ import annotations

from decimal import Decimal

import pytest
import respx

from contriwork_market_data import (
    AdapterFeatureNotSupportedError,
    Interval,
    InvalidIntervalError,
    MissingCredentialsError,
    SymbolNotFoundError,
)
from contriwork_market_data.adapters import CoinMarketCapAdapter

_BASE = "https://pro-api.coinmarketcap.com"


@pytest.mark.asyncio
async def test_get_spot_happy_path() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/v2/cryptocurrency/quotes/latest").respond(
            200,
            json={
                "status": {"timestamp": "2026-04-30T12:00:00.000Z"},
                "data": {
                    "BTC": [
                        {
                            "id": 1,
                            "name": "Bitcoin",
                            "symbol": "BTC",
                            "quote": {
                                "USD": {
                                    "price": 65000.12,
                                    "volume_24h": 12345.6,
                                    "percent_change_24h": 1.23,
                                    "market_cap": 1.3e12,
                                    "last_updated": "2026-04-30T12:00:00.000Z",
                                }
                            },
                        }
                    ]
                },
            },
        )
        adapter = CoinMarketCapAdapter(api_key="test-key")
        spot = await adapter.get_spot("BTC", "USD")
        await adapter.aclose()
        assert spot.last == Decimal("65000.12")
        assert spot.change_24h_pct == Decimal("1.23")


@pytest.mark.asyncio
async def test_missing_credentials_raises_on_first_call() -> None:
    adapter = CoinMarketCapAdapter()
    with pytest.raises(MissingCredentialsError):
        await adapter.get_spot("BTC", "USD")
    await adapter.aclose()


@pytest.mark.asyncio
async def test_get_spot_unknown_symbol() -> None:
    async with respx.mock(base_url=_BASE) as router:
        router.get("/v2/cryptocurrency/quotes/latest").respond(200, json={"data": {}})
        adapter = CoinMarketCapAdapter(api_key="test-key")
        with pytest.raises(SymbolNotFoundError):
            await adapter.get_spot("ZZZ", "USD")
        await adapter.aclose()


@pytest.mark.asyncio
async def test_get_ohlcv_paid_tier_raises() -> None:
    adapter = CoinMarketCapAdapter(api_key="test-key")
    with pytest.raises(InvalidIntervalError):
        await adapter.get_ohlcv("BTC", Interval.D1, None, 30)
    await adapter.aclose()


@pytest.mark.asyncio
async def test_get_order_book_not_supported() -> None:
    adapter = CoinMarketCapAdapter(api_key="test-key")
    with pytest.raises(AdapterFeatureNotSupportedError):
        await adapter.get_order_book("BTC", 10)
    await adapter.aclose()


@pytest.mark.asyncio
async def test_subscribe_ticker_raises_not_supported() -> None:
    adapter = CoinMarketCapAdapter(api_key="test-key")
    with pytest.raises(AdapterFeatureNotSupportedError):
        async for _ in adapter.subscribe_ticker("BTC"):
            pass
    await adapter.aclose()


@pytest.mark.asyncio
async def test_api_key_provider_overrides_static_key() -> None:
    """When both api_key and api_key_provider are set, provider wins."""
    captured: dict[str, str] = {}

    async def provider() -> str | None:
        return "provider-key"

    import httpx

    async with respx.mock(base_url=_BASE) as router:

        def _capture(request: httpx.Request) -> httpx.Response:
            captured["header"] = request.headers.get("X-CMC_PRO_API_KEY", "")
            return httpx.Response(
                200,
                json={
                    "data": {
                        "BTC": [
                            {
                                "id": 1,
                                "quote": {
                                    "USD": {
                                        "price": 65000.0,
                                        "last_updated": "2026-04-30T12:00:00.000Z",
                                    }
                                },
                            }
                        ]
                    }
                },
            )

        router.get("/v2/cryptocurrency/quotes/latest").mock(side_effect=_capture)
        adapter = CoinMarketCapAdapter(api_key="static-key", api_key_provider=provider)
        await adapter.get_spot("BTC", "USD")
        await adapter.aclose()
    assert captured["header"] == "provider-key"

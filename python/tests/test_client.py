"""Focused unit tests for MarketDataClient orchestration."""

from __future__ import annotations

import pytest

from contriwork_market_data import (
    AdapterRegistry,
    AllAdaptersFailedError,
    ClientConfig,
    InvalidInputError,
    MarketDataClient,
    NoAdapterForMarketError,
)
from contriwork_market_data._clock import ManualClock
from contriwork_market_data.adapters import InMemoryAdapter, InMemoryFailMode


def _client(
    adapters: dict[str, list],
    *,
    config: ClientConfig | None = None,
    clock: ManualClock | None = None,
) -> MarketDataClient:
    return MarketDataClient(
        registry=AdapterRegistry(adapters),
        config=config or ClientConfig.defaults(),
        clock=clock or ManualClock(),
    )


@pytest.mark.asyncio
async def test_get_spot_validates_symbol() -> None:
    client = _client({"crypto": [InMemoryAdapter(adapter_id="p", data={})]})
    with pytest.raises(InvalidInputError):
        await client.get_spot("", "crypto", "USDT")
    with pytest.raises(InvalidInputError):
        await client.get_spot("X" * 65, "crypto", "USDT")
    with pytest.raises(InvalidInputError):
        await client.get_spot("OK", "crypto", "U")  # quote too short


@pytest.mark.asyncio
async def test_no_adapter_for_market() -> None:
    client = _client({"crypto": [InMemoryAdapter(adapter_id="p")]})
    with pytest.raises(NoAdapterForMarketError):
        await client.get_spot("AAPL", "stocks_us")


@pytest.mark.asyncio
async def test_fallback_returns_secondary_on_primary_failure() -> None:
    primary = InMemoryAdapter(
        adapter_id="primary",
        data={},
        fail_modes=[InMemoryFailMode(symbol="ETHUSDT", code="SYMBOL_NOT_FOUND")],
    )
    secondary = InMemoryAdapter(
        adapter_id="secondary",
        data={"ETHUSDT": {"spot": {"last": "3500", "quote_currency": "USDT"}}},
    )
    client = _client({"crypto": [primary, secondary]})
    spot = await client.get_spot("ETHUSDT", "crypto", "USDT")
    assert spot.source_adapter == "secondary"


@pytest.mark.asyncio
async def test_all_adapters_fail_aggregates_causes() -> None:
    a1 = InMemoryAdapter(
        adapter_id="a1",
        data={},
        fail_modes=[InMemoryFailMode(symbol="X", code="ADAPTER_UNAVAILABLE")],
    )
    a2 = InMemoryAdapter(
        adapter_id="a2",
        data={},
        fail_modes=[InMemoryFailMode(symbol="X", code="ADAPTER_UNAVAILABLE")],
    )
    client = _client({"crypto": [a1, a2]})
    with pytest.raises(AllAdaptersFailedError) as info:
        await client.get_spot("X", "crypto", "USD")
    assert len(info.value.cause_list) == 2

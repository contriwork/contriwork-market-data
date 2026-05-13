"""Tests for the polling-emulation streaming helper."""

from __future__ import annotations

from decimal import Decimal

import pytest

from contriwork_market_data import StreamDisconnectedError
from contriwork_market_data._clock import ManualClock
from contriwork_market_data._streaming import polling_ticker_iterator
from contriwork_market_data.adapters import InMemoryAdapter, InMemoryFailMode


@pytest.mark.asyncio
async def test_polling_emulation_yields_three_tickers() -> None:
    clock = ManualClock()
    adapter = InMemoryAdapter(
        adapter_id="p",
        data={"BTCUSDT": {"spot": {"last": "65000", "quote_currency": "USDT"}}},
        clock=clock,
    )
    collected = []
    async for ticker in polling_ticker_iterator(
        adapter,
        symbol="BTCUSDT",
        quote_currency="USDT",
        polling_interval_s=1.0,
        clock=clock,
    ):
        collected.append(ticker)
        if len(collected) == 3:
            break
    assert len(collected) == 3
    assert all(t.source_adapter == "p" for t in collected)
    assert collected[0].price == Decimal("65000")


@pytest.mark.asyncio
async def test_polling_emulation_raises_after_consecutive_failures() -> None:
    clock = ManualClock()
    adapter = InMemoryAdapter(
        adapter_id="p",
        data={"BTCUSDT": {"spot": {"last": "65000", "quote_currency": "USDT"}}},
        fail_modes=[InMemoryFailMode(symbol="BTCUSDT", code="ADAPTER_UNAVAILABLE")],
        clock=clock,
    )
    with pytest.raises(StreamDisconnectedError):
        async for _ in polling_ticker_iterator(
            adapter,
            symbol="BTCUSDT",
            quote_currency="USDT",
            polling_interval_s=0.1,
            clock=clock,
            max_consecutive_failures=2,
        ):
            pass

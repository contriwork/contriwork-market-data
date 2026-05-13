"""Tests for the public data types."""

from __future__ import annotations

from datetime import UTC, datetime
from decimal import Decimal

import pytest

from contriwork_market_data import (
    BookLevel,
    Candle,
    Capability,
    Interval,
    OrderBook,
    SpotPrice,
    Ticker,
)


def test_interval_values_are_invariant() -> None:
    # Names + values are CONTRACT v1 invariants — any change is a major bump.
    assert [i.value for i in Interval] == [
        "M1",
        "M5",
        "M15",
        "M30",
        "H1",
        "H4",
        "D1",
        "W1",
        "MN1",
    ]


def test_spot_price_is_frozen() -> None:
    s = SpotPrice(
        symbol="BTCUSDT",
        last=Decimal("65000"),
        quote_currency="USDT",
        timestamp=datetime.now(UTC),
        source_adapter="test",
    )
    with pytest.raises(AttributeError):
        s.last = Decimal("70000")  # type: ignore[misc]


def test_spot_price_extra_defaults_to_empty_mappingproxy() -> None:
    s = SpotPrice(
        symbol="X",
        last=Decimal("1"),
        quote_currency="USD",
        timestamp=datetime.now(UTC),
        source_adapter="t",
    )
    assert dict(s.extra) == {}
    # Read-only: cannot mutate the proxy in place.
    with pytest.raises(TypeError):
        s.extra["custom"] = "value"  # type: ignore[index]


def test_order_book_levels() -> None:
    book = OrderBook(
        symbol="X",
        bids=(BookLevel(price=Decimal("99"), size=Decimal("1")),),
        asks=(BookLevel(price=Decimal("101"), size=Decimal("1")),),
        timestamp=datetime.now(UTC),
        source_adapter="t",
    )
    assert book.bids[0].price == Decimal("99")
    assert book.asks[0].size == Decimal("1")


def test_ticker_optional_side_default_none() -> None:
    t = Ticker(
        symbol="X",
        price=Decimal("1"),
        quote_currency="USD",
        timestamp=datetime.now(UTC),
        source_adapter="t",
    )
    assert t.side is None


def test_candle_quote_volume_optional() -> None:
    c = Candle(
        timestamp=datetime.now(UTC),
        open=Decimal("1"),
        high=Decimal("2"),
        low=Decimal("1"),
        close=Decimal("2"),
        volume=Decimal("10"),
    )
    assert c.quote_volume is None
    assert c.trade_count is None


def test_capability_supports_any_quote_marker() -> None:
    cap = Capability(
        supported_markets=("crypto",),
        supported_intervals=(Interval.M1,),
        supported_quote_currencies="ANY",
        supports_order_book=False,
        supports_native_streaming=False,
        rate_limit_per_minute=60,
        requires_auth=False,
    )
    assert cap.supported_quote_currencies == "ANY"

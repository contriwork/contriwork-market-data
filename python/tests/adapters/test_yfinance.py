"""YFinanceAdapter unit tests — mock yfinance.Ticker; no scraping in CI."""

from __future__ import annotations

from datetime import UTC, datetime
from decimal import Decimal
from unittest.mock import MagicMock, patch

import pytest

from contriwork_market_data import (
    AdapterFeatureNotSupportedError,
    Interval,
    SymbolNotFoundError,
)
from contriwork_market_data.adapters.yfinance_adapter import YFinanceAdapter


def _mock_ticker_with_fast_info(fast_info: dict[str, object]) -> MagicMock:
    ticker = MagicMock()
    ticker.fast_info = fast_info
    return ticker


def _mock_ticker_with_history(rows: list[dict[str, object]]) -> MagicMock:
    """Build a mock yfinance.Ticker.history() return value.

    yfinance returns a pandas DataFrame; we mimic the iterrows interface
    with a small fake object so the adapter doesn't need pandas as a hard
    dependency.
    """

    class _FakeRow(dict):  # type: ignore[type-arg]
        def get(self, key, default=None):  # type: ignore[override]
            return super().get(key, default)

    class _FakeFrame:
        def __init__(self, rows: list[dict[str, object]]) -> None:
            self._rows = rows

        def __len__(self) -> int:
            return len(self._rows)

        def iterrows(self):
            for r in self._rows:
                yield r["__ts__"], _FakeRow(r)

    ticker = MagicMock()
    ticker.history.return_value = _FakeFrame(rows)
    return ticker


@pytest.mark.asyncio
async def test_get_spot_happy_path() -> None:
    mock_ticker = _mock_ticker_with_fast_info(
        {"lastPrice": 199.99, "dayHigh": 200.0, "dayLow": 198.5}
    )
    with patch("yfinance.Ticker", return_value=mock_ticker):
        adapter = YFinanceAdapter()
        spot = await adapter.get_spot("AAPL", "USD")
    assert spot.last == Decimal("199.99")
    assert spot.high_24h == Decimal("200.0")


@pytest.mark.asyncio
async def test_get_spot_no_price_raises_symbol_not_found() -> None:
    mock_ticker = _mock_ticker_with_fast_info({"lastPrice": 0})
    with patch("yfinance.Ticker", return_value=mock_ticker):
        adapter = YFinanceAdapter()
        with pytest.raises(SymbolNotFoundError):
            await adapter.get_spot("ZZZZ", "USD")


@pytest.mark.asyncio
async def test_get_ohlcv() -> None:
    rows = [
        {
            "__ts__": datetime(2026, 4, 30, 12, 0, tzinfo=UTC),
            "Open": 199.0,
            "High": 200.0,
            "Low": 198.0,
            "Close": 199.5,
            "Volume": 100,
        },
        {
            "__ts__": datetime(2026, 4, 30, 12, 1, tzinfo=UTC),
            "Open": 199.5,
            "High": 200.5,
            "Low": 199.0,
            "Close": 200.0,
            "Volume": 200,
        },
    ]
    mock_ticker = _mock_ticker_with_history(rows)
    with patch("yfinance.Ticker", return_value=mock_ticker):
        adapter = YFinanceAdapter()
        candles = await adapter.get_ohlcv("AAPL", Interval.M1, None, 10)
    assert len(candles) == 2
    assert candles[0].close == Decimal("199.5")


@pytest.mark.asyncio
async def test_order_book_not_supported() -> None:
    adapter = YFinanceAdapter()
    with pytest.raises(AdapterFeatureNotSupportedError):
        await adapter.get_order_book("AAPL", 10)


@pytest.mark.asyncio
async def test_supported_markets_include_bist_and_globals() -> None:
    adapter = YFinanceAdapter()
    assert "stocks_tr" in adapter.capability.supported_markets
    assert "stocks_global" in adapter.capability.supported_markets
    assert "commodities" in adapter.capability.supported_markets
    assert "indices" in adapter.capability.supported_markets

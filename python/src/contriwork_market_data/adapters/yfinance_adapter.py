"""YFinance adapter — Python-only opt-in (see SCOPE.md §2.2).

Wraps the ``yfinance`` library so callers covering Borsa Istanbul
(``AKBNK.IS``), global stocks, indices, commodities, or anything else
the unofficial Yahoo Finance scraper supports get a uniform port.

Installation::

    pip install "contriwork-market-data[yfinance]"

``yfinance`` is intentionally NOT a hard runtime dependency — keeping the
core package import-light. Constructing :class:`YFinanceAdapter` without
the optional dependency raises a clear ImportError.
"""

from __future__ import annotations

import asyncio
from collections.abc import AsyncIterator
from datetime import UTC, datetime
from decimal import Decimal
from typing import Any

from .._adapter_helpers import streaming_not_supported
from ..errors import (
    AdapterFeatureNotSupportedError,
    AdapterUnavailableError,
    InvalidIntervalError,
    SymbolNotFoundError,
)
from ..types import (
    Candle,
    Capability,
    Interval,
    OrderBook,
    SpotPrice,
    Ticker,
)

__all__ = ["YFinanceAdapter"]

_RATE_LIMIT_PER_MINUTE = 60  # yfinance has no documented limit; conservative

_INTERVAL_MAP: dict[Interval, str] = {
    Interval.M1: "1m",
    Interval.M5: "5m",
    Interval.M15: "15m",
    Interval.M30: "30m",
    Interval.H1: "60m",
    Interval.D1: "1d",
    Interval.W1: "1wk",
    Interval.MN1: "1mo",
}

_SUPPORTED_INTERVALS: tuple[Interval, ...] = tuple(_INTERVAL_MAP.keys())


class YFinanceAdapter:
    adapter_id = "yfinance"

    def __init__(self) -> None:
        try:
            import yfinance  # noqa: F401  (probe only)
        except ImportError as exc:
            raise ImportError(
                "yfinance is not installed. Install the optional extra with:\n"
                "  pip install 'contriwork-market-data[yfinance]'"
            ) from exc
        self.capability = Capability(
            supported_markets=(
                "stocks_us",
                "stocks_tr",
                "stocks_eu",
                "stocks_global",
                "commodities",
                "indices",
            ),
            supported_intervals=_SUPPORTED_INTERVALS,
            supported_quote_currencies="ANY",
            supports_order_book=False,
            supports_native_streaming=False,
            rate_limit_per_minute=_RATE_LIMIT_PER_MINUTE,
            requires_auth=False,
        )

    async def aclose(self) -> None:  # pragma: no cover - parity with REST adapters
        return None

    async def get_spot(self, symbol: str, quote_currency: str) -> SpotPrice:
        info = await asyncio.to_thread(_yf_fast_info, symbol)
        if not info or info.get("last_price") in (None, 0):
            raise SymbolNotFoundError(
                f"yfinance returned no price for {symbol!r}",
                adapter_id=self.adapter_id,
            )
        return SpotPrice(
            symbol=symbol,
            last=Decimal(str(info["last_price"])),
            quote_currency=quote_currency,
            timestamp=datetime.now(UTC),
            source_adapter=self.adapter_id,
            high_24h=_opt_decimal(info.get("day_high")),
            low_24h=_opt_decimal(info.get("day_low")),
            volume_24h=_opt_decimal(info.get("last_volume")),
            previous_close=_opt_decimal(info.get("previous_close")),
            market_cap=_opt_decimal(info.get("market_cap")),
        )

    async def get_ohlcv(
        self,
        symbol: str,
        interval: Interval,
        since: datetime | None,
        limit: int,
    ) -> list[Candle]:
        if interval not in _SUPPORTED_INTERVALS:
            raise InvalidIntervalError(
                f"yfinance does not support interval {interval.value}",
                adapter_id=self.adapter_id,
            )
        rows = await asyncio.to_thread(_yf_history, symbol, _INTERVAL_MAP[interval], since)
        if not rows:
            raise SymbolNotFoundError(
                f"yfinance returned no candles for {symbol!r}/{interval.value}",
                adapter_id=self.adapter_id,
            )
        candles: list[Candle] = []
        for row in rows[:limit]:
            candles.append(
                Candle(
                    timestamp=row["timestamp"],
                    open=Decimal(str(row["open"])),
                    high=Decimal(str(row["high"])),
                    low=Decimal(str(row["low"])),
                    close=Decimal(str(row["close"])),
                    volume=Decimal(str(row.get("volume", 0))),
                )
            )
        return candles

    async def get_order_book(self, symbol: str, depth: int) -> OrderBook:
        raise AdapterFeatureNotSupportedError(
            "yfinance does not expose order book",
            adapter_id=self.adapter_id,
        )

    def subscribe_ticker(self, symbol: str) -> AsyncIterator[Ticker]:
        return streaming_not_supported(self.adapter_id)


def _opt_decimal(value: Any) -> Decimal | None:
    if value is None or value == "":
        return None
    try:
        return Decimal(str(value))
    except (ValueError, ArithmeticError):
        return None


def _yf_fast_info(symbol: str) -> dict[str, Any]:
    """Sync helper — runs inside ``asyncio.to_thread``."""
    import yfinance

    ticker = yfinance.Ticker(symbol)
    try:
        fi = ticker.fast_info
        return {
            "last_price": fi.get("lastPrice") or fi.get("last_price"),
            "day_high": fi.get("dayHigh") or fi.get("day_high"),
            "day_low": fi.get("dayLow") or fi.get("day_low"),
            "last_volume": fi.get("lastVolume") or fi.get("last_volume"),
            "previous_close": fi.get("previousClose") or fi.get("previous_close"),
            "market_cap": fi.get("marketCap") or fi.get("market_cap"),
        }
    except Exception as exc:
        raise AdapterUnavailableError(
            f"yfinance fast_info failed for {symbol!r}: {exc.__class__.__name__}",
            adapter_id="yfinance",
        ) from exc


def _yf_history(symbol: str, interval: str, since: datetime | None) -> list[dict[str, Any]]:
    """Sync helper — runs inside ``asyncio.to_thread``."""
    import yfinance

    ticker = yfinance.Ticker(symbol)
    try:
        kwargs: dict[str, Any] = {"interval": interval}
        if since is not None:
            kwargs["start"] = since.strftime("%Y-%m-%d")
        else:
            kwargs["period"] = "1mo"
        df = ticker.history(**kwargs)
    except Exception as exc:
        raise AdapterUnavailableError(
            f"yfinance history failed for {symbol!r}: {exc.__class__.__name__}",
            adapter_id="yfinance",
        ) from exc
    if df is None or len(df) == 0:
        return []
    rows: list[dict[str, Any]] = []
    for ts, row in df.iterrows():
        ts_py: datetime = ts.to_pydatetime() if hasattr(ts, "to_pydatetime") else ts
        if ts_py.tzinfo is None:
            ts_py = ts_py.replace(tzinfo=UTC)
        rows.append(
            {
                "timestamp": ts_py,
                "open": row["Open"],
                "high": row["High"],
                "low": row["Low"],
                "close": row["Close"],
                "volume": row.get("Volume", 0),
            }
        )
    return rows

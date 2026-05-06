"""contriwork-market-data — Python adapter.

Public surface re-exports from :mod:`contriwork_market_data.port`. Do not
import concrete adapter classes from outside — they are internal detail.
"""

from __future__ import annotations

from importlib.metadata import PackageNotFoundError, version

from .port import MarketDataPort

__all__ = ["MarketDataPort", "__version__"]

try:
    __version__ = version("contriwork-market-data")
except PackageNotFoundError:
    __version__ = "0.0.0"

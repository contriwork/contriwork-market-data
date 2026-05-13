"""contriwork-market-data — Python adapter.

Public surface — see ``CONTRACT.md`` for the language-agnostic specification
and ``docs/SCOPE.md`` for v0.1.0 rationale. Concrete provider adapters
(CoinGecko, Binance, …) ship in PR 3 / PR 4 and are wired by the caller
into an :class:`AdapterRegistry` passed to :class:`MarketDataClient`.
"""

from __future__ import annotations

from importlib.metadata import PackageNotFoundError, version

from ._adapter import Adapter
from ._registry import AdapterRegistry
from .client import MarketDataClient
from .config import (
    CacheConfig,
    ClientConfig,
    RateLimitConfig,
    RateLimitStrategy,
    StreamingConfig,
)
from .errors import (
    AdapterFeatureNotSupportedError,
    AdapterUnavailableError,
    AllAdaptersFailedError,
    InvalidInputError,
    InvalidIntervalError,
    MarketDataError,
    MissingCredentialsError,
    NoAdapterForMarketError,
    RateLimitedError,
    StreamDisconnectedError,
    StreamingNotSupportedError,
    SymbolNotFoundError,
    UnsupportedQuoteCurrencyError,
    error_for_code,
)
from .port import MarketDataPort
from .types import (
    BookLevel,
    Candle,
    Capability,
    Interval,
    OrderBook,
    SpotPrice,
    Ticker,
    TickerSide,
)

__all__ = [
    "Adapter",
    "AdapterFeatureNotSupportedError",
    "AdapterRegistry",
    "AdapterUnavailableError",
    "AllAdaptersFailedError",
    "BookLevel",
    "CacheConfig",
    "Candle",
    "Capability",
    "ClientConfig",
    "Interval",
    "InvalidInputError",
    "InvalidIntervalError",
    "MarketDataClient",
    "MarketDataError",
    "MarketDataPort",
    "MissingCredentialsError",
    "NoAdapterForMarketError",
    "OrderBook",
    "RateLimitConfig",
    "RateLimitStrategy",
    "RateLimitedError",
    "SpotPrice",
    "StreamDisconnectedError",
    "StreamingConfig",
    "StreamingNotSupportedError",
    "SymbolNotFoundError",
    "Ticker",
    "TickerSide",
    "UnsupportedQuoteCurrencyError",
    "__version__",
    "error_for_code",
]

try:
    __version__ = version("contriwork-market-data")
except PackageNotFoundError:
    __version__ = "0.0.0"

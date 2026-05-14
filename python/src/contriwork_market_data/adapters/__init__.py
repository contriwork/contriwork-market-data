"""Concrete adapter implementations.

The v0.1.0 set covers the five public-crypto providers shipped in this PR
(plus the InMemoryAdapter from PR 2). Stocks adapters land in PR 4.
"""

from __future__ import annotations

from .binance_public import BinancePublicAdapter
from .coinbase import CoinbaseAdapter
from .coingecko import CoinGeckoAdapter
from .coinmarketcap import CoinMarketCapAdapter
from .in_memory import InMemoryAdapter, InMemoryFailMode
from .kraken import KrakenAdapter

__all__ = [
    "BinancePublicAdapter",
    "CoinGeckoAdapter",
    "CoinMarketCapAdapter",
    "CoinbaseAdapter",
    "InMemoryAdapter",
    "InMemoryFailMode",
    "KrakenAdapter",
]

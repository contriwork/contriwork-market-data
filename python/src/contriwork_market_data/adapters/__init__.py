"""Concrete adapter implementations.

v0.1.0 ships eleven providers plus the InMemoryAdapter reference fixture.
Crypto adapters arrived in PR 3, stocks adapters arrive in this PR.
"""

from __future__ import annotations

from .alpha_vantage import AlphaVantageAdapter
from .binance_public import BinancePublicAdapter
from .coinbase import CoinbaseAdapter
from .coingecko import CoinGeckoAdapter
from .coinmarketcap import CoinMarketCapAdapter
from .finnhub import FinnhubAdapter
from .iex_cloud import IEXCloudAdapter
from .in_memory import InMemoryAdapter, InMemoryFailMode
from .kraken import KrakenAdapter
from .polygon_io import PolygonIOAdapter
from .tiingo import TiingoAdapter

__all__ = [
    "AlphaVantageAdapter",
    "BinancePublicAdapter",
    "CoinGeckoAdapter",
    "CoinMarketCapAdapter",
    "CoinbaseAdapter",
    "FinnhubAdapter",
    "IEXCloudAdapter",
    "InMemoryAdapter",
    "InMemoryFailMode",
    "KrakenAdapter",
    "PolygonIOAdapter",
    "TiingoAdapter",
]

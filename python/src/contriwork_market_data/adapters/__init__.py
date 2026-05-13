"""Concrete adapter implementations.

Only :class:`InMemoryAdapter` ships in PR 2; provider adapters
(CoinGecko, Binance, …) arrive in PR 3 (crypto) and PR 4 (stocks).
"""

from __future__ import annotations

from .in_memory import InMemoryAdapter, InMemoryFailMode

__all__ = ["InMemoryAdapter", "InMemoryFailMode"]

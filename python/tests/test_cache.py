"""Tests for the internal TTL cache."""

from __future__ import annotations

import pytest

from contriwork_market_data._cache import TTLCache
from contriwork_market_data._clock import ManualClock


def test_cache_hit_within_ttl() -> None:
    clock = ManualClock()
    cache: TTLCache[str] = TTLCache(max_entries=4, clock=clock)
    cache.set(("a",), "alpha", ttl_s=5)
    clock.advance(3)
    assert cache.get(("a",)) == "alpha"


def test_cache_miss_after_ttl() -> None:
    clock = ManualClock()
    cache: TTLCache[str] = TTLCache(max_entries=4, clock=clock)
    cache.set(("a",), "alpha", ttl_s=5)
    clock.advance(6)
    assert cache.get(("a",)) is None
    assert len(cache) == 0


def test_cache_lru_eviction() -> None:
    clock = ManualClock()
    cache: TTLCache[str] = TTLCache(max_entries=2, clock=clock)
    cache.set(("a",), "1", ttl_s=100)
    cache.set(("b",), "2", ttl_s=100)
    cache.get(("a",))  # promote a
    cache.set(("c",), "3", ttl_s=100)  # evicts b (least recent)
    assert cache.get(("a",)) == "1"
    assert cache.get(("b",)) is None
    assert cache.get(("c",)) == "3"


def test_cache_does_not_store_nonpositive_ttl() -> None:
    clock = ManualClock()
    cache: TTLCache[str] = TTLCache(max_entries=4, clock=clock)
    cache.set(("a",), "alpha", ttl_s=0)
    assert cache.get(("a",)) is None
    cache.set(("b",), "beta", ttl_s=-1)
    assert cache.get(("b",)) is None


def test_cache_rejects_invalid_max_entries() -> None:
    with pytest.raises(ValueError):
        TTLCache(max_entries=0, clock=ManualClock())  # type: ignore[arg-type]

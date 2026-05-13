"""TTL cache with LRU eviction — CONTRACT.md §6.

Generic over the cached value type. Eviction is purely LRU once
``max_entries`` is reached; entries whose TTL has expired are surfaced as a
miss and lazily removed on access. The cache is not thread-safe — coroutines
sharing one cache must run on the same event loop.
"""

from __future__ import annotations

from collections import OrderedDict
from typing import Any, TypeVar

from ._clock import Clock

__all__ = ["TTLCache"]

T = TypeVar("T")
Key = tuple[Any, ...]


class TTLCache[T]:
    def __init__(self, *, max_entries: int, clock: Clock) -> None:
        if max_entries < 1:
            raise ValueError("max_entries must be >= 1")
        self._max_entries = max_entries
        self._clock = clock
        self._store: OrderedDict[Key, tuple[T, float]] = OrderedDict()

    def get(self, key: Key) -> T | None:
        entry = self._store.get(key)
        if entry is None:
            return None
        value, expires_at = entry
        if self._clock.monotonic() >= expires_at:
            self._store.pop(key, None)
            return None
        self._store.move_to_end(key)
        return value

    def set(self, key: Key, value: T, *, ttl_s: float) -> None:
        if ttl_s <= 0:
            # CONTRACT §6: TTL <= 0 means do not cache.
            return
        expires_at = self._clock.monotonic() + ttl_s
        if key in self._store:
            self._store.move_to_end(key)
        else:
            while len(self._store) >= self._max_entries:
                self._store.popitem(last=False)
        self._store[key] = (value, expires_at)

    def clear(self) -> None:
        self._store.clear()

    def __len__(self) -> int:
        return len(self._store)

    def __contains__(self, key: object) -> bool:
        if not isinstance(key, tuple):
            return False
        return self.get(key) is not None

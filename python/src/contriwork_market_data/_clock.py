"""Clock abstraction — injectable for deterministic tests.

The public surface never exposes this; it is wired internally by
:class:`MarketDataClient` and used by cache, rate limiter, and streaming
emulation. Tests substitute a ``ManualClock`` so cache TTLs and backoff
delays are reproducible without real ``asyncio.sleep`` waits.
"""

from __future__ import annotations

import asyncio
import time
from datetime import UTC, datetime
from typing import Protocol, runtime_checkable

__all__ = ["Clock", "ManualClock", "SystemClock"]


@runtime_checkable
class Clock(Protocol):
    def now(self) -> datetime: ...

    def monotonic(self) -> float: ...

    async def sleep(self, seconds: float) -> None: ...


class SystemClock:
    """Wall-clock implementation backed by stdlib ``time``/``asyncio``."""

    def now(self) -> datetime:
        return datetime.now(UTC)

    def monotonic(self) -> float:
        return time.monotonic()

    async def sleep(self, seconds: float) -> None:
        if seconds > 0:
            await asyncio.sleep(seconds)


class ManualClock:
    """Test clock — caller advances time explicitly.

    ``sleep`` does not actually wait; it advances the manual clock by the
    requested amount and yields once via ``asyncio.sleep(0)`` so other
    coroutines may run. Production code never instantiates this.
    """

    def __init__(self, *, epoch_seconds: float = 0.0) -> None:
        self._monotonic = float(epoch_seconds)
        self._epoch_now = datetime.fromtimestamp(epoch_seconds, tz=UTC)

    def now(self) -> datetime:
        return self._epoch_now

    def monotonic(self) -> float:
        return self._monotonic

    def advance(self, seconds: float) -> None:
        if seconds < 0:
            raise ValueError("advance amount must be >= 0")
        self._monotonic += seconds
        # ``datetime`` is intentionally not slewed alongside monotonic so a
        # test can decide independently. Use ``set_now`` to update wall time.

    def set_now(self, value: datetime) -> None:
        if value.tzinfo is None:
            raise ValueError("ManualClock.set_now requires a timezone-aware datetime")
        self._epoch_now = value

    async def sleep(self, seconds: float) -> None:
        self.advance(max(0.0, seconds))
        await asyncio.sleep(0)

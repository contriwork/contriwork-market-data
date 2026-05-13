"""Per-adapter token-bucket rate limiter + retry — CONTRACT.md §6.

The orchestrator owns one :class:`TokenBucket` per adapter (capacity =
``Capability.rate_limit_per_minute``). Operations queue on the bucket and
retry with exponential backoff when an adapter raises
:class:`~contriwork_market_data.errors.RateLimitedError`.
"""

from __future__ import annotations

import secrets
from collections.abc import Awaitable, Callable
from typing import TypeVar

from ._clock import Clock
from .config import RateLimitConfig
from .errors import RateLimitedError

__all__ = ["TokenBucket", "run_with_retry"]

T = TypeVar("T")


class TokenBucket:
    """Classic refill token bucket; not thread-safe."""

    def __init__(self, *, capacity: int, refill_per_second: float, clock: Clock) -> None:
        if capacity < 1:
            raise ValueError("capacity must be >= 1")
        if refill_per_second < 0:
            raise ValueError("refill_per_second must be >= 0")
        self._capacity = capacity
        self._refill = refill_per_second
        self._tokens = float(capacity)
        self._last = clock.monotonic()
        self._clock = clock

    def _refill_tokens(self) -> None:
        now = self._clock.monotonic()
        elapsed = max(0.0, now - self._last)
        self._tokens = min(float(self._capacity), self._tokens + elapsed * self._refill)
        self._last = now

    def try_acquire(self, tokens: int = 1) -> bool:
        self._refill_tokens()
        if self._tokens + 1e-9 >= tokens:
            self._tokens -= tokens
            return True
        return False

    def time_until_available(self, tokens: int = 1) -> float:
        self._refill_tokens()
        deficit = tokens - self._tokens
        if deficit <= 0:
            return 0.0
        if self._refill <= 0:
            # No refill: the bucket never recovers; treat as infinite wait.
            return float("inf")
        return deficit / self._refill


async def run_with_retry[T](
    fn: Callable[[], Awaitable[T]],
    *,
    config: RateLimitConfig,
    clock: Clock,
    bucket: TokenBucket | None = None,
) -> T:
    """Invoke ``fn`` with rate-limit-aware retry.

    Behavior:
    - If ``bucket`` is provided, wait for a token before each attempt.
    - On :class:`RateLimitedError`, sleep with jittered exponential backoff
      and retry, up to ``config.max_retry_attempts`` extra attempts.
    - Other exceptions propagate immediately.
    """
    attempts = 0
    backoff_s = max(0.0, config.initial_backoff_s)
    while True:
        if bucket is not None:
            wait_s = bucket.time_until_available()
            if wait_s > 0:
                # Inf would block forever; cap defensively at max_backoff_s.
                if wait_s == float("inf"):
                    wait_s = config.max_backoff_s
                await clock.sleep(wait_s)
            bucket.try_acquire()
        try:
            return await fn()
        except RateLimitedError:
            if attempts >= config.max_retry_attempts:
                raise
            attempts += 1
            sleep_s = min(backoff_s, config.max_backoff_s)
            if config.jitter:
                # ``secrets.SystemRandom`` is overkill for jitter but the
                # plain ``random`` module is flagged by bandit (S311) for
                # crypto use. ``SystemRandom`` keeps lint clean and the cost
                # is irrelevant compared to network latency.
                sleep_s *= 0.5 + secrets.SystemRandom().random()
            await clock.sleep(sleep_s)
            backoff_s = min(config.max_backoff_s, max(backoff_s, 0.001) * 2.0)

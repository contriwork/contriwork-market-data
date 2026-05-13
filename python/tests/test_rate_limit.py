"""Tests for the token-bucket rate limiter + retry runner."""

from __future__ import annotations

import pytest

from contriwork_market_data import RateLimitConfig, RateLimitedError
from contriwork_market_data._clock import ManualClock
from contriwork_market_data._rate_limit import TokenBucket, run_with_retry


def test_token_bucket_drains_and_refills() -> None:
    clock = ManualClock()
    bucket = TokenBucket(capacity=2, refill_per_second=1.0, clock=clock)
    assert bucket.try_acquire()
    assert bucket.try_acquire()
    assert not bucket.try_acquire()
    clock.advance(1.5)
    # 1.5 tokens generated → at least one acquire succeeds again.
    assert bucket.try_acquire()


def test_time_until_available() -> None:
    clock = ManualClock()
    bucket = TokenBucket(capacity=1, refill_per_second=2.0, clock=clock)
    bucket.try_acquire()
    wait = bucket.time_until_available()
    assert wait == pytest.approx(0.5, rel=0.05)


@pytest.mark.asyncio
async def test_run_with_retry_succeeds_on_second_attempt() -> None:
    clock = ManualClock()
    config = RateLimitConfig(max_retry_attempts=3, initial_backoff_s=0.001, jitter=False)
    counter = {"n": 0}

    async def fn() -> str:
        counter["n"] += 1
        if counter["n"] == 1:
            raise RateLimitedError("first call rate-limited")
        return "ok"

    result = await run_with_retry(fn, config=config, clock=clock, bucket=None)
    assert result == "ok"
    assert counter["n"] == 2


@pytest.mark.asyncio
async def test_run_with_retry_bubbles_when_attempts_exhausted() -> None:
    clock = ManualClock()
    config = RateLimitConfig(max_retry_attempts=2, initial_backoff_s=0.001, jitter=False)
    counter = {"n": 0}

    async def fn() -> str:
        counter["n"] += 1
        raise RateLimitedError("always limited")

    with pytest.raises(RateLimitedError):
        await run_with_retry(fn, config=config, clock=clock, bucket=None)
    # initial attempt + 2 retries
    assert counter["n"] == 3

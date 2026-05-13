"""Configuration dataclasses — mirror of CONTRACT.md §8.

Two levels: per-adapter (handled by each adapter's own constructor) and
client-wide (CacheConfig / RateLimitConfig / StreamingConfig held inside
:class:`ClientConfig`). Defaults are invariant within v1.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal

__all__ = [
    "CacheConfig",
    "ClientConfig",
    "RateLimitConfig",
    "RateLimitStrategy",
    "StreamingConfig",
]


RateLimitStrategy = Literal["bubble", "fallthrough"]


@dataclass(frozen=True, slots=True)
class CacheConfig:
    enabled: bool = False
    spot_ttl_s: int = 5
    ohlcv_ttl_s: int = 60
    order_book_ttl_s: int = 1
    max_entries: int = 10_000

    def __post_init__(self) -> None:
        for label, value in (
            ("spot_ttl_s", self.spot_ttl_s),
            ("ohlcv_ttl_s", self.ohlcv_ttl_s),
            ("order_book_ttl_s", self.order_book_ttl_s),
        ):
            if value < 0:
                raise ValueError(f"{label} must be >= 0, got {value}")
        if self.max_entries < 1:
            raise ValueError(f"max_entries must be >= 1, got {self.max_entries}")


@dataclass(frozen=True, slots=True)
class RateLimitConfig:
    enabled: bool = True
    strategy: RateLimitStrategy = "fallthrough"
    max_retry_attempts: int = 3
    initial_backoff_s: float = 0.5
    max_backoff_s: float = 30.0
    jitter: bool = True

    def __post_init__(self) -> None:
        if self.strategy not in ("bubble", "fallthrough"):
            raise ValueError(
                f"strategy must be 'bubble' or 'fallthrough', got {self.strategy!r}"
            )
        if self.max_retry_attempts < 0:
            raise ValueError(
                f"max_retry_attempts must be >= 0, got {self.max_retry_attempts}"
            )
        if self.initial_backoff_s < 0 or self.max_backoff_s < self.initial_backoff_s:
            raise ValueError(
                "0 <= initial_backoff_s <= max_backoff_s required, got "
                f"{self.initial_backoff_s} / {self.max_backoff_s}"
            )


@dataclass(frozen=True, slots=True)
class StreamingConfig:
    default_polling_interval_s: float = 4.0
    max_reconnect_attempts: int = 5
    reconnect_backoff_s: float = 2.0

    def __post_init__(self) -> None:
        if not (1.0 <= self.default_polling_interval_s <= 3600.0):
            raise ValueError(
                "default_polling_interval_s must be 1.0..3600.0, got "
                f"{self.default_polling_interval_s}"
            )
        if self.max_reconnect_attempts < 0:
            raise ValueError(
                f"max_reconnect_attempts must be >= 0, got {self.max_reconnect_attempts}"
            )
        if self.reconnect_backoff_s < 0:
            raise ValueError(
                f"reconnect_backoff_s must be >= 0, got {self.reconnect_backoff_s}"
            )


@dataclass(frozen=True, slots=True)
class ClientConfig:
    cache: CacheConfig
    rate_limit: RateLimitConfig
    streaming: StreamingConfig

    @classmethod
    def defaults(cls) -> ClientConfig:
        return cls(CacheConfig(), RateLimitConfig(), StreamingConfig())

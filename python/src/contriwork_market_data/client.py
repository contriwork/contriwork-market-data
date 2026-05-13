"""MarketDataClient — public orchestrator. CONTRACT.md §1, §3, §4.

Builds the four port operations on top of any
:class:`~contriwork_market_data._adapter.Adapter` set registered in an
:class:`AdapterRegistry`, layering in:

* per-method TTL cache (opt-in via ``CacheConfig.enabled``)
* per-adapter token-bucket rate limiting with jittered exponential retry
* ordered adapter fallback (success or bubble vs. fall-through per
  ``RateLimitConfig.strategy``)
* native-or-emulated streaming dispatch
"""

from __future__ import annotations

from collections.abc import AsyncIterator, Awaitable, Callable, Sequence
from datetime import datetime
from typing import Any

from ._adapter import Adapter
from ._cache import TTLCache
from ._clock import Clock, SystemClock
from ._rate_limit import TokenBucket, run_with_retry
from ._registry import AdapterRegistry
from ._streaming import polling_ticker_iterator
from .config import ClientConfig
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
    StreamingNotSupportedError,
    SymbolNotFoundError,
    UnsupportedQuoteCurrencyError,
)
from .types import Candle, Interval, OrderBook, SpotPrice, Ticker

__all__ = ["MarketDataClient"]

_OHLCV_LIMIT_CAP = 1000
_ORDER_BOOK_DEPTH_CAP = 100

# CONTRACT §3: ``symbol`` is 1..64 ASCII-printable; ``quote_currency`` 2..8.
_SYMBOL_MIN, _SYMBOL_MAX = 1, 64
_QUOTE_MIN, _QUOTE_MAX = 2, 8

# Codes that mean "no adapter in the chain could legitimately serve the
# request" and should be surfaced directly when the chain has a single
# member (rather than being wrapped in ALL_ADAPTERS_FAILED). See
# CONTRACT.md §3 and the contract-tests fixture set.
_FATAL_PASSTHROUGH_CODES: frozenset[str] = frozenset(
    {
        "INVALID_INTERVAL",
        "UNSUPPORTED_QUOTE_CURRENCY",
        "MISSING_CREDENTIALS",
        "SYMBOL_NOT_FOUND",
    }
)


def _validate_symbol(symbol: str) -> None:
    if not isinstance(symbol, str) or not (_SYMBOL_MIN <= len(symbol) <= _SYMBOL_MAX):
        raise InvalidInputError(
            f"symbol must be {_SYMBOL_MIN}..{_SYMBOL_MAX} chars, got len="
            f"{len(symbol) if isinstance(symbol, str) else 'n/a'}"
        )
    if not symbol.isascii() or not symbol.isprintable():
        raise InvalidInputError("symbol must be ASCII-printable")


def _validate_quote_currency(quote_currency: str) -> None:
    if not isinstance(quote_currency, str) or not (
        _QUOTE_MIN <= len(quote_currency) <= _QUOTE_MAX
    ):
        raise InvalidInputError(
            f"quote_currency must be {_QUOTE_MIN}..{_QUOTE_MAX} chars, got "
            f"len={len(quote_currency) if isinstance(quote_currency, str) else 'n/a'}"
        )


def _validate_market(market: str) -> None:
    if not isinstance(market, str) or len(market) == 0 or not market.isascii():
        raise InvalidInputError("market must be a non-empty ASCII string")


def _quote_supported(adapter: Adapter, quote_currency: str) -> bool:
    caps = adapter.capability.supported_quote_currencies
    if caps == "ANY":
        return True
    return quote_currency in caps


class MarketDataClient:
    """Concrete :class:`MarketDataPort` implementation."""

    def __init__(
        self,
        *,
        registry: AdapterRegistry,
        config: ClientConfig | None = None,
        clock: Clock | None = None,
    ) -> None:
        self._registry = registry
        self._config = config or ClientConfig.defaults()
        self._clock = clock or SystemClock()
        self._spot_cache: TTLCache[SpotPrice] = TTLCache(
            max_entries=self._config.cache.max_entries, clock=self._clock
        )
        self._ohlcv_cache: TTLCache[tuple[Candle, ...]] = TTLCache(
            max_entries=self._config.cache.max_entries, clock=self._clock
        )
        self._order_book_cache: TTLCache[OrderBook] = TTLCache(
            max_entries=self._config.cache.max_entries, clock=self._clock
        )
        self._buckets: dict[str, TokenBucket] = {}

    # ---- public surface ---------------------------------------------------

    async def get_spot(
        self,
        symbol: str,
        market: str,
        quote_currency: str = "USD",
    ) -> SpotPrice:
        _validate_symbol(symbol)
        _validate_market(market)
        _validate_quote_currency(quote_currency)
        chain = self._resolve_chain(market)
        cache_key = ("get_spot", market, symbol, quote_currency)
        cache = self._spot_cache if self._config.cache.enabled else None
        if cache is not None:
            hit = cache.get(cache_key)
            if hit is not None:
                return hit

        async def call(adapter: Adapter) -> SpotPrice:
            self._reject_if_unsupported_quote(adapter, quote_currency)
            return await adapter.get_spot(symbol, quote_currency)

        result = await self._run_chain(chain=chain, op=call)
        if cache is not None:
            cache.set(cache_key, result, ttl_s=self._config.cache.spot_ttl_s)
        return result

    async def get_ohlcv(
        self,
        symbol: str,
        market: str,
        interval: Interval,
        since: datetime | None = None,
        limit: int = 100,
    ) -> list[Candle]:
        _validate_symbol(symbol)
        _validate_market(market)
        if not (1 <= limit <= _OHLCV_LIMIT_CAP):
            raise InvalidInputError(
                f"limit must be 1..{_OHLCV_LIMIT_CAP}, got {limit}"
            )
        if since is not None and since > self._clock.now():
            raise InvalidInputError("since must not be in the future")

        chain = self._resolve_chain(market)
        cache_key: tuple[Any, ...] = (
            "get_ohlcv",
            market,
            symbol,
            interval.value,
            since.isoformat() if since else None,
            limit,
        )
        cache = self._ohlcv_cache if self._config.cache.enabled else None
        if cache is not None:
            hit = cache.get(cache_key)
            if hit is not None:
                return list(hit)

        async def call(adapter: Adapter) -> list[Candle]:
            if interval not in adapter.capability.supported_intervals:
                raise InvalidIntervalError(
                    f"adapter {adapter.adapter_id} does not support interval "
                    f"{interval.value}",
                    adapter_id=adapter.adapter_id,
                )
            return await adapter.get_ohlcv(symbol, interval, since, limit)

        result = await self._run_chain(chain=chain, op=call)
        if cache is not None:
            cache.set(
                cache_key, tuple(result), ttl_s=self._config.cache.ohlcv_ttl_s
            )
        return result

    async def get_order_book(
        self,
        symbol: str,
        market: str,
        depth: int = 20,
    ) -> OrderBook:
        _validate_symbol(symbol)
        _validate_market(market)
        if not (1 <= depth <= _ORDER_BOOK_DEPTH_CAP):
            raise InvalidInputError(
                f"depth must be 1..{_ORDER_BOOK_DEPTH_CAP}, got {depth}"
            )

        chain = self._resolve_chain(market)
        cache_key: tuple[Any, ...] = ("get_order_book", market, symbol, depth)
        cache = self._order_book_cache if self._config.cache.enabled else None
        if cache is not None:
            hit = cache.get(cache_key)
            if hit is not None:
                return hit

        async def call(adapter: Adapter) -> OrderBook:
            if not adapter.capability.supports_order_book:
                raise AdapterFeatureNotSupportedError(
                    f"adapter {adapter.adapter_id} does not support order book",
                    adapter_id=adapter.adapter_id,
                )
            return await adapter.get_order_book(symbol, depth)

        result = await self._run_chain(chain=chain, op=call)
        if cache is not None:
            cache.set(
                cache_key, result, ttl_s=self._config.cache.order_book_ttl_s
            )
        return result

    async def subscribe_ticker(
        self,
        symbol: str,
        market: str,
        polling_fallback: bool = True,
        polling_interval_s: float = 4.0,
    ) -> AsyncIterator[Ticker]:
        _validate_symbol(symbol)
        _validate_market(market)
        if not (1.0 <= polling_interval_s <= 3600.0):
            raise InvalidInputError(
                f"polling_interval_s must be 1.0..3600.0, got {polling_interval_s}"
            )
        chain = self._resolve_chain(market)

        chosen_native: Adapter | None = None
        chosen_polling: Adapter | None = None
        for adapter in chain:
            if adapter.capability.supports_native_streaming and chosen_native is None:
                chosen_native = adapter
                break
            if polling_fallback and chosen_polling is None:
                chosen_polling = adapter
        if chosen_native is None and chosen_polling is None:
            raise StreamingNotSupportedError(
                f"no adapter in chain for market {market!r} supports streaming "
                "(neither native nor polling fallback applies)",
            )

        if chosen_native is not None:
            async for ticker in chosen_native.subscribe_ticker(symbol):
                yield ticker
            return

        assert chosen_polling is not None
        async for ticker in polling_ticker_iterator(
            chosen_polling,
            symbol=symbol,
            quote_currency="USD",
            polling_interval_s=polling_interval_s,
            clock=self._clock,
        ):
            yield ticker

    # ---- internals --------------------------------------------------------

    def _resolve_chain(self, market: str) -> tuple[Adapter, ...]:
        chain = self._registry.chain_for(market)
        if not chain:
            raise NoAdapterForMarketError(
                f"no adapter chain registered for market {market!r}",
            )
        return chain

    def _bucket_for(self, adapter: Adapter) -> TokenBucket | None:
        if not self._config.rate_limit.enabled:
            return None
        adapter_id = adapter.adapter_id
        existing = self._buckets.get(adapter_id)
        if existing is not None:
            return existing
        rpm = max(1, adapter.capability.rate_limit_per_minute)
        bucket = TokenBucket(
            capacity=rpm,
            refill_per_second=rpm / 60.0,
            clock=self._clock,
        )
        self._buckets[adapter_id] = bucket
        return bucket

    def _reject_if_unsupported_quote(
        self, adapter: Adapter, quote_currency: str
    ) -> None:
        if not _quote_supported(adapter, quote_currency):
            raise UnsupportedQuoteCurrencyError(
                f"adapter {adapter.adapter_id} does not support quote_currency "
                f"{quote_currency!r}",
                adapter_id=adapter.adapter_id,
            )

    async def _run_chain[T](
        self,
        *,
        chain: Sequence[Adapter],
        op: Callable[[Adapter], Awaitable[T]],
    ) -> T:
        """Invoke ``op`` against each adapter in order until one succeeds.

        Single-adapter chains surface the adapter's error directly so caller
        intent errors (INVALID_INTERVAL, UNSUPPORTED_QUOTE_CURRENCY, …) bubble
        unwrapped. Multi-adapter chains aggregate failures into
        ``ALL_ADAPTERS_FAILED`` unless every adapter raised the same
        passthrough-eligible code.
        """
        if len(chain) == 1:
            return await self._invoke_one(chain[0], op)

        causes: list[MarketDataError] = []
        for adapter in chain:
            try:
                return await self._invoke_one(adapter, op)
            except RateLimitedError as exc:
                causes.append(exc)
                if self._config.rate_limit.strategy == "bubble":
                    raise
                continue
            except (
                AdapterFeatureNotSupportedError,
                AdapterUnavailableError,
                InvalidIntervalError,
                MissingCredentialsError,
                SymbolNotFoundError,
                UnsupportedQuoteCurrencyError,
            ) as exc:
                causes.append(exc)
                continue

        codes = {c.code for c in causes}
        if len(codes) == 1:
            only = next(iter(codes))
            if only in _FATAL_PASSTHROUGH_CODES:
                first = causes[0]
                raise type(first)(
                    f"all {len(causes)} adapter(s) failed with {only}",
                    cause=causes,
                )
        raise AllAdaptersFailedError(
            f"all {len(causes)} adapter(s) failed",
            cause=causes,
        )

    async def _invoke_one[T](
        self,
        adapter: Adapter,
        op: Callable[[Adapter], Awaitable[T]],
    ) -> T:
        bucket = self._bucket_for(adapter)
        return await run_with_retry(
            lambda: op(adapter),
            config=self._config.rate_limit,
            clock=self._clock,
            bucket=bucket,
        )

"""Error taxonomy — mirror of CONTRACT.md §7. Codes are invariant within v1.

Per-language exception types wrap a stable SCREAMING_SNAKE_CASE ``code``
string. Callers may catch by code (via :func:`error_for_code`) or by
exception class.
"""

from __future__ import annotations

from collections.abc import Sequence

__all__ = [
    "AdapterFeatureNotSupportedError",
    "AdapterUnavailableError",
    "AllAdaptersFailedError",
    "InvalidInputError",
    "InvalidIntervalError",
    "MarketDataError",
    "MissingCredentialsError",
    "NoAdapterForMarketError",
    "RateLimitedError",
    "StreamDisconnectedError",
    "StreamingNotSupportedError",
    "SymbolNotFoundError",
    "UnsupportedQuoteCurrencyError",
    "error_for_code",
]


class MarketDataError(Exception):
    """Base class for all errors raised by the public surface."""

    code: str = "UNKNOWN"

    def __init__(
        self,
        message: str = "",
        *,
        adapter_id: str | None = None,
        cause: Sequence[MarketDataError] | None = None,
    ) -> None:
        super().__init__(message or self.code)
        self.adapter_id = adapter_id
        # ``__cause__`` is reserved by Python's exception chaining; we expose
        # the per-adapter failure trail under a distinct attribute.
        self.cause_list: tuple[MarketDataError, ...] = tuple(cause or ())

    def __repr__(self) -> str:  # pragma: no cover - debug helper
        return f"{type(self).__name__}(code={self.code!r}, adapter={self.adapter_id!r})"


class InvalidInputError(MarketDataError):
    code = "INVALID_INPUT"


class InvalidIntervalError(MarketDataError):
    code = "INVALID_INTERVAL"


class UnsupportedQuoteCurrencyError(MarketDataError):
    code = "UNSUPPORTED_QUOTE_CURRENCY"


class SymbolNotFoundError(MarketDataError):
    code = "SYMBOL_NOT_FOUND"


class RateLimitedError(MarketDataError):
    code = "RATE_LIMITED"


class AdapterUnavailableError(MarketDataError):
    code = "ADAPTER_UNAVAILABLE"


class AdapterFeatureNotSupportedError(MarketDataError):
    code = "ADAPTER_FEATURE_NOT_SUPPORTED"


class MissingCredentialsError(MarketDataError):
    code = "MISSING_CREDENTIALS"


class NoAdapterForMarketError(MarketDataError):
    code = "NO_ADAPTER_FOR_MARKET"


class AllAdaptersFailedError(MarketDataError):
    code = "ALL_ADAPTERS_FAILED"


class StreamingNotSupportedError(MarketDataError):
    code = "STREAMING_NOT_SUPPORTED"


class StreamDisconnectedError(MarketDataError):
    code = "STREAM_DISCONNECTED"


_CODE_TO_CLASS: dict[str, type[MarketDataError]] = {
    cls.code: cls
    for cls in (
        InvalidInputError,
        InvalidIntervalError,
        UnsupportedQuoteCurrencyError,
        SymbolNotFoundError,
        RateLimitedError,
        AdapterUnavailableError,
        AdapterFeatureNotSupportedError,
        MissingCredentialsError,
        NoAdapterForMarketError,
        AllAdaptersFailedError,
        StreamingNotSupportedError,
        StreamDisconnectedError,
    )
}


def error_for_code(code: str) -> type[MarketDataError]:
    """Look up the exception class for a stable code string."""
    try:
        return _CODE_TO_CLASS[code]
    except KeyError as exc:
        raise KeyError(f"unknown error code: {code!r}") from exc

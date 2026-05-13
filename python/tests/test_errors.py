"""Tests for the locked error taxonomy."""

from __future__ import annotations

import pytest

from contriwork_market_data import (
    AdapterFeatureNotSupportedError,
    AdapterUnavailableError,
    AllAdaptersFailedError,
    InvalidInputError,
    InvalidIntervalError,
    MarketDataError,
    MissingCredentialsError,
    NoAdapterForMarketError,
    RateLimitedError,
    StreamDisconnectedError,
    StreamingNotSupportedError,
    SymbolNotFoundError,
    UnsupportedQuoteCurrencyError,
    error_for_code,
)


@pytest.mark.parametrize(
    "exc_cls,code",
    [
        (InvalidInputError, "INVALID_INPUT"),
        (InvalidIntervalError, "INVALID_INTERVAL"),
        (UnsupportedQuoteCurrencyError, "UNSUPPORTED_QUOTE_CURRENCY"),
        (SymbolNotFoundError, "SYMBOL_NOT_FOUND"),
        (RateLimitedError, "RATE_LIMITED"),
        (AdapterUnavailableError, "ADAPTER_UNAVAILABLE"),
        (AdapterFeatureNotSupportedError, "ADAPTER_FEATURE_NOT_SUPPORTED"),
        (MissingCredentialsError, "MISSING_CREDENTIALS"),
        (NoAdapterForMarketError, "NO_ADAPTER_FOR_MARKET"),
        (AllAdaptersFailedError, "ALL_ADAPTERS_FAILED"),
        (StreamingNotSupportedError, "STREAMING_NOT_SUPPORTED"),
        (StreamDisconnectedError, "STREAM_DISCONNECTED"),
    ],
)
def test_error_codes_are_locked(exc_cls: type, code: str) -> None:
    assert exc_cls.code == code
    assert error_for_code(code) is exc_cls


def test_unknown_code_raises() -> None:
    with pytest.raises(KeyError):
        error_for_code("DEFINITELY_NOT_A_CODE")


def test_cause_list_preserves_order() -> None:
    a = AdapterUnavailableError("a", adapter_id="x")
    b = AdapterUnavailableError("b", adapter_id="y")
    agg = AllAdaptersFailedError("agg", cause=[a, b])
    assert agg.cause_list == (a, b)


def test_market_data_error_is_root() -> None:
    assert issubclass(RateLimitedError, MarketDataError)

"""Smoke tests — verify package imports and the public surface is reachable."""

from __future__ import annotations


def test_package_imports() -> None:
    import contriwork_market_data

    assert contriwork_market_data.__version__


def test_port_protocol_is_exported() -> None:
    from contriwork_market_data import MarketDataPort

    assert hasattr(MarketDataPort, "get_spot")
    assert hasattr(MarketDataPort, "get_ohlcv")
    assert hasattr(MarketDataPort, "get_order_book")
    assert hasattr(MarketDataPort, "subscribe_ticker")


def test_client_is_exported() -> None:
    from contriwork_market_data import (
        AdapterRegistry,
        ClientConfig,
        MarketDataClient,
    )

    assert MarketDataClient is not None
    assert AdapterRegistry is not None
    assert ClientConfig.defaults() is not None

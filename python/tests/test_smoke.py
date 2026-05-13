"""Smoke tests — verify package imports and port is reachable."""

from __future__ import annotations


def test_package_imports() -> None:
    import contriwork_market_data

    assert contriwork_market_data.__version__


def test_port_is_exported() -> None:
    from contriwork_market_data import MarketDataPort

    assert MarketDataPort is not None
    assert hasattr(MarketDataPort, "example")

"""Smoke tests — verify package imports and port is reachable."""

from __future__ import annotations


def test_package_imports() -> None:
    import contriwork_PACKAGE_NAME

    assert contriwork_PACKAGE_NAME.__version__


def test_port_is_exported() -> None:
    from contriwork_PACKAGE_NAME import PackageNamePort

    assert PackageNamePort is not None
    assert hasattr(PackageNamePort, "example")

"""contriwork-PACKAGE_NAME — Python adapter.

Public surface re-exports from :mod:`contriwork_PACKAGE_NAME.port`. Do not
import concrete adapter classes from outside — they are internal detail.
"""

from __future__ import annotations

from importlib.metadata import PackageNotFoundError, version

from .port import PackageNamePort

__all__ = ["PackageNamePort", "__version__"]

try:
    __version__ = version("contriwork-PACKAGE_NAME")
except PackageNotFoundError:
    __version__ = "0.0.0"

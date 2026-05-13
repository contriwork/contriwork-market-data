"""AdapterRegistry — maps ``market`` string to ordered adapter chain.

The registry is constructed by the caller (typically the application
wiring code in their DI container or factory function). Default chains for
well-known markets ship in
:func:`contriwork_market_data.default_chains` once the concrete adapters
are imported.
"""

from __future__ import annotations

from collections.abc import Iterable

from ._adapter import Adapter

__all__ = ["AdapterRegistry"]


class AdapterRegistry:
    def __init__(self, chains: dict[str, Iterable[Adapter]] | None = None) -> None:
        self._chains: dict[str, tuple[Adapter, ...]] = {}
        for market, adapters in (chains or {}).items():
            self._chains[market] = tuple(adapters)

    def chain_for(self, market: str) -> tuple[Adapter, ...]:
        """Return the ordered adapter chain for ``market``; empty if unknown."""
        return self._chains.get(market, ())

    def register(self, market: str, adapters: Iterable[Adapter]) -> None:
        self._chains[market] = tuple(adapters)

    def markets(self) -> tuple[str, ...]:
        return tuple(self._chains.keys())

    def adapters(self) -> tuple[Adapter, ...]:
        """Return every unique adapter across all chains, in first-seen order."""
        seen: set[str] = set()
        result: list[Adapter] = []
        for adapters in self._chains.values():
            for adapter in adapters:
                if adapter.adapter_id not in seen:
                    seen.add(adapter.adapter_id)
                    result.append(adapter)
        return tuple(result)

# contriwork-market-data (Python)

Python adapter for the ContriWork **market-data** port. One API surface, three languages (Python / .NET / npm) — this package is the Python implementation.

Cross-language specification, contract, and release history live in the
[GitHub repository](https://github.com/contriwork/contriwork-market-data):

- [Root README](https://github.com/contriwork/contriwork-market-data/blob/main/README.md) — ecosystem overview
- [`CONTRACT.md`](https://github.com/contriwork/contriwork-market-data/blob/main/CONTRACT.md) — language-agnostic port spec
- [`CHANGELOG.md`](https://github.com/contriwork/contriwork-market-data/blob/main/CHANGELOG.md)

Sister packages: [`Contriwork.MarketData`](https://www.nuget.org/packages/Contriwork.MarketData) (NuGet), [`@contriwork/market-data`](https://www.npmjs.com/package/@contriwork/market-data) (npm).

## Install

```bash
pip install contriwork-market-data
```

Requires **Python ≥ 3.13**.

## Quick start

```python
from contriwork_market_data import MarketDataPort

# TODO: one-line example once the port has real methods.
```

## Local development

```bash
uv sync --all-extras
uv run pytest
uv run ruff check
uv run mypy src
```

## License

MIT — see [LICENSE](https://github.com/contriwork/contriwork-market-data/blob/main/LICENSE).

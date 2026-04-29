# contriwork-PACKAGE_NAME (Python)

Python adapter for the ContriWork **PACKAGE_NAME** port. One API surface, three languages (Python / .NET / npm) — this package is the Python implementation.

Cross-language specification, contract, and release history live in the
[GitHub repository](https://github.com/contriwork/contriwork-PACKAGE_NAME):

- [Root README](https://github.com/contriwork/contriwork-PACKAGE_NAME/blob/main/README.md) — ecosystem overview
- [`CONTRACT.md`](https://github.com/contriwork/contriwork-PACKAGE_NAME/blob/main/CONTRACT.md) — language-agnostic port spec
- [`CHANGELOG.md`](https://github.com/contriwork/contriwork-PACKAGE_NAME/blob/main/CHANGELOG.md)

Sister packages: [`Contriwork.PackageName`](https://www.nuget.org/packages/Contriwork.PackageName) (NuGet), [`@contriwork/PACKAGE_NAME`](https://www.npmjs.com/package/@contriwork/PACKAGE_NAME) (npm).

## Install

```bash
pip install contriwork-PACKAGE_NAME
```

Requires **Python ≥ 3.13**.

## Quick start

```python
from contriwork_PACKAGE_NAME import PackageNamePort

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

MIT — see [LICENSE](https://github.com/contriwork/contriwork-PACKAGE_NAME/blob/main/LICENSE).

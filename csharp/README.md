# Contriwork.MarketData (.NET)

.NET adapter for the ContriWork **MarketData** port. One API surface, three languages (Python / .NET / npm) — this package is the .NET implementation.

Cross-language specification, contract, and release history live in the
[GitHub repository](https://github.com/contriwork/contriwork-market-data):

- [Root README](https://github.com/contriwork/contriwork-market-data/blob/main/README.md) — ecosystem overview
- [`CONTRACT.md`](https://github.com/contriwork/contriwork-market-data/blob/main/CONTRACT.md) — language-agnostic port spec
- [`CHANGELOG.md`](https://github.com/contriwork/contriwork-market-data/blob/main/CHANGELOG.md)

Sister packages: [`contriwork-market-data`](https://pypi.org/project/contriwork-market-data/) (PyPI), [`@contriwork/market-data`](https://www.npmjs.com/package/@contriwork/market-data) (npm).

## Install

```bash
dotnet add package Contriwork.MarketData
```

Targets **.NET 10 LTS**.

## Quick start

```csharp
using Contriwork.MarketData;

// TODO: one-line example once the port has real methods.
```

## Local development

```bash
dotnet restore
dotnet build
dotnet test
dotnet format --verify-no-changes
```

## License

MIT — see [LICENSE](https://github.com/contriwork/contriwork-market-data/blob/main/LICENSE).

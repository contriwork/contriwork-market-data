<div align="center">

# contriwork-market-data

**One API surface, three languages.**

[![PyPI](https://img.shields.io/pypi/v/contriwork-market-data.svg)](https://pypi.org/project/contriwork-market-data/)
[![NuGet](https://img.shields.io/nuget/v/Contriwork.MarketData.svg)](https://www.nuget.org/packages/Contriwork.MarketData/)
[![npm](https://img.shields.io/npm/v/@contriwork/market-data.svg)](https://www.npmjs.com/package/@contriwork/market-data)
[![CI](https://github.com/contriwork/contriwork-market-data/actions/workflows/ci.yml/badge.svg)](https://github.com/contriwork/contriwork-market-data/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)

</div>

> **TODO:** One-line description of what this package does. Replace this block.

## Why

> **TODO:** Explain the problem this package solves and the gap it fills in the consumer's stack.

## Install

| Registry | Command                                                |
|----------|--------------------------------------------------------|
| PyPI     | `pip install contriwork-market-data`                  |
| NuGet    | `dotnet add package Contriwork.MarketData`            |
| npm      | `npm install @contriwork/market-data`                 |

All three publish at the **same version** on the **same release**. See [`VERSION_MATRIX.md`](./VERSION_MATRIX.md) for runtime support per release.

## Quick start

### Python

```python
from contriwork_market_data import MarketDataPort
# TODO: example
```

### C#

```csharp
using Contriwork.MarketData;
// TODO: example
```

### TypeScript

```typescript
import { MarketDataPort } from "@contriwork/market-data";
// TODO: example
```

## Architecture

This package follows the [ContriWork port + adapter](https://github.com/contriwork/.github) pattern:

- **Port** — language-agnostic interface defined in [`CONTRACT.md`](./CONTRACT.md).
- **Adapters** — concrete implementations per language (`python/src/`, `csharp/src/`, `typescript/src/`).
- **Contract tests** — shared fixture set in [`contract-tests/test_cases.json`](./contract-tests/test_cases.json), executed by all three language test runners. Release is blocked unless all three are green.

## Runtime baseline

| Language | Target               |
|----------|----------------------|
| Python   | **3.13**             |
| .NET     | **10 (LTS)**         |
| Node.js  | **24 (Active LTS)**  |

Single LTS per language by policy — no parallel matrix support for short-lived LTS releases.

## Security

See [`SECURITY.md`](./SECURITY.md) for the disclosure channel and the package's hardening posture (black-box / gray-box / white-box pen-test results, scan tooling, and remediation history).

## Contributing

Forks welcome. See [`CONTRIBUTING.md`](./CONTRIBUTING.md) for the DCO sign-off rule, the contract-test workflow, and branch protection details.

## License

MIT — see [`LICENSE`](./LICENSE).

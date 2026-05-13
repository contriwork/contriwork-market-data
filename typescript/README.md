# @contriwork/market-data (npm)

Node.js / TypeScript adapter for the ContriWork **MarketData** port. One API surface, three languages (Python / .NET / npm) — this package is the Node.js implementation.

Cross-language specification, contract, and release history live in the
[GitHub repository](https://github.com/contriwork/contriwork-market-data):

- [Root README](https://github.com/contriwork/contriwork-market-data/blob/main/README.md) — ecosystem overview
- [`CONTRACT.md`](https://github.com/contriwork/contriwork-market-data/blob/main/CONTRACT.md) — language-agnostic port spec
- [`CHANGELOG.md`](https://github.com/contriwork/contriwork-market-data/blob/main/CHANGELOG.md)

Sister packages: [`contriwork-market-data`](https://pypi.org/project/contriwork-market-data/) (PyPI), [`Contriwork.MarketData`](https://www.nuget.org/packages/Contriwork.MarketData) (NuGet).

## Install

```bash
npm install @contriwork/market-data
# or: pnpm add @contriwork/market-data
# or: yarn add @contriwork/market-data
```

Requires **Node.js ≥ 24**. Dual-published ESM + CJS with bundled `.d.ts` / `.d.cts` type declarations. Published with [npm provenance](https://docs.npmjs.com/generating-provenance-statements) via GitHub Actions OIDC.

## Quick start

```ts
import type { MarketDataPort } from "@contriwork/market-data";

// TODO: one-line example once the port has real methods.
```

## Local development

```bash
pnpm install --frozen-lockfile
pnpm test
pnpm typecheck
pnpm lint
pnpm build
```

## License

MIT — see [LICENSE](https://github.com/contriwork/contriwork-market-data/blob/main/LICENSE).

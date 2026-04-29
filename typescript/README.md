# @contriwork/PACKAGE_NAME (npm)

Node.js / TypeScript adapter for the ContriWork **PackageName** port. One API surface, three languages (Python / .NET / npm) — this package is the Node.js implementation.

Cross-language specification, contract, and release history live in the
[GitHub repository](https://github.com/contriwork/contriwork-PACKAGE_NAME):

- [Root README](https://github.com/contriwork/contriwork-PACKAGE_NAME/blob/main/README.md) — ecosystem overview
- [`CONTRACT.md`](https://github.com/contriwork/contriwork-PACKAGE_NAME/blob/main/CONTRACT.md) — language-agnostic port spec
- [`CHANGELOG.md`](https://github.com/contriwork/contriwork-PACKAGE_NAME/blob/main/CHANGELOG.md)

Sister packages: [`contriwork-PACKAGE_NAME`](https://pypi.org/project/contriwork-PACKAGE_NAME/) (PyPI), [`Contriwork.PackageName`](https://www.nuget.org/packages/Contriwork.PackageName) (NuGet).

## Install

```bash
npm install @contriwork/PACKAGE_NAME
# or: pnpm add @contriwork/PACKAGE_NAME
# or: yarn add @contriwork/PACKAGE_NAME
```

Requires **Node.js ≥ 24**. Dual-published ESM + CJS with bundled `.d.ts` / `.d.cts` type declarations. Published with [npm provenance](https://docs.npmjs.com/generating-provenance-statements) via GitHub Actions OIDC.

## Quick start

```ts
import type { PackageNamePort } from "@contriwork/PACKAGE_NAME";

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

MIT — see [LICENSE](https://github.com/contriwork/contriwork-PACKAGE_NAME/blob/main/LICENSE).

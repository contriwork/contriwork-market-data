# VERSION_MATRIX

This file documents which language runtime versions and contract revision each release of this package supports. Every release MUST add a row here. The release workflow refuses to publish if `VERSION` is bumped without a corresponding row.

| Version | Python | .NET | Node | Contract | npm strategy | Released  |
|---------|--------|------|------|----------|--------------|-----------|
| 0.0.0   | 3.13   | 10.0 | 24   | v0       | pure-TS      | unreleased |

## Rules

- One row per version, ascending.
- Python / .NET / Node values reflect the **minimum** supported runtime, matching the ContriWork baseline (Python 3.13, .NET 10 LTS, Node 24 LTS).
- Contract revision (`v0`, `v1`, …) is bumped only when `CONTRACT.md` has a behavior change. Patch releases never bump it.
- npm strategy values: `pure-TS` (default), `wasm`, `n-api`, `sidecar`, `http-client` — see `typescript/src/strategy.md` for the rationale.

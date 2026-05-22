# npm Production Strategy

The ContriWork roadmap (`PACKAGES_ROADMAP.md §3.5`) lists five possible strategies for shipping a package on npm. This file documents which one this package uses and **why**.

## Decision

**Strategy A — pure-TS reimplementation** (default).

### Rationale

`contriwork-market-data` is pure-logic glue over HTTP/JSON: an orchestrator
(cache, rate limiter, fallback chain, polling-emulated streaming) plus
per-provider adapters that shape REST responses into the shared types. None
of that benefits from native code, a WASM core, or a sidecar process — it is
exactly the "parsers / validators / encoders / algorithms" case Strategy A
targets.

- **HTTP**: the global `fetch` (Node >= 24) — no client dependency.
- **Decimals**: `decimal.js` is the single runtime dependency, chosen so the
  TypeScript port preserves the exact-precision semantics the Python
  (`Decimal`) and C# (`decimal`) ports guarantee. Representing prices as
  `number` (float64) would silently diverge from the other two languages and
  fail the cross-language `contract-tests`.
- **Streaming**: native async generators (`async function*` +
  `AsyncIterable<Ticker>`) with `AbortSignal` cancellation — no `ws`
  dependency in v0.1.0 (all adapters use polling emulation; native WSS is a
  v0.1.x additive).

Behaviour parity with Python and C# is enforced by the shared
`contract-tests/test_cases.json` fixture, which all three language runners
load and must satisfy identically.

## Alternatives considered

| ID  | Name                                           | When to pick                                                                      | Trade-off                                                                           |
| --- | ---------------------------------------------- | --------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| A   | **Pure-TS reimplementation**                   | Pure-logic package (parsers, validators, encoders, algorithms). Zero native deps. | Must maintain three code lines. Behaviour parity enforced only by `contract-tests`. |
| B   | **WASM** (compile Rust/Go/C++ to WebAssembly)  | Perf-critical core shared between runtimes.                                       | Toolchain complexity; binary size; restricted syscalls.                             |
| C   | **N-API / node-gyp native addon**              | Existing C/C++ codebase, must match bit-for-bit.                                  | Cross-platform build pain; prebuilt-binary hosting; sandbox/security surface.       |
| D   | **Sidecar** (bundled binary / subprocess)      | Large Python/.NET runtime, not worth rewriting.                                   | Process management, startup cost, platform-matrix of binaries.                      |
| E   | **HTTP client** (pointing at a hosted service) | Package wraps a SaaS or centralised service the org runs.                         | Requires infra; not usable offline; introduces network as dependency.               |

## Rationale

> **TODO:** One-paragraph justification for the chosen strategy. If the package logic is pure and contract tests enforce parity, strategy A is the default. If any of (B)–(E) is chosen, document the blocking reason why (A) fails (e.g. "reference implementation is 20 KLOC of Rust — reimplementation risk is too high").

## Revisiting this decision

A strategy change is a **minor bump** at minimum and likely a **major** because consumers see different install-time artefacts (prebuilt binaries, WASM glue, postinstall scripts). Do not switch strategies silently — open an ADR-style issue first and link it from this file.

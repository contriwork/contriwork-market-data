# npm Production Strategy

The ContriWork roadmap (`PACKAGES_ROADMAP.md §3.5`) lists five possible strategies for shipping a package on npm. This file documents which one this package uses and **why**.

## Decision

**Strategy A — pure-TS reimplementation** (default).

> **TODO:** Confirm strategy A is the right call for this package. If not, update the decision below and fill in the rationale.

## Alternatives considered

| ID | Name | When to pick | Trade-off |
|----|------|--------------|-----------|
| A | **Pure-TS reimplementation** | Pure-logic package (parsers, validators, encoders, algorithms). Zero native deps. | Must maintain three code lines. Behaviour parity enforced only by `contract-tests`. |
| B | **WASM** (compile Rust/Go/C++ to WebAssembly) | Perf-critical core shared between runtimes. | Toolchain complexity; binary size; restricted syscalls. |
| C | **N-API / node-gyp native addon** | Existing C/C++ codebase, must match bit-for-bit. | Cross-platform build pain; prebuilt-binary hosting; sandbox/security surface. |
| D | **Sidecar** (bundled binary / subprocess) | Large Python/.NET runtime, not worth rewriting. | Process management, startup cost, platform-matrix of binaries. |
| E | **HTTP client** (pointing at a hosted service) | Package wraps a SaaS or centralised service the org runs. | Requires infra; not usable offline; introduces network as dependency. |

## Rationale

> **TODO:** One-paragraph justification for the chosen strategy. If the package logic is pure and contract tests enforce parity, strategy A is the default. If any of (B)–(E) is chosen, document the blocking reason why (A) fails (e.g. "reference implementation is 20 KLOC of Rust — reimplementation risk is too high").

## Revisiting this decision

A strategy change is a **minor bump** at minimum and likely a **major** because consumers see different install-time artefacts (prebuilt binaries, WASM glue, postinstall scripts). Do not switch strategies silently — open an ADR-style issue first and link it from this file.

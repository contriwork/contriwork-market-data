# CONTRACT — PackageName

This document is the **language-agnostic contract** for this package. It is the single source of truth for the public surface. Every change to public behavior MUST start here before any code is written in `python/`, `csharp/`, or `typescript/`.

Contract revision (bumped on any behavior-visible change): **v0**

---

## Overview

> **TODO:** One paragraph. What problem does this package solve? Who is the consumer? What does the consumer hand in, and what does it get back?

---

## Port (language-agnostic interface)

The port defines the operations exposed across all three language implementations. Method names MUST be identical modulo language-idiomatic casing (`snake_case` for Python, `PascalCaseAsync` for C#, `camelCase` for TypeScript).

> **TODO:** List port operations in a single table. One row per method. Example:

| Operation | Input | Output | Failure modes |
|-----------|-------|--------|---------------|
| `example` | `input: string` | `string` | `InvalidInput`, `Timeout` |

---

## Methods

For each method listed above, document:

- **Signature** (the canonical form — languages adapt casing only).
- **Parameters** — name, type, constraints (nullable? range? encoding?).
- **Returns** — type, shape, what "success" means.
- **Preconditions** — what MUST hold before calling.
- **Postconditions** — what the caller can rely on afterward.

> **TODO:** Fill in one subsection per method.

### `example`

- **Signature**: `example(input: string) -> string`
- **Parameters**:
  - `input` — non-empty string, UTF-8, length ≤ 4096.
- **Returns**: a non-empty string derived from `input`.
- **Preconditions**: none beyond input validation.
- **Postconditions**: the return value is deterministic for a given input.

---

## Behavior

Describe observable behavior that is NOT captured by signatures alone:

- Idempotency (is calling twice with the same input guaranteed to be identical?).
- Side effects (I/O, allocations, background work).
- Ordering / concurrency guarantees.
- Resource ownership (who closes what?).

> **TODO:** Fill in.

---

## Error Taxonomy

Every failure mode MUST have:

- A **stable error code** (string, SCREAMING_SNAKE_CASE, never renamed without a major bump).
- A **language-agnostic description**.
- A per-language exception / error type that wraps it.

| Code | Description | When it is raised |
|------|-------------|-------------------|
| `INVALID_INPUT` | Input failed validation. | See method preconditions. |
| `TIMEOUT` | An operation did not complete within the deadline. | Any method with an I/O dependency. |

> **TODO:** Replace with the real taxonomy. Keep the codes short, stable, and unambiguous.

---

## Config Schema

If the package takes configuration (credentials, timeouts, endpoints), document the schema here as a flat table. Environment variable names, default values, and required/optional status MUST match across languages.

| Key | Env var | Type | Default | Required | Notes |
|-----|---------|------|---------|----------|-------|
| `timeout_ms` | `CONTRIWORK_PACKAGE_NAME_TIMEOUT_MS` | int | `5000` | no | Upper bound for any single operation. |

> **TODO:** Fill in, or write "This package takes no configuration." and delete the table.

---

## Invariants

Properties that MUST hold across all releases at the same contract revision:

- Method signatures do not change shape (arg count, return type) without a contract bump.
- Error codes are never renamed within the same contract revision.
- Config keys and env var names are never renamed within the same contract revision.
- Default values for optional config keys never change within the same contract revision.

> **TODO:** Add any package-specific invariants.

---

## Compatibility

- **Python**: ≥ 3.13 (see `VERSION_MATRIX.md`).
- **.NET**: ≥ 10.0 LTS.
- **Node.js**: ≥ 24 Active LTS.
- **npm strategy**: pure-TS (default). See [`typescript/src/strategy.md`](./typescript/src/strategy.md) if this package chose WASM / N-API / sidecar / HTTP client.

Runtime baseline is a hard constraint — no parallel matrix support for older LTSes.

---

## Change Log

Contract revisions ONLY — bumped when any of the sections above change in a way a consumer can observe. Does NOT track patch fixes or internal refactors; those go in `CHANGELOG.md`.

| Revision | Summary | Released with package version |
|----------|---------|-------------------------------|
| v0 | Initial scaffold. No public surface yet. | unreleased |

> **TODO:** Each release that changes this file adds a row here.

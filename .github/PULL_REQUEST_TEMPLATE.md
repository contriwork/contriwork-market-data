<!-- Thanks for contributing. Fill in every section; unchecked boxes block merge. -->

## Summary

<!-- 1-3 sentences: what changes, why. -->

## Languages touched

- [ ] Python
- [ ] C#
- [ ] TypeScript

## Contract impact

- [ ] `CONTRACT.md` updated (behavior / signature / error taxonomy / config change)
- [ ] N/A — internal only (refactor, test, docs)

<!-- If N/A, briefly justify: -->

## PR checklist

Mirrors `CONTRIBUTING.md §PR checklist`. Every item must be true before merge.

- [ ] Every commit is DCO-signed (`Signed-off-by:` trailer, `git commit -s`).
- [ ] Every commit is signature-verified (GPG or Sigstore).
- [ ] If public behavior changed: all three languages (`python/`, `csharp/`, `typescript/`) updated in this PR.
- [ ] `contract-tests/test_cases.json` covers the change and the three language runners are green.
- [ ] `CHANGELOG.md` updated under `## [Unreleased]` with an entry in **each** of `### Python`, `### C#`, `### npm`.
- [ ] Input validation considered at every new/changed public entry point.
- [ ] Security considered (authN/Z, supply chain, deserialization, SSRF, path traversal) — surface non-trivial notes below.
- [ ] If a `Dockerfile` changed: hadolint clean, Trivy HIGH/CRITICAL = 0.
- [ ] No over-engineering: simplest design meeting the contract wins.

## Security notes

<!-- Anything a reviewer should challenge from a white-box perspective. If none, write "none". -->

## How this was tested

<!-- Local commands run, CI results, manual repro steps. -->

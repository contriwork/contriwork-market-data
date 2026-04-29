# Contributing

Thanks for your interest in ContriWork. This document covers the rules for contributing to this package.

## Quick rules

- **Fork → branch → PR** to `main`. Direct pushes to `main` are blocked by branch protection.
- **DCO sign-off required.** Every commit must end with `Signed-off-by: Your Name <your@email>`. Use `git commit -s`.
- **Signed commits required.** GPG or Sigstore signatures both accepted. CI will reject unsigned commits.
- **Conventional Commits** for the PR title (`feat:`, `fix:`, `docs:`, `chore:`, `refactor:`, `test:`).
- **All three language CIs must be green** (Python + C# + TypeScript) plus the contract test suite. No exceptions.

## Local setup

### Prerequisites

| Tool          | Minimum version | Install                                              |
|---------------|-----------------|------------------------------------------------------|
| Python        | 3.13            | `brew install python@3.13` / `uv python install 3.13` |
| uv            | 0.5+            | `curl -LsSf https://astral.sh/uv/install.sh \| sh`   |
| .NET SDK      | 10.0            | https://dotnet.microsoft.com/download                |
| Node.js       | 24 LTS          | `nvm install 24` or `brew install node@24`           |
| pnpm          | 9+              | `npm install -g pnpm`                                |
| pre-commit    | 3+              | `pip install pre-commit` or `brew install pre-commit`|
| Docker        | 24+             | https://www.docker.com/get-started                   |

### Bootstrap

```bash
# clone your fork
git clone git@github.com:<your-username>/contriwork-PACKAGE_NAME.git
cd contriwork-PACKAGE_NAME

# install pre-commit hooks (gitleaks, ruff, dotnet format, eslint)
pre-commit install --install-hooks

# python
cd python && uv sync && uv run pytest && cd ..

# csharp
cd csharp && dotnet restore && dotnet test && cd ..

# typescript
cd typescript && pnpm install --frozen-lockfile && pnpm test && cd ..
```

## DCO sign-off

By signing off on your commits, you certify the [Developer Certificate of Origin 1.1](https://developercertificate.org/). The sign-off line is:

```
Signed-off-by: Your Name <your@email.com>
```

`git commit -s` adds it automatically. Set `git config user.name` and `git config user.email` first.

If you forget, you can fix the most recent commit with:

```bash
git commit --amend --signoff
```

For older commits use `git rebase --signoff <base>`.

## Contract-first workflow

If your change affects **public behavior** (anything visible across the port boundary):

1. Update [`CONTRACT.md`](./CONTRACT.md) first. Be explicit about inputs, outputs, error cases, and config schema.
2. Add or update the fixture in [`contract-tests/test_cases.json`](./contract-tests/test_cases.json).
3. Implement the change in **all three** languages (`python/`, `csharp/`, `typescript/`).
4. Run the contract test runners locally for all three; they MUST agree.
5. Bump `VERSION` per SemVer rules and add a `CHANGELOG.md` entry under `## [Unreleased]` with a sub-section for each language.

If your change is **not** public-behavior (refactor, test, docs, internal rename), you may touch a single language without breaking parity.

## PR checklist

Your PR description must confirm each of these:

- [ ] Commits are DCO-signed and signature-verified.
- [ ] `CONTRACT.md` updated (or marked N/A with reason).
- [ ] Contract tests added/updated and green in all three languages.
- [ ] `CHANGELOG.md` entry added under `## [Unreleased]` with all three language sub-sections.
- [ ] Security implications considered (input validation, authN/Z, supply chain). Surface anything non-trivial in the PR description.
- [ ] If a Dockerfile changed: hadolint clean, Trivy HIGH/CRITICAL = 0.

## Branch protection

`main` is protected with the following rules; CI enforces them:

- Linear history (no merge commits — rebase or squash only).
- Signed commits required.
- All required status checks must pass.
- Force push and branch deletion blocked.
- "Include administrators" is on — no bypass for anyone, including the org owner.

## Code of Conduct

Participation in this project is governed by the [Code of Conduct](./CODE_OF_CONDUCT.md). Report issues to **conduct@contriwork.dev**.

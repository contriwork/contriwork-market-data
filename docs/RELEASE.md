# Release guide

This repo ships **one version to three registries** (PyPI / NuGet / npm) in lockstep. A release is:

1. Bump `VERSION` + `CHANGELOG.md` + `VERSION_MATRIX.md` on `main`.
2. Push a signed tag `vX.Y.Z`.
3. Three independent workflows — `release-python`, `release-dotnet`, `release-npm` — build from the tag ref and publish to their registry.
4. `release-gate` watches all three and is the "did we actually ship?" signal.

There is no cross-language atomic transaction. If one publish fails, the other two still go out. `release-gate` surfaces partial failures so you don't notice them a week later.

## Prerequisites (one-time)

- `gh` authenticated (`gh auth login`) with push access to this repo.
- `uv`, `pnpm`, `dotnet`, `python3`, `unzip`, `tar` on PATH.
- Git signing configured — this repo uses signed tags (`git tag -s`).
- Registry trust policies in place:
  - **PyPI**: OIDC trusted publisher for `pypa/gh-action-pypi-publish` on this repo.
  - **NuGet**: Active trust policy scoped to `Repository: contriwork-PACKAGE_NAME` on the `ContriWork` account. The account name is **case-sensitive** on trust-policy lookup.
  - **npm**: GitHub Actions OIDC → publish with provenance.

## Happy path: `./scripts/release.sh`

```bash
./scripts/release.sh
```

The script walks through:

1. **Preflight** — clean tree, on `main`, up-to-date with origin, tools + `gh` auth present.
2. **Version** — defaults to bumping patch; validates `X.Y.Z`; aborts if tag already exists locally or on GitHub.
3. **Changelog** — opens `$EDITOR` on a template with `### Python` / `### C#` / `### npm` sections. All three sections are required (workflow enforces this).
4. **Apply** — writes `VERSION`, inserts the changelog entry after `## [Unreleased]`, updates compare links, appends a row to `VERSION_MATRIX.md` by duplicating the previous row's runtime cells.
5. **Verify** — runs `pre-commit`, packs all three languages locally, confirms the per-registry README is inside each tarball with the expected first-line header.
6. **PR** — opens `release/vX.Y.Z` → `main`, waits for CI with `gh pr checks --watch`.
7. **Tag** — after you confirm CI is green, squash-merges the PR, pulls main, creates a signed tag, pushes it.
8. **Watch** — streams the three publish workflows. Exits non-zero if any fail.

Then give the registries ~5–15 min to index and verify the package pages manually.

## Manual fallback (when the script breaks)

The script is just a driver — all state lives in `VERSION`, `CHANGELOG.md`, and the tag. If it dies mid-run, continue manually.

### 1. Bump files

```bash
echo "X.Y.Z" > VERSION
```

Edit `CHANGELOG.md`: insert after `## [Unreleased]`:

```markdown
## [X.Y.Z] — YYYY-MM-DD

<optional headline paragraph>

### Python
- <changes>

### C#
- <changes>

### npm
- <changes>
```

Update compare links at bottom:

```markdown
[Unreleased]: https://github.com/contriwork/contriwork-PACKAGE_NAME/compare/vX.Y.Z...HEAD
[X.Y.Z]: https://github.com/contriwork/contriwork-PACKAGE_NAME/compare/v<PREV>...vX.Y.Z
```

Add a row to `VERSION_MATRIX.md` matching the previous row's runtime columns.

### 2. Local verify

```bash
pre-commit run --all-files

cd csharp && dotnet restore && dotnet pack -c Release --no-restore -o /tmp/rel -p:Version=X.Y.Z
unzip -p /tmp/rel/Contriwork.PackageName.X.Y.Z.nupkg README.md | head -1
# expect: "# Contriwork.PackageName (.NET)"

cd ../typescript && pnpm install --frozen-lockfile && pnpm build && npm pack --dry-run
# expect: README.md in file list

cd ../python && rm -rf dist && uv build
tar -xOzf dist/contriwork_PACKAGE_NAME-X.Y.Z.tar.gz contriwork_PACKAGE_NAME-X.Y.Z/README.md | head -1
# expect: "# contriwork-PACKAGE_NAME (Python)"
```

### 3. Branch, PR, merge

```bash
git switch -c release/vX.Y.Z
git add VERSION CHANGELOG.md VERSION_MATRIX.md
git commit -s -m "chore(release): X.Y.Z"
git push -u origin release/vX.Y.Z

gh pr create --base main --title "chore(release): X.Y.Z" --body "See CHANGELOG.md"
gh pr checks --watch
gh pr merge --squash --delete-branch
git switch main && git pull
```

### 4. Tag + watch

```bash
git tag -s vX.Y.Z -m "vX.Y.Z"
git push origin vX.Y.Z

for wf in release-python release-dotnet release-npm; do
  RUN=$(gh run list --workflow "$wf.yml" --limit 1 --json databaseId -q '.[0].databaseId')
  gh run watch "$RUN" --exit-status || echo "❌ $wf failed"
done
```

## Partial failure

If one or two of the three workflows fail, the others have already published. You cannot unpublish from PyPI/NuGet/npm within their retention windows, so the fix is always to **publish a new patch** with the same content — never to delete.

Each retry ships a new patch version and documents WHY in the `CHANGELOG.md` entry for that version. Consumers can trust that a version number is monotonic and that skipped numbers reflect real publish attempts, not silent rewrites. Typical failure modes worth documenting when they occur: `ContriWork` account casing drift on NuGet, dormant wildcard trust policies, OIDC claim mismatches, per-registry README shipment gaps.

## Troubleshooting

### NuGet OIDC: `No matching trust policy owned by user '...' was found`

Two causes, check in this order:

1. **Account-name casing**: the NuGet account is `ContriWork` (PascalCase). The `NuGet/login` action's `user:` field is case-sensitive. The workflow pins `user: ContriWork` — do not change it to lowercase.

2. **Dormant trust policy**: wildcard (`*`) trust policies on nuget.org go into a 7-day dormant state ("Use within N days to keep it permanently active") and silently reject OIDC claims even after clicking `Activate for 7 days`. Replace the wildcard with a package-specific policy (`Repository: <repo-name>`, marked **Active**). This is the fix that unblocked `0.0.3`.

### NuGet: version missing from the flat-container index after publish

NuGet indexing takes up to 15 min. Check:

```bash
curl -s "https://api.nuget.org/v3-flatcontainer/contriwork.packagename/index.json"
```

If your `release-dotnet` workflow succeeded, the push was accepted — waiting is correct. To verify content before indexing completes, download the nupkg directly once indexed:

```bash
curl -sL "https://api.nuget.org/v3-flatcontainer/contriwork.packagename/X.Y.Z/contriwork.packagename.X.Y.Z.nupkg" -o /tmp/pkg.nupkg
unzip -p /tmp/pkg.nupkg README.md | head -5
```

### PyPI: version not visible via `pip install` for a few minutes

CDN propagation is usually under 60s but can spike to 5 min. `pip install --index-url https://pypi.org/simple/` bypasses the cache.

### npm: "No README" on the package page right after publish

npm sometimes lags on surfacing the README field. Verify the tarball itself:

```bash
curl -sL "https://registry.npmjs.org/@contriwork/PACKAGE_NAME/-/PACKAGE_NAME-X.Y.Z.tgz" -o /tmp/npm.tgz
tar -xOzf /tmp/npm.tgz package/README.md | head -5
```

If the README is in the tarball, the page will catch up. If the tarball has no README, check `typescript/package.json`'s `files` field and re-publish.

### pre-commit fails mid-script on unrelated files

The script runs `pre-commit run --all-files` before opening the PR. If it fails on files you haven't touched, your local `main` is behind or `pre-commit autoupdate` moved something. Abort, `git pull`, and re-run. If it fails on your staged edits, fix and re-run — the script re-branches from `main` each run, so your `VERSION`/`CHANGELOG`/`VERSION_MATRIX` edits on the release branch will need to be redone. To avoid that: stash your edits before re-running, pop them into the release branch manually.

### tag already exists on GitHub but workflow never ran

Someone pushed the tag without push access to workflows, or a workflow trigger was temporarily disabled. Delete the tag locally and remotely, then re-push:

```bash
git tag -d vX.Y.Z
git push --delete origin vX.Y.Z
# fix whatever blocked the workflow, then:
git tag -s vX.Y.Z -m "vX.Y.Z" && git push origin vX.Y.Z
```

**Caveat**: if any publish succeeded before you deleted the tag, the registries still have that version. Bump to a new patch instead of recycling the tag.

## What lives where

| File                                          | Role                                               |
|-----------------------------------------------|----------------------------------------------------|
| `VERSION`                                      | Single source of truth. All three release workflows read it and fail-fast if it doesn't match the tag. |
| `VERSION_MATRIX.md`                           | Per-version runtime support (Python / .NET / Node / contract rev). One row per release. |
| `CHANGELOG.md`                                | Per-release notes with `### Python` / `### C#` / `### npm` sub-sections (workflow-enforced). |
| `python/pyproject.toml`                       | `[tool.hatch.version] path = "../VERSION"` — no manual bump.  |
| `typescript/package.json`                     | `version` is rewritten at publish time via `npm version`.      |
| `csharp/src/*/*.csproj`                       | Version injected via `dotnet pack -p:Version=...`.             |
| `.github/workflows/release-*.yml`             | Three independent publishers; each verifies `VERSION == tag`. |
| `.github/workflows/release-gate.yml`          | Runs on each publisher's completion; all-or-nothing gate via `workflow_run`. |
| `scripts/release.sh`                          | Interactive driver for the happy path.             |
| `docs/RELEASE.md`                             | This file.                                         |

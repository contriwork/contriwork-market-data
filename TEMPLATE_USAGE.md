# Template Usage

You just clicked **Use this template** on `contriwork-repo-template`. Follow these steps in order. Do NOT push a release tag until step 10 is green.

The placeholder tokens used throughout the template:

| Context | Placeholder | Replace with (example `config-core`) |
|---------|-------------|--------------------------------------|
| Python dist name | `contriwork-PACKAGE_NAME` | `contriwork-config-core` |
| Python import | `contriwork_PACKAGE_NAME` | `contriwork_config_core` |
| C# namespace / assembly | `Contriwork.PackageName` | `Contriwork.ConfigCore` |
| TypeScript symbol | `PackageName` | `ConfigCore` |
| npm package | `@contriwork/PACKAGE_NAME` | `@contriwork/config-core` |

---

## 0. One-time org-level prerequisites

These belong to the ContriWork org / accounts, **not the package**. You do them once across all packages — if you've already shipped a `contriwork-*` package, you can skip this section. Listed here so future scaffolds don't re-discover the same one-time setup.

- [ ] **PyPI account** that will own `contriwork-*` package names, with 2FA enabled.
- [ ] **NuGet account `ContriWork`** (case-sensitive — the exact casing must match `vars.NUGET_ACCOUNT` in step 7) with 2FA enabled. NuGet auth is backed by a Microsoft account; 2FA is configured there.
- [ ] **npm org `contriwork`** with 2FA on org-owner accounts. Org-level "Require 2FA for publishing" is recommended.

Steps 6–8 (per-package Trusted Publisher registrations and per-repo GitHub settings) repeat for every new package — they are NOT part of this one-time list.

---

## 1. Rename directories

```bash
git mv python/src/contriwork_PACKAGE_NAME python/src/contriwork_<your_name>
git mv csharp/src/Contriwork.PackageName csharp/src/Contriwork.<YourName>
git mv csharp/tests/Contriwork.PackageName.Tests csharp/tests/Contriwork.<YourName>.Tests
```

## 2. Global find-and-replace

Use your editor's project-wide replace (case-sensitive). Do the replacements in this order to avoid partial-match collisions:

1. `@contriwork/PACKAGE_NAME` → `@contriwork/<your-name>` (kebab-case, npm)
2. `contriwork-PACKAGE_NAME` → `contriwork-<your-name>` (kebab-case, PyPI)
3. `contriwork_PACKAGE_NAME` → `contriwork_<your_name>` (snake_case, Python import)
4. `Contriwork.PackageName` → `Contriwork.<YourName>` (PascalCase, C#)
5. `PackageName` → `<YourName>` (PascalCase, TypeScript symbols)
6. `PACKAGE_NAME` → `<your-name>` or `<your_name>` — context-dependent; verify by diff.

Rename C# solution file:

```bash
git mv csharp/Contriwork.PackageName.sln csharp/Contriwork.<YourName>.sln
```

## 3. Pin Dockerfile base image digests

Every `FROM` line carries a `@sha256:TODO` placeholder. Pin each to a current digest:

```bash
docker pull python:3.13-slim-trixie
docker inspect --format '{{index .RepoDigests 0}}' python:3.13-slim-trixie
# copy the @sha256:... suffix into python/Dockerfile
```

Repeat for:

- `python/Dockerfile` — build stage `python:3.13-slim-trixie`, runtime stage `python:3.13-slim-trixie`.
- `csharp/Dockerfile` — build stage `mcr.microsoft.com/dotnet/sdk:10.0`, runtime stage `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled-extra`.
- `typescript/Dockerfile` — build stage `node:24-bookworm-slim`, runtime stage `node:24-alpine`.

## 4. Fill `CONTRACT.md`

The contract is the single source of truth. Complete every `TODO` block before writing implementation code. A PR that touches public behavior without updating `CONTRACT.md` is rejected by the PR checklist.

## 5. Fill the READMEs (four of them)

Each registry displays a **different** README from the package tarball; if any of them ships the repo-root README, consumers see cross-registry badges and sister-package references that read as noise on that registry. Keep the root README as the ecosystem landing page (GitHub repo view) and let every language directory carry its own.

- `README.md` (repo root) — ecosystem overview, all three registry badges, cross-language quick-tour. This is what shows up on GitHub.
- `python/README.md` — ships to PyPI (declared by `python/pyproject.toml`'s `readme` field).
- `csharp/README.md` — ships to NuGet (packed by `csharp/src/Contriwork.PackageName/Contriwork.PackageName.csproj` via `<None Include="..\..\README.md" Pack="true" PackagePath="\" />`).
- `typescript/README.md` — ships to npm (included by `typescript/package.json`'s `"files": ["dist", "README.md", "LICENSE"]`).

For each of the three per-registry READMEs, replace the `TODO` blocks:

- `## Install` — keep only the install command for that registry (`pip install ...` on PyPI's README, `dotnet add package ...` on NuGet's, `npm install ...` on npm's).
- `## Quick start` — one-line usage example in the matching language.
- Any cross-registry links (sister packages, root README, CONTRACT.md) must be **absolute GitHub URLs**, not relative paths — PyPI / NuGet / npm do not resolve `../` relative links.

For the repo-root `README.md`, replace the `## Why` and `## Quick start` blocks with ecosystem-level content (not language-specific). Badges auto-populate once step 2 is done.

## 6. Register PyPI Trusted Publisher

PyPI uses OIDC trusted publishers — no API tokens, no secrets in CI.

1. **Create the `pypi` GitHub environment** in the new repo: **Settings → Environments → New environment → name: `pypi`**. The release workflow's `environment: pypi` clause refers to this; `release-python.yml` will not pick up the OIDC token without it. Required reviewers are optional but recommended for a human gate before publish.

2. Sign in to <https://pypi.org/manage/account/publishing/> with the PyPI account that owns (or will own) `contriwork-<your-name>`. Reserve the name first if it's not already taken.

3. Click **Add a pending publisher** (or, if the project already exists, open the project's **Publishing** tab → **Add publisher**). Select **GitHub** and fill the form exactly:

   | Field                              | Value                          |
   |------------------------------------|--------------------------------|
   | PyPI Project Name (pending only)   | `contriwork-<your-name>`       |
   | Owner (required)                   | `contriwork`                   |
   | Repository name (required)         | `contriwork-<your-name>`       |
   | Workflow name (required)           | `release-python.yml`           |
   | Environment name (optional)        | `pypi`                         |

4. Save. The publisher starts as **pending** when the project doesn't exist yet — the first successful publish from `release-python.yml` running in the `pypi` environment converts it to permanent.

## 7. Register NuGet Trusted Publisher

1. **Create the `nuget` GitHub environment** in **Settings → Environments → New environment → name: `nuget`**.

2. **Set the `NUGET_ACCOUNT` repo variable** in **Settings → Secrets and variables → Actions → Variables → New repository variable → `NUGET_ACCOUNT = ContriWork`** (or whatever NuGet account owns this package — case-sensitive). The release workflow reads `vars.NUGET_ACCOUNT` so the same workflow file works across packages owned by different NuGet accounts without forking it.

3. Sign in to <https://www.nuget.org/account/trustedpublishing> as the NuGet account that owns (or will own) this package. The account name is **case-sensitive** and must match `vars.NUGET_ACCOUNT` from step 2 character-for-character.

4. Click **+ Create** and fill the form exactly:

   | Field             | Value                                                            |
   |-------------------|------------------------------------------------------------------|
   | Package owner     | `ContriWork` — case-sensitive, must equal `vars.NUGET_ACCOUNT`   |
   | Publisher         | **GitHub Actions**                                               |
   | Repository Owner  | `contriwork`                                                     |
   | Repository        | `contriwork-<your-name>` — **never use the wildcard `*` form** (see step 5) |
   | Workflow File     | `release-dotnet.yml`                                             |
   | Environment       | `nuget`                                                          |

   Wildcard policies on nuget.org interact badly with the dormant grace-period mechanic (step 5) and produce silent 401 rejections that are difficult to diagnose later. Per-package policies isolate the failure mode.

5. Policy state after creation is a 7-day grace period labelled **"Use within N day(s)"**. The first successful publish inside this window converts the policy to permanent. If the window expires before a publish occurs, the policy goes **dormant** and returns a silent HTTP 401 on the next OIDC exchange — even though the UI may still present it as "Active". The **Activate for 7 days** button re-opens the window but does not itself make the policy permanent; only a successful publish does. Fastest path to stability: publish within the window.

6. First tag push after the policy is created and `NUGET_ACCOUNT` is set should publish cleanly. If you see HTTP 401 despite a green OIDC token exchange, investigate in this order: (a) `NUGET_ACCOUNT` capitalization, (b) policy is not dormant (re-click "Activate for 7 days"), (c) the account actually has ownership or push rights on the package name.

## 8. Configure npm publish auth

npm Trusted Publishers is on a rolling rollout — many orgs do not see the **Trusted Publishers** tab yet. The template's default `release-npm.yml` therefore ships in token-auth mode (works for everyone) and switches to trusted-publisher OIDC by deletion of one block once your org gets the feature.

### Default path: `NPM_TOKEN` automation token

1. **Create the `npm` GitHub environment** in **Settings → Environments → New environment → name: `npm`**.

2. **The package must exist** before publish. If `@contriwork/<your-name>` has not been claimed yet, either reserve the scoped name through the `contriwork` org settings, or publish a `0.0.0` placeholder first.

3. On <https://www.npmjs.com/> as an org member: **Account → Access Tokens → Generate new token → "Automation"**, scoped to `@contriwork/*`. **npm Automation tokens expire after 90 days** — log the rotation date somewhere you'll see (calendar reminder, runbook). Rotation is a one-line `gh secret set NPM_TOKEN` overwrite; the workflow does not change.

4. Set the token as a repo secret — never paste it into chat, commits, or terminal logs that get archived:

   ```bash
   gh secret set NPM_TOKEN --repo contriwork/contriwork-<your-name>
   # the prompt accepts the token interactively
   ```

   The default `release-npm.yml` already reads `NODE_AUTH_TOKEN: ${{ secrets.NPM_TOKEN }}` — no workflow edit needed for the token path.

### Preferred path (when available): npm Trusted Publisher

If the **Trusted Publishers** tab appears on either your account settings or the `contriwork` org settings, switch to it — no token rotation, no leak surface, provenance still works.

1. Open `@contriwork/<your-name>` → **Settings → Publishing access → Trusted publisher → Add**, and fill exactly:

   | Field                  | Value                       |
   |------------------------|-----------------------------|
   | Publisher              | **GitHub Actions**          |
   | Organization or user   | `contriwork`                |
   | Repository             | `contriwork-<your-name>`    |
   | Workflow filename      | `release-npm.yml`           |
   | Environment name       | `npm`                       |

2. In `release-npm.yml`, **delete** the `env: NODE_AUTH_TOKEN: …` block from the publish step — OIDC + Trusted Publisher handles auth without the token. Keep the `id-token: write` permission and the `--provenance` flag.

3. Remove the now-unused `NPM_TOKEN` secret: `gh secret delete NPM_TOKEN --repo contriwork/contriwork-<your-name>`.

4. While on the same npm page, set **Publishing access → "Require two-factor authentication and disallow tokens (recommended)"**. npm's note confirms Trusted Publishers stay compatible with this setting, and disabling the token path closes the most common credential-leak vector.

> See [`docs/REGISTRY_BOOTSTRAP.md`](docs/REGISTRY_BOOTSTRAP.md) for the full `gh` CLI bootstrap flow (PyPI / NuGet / npm in one place), real error messages encountered in production releases, and the smoke-verify commands to run after each registry indexes the new version.

## 9. Enable branch ruleset

In **Settings → Rules → Rulesets** for this repo, apply the org default ruleset for `main`:

- Require signed commits.
- Require linear history.
- Require a pull request before merging (approvals: at least 1 for multi-dev projects; 0 is acceptable for a solo template-bootstrap phase but switch to 1 before onboarding contributors).
- **Allow merge methods: Squash ONLY.** Do NOT enable "Rebase merging" or "Merge commits". GitHub cannot produce a verified signature on rebased commits (server-side rebase changes the committer metadata), so `require signed commits` + rebase merge leaves the merged commits in an "Unverified" state that the ruleset itself then blocks. Squash merge works because GitHub signs the new single commit with its own web-flow identity, which counts as verified.
- Require status checks to pass. Modern GitHub Actions writes **only** to
  the Check Runs API (not the legacy Status API), and the Check Run name is
  the job's `name:` field alone — no `{workflow}/` prefix. A ruleset entry
  of `ci / python` therefore never matches and the ruleset hangs on
  "N of N required status checks are expected" forever. Use the plain
  Check Run names below; these are what both the exported ruleset JSON
  and the runtime output agree on:

  ```
  python
  csharp
  typescript
  contract
  gitleaks
  hadolint (Dockerfiles) (python/Dockerfile)
  hadolint (Dockerfiles) (csharp/Dockerfile)
  hadolint (Dockerfiles) (typescript/Dockerfile)
  trivy (filesystem)
  grype
  semgrep
  codeql (python, none)
  codeql (csharp, manual)
  codeql (javascript-typescript, none)
  deps (python / pip-audit)
  deps (dotnet list --vulnerable)
  deps (pnpm audit)
  SBOM (CycloneDX) (python)
  SBOM (CycloneDX) (csharp)
  SBOM (CycloneDX) (typescript)
  ```

  When you type these into the ruleset "Add checks" search box, GitHub's
  autocomplete displays them as `ci / python`, `security-scan / gitleaks`
  etc. for UI grouping — **ignore the prefix**; the stored context must be
  the short form. If autocomplete inserts the prefix anyway, edit the
  stored entry or `gh api --method PUT repos/:owner/:repo/rulesets/:id`
  with a JSON body that uses the short form.

- Block force pushes.
- Restrict deletions.
- **Bypass list empty.** No admin bypass — include administrators in the rule.

If the org ruleset is not yet configured, apply the same rules as a repo-level ruleset temporarily.

## 10. Verify locally

```bash
pre-commit install --install-hooks
pre-commit run --all-files
cd python && uv sync && uv run pytest && uv run ruff check && uv run mypy src && cd ..
cd csharp && dotnet restore && dotnet build && dotnet test && dotnet format --verify-no-changes && cd ..
cd typescript && pnpm install --frozen-lockfile && pnpm build && pnpm test && pnpm lint && pnpm typecheck && cd ..
hadolint python/Dockerfile csharp/Dockerfile typescript/Dockerfile
```

Every step green → proceed. Any red → fix before tagging.

## 11. Scaffold commit via PR

Your rename + find-replace + Dockerfile pins + `CONTRACT.md` + `README.md` edits are on the default branch locally, but have not been pushed. The ruleset blocks direct pushes to `main` (require PR + signed commits), so even the **first** scaffold commit goes through a PR.

```bash
# Create a branch for the scaffold edits
git switch -c scaffold/initial
git add -A
git commit -s -m "chore: scaffold from template (rename + digest pins)"
git push -u origin scaffold/initial

# Open the PR and watch checks
gh pr create --base main --head scaffold/initial --fill

# Wait for all 20 required checks to go green
gh pr checks --watch

# Squash-merge. Rebase is NOT allowed: GitHub cannot sign rebased commits,
# and the ruleset's "require signed commits" then blocks the merged commits.
# Squash produces a single new commit that GitHub web-flow signs as verified.
gh pr merge --squash --delete-branch

# Refresh local main to the merged state
git switch main
git fetch origin main
git reset --hard origin/main
```

If `gh pr merge --squash` fails because of branch-protection enforcement, either add your identity to the ruleset bypass list temporarily, or pass `--admin` (requires admin role on the repo) to bypass the pull-request-approval requirement while still going through the squash path.

## 12. Initial release

All three publish workflows gate on CI being green on the tagged commit. If any of PyPI / NuGet / npm publish fails, the GitHub Release is marked failed and consumers must not adopt that tag.

**Release is a two-step flow: merge the version bump via PR, then tag the merge commit.** Direct-push to `main` is blocked by the ruleset (`require a pull request before merging`) and tagging an unmerged branch commit would publish code that never landed on `main`.

```bash
# ---- on a release branch ----
git switch -c release/0.1.0

# bump VERSION and add a row to VERSION_MATRIX.md
echo "0.1.0" > VERSION
# edit CHANGELOG.md: move [Unreleased] to [0.1.0] with all three language sub-sections

git add VERSION VERSION_MATRIX.md CHANGELOG.md
git commit -s -m "chore(release): 0.1.0"
git push -u origin release/0.1.0

# ---- open a PR, wait for CI (all 20 checks green), SQUASH-MERGE via UI ----
# The squash merge produces a new commit on main that GitHub signs as
# "verified" using the web-flow identity. This is the commit we tag.

# ---- back on main, after the PR is merged ----
git switch main
git pull
git tag -s v0.1.0 -m "v0.1.0"
git push origin v0.1.0
```

Why this order: the tag MUST point at a commit that actually exists on `main`. If you tag the release branch's pre-merge commit, the three release workflows publish code that isn't on `main`, and any future CHANGELOG diff will disagree with what was published.

### If a publish step fails

- The tag stays in git, but the release is invalid. Do NOT retry the same tag — registries may reject a second attempt at the same version.
- Diagnose the root cause (check the Actions run logs; common issues: Trusted Publisher not registered, OIDC claim mismatch, package name collision, SBOM artifact upload timeout).
- Delete the remote tag, bump the patch (`0.1.1`), add a CHANGELOG note explaining the skipped version, and re-release via a new PR:

  ```bash
  git push --delete origin v0.1.0
  git tag -d v0.1.0

  git switch -c release/0.1.1
  echo "0.1.1" > VERSION
  # update CHANGELOG.md and VERSION_MATRIX.md to mark 0.1.0 as "failed — never published"
  git add VERSION CHANGELOG.md VERSION_MATRIX.md
  git commit -s -m "chore(release): skip 0.1.0, re-release as 0.1.1"
  git push -u origin release/0.1.1
  # open PR, wait for CI green, SQUASH-MERGE

  git switch main
  git pull
  git tag -s v0.1.1 -m "v0.1.1"
  git push origin v0.1.1
  ```

- Rolling back is **per-tag, not per-registry**. If one of the three succeeded and two failed, the one that succeeded is still on its registry — document it in `CHANGELOG.md` under the failed version and supersede it with the next tag.

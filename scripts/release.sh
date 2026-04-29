#!/usr/bin/env bash
# scripts/release.sh — interactive release driver for this mono-port repo.
#
# Walks through: bump VERSION + CHANGELOG + VERSION_MATRIX, verify all
# three tarballs build locally, open a release PR, squash-merge after
# CI, push a signed tag, watch the three publish workflows.
#
# Usage:  ./scripts/release.sh
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

REPO_URL="https://github.com/contriwork/contriwork-PACKAGE_NAME"
NUGET_ID="Contriwork.PackageName"
PYPI_ID="contriwork-PACKAGE_NAME"
NPM_ID="@contriwork/PACKAGE_NAME"

die()     { printf '\033[31m✘\033[0m %s\n' "$*" >&2; exit 1; }
ok()      { printf '\033[32m✓\033[0m %s\n' "$*"; }
step()    { printf '\n\033[1m━━ %s ━━\033[0m\n' "$*"; }
ask()     { local p="$1" d="${2:-}" r; read -r -p "$p${d:+ [$d]}: " r; printf '%s' "${r:-$d}"; }
confirm() { local r; r=$(ask "$1 (y/N)" "N"); [[ "$r" =~ ^[Yy]$ ]]; }

# ── preflight ───────────────────────────────────────────────────────
step "preflight"
git diff --quiet                                                 || die "working tree dirty"
git diff --cached --quiet                                        || die "staged changes present"
[[ "$(git branch --show-current)" == "main" ]]                   || die "must be on main"
git fetch --quiet origin main
[[ "$(git rev-parse HEAD)" == "$(git rev-parse origin/main)" ]]  || die "main is behind origin/main"
for t in gh uv pnpm dotnet python3 unzip tar; do
  command -v "$t" >/dev/null || die "missing tool: $t"
done
gh auth status >/dev/null 2>&1 || die "gh not authenticated (run: gh auth login)"
ok "preflight passed"

# ── input: version ──────────────────────────────────────────────────
step "version"
CUR=$(tr -d '[:space:]' < VERSION)
IFS=. read -r MAJ MIN PAT <<<"$CUR"
DEF_NEXT="${MAJ}.${MIN}.$((PAT + 1))"
NEW=$(ask "new version (current: $CUR)" "$DEF_NEXT")
[[ "$NEW" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "version must be X.Y.Z"
git rev-parse "v$NEW" >/dev/null 2>&1 && die "tag v$NEW already exists locally"
gh release view "v$NEW" >/dev/null 2>&1 && die "release v$NEW already exists on GitHub"

echo "release: $CUR → $NEW"
confirm "proceed?" || die "aborted"

# ── input: changelog (via $EDITOR) ──────────────────────────────────
step "changelog"
TODAY=$(date -u +%Y-%m-%d)
TMP=$(mktemp)
trap 'rm -f "$TMP"' EXIT

cat > "$TMP" <<EOF
## [$NEW] — $TODAY

<!-- Optional one-line headline paragraph. Delete if not needed. -->

### Python

- <your Python-side change here>

### C#

- <your C#-side change here>

### npm

- <your npm-side change here>
EOF

"${EDITOR:-vi}" "$TMP"
ENTRY=$(cat "$TMP")
for section in '### Python' '### C#' '### npm'; do
  grep -q "^${section}" <<<"$ENTRY" || die "missing section: $section"
done

# ── apply file changes ──────────────────────────────────────────────
step "apply file changes"
echo "$NEW" > VERSION

python3 - "$CUR" "$NEW" "$TMP" "$REPO_URL" <<'PYEOF'
import sys, pathlib, re
cur, new, entry_path, repo = sys.argv[1:]
entry = pathlib.Path(entry_path).read_text().rstrip() + "\n\n"
p = pathlib.Path("CHANGELOG.md")
s = p.read_text()
if "## [Unreleased]\n" not in s:
    sys.exit("CHANGELOG.md missing '## [Unreleased]' anchor")
s = s.replace("## [Unreleased]\n\n", "## [Unreleased]\n\n" + entry, 1)
s = re.sub(
    rf"\[Unreleased\]: {re.escape(repo)}/compare/v{re.escape(cur)}\.\.\.HEAD",
    f"[Unreleased]: {repo}/compare/v{new}...HEAD\n[{new}]: {repo}/compare/v{cur}...v{new}",
    s,
)
p.write_text(s)
PYEOF

python3 - "$CUR" "$NEW" "$TODAY" <<'PYEOF'
import sys, pathlib
cur, new, today = sys.argv[1:]
p = pathlib.Path("VERSION_MATRIX.md")
lines = p.read_text().splitlines(keepends=True)
last = None
for i, ln in enumerate(lines):
    if ln.startswith(f"| {cur}"):
        last = i
if last is None:
    sys.exit(f"could not find {cur} row in VERSION_MATRIX.md")
parts = lines[last].split("|")
parts[1] = f" {new}   "
parts[-2] = f" {today} "
lines.insert(last + 1, "|".join(parts))
p.write_text("".join(lines))
PYEOF

ok "VERSION, CHANGELOG.md, VERSION_MATRIX.md updated"

# ── local verify all three tarballs ─────────────────────────────────
step "local verify"
pre-commit run --all-files

VERIFY_DIR=$(mktemp -d)
trap 'rm -rf "$TMP" "$VERIFY_DIR"' EXIT

(cd csharp && dotnet restore >/dev/null && \
   dotnet pack -c Release --no-restore -o "$VERIFY_DIR" -p:Version="$NEW" >/dev/null)
unzip -p "$VERIFY_DIR/$NUGET_ID.$NEW.nupkg" README.md \
  | head -1 | grep -q "^# $NUGET_ID" || die "NuGet README not C#-tailored"
ok "NuGet tarball OK"

(cd typescript && pnpm install --frozen-lockfile >/dev/null && pnpm build >/dev/null)
(cd typescript && npm pack --dry-run 2>&1 | grep -q 'README.md') \
  || die "npm tarball missing README.md"
ok "npm tarball OK"

(cd python && rm -rf dist && uv build >/dev/null)
SDIST="python/dist/${PYPI_ID//-/_}-$NEW.tar.gz"
test -f "$SDIST" || die "python sdist version mismatch (expected $SDIST)"
ok "python sdist OK"

# ── branch + PR ─────────────────────────────────────────────────────
BRANCH="release/v$NEW"
step "branch + PR"
git switch -c "$BRANCH"
git add VERSION CHANGELOG.md VERSION_MATRIX.md
git commit -s -m "chore(release): $NEW"
git push -u origin "$BRANCH"

PR_URL=$(gh pr create --base main --head "$BRANCH" \
  --title "chore(release): $NEW" \
  --body "Release **v$NEW**. See \`CHANGELOG.md\` for per-language entries.")
echo "PR: $PR_URL"

step "wait for PR CI"
gh pr checks --watch

confirm "CI green — merge, tag, and publish v$NEW?" || die "aborted before merge"

gh pr merge --squash --delete-branch
git switch main && git pull

# ── tag + push ──────────────────────────────────────────────────────
step "tag"
git tag -s "v$NEW" -m "v$NEW"
git push origin "v$NEW"
ok "tag v$NEW pushed"

# ── watch the three publish workflows ───────────────────────────────
step "watch publish workflows"
sleep 5
FAILED=()
for wf in release-python release-dotnet release-npm; do
  RUN=""
  for _ in $(seq 1 15); do
    RUN=$(gh run list --workflow "$wf.yml" --limit 5 \
         --json databaseId,headBranch -q ".[] | select(.headBranch == \"v$NEW\") | .databaseId" | head -1)
    [[ -n "$RUN" ]] && break
    sleep 3
  done
  if [[ -z "$RUN" ]]; then
    echo "⏭  $wf run not visible yet — check 'gh run list'"
    FAILED+=("$wf(not-found)")
    continue
  fi
  echo "↓ $wf (run $RUN)"
  if gh run watch "$RUN" --exit-status >/dev/null; then
    ok "$wf succeeded"
  else
    echo "✘ $wf failed — 'gh run view $RUN' to inspect"
    FAILED+=("$wf")
  fi
done

# ── done ────────────────────────────────────────────────────────────
cat <<EOF

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 v$NEW tagged${FAILED:+ (WITH FAILURES — see above)}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Registry indexing takes ~5–15 min after a successful publish.

  PyPI:  https://pypi.org/project/$PYPI_ID/$NEW/
  NuGet: https://www.nuget.org/packages/$NUGET_ID/$NEW
  npm:   https://www.npmjs.com/package/$NPM_ID/v/$NEW

EOF

if [[ ${#FAILED[@]} -gt 0 ]]; then
  echo "Failed workflows: ${FAILED[*]}"
  echo "See docs/RELEASE.md → 'Partial failure' for recovery."
  exit 1
fi

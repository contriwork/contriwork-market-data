# Security Policy

## Reporting a Vulnerability

**Do not file public issues for security vulnerabilities.**

Use GitHub's private vulnerability reporting:
1. Go to the **Security** tab of this repository.
2. Click **Report a vulnerability**.
3. Fill in the form. We acknowledge within **3 business days** and aim to ship a fix within **30 days** for HIGH/CRITICAL issues.

If GitHub private reporting is unavailable, email **security@contriwork.dev** (PGP key: TBA).

## Coordinated Disclosure

We follow a **coordinated disclosure** model:
- We do not publicly discuss the issue until a fix is available.
- We credit the reporter in `CHANGELOG.md` unless they opt out.
- A CVE is requested for any HIGH/CRITICAL vulnerability that affects a published release.

## Hardening Posture

This package is developed with an adversarial mindset and validated against three attacker viewpoints before each release:

- **Black-box** — fuzz public APIs, HTTP surfaces, and input validators with no source access.
- **Gray-box** — review authZ boundaries, trust assumptions, and adapter handoffs for privilege-escalation and confused-deputy vectors.
- **White-box** — full source review covering deserialization, SSRF, path-traversal, timing side-channels, and supply-chain vectors.

Findings — including any zero-day-class issues uncovered during development — are fixed before the affected version is tagged. We do not publish known-exploitable versions to PyPI, NuGet, or npm.

## Hardened Against (per release)

> **TODO:** This list MUST be updated each release with the actual attack classes the current version is hardened against. Empty rows are not acceptable for tagged releases.

| Version | Attack class           | Status   | Notes |
|---------|------------------------|----------|-------|
| 0.0.0   | _scaffold, no surface_ | n/a      | initial template |

## Scan Tooling (CI gates)

The release workflow blocks publication if any of the following report HIGH/CRITICAL findings:

| Layer       | Python                  | C#                              | TypeScript                  |
|-------------|-------------------------|---------------------------------|------------------------------|
| SAST        | Bandit, Semgrep         | CodeQL, Security Code Scan      | CodeQL, Semgrep, eslint-plugin-security |
| Deps        | pip-audit, safety       | dotnet list --vulnerable        | pnpm audit                   |
| Container   | Trivy, Grype, hadolint  | Trivy, Grype, hadolint          | Trivy, Grype, hadolint       |
| Secrets     | gitleaks (pre-commit + CI)                                                                |
| SBOM        | CycloneDX (per language, attached to release)                                             |

## Out of Scope

- Vulnerabilities in dependencies that already have a published advisory and a release available — please report upstream first, then open a PR here updating the lockfile.
- Issues in example/demo code clearly marked as such.
- Self-XSS, social engineering, physical access, denial-of-service via legitimate use of public APIs.

## Safe Harbor

We support good-faith security research. We will not pursue legal action against researchers who:
- Make a good-faith effort to avoid privacy violations, data destruction, and service degradation.
- Report findings through the channels above and allow us a reasonable disclosure window.

# Security Policy

## Supported versions

Security fixes target the latest Tonarink release. Users should upgrade before
reporting an issue that affects an older build.

## Reporting a vulnerability

Please report vulnerabilities through the repository's
[private vulnerability form](https://github.com/kusutori/Tonarink/security/advisories/new).
Include the affected version, reproduction steps, impact, and any proposed
mitigation. Do not attach private transferred files, PINs, certificates, or
unreviewed logs.

Use a regular GitHub issue for non-sensitive bugs. Please do not publicly disclose
a vulnerability before a fix or coordinated disclosure is ready.

## Release integrity

Official releases are built from tagged commits by GitHub Actions. The current
GitHub Release MSIX packages use the project's documented sideload certificate.
The intended production-signing transition and artifact scope are recorded in
`CODE_SIGNING_POLICY.md`.

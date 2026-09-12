# Tonarink Code-Signing Policy

## Project and release source

Official Tonarink source code is hosted at
https://github.com/kusutori/Tonarink. Release builds are produced from annotated
`app-v*` tags by GitHub-hosted Actions and published on the repository's Releases
page. The source commit, workflow run, and resulting artifacts must remain
traceable to one another.

## Signed artifacts

Production signing is intended for the public Windows packages that users run or
install: the x64 and ARM64 MSIX packages and the executable files contained in
supported portable packages. Experimental widget variants are not part of the
initial production-signing scope. Unsigned intermediate artifacts must never be
presented as production releases.

Until a publicly trusted signing service is approved and integrated, GitHub
Release sideload packages use the project's self-signed certificate and require
the explicit trust/install flow documented with the release. Microsoft Store
packages will be re-signed by Microsoft during Store certification.

Tonarink is applying for **Free code signing provided by SignPath.io, certificate
by SignPath Foundation**. This statement describes the planned service and must
not be interpreted as confirmation that current artifacts are SignPath-signed;
the release notes and signature properties are authoritative for each artifact.

## Authorization and controls

The current signing team roles are:

- Authors: [`kusutori`](https://github.com/kusutori), who maintains the source
  code and build workflows.
- Reviewers: [`kusutori`](https://github.com/kusutori), who reviews external
  contributions and signing-sensitive changes.
- Approvers: [`kusutori`](https://github.com/kusutori), who manually approves
  production signing requests and releases.

Signing requests must originate from the public repository's GitHub Actions
workflow on GitHub-hosted runners. Private keys must not be committed to the
repository or included in build artifacts. Signing-service tokens are stored only
as protected GitHub Actions secrets. Every team member must use multi-factor
authentication for GitHub and SignPath access.

A production signing workflow must verify the release tag and version, build from
the tagged commit, upload the unsigned artifact to GitHub Actions before the
signing request, wait for signing to complete, and publish only the returned
signed artifact. Signature verification and artifact hashes must be checked before
release publication.

Packaged releases can be uninstalled from Windows Settings or the Start menu.
Portable releases can be removed by closing the application and deleting the
extracted directory. Features that change Windows integration are exposed through
the packaged app, its manifest, or explicit user-facing settings.

## Privacy and incident response

The application privacy policy is available in [PRIVACY.md](PRIVACY.md). Suspected
key compromise, unauthorized signing, malicious releases, or vulnerabilities must
be reported according to [SECURITY.md](SECURITY.md). Affected releases will be
withdrawn or replaced and users will be informed through the repository's release
and security channels.

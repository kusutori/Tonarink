# Distribution preparation

This document tracks the remaining work for Microsoft Store, SignPath Foundation,
and WinGet distribution. It deliberately does not contain account credentials or
enable automatic production submission before the product identities exist.

## Prepared in the repository

- English and Simplified Chinese Store copy under `store-listing/`, synchronized
  through Crowdin project `929895`.
- Structural validation in `.github/workflows/store-listing.yml`.
- Public privacy, security, and code-signing policies at the repository root.
- Existing automated GitHub Release builds for x64 and ARM64.

## Microsoft Store

For the first submission:

1. Reserve `Tonarink` in Partner Center and associate the package identity with
   `src/Tonarink.App/Package.appxmanifest`. Replace the development identity and
   publisher values with the exact values assigned by Partner Center.
2. Select one supported product package. The standard managed MSIX is the safest
   first Store package; keep experimental Widgets packages out of the main Store
   listing. Include x64 and ARM64 in one submission.
3. Complete category, properties, age rating, privacy URL, support URL, pricing,
   and the English and Simplified Chinese listings. Each listing needs at least a
   description and screenshot; use the final screenshots and video prepared for
   the product page.
4. Submit the first version manually. Store automation applies to an already-live
   product, and Microsoft currently documents automated updates for free products.

Use these URLs when the corresponding Partner Center fields are requested:

- Privacy: `https://github.com/kusutori/Tonarink/blob/main/PRIVACY.md`
- Support: `https://github.com/kusutori/Tonarink/issues`
- Website: `https://github.com/kusutori/Tonarink`

After the product is live, install Microsoft Store Developer CLI and fetch the
real product metadata. Build an adapter that merges the localized
`store-listing/<locale>/listing.json` values into that Store-owned metadata. The
repository JSON is intentionally not treated as a Partner Center export.

For CI updates, create a protected GitHub environment named `microsoft-store`,
prefer required reviewer approval, and add:

| Kind | Name |
| --- | --- |
| Secret | `AZURE_AD_APPLICATION_CLIENT_ID` |
| Secret | `AZURE_AD_APPLICATION_SECRET` |
| Secret | `AZURE_AD_TENANT_ID` |
| Secret | `SELLER_ID` |
| Variable | `MICROSOFT_STORE_PRODUCT_ID` |

The future workflow can use `microsoft/microsoft-store-apppublisher` to configure
`msstore`, then publish the Store-ready package and metadata. Keep Store publishing
separate from the GitHub Release job so a release can be built and reviewed before
the protected environment approves submission.

## Crowdin and Store languages

The source language is `en-US`; `zh-CN` is currently downloaded by CI because the
Windows app currently declares those two languages. To add another Store language:

1. Finish and review that locale in Crowdin.
2. Add the matching app resource language and package manifest resource.
3. Remove or broaden `download_language: zh-CN` in the Crowdin workflow.
4. Run `./tools/Test-StoreListing.ps1` and add the locale in Partner Center.

This avoids advertising a Store language that the installed UI does not support.

## SignPath Foundation

Apply only after the public policy pages are visible. The application should link
to this repository, its Releases page, `PRIVACY.md`, `SECURITY.md`, and
`CODE_SIGNING_POLICY.md`. Tonarink already has an OSI-approved Apache-2.0 license,
public source, tagged releases, and GitHub-hosted builds; SignPath Foundation makes
the final eligibility decision.

After approval:

1. Install the SignPath GitHub App and connect this repository to the assigned
   organization and project.
2. Define an artifact configuration for the exact MSIX/portable layout and a
   release signing policy. Do not guess these slugs before the SignPath project is
   provisioned.
3. Add `SIGNPATH_API_TOKEN` as a protected secret. Store the organization ID,
   project slug, signing-policy slug, and artifact-configuration slug as repository
   or environment variables.
4. Update the release workflow to upload the unsigned artifact first, submit its
   GitHub artifact ID through `signpath/github-action-submit-signing-request@v2`,
   wait for completion, verify the returned signature, and publish only the signed
   result.
5. Replace the provisional wording in `CODE_SIGNING_POLICY.md` once signing is
   active and add the required SignPath attribution to download/release pages.

SignPath keeps the certificate key in its signing infrastructure, so this replaces
the current repository PFX secret rather than adding another private certificate
to GitHub.

## WinGet

Do not submit the current self-signed sideload ZIP. The community repository does
not accept scripts as installers, and an MSIX must carry a trusted signature.
Once SignPath signing is live:

1. Publish immutable, versioned, directly downloadable signed `.msix` or
   `.msixbundle` assets in GitHub Releases (not only ZIPs containing `Install.ps1`).
2. Verify install, upgrade, uninstall, x64, and ARM64 behavior in clean Windows
   environments and calculate both installer and MSIX signature hashes.
3. Create the first manifest with `wingetcreate new` and submit it for review.
4. After the package identifier is accepted, add a separate update workflow using
   `wingetcreate update --submit`; protect its GitHub token and run it only after
   the signed Release assets are final.

The Store and WinGet channels may use different signatures and package identities;
do not replace the public download assets with Store-re-signed packages unless the
identity and update behavior have been tested explicitly.

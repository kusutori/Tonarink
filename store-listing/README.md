# Microsoft Store listing copy

`en-US/listing.json` is the source of truth for translatable Microsoft Store
copy. Crowdin project `929895` manages translations and writes supported locales
to `%locale%/listing.json`. Checked-in locales are `en-US`, `zh-CN`, `ja-JP`,
`ko-KR`, `fr-FR`, and `es-ES`. CI still downloads only `zh-CN` from Crowdin.

These JSON files are a stable, reviewable source format, not a Partner Center
submission export. After the first Store product is created, the publishing
adapter should merge this copy into metadata obtained with Microsoft Store
Developer CLI. Do not add Product IDs, tenant credentials, or media binaries to
these files.

Validate all checked-in locales with:

```powershell
./tools/Test-StoreListing.ps1
```

Screenshots and trailers remain outside this folder until the final media set is
selected. See `docs/distribution.md` for the remaining publishing steps.

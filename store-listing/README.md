# Microsoft Store listing copy

`en-US/listing.json` is the source of truth for translatable Microsoft Store
copy. Crowdin project `929895` manages translations and writes supported locales
to `%locale%/listing.json`. The app currently publishes English and Simplified
Chinese resources, so CI intentionally downloads only `zh-CN`; additional Store
languages should be enabled when the corresponding app language is ready.

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

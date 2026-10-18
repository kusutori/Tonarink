# Shared Core text

`LocalSendDotNet.Resources` owns the text catalog used by the C# Core, its
portable/mobile transport, and the independent F# Core. It contains no Core,
App, WinUI, ASP.NET Core or platform-specific dependency.

- `Messages.resx`: English fallback
- `Messages.zh/ja/ko/fr/es.resx`: Chinese, Japanese, Korean, French and Spanish
- `CoreMessage`: stable resource identifiers, not translated wire values
- `CoreText`: typed text accessors and formatting; diagnostics use the current
  async operation's `CultureInfo.CurrentUICulture`
- `CoreText.Get` / `FormatInCulture`: explicit-culture access without changing
  process-wide culture
- `CoreText.WebShare`: the same catalog for browser pages, using their explicit
  configured UI culture, with reflection-free JSON writing

Language selection uses the culture's two-letter language, so `ja-JP`, `ja` and
other Japanese regional variants share the Japanese resources. Unsupported or
invalid explicit cultures fall back to English. Translation files are embedded
directly in this assembly, not satellite DLLs, to keep single-file/AOT publishing
self-contained. Browser `{count}` and diagnostic `{0}` placeholders must remain
consistent across all six files.

Only owned human-readable messages are translated. Protocol field names,
routes, MIME types, certificate subjects, HTTP reason phrases, structured log
templates and their property names stay invariant. Original OS/library/peer
exception messages and inner exceptions are retained for diagnosis, not guessed
at or machine-translated.

Transport decisions never inspect displayed strings: HTTP 409 represents busy,
429 represents request rate limiting, and checksum/length validation uses typed
failure codes (checksum mismatch is HTTP 422). A generic 429 does not prove that
PIN attempts were the cause. The public outgoing result is now `RateLimited`,
not the former PIN-specific case; there is no compatibility alias.

This project is not independently packed or published. The existing Core NuGet
package includes both Core and Resources DLLs under `lib/net11.0`, and has no
separate Resources package dependency. Direct source consumers that need to call
`CoreText` themselves should explicitly reference this project; Core-only source
consumers still receive its runtime assembly transitively.

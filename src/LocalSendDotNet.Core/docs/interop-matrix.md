# Official-client interoperability matrix

This document separates observed official-client behavior from in-process protocol coverage. The official client used on 2026-08-10 was an Android LocalSend client advertising protocol v2.1 as `不错的柠檬` at `192.168.31.72:53317` over HTTPS.

## Observed on a real official client

| Scenario | Status | Evidence |
|---|---|---|
| Official client discovers the .NET CLI | Passed | The Android device displayed the computer. |
| .NET CLI discovers the official client | Passed | CLI resolved alias, endpoint, fingerprint and v2.1. |
| .NET sends a file to official | Passed | `README.md`, 1,438 bytes, completed on both sides. |
| Official sends files/text to .NET | Passed | Two text items and two copies of a 434,731-byte PNG were atomically published. |
| Duplicate filename collision | Passed | The second PNG used the `(1)` suffix and both SHA-256 values matched. |
| Temporary-file cleanup | Passed | No `.part-*` file remained after the official transfers. |

## Automated protocol coverage

- HTTPS mutual identity and fingerprint pinning
- single, multiple and concurrent file uploads
- text, partial acceptance and folder-relative names
- correct, missing and incorrect PIN behavior and lockout
- local and remote cancellation paths
- wrong and reused upload tokens
- length mismatch, truncated body and SHA-256 mismatch
- safe collision handling, path traversal and linked-directory rejection
- busy receiver admission, abandoned-session timeout and temporary-file cleanup
- generated 32 MiB streaming transfer above Kestrel's former default body limit

## Remaining manual official-client checks

These require interactive actions in an official client and are not CI blockers:

- one multi-file offer in each direction
- partial acceptance by the official client
- sender cancellation and receiver cancellation in both directions
- official-client PIN prompt, incorrect PIN, correct PIN and post-lockout recovery
- deliberate fingerprint mismatch against an official client
- sleep/resume and Wi-Fi/VPN interface change while both clients remain open

### CLI commands

Install the Windows Tonarink MSIX with CLI support; the `tonarink` execution
alias reuses the main app's identity and settings. See [CLI usage](../../../docs/cli.md).
The independent `src/Tonarink.Cli` reuses the same System.CommandLine command
tree without depending on the Windows GUI. The older trial CLI project has
been removed; the historical official-client observations above are unchanged.

```powershell
tonarink discover --seconds 10
tonarink receive watch --auto-accept --directory artifacts/interop-received
tonarink send --target "Device Alias" FILE1 FILE2
tonarink send --target "Device Alias" --text "interop text"
```

Record the official app version, operating system, date and result when completing a remaining row. A future v3 implementation must add a separate matrix rather than replacing this v2 evidence.

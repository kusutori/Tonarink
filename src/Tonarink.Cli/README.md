# tonarink-cli

Standalone [Tonarink](https://github.com/kusutori/Tonarink) command-line client for the LocalSend protocol. Discover devices, send files and text, receive transfers, and manage a background server without the desktop app.

## Install

Choose one distribution; both expose the same `tonarink-cli` command.

- **NuGet / .NET tool (JIT):** `dotnet tool install --global tonarink-cli`
- **Windows portable (Native AOT):** `winget install --id kusutori.tonarink-cli --exact --source winget`

The .NET tool currently targets **.NET 11 RC** and needs matching `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App` shared runtimes. Installing the .NET 11 SDK supplies both. It is framework-dependent, not an AOT executable.

The Windows x64 / ARM64 portable packages need neither .NET nor WinUI. WinGet registers the portable command in PATH. Alternatively, extract the matching ZIP from [CLI releases](https://github.com/kusutori/Tonarink/releases?q=cli-v&expanded=true) and run `tonarink-cli.exe` directly; manual extraction does not register PATH. Open a new terminal if a newly installed command is not found.

## Use

```powershell
tonarink-cli --help
tonarink-cli --language zh-CN --help
tonarink-cli discover --seconds 5
tonarink-cli send --target "My phone" ./photo.jpg
tonarink-cli send --target "My phone" --text "Hello"
tonarink-cli server start
tonarink-cli receive watch
tonarink-cli server stop
tonarink-cli app quit --yes
```

Commands that need a service start a background host automatically. `server stop` releases the transfer port but keeps the host alive; `app quit --yes` exits the host. **Quit the host before upgrading or uninstalling** to release executable file locks. With a custom `--profile`, quit that profile's host as well.

Settings and identity are separate from the desktop app, in `%LocalAppData%\Tonarink.Cli` on Windows. Use `--profile DIRECTORY` to isolate them further. When running alongside the GUI, configure another listening port with `tonarink-cli settings set port 53318` before starting the CLI server.

Help and messages support English and Simplified Chinese. Use `--json` for machine-readable results and `--language en-US`, `zh-CN`, or `system` to select the language for one invocation.

See the [full command reference](https://github.com/kusutori/Tonarink/blob/main/docs/cli.md). The integrated desktop alias is `tonarink`; the independently distributed tool is **`tonarink-cli`**. `app open` and desktop integration settings are not available in the standalone tool.

Apache-2.0; see LICENSE and NOTICE.

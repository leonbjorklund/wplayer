# WPlayer

Small Windows 11 WPF media-session widget. `README.md` is the user-facing overview.

## Product invariants

- Preserve session selection, source focusing, and per-app volume behavior.

## Commands

```powershell
dotnet build
dotnet test .\WPlayer.Tests\WPlayer.Tests.csproj
dotnet run
.\scripts\package-release.ps1 # direct artifacts
.\scripts\package-store.ps1   # Store MSIX
```

## Troubleshooting

- Stop a running WPlayer before rebuilding if it locks the output.
- Logs: `%LOCALAPPDATA%\WPlayer\wplayer.log` and `wplayer.previous.log`.
- After runtime changes, confirm `Get-Process WPlayer | Select-Object Path` points to `bin\Debug`.

## Verification

- Runtime or release changes: `.\scripts\release-smoke.ps1`; use its `WPlayer.dll` comparison to prove installed code is current.
- Update changes: `.\scripts\update-smoke.ps1 -SourceVersion <installed-version>`; `WPLAYER_UPDATE_SOURCE` overrides the source.
- Resource-sensitive changes or production release: `.\scripts\resource-smoke.ps1`.

## Architecture

- Use WPF and built-in Windows media/session APIs; keep integrations app-agnostic.
- `AppConfig.cs` owns schema and defaults; Settings writes changes live. Direct config is `%LOCALAPPDATA%\WPlayer\config.json`; Store config uses MSIX-managed storage.
- Keep `ShowInTaskbar="True"`; native `WS_EX_TOOLWINDOW` handles taskbar/Alt-Tab visibility.
- Keep the `WindowZOrder` shell watcher; it fixes taskbar ordering after `Win+D`.

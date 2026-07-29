# WPlayer

Small Windows 11 media-session widget. Built with C# and WPF.

<img src="readme-img.png" alt="WPlayer preview composition" width="460">

## Install

- **[Microsoft Store](https://apps.microsoft.com/detail/9P2KW14109F8)** - recommended.
- **[GitHub Releases](https://github.com/leonbjorklund/wplayer/releases/latest)** - `WPlayer-Setup.exe`. Currently unsigned, Windows might show a warning.

## Controls

- Right-click for settings, dragging, and exit.
- Drag player's right edge to resize.
- Click text area to focus playing app.
- Scroll text area to adjust app or system volume.
- Click cycle button to switch playback source.

## Development

```powershell
dotnet run
dotnet build
dotnet test .\WPlayer.Tests\WPlayer.Tests.csproj
```

## Support

Direct installs store settings in `%LOCALAPPDATA%\WPlayer\config.json`; Store installs use Windows-managed app storage.

## License

[MIT](LICENSE)

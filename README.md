# WPlayer

Small Windows 11 media-session widget. Built with C# and WPF.

<img src="readme-img.png" alt="WPlayer preview composition" width="460">

## Install

Public downloads are not available yet. At launch:

- **Microsoft Store** - recommended.
- **GitHub Releases** - download `WPlayer-Setup.exe`. It is unsigned, so Windows may show a security warning.

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

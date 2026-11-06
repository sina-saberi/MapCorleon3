# MapCorleon

Production-oriented WPF map location manager scaffold. See [Architecture](docs/ARCHITECTURE.md).

## Run

```powershell
dotnet restore MapCorleon.sln
dotnet run --project src/MapCorleon/MapCorleon.csproj
```

Closing the main window keeps MapCorleon running in the Windows system tray.
Use **Exit** from the tray menu to stop the application and any active background download.

See [Skyrim fullscreen overlay](docs/skyrim-overlay.md) for the optional SKSE plugin.

## Production publish

```powershell
dotnet publish src/MapCorleon/MapCorleon.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=embedded `
  -o artifacts/MapCorleon-1.0.12-win-x64
```

The resulting `MapCorleon.exe` is self-contained. Keep the executable at the
same path when replacing it with a newer build so pinned shortcuts still work.

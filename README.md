# MapCorleon

Production-oriented WPF map location manager scaffold. See [Architecture](docs/ARCHITECTURE.md).

## Run

```powershell
dotnet restore MapCorleon.sln
dotnet run --project src/MapCorleon/MapCorleon.csproj
```

Closing the main window keeps MapCorleon running in the Windows system tray.
Use **Exit** from the tray menu to stop the application and any active background download.

## Production publish

```powershell
dotnet publish src/MapCorleon/MapCorleon.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/MapCorleon-1.0.1-win-x64
```

Run `MapCorleon.exe` from the resulting folder. Keep the other published files
beside the executable when copying the application to another computer.

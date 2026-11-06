# MapCorleon

MapCorleon is a Windows interactive game map, collectible tracker, and companion overlay for **Skyrim Special Edition**, **Fallout 4**, and **Kingdom Come: Deliverance II**. Import MapGenie maps, track discoveries across playthroughs, and see nearby uncollected locations while you play.

## Features

- Search locations, filter and collapse categories, and track collection progress. Add custom pins and use timelines to keep different playthroughs separate.
- Browse location descriptions and image galleries. Download map tiles and images for local use.
- Follow your live player position on supported game builds. After manual map navigation, Follow Player resumes after 15 seconds when enabled.
- See nearby location alerts and mark locations from the overlay. **F7** opens the nearby panel and **F8** toggles Follow Player by default. Set the hotkeys and alert options in **Settings → Game overlay**.
- Resume each map with its own position and zoom, category expansion, selected location and timeline, and window placement.

## Player tracking and overlays

| Game | Supported maps | Overlay |
| --- | --- | --- |
| Skyrim Special Edition / Anniversary Edition | Skyrim, Solstheim | Optional [SKSE fullscreen overlay](docs/skyrim-overlay.md) |
| Fallout 4 | Commonwealth, Far Harbor, Nuka-World | [Windows desktop overlay](docs/fallout4-tracking.md) for windowed or borderless play |
| Kingdom Come: Deliverance II | Trosky, Kuttenberg, Sedletz Monastery | [Windows desktop overlay](docs/kcd2-tracking.md) for windowed or borderless play |

The tracking guides list the supported game versions, setup steps, and verification notes. Standard map browsing and progress tracking work with the game closed.

## Get started

On Windows, install the .NET 8 SDK or use Visual Studio with the .NET desktop development workload. From the repository root, run:

```powershell
dotnet run --project src/MapCorleon/MapCorleon.csproj
```

1. Use **Import map** in the sidebar and paste a MapGenie map URL. The KCD2 Sedletz option imports from the installed game.
2. Choose a map and timeline to track your progress.
3. For live tracking, open **Settings → Game launch**, set your game executable, and select the map that matches your current region.

Closing the window leaves MapCorleon in the system tray. Open MapCorleon again to return to it, or choose **Exit** from the tray menu to stop it.

## Build a standalone executable

```powershell
dotnet publish src/MapCorleon/MapCorleon.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=embedded `
  -o artifacts/MapCorleon-win-x64
```

The output contains `MapCorleon.exe`. Keep the same executable path when replacing a build you have pinned. See the [architecture guide](docs/ARCHITECTURE.md) for the project layout.

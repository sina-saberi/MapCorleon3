# Skyrim fullscreen overlay

The overlay runs inside Skyrim through SKSE Menu Framework and draws into the game's DirectX frame. MapCorleon supplies nearby locations and handles progress and follow actions through a same-user Windows named pipe. Keep MapCorleon running with the Skyrim world map selected. It can stay in the tray.

## Runtime requirements

- Skyrim Anniversary Edition for Steam, executable version `1.6.1170`
- [SKSE64](https://skse.silverlock.org/download/archive/) for that executable, version `2.2.6`
- [SKSE Menu Framework 3](https://www.nexusmods.com/skyrimspecialedition/mods/120352) for the same Skyrim runtime, with its required dependencies

The desktop tracker uses version-specific memory offsets. Other executables, including GOG `1.6.1179`, need their own validated tracking support.

## Build and install

Use CMake 3.25 or newer and Visual Studio Build Tools with MSVC x64 and the Windows SDK. From the repository root, run the following commands.

```powershell
cmake -S src/MapCorleon.SkyrimOverlay -B artifacts/skyrim-overlay -A x64
cmake --build artifacts/skyrim-overlay --config Release --parallel
cmake --install artifacts/skyrim-overlay --config Release --component Overlay --prefix artifacts/skyrim-overlay-package
dotnet build MapCorleon.sln
```

Install the package's `SKSE` directory under Skyrim's `Data` directory, or package it as a mod through your mod manager. Start Skyrim through the matching SKSE loader and open MapCorleon.

Use the desktop build from this branch. When testing with a previous MapCorleon build open, exit that instance from the tray before starting the new one. Skyrim loads SKSE plugins at launch, so restart it after installing the plugin or framework. `MapCorleonOverlay.log` in Skyrim's SKSE log directory reports DLL load and framework registration.

## Controls

- Nearby uncollected locations show a notification for four seconds by default. A location that stays nearby does not keep refreshing its notification.
- **F7** opens the panel with up to three nearby uncollected locations. **Collect** marks the location for the active MapCorleon timeline. **X** hides a card until you leave its area. **F7**, **Esc**, and **Close** close the panel and return control to Skyrim.
- **F8** toggles the desktop map's Follow Player setting and shows a two-second confirmation. Manual map navigation still uses the existing 15-second resume timer.

Open **Settings → Game overlay** in MapCorleon to choose Skyrim's nearby radius, alert duration, categories, and hotkeys. Category selections apply to alerts and the nearby panel. The updated plugin reads saved settings while Skyrim is running. With custom bindings off, it uses the keys in `Data/SKSE/Plugins/MapCorleonOverlay.ini`, which default to F7 and F8.

To verify in exclusive fullscreen, approach an uncollected marker outdoors. Open the controls, click **X** or **Collect**, then close the overlay. Confirm Skyrim stays in the foreground, the click does not trigger a gameplay action, and mouse-look resumes. Check **Collect** on the active MapCorleon timeline and **F8** against the desktop follow button. Repeat with the other overlays and renderer mods you normally use.

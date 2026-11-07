# Kingdom Come: Deliverance II tracking

The KCD2 preview targets Steam build `23914554`, executable `1.5.6.0`. It tracks Trosky, Kuttenberg, and Sedletz Monastery. Select the map that matches the loaded region. The tracker reports when the game is in a different level.

## Setup

1. For Trosky or Kuttenberg, select the existing imported map. In **Settings → Game launch**, choose the installed `KingdomCome.exe` if you want to launch from MapCorleon.
2. Sedletz is available if it was already imported by an earlier version.
3. Keep MapCorleon running and focus the game in borderless or windowed mode. Nearby cards expire automatically. **F7** opens the clickable panel. **F8** toggles desktop Follow Player. **Settings → Game overlay** controls the keys, radius in meters, alert duration, and categories.

## Position source and verification

The reader verifies the loaded `WHGame.dll` timestamp and image size. The installed game's `wh_rpg_getLocation` implementation reads the primary 3D observer. MapCorleon reads the same XYZ fields through read-only process access. It does not invoke the command. The normal-mode Trosky session confirmed the coordinates changed as the player moved.

The level is read from the newest loading banner in the game's `kcd.log`. The Trosky and Kuttenberg projections were fitted against 15 and 22 matching fast-travel points in the installed level archives and MapGenie maps. Kuttenberg's projection includes the map's rotation. The Sedletz projection uses the local map rectangle and orientation from the installed tables.

Hardcore hides Henry's in-game map position. The MapCorleon reader uses the world observer independently of that UI. A playable Hardcore save is still needed to verify tracking and hotkeys in that mode. Live checks in Kuttenberg and Sedletz are also pending. [Warhorse Hardcore announcement](https://www.gamespress.com/de/Warhorse-Studios-Unleashes-Hardcore-Mode-into-Kingdom-Come-Deliverance) · [Sedletz DLC](https://www.deepsilver.com/games/kingdom-come-deliverance-ii/mysteria-ecclesiae)

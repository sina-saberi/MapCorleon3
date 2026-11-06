# Fallout 4 and Skyrim player tracking

The desktop tracker supports MapGenie Commonwealth, Far Harbor, and Nuka-World on Fallout 4 `1.11.221.0`. It also supports Skyrim and Solstheim on Skyrim SE `1.6.1170`. Select the map that matches the loaded game area. A different worldspace or an interior hides the marker. The Skyrim overlay can use both Skyrim maps. Fallout 4 has its own Windows overlay for windowed and borderless play.

## Launch settings

Open **Settings → Game launch** from a supported map, browse to the executable you normally use, then select **Save**. Use the play button in the header to launch the game. MapCorleon uses the selected map's saved executable first. A map with an empty path can use a configured map from the same game. Existing per-map launch settings take precedence.

## Fallout 4 overlay

MapCorleon draws a separate window over Fallout 4. Keep MapCorleon running with the matching Fallout map selected. Passive nearby alerts time out and allow clicks to pass through. Press **F7** to open the clickable panel, then use **Collect** or **×** on a location. Press **F7**, **Esc**, or the panel close button to return focus to the game. Press **F8** to toggle desktop Follow Player and see brief feedback.

Use **Settings → Game overlay** to change the keys, alert duration, nearby radius, and categories. The Fallout settings are shared across the three Fallout maps. The overlay uses Windows window positioning and hotkeys. [Window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) · [Hotkeys](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)

## Memory profiles and alignment

- F4SE v0.7.8 targets runtime `1.11.221`. Its `g_player` relocation is `Fallout4.exe + 0x032D22E0`. The profile checks the reference form ID, parent cell, interior flag, and worldspace form ID before reading XYZ. [Player relocation](https://github.com/ianpatt/f4se/blob/eddb35031204bea04ede6707bc163ea88988a786/f4se/GameReferences.cpp) · [Reference layout](https://github.com/ianpatt/f4se/blob/eddb35031204bea04ede6707bc163ea88988a786/f4se/GameReferences.h) · [Cell and form layout](https://github.com/ianpatt/f4se/blob/eddb35031204bea04ede6707bc163ea88988a786/f4se/GameForms.h)
- Skyrim AE reads the exterior cell's worldspace pointer at `0x128` and retains the existing XYZ source. The five walled cities retain Skyrim map tracking. [Skyrim cell layout](https://github.com/CharmedBaryon/CommonLibSSE-NG/blob/main/include/RE/T/TESObjectCELL.h)
- The worldspace IDs below assume the official master slots. The fits pair exterior map-marker records in the game masters with unique names in the imported MapGenie maps. The source marker and the map pin can represent different points at the same landmark.
- Tracking reads process memory through the desktop app. The F4SE source is a layout reference. Fallout desktop tracking requires no game plugin.

| Map | MapGenie ID | Runtime worldspace | Fit landmarks | Held-out landmarks | Median held-out difference |
| --- | --- | --- | --- | --- | --- |
| Commonwealth | 200 | `0x0000003C` | 183 | 55 | 0.00162 map degrees |
| Far Harbor | 201 | `0x03000B0F` | 26 | 9 | 0.00458 map degrees |
| Nuka-World | 202 | `0x0600290F` | 30 | 6 | 0.00213 map degrees |
| Solstheim | 18 | `0x04000800` | 35 | 9 | 0.000215 map degrees |

## Live checks

1. Exit the regular MapCorleon and run the preview. Select a Fallout map, set the executable, and use the header play button. Load an outdoor Commonwealth save. The blue marker should move as you walk.
2. Enable Follow Player, drag the map, and verify it resumes after 15 seconds. Enter a building and exit. The marker should disappear indoors and return outdoors.
3. Compare the marker to a second landmark after fast travel. If saves are available in Far Harbor, Nuka-World, or Solstheim, load outdoors in each area and select its matching map in MapCorleon.
4. If a Fallout marker is missing or misaligned, run `MapCorleon.Fallout4Probe.exe 120 "Far Harbor"` from PowerShell with the matching map name. The default is Commonwealth. The probe prints one sample per second with XYZ, projected coordinates, and cell/worldspace IDs when available. Ctrl+C stops it.

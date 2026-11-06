# Fallout 4 desktop tracking

The preview targets the MapGenie Commonwealth map (map 200) and the Fallout 4 Windows runtime `1.11.221.0`. It reads a running game and uses the existing desktop marker and Follow Player controls. The Skyrim overlay remains separate.

## Memory profile and map alignment

- F4SE v0.7.8 targets runtime `1.11.221`. Its `g_player` relocation is `Fallout4.exe + 0x032D22E0`. The profile reads the reference's form ID, parent cell, and XYZ. It checks the cell's interior flag and worldspace form ID before showing a Commonwealth marker. [Player relocation](https://github.com/ianpatt/f4se/blob/eddb35031204bea04ede6707bc163ea88988a786/f4se/GameReferences.cpp) · [Reference layout](https://github.com/ianpatt/f4se/blob/eddb35031204bea04ede6707bc163ea88988a786/f4se/GameReferences.h) · [Cell and form layout](https://github.com/ianpatt/f4se/blob/eddb35031204bea04ede6707bc163ea88988a786/f4se/GameForms.h)
- Commonwealth worldspace `0x0000003C` comes from the installed `Fallout4.esm` file. The affine fit uses the positions of exterior map-marker references in that file and the matching imported MapGenie markers. The fit uses 183 training landmarks from 248 unique name matches. The 55 held-out matches had a median difference of about `0.00162` map degrees. The source marker and the map pin can represent different points at the same landmark.
- MapCorleon uses read-only process access. The F4SE source is a reference for the layout and tracking itself has no F4SE or game plugin dependency.

## One live test session

1. Start Fallout 4 the way you normally do and load an outdoor Commonwealth save. Close the regular MapCorleon window if it is open, then start the preview executable and select Fallout 4 → Commonwealth.
2. Check the blue marker near a known landmark. Walk in two directions and change elevation. Enable Follow Player, drag the desktop map, and verify it resumes after 15 seconds. Toggle Follow Player off and confirm it stays off.
3. Enter a building and exit again. The marker should disappear indoors and return outdoors. Fast travel to a second landmark far from the first and compare the marker with the expected map area.
4. If convenient, restart Fallout 4 with the map open and verify it reconnects. Visiting a different worldspace should hide the Commonwealth marker.
5. If the marker is missing or misaligned, run `MapCorleon.Fallout4Probe.exe 120` from PowerShell while the game is running. It prints one status sample per second for two minutes, including XYZ, projected map coordinates, and cell/worldspace IDs when available. Ctrl+C stops it.

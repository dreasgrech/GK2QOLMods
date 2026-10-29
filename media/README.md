# Media

Pictures and page texts for the mods' pages (Nexus Mods, this README). One file per subject, named by the subject
and re-shot in place when it changes; pages hotlink the raw URL
(`https://raw.githubusercontent.com/dreasgrech/GK2QOLMods/main/media/<folder>/<file>?v=N`, `v` bumped when a picture
changes so Nexus drops its cached copy). Pictures 560 wide (Pillow, Lanczos, `tools/crop-shot.py`); a Nexus page
header 1300x372. A crop's box is given as left,top-right,bottom in the 2560x1440 screenshot, so a re-shoot from the
same spot is cut the same way.

| File | What |
|---|---|
| `GK2_TechTreeSearch_Nexus.txt` | the Nexus Mods page text of GK2 Tech Tree Search (BBCode) |

## `tech-tree-search/`

Steam screenshot of 2026-10-29 (23:52:37; Tech Tree, the Metallurgy tab, "Iron" typed: eight matches and a "+4 more"
row, five of them researched and green), with the 1.0.1 release build.

| File | Shows | Source |
|---|---|---|
| `search-iron.png` | the whole Tech Tree window: the box at the top right of the tab strip, the list under it over the tree | box 634,305-1926,1143 (the window's frame), 560x363 |
| `search-results.png` | close up: the box with "Iron" and the list: tech name, its tab, researched ones green, the "+4 more" row | box 1280,365-1926,880, 560x446 |
| `settings-mods-menu.png` | the mod's settings page in GK2 Mod Framework's Mods menu: Status Active, the two toggles and the key (Steam screenshot of 2026-10-30 00:45:19, the game's own Mods window, HUD out) | box 570,340-1990,1037, 560x275 |
| `nexus-header.png` | the Nexus page header (1300x372): the window's title bar, the tab strip, the box and the first rows | box 630,310-1930,682, at its own scale |

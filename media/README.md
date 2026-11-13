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
| `GK2_TechTreeSearch_Workshop.txt` | the Steam Workshop description of the same (Steam's BBCode; `make-release-zip.py` stages it as `_description.bbcode`; under 8,000 bytes) |

## `workshop/`

The Steam Workshop items' pictures, one folder per mod (`tools/workshop-thumbnail.py`, `tools/workshop-gallery.py`;
the Workshop steps in `docs/release-checklist.md`, section 7). `Thumbnail.jpg` is the item's preview, square (Steam's
tiles letterbox anything else), 1024 wide, under 1 MB; `gallery/*.jpg` are the page's cuts at the screenshot's own
pixels, uploaded by hand on the item's page.

| File | Shows | Source |
|---|---|---|
| `GK2TechTreeSearch/Thumbnail.jpg` | the box with "Iron" and the list, the tab icons above (2026-11-13) | the screenshot of 2026-10-29 23:52:37, box 1280,307-1920,947 (640x640), scaled to 1024x1024 |
| `GK2TechTreeSearch/gallery/search-iron.jpg` | as `search-iron.png`, full size | the same box, 1292x838 |
| `GK2TechTreeSearch/gallery/unlock-empty-bottle.jpg` | as `unlock-empty-bottle.png`, full size | the same box, 1292x838 |
| `GK2TechTreeSearch/gallery/settings-mods-menu.jpg` | as `settings-mods-menu.png`, full size | the same box, 1420x697 |

## `tech-tree-search/`

Steam screenshot of 2026-10-29 (23:52:37; Tech Tree, the Metallurgy tab, "Iron" typed: eight matches and a "+4 more"
row, five of them researched and green), with the 1.0.1 release build.

| File | Shows | Source |
|---|---|---|
| `search-iron.png` | the whole Tech Tree window: the box at the top right of the tab strip, the list under it over the tree | box 634,305-1926,1143 (the window's frame), 560x363 |
| `search-results.png` | close up: the box with "Iron" and the list: tech name, its tab, researched ones green, the "+4 more" row | box 1280,365-1926,880, 560x446 |
| `unlock-empty-bottle.png` | "Empt" typed: Glass Blowing listed through its unlock ("Metallurgy · Empty Bottle"), its card framed at the bottom left with the Empty Bottle tooltip open (Steam screenshot of 2026-10-30 00:50:02) | box 634,305-1926,1143 (the window's frame), 560x363 |
| `settings-mods-menu.png` | the mod's settings page in GK2 Mod Framework's Mods menu: Status Active, the two toggles and the key (Steam screenshot of 2026-10-30 00:45:19, the game's own Mods window, HUD out) | box 570,340-1990,1037, 560x275 |
| `nexus-header.png` | the Nexus page header (1300x372): the window's title bar, the tab strip, the box and the first rows | box 630,310-1930,682, at its own scale |

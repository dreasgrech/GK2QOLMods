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
| `GK2_EventDecorations_Nexus.txt` | the Nexus Mods page text of GK2 Event Decorations (BBCode) |
| `GK2_EventDecorations_Workshop.txt` | the Steam Workshop description of the same (without GK2 Mod Framework) |

## `workshop/`

The Steam Workshop items' pictures, one folder per mod (`tools/workshop-thumbnail.py`, `tools/workshop-gallery.py`;
the Workshop steps in `docs/release-checklist.md`, section 7). `Thumbnail.jpg` is the item's preview, square (Steam's
tiles letterbox anything else), 1024 wide, under 1 MB; `gallery/*.jpg` are the page's cuts at the screenshot's own
pixels, uploaded by hand on the item's page. No framework picture on the Workshop: GK2 Mod Framework is a Nexus-only
mod, so the Workshop texts never mention it (the user, 2026-11-13).

| File | Shows | Source |
|---|---|---|
| `GK2TechTreeSearch/Thumbnail.jpg` | the box with "Iron" and the list, the tab icons above (2026-11-13) | the screenshot of 2026-10-29 23:52:37, box 1280,307-1920,947 (640x640), scaled to 1024x1024 |
| `GK2TechTreeSearch/gallery/search-iron.jpg` | as `search-iron.png`, full size | the same box, 1292x838 |
| `GK2TechTreeSearch/gallery/unlock-empty-bottle.jpg` | as `unlock-empty-bottle.png`, full size | the same box, 1292x838 |
| `GK2TechTreeSearch/gallery/settings-row.jpg` | as `settings-row.png`, full size (1.1.0) | the same box, 804x802 |
| `GK2TechTreeSearch/gallery/options-window.jpg` | as `options-window.png`, full size (1.1.0) | the same box, 1828x1226 |
| `GK2EventDecorations/Thumbnail.jpg` | the tavern's front on Halloween: the ghost figure, pumpkins and a garland at its door, the keeper on the road (2026-11-19) | the screenshot of 2026-11-19 18:46:36, box 680,400-1400,1120 (720x720), scaled to 1024x1024 |
| `GK2EventDecorations/gallery/halloween.jpg` | as `halloween.png`, full size | the same box, 1536x691 |
| `GK2EventDecorations/gallery/street-plain.jpg` | as `street-plain.png`, full size | the same box, 1024x409 |
| `GK2EventDecorations/gallery/street-halloween.jpg` | as `street-halloween.png`, full size | the same box, 1024x409 |
| `GK2EventDecorations/gallery/night.jpg` | as `night.png`, full size | the same box, 1536x826 |
| `GK2EventDecorations/gallery/ghost.jpg` | as `ghost.png`, full size | the same box, 712x602 |
| `GK2EventDecorations/gallery/settings-row.jpg` | as `settings-row.png`, full size | the same box, 804x802 |
| `GK2EventDecorations/gallery/options-halloween.jpg` | as `options-halloween.png`, full size | the same box, 1828x1226 |

## `tech-tree-search/`

Steam screenshot of 2026-10-29 (23:52:37; Tech Tree, the Metallurgy tab, "Iron" typed: eight matches and a "+4 more"
row, five of them researched and green), with the 1.0.1 release build.

| File | Shows | Source |
|---|---|---|
| `search-iron.png` | the whole Tech Tree window: the box at the top right of the tab strip, the list under it over the tree | box 634,305-1926,1143 (the window's frame), 560x363 |
| `search-results.png` | close up: the box with "Iron" and the list: tech name, its tab, researched ones green, the "+4 more" row | box 1280,365-1926,880, 560x446 |
| `unlock-empty-bottle.png` | "Empt" typed: Glass Blowing listed through its unlock ("Metallurgy · Empty Bottle"), its card framed at the bottom left with the Empty Bottle tooltip open (Steam screenshot of 2026-10-30 00:50:02) | box 634,305-1926,1143 (the window's frame), 560x363 |
| `settings-mods-menu.png` | the mod's settings page in GK2 Mod Framework's Mods menu: Status Active, the two toggles and the key (Steam screenshot of 2026-10-30 00:45:19, the game's own Mods window, HUD out). Up to 1.0.2; out of the page text with 1.1.0 (the menu's Settings button opens the options window), the file kept: the live page shows it until its text is pasted again | box 570,340-1990,1037, 560x275 |
| `settings-row.png` | the game's Settings window in the main menu with the mod's row under the game's own, "Open" in its box (1.1.0; the first rehearsal start, no other mod of ours installed; Steam screenshot of 2026-11-19 00:24:01) | box 878,320-1682,1122 (the window with a margin), 560x559 |
| `options-window.png` | the mod's options window: General (Status Active, the two switches, Results shown 8, the key Ctrl + F) and the Safety tab, Defaults and Done (1.1.0; Steam screenshot of 2026-11-19 00:24:06, the same start) | box 366,108-2194,1334 (the window with a margin), 560x376 |
| `nexus-header.png` | the Nexus page header (1300x372): the window's title bar, the tab strip, the box and the first rows | box 630,310-1930,682, at its own scale |

## `event-decorations/`

Steam screenshots of 2026-11-19, the first rehearsal start of GK2 Event Decorations 1.0.0 (the release zip alone, no
other mod of ours, no GK2 Mod Framework; game 1.012.9). The keeper stands at the village crossroads by the Golden
Horse tavern; three shots from that one spot: out of season on By date (18:45:53), a few seconds after Halloween was
set to Always (18:46:36), and the same at night (18:46:58).

| File | Shows | Source |
|---|---|---|
| `halloween.png` | the crossroads on Halloween by day: the tavern's door with the ghost figure, pumpkins and garlands, the gate with a pumpkin on its beam, the scarecrow at the bottom left | 18:46:36, box 230,512-1766,1203, 560x252 |
| `street-plain.png` | the tavern's door and the gate without Halloween (By date, out of season) | 18:45:53, box 691,538-1715,947, 560x224 |
| `street-halloween.png` | the same cut with Halloween on Always | 18:46:36, the same box, 560x224 |
| `night.png` | the crossroads on Halloween at night: the pumpkins and the scarecrow's face lit, glowing eyes in the bush at the bottom | 18:46:58, box 230,512-1766,1338, 560x301 |
| `ghost.png` | the ghost on its night walk in the village, above the gate with its lit pumpkin (the second rehearsal start, 19:00:51; the camera zoomed in with another mod, the keeper beside it cut away) | box 1490,320-2202,922, 560x473 |
| `settings-row.png` | the game's Settings window in the main menu with the mod's row under the game's own, "Open" in its box | 18:40:11, box 878,320-1682,1122 (the window with a margin), 560x559 |
| `options-halloween.png` | the mod's options window on its Halloween tab: Halloween on Always, the own dates greyed, the three parts With Halloween, the first areas | 18:46:31, box 366,108-2194,1334 (the window with a margin), 560x376 |
| `nexus-header.png` | the Nexus page header (1300x372): the tavern's door and the gate on Halloween, the keeper between | 18:46:36, box 660,560-1960,932, at its own scale |

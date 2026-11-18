# Game update 1.012.9 (Steam build 26734308): the QOL mods

On 2026-11-18 the game went from 26624074 (1.012.7) to 26734308 (1.012.9), a hotfix without patch notes. The whole
audit (what the game changed, the method diffs, the art and data) is in the GK2DonkeyAPI repository,
`docs/game-update-26734308.md`; this page is what it means here.

**Nothing the QOL mods use changed.**

| Mod | `DepManifest verify` against the new game | For players |
|---|---|---|
| Built Count (public 1.0.6 in `dist/`) | 35 entries, identical | runs as it is |
| Global Storage (public 1.1.6 in `dist/`) | 43 entries, identical | runs as it is |
| Tech Tree Search (public 1.0.2 in `dist/`) | 49 entries, identical (today's build: 141, identical) | runs as it is |
| Owned Count (public 1.0.4 in `dist/`) | 61 entries, 1 changed: `UITooltip.AddItemWidgets`, the same one as since 1.012 | runs with its warning line, as since 1.012 |
| Event Decorations (never released) | 124 entries, identical | |

- Every project compiles against the new DLLs with 0 warnings, the test plugin (`tools/GK2QOLMods.Tests`) too. Move
  Building does not, as since 1.011 (retired then).
- Release scans: 22 of 23 green. The one failure is `check-dist`, and not the update's: `dist/` has no DLL of Event
  Decorations (it was never released).
- Rebuilt and deployed 2026-11-18 12:48 with the game closed; every deployed DLL says "the same build, identical".
  The deploy puts each mod in a folder of its own since 2026-11-16 (`plugins\<mod>\`, its `Languages\` beside it):
  Built Count, Global Storage and Owned Count were still loose in `plugins\` from before and moved now, so the game
  folder's git shows three deleted DLLs and three new folders.
- The fonts: the game's `small_font` and `small_font_bold` assets changed, the characters they hold did not (the
  library's `l10n-fonts.py` on the new files gives the same seven character files as on 2026-11-17). The
  translations' checks stand.

## New in the game that touches these mods

**The Mods window judges BepInEx plugins now** (`LazyBearTechnology.BepInExPluginHealth`, once when the main menu
comes up). For a code mod the game tracks (a Workshop item, or a pack in `Mods\Code`), it shows in red "BepInEx did
not load this mod" when none of the mod's DLLs has a loaded plugin, and "Failed to start: <the line>" when the
game's log holds, up to that moment, an Error or Fatal line from the plugin's name or an exception naming one of its
types.

Tech Tree Search is on the Workshop (item 3839100125). Its start writes no Error line unless patching fails
("Patching failed; all patches removed, game runs unmodified", which is a failed start), and the dependency check's
"running on changed code" line is a warning: it stays "Running". The rule to keep for every mod here: **an Error
line before the main menu means "this mod did not start"**, nothing less.

## Owed in the game

One run of the bench's suites on the new build (F4, Run all; with it Tech Tree Search's names-against-unlocks timing
line, still unread), and a look at the Mods window's line for Tech Tree Search if the Workshop item is subscribed.
The pins (the checklist, `dist/README.md`, three READMEs, Tech Tree Search's store texts: game 1.012.7, build
26624074) move with the next release.

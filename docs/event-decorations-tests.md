# GK2 Event Decorations: what is tested, and where

Written 2026-11-18, when the in-game suite was built for the mod's first release (the user: "a solid test suite ...
which covers everything in it"). This page is the map from each part of the mod to what checks it, and the list of
what nothing checks, with the reason for each. Keep it current when the mod or its tests change.

**State: the suite's first run in the game passed whole (2026-11-18 16:03, game 1.012.9): 45 tests, 226 checks, 0
failed, 0 warnings, none skipped. The mod changed after it the same day (no auto-detected events; "On today" and
Status in the game's language), and its tests with it; the second run, on that build, passed whole too (16:59: 45
tests, 233 checks, 0 failed, 0 warnings, none skipped).** The runs are at the end of this page.

## The checks there are

| Check | Where | Runs |
|---|---|---|
| The in-game suite, 45 tests | `tools/GK2QOLMods.Tests/EventDecorations/` | In the game, from GK2 Mod Test Bench's panel (F4): Run on the row "GK2 Event Decorations" |
| The truth table, 2430 combinations of the settings in three seasons | `tests/HarmonyHarness-EventDecorations` | Offline (`tools/release-scans.sh`), .NET Framework, the built DLL's real patches on the game's real methods |
| The mod's game code: one event gate, the members it patches | `tests/CecilScan-EventDecorations`, the dependency manifest (`tools/DepManifest verify`) | Offline, `release-scans.sh` |
| No framework type outside `FrameworkLink`, every patch class listed | `tests/FrameworkScan` | Offline, `release-scans.sh` |
| No LINQ | `tests/NoLinqScan`, the build | Offline |
| The translation files, line by line | the window library's `tests/TranslationScan`, `l10n-sync.py --check` | Offline, `release-scans.sh` |

The in-game suite repeats the truth table on purpose: the offline one runs under another runtime than the game's
(the game's Mono may compile a tiny method into its caller, which is how a patch gets lost), and it can't reach the
game's head table, its expression function or the ghost's two balance rows.

## Running the suite

- The game closed: `dotnet build tools/GK2QOLMods.Tests -c Release -p:Deploy=true` (after the mods' own Release build
  and with GK2 Mod Test Bench deployed).
- In a loaded save: **F4, Run on "GK2 Event Decorations"**. About a minute. The decorations go up and down several
  times and the options window opens and closes by itself. Nothing has to be arranged first: every test makes the
  state it starts from (the settings, the season, the ghost at home, no window of ours open) and the run puts back
  what it found; the bench itself waits until the keeper walks freely.
- Read it in `BepInEx\GK2ModTestBench-runs\<date>-<time>.log` (or the `[test]` lines of `BepInEx\LogOutput.log`).
- One test, one group or the whole suite: every changing test starts from the same settings (`EdRun.Arrange`), and
  the two closing checks run in every run.

What a run holds and gives back, also when it is ended from outside (the suite's holds):

- **The player's settings**: every entry's value kept and put back; the cfg file is not written during the run.
- **The game's own dates**: a prefix of the tests' own on `TemporaryEventDates.IsMonthDayInInclusiveRange`, so both
  "the game says Halloween" and "it says not" are tested on any date. The first tests say whether it took.
- **The mod's log**: a warning during the run is either one a test provoked on purpose (a date that isn't one,
  decorations changed behind the mod's back) or a failure of the last-but-one check.
- **The language of the mod's windows**, and **the options window closed**.

Nothing is saved by the tests. Two things a run can leave until the game redraws them: a zombie picture drawn in a
frame where a test had the Halloween head on (the head swap is display only), and the ghost's own `current_point`
variable (as after any of its walks).

## The map: each part of the mod

`T` a test of the in-game suite (its group), `H` the offline truth table, `S` an offline scan, `hand` only seen by
playing, `none` not checked by anything.

### `Plugin.cs`

| Part | Checked by |
|---|---|
| The settings bound (every key, its default) | T The mod: "every setting is in the cfg ..." (its own list of all 30; a setting without a line there fails it) |
| The dependency check passing, the patches applied, the start line in the log | T The mod: "running", "every game method ... carries its patch", "nothing ... at warning level or above in this session's log"; S the manifest |
| `StatusShown` (the status as a word of the mod's, in the game's language) | T The options window: "On today and Status"; T Languages. The five sentences of a start that failed are read as words (each language has its line, the number kept) but never shown by a real failed start: **none in the game** |
| The window's frame step (`UiHost.LateUpdate`) | T through every window test |
| A start that fails: a game member gone or changed (`StopOnChangedGameCode`), patching throwing | **none in the game** (it would need a changed game); the shared check's own rules are `docs/dependency-check.md`'s |
| `OnDestroy`, `OnApplicationQuit` | **none** (BepInEx never destroys a plugin; quitting ends the run) |

### `Events.cs`

| Part | Checked by |
|---|---|
| `IsOn`, the player's dates: what is a date, both needed, inclusive ends, past the new year | T The player's own dates (4 tests); H |
| `UsesGameDates`, `CustomWindow`, `DatesDontCount` | T The player's own dates; T What the game is told (the table's "On today"); H |
| `GameDates` and `Bypass` (the game's own answer past the mod) | T As the player has it; T the table (closed again after every call); H |
| `OtherGates`: the events a class of gates has besides Halloween (asked of the game's class at start, for one line in the log) | T Other mods and later games: asked of the game's class (none today, and the start said nothing) and of a **made-up** class with two gates and seven things that only look like gates; and that naming is all the mod does: no patch on such a gate, no cfg section, no tab, no word in "On today" |
| The line in the log for an event the mod doesn't know (`Plugin.SayOtherEvents`) | **none in the game** while the game has no other event (the test reads the session's log and expects no such line); S CecilScan names a new event at a game update |

### `Halloween.cs`

| Part | Checked by |
|---|---|
| `PieceOn`, `EventOff`, `DecorationsOn`, `ScriptsOn`, `Untouched` | T the table (2430 combinations where the season can be forced, 810 otherwise); T the three leaks by name; H |
| `DecorationsCanShow` | T The options window: "which rows are greyed when" |
| `Overrides` | T The options window: "On today and Status" (the start line's wording for one setting) |
| The two gate patches | T the table (called as the game calls them), T The ghost (its script and its two balance rows) |
| The `ShouldLoad` patch | T the table (the game's own condition asset), T The decorations: "at a load the game asks the mod" (through the game's own loader) |
| The `Resolve` patch | T the table and "the zombie head is swapped by the game's own table" (every row of the real table, `ResolveDropIconId`, `IsReplaced`) |
| The head table unreadable (`tableMissing`) | H only (outside the game the table can't be read, which is that path) |
| The `AddMissingConditionalContent` patch being on the method | T The mod: "carries its patch". What it calls: T "at a load ..." (`AfterGameAdded` called after the game's own step). **The patch firing in a real load: hand** (every load of a save does it; the first test of "As the player has it" reads its result) |

### `Decorations.cs`

| Part | Checked by |
|---|---|
| `Sync`: up and down at once by any setting that decides it | T The decorations: "put up and taken down at once", "what decides whether they are up", "up and down many times over" |
| `Apply`: areas, kinds, the bats, glow, blockers, the mod off | T The decorations: "each area's switch", "each kind's switch", "an area and a kind together", "the glow switch", "block the way", "with the mod switched off", "with every switch on the mod leaves them as the game made them" |
| `Apply`'s line in the log | T each switch test compares it with its own count |
| The game's layout the switches are written for (12 groups, 140 items, 44 glows, 104 blockers, 3 other colliders) | T "the game's decorations are laid out as the switches expect" (the audit's numbers; a game that changes them fails it) |
| `AfterGameAdded` | T "at a load the game asks the mod, and the areas are switched before the world is built" |
| `OnGameStarted` and the mismatch warning | T "after a load, decorations that don't match the settings are said and put right" (both directions, and nothing said when they match) |
| Nothing of the decorations in the save | T "put up and taken down ..." and "many times over" (the scene's saved lists counted before and after) |
| "could not be taken down", "could not be put up", a game with another layout (`root == null`), the two catch blocks | **none**: each needs the game to refuse or to be another version |
| Villagers' paths after a switch ("they follow at the next load") | **hand**, and cosmetic |

### `GhostHome.cs`

| Part | Checked by |
|---|---|
| A ghost standing in the village with Halloween off is sent home; left alone while its Halloween is on, with `SendGhostHome` false, with the mod off, and when it is at home | T The ghost: "a ghost left standing in the village ..." (the ghost is taken out by its own script's evening start, its walk stopped) |
| The ghost's script itself hearing the mod | T The ghost: "told to start its evening ..." |
| The check coming round by itself every ten seconds | **none** (the test calls the game-started entry, which runs the same check at once) |
| Two steps that didn't take it home (the warning), a save without a ghost or its home point, a second save loaded in one session | **none**: the first needs a changed ghost script, the others another save |
| The state the fix is for, made the real way: a save during the ghost's morning walk home, loaded with Halloween off | **hand** (never seen; the test makes the same state without a save) |

### `OptionsPage.cs`

| Part | Checked by |
|---|---|
| The rows, their order, tabs, choices, descriptions; a row for every setting | T The options window: "the page"; T The mod: "every setting ... has a row" |
| The greying rules | T "which rows are greyed when" (five settings of mode, part and mod switch, against the test's own rule) |
| `Today` | T the table (what it names), T "On today and Status" (its exact words in eight settings, and that it names the decorations exactly when they are up) |
| The window opened, drawn, a switch pressed, Defaults, closed | T "the window opens ...", "a switch pressed ...", "Defaults ..." |
| A tab for an event the mod doesn't know | there is none, by design (2026-11-18); T Other mods and later games checks the window has the two tabs only |
| Typing a date into the text rows | **hand** (a test can't type; the library writes a text row when the typing ends, so a half-typed date is never read) |

### `FrameworkLink.cs`

| Part | Checked by |
|---|---|
| With GK2 Mod Framework: registered, its Settings button pointed at the mod's window | T Other mods: "with GK2 Mod Framework ..." (the mod's prefix is on the framework's method; no warning about it) |
| The button pressed in the Mods menu really opening the window; the framework's "compatible" mark | **hand** |
| Without the framework (how every Workshop player has it) | the test is skipped with that reason. **One full run with `GK2.Framework.dll` set aside is owed**, to see the mod start and every other test pass without it |
| A framework version that changed its API (the two warnings) | **none** |

### The words (`Languages/`)

| Part | Checked by |
|---|---|
| Each shipped file found beside the DLL and read without a complaint, a current line for every word | T Languages: "every shipped language ..." (in the game, from the deployed folder); S TranslationScan, l10n-sync |
| A text for every row in every language, the sentences that take a value still taking it | T Languages: "in every language each row has its text ..." |
| Every character having a glyph | T Languages: "in the game's own language ...": **only for the language the game is in** (the fonts are the game's for its language). For another language: set the game to it and run that test |
| "On today" and Status in the game's language (the mod's own 18 words, `EdWords`) | T Languages: "in every language each row has its text ...": per language the two lines as they read, the event named as its tab, "Always" as the part's own choice, the switch by its label, no sentence left English, a number still in its place; T the table and "On today and Status" read them in English (pinned by `EdRun.Arrange`); S TranslationScan, l10n-sync |
| How it reads and looks | **hand** |

## What only playing shows

1. A real load of a save with decorations up and with them down (the patch at the load; villagers walking round them).
2. How the decorations, the glow off and the walk-through look and feel.
3. The zombie head on a zombie, and its lying picture.
4. The ghost at night with Halloween on, and its morning walk; a save made during that walk, loaded with Halloween off.
5. The Mods menu's Settings button (with the framework), and one full run of the suite without the framework.
6. A date typed into the window.
7. The game's own Mods window line for the mod (as a Workshop item).
8. Another language set in the game: the window's look, and the glyph test run in it.

## Runs

**2026-11-18 16:03, the user, game 1.012.9 (Steam 26734308), English, GK2 Mod Framework 0.1.25 installed, the keeper
in RuinedTemple, the player's settings: Halloween by date, the decorations on Always (up), the rest default.** Run on
the suite's row: **45 of 45 tests, 226 passed, 0 failed, 0 warnings, 0 skipped**, in under a minute (the bench's
panel: 5.2 microseconds a frame). Read from `BepInEx\GK2ModTestBench-runs\20261118-160302.log` (a copy, with the
session's log, in `C:\ClaudeCodeTemp\GK2-event-decorations-tests-2026-11-18`).

- The game's own date check could be turned both ways, so every rule ran in season and out of it.
- The table: 2430 combinations, each answer as the rules say: the gate (with and without a date), the decorations'
  condition at a load, the zombie head (the game's table has one swap, 1051 shown as 1060), the lying zombie's
  picture, the ghost's two balance rows, "On today"; the way past the mod's patches closed after each of 7290 calls.
- The world: as the game makes them, 12 groups, 140 items, 44 glows, 104 blocking colliders and 3 others, nothing
  switched off by the game itself; the mod with every switch on leaves them so. Every area, every kind, the bats,
  the glow and the blockers switched and back. Putting them up cost 20 to 25 ms (one frame, with the game paused
  when it is done from the options window), taking them down 0.1 ms, an area's switch 0.36 ms.
- The ghost: told to start its evening with the mod saying Halloween, it went out (284 units from its home point)
  and walked; its next step without took it home. Left standing in the village with Halloween off, the mod sent
  it home, once, and not while its Halloween was on, with `SendGhostHome` false or with the mod off. **The first
  time the send-home fix was seen working.**
- The window: both tabs drawn inside their column (General 110 of 445 px, Halloween 1006 of 445: it scrolls), a
  switch pressed in it changing the world under it, Defaults after asking.
- The languages: 10 files found, 97 of 97 lines current in each, none unreadable; every character of the English
  window has a glyph.
- The mod's log during the run: 16 warnings, all 16 the ones the tests provoked (14 for dates that aren't dates, 2
  for decorations changed behind the mod's back); none before the run in the session. Player.log: no exception.
- At the end: every setting as the player had it, the cfg file not written, the decorations up as found, the ghost
  at home, no window open.

Not in this run: the suite without GK2 Mod Framework (owed), and another language set in the game.

**After that run, 2026-11-18 evening (the user's two decisions): the mod and the tests changed.** The mod no longer finds events by itself, and "On today" and Status are in the game's language (18 words
of the mod's own, ten languages). In the tests: "an event a later game adds (a made-up one) is answered by its own
mode and dates" became "an event a later game adds is left to the game: named, not switched"; the language test
"in every language each row has its text ..." now also reads the two lines in each language; every test that reads
the lines' English pins English first (`EdRun.Arrange`), the glyph test goes back to the game's own language. Still
45 tests. Offline: the truth table passes with the changed mod; the translation scan passes with a fix to the window
library's reader (below), which the mods get when the library's pointer moves.

A finding of that work, in the window library: the scan's "a text's opening quote deleted" failed on five of the new
lines (German, Polish, Turkish, Brazilian Portuguese: `status.off`, German `today.set_to`). They are the first
shipped texts that quote a name with straight quotes and have no comma: with the CSV's opening quote deleted by a
translator, the reader took the rest as a plain text and would have shown it with its doubled quotes and the
closing one. Fixed in the library's own checkout (`TranslationFiles.ParseCsv`: a doubled quote in a text that isn't
in quotes marks the row's quoting broken, and the row is left out with the warning). The library's scan with the
fix, over this repository: 231 of 231 catalogue cases, 14,513 edited rows, 0 failing; over GK2 Donkey API's: 231 of
231, 43,475 edited rows, 0 failing.

**2026-11-18 16:59, the user, the changed build (the same game, save and settings as the first run).** Run on the
suite's row: **45 of 45 tests, 233 passed, 0 failed, 0 warnings, 0 skipped.** Read from
`BepInEx\GK2ModTestBench-runs\20261118-165945.log` (a copy beside the first run's).

- Everything of the first run again, with the same numbers (the table 2430 of 2430 on every answer; the world 12,
  140, 44, 104 and 3; a put-up 20 to 26 ms; the ghost out, walking, home, and sent home once; 16 warnings, all 16
  provoked).
- An event a later game adds: the game has none besides Halloween and the mod's start said nothing of one; asked of
  the made-up class, the mod named its two events (BenchDated, BenchTest), each once, and none of the seven things
  that only look like a gate; no patch on such a gate; the cfg's sections General, Halloween, Safety, the window's
  tabs General, Halloween.
- The two lines in the ten languages, each read from the deployed file (115 lines each), e.g. German "Halloween:
  Dekorationen (auf "Immer" gestellt)" and "Aus: "Mod aktiviert" ist nicht angehakt"; French "Halloween :
  décorations (réglage : Toujours)"; Japanese "ハロウィン：装飾（「常に」に設定）"; no sentence left English in any.
- The mod's log outside the provoked warnings: clean. Player.log has one exception in that session, the game's own:
  `InvalidKeyException ... No Location found for Key=45a023dc...` in the main menu, long before the run; the key
  is named in the game's own `UICodeModsWindow` (its Mods window asks for an asset this build doesn't have).

Still not run: the suite without GK2 Mod Framework, and another language set in the game.

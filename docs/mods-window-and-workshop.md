# The game's Mods window and the Steam Workshop (pointer)

The investigation of game 1.011's Mods window (what it lists, why loose DLLs in `BepInEx\plugins` never
appear, the local-pack and Workshop ways in, mixed installs, the publishing flow) is in the GK2DonkeyAPI
repo: `docs/mods-window-and-workshop.md`. It covers these mods too; the facts are the same for every BepInEx
plugin.

What applies here:

- A release zip (`BepInEx/plugins/<Mod>/<Mod>.dll`) extracted into
  `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Mods\Code\<Name>\` is already a valid
  pack: it shows in the Mods window with Enable/Disable and the game copies the DLL into `BepInEx\plugins`
  on the next launch (also on GOG and Epic). It shows the folder name and no version until the zip carries a
  `modinfo.json` (`schemaVersion` 1, `id` = plugin GUID, `name`, `version` mirroring `Plugin.cs`); that file
  belongs in `tools/make-release-zip.py`, which already checks the version.
- Workshop items: one per mod (Tech Tree Search, Owned Count, Built Count; Global Storage stays GitHub-only
  unless the user decides otherwise), same folder layout, uploaded with the game's Shift+F11 creator.
- Disable in the window deletes the copied DLL; on the dev machine that is the deployed build (a rebuild
  restores it).

Nothing in these mods needs changing: none reads files relative to its DLL; settings and the item-definition
export go through `Paths.ConfigPath` / `Paths.BepInExRootPath`, not `plugins\`.

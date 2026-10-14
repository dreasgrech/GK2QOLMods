# The game-update gate: what it costs, what it catches, how to soften it

Investigation of 2026-10-13, after the 1.010 re-audit, for both repos (the five QOL mods here, GK2 Donkey API and
Saddle Up in `GK2DonkeyAPI`). Question: every game update recompiles `Assembly-CSharp` and `LazyBearTechnology`, so
every update changes both module ids and every mod switches itself off until it is re-audited and re-released. Is
that gate doing real work, and what is the best way to soften it?

## What the gate does today

Each `Plugin.cs` pins the module version id (MVID) of both game assemblies. On a mismatch and
`Safety.DisableOnGameUpdate=true` (the default) the mod logs an error and patches nothing. With `false` it runs with a
warning; the Donkey API additionally locks the chest on the cart. Independently of the pin, every mod already has two
hard checks that do not depend on the game build:

- Harmony itself throws on a missing or ambiguous patch target; every `Plugin.cs` catches that, unpatches and stops.
- `Patches.Verify` / `Corpses.Verify` (Donkey API) resolve every string-named member at start and throw if one is gone.

So "a member we need is gone" is already handled without the pin. The pin adds exactly one thing: it stops the mod
when the game's code changed anywhere, including changes we do not reference.

## What the four updates so far actually changed in what the mods use

Tool: `tmp/RefDump` (Mono.Cecil; the IL references of each built mod DLL into the game assemblies, in
`tests/GameDiff`'s key format, plus Harmony attribute targets), intersected with GameDiff's removed/changed lists
between consecutive game DLLs taken from the game folder's git history (1.007 `3ffadd70`, 1.008 `f7d46132`,
1.009 hotfix `44edf1a9`, 1.010 `5ec8094e`; the plain 1.009 DLL is not kept anywhere, so 1.008 → 1.009 hotfix is one
step). Method bodies are compared by a hash of their IL with operands resolved to names and compiler-generated
names normalised (`<Foo>b__12_0` → `<Foo>b__N`), which removes the renumbering noise: 372 → 321 changed members for
1.010, 83 → 62 for 1.009, 78 → 61 for 1.008. Caveat: the reference lists are those of today's DLLs, not of the DLLs
of the time (Global Storage's 1.008 target `OnInstrumentCellPressed2`, since replaced, is therefore not in the table).

| Mod (game members referenced) | 1.007 → 1.008 | 1.008 → 1.009 hf | 1.009 hf → 1.010 |
|---|---|---|---|
| Built Count (22) | identical | identical | identical |
| Global Storage (24) | identical | identical | identical |
| Move Building (86) | identical | identical | 3 changed bodies (`WorldZoneData.TryAddWgoData`, `Wgo.UpdateWgoPartState`, `WgoData.RefreshCustomNavMeshCutUnit`) |
| Owned Count (41) | identical | identical | identical |
| Tech Search (27) | identical | identical | identical |
| Donkey API (451 + 29 patch targets) | 6 changed bodies | 1 changed body | 9 changed bodies |

Nothing referenced was removed in any step (the one removal, Global Storage 1.008, is the case the caveat names: Harmony
would have failed on it, which is the hard check above).

The breaks and precautions the hand audits found, against what a dependency check would have seen:

| Update | Finding | Visible to a dependency check? |
|---|---|---|
| 1.008 | Mounts: `Wgo.UpdateChunkVisibility` gained an overload, our by-name lookup became ambiguous | Yes: the lookup fails to resolve (hard check) |
| 1.008 | Global Storage: patch target removed | Yes: Harmony throws (hard check) |
| 1.009 | Donkey API: `StationInteractionHandler` gained a Take while the caretaker is out; our head reroute did not know the handler, a worker could end up laid on the ground | **No.** The class was not referenced by the mod; nothing it referenced changed on that path |
| 1.010 | Global Storage: the new take-only crow nest could receive crafting results (precaution, not a confirmed break) | **No.** New game content meeting a generic mechanism; nothing referenced changed |

## Reading

- **The five QOL mods**: in 14 of 15 mod-updates nothing they use changed at all, and the one genuine break was a
  removed target that Harmony stops on its own. The pin disabled them for nothing every time. The one finding a
  dependency check would miss (the crow nest) is a judgment call about new content, reversible, and no item is lost
  by it (items in the nest are taken from the nest).
- **The Donkey API**: something it depends on changed in every update, and the only finding of the kind no
  dependency check can see (1.009, the caretaker) was on its paths and concerned the keeper's hands. For it the pin
  is doing the work the audit exists for. Softening it means choosing to run cargo and hands code on changed game
  code before anyone read it.
- The LazyBearTechnology pin is pure cost: 0, 1, 2 and 0 changed members over the four updates, none of them ours.

## Options

1. **Dependency fingerprint with three outcomes (recommended for the QOL mods).** A manifest of every game member
   a mod uses, each with the signature and the normalised IL hash: its IL references, which Cecil lists, plus its
   string-named lookups, which the tool must find itself by matching the `ldtoken T; ldstr "name"; call AccessTools.*`
   pattern in the mod's IL (CecilScan already reads Harmony attribute strings the same way; today Move Building, Owned
   Count and Tech Search resolve theirs in a `Verify` at start, Global Storage's one is a Harmony `TargetMethods`,
   Built Count has none), generated after the build and shipped in the DLL. At start the mod compares:
   - every member present and identical → run, one info line, confirm the build to the Mods menu as "dependencies
     verified" (14 of 15 historical cases);
   - all present, some bodies changed → run, one warning naming the members; a mod whose state-changing path is among
     them declines that path until re-audited: Move Building declines moves (its 1.010 case), Global Storage leaves
     the game's own storage alone and does not extend the multi-inventory (its whole job is routing items, so a
     changed dependency means no routing, not unaudited routing); Built Count, Owned Count and Tech Search are
     read-only UI and run; the Mods menu keeps "Unknown build";
   - a member missing or ambiguous → disable, as now (Harmony and `Verify` already do this).
   Costs: a build step (RefDump + GameDiff's `Index` are the whole tool, ~150 lines; it must run on the built DLL, so
   either a post-build rewrite that embeds the manifest as a resource, or a manifest file beside the DLL that
   `check-dist.py` verifies), a shared runtime check (~80 lines; `Mono.Cecil` is in `BepInEx/core` and loaded in the
   game process, so the same hashing code runs at both ends; reading the 43k-member `Assembly-CSharp` with Cecil costs
   tens to a few hundred ms once, so cache the verdict per game MVID in the cfg and later starts are instant), and discipline that every
   string-named lookup goes through `Verify` so it lands in the manifest. What it cannot see: a callee changing under
   an unchanged caller, new content meeting a generic mechanism, balance data, scenes. The MVID pin cannot see the
   last three either; it only reacts to the first by stopping on every recompile.
2. **Flip the QOL default to warn-and-run.** `DisableOnGameUpdate=false` as the default for the five QOL mods, no
   tooling. Historically identical outcome to option 1 (the one break was a Harmony failure), but it logs a warning
   on every game update for every player and shows "Unknown build" in the Mods menu even when nothing the mod uses
   changed, so the re-release chore stays, only the players are no longer stopped. Under the no-ignored-warnings rule
   this trades a weekly error for a weekly warning.
3. **Keep the pin as it is.** Honest, simple, and for the Donkey API the right default. For the QOL mods it is a
   weekly outage for players and a weekly re-release for us that the data says has been unnecessary every time.

## Recommendation

Option 1 for the five QOL mods: when an update leaves a mod's dependencies identical (14 of 15 cases so far) there
is no pin to move, no README build number, no dist rebuild and no release for it. Keep the Donkey API's pin strict (its override and the chest lock stay as they are),
and keep the same-day re-audit for both repos, because the audit is what catches the 1.009 kind of change. Drop the
LazyBearTechnology pin from the QOL mods as part of option 1 (its members are in the manifest like any other).

Built the same evening for the five QOL mods: `docs/dependency-check.md`. The Donkey API got the check the next day,
decided per feature (`GK2DonkeyAPI/docs/dependency-check.md`). Tool and data of the investigation: `C:\Users\User\.claude\jobs\5184282a\tmp\RefDump`,
`refs_*.txt`, `diffn_*_*.txt`, `intersect.py`.

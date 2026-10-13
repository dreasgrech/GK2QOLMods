# The dependency check (what replaced the MVID pin in the QOL mods)

Built 2026-10-13 after `docs/update-gate.md`: the five QOL mods no longer switch off because the game was recompiled.
Each DLL carries a manifest of every game member it uses, and at start compares those members with the installed
game. The Donkey API keeps its strict pin (its own repo, `GK2DonkeyAPI`).

## The manifest

An embedded resource `gk2deps.txt` in each mod DLL, written by `tools/DepManifest` after every build
(`DependencyManifest.targets`, imported by each `.csproj`; it runs after `Build` and before `DeployToGame`, so the
game always gets a DLL with its manifest, and a `-p:ForDist=true` build gets one too):

```
game-mvid 5ec8094e-4c33-4615-9eb8-011501193186
lazybear-mvid 6cd86a0b-b1ed-40ba-88b0-a9313146514b
T <scope> - <type FullName>              a type the mod names (signatures, locals, ldtoken, casts): must exist
M <scope> <hash> <method FullName>       a method the mod calls, patches or looks up by name
F <scope> <hash> <field FullName>        a field the mod reads, writes or looks up by name
```

`<scope>` is the assembly, `Assembly-CSharp` or `LazyBearTechnology`. The members come from the mod's IL
(`tools/DepManifest/DepBuilder.cs`):

- every `MemberReference` into the two game assemblies, resolved to its definition (a call on `LazyWidget<X>` lands
  on `LazyWidget\`1`); the pseudo-methods of arrays of game types (`BuildCellData[,]::Address`) are skipped, the
  element type is listed as a `T`;
- every `[HarmonyPatch(typeof(T), "name" | nameof(...) | MethodType.Constructor, Type[] | MethodType.Getter/Setter)]`
  on a class or a method, resolved the way Harmony resolves it (declared on `T`, matching the argument types when
  given); a target that resolves to nothing fails the build;
- every `AccessTools.*(typeof(T), "name", …)` and `AccessTools.FieldRefAccess<T, F>("name")` /
  `StructFieldRefAccess<T, F>("name")`: walking back from the call, the first `ldstr` is the name and the first
  `ldtoken` before it the type (an argument-types array after the name holds `ldtoken`s too, but the walk passes them
  before it reaches the name); the generic forms take `T` from the call's generic arguments. Every member declared
  on `T` with that name is listed (all overloads, a property's accessors). A pair that resolves to nothing, or a
  lookup whose name is not a literal, fails the build.

The hash is `tests/GameDiff`'s idea: SHA-1 of the method's attributes and its IL with operands resolved to names
(not metadata tokens, which shift between builds), branch targets as offsets, locals and parameters as indices.
Compiler-generated names are normalised first (`<Foo>b__12_0` → `<Foo>b__N`, `<>c__DisplayClass12_0`,
`<>9__12_0`, local functions `<Foo>g__Bar|12_0`), so a renumbered lambda in an otherwise identical method hashes the
same (that removed 51 of 372 "changed" members in the 1.010 diff). Fields: type, static, attributes. Nothing in the
hash may depend on the runtime that computes it: attributes go in as numbers, not `Enum.ToString` (flag names come
out in a different order on .NET Framework and .NET 8, found when the harness disagreed with the tool on the same
DLL), floating-point operands as their bytes. The one source file `src/Shared/DependencyManifest.cs` (Mono.Cecil
0.10.4 from `BepInEx/core`) does the hashing in the tool (net8), in the mods (the game's Mono) and in the harness
(.NET Framework 4.8), so every side agrees by construction.

## At start

`src/Shared/GameDependencies.cs`, linked into every mod by the targets file:

1. The game's module ids equal the manifest's: identical, nothing read (the common case between game updates).
2. Else the cfg cache `[Safety] LastDependencyCheck` (`<game mvid>|<lazybear mvid>|<manifest sha1>|<verdict>|…`)
   holds the verdict for this game build and this manifest: reused, so the game's code is read once per game
   build per mod, not at every start.
3. Else `Assembly-CSharp.dll` and `LazyBearTechnology.dll` are read from their `Assembly.Location` with Cecil (one
   `NoInlining` method holds every Cecil-touching line, so the fast paths never load it), every manifest entry is
   looked up and hashed, and the verdict is cached. A check that cannot run (no file location, Cecil failing, a
   malformed manifest) is a verdict of its own: the mod stops, fail closed.

| Verdict | Log | The mod | Mods menu (GK2 Mod Framework) |
|---|---|---|---|
| identical, same build | nothing | runs | "Compatible": "Its own check passed: every game member it uses is identical to the audited build." |
| identical, recompiled game | info naming both builds and the member count | runs | the same |
| changed | warning naming the members and what the mod does | runs: Built Count, Owned Count and Tech Search as before; Global Storage without extending storage (`Plugin.ChangedGameCode` in the two postfixes); Move Building without moving (`Mover.CanMove`: "Moving is off: the game changed code this mod uses. Wait for an updated mod."). `Safety.StopOnChangedGameCode=true` stops the mod instead | "Unknown build"; the Status row: "Running with moving off: the game changed 3 member(s) this mod uses since this build (…). A re-audited build is pending." |
| missing (gone or ambiguous) | error naming the members | stops; the game runs unmodified | "Unknown build"; the Status row says what is gone |
| the check failed | error with the cause | stops | "Unknown build"; the Status row says so |

cfg: `Safety.StopOnChangedGameCode` (default false) and `Safety.LastDependencyCheck` (written by the mod). The old
`Safety.DisableOnGameUpdate` is gone (nothing had shipped; the five cfgs in the game were cleaned by hand).

## The tool

`tools/DepManifest` (net8, Mono.Cecil from `BepInEx/core`; the targets file builds it with an `MSBuild` task, so one
build per invocation however many plugins build):

- `embed <mod.dll> <Managed> <BepInEx\core>`: computes the manifest and embeds it, rewriting the DLL in place
  (module id kept; a portable PDB beside the DLL is read and rewritten with it, a ForDist build has none); a DLL that
  already carries an identical manifest is left alone. Errors (an unresolvable Harmony target or by-name lookup)
  fail the build.
- `verify <mod.dll> <Managed>`: the start-up comparison, offline. Exit 0 identical, 2 changed, 1 missing or no
  manifest; prints the members. Run by `tools/check-dist.py` on every dist DLL (a dist whose members changed or
  vanished in the installed game fails the check), by the release scans on every bin DLL, and by hand after a game
  update: `verify` against the new game says exactly which of a mod's members changed, which is the audit's reading
  list for that mod.
- `show <mod.dll>`: prints the manifest.

`tests/HarmonyHarness-BuiltCount` links the shared file too and PASSes only when the embedded manifest finds every
member in the installed game unchanged.

## Testing it

Offline, the manifests of all five mods verify identical against 1.010, and Built Count's verifies identical against
the 1.009-hotfix DLLs too (its members did not change). In game, the fast path hides the Cecil path, so the test
gives a deployed DLL a manifest made against the previous game's DLLs (kept in the game folder's git history):
`embed <mod.dll> <folder with the 1.009 Assembly-CSharp.dll and LazyBearTechnology.dll> <core>`. Built Count then
takes the Cecil path and must log the identical-after-recompile info line with its timing, and write the cache;
Move Building must log the changed warning naming `WorldZoneData.TryAddWgoData`, `Wgo.UpdateWgoPartState` and
`WgoData.RefreshCustomNavMeshCutUnit`, show "Unknown build" in the Mods menu with the Status text, and refuse a move
with the message above. A normal rebuild restores the proper manifests.

Done 2026-10-13 late evening, all as described: Built Count "every one of the 35 game members this mod uses is
identical (checked in 11 ms)", confirmed to the framework as Compatible, cache written; Move Building the warning
with the three members, `status=UnknownBuild`, the move refused with the message; the other three only their "loaded"
line; no other warning or error in the log. Dev builds then rebuilt and deployed, dist rebuilt (`-p:ForDist=true`),
`check-dist.py` green, every scan green.

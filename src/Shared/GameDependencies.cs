// The mods' start-up check of the game's code (docs/dependency-check.md): compares the manifest embedded in the mod
// DLL (DependencyManifest.cs) with the game that is running. Linked into every mod by DependencyManifest.targets.
//
//  1. The game's module ids equal the manifest's: identical, nothing read (the common case between game updates).
//  2. The cfg cache ([Safety] LastDependencyCheck) holds the verdict for this game build and this manifest: reused.
//  3. Otherwise Assembly-CSharp.dll and LazyBearTechnology.dll are read with Mono.Cecil (the game's own copy, loaded
//     by BepInEx) and every manifest entry is looked up and hashed; the verdict is cached.
//
// A check that cannot run disables the mod (fail closed), like a missing member. A changed member runs the mod with a
// warning unless Safety.StopOnChangedGameCode is set (the QOL mods; the Donkey API decides per feature, GameCode.cs).
// Identical copies live in GK2QOLMods and GK2DonkeyAPI (src/Shared): change both and keep them byte-identical.
#nullable disable // linked into projects with and without nullable annotations
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace GK2Shared
{
    internal static class GameDependencies
    {
        internal enum State { Identical, Changed, Missing, Failed }

        internal sealed class Result
        {
            public State State;
            public bool Recompiled;                 // the game's module ids differ from the manifest's
            public bool FromCache;
            public long Milliseconds;
            public int Checked;
            public Guid GameMvid, LazyBearMvid, AuditedGameMvid, AuditedLazyBearMvid;
            public readonly List<string> Changed = new List<string>();
            public readonly List<string> Missing = new List<string>();
            // For each item in Changed and Missing: the mod's own top-level types that use it, comma-separated (the
            // manifest's column; "" when it has none). Filled on the cached path too.
            public readonly Dictionary<string, string> UsedBy = new Dictionary<string, string>(StringComparer.Ordinal);
            public string Error;                    // State.Failed: why the check could not run
        }

        internal static ConfigEntry<bool> StopOnChangedGameCode;
        private static ConfigEntry<string> cache;
        private const string ListSeparator = "~~"; // never in a Cecil FullName

        internal static void Bind(ConfigFile config)
        {
            StopOnChangedGameCode = config.Bind("Safety", "StopOnChangedGameCode", false,
                "At start the mod compares every game member it uses with the installed game. A member that is gone always stops the mod. " +
                "A member whose code changed runs the mod with a warning in the log; true stops the mod in that case too, until a re-audited build.");
            cache = config.Bind("Safety", "LastDependencyCheck", "",
                "Written by the mod: the result of its last comparison, so the game's code is read once per game build, not at every start. Leave it alone.");
        }

        internal static Result Check(Assembly self, Assembly game, Assembly lazyBear)
        {
            Result r = new Result();
            try
            {
                DepManifest m = DepManifest.ReadFrom(self);
                r.AuditedGameMvid = m.GameMvid;
                r.AuditedLazyBearMvid = m.LazyBearMvid;
                r.GameMvid = game.ManifestModule.ModuleVersionId;
                r.LazyBearMvid = lazyBear.ManifestModule.ModuleVersionId;
                r.Checked = m.Entries.Count;
                r.Recompiled = r.GameMvid != m.GameMvid || r.LazyBearMvid != m.LazyBearMvid;
                if (!r.Recompiled)
                {
                    r.State = State.Identical;
                    return r;
                }
                string manifestSha = m.Sha1();
                if (ReadCache(r, manifestSha))
                {
                    r.FromCache = true;
                    FillUsedBy(m, r);
                    return r;
                }
                Stopwatch sw = Stopwatch.StartNew();
                CompareWithCecil(m, game.Location, lazyBear.Location, r);
                r.Milliseconds = sw.ElapsedMilliseconds;
                WriteCache(r, manifestSha);
                FillUsedBy(m, r);
                return r;
            }
            catch (Exception e)
            {
                r.State = State.Failed;
                r.Error = e.GetType().Name + ": " + e.Message;
                return r;
            }
        }

        // The mod's own types behind each changed or missing item, from the manifest (so the cached verdict has them too).
        // A Missing item carries " (why)" after the key; a Cecil FullName never contains " (".
        private static void FillUsedBy(DepManifest m, Result r)
        {
            Dictionary<string, string> byItem = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DepEntry e in m.Entries)
            {
                byItem[e.Kind + " " + e.Key] = e.UsedBy ?? "";
            }
            foreach (List<string> list in new[] { r.Changed, r.Missing })
            {
                foreach (string item in list)
                {
                    string key = item;
                    if (!byItem.ContainsKey(key))
                    {
                        int p = item.LastIndexOf(" (", StringComparison.Ordinal);
                        if (p > 0) key = item.Substring(0, p);
                    }
                    r.UsedBy[item] = byItem.TryGetValue(key, out string u) ? u : "";
                }
            }
        }

        // Everything that touches Mono.Cecil, in one method the fast paths never compile.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CompareWithCecil(DepManifest m, string gamePath, string lazyBearPath, Result r)
        {
            if (string.IsNullOrEmpty(gamePath) || !File.Exists(gamePath) || string.IsNullOrEmpty(lazyBearPath) || !File.Exists(lazyBearPath))
            {
                throw new FileNotFoundException("the game assemblies have no file location to read (" + gamePath + ", " + lazyBearPath + ")");
            }
            string managed = Path.GetDirectoryName(gamePath);
            Func<string, Mono.Cecil.ModuleDefinition> modules = DepCompare.ModulesIn(managed, out IDisposable handle);
            using (handle)
            {
                DepVerdict v = DepCompare.Compare(m, modules);
                r.Checked = v.Checked;
                r.Missing.AddRange(v.Missing);
                r.Changed.AddRange(v.Changed);
                r.State = v.Missing.Count > 0 ? State.Missing : v.Changed.Count > 0 ? State.Changed : State.Identical;
            }
        }

        // "<game mvid>|<lazybear mvid>|<manifest sha1>|<identical|changed|missing>|<checked>|<missing list>|<changed list>"
        private static bool ReadCache(Result r, string manifestSha)
        {
            string s = cache?.Value;
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            string[] parts = s.Split('|');
            if (parts.Length != 7 || parts[0] != r.GameMvid.ToString() || parts[1] != r.LazyBearMvid.ToString() || parts[2] != manifestSha)
            {
                return false;
            }
            if (!int.TryParse(parts[4], out int checkedCount))
            {
                return false;
            }
            switch (parts[3])
            {
                case "identical": r.State = State.Identical; break;
                case "changed": r.State = State.Changed; break;
                case "missing": r.State = State.Missing; break;
                default: return false;
            }
            r.Checked = checkedCount;
            if (parts[5].Length > 0) r.Missing.AddRange(parts[5].Split(new[] { ListSeparator }, StringSplitOptions.None));
            if (parts[6].Length > 0) r.Changed.AddRange(parts[6].Split(new[] { ListSeparator }, StringSplitOptions.None));
            return true;
        }

        private static void WriteCache(Result r, string manifestSha)
        {
            if (cache == null)
            {
                return;
            }
            string kind = r.State == State.Identical ? "identical" : r.State == State.Changed ? "changed" : "missing";
            cache.Value = string.Join("|", new[]
            {
                r.GameMvid.ToString(), r.LazyBearMvid.ToString(), manifestSha, kind, r.Checked.ToString(),
                string.Join(ListSeparator, r.Missing.ToArray()), string.Join(ListSeparator, r.Changed.ToArray())
            });
        }

        // Logs the result and says whether the mod may patch. whenChanged: what the mod does on changed game code
        // ("Running anyway", "Running with the storage extension off", ...), for the log and the status. status: the
        // Mods menu's Status row ("Active" lets FrameworkLink confirm the build to the framework; anything else shows
        // as an unknown build there).
        internal static bool Apply(Result r, string modName, ManualLogSource log, string whenChanged, out string status)
        {
            string builds = $"Assembly-CSharp {r.GameMvid}, built against {r.AuditedGameMvid}; LazyBearTechnology {r.LazyBearMvid}, built against {r.AuditedLazyBearMvid}";
            switch (r.State)
            {
                case State.Failed:
                    log.LogError($"{modName}: the check of the game's code could not run ({r.Error}). Mod is DISABLED; the game runs unmodified.");
                    status = "Disabled: its check of the game's code could not run (see BepInEx/LogOutput.log).";
                    return false;
                case State.Missing:
                    log.LogError($"{modName}: game members this mod uses are gone since it was built ({builds}): {string.Join("; ", r.Missing.ToArray())}. Mod is DISABLED; the game runs unmodified. Update the mod.");
                    status = $"Disabled: the game no longer has what this mod uses ({r.Missing.Count} member(s)). Update the mod.";
                    return false;
                case State.Changed:
                    string list = string.Join("; ", r.Changed.ToArray());
                    if (StopOnChangedGameCode != null && StopOnChangedGameCode.Value)
                    {
                        log.LogError($"{modName}: the game changed the code of {r.Changed.Count} member(s) this mod uses since it was built ({builds}): {list}. Mod is DISABLED because Safety.StopOnChangedGameCode=true; the game runs unmodified.");
                        status = $"Disabled: the game changed {r.Changed.Count} member(s) this mod uses, and Safety.StopOnChangedGameCode is set.";
                        return false;
                    }
                    log.LogWarning($"{modName}: the game changed the code of {r.Changed.Count} of the {r.Checked} game members this mod uses since it was built ({builds}): {list}. {whenChanged} until a re-audited build; set Safety.StopOnChangedGameCode=true to stop the mod instead.");
                    status = $"{whenChanged}: the game changed {r.Changed.Count} member(s) this mod uses since this build ({list}). A re-audited build is pending.";
                    return true;
                default:
                    if (r.Recompiled)
                    {
                        string how = r.FromCache ? "checked at an earlier start" : $"checked in {r.Milliseconds} ms";
                        log.LogInfo($"{modName}: the game was recompiled since this mod was built ({builds}); every one of the {r.Checked} game members this mod uses is identical ({how}).");
                    }
                    status = "Active";
                    return true;
            }
        }
    }
}

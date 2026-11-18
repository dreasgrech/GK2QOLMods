extern alias EDasm;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using GK2ModTestBench;
using HarmonyLib;
using UnityEngine;
using ED = EDasm::GK2EventDecorations;
using UI = EDasm::GK2DreasUI;

namespace GK2QOLMods.Tests
{
    // The mod as it started, and the player's own settings as the run finds them: nothing here changes a setting.
    internal static class EdStartTests
    {
        internal static IEnumerator Running()
        {
            Test.Check(ED.Plugin.Status == "Active", $"its status is \"{ED.Plugin.Status}\" (Active when it started without a fault)");
            Test.Check(ED.Events.Halloween != null && ED.Events.Halloween.Mode != null, "it knows the game's Halloween, with its settings bound");
            Test.Info($"the settings: mod {(ED.Plugin.Enabled.Value ? "enabled" : "off")}; {ED.Events.Describe()}");
            Test.Info($"the game's own dates say {(EdRun.RealSeason ? "it is" : "it isn't")} Halloween today ({DateTime.Now:yyyy-MM-dd}); the tests can make them say either: {(EdRun.ForceWorks ? "yes" : "no")} ({EdRun.ForceWhy})");
            yield return null;
        }

        // ------------------------------------------------------------------ the patches

        private static int Own(IEnumerable<Patch> patches)
        {
            int n = 0;
            if (patches == null) return 0;
            foreach (Patch p in patches) if (p.owner == EdRun.ModGuid) n++;
            return n;
        }

        private static void Carries(MethodBase game, string name, int prefixes, int postfixes, int finalizers)
        {
            if (game == null)
            {
                Test.Fail($"{name}: the game has no such method");
                return;
            }
            HarmonyLib.Patches info = Harmony.GetPatchInfo(game);
            int p = Own(info?.Prefixes), o = Own(info?.Postfixes), f = Own(info?.Finalizers);
            Test.Check(p == prefixes && o == postfixes && f == finalizers,
                $"{name}: {p} prefix(es), {o} postfix(es), {f} finalizer(s) of the mod's on it ({prefixes}, {postfixes}, {finalizers} expected)");
            // another mod on the same method would be answered before or after ours: a fact for the reader
            var others = new List<string>();
            if (info != null)
            {
                foreach (string owner in info.Owners)
                {
                    if (owner != EdRun.ModGuid && !owner.StartsWith(Plugin.Guid, StringComparison.Ordinal)) others.Add(owner);
                }
            }
            if (others.Count > 0) Test.Info($"{name} is patched by others too: {string.Join(", ", others)}");
        }

        internal static IEnumerator Patched()
        {
            if (!EdRun.Ready()) yield break;
            Carries(AccessTools.Method(typeof(TemporaryEventDates), nameof(TemporaryEventDates.IsHalloweenEvent), Type.EmptyTypes), "TemporaryEventDates.IsHalloweenEvent()", 1, 0, 0);
            Carries(AccessTools.Method(typeof(TemporaryEventDates), nameof(TemporaryEventDates.IsHalloweenEvent), new[] { typeof(DateTime) }), "TemporaryEventDates.IsHalloweenEvent(DateTime)", 1, 0, 0);
            Carries(AccessTools.Method(typeof(HalloweenConditionalLoadContentPart), nameof(HalloweenConditionalLoadContentPart.ShouldLoad)), "HalloweenConditionalLoadContentPart.ShouldLoad (the decorations)", 1, 0, 1);
            Carries(AccessTools.Method(typeof(HalloweenHeadSkinOverride), nameof(HalloweenHeadSkinOverride.Resolve), new[] { typeof(int) }), "HalloweenHeadSkinOverride.Resolve (the zombie head)", 1, 0, 1);
            Carries(AccessTools.Method(typeof(WorldData), nameof(WorldData.AddMissingConditionalContent)), "WorldData.AddMissingConditionalContent (the areas and kinds at a load)", 0, 1, 0);
            yield return null;

            // every patch class of the mod is on a game method, and nothing of the mod's is on a method it has no class for
            int methods = 0;
            var names = new List<string>();
            foreach (MethodBase m in Harmony.GetAllPatchedMethods())
            {
                HarmonyLib.Patches info = Harmony.GetPatchInfo(m);
                if (info == null || Own(info.Prefixes) + Own(info.Postfixes) + Own(info.Finalizers) + Own(info.Transpilers) == 0) continue;
                methods++;
                names.Add((m.DeclaringType != null ? m.DeclaringType.Name + "." : "") + m.Name);
            }
            int expected = ED.Plugin.PatchClasses.Length; // one class a method, and nothing else: no event the mod doesn't know is patched
            Test.Check(methods == expected, $"{methods} game method(s) carry the mod's patches, one for each of its {expected} patch classes and no other: {string.Join(", ", names)}");
        }

        // ------------------------------------------------------------------ the settings

        // Every setting the mod has, by this test's own list: section, key, default (null: any). A setting the mod
        // gains without a line here fails the test: it has no test either.
        private static readonly string[][] Known =
        {
            new[] { "General", "Enabled", "True" },
            new[] { "Halloween", "Mode", "Auto" }, new[] { "Halloween", "Start", "" }, new[] { "Halloween", "End", "" },
            new[] { "Halloween", "Decorations", "Event" }, new[] { "Halloween", "Ghost", "Event" }, new[] { "Halloween", "ZombieHead", "Event" },
            new[] { "Halloween", "Village", "True" }, new[] { "Halloween", "Port", "True" }, new[] { "Halloween", "PlayerZone", "True" },
            new[] { "Halloween", "PlayerInterior", "True" }, new[] { "Halloween", "TavernInterior", "True" }, new[] { "Halloween", "RestaurantInterior", "True" },
            new[] { "Halloween", "Barracks", "True" }, new[] { "Halloween", "BarracksInterior", "True" }, new[] { "Halloween", "AstrologerInterior", "True" },
            new[] { "Halloween", "WarehouseInterior", "True" }, new[] { "Halloween", "Dungeons", "True" },
            new[] { "Halloween", "Pumpkins", "True" }, new[] { "Halloween", "Garlands", "True" }, new[] { "Halloween", "Scarecrows", "True" },
            new[] { "Halloween", "Pies", "True" }, new[] { "Halloween", "Plants", "True" }, new[] { "Halloween", "OtherProps", "True" },
            new[] { "Halloween", "Bats", "True" }, new[] { "Halloween", "CandleGlow", "True" }, new[] { "Halloween", "BlockTheWay", "True" },
            new[] { "Halloween", "SendGhostHome", "True" },
            new[] { "Safety", "StopOnChangedGameCode", "True" }, new[] { "Safety", "LastDependencyCheck", null }
        };

        // In the cfg only, by design: a fix and the safety switch (Plugin.BindConfig says why each has no row).
        private static readonly string[] NoRow = { "SendGhostHome", "StopOnChangedGameCode", "LastDependencyCheck" };

        internal static IEnumerator Settings()
        {
            if (!EdRun.Ready()) yield break;
            ConfigFile cfg = EdRun.Cfg;
            var missing = new List<string>();
            var wrongDefault = new List<string>();
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (string[] k in Known)
            {
                known.Add(k[0] + "." + k[1]);
                var def = new ConfigDefinition(k[0], k[1]);
                if (!cfg.ContainsKey(def))
                {
                    missing.Add(k[0] + "." + k[1]);
                    continue;
                }
                if (k[2] != null && Convert.ToString(cfg[def].DefaultValue) != k[2]) wrongDefault.Add($"{k[0]}.{k[1]} defaults to \"{cfg[def].DefaultValue}\", not \"{k[2]}\"");
            }
            Test.Check(missing.Count == 0, $"{Known.Length - missing.Count} of {Known.Length} settings are in the cfg{EdWorld.Some(missing)}");
            Test.Check(wrongDefault.Count == 0, $"{wrongDefault.Count} with another default than the documented one{EdWorld.Some(wrongDefault)}");
            var untested = new List<string>();
            foreach (ConfigDefinition d in cfg.Keys)
            {
                if (!known.Contains(d.Section + "." + d.Key)) untested.Add(d.Section + "." + d.Key);
            }
            Test.Check(untested.Count == 0, $"{untested.Count} setting(s) of the mod's that these tests don't know{EdWorld.Some(untested)}");
            yield return null;

            // each has a row in the options window, or is one of the three kept to the cfg; and a row's entry is the
            // very entry the mod reads
            UI.SettingsPage page = EdRun.Page();
            Test.Check(page != null && page.Shown, $"the mod's settings page is made and shown to the windows ({(page == null ? "no page" : page.Shown ? "shown" : "not shown")})");
            if (page == null) yield break;
            var noRow = new List<string>();
            var rowUnwanted = new List<string>();
            var notSame = new List<string>();
            foreach (ConfigDefinition d in cfg.Keys)
            {
                UI.SettingsPage.Row row = page.RowOfKey(d.Section, d.Key);
                bool cfgOnly = Array.IndexOf(NoRow, d.Key) >= 0;
                if (row == null && !cfgOnly) noRow.Add(d.Section + "." + d.Key);
                if (row != null && cfgOnly) rowUnwanted.Add(d.Section + "." + d.Key);
                if (row != null && !ReferenceEquals(row.Entry, cfg[d])) notSame.Add(d.Section + "." + d.Key);
            }
            Test.Check(noRow.Count == 0, $"{noRow.Count} setting(s) without a row in the options window{EdWorld.Some(noRow)}");
            Test.Check(rowUnwanted.Count == 0, $"{rowUnwanted.Count} of the three cfg-only settings with a row{EdWorld.Some(rowUnwanted)}");
            Test.Check(notSame.Count == 0, $"{notSame.Count} row(s) editing another entry than the one the mod reads{EdWorld.Some(notSame)}");

            // lines in the player's file the mod no longer has (an older version's): a fact, they do nothing
            Dictionary<ConfigDefinition, string> orphans = UI.SettingsPage.OrphansOf(cfg);
            if (orphans != null && orphans.Count > 0)
            {
                var names = new List<string>();
                foreach (ConfigDefinition d in orphans.Keys) names.Add(d.Section + "." + d.Key);
                Test.Info($"the cfg file holds {orphans.Count} line(s) the mod doesn't read (an earlier build's){EdWorld.Some(names)}");
            }
        }

        // ------------------------------------------------------------------ the session's log

        // The game's Mods window (1.012.9) marks a mod "Failed to start" on an error line before the main menu, and no
        // warning of ours is ever waved off: nothing of the mod's at warning level or above in this session's log,
        // from the start of the game to now, but what the tests of an earlier run this session provoked.
        internal static IEnumerator SessionLog()
        {
            string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            if (!File.Exists(path))
            {
                Test.Skip($"no log file at {path} (BepInEx's disk log is switched off)");
                yield break;
            }
            int all = 0, bad = 0, provoked = 0;
            bool loaded = false;
            var said = new List<string>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.IndexOf(":GK2 Event Decorations]", StringComparison.Ordinal) < 0) continue;
                    all++;
                    if (line.IndexOf("] GK2 Event Decorations " + ED.Plugin.Version + " loaded.", StringComparison.Ordinal) >= 0) loaded = true;
                    if (!line.StartsWith("[Warning", StringComparison.Ordinal) && !line.StartsWith("[Error", StringComparison.Ordinal) && !line.StartsWith("[Fatal", StringComparison.Ordinal)) continue;
                    if (EdRun.Provoked(line))
                    {
                        provoked++;
                        continue;
                    }
                    bad++;
                    if (said.Count < 5) said.Add(line.Length > 200 ? line.Substring(0, 200) + "..." : line);
                }
            }
            Test.Check(loaded, $"the log has the mod's start line (\"GK2 Event Decorations {ED.Plugin.Version} loaded.\") among its {all} line(s)");
            Test.Check(bad == 0, $"{bad} warning or error line(s) of the mod's in this session's log{(provoked > 0 ? $" ({provoked} more that earlier test runs provoked on purpose)" : "")}{EdWorld.Some(said)}");
            yield return null;
        }

        // ------------------------------------------------------------------ the player's own settings, as found

        internal static IEnumerator Gate()
        {
            if (!EdRun.Ready()) yield break;
            // the mod's own rule (Halloween.cs): a piece left as the game has it lets the game's check run; else the
            // gate says what the Ghost setting says (the decorations and the zombie head have patches of their own)
            bool untouched = ED.Halloween.Untouched(ED.Halloween.Ghost);
            bool expected = untouched ? ED.Events.GameDates(ED.Events.Halloween) : ED.Halloween.ScriptsOn();
            bool today = TemporaryEventDates.IsHalloweenEvent();
            bool dated = TemporaryEventDates.IsHalloweenEvent(DateTime.Now);
            string why = untouched ? "the game's own dates (the ghost follows the event and the event the game's dates, or the mod is off)" : $"the Ghost setting ({ED.Halloween.Ghost.Value}, Halloween {ED.Events.Halloween.Mode.Value})";
            Test.Check(today == expected, $"the game is told {(today ? "it is" : "it isn't")} Halloween; {why} say{(untouched ? "" : "s")} {(expected ? "it is" : "it isn't")}");
            Test.Check(dated == expected, $"asked with today's date, it is told the same ({(dated ? "it is" : "it isn't")})");
            yield return null;
        }

        internal static IEnumerator PastTheMod()
        {
            if (!EdRun.Ready()) yield break;
            int before = ED.Events.Bypass;
            bool first = ED.Events.GameDates(ED.Events.Halloween);
            bool second = ED.Events.GameDates(ED.Events.Halloween);
            Test.Check(first == second, $"the game's own dates say {(first ? "it is" : "it isn't")} Halloween today, twice the same");
            Test.Check(before == 0 && ED.Events.Bypass == 0, $"the way past the mod's patches is closed before and after ({before}, {ED.Events.Bypass})");
            yield return null;
        }

        internal static IEnumerator InTheWorld()
        {
            if (!EdRun.Ready()) yield break;
            if (EdWorld.Owner() == null)
            {
                Test.Fail("no scene of the game has the Halloween decorations (" + EdWorld.ContentName + ")");
                yield break;
            }
            bool up = EdWorld.Up, want = ED.Halloween.DecorationsOn();
            Test.Check(up == want, $"the decorations are {(up ? "up" : "down")}; the settings say {(want ? "up" : "down")} ({ED.Events.Describe()})");
            Test.Check(EdWorld.Copies() == (up ? 1 : 0), $"{EdWorld.Copies()} set(s) of them in the world");
            yield return null;
        }

        internal static IEnumerator AreasAndKinds()
        {
            if (!EdRun.Ready()) yield break;
            // the mod's lists against this file's own (EdWorld: the audit's names)
            var areas = new List<string>();
            foreach (ED.Halloween.Area a in ED.Halloween.Areas) areas.Add(a.Key);
            var kinds = new List<string>();
            foreach (ED.Halloween.Kind k in ED.Halloween.Kinds) kinds.Add(k.Key + "=" + (k.Prefix ?? "(the bats' group)"));
            var wantKinds = new List<string>();
            for (int k = 0; k < EdWorld.KindKeys.Length; k++) wantKinds.Add(EdWorld.KindKeys[k] + "=" + EdWorld.KindPrefixes[k]);
            wantKinds.Add(EdWorld.BatsKey + "=(the bats' group)");
            Test.Check(string.Join(",", areas) == string.Join(",", EdWorld.AreaKeys), $"the mod's {areas.Count} areas are the audit's {EdWorld.AreaKeys.Length}, in order ({string.Join(", ", areas)})");
            Test.Check(string.Join(",", kinds) == string.Join(",", wantKinds), $"the mod's {kinds.Count} kinds are the audit's {wantKinds.Count}, each by the same item name ({string.Join(", ", kinds)})");
            int unbound = 0;
            foreach (ED.Halloween.Area a in ED.Halloween.Areas) if (a.Entry == null) unbound++;
            foreach (ED.Halloween.Kind k in ED.Halloween.Kinds) if (k.Entry == null) unbound++;
            Test.Check(unbound == 0, $"{unbound} of them without a setting");
            yield return null;
        }
    }
}

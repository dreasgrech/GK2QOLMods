extern alias EDasm;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GK2ModTestBench;
using HarmonyLib;
using ED = EDasm::GK2EventDecorations;

namespace GK2QOLMods.Tests
{
    // A made-up event's gates, standing in for one a later game adds: named and shaped as TemporaryEventDates' own
    // (public static bool Is<Name>Event, without parameters or with one date).
    internal static class EdMadeUpGates
    {
        internal static bool Answer;
        internal static int Asked;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool IsBenchTestEvent()
        {
            Asked++;
            return Answer;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool IsBenchDatedEvent(DateTime date)
        {
            Asked++;
            return Answer;
        }
    }

    internal static class EdOtherTests
    {
        // ------------------------------------------------------------------ GK2 Mod Framework's Mods menu

        private const string FrameworkGuid = "ru.superman4eg.gk2.framework";

        internal static IEnumerator ModsMenu()
        {
            if (!EdRun.Ready()) yield break;
            if (!Chainloader.PluginInfos.TryGetValue(FrameworkGuid, out BepInEx.PluginInfo framework))
            {
                Test.Skip("GK2 Mod Framework isn't installed: the mod isn't in a Mods menu, its options open from the game's Settings window (the way every Workshop player has it)");
                yield break;
            }
            Test.Info($"GK2 Mod Framework {framework.Metadata.Version} is installed");
            // by name, as the mod itself finds it: nothing here needs the framework's DLL
            Type window = Type.GetType("GK2.Framework.ModsMenuWindow, GK2.Framework");
            Type registered = Type.GetType("GK2.Framework.RegisteredMod, GK2.Framework");
            MethodInfo open = window != null && registered != null
                ? window.GetMethod("OpenSettingsPage", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { registered }, null)
                : null;
            if (open == null)
            {
                Test.Warn("this version of the framework has no ModsMenuWindow.OpenSettingsPage(RegisteredMod): its Settings button shows the one button that opens the options window; not tested further");
                yield break;
            }
            int ours = 0;
            HarmonyLib.Patches info = Harmony.GetPatchInfo(open);
            if (info != null)
            {
                foreach (Patch p in info.Prefixes) if (p.owner == EdRun.ModGuid + ".modsmenu") ours++;
            }
            Test.Check(ours == 1, $"the framework's Settings button is pointed at the mod's own options window ({ours} prefix of the mod's on OpenSettingsPage): the mod registered with the framework and reached that step");
            Test.Check(EdRun.Count("registering with it failed") == 0 && EdRun.Count("didn't take the build confirmation") == 0, "nothing about the framework went wrong during this run");
            yield return null;
        }

        // ------------------------------------------------------------------ an event a later game adds

        // The mod finds every Is<Name>Event gate on the game's TemporaryEventDates at its start and answers each by its
        // own mode and dates. The game has only Halloween's, so that code has never met a second event. Here one is
        // made up: an event object as the mod makes them, put into the mod's own table of found events (private:
        // reached by name), its gate a method of this file, patched with the mod's own prefix for found events.
        // What this can't reach is the finding itself (Events.Bind's walk over the game's type) and the new event's tab
        // in the options window, both of which run only at the mod's start against the game's own class.
        internal static IEnumerator FutureEvent()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            Test.Check(ED.Events.All.Count == 1, ED.Events.All.Count == 1
                ? "the game has one event today, Halloween: the rest of this test makes one up"
                : $"the game has {ED.Events.All.Count} events now: {ED.Events.Describe()}. A new one needs its own look (its tab, its decorations, these tests)");

            FieldInfo tableField = AccessTools.Field(typeof(ED.Events), "discovered");
            MethodInfo prefix = AccessTools.Method(typeof(ED.Events), "GenericPrefix");
            var table = tableField != null ? tableField.GetValue(null) as Dictionary<string, ED.GameEvent> : null;
            if (table == null || prefix == null)
            {
                Test.Fail("the mod's table of found events (Events.discovered) or its prefix for them (Events.GenericPrefix) isn't there under that name any more: this test needs the new name");
                yield break;
            }
            MethodInfo plain = typeof(EdMadeUpGates).GetMethod(nameof(EdMadeUpGates.IsBenchTestEvent));
            MethodInfo dated = typeof(EdMadeUpGates).GetMethod(nameof(EdMadeUpGates.IsBenchDatedEvent));
            string cfgPath = Path.Combine(BepInEx.Paths.CachePath, "gk2-eventdecorations-tests-" + Guid.NewGuid().ToString("N") + ".cfg");
            var cfg = new ConfigFile(cfgPath, false) { SaveOnConfigSet = false }; // never written
            var e = new ED.GameEvent("BenchTest", false);
            var e2 = new ED.GameEvent("BenchDated", false);
            e.Bind(cfg, "a made-up event", "");
            e2.Bind(cfg, "a made-up event asked with a date", "");
            e.GameGate = plain;
            e2.GameGate = dated;
            e2.GameGateTakesDate = true;
            var harmony = new Harmony(Plugin.Guid + ".madeup");
            DateTime now = DateTime.Now;
            string Md(DateTime d) => $"{d.Month:D2}-{d.Day:D2}";
            // through reflection, as a caller compiled before the patch would never be: the JIT may have put the tiny
            // method into this one already
            bool Ask() => (bool)plain.Invoke(null, null);
            bool AskDated() => (bool)dated.Invoke(null, new object[] { DateTime.Now });
            try
            {
                table["BenchTest"] = e;
                table["BenchDated"] = e2;
                harmony.Patch(plain, prefix: new HarmonyMethod(prefix));
                harmony.Patch(dated, prefix: new HarmonyMethod(prefix));

                // By date without dates of the player's: the game's own check runs
                EdMadeUpGates.Answer = true;
                bool yes = Ask() && AskDated();
                EdMadeUpGates.Answer = false;
                bool no = !Ask() && !AskDated();
                Test.Check(yes && no, $"a found event left By date: the game's own check answers (it says yes: {yes}; it says no: {no})");

                // Always and Never, against the game's answer
                e.Mode.Value = ED.EventMode.On;
                e2.Mode.Value = ED.EventMode.On;
                int asked = EdMadeUpGates.Asked;
                bool always = Ask() && AskDated() && EdMadeUpGates.Asked == asked;
                EdMadeUpGates.Answer = true;
                e.Mode.Value = ED.EventMode.Off;
                e2.Mode.Value = ED.EventMode.Off;
                bool never = !Ask() && !AskDated();
                Test.Check(always && never, $"set to Always it is on though the game says no, without the game's check being run ({always}); set to Never it is off though the game says yes ({never})");

                // the game's own answer is still to be had past the mod (what "Mod enabled" off and the status line use)
                bool past = ED.Events.GameDates(e) && ED.Events.GameDates(e2);
                Test.Check(past && ED.Events.Bypass == 0, $"the game's own answer can still be asked past the mod ({past}), with and without a date, and the way past closes again ({ED.Events.Bypass})");
                yield return null;

                // the player's own dates
                e.Mode.Value = ED.EventMode.Auto;
                e.Start.Value = Md(now.AddDays(-1));
                e.End.Value = Md(now.AddDays(1));
                EdMadeUpGates.Answer = false;
                bool inWindow = Ask();
                e.End.Value = Md(now.AddDays(20));
                e.Start.Value = Md(now.AddDays(10));
                EdMadeUpGates.Answer = true;
                bool outOfWindow = !Ask();
                e.Start.Value = "";
                e.End.Value = "";
                Test.Check(inWindow && outOfWindow, $"By date with the player's own dates: on inside them though the game says no ({inWindow}), off outside them though it says yes ({outOfWindow})");

                // "Mod enabled" off: the game's answer whatever the mode
                e.Mode.Value = ED.EventMode.Off;
                EdRun.Detach(); // the mod's switch alone, the decorations left out of it
                ED.Plugin.Enabled.Value = false;
                EdMadeUpGates.Answer = true;
                bool modOff = Ask() && e.IsOn();
                ED.Plugin.Enabled.Value = true;
                EdRun.Attach();
                Test.Check(modOff, $"with the mod switched off the game's own answer comes through, whatever the event is set to ({modOff})");

                // how it is said: the start line of the log and the "On today" line
                e.Mode.Value = ED.EventMode.On;
                ED.Events.All.Add(e);
                string described = ED.Events.Describe(), today = ED.OptionsPage.Today();
                e.Mode.Value = ED.EventMode.Off;
                string todayOff = ED.OptionsPage.Today();
                Test.Check(described.Contains("BenchTest (On, new in this game version)") && today.Contains(" · BenchTest: on") && todayOff.Contains(" · BenchTest: off"),
                    $"the log's start line names it as new (\"{described}\"), and \"On today\" has it after Halloween (\"{today}\")");
            }
            finally
            {
                ED.Events.All.Remove(e);
                ED.Events.All.Remove(e2);
                harmony.UnpatchSelf();
                table.Remove("BenchTest");
                table.Remove("BenchDated");
                EdRun.Arrange();
            }
            HarmonyLib.Patches left = Harmony.GetPatchInfo(plain);
            int prefixes = left != null ? left.Prefixes.Count : 0;
            EdMadeUpGates.Answer = true;
            Test.Check(!table.ContainsKey("BenchTest") && !table.ContainsKey("BenchDated") && ED.Events.All.IndexOf(e) < 0 && prefixes == 0 && Ask() && !ED.OptionsPage.Today().Contains("BenchTest"),
                $"the made-up event is gone again: out of the mod's table and lists, its gate unpatched ({prefixes} prefix(es)) and answering for itself, \"On today\" without it");
        }
    }
}

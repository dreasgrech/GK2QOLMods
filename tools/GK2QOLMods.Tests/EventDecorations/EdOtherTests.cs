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
using UI = EDasm::GK2DreasUI;

namespace GK2QOLMods.Tests
{
    // A made-up class of event gates, standing in for the game's TemporaryEventDates after a later update: two gates
    // shaped as the game's own (public static bool Is<Name>Event, without parameters or with one date), Halloween's,
    // and things that only look like gates.
    internal static class EdMadeUpGates
    {
        internal static bool Answer;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool IsBenchTestEvent() => Answer;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool IsBenchDatedEvent(DateTime date) => Answer;

        public static bool IsBenchDatedEvent() => Answer;     // the same event's other gate: named once

        public static bool IsHalloweenEvent() => Answer;      // the one the mod knows: not "another"

        public static int IsNumberEvent() => 1;               // not a yes or no
        public static bool IsTwoThingsEvent(DateTime a, int b) => Answer; // not a gate's parameters
        public static bool IsEvent() => Answer;               // no name
        public static bool HasBenchEvent() => Answer;         // not Is...
        internal static bool IsHiddenEvent() => Answer;       // not public
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

        // The mod switches Halloween, which was read through before it got its switches (what it places, starts and
        // saves). An event a later game adds is left to the game on purpose (the user, 2026-11-18): the mod gives the
        // player no setting for it and puts no patch on it; it names it once in the log, and its options come with a
        // version of the mod written for it. The one piece of code for that is the mod's list of a gates class's other
        // events, asked of the game's class at start; here it is asked of the game's and of a made-up one.
        internal static IEnumerator LaterEvent()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            List<string> game = ED.Events.OtherGates(typeof(TemporaryEventDates));
            const string said = "event(s) this version of the mod doesn't know";
            bool inLog = SessionLogHas(said);
            Test.Check(game.Count == 0, game.Count == 0
                ? "the game has no event besides Halloween today"
                : $"the game has {game.Count} event(s) this version of the mod doesn't know: {string.Join(", ", game)}. Each is left to the game; read what it does, then give it its options in a new version");
            Test.Check(inLog == (game.Count > 0), game.Count > 0 ? $"the mod named them in the log at its start ({inLog})" : $"and the mod's start says nothing about unknown events ({!inLog})");
            yield return null;

            List<string> made = ED.Events.OtherGates(typeof(EdMadeUpGates));
            Test.Check(string.Join(", ", made) == "BenchDated, BenchTest",
                $"asked of a made-up class of gates, the mod names its two other events, each once, and nothing that only looks like a gate (Halloween's own, a number, two parameters, no name, not \"Is\", not public): {string.Join(", ", made)}");

            // named is all: no patch, no setting, no tab, no word in "On today"
            MethodInfo plain = typeof(EdMadeUpGates).GetMethod(nameof(EdMadeUpGates.IsBenchTestEvent));
            HarmonyLib.Patches info = Harmony.GetPatchInfo(plain);
            int patches = info != null ? info.Prefixes.Count + info.Postfixes.Count + info.Finalizers.Count + info.Transpilers.Count : 0;
            EdMadeUpGates.Answer = true;
            bool yes = (bool)plain.Invoke(null, null);
            EdMadeUpGates.Answer = false;
            bool no = !(bool)plain.Invoke(null, null);
            Test.Check(patches == 0 && yes && no, $"and that is all it does with them: {patches} patch(es) on such a gate, which answers for itself ({yes}, {no})");
            var sections = new List<string>();
            foreach (ConfigDefinition d in EdRun.Cfg.Keys) if (!sections.Contains(d.Section)) sections.Add(d.Section);
            sections.Sort(StringComparer.Ordinal);
            var tabs = new List<string>();
            UI.SettingsPage page = EdRun.Page();
            if (page != null)
            {
                foreach (UI.SettingsPage.Row r in page.Rows)
                {
                    string tab = r.ShownSection ?? r.Section;
                    if (page.InOptions(r) && !tabs.Contains(tab)) tabs.Add(tab);
                }
            }
            string today = ED.OptionsPage.Today();
            Test.Check(string.Join(", ", sections) == "General, Halloween, Safety" && string.Join(", ", tabs) == "General, Halloween" && today == "Halloween: off",
                $"the mod's settings are Halloween's alone: the cfg's sections {string.Join(", ", sections)}; the window's tabs {string.Join(", ", tabs)}; \"On today\" reads \"{today}\"");
        }

        // A line of the mod's in this session's log file holding the text (the mod's start is before any run).
        private static bool SessionLogHas(string text)
        {
            string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            if (!File.Exists(path)) return false;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.IndexOf(":GK2 Event Decorations]", StringComparison.Ordinal) >= 0 && line.IndexOf(text, StringComparison.Ordinal) >= 0) return true;
                }
            }
            return false;
        }
    }
}

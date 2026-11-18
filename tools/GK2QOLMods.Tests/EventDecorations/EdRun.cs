extern alias EDasm;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using GK2ModTestBench;
using HarmonyLib;
using ED = EDasm::GK2EventDecorations;
using UI = EDasm::GK2DreasUI;

namespace GK2QOLMods.Tests
{
    // What a run of GK2 Event Decorations' tests holds and gives back (the suite's holds, EventDecorationsTests), and
    // what every test starts from:
    // - the player's settings: every entry's value kept at the start and put back at the end; the cfg file is not
    //   written during the run (SaveOnConfigSet off), so a run that dies leaves the file as the player had it;
    // - the season: a prefix of the tests' own on the game's private date check (TemporaryEventDates.
    //   IsMonthDayInInclusiveRange, what both IsHalloweenEvent overloads end in), so that both directions of every
    //   rule run on any date: "the game's dates say Halloween" and "they say not" (the offline truth table,
    //   tests/HarmonyHarness-EventDecorations, forces it the same way);
    // - the mod's log: every line it writes during the run is kept; a warning or an error is either one a test said
    //   it would provoke (Expect) or a fault of the run (the closing check lists them).
    // No field here has a type of the mod's: the suite is added whether the mod is installed or not.
    internal static class EdRun
    {
        internal const string ModGuid = "dreas.gk2.eventdecorations";

        // ------------------------------------------------------------------ the mod runs

        // False with a SKIP line: the mod didn't start (its status says why), so nothing of it can be tested. The
        // test "the mod is running" is the one that fails then.
        internal static bool Ready()
        {
            if (ED.Plugin.Status == "Active") return true;
            Test.Skip($"the mod isn't running (its status: \"{ED.Plugin.Status}\")");
            return false;
        }

        internal static ConfigFile Cfg => ED.Plugin.Enabled.ConfigFile;

        // The mod's settings page (the options window's rows): the one the mod made in its Awake.
        internal static UI.SettingsPage Page()
        {
            foreach (UI.SettingsPage p in UI.SettingsPage.All)
            {
                if (string.Equals(p.OwnerId, ModGuid, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        // ------------------------------------------------------------------ the player's settings

        private static Dictionary<ConfigEntryBase, object> found; // null outside a run
        private static bool saveOnSetWas;
        private static DateTime fileWrittenAt;

        internal static void HoldSettings()
        {
            ConfigFile cfg = Cfg;
            found = new Dictionary<ConfigEntryBase, object>();
            foreach (ConfigDefinition d in cfg.Keys) found[cfg[d]] = cfg[d].BoxedValue;
            saveOnSetWas = cfg.SaveOnConfigSet;
            cfg.SaveOnConfigSet = false;
            fileWrittenAt = File.Exists(cfg.ConfigFilePath) ? File.GetLastWriteTimeUtc(cfg.ConfigFilePath) : DateTime.MinValue;
        }

        // The hold's end, also when the run is ended from outside: the mod listening again, every value back, the
        // world made to match once more (a run that changed no value back still may have forced the season).
        internal static void GiveSettingsBack()
        {
            if (found == null) return;
            try
            {
                Attach();
                PutBack();
                ED.Decorations.OnSettingChanged(null, EventArgs.Empty);
            }
            finally
            {
                Cfg.SaveOnConfigSet = saveOnSetWas;
                found = null;
            }
        }

        // Every entry back to what the run found; how many had to be set.
        internal static int PutBack()
        {
            int n = 0;
            if (found == null) return 0;
            foreach (KeyValuePair<ConfigEntryBase, object> kv in found)
            {
                if (Equals(kv.Key.BoxedValue, kv.Value)) continue;
                kv.Key.BoxedValue = kv.Value;
                n++;
            }
            return n;
        }

        // The entries that aren't what the run found, as "Section.Key = now (was then)".
        internal static List<string> Changed()
        {
            var list = new List<string>();
            if (found == null) return list;
            foreach (KeyValuePair<ConfigEntryBase, object> kv in found)
            {
                if (!Equals(kv.Key.BoxedValue, kv.Value)) list.Add($"{kv.Key.Definition.Section}.{kv.Key.Definition.Key} = {kv.Key.BoxedValue} (was {kv.Value})");
            }
            return list;
        }

        internal static bool SettingsHeld => found != null;

        // The cfg file was written since the run began.
        internal static bool FileWritten()
        {
            string path = Cfg.ConfigFilePath;
            return File.Exists(path) && File.GetLastWriteTimeUtc(path) != fileWrittenAt;
        }

        internal static ConfigEntry<T> Entry<T>(string section, string key)
        {
            return Cfg.TryGetEntry(section, key, out ConfigEntry<T> e) ? e : null;
        }

        internal static ConfigEntry<bool> Switch(string key) => Entry<bool>("Halloween", key);

        // ------------------------------------------------------------------ the mod's ear for a changed setting

        private static bool detached;

        // The mod follows every changed setting at once (Decorations.OnSettingChanged on the cfg's SettingChanged): the
        // decorations go up or down in the world. A test that only asks for answers over thousands of combinations
        // takes that ear off meanwhile (the answers don't depend on it), and puts it back.
        internal static void Detach()
        {
            if (detached) return;
            Cfg.SettingChanged -= ED.Decorations.OnSettingChanged;
            detached = true;
        }

        internal static void Attach()
        {
            if (!detached) return;
            Cfg.SettingChanged += ED.Decorations.OnSettingChanged;
            detached = false;
        }

        internal static bool Detached => detached;

        // ------------------------------------------------------------------ the season

        private static Harmony seasonHarmony;
        private static bool? season; // null: the game's own date check runs

        // What the game's own dates say today, and whether the tests can make them say either (read in BeforeRun).
        internal static bool RealSeason;
        internal static bool ForceWorks;
        internal static string ForceWhy = "not tried";

        private static bool SeasonPrefix(ref bool __result)
        {
            if (season == null) return true;
            __result = season.Value;
            return false;
        }

        internal static void BeginSeason()
        {
            season = null;
            ForceWorks = false;
            ForceWhy = "not tried";
            MethodInfo check = AccessTools.Method(typeof(TemporaryEventDates), "IsMonthDayInInclusiveRange");
            if (check == null)
            {
                ForceWhy = "the game has no TemporaryEventDates.IsMonthDayInInclusiveRange any more";
                return;
            }
            seasonHarmony = new Harmony(Plugin.Guid + ".season");
            seasonHarmony.Patch(check, prefix: new HarmonyMethod(typeof(EdRun), nameof(SeasonPrefix)));
        }

        internal static void EndSeason()
        {
            season = null;
            Harmony h = seasonHarmony;
            seasonHarmony = null;
            h?.UnpatchSelf();
        }

        // Asked once a run, from BeforeRun: the game's own answer today, then forced both ways and read back past the
        // mod's patches (Events.GameDates). A force that doesn't take is said, and the tests keep to today's season.
        internal static void ProbeSeason()
        {
            season = null;
            RealSeason = ED.Events.GameDates(ED.Events.Halloween);
            if (seasonHarmony == null) return;
            season = true;
            bool forcedIn = ED.Events.GameDates(ED.Events.Halloween);
            season = false;
            bool forcedOut = ED.Events.GameDates(ED.Events.Halloween);
            season = null;
            ForceWorks = forcedIn && !forcedOut;
            ForceWhy = ForceWorks ? "it takes both ways" : $"forced to Halloween the game's check said {forcedIn}, forced to not {forcedOut}";
        }

        // The game's dates made to say Halloween (true), not Halloween (false), or left to the calendar (null). Not a
        // setting, so the mod hears nothing: it is told, as a changed setting would tell it.
        internal static void Season(bool? halloween)
        {
            if (!ForceWorks) halloween = null;
            if (season == halloween) return;
            season = halloween;
            if (!detached && SettingsHeld) ED.Decorations.OnSettingChanged(null, EventArgs.Empty);
        }

        // What the game's own dates say now (forced or not).
        internal static bool SeasonNow => season ?? RealSeason;

        // The game's dates made to say "not Halloween" for a test that needs the out-of-season side; false when that
        // can't be had (the force doesn't work and today is in season).
        internal static bool OutOfSeason()
        {
            Season(false);
            return !SeasonNow;
        }

        internal static bool InSeason()
        {
            Season(true);
            return SeasonNow;
        }

        // ------------------------------------------------------------------ where every changing test starts

        // The settings every test that changes one starts from, whatever the player has and whatever the test before
        // left: the mod on, Halloween on Always with no dates of the player's and all three of its parts on Never
        // (nothing goes up in the world, the game's own evening row can't start the ghost's walk under a test, no
        // zombie is drawn with the Halloween head), every area and kind on, the mod's own texts in English. A test
        // then switches on the part it is about (set to "With Halloween", it shows at once: Halloween is on). The
        // decorations first, so nothing is put up on the way.
        internal static void Arrange()
        {
            Attach();
            // the mod's two lines ("On today", Status) are in the game's language, and the tests read their English
            if (UI.Texts.LanguageOverride != "en")
            {
                UI.Texts.LanguageOverride = "en";
                UI.Texts.Forget();
            }
            ED.Halloween.Decorations.Value = ED.PieceMode.Off;
            ED.Halloween.Ghost.Value = ED.PieceMode.Off;
            ED.Halloween.ZombieHead.Value = ED.PieceMode.Off;
            ED.Plugin.Enabled.Value = true;
            ED.Plugin.SendGhostHome.Value = true;
            ED.Events.Halloween.Mode.Value = ED.EventMode.On;
            ED.Events.Halloween.Start.Value = "";
            ED.Events.Halloween.End.Value = "";
            foreach (ED.Halloween.Area a in ED.Halloween.Areas) a.Entry.Value = true;
            foreach (ED.Halloween.Kind k in ED.Halloween.Kinds) k.Entry.Value = true;
            ED.Halloween.Glow.Value = true;
            ED.Halloween.BlockWay.Value = true;
            Season(null);
        }

        // ------------------------------------------------------------------ the mod's log

        private const int KeptLines = 4000;
        private static readonly List<string> lines = new List<string>();      // "Level|text", in order
        private static readonly List<string> expecting = new List<string>();  // parts of warnings a test provokes now
        internal static readonly List<string> Unexpected = new List<string>();
        internal static int ExpectedSeen;
        // The warnings tests provoked, over every run of this game session: the session's log holds them too.
        private static readonly HashSet<string> provokedEver = new HashSet<string>(StringComparer.Ordinal);
        private static bool listening;

        internal static void BeginListen()
        {
            lines.Clear();
            expecting.Clear();
            Unexpected.Clear();
            ExpectedSeen = 0;
            ED.Plugin.Log.LogEvent += OnLog;
            listening = true;
        }

        internal static void EndListen()
        {
            if (!listening) return;
            listening = false;
            ED.Plugin.Log.LogEvent -= OnLog;
        }

        private static void OnLog(object sender, LogEventArgs e)
        {
            string text = e.Data != null ? e.Data.ToString() : "";
            if (lines.Count < KeptLines) lines.Add(e.Level + "|" + text);
            if ((e.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0) return;
            for (int k = 0; k < expecting.Count; k++)
            {
                if (text.IndexOf(expecting[k], StringComparison.Ordinal) < 0) continue;
                ExpectedSeen++;
                provokedEver.Add(text);
                return;
            }
            Unexpected.Add(e.Level + ": " + text);
        }

        // A place in the mod's log, for Count and Last.
        internal static int Mark => lines.Count;

        // The lines since a mark that hold the text.
        internal static int Count(string part, int from = 0)
        {
            int n = 0;
            for (int k = Math.Max(0, from); k < lines.Count; k++)
            {
                if (lines[k].IndexOf(part, StringComparison.Ordinal) >= 0) n++;
            }
            return n;
        }

        // The last line since a mark that holds the text, without its level; null if none.
        internal static string Last(string part, int from = 0)
        {
            for (int k = lines.Count - 1; k >= Math.Max(0, from); k--)
            {
                if (lines[k].IndexOf(part, StringComparison.Ordinal) >= 0) return lines[k].Substring(lines[k].IndexOf('|') + 1);
            }
            return null;
        }

        // A warning this test provokes on purpose: `using (EdRun.Expect("dates are month-day")) { ... }`.
        internal static IDisposable Expect(string part) => new Expectation(part);

        private sealed class Expectation : IDisposable
        {
            private readonly string part;

            internal Expectation(string part)
            {
                this.part = part;
                expecting.Add(part);
            }

            public void Dispose() => expecting.Remove(part);
        }

        // Between tests: no expectation outlives its test (one that threw inside its using keeps none either way).
        internal static void ForgetExpectations() => expecting.Clear();

        // A line of the session's log file that a test of this session provoked.
        internal static bool Provoked(string logLine)
        {
            foreach (string text in provokedEver)
            {
                // a message of several lines: its first line is what the file's line holds
                int cut = text.IndexOf('\n');
                string first = cut >= 0 ? text.Substring(0, cut).TrimEnd('\r') : text;
                if (first.Length > 0 && logLine.IndexOf(first, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }
    }
}

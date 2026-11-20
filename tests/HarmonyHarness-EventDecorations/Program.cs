using System; using System.Collections.Generic; using System.IO; using System.Linq; using System.Reflection;
using System.Runtime.Serialization; using BepInEx.Configuration; using BepInEx.Logging; using HarmonyLib;

// The truth table of GK2 Event Decorations (2026-11-15, after a part left on "Always" showed the decorations out of season
// and the Ghost setting was found leaking into the decorations and the zombie head through the game's own gate call):
// the built mod's real Harmony patches on the game's real methods, in a .NET Framework process, over every combination of
// the settings that decide what shows: Mod enabled x Halloween's mode x own dates x the three parts.
// For each, the answers the game gets (the gate TemporaryEventDates.IsHalloweenEvent, both overloads: the ghost's scripts;
// HalloweenConditionalLoadContentPart.ShouldLoad: the decorations at load; HalloweenHeadSkinOverride.Resolve: the head)
// and the options window's "On today" line are compared with a model of the rules written here, apart from the mod's code:
//   the event today = mod off: the game's dates; Always: yes; Never: no; By date: own dates if both are valid, else the game's
//   a part         = mod off: the game's dates; part Never: no; With Halloween: the event today (a part has no "Always": taken out 2026-11-18)
// The table runs three times: with the game's own date check as it is today (read before anything is patched), then forced
// to "Halloween" and to "not Halloween" (a prefix on its private IsMonthDayInInclusiveRange), so both directions of every
// rule are exercised on any date: a setting forcing a part on out of season, and one taking it off in season. The force
// checks itself: with the mod off every answer is the game's, so a force that didn't take would fail those rows.
class P
{
    const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    static bool? forceGame; // null: the game's own date check; else its answer

    static bool ForceGameDates(ref bool __result)
    {
        if (forceGame == null) return true;
        __result = forceGame.Value;
        return false;
    }

    static int fails, checks, combinations;
    static readonly List<string> failures = new List<string>();

    static void Check(bool ok, string what)
    {
        checks++;
        if (ok) return;
        fails++;
        if (failures.Count < 40) failures.Add(what);
    }

    static int Main()
    {
        var dirs = new[] { GamePaths.Managed, GamePaths.BepInExCore };
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => { var n = new AssemblyName(e.Name).Name + ".dll"; foreach (var d in dirs) { var p = Path.Combine(d, n); if (File.Exists(p)) return Assembly.LoadFrom(p); } return null; };
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")); // tests/X/bin/Release/net48 -> repo root
        var mod = Assembly.LoadFrom(Path.Combine(repo, "src", "GK2EventDecorations", "bin", "Release", "netstandard2.1", "GK2EventDecorations.dll"));
        Type T(string n) => mod.GetType("GK2EventDecorations." + n, true);

        // the game's own answer today, before any patch
        bool realToday = TemporaryEventDates.IsHalloweenEvent();
        Console.WriteLine($"INFO today {DateTime.Now:yyyy-MM-dd}: the game's own Halloween check says {(realToday ? "on" : "off")}");

        // the mod's settings bound as Plugin.BindConfig binds them, in a throwaway cfg
        string cfgPath = Path.Combine(Path.GetTempPath(), "gk2-eventdecorations-harness-" + Guid.NewGuid().ToString("N") + ".cfg");
        var cfg = new ConfigFile(cfgPath, false) { SaveOnConfigSet = false };
        var plugin = T("Plugin");
        var log = new ManualLogSource("harness");
        var warnings = new List<string>();
        log.LogEvent += (s, e) => { if (e.Level == LogLevel.Warning || e.Level == LogLevel.Error) warnings.Add(e.Data?.ToString()); };
        plugin.GetField("Log", BF).SetValue(null, log);
        var enabled = cfg.Bind("General", "Enabled", true, "");
        plugin.GetField("Enabled", BF).SetValue(null, enabled);
        plugin.GetField("SendGhostHome", BF).SetValue(null, cfg.Bind("Halloween", "SendGhostHome", true, ""));
        T("Events").GetMethod("Bind", BF).Invoke(null, new object[] { cfg });
        T("Halloween").GetMethod("Bind", BF).Invoke(null, new object[] { cfg });
        object ev = T("Events").GetField("Halloween", BF).GetValue(null);
        var mode = (ConfigEntryBase)ev.GetType().GetField("Mode", BF).GetValue(ev);
        var start = (ConfigEntry<string>)ev.GetType().GetField("Start", BF).GetValue(ev);
        var end = (ConfigEntry<string>)ev.GetType().GetField("End", BF).GetValue(ev);
        var deco = (ConfigEntryBase)T("Halloween").GetField("Decorations", BF).GetValue(null);
        var ghost = (ConfigEntryBase)T("Halloween").GetField("Ghost", BF).GetValue(null);
        var head = (ConfigEntryBase)T("Halloween").GetField("ZombieHead", BF).GetValue(null);
        var bypass = T("Events").GetField("Bypass", BF);
        var today = T("OptionsPage").GetMethod("Today", BF);
        var tableMissing = T("HalloweenHeadSkinOverride_Resolve_Patch").GetField("tableMissing", BF);

        // the mod's patches, as Plugin.StartMod applies them
        var h = new Harmony("harness.eventdecorations");
        foreach (var t in (Type[])plugin.GetField("PatchClasses", BF).GetValue(null)) h.PatchAll(t);
        Console.WriteLine("INFO patched: " + string.Join(", ", h.GetPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name)));
        var force = new Harmony("harness.eventdecorations.force");
        force.Patch(typeof(TemporaryEventDates).GetMethod("IsMonthDayInInclusiveRange", BF), prefix: new HarmonyMethod(typeof(P).GetMethod(nameof(ForceGameDates), BF)));

        MethodInfo gateNoDate = typeof(TemporaryEventDates).GetMethod(nameof(TemporaryEventDates.IsHalloweenEvent), Type.EmptyTypes);
        MethodInfo gateWithDate = typeof(TemporaryEventDates).GetMethod(nameof(TemporaryEventDates.IsHalloweenEvent), new[] { typeof(DateTime) });
        var part = (HalloweenConditionalLoadContentPart)FormatterServices.GetUninitializedObject(typeof(HalloweenConditionalLoadContentPart));
        string Dm(DateTime d) => $"{d.Day:D2}-{d.Month:D2}";
        DateTime now = DateTime.Now;
        // own dates: none; a window around today; one well away from today; only the first day; a first day that isn't a date
        var dates = new (string Name, string Start, string End, bool Valid, bool In)[]
        {
            ("none", "", "", false, false),
            ("around today", Dm(now.AddDays(-1)), Dm(now.AddDays(1)), true, true),
            ("away from today", Dm(now.AddDays(10)), Dm(now.AddDays(20)), true, false),
            ("first day only", Dm(now.AddDays(-1)), "", false, false),
            ("not a date", "45-13", Dm(now.AddDays(1)), false, false),
        };
        string[] modes = { "Auto", "On", "Off" }, pieces = { "Event", "Off" };
        Type modeT = mode.SettingType, pieceT = deco.SettingType;

        foreach (bool? forced in new bool?[] { null, true, false })
        foreach (bool en in new[] { true, false })
        foreach (string m in modes)
        foreach (var d in dates)
        foreach (string dv in pieces)
        foreach (string gv in pieces)
        foreach (string hv in pieces)
        {
            forceGame = forced;
            bool gameToday = forced ?? realToday;
            combinations++;
            enabled.Value = en;
            mode.BoxedValue = Enum.Parse(modeT, m);
            start.Value = d.Start;
            end.Value = d.End;
            deco.BoxedValue = Enum.Parse(pieceT, dv);
            ghost.BoxedValue = Enum.Parse(pieceT, gv);
            head.BoxedValue = Enum.Parse(pieceT, hv);
            string at = $"[game's dates {(forced == null ? "today" : forced.Value ? "forced in season" : "forced out of season")}, mod {(en ? "on" : "off")}, mode {m}, dates {d.Name}, decorations {dv}, ghost {gv}, head {hv}]";

            // the model
            bool evToday = !en ? gameToday : m == "On" ? true : m == "Off" ? false : d.Valid ? d.In : gameToday;
            bool Piece(string v) => !en ? gameToday : v == "Off" ? false : evToday;

            // what the game gets; the gate through reflection: Main was compiled before the patches, and the JIT inlines the
            // tiny gate into it (a direct call here never reached the patch: 288 false failures, 2026-11-15)
            bool gate = (bool)gateNoDate.Invoke(null, null);
            Check((int)bypass.GetValue(null) == 0, at + " the gate left Events.Bypass raised");
            bool gateDate = (bool)gateWithDate.Invoke(null, new object[] { DateTime.Now });
            bool load = part.ShouldLoad();
            Check((int)bypass.GetValue(null) == 0, at + " ShouldLoad left Events.Bypass raised");
            Check(gate == Piece(gv), $"{at} gate (the ghost's scripts) = {gate}, expected {Piece(gv)}");
            Check(gateDate == Piece(gv), $"{at} gate(DateTime) = {gateDate}, expected {Piece(gv)}");
            Check(load == Piece(dv), $"{at} decorations at load (ShouldLoad) = {load}, expected {Piece(dv)}");
            if (!Piece(hv))
            {
                // only where the head is expected unchanged: the swap reads the game's table, which needs the running game
                // (a call that reaches the table throws here: Unity's own code, outside the game; the game's Resolve only goes
                // there when it takes Halloween as on, which is the leak this checks)
                try
                {
                    int r = HalloweenHeadSkinOverride.Resolve(1051);
                    Check(r == 1051 && !(bool)tableMissing.GetValue(null), $"{at} zombie head 1051 -> {r} (table read: {tableMissing.GetValue(null)}), expected unchanged");
                }
                catch (Exception e)
                {
                    Check(false, $"{at} zombie head: Resolve went for the game's table (it took Halloween as on), expected the head unchanged ({e.GetType().Name})");
                    bypass.SetValue(null, 0); // a throw past the mod's finalizer would leave it raised for the next combination
                }
                Check((int)bypass.GetValue(null) == 0, at + " Resolve left Events.Bypass raised");
            }
            else
            {
                // the head expected swapped: Resolve must go for the game's table. Outside the game that shows as Unity's code
                // throwing (the game's own Resolve, the part following the event) or as the mod's prefix catching it and
                // marking the table unreadable (the part with Halloween, Halloween on Always); either way the decision was "swap". The swap itself
                // needs the running game.
                bool wentForTable;
                try
                {
                    HalloweenHeadSkinOverride.Resolve(1051);
                    wentForTable = (bool)tableMissing.GetValue(null);
                }
                catch (Exception)
                {
                    wentForTable = true;
                }
                tableMissing.SetValue(null, false);
                bypass.SetValue(null, 0);
                Check(wentForTable, $"{at} zombie head: Resolve left the head as it is, expected the Halloween swap");
            }

            // what the options window says
            string line = (string)today.Invoke(null, null);
            Check(line.Contains("decorations") == Piece(dv), $"{at} On today \"{line}\": decorations expected {(Piece(dv) ? "listed" : "not listed")}");
            Check(line.Contains("ghost") == Piece(gv), $"{at} On today \"{line}\": ghost expected {(Piece(gv) ? "listed" : "not listed")}");
            Check(line.Contains("zombie head") == Piece(hv), $"{at} On today \"{line}\": zombie head expected {(Piece(hv) ? "listed" : "not listed")}");
            bool nothing = !Piece(dv) && !Piece(gv) && !Piece(hv);
            Check(line.StartsWith("Halloween: off") == nothing, $"{at} On today \"{line}\": \"off\" expected {nothing}");
            bool datesNote = en && m == "Auto" && (d.Name == "first day only" || d.Name == "not a date");
            Check(line.Contains("don't count") == datesNote, $"{at} On today \"{line}\": the own-dates note expected {datesNote}");
            Check(line.EndsWith("(the game's own dates)") == !en, $"{at} On today \"{line}\": \"(the game's own dates)\" expected {!en}");
        }

        h.UnpatchSelf();
        force.UnpatchSelf();
        try { File.Delete(cfgPath); } catch { }
        // the only warnings the table may cause: the invalid own date (once per value), and the head table unreadable outside the
        // game (the swap checks reach for it on purpose)
        var unexpected = warnings.Where(w => w == null || !w.Contains("dates are day-month") && !w.Contains("head table could not be read")).ToList();
        Check(unexpected.Count == 0, "unexpected warnings: " + string.Join(" | ", unexpected.Take(5)));
        SettingsOf100(T);
        foreach (string f in failures) Console.WriteLine("FAIL " + f);
        Console.WriteLine(fails == 0 ? $"ALL PASS ({checks} checks over {combinations} combinations)" : $"FAILURES: {fails} of {checks} checks");
        return fails == 0 ? 0 : 1;
    }

    // A settings file of 1.0.0 (its dates month-day, no DateForm line) at the first start of a later version, and at
    // the starts after: the dates' steps of Plugin.Awake on scratch files (is the file 1.0.0's, asked before anything
    // is bound; the binding; the dates turned round if it was; one save). Since 1.0.1 a date is day-month (the user,
    // 2026-11-19: "migrate any settings ... so we keep existing inputs").
    static void SettingsOf100(Func<string, Type> T)
    {
        Type old = T("OldDates"), gameEvent = T("GameEvent");
        string dir = Path.Combine(Path.GetTempPath(), "gk2-eventdecorations-harness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        (string Start, string End, bool WasOld, string Text) Start(string path)
        {
            bool was = (bool)old.GetMethod("FileIsMonthDay", BF).Invoke(null, new object[] { path });
            var cfg = new ConfigFile(path, false) { SaveOnConfigSet = false };
            object e = Activator.CreateInstance(gameEvent, BF, null, new object[] { "Halloween" }, null);
            gameEvent.GetMethod("Bind", BF).Invoke(e, new object[] { cfg, "the mode", "" });
            old.GetMethod("Bind", BF).Invoke(null, new object[] { cfg });
            if (was) old.GetMethod("Turn", BF).Invoke(null, new object[] { e, null });
            cfg.Save();
            string s = ((ConfigEntry<string>)gameEvent.GetField("Start", BF).GetValue(e)).Value, en = ((ConfigEntry<string>)gameEvent.GetField("End", BF).GetValue(e)).Value;
            return (s, en, was, File.ReadAllText(path));
        }
        string File100(string s, string e) => "[General]\r\n\r\nEnabled = true\r\n\r\n[Halloween]\r\n\r\nMode = Auto\r\n\r\nStart = " + s + "\r\n\r\nEnd = " + e + "\r\n";
        try
        {
            var cases = new (string Name, string Start, string End, string WantStart, string WantEnd)[]
            {
                ("both dates", "11-01", "11-20", "01-11", "20-11"),
                ("the game's own Halloween typed in", "10-24", "11-07", "24-10", "07-11"),
                ("a window past the new year", "12-20", "01-06", "20-12", "06-01"),
                ("written with slashes and no zeros", "10/1", "11/30", "01-10", "30-11"),
                ("the first date only", "10-24", "", "24-10", ""),
                ("the last date only", "", "11-07", "", "07-11"),
                ("no dates", "", "", "", ""),
                ("something 1.0.0 took for no date beside a date", "11-1A", "11-20", "11-1A", "20-11"),
                ("a date 1.0.0 took for none, being day-month already", "24-10", "11-07", "24-10", "07-11"),
            };
            int n = 0;
            foreach (var c in cases)
            {
                string path = Path.Combine(dir, "case" + n++ + ".cfg");
                File.WriteAllText(path, File100(c.Start, c.End));
                var first = Start(path);
                Check(first.WasOld, $"settings of 1.0.0, {c.Name}: the file without a DateForm line is taken as 1.0.0's");
                Check(first.Start == c.WantStart && first.End == c.WantEnd, $"settings of 1.0.0, {c.Name}: \"{c.Start}\", \"{c.End}\" became \"{first.Start}\", \"{first.End}\" (expected \"{c.WantStart}\", \"{c.WantEnd}\")");
                Check(first.Text.Contains("DateForm = day-month") && first.Text.Contains("Start = " + c.WantStart) && first.Text.Contains("End = " + c.WantEnd), $"settings of 1.0.0, {c.Name}: the file holds the dates and the DateForm line after the one save");
                var second = Start(path);
                var third = Start(path);
                Check(!second.WasOld && !third.WasOld && second.Start == c.WantStart && second.End == c.WantEnd && third.Start == c.WantStart && third.End == c.WantEnd,
                    $"settings of 1.0.0, {c.Name}: untouched at the next two starts (taken as 1.0.0's {second.WasOld}, {third.WasOld}; \"{second.Start}\", \"{second.End}\"; \"{third.Start}\", \"{third.End}\")");
            }
            var fresh = Start(Path.Combine(dir, "fresh.cfg"));
            Check(!fresh.WasOld && fresh.Start == "" && fresh.End == "" && fresh.Text.Contains("DateForm = day-month"), $"a first start with no settings file: not taken as 1.0.0's ({fresh.WasOld}), the DateForm line written");
            string later = Path.Combine(dir, "later.cfg");
            File.WriteAllText(later, "[General]\r\n\r\nDateForm = anything\r\n\r\n[Halloween]\r\n\r\nStart = 01-11\r\n\r\nEnd = 05-12\r\n");
            var l = Start(later);
            Check(!l.WasOld && l.Start == "01-11" && l.End == "05-12", $"a file with the DateForm line, whatever it says, is not 1.0.0's: its dates stay (\"{l.Start}\", \"{l.End}\")");
            // the options window's check of a typed date (the window library's Row.TextAccept)
            MethodInfo typed = gameEvent.GetMethod("TypedDate", BF);
            string Typed(string t) => (string)typed.Invoke(null, new object[] { t });
            foreach (var (text, want) in new[] { ("24-10", "24-10"), ("1-11", "01-11"), ("29-2", "29-02"), ("", ""), ("10-24", null), ("31-4", null), ("45-13", null), ("24-", null), ("2410", null), ("11-1A", null) })
            {
                Check(Typed(text) == want, $"a date typed into the window, \"{text}\": {(Typed(text) == null ? "refused" : "kept as \"" + Typed(text) + "\"")} (expected {(want == null ? "refused" : "\"" + want + "\"")})");
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

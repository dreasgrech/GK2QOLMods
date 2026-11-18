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
//   a part         = mod off: the game's dates; event Never: no; part Always: yes; part Never: no; With Halloween: the event today
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
        string Md(DateTime d) => $"{d.Month:D2}-{d.Day:D2}";
        DateTime now = DateTime.Now;
        // own dates: none; a window around today; one well away from today; only the first day; a first day that isn't a date
        var dates = new (string Name, string Start, string End, bool Valid, bool In)[]
        {
            ("none", "", "", false, false),
            ("around today", Md(now.AddDays(-1)), Md(now.AddDays(1)), true, true),
            ("away from today", Md(now.AddDays(10)), Md(now.AddDays(20)), true, false),
            ("first day only", Md(now.AddDays(-1)), "", false, false),
            ("not a date", "13-45", Md(now.AddDays(1)), false, false),
        };
        string[] modes = { "Auto", "On", "Off" }, pieces = { "Event", "On", "Off" };
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
            bool Piece(string v) => !en ? gameToday : m == "Off" ? false : v == "On" ? true : v == "Off" ? false : evToday;

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
                // marking the table unreadable (the part on Always); either way the decision was "swap". The swap itself
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
            bool decoAlways = en && Piece(dv) && dv == "On" && !evToday;
            Check(line.Contains("decorations (set to Always)") == decoAlways, $"{at} On today \"{line}\": \"set to Always\" on the decorations expected {decoAlways}");
            bool datesNote = en && m == "Auto" && (d.Name == "first day only" || d.Name == "not a date");
            Check(line.Contains("don't count") == datesNote, $"{at} On today \"{line}\": the own-dates note expected {datesNote}");
            Check(line.EndsWith("(the game's own dates)") == !en, $"{at} On today \"{line}\": \"(the game's own dates)\" expected {!en}");
        }

        h.UnpatchSelf();
        force.UnpatchSelf();
        try { File.Delete(cfgPath); } catch { }
        // the only warnings the table may cause: the invalid own date (once per value), and the head table unreadable outside the
        // game (the swap checks reach for it on purpose)
        var unexpected = warnings.Where(w => w == null || !w.Contains("dates are month-day") && !w.Contains("head table could not be read")).ToList();
        Check(unexpected.Count == 0, "unexpected warnings: " + string.Join(" | ", unexpected.Take(5)));
        foreach (string f in failures) Console.WriteLine("FAIL " + f);
        Console.WriteLine(fails == 0 ? $"ALL PASS ({checks} checks over {combinations} combinations)" : $"FAILURES: {fails} of {checks} checks");
        return fails == 0 ? 0 : 1;
    }
}

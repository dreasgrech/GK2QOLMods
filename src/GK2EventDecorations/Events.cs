using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace GK2EventDecorations
{
    internal enum EventMode
    {
        Auto, // the game's own dates, or the Start/End set here
        On,   // every day
        Off   // never
    }

    // One of the game's temporary events. The game asks TemporaryEventDates.Is<Name>Event() whenever it needs to know
    // whether the event is on (decorations at save load, the event's scripts, display swaps); docs/seasonal-events.md.
    internal sealed class GameEvent
    {
        internal readonly string Name;
        internal ConfigEntry<EventMode> Mode;
        internal ConfigEntry<string> Start;
        internal ConfigEntry<string> End;

        private string parsedStart, parsedEnd;
        private int windowStart = -1, windowEnd = -1; // month * 100 + day; -1: not set or not valid

        internal GameEvent(string name)
        {
            Name = name;
        }

        internal void Bind(ConfigFile config, string modeText, string defaultDates)
        {
            Mode = config.Bind(Name, "Mode", EventMode.Auto, modeText);
            Start = config.Bind(Name, "Start", "",
                "By date (Auto) only: the first day of the event as day-month, e.g. 01-10. Empty: the game's own dates" + defaultDates + ". Needs the last day too.");
            End = config.Bind(Name, "End", "",
                "By date (Auto) only: the last day of the event as day-month, e.g. 30-11 (it may wrap past the new year, e.g. 20-12 to 06-01). Empty: the game's own dates. Needs the first day too.");
        }

        // Is the event on today, by this mod's settings? (With the mod switched off: by the game's own check.)
        internal bool IsOn()
        {
            if (!Plugin.Enabled.Value)
            {
                return Events.GameDates(this);
            }
            switch (Mode.Value)
            {
                case EventMode.On:
                    return true;
                case EventMode.Off:
                    return false;
            }
            ParseWindow();
            if (windowStart < 0 || windowEnd < 0)
            {
                return Events.GameDates(this);
            }
            DateTime now = DateTime.Now;
            int today = now.Month * 100 + now.Day;
            // the game's own rule (TemporaryEventDates.IsMonthDayInInclusiveRange): inclusive, wrapping the new year
            return windowStart <= windowEnd ? today >= windowStart && today <= windowEnd : today >= windowStart || today <= windowEnd;
        }

        // Mode Auto without own dates: the game's own check decides, so our patches can let the game's code run.
        internal bool UsesGameDates() => Mode.Value == EventMode.Auto && CustomWindow() == null;

        // "01-10 to 30-11" (day-month, as the player types them) for the log's start line; null when the game's dates apply.
        internal string CustomWindow()
        {
            ParseWindow();
            return windowStart < 0 || windowEnd < 0 ? null : $"{windowStart % 100:D2}-{windowStart / 100:D2} to {windowEnd % 100:D2}-{windowEnd / 100:D2}";
        }

        // Own dates typed in but not counting (only one of the two, or not day-month), while they would count (By date):
        // the "On today" line says so, as the window can't (a wrong date was only in the log).
        internal bool DatesDontCount()
        {
            if (Mode.Value != EventMode.Auto || CustomWindow() != null)
            {
                return false;
            }
            return (Start.Value ?? "").Trim().Length > 0 || (End.Value ?? "").Trim().Length > 0;
        }

        private void ParseWindow()
        {
            string s = Start.Value ?? "", e = End.Value ?? "";
            if (s == parsedStart && e == parsedEnd)
            {
                return;
            }
            parsedStart = s;
            parsedEnd = e;
            windowStart = windowEnd = -1;
            bool hasStart = s.Trim().Length > 0, hasEnd = e.Trim().Length > 0;
            int a = hasStart ? ParseDayMonth(s) : -1, b = hasEnd ? ParseDayMonth(e) : -1;
            // a value that is there but not a date: say so once per value; only one of the two (still typing the
            // other): quietly the game's dates
            if ((hasStart && a < 0) || (hasEnd && b < 0))
            {
                Plugin.Log.LogWarning($"[{Name}] Start \"{s}\" / End \"{e}\": dates are day-month like 24-10; using the game's own dates.");
                return;
            }
            if (a < 0 || b < 0)
            {
                return;
            }
            windowStart = a;
            windowEnd = b;
        }

        // A date as the player writes it since 1.0.1: day-month ("24-10", "1/11", "01.10"); month * 100 + day, or -1.
        internal static int ParseDayMonth(string text) => Parse(text, dayFirst: true);

        // A date as 1.0.0 took it: month-day. Only OldDates asks, to turn a 1.0.0 file's dates round once.
        internal static int ParseMonthDay(string text) => Parse(text, dayFirst: false);

        private static int Parse(string text, bool dayFirst)
        {
            string[] parts = (text ?? "").Trim().Split('-', '/', '.');
            // the numbers read as 1.0.0 read them (int.TryParse), so a date it took is one still, turned round or not
            if (parts.Length != 2 || !int.TryParse(parts[0], out int first) || !int.TryParse(parts[1], out int second))
            {
                return -1;
            }
            int day = dayFirst ? first : second, month = dayFirst ? second : first;
            if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(2024, month)) // 2024: 29 Feb allowed
            {
                return -1;
            }
            return month * 100 + day;
        }

        // month * 100 + day as the player reads it: "24-10".
        internal static string DayMonth(int date) => $"{date % 100:D2}-{date / 100:D2}";

        // The options window's check of a date typed into its box (the window library's Row.TextAccept): the date
        // tidied ("1-11" becomes "01-11"), an emptied box as it is (no own date), null for what is no day of the
        // year: the box then says again what it held.
        internal static string TypedDate(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length == 0)
            {
                return "";
            }
            int date = ParseDayMonth(t);
            return date < 0 ? null : DayMonth(date);
        }
    }

    // 1.0.0 took the player's own dates as month-day; since 1.0.1 they are day-month (the user, 2026-11-19), and a
    // settings file 1.0.0 wrote has its dates turned round once so the same days stay set. Such a file is known by
    // what it lacks: the [General] DateForm line, which every later version writes. The line's value decides nothing
    // (a default put back by anything changes nothing); only a file that was there before this start without the
    // line is taken as 1.0.0's. A date 1.0.0 itself did not take ("11-1A", or "24-10" typed the other way round
    // already) is left as it is.
    internal static class OldDates
    {
        internal const string Section = "General", Key = "DateForm", Form = "day-month";
        private static readonly char[] Blanks = { ' ', '\t' };

        // Asked before anything is bound (binding and saving writes the line): the file is there and has no DateForm.
        internal static bool FileIsMonthDay(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    return false; // a first start: nothing to turn round
                }
                string[] lines = System.IO.File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimStart(Blanks); // not TrimStart(): the offline harness runs on .NET Framework, which lacks it
                    if (line.StartsWith(Key, StringComparison.Ordinal) && line.Substring(Key.Length).TrimStart(Blanks).StartsWith("=", StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"The settings file could not be read to see whether its dates are 1.0.0's month-day ({e.GetType().Name}: {e.Message}); they are taken as day-month.");
                return false;
            }
        }

        // The line itself: cfg only, no row.
        internal static ConfigEntry<string> Bind(ConfigFile config) =>
            config.Bind(Section, Key, Form,
                "Written by the mod: the dates in this file are day-month (24-10). They were month-day in version 1.0.0; a file without " +
                "this line has its dates turned round once. Leave the line as it is.");

        // A 1.0.0 date as it is written now, or null when 1.0.0 did not take the text as a date.
        internal static string Turned(string monthDay)
        {
            int date = GameEvent.ParseMonthDay(monthDay);
            return date < 0 ? null : GameEvent.DayMonth(date);
        }

        // The event's two dates turned round, in memory; the file is written by the caller's one save (the settings
        // page's, at its Publish), dates and line together. How many were turned.
        internal static int Turn(GameEvent e, BepInEx.Logging.ManualLogSource log)
        {
            int turned = 0;
            turned += TurnOne(e, e.Start, "Start", log);
            turned += TurnOne(e, e.End, "End", log);
            return turned;
        }

        private static int TurnOne(GameEvent e, ConfigEntry<string> entry, string name, BepInEx.Logging.ManualLogSource log)
        {
            string was = entry.Value ?? "";
            if (was.Trim().Length == 0)
            {
                return 0;
            }
            string now = Turned(was);
            if (now == null)
            {
                log?.LogInfo($"[{e.Name}] {name} \"{was}\" in the settings file of version 1.0.0 was no month-day date there: left as it is (dates are day-month now).");
                return 0;
            }
            entry.Value = now;
            log?.LogInfo($"[{e.Name}] {name}: your date \"{was}\" (month-day, version 1.0.0) is written \"{now}\" now (day-month); the same day.");
            return 1;
        }
    }

    // The events this version of the mod knows: Halloween, the only one the game has (1.012.9). An event a later game
    // adds is left to the game entirely, on purpose (the user, 2026-11-18): nothing is known of what such an event
    // places, starts or saves until it has been read (as Halloween's was, docs/seasonal-events.md), so the mod gives
    // the player no switch for it; it is named once in the log (OtherGates), and support comes with a new version.
    internal static class Events
    {
        internal const string HalloweenName = "Halloween";

        internal static GameEvent Halloween;

        // While above zero our gate prefixes let the game's own date check run (GameDates).
        internal static int Bypass;

        internal static void Bind(ConfigFile config)
        {
            Halloween = new GameEvent(HalloweenName);
            Halloween.Bind(config,
                "By date (Auto): Halloween on the game's own dates (24 October to 7 November), or on your own dates below. " +
                "Always (On): every day. Never (Off): no Halloween at all. Its decorations, ghost and zombie head each show " +
                "with Halloween, or can be switched off on their own (Never).",
                " (24 October to 7 November)");
        }

        // The game's own answer for the event today, past our patches.
        internal static bool GameDates(GameEvent e)
        {
            Bypass++;
            try
            {
                return TemporaryEventDates.IsHalloweenEvent();
            }
            finally
            {
                Bypass--;
            }
        }

        // The names of the events a class of gates has besides Halloween: every public static bool Is<Name>Event method,
        // without parameters or with one date, as TemporaryEventDates' own pair for Halloween; each name once, in order.
        // Asked of the game's class at start, for the log. Plain System.Type reflection on purpose: the dependency
        // manifest checks Halloween's members (named in HalloweenPatches), and a gate a later game adds or removes is
        // no reason to stop the mod.
        internal static List<string> OtherGates(Type gates)
        {
            var names = new List<string>();
            MethodInfo[] methods = gates.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
            {
                string name = EventName(methods[i]);
                if (name != null && name != HalloweenName && !names.Contains(name))
                {
                    names.Add(name);
                }
            }
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        // "IsHalloweenEvent" -> "Halloween"; null for anything that isn't an event gate.
        private static string EventName(MethodInfo m)
        {
            string n = m.Name;
            if (m.ReturnType != typeof(bool) || n.Length <= "IsEvent".Length || !n.StartsWith("Is", StringComparison.Ordinal) || !n.EndsWith("Event", StringComparison.Ordinal))
            {
                return null;
            }
            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length > 1 || (ps.Length == 1 && ps[0].ParameterType != typeof(DateTime)))
            {
                return null;
            }
            return n.Substring(2, n.Length - "IsEvent".Length);
        }

        // One sentence for the log: Halloween's mode, its own dates when they count, the parts that don't follow it.
        internal static string Describe()
        {
            GameEvent e = Halloween;
            string window = e.CustomWindow();
            string overrides = GK2EventDecorations.Halloween.Overrides();
            return e.Name + " (" + e.Mode.Value + (window != null && e.Mode.Value == EventMode.Auto ? " " + window : "") + (overrides.Length > 0 ? "; " + overrides : "") + ")";
        }
    }
}

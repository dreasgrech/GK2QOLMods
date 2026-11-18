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
                "By date (Auto) only: the first day of the event as month-day, e.g. 10-01. Empty: the game's own dates" + defaultDates + ". Needs the last day too.");
            End = config.Bind(Name, "End", "",
                "By date (Auto) only: the last day of the event as month-day, e.g. 11-30 (it may wrap past the new year, e.g. 12-20 to 01-06). Empty: the game's own dates. Needs the first day too.");
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

        // "Custom dates 10-01 to 11-30" for the status row; null when the game's dates apply.
        internal string CustomWindow()
        {
            ParseWindow();
            return windowStart < 0 || windowEnd < 0 ? null : $"{windowStart / 100:D2}-{windowStart % 100:D2} to {windowEnd / 100:D2}-{windowEnd % 100:D2}";
        }

        // Own dates typed in but not counting (only one of the two, or not month-day), while they would count (By date):
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
            int a = hasStart ? ParseMonthDay(s) : -1, b = hasEnd ? ParseMonthDay(e) : -1;
            // a value that is there but not a date: say so once per value; only one of the two (still typing the
            // other): quietly the game's dates
            if ((hasStart && a < 0) || (hasEnd && b < 0))
            {
                Plugin.Log.LogWarning($"[{Name}] Start \"{s}\" / End \"{e}\": dates are month-day like 10-24; using the game's own dates.");
                return;
            }
            if (a < 0 || b < 0)
            {
                return;
            }
            windowStart = a;
            windowEnd = b;
        }

        private static int ParseMonthDay(string text)
        {
            string[] parts = (text ?? "").Trim().Split('-', '/', '.');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int month) || !int.TryParse(parts[1], out int day))
            {
                return -1;
            }
            if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(2024, month)) // 2024: 29 Feb allowed
            {
                return -1;
            }
            return month * 100 + day;
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
                "Always (On): every day. Never (Off): no Halloween at all, decorations, ghost and zombie head included, whatever they " +
                "are set to. Otherwise each of those follows this (With Halloween) or is set to Always or Never on its own.",
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

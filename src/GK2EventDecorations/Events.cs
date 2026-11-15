using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

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
        internal readonly bool Known; // written for this version (Halloween); else found in a newer game
        internal ConfigEntry<EventMode> Mode;
        internal ConfigEntry<string> Start;
        internal ConfigEntry<string> End;
        internal MethodInfo GameGate;      // the game's own method, for a found event
        internal bool GameGateTakesDate;   // only the (DateTime) overload exists

        private string parsedStart, parsedEnd;
        private int windowStart = -1, windowEnd = -1; // month * 100 + day; -1: not set or not valid

        internal GameEvent(string name, bool known)
        {
            Name = name;
            Known = known;
        }

        internal void Bind(ConfigFile config, string modeText, string defaultDates)
        {
            Mode = config.Bind(Name, "Mode", EventMode.Auto, modeText);
            Start = config.Bind(Name, "Start", "",
                "Auto only: the first day of the event as month-day, e.g. 10-01. Empty: the game's own dates" + defaultDates + ". Needs End too.");
            End = config.Bind(Name, "End", "",
                "Auto only: the last day of the event as month-day, e.g. 11-30 (it may wrap past the new year, e.g. 12-20 to 01-06). Empty: the game's own dates. Needs Start too.");
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

        // A Harmony prefix's body for a found event's gate: our answer instead of the game's date check.
        internal bool Answer(ref bool result)
        {
            if (!Plugin.Enabled.Value || Events.Bypass > 0 || UsesGameDates())
            {
                return true; // the game's own check, as without the mod
            }
            result = IsOn();
            return false;
        }
    }

    internal static class Events
    {
        internal const string HalloweenName = "Halloween";

        internal static GameEvent Halloween;
        internal static readonly List<GameEvent> All = new List<GameEvent>();

        // While above zero our gate prefixes let the game's own date check run (GameDates).
        internal static int Bypass;

        // The gates of events a newer game added (not Halloween), by event name; patched with GenericPrefix.
        private static readonly Dictionary<string, GameEvent> discovered = new Dictionary<string, GameEvent>();
        private static readonly List<MethodInfo> discoveredGates = new List<MethodInfo>();

        // Halloween first, then every other public static bool Is<Name>Event method on TemporaryEventDates. Plain
        // System.Type reflection on purpose: the dependency manifest checks Halloween's members (named in
        // HalloweenPatches), and a gate a later game adds or removes is no reason to stop the mod.
        internal static void Bind(ConfigFile config)
        {
            Halloween = new GameEvent(HalloweenName, known: true);
            Halloween.Bind(config,
                "Auto: the game's own dates (24 October to 7 November), or Start/End below. On: Halloween every day. Off: never. " +
                "The pieces below (decorations, ghost, zombie head) follow this unless set on their own.",
                " (24 October to 7 November)");
            All.Add(Halloween);

            MethodInfo[] methods = typeof(TemporaryEventDates).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                string name = EventName(m);
                if (name == null || name == HalloweenName)
                {
                    continue;
                }
                if (!discovered.TryGetValue(name, out GameEvent e))
                {
                    e = new GameEvent(name, known: false);
                    e.Bind(config,
                        "Auto: the game's own dates, or Start/End below. On: every day. Off: never. A game update added this " +
                        "event after this version of the mod was written: the mode switches the game's date check for it. " +
                        "Decorations are placed while a save loads: after a change, load your save again.",
                        "");
                    discovered.Add(name, e);
                    All.Add(e);
                }
                if (m.GetParameters().Length == 0)
                {
                    e.GameGate = m;
                    e.GameGateTakesDate = false;
                }
                else if (e.GameGate == null)
                {
                    e.GameGate = m;
                    e.GameGateTakesDate = true;
                }
                discoveredGates.Add(m);
            }
        }

        // The game's own answer for the event today, past our patches.
        internal static bool GameDates(GameEvent e)
        {
            Bypass++;
            try
            {
                if (e == Halloween)
                {
                    return TemporaryEventDates.IsHalloweenEvent();
                }
                if (e.GameGate == null)
                {
                    return false;
                }
                object r = e.GameGate.Invoke(null, e.GameGateTakesDate ? new object[] { DateTime.Now } : null);
                return r is bool b && b;
            }
            finally
            {
                Bypass--;
            }
        }

        // "IsHalloweenEvent" -> "Halloween"; null for anything that isn't an event gate (static bool, no parameters
        // or one DateTime, as TemporaryEventDates' own pair).
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

        internal static void PatchDiscovered(Harmony harmony)
        {
            HarmonyMethod prefix = new HarmonyMethod(typeof(Events).GetMethod(nameof(GenericPrefix), BindingFlags.NonPublic | BindingFlags.Static));
            for (int i = 0; i < discoveredGates.Count; i++)
            {
                harmony.Patch(discoveredGates[i], prefix: prefix);
            }
        }

        // One sentence for the log: which events the game has and which this version knows.
        internal static string Describe()
        {
            string s = "";
            for (int i = 0; i < All.Count; i++)
            {
                GameEvent e = All[i];
                string window = e.CustomWindow();
                s += (i == 0 ? "" : ", ") + e.Name + " (" + e.Mode.Value + (window != null && e.Mode.Value == EventMode.Auto ? " " + window : "") +
                     (e.Known ? "" : ", new in this game version") + ")";
            }
            return s;
        }

        private static bool GenericPrefix(MethodBase __originalMethod, ref bool __result)
        {
            string name = __originalMethod is MethodInfo mi ? EventName(mi) : null;
            if (name == null || !discovered.TryGetValue(name, out GameEvent e))
            {
                return true;
            }
            return e.Answer(ref __result);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace GK2EventDecorations
{
    internal enum EventMode
    {
        Auto, // the game's own dates
        On,   // every day
        Off   // never
    }

    // One of the game's temporary events. The game asks TemporaryEventDates.Is<Name>Event() whenever it needs to know
    // whether the event is on (decorations at save load, the event's scripts, display swaps); that one method is all a
    // mode has to answer (docs/seasonal-events.md).
    internal sealed class GameEvent
    {
        internal readonly string Name;
        internal readonly bool Known; // written for this version (Halloween); else found in a newer game
        internal ConfigEntry<EventMode> Mode;

        internal GameEvent(string name, bool known)
        {
            Name = name;
            Known = known;
        }

        // A Harmony prefix's body: Auto lets the game's date check run, On and Off answer instead of it.
        internal bool Answer(ref bool result)
        {
            if (!Plugin.Enabled.Value)
            {
                return true;
            }
            EventMode mode = Mode.Value;
            if (mode == EventMode.Auto)
            {
                return true;
            }
            result = mode == EventMode.On;
            return false;
        }
    }

    internal static class Events
    {
        internal const string HalloweenName = "Halloween";

        internal static GameEvent Halloween;
        internal static readonly List<GameEvent> All = new List<GameEvent>();

        // The gates of events a newer game added (not Halloween), by event name; patched with GenericPrefix.
        private static readonly Dictionary<string, GameEvent> discovered = new Dictionary<string, GameEvent>();
        private static readonly List<MethodInfo> discoveredGates = new List<MethodInfo>();

        // Halloween first, then every other public static bool Is<Name>Event method on TemporaryEventDates. Plain
        // System.Type reflection on purpose: the dependency manifest checks Halloween's members (named in
        // HalloweenPatches), and a gate a later game adds or removes is no reason to stop the mod.
        internal static void Bind(ConfigFile config)
        {
            Halloween = new GameEvent(HalloweenName, known: true);
            Halloween.Mode = config.Bind(HalloweenName, "Mode", EventMode.Auto,
                "Auto: the game's own dates (24 October to 7 November in game 1.011). On: Halloween every day. Off: never. " +
                "Covers everything the game ties to the event: the decorations, the ghost's walks through the village at night " +
                "and the Halloween zombie head. The decorations are placed while a save loads: after a change, load your save again.");
            All.Add(Halloween);

            MethodInfo[] methods = typeof(TemporaryEventDates).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                string name = EventName(m);
                if (name == null)
                {
                    continue;
                }
                if (name != HalloweenName && !discovered.ContainsKey(name))
                {
                    GameEvent e = new GameEvent(name, known: false);
                    e.Mode = config.Bind(name, "Mode", EventMode.Auto,
                        "Auto: the game's own dates. On: every day. Off: never. A game update added this event after this " +
                        "version of the mod was written: the mode switches the game's date check for it, which is what " +
                        "Halloween's decorations use too. Decorations are placed while a save loads: after a change, load your save again.");
                    discovered.Add(name, e);
                    All.Add(e);
                }
                if (name != HalloweenName)
                {
                    discoveredGates.Add(m);
                }
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
                s += (i == 0 ? "" : ", ") + e.Name + " (" + e.Mode.Value + (e.Known ? "" : ", new in this game version") + ")";
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

    // Both overloads: everything in the game calls the one without a date, which calls the other with DateTime.Now; the
    // second patch keeps the answer if Mono inlines the first into a caller.
    [HarmonyPatch(typeof(TemporaryEventDates), nameof(TemporaryEventDates.IsHalloweenEvent), new Type[] { })]
    internal static class TemporaryEventDates_IsHalloweenEvent_Patch
    {
        private static bool Prefix(ref bool __result) => Events.Halloween.Answer(ref __result);
    }

    [HarmonyPatch(typeof(TemporaryEventDates), nameof(TemporaryEventDates.IsHalloweenEvent), new Type[] { typeof(DateTime) })]
    internal static class TemporaryEventDates_IsHalloweenEventDate_Patch
    {
        private static bool Prefix(ref bool __result) => Events.Halloween.Answer(ref __result);
    }
}

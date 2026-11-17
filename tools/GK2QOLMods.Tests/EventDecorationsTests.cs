using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using GK2ModTestBench;
using UnityEngine;
using ED = GK2EventDecorations;

namespace GK2QOLMods.Tests
{
    // GK2 Event Decorations in the game: what the game is told and what stands in the world, against the settings as
    // they are. Read-only: no test changes a setting (the offline truth table, tests/HarmonyHarness-EventDecorations,
    // covers every combination of them; these look at the one the player has, in the real game).
    internal static class EventDecorationsTests
    {
        internal const string ModGuid = "dreas.gk2.eventdecorations";
        private const string Mod = "The mod", Halloween = "Halloween";

        // Nothing of the mod's own types here: the suite is added whether the mod is installed or not.
        internal static void Register()
        {
            TestSuite s = TestBench.Suite(ModGuid, "GK2 Event Decorations");
            s.CanStart = () => Chainloader.PluginInfos.ContainsKey(ModGuid) ? null : "GK2 Event Decorations isn't installed";
            s.Add(Mod, "the mod is running and knows the game's events", Running);
            s.Add(Halloween, "the game's answer to \"is it Halloween\" is what the settings say", Gate);
            s.Add(Halloween, "the game's own dates can still be asked past the mod, and the way past closes again", PastTheMod);
            s.Add(Halloween, "the decorations are up or down in the world as the settings say", InTheWorld);
            s.Add(Halloween, "every area and every kind of decoration has its setting", AreasAndKinds);
        }

        private static IEnumerator Running()
        {
            Test.Check(ED.Plugin.Status == "Active", $"its status is \"{ED.Plugin.Status}\" (Active when it started without a fault)");
            Test.Check(ED.Events.All.Count > 0 && ED.Events.All[0] == ED.Events.Halloween && ED.Events.Halloween != null, $"{ED.Events.All.Count} event(s) of the game known, Halloween the first");
            Test.Info($"the settings: mod {(ED.Plugin.Enabled.Value ? "enabled" : "off")}; {ED.Events.Describe()}");
            yield return null;
        }

        private static IEnumerator Gate()
        {
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

        private static IEnumerator PastTheMod()
        {
            int before = ED.Events.Bypass;
            bool first = ED.Events.GameDates(ED.Events.Halloween);
            bool second = ED.Events.GameDates(ED.Events.Halloween);
            Test.Check(first == second, $"the game's own dates say {(first ? "it is" : "it isn't")} Halloween today, twice the same");
            Test.Check(before == 0 && ED.Events.Bypass == 0, $"the way past the mod's patches is closed before and after ({before}, {ED.Events.Bypass})");
            yield return null;
        }

        private static IEnumerator InTheWorld()
        {
            MainGame game = MainGame.Instance;
            if (game == null || game.gameSceneConfigs == null)
            {
                Test.Warn("the game's scene list isn't there");
                yield break;
            }
            bool up = false;
            foreach (GameSceneConfig c in game.gameSceneConfigs)
            {
                if (c != null && c.TryGetLoadedContent(ED.Decorations.ContentName, out GameObject go) && go != null)
                {
                    up = true;
                    break;
                }
            }
            bool want = ED.Halloween.DecorationsOn();
            Test.Check(up == want, $"the decorations are {(up ? "up" : "down")}; the settings say {(want ? "up" : "down")} ({ED.Events.Describe()})");
            yield return null;
        }

        private static IEnumerator AreasAndKinds()
        {
            var unbound = new List<string>();
            foreach (ED.Halloween.Area a in ED.Halloween.Areas)
            {
                if (a.Entry == null) unbound.Add(a.Key);
            }
            Test.Check(ED.Halloween.Areas.Count > 0 && unbound.Count == 0, $"{ED.Halloween.Areas.Count} area(s), {unbound.Count} without a setting{(unbound.Count > 0 ? ": " + string.Join(", ", unbound) : "")}");
            unbound.Clear();
            foreach (ED.Halloween.Kind k in ED.Halloween.Kinds)
            {
                if (k.Entry == null) unbound.Add(k.Key);
            }
            Test.Check(ED.Halloween.Kinds.Count > 0 && unbound.Count == 0, $"{ED.Halloween.Kinds.Count} kind(s), {unbound.Count} without a setting{(unbound.Count > 0 ? ": " + string.Join(", ", unbound) : "")}");
            yield return null;
        }
    }
}

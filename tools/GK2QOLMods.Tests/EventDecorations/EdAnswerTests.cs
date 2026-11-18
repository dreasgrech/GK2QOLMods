extern alias EDasm;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using GK2ModTestBench;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using ED = EDasm::GK2EventDecorations;

namespace GK2QOLMods.Tests
{
    // What the game is told, in the running game: the truth table of tests/HarmonyHarness-EventDecorations (the same
    // rules, written here again apart from the mod's code), asked through the game's own methods as the game calls
    // them, under the game's own runtime, and with what the offline table can't reach: the real head table, the
    // game's expression function, and the two rows of the balance that start the ghost and send it home.
    //   the event today = mod off: the game's dates; Always: yes; Never: no; By date: own dates if both are valid, else the game's
    //   a part         = mod off: the game's dates; event Never: no; part Always: yes; part Never: no; With Halloween: the event today
    internal static class EdAnswerTests
    {
        // The game's own check, forced: the run's other tests lean on it.
        internal static IEnumerator Force()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            try
            {
                Test.Check(EdRun.ForceWorks, $"the game's own date check can be made to say Halloween or not: {EdRun.ForceWhy}{(EdRun.ForceWorks ? "" : " (the tests keep to today's real season: half of every rule goes untested)")}");
                if (!EdRun.ForceWorks) yield break;
                // with the mod off every answer is the game's own: the force shows through all of them
                ED.Plugin.Enabled.Value = false;
                EdRun.Season(true);
                bool gateIn = TemporaryEventDates.IsHalloweenEvent(), datedIn = TemporaryEventDates.IsHalloweenEvent(DateTime.Now);
                EdRun.Season(false);
                bool gateOut = TemporaryEventDates.IsHalloweenEvent(), datedOut = TemporaryEventDates.IsHalloweenEvent(DateTime.Now);
                EdRun.Season(null);
                bool gateReal = TemporaryEventDates.IsHalloweenEvent();
                Test.Check(gateIn && datedIn && !gateOut && !datedOut, $"with the mod off the game is told what its own dates were made to say: Halloween ({gateIn}, {datedIn}), not Halloween ({gateOut}, {datedOut})");
                Test.Check(gateReal == EdRun.RealSeason, $"and left alone again, what the calendar says ({(gateReal ? "Halloween" : "not Halloween")} on {DateTime.Now:yyyy-MM-dd})");
                yield return null;
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // ------------------------------------------------------------------ the game's things the table asks

        // The decorations' condition as the game holds it (the asset RuinedTemple's content list names).
        private static HalloweenConditionalLoadContentPart Condition()
        {
            foreach (HalloweenConditionalLoadContentPart p in Resources.FindObjectsOfTypeAll<HalloweenConditionalLoadContentPart>())
            {
                if (p != null) return p;
            }
            return null;
        }

        // The game's head table: (the head a zombie has, the head shown at Halloween).
        internal static List<int[]> HeadPairs()
        {
            var pairs = new List<int[]>();
            HalloweenHeadSkinOverride table = LazySingletonSO<HalloweenHeadSkinOverride>.Instance;
            if (table == null) return pairs;
            FieldInfo list = AccessTools.Field(typeof(HalloweenHeadSkinOverride), "headReplacements");
            if (list != null && list.GetValue(table) is List<HalloweenHeadSkinOverride.HeadReplacement> rows)
            {
                foreach (HalloweenHeadSkinOverride.HeadReplacement r in rows)
                {
                    if (r.sourceHeadId != r.displayHeadId) pairs.Add(new[] { r.sourceHeadId, r.displayHeadId });
                }
            }
            return pairs;
        }

        private sealed class Tally
        {
            internal readonly string What;
            internal int Asked, Wrong;
            internal readonly List<string> First = new List<string>();

            internal Tally(string what) { What = what; }

            internal void Take(bool ok, string at, string got)
            {
                Asked++;
                if (ok) return;
                Wrong++;
                if (First.Count < 3) First.Add(at + " " + got);
            }

            internal void Say() => Test.Check(Wrong == 0 && Asked > 0, $"{What}: {Asked - Wrong} of {Asked} as the rules say{EdWorld.Some(First)}");
        }

        internal static IEnumerator Table()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            HalloweenConditionalLoadContentPart condition = Condition();
            List<int[]> heads = HeadPairs();
            GameLogicDef startRow = EdWorld.GhostRow("halloween_npc_ghost_start_action"), homeRow = EdWorld.GhostRow("halloween_npc_ghost_go_home");
            Test.Check(condition != null, "the game holds the decorations' condition (HalloweenConditionalLoad), asked as the game asks it at a load");
            Test.Check(heads.Count > 0, $"the game's head table has {heads.Count} swap(s){(heads.Count > 0 ? $": head {heads[0][0]} shown as {heads[0][1]}" : "")}");
            Test.Check(startRow != null && homeRow != null && startRow.condition != null && homeRow.condition != null,
                "the game's balance has the ghost's two rows (halloween_npc_ghost_start_action, halloween_npc_ghost_go_home), each with a condition");
            if (condition == null) yield break;
            bool rows = startRow != null && homeRow != null && startRow.condition != null && homeRow.condition != null;
            int head = heads.Count > 0 ? heads[0][0] : 1051, shown = heads.Count > 0 ? heads[0][1] : 1051;

            DateTime now = DateTime.Now;
            string Md(DateTime d) => $"{d.Month:D2}-{d.Day:D2}";
            // own dates: none; a window around today; one well away from today; only the first day; a first day that isn't a date
            var dates = new (string Name, string Start, string End, bool Valid, bool In)[]
            {
                ("none", "", "", false, false),
                ("around today", Md(now.AddDays(-1)), Md(now.AddDays(1)), true, true),
                ("away from today", Md(now.AddDays(10)), Md(now.AddDays(20)), true, false),
                ("first day only", Md(now.AddDays(-1)), "", false, false),
                ("not a date", "13-45", Md(now.AddDays(1)), false, false)
            };
            ED.EventMode[] modes = { ED.EventMode.Auto, ED.EventMode.On, ED.EventMode.Off };
            ED.PieceMode[] pieces = { ED.PieceMode.Event, ED.PieceMode.On, ED.PieceMode.Off };
            bool?[] seasons = EdRun.ForceWorks ? new bool?[] { null, true, false } : new bool?[] { null };

            var gate = new Tally("the gate the ghost's scripts ask (IsHalloweenEvent)");
            var gateDated = new Tally("the gate asked with a date");
            var load = new Tally("the decorations' condition at a load (ShouldLoad)");
            var swap = new Tally($"the zombie head ({head} shown as {shown} or left)");
            var icon = new Tally("the lying zombie's picture (ResolveDropIconId)");
            var balance = new Tally("the ghost's two rows in the balance (the game's expression IsHalloweenEvent())");
            var closed = new Tally("the way past the mod's patches closed again after each");
            var today = new Tally("the options window's \"On today\" line");
            int combinations = 0;

            // the mod's ear for a changed setting off: thousands of changes, and only the answers are asked (the
            // decorations going up and down with the settings have tests of their own)
            EdRun.Detach();
            try
            {
                using (EdRun.Expect("dates are month-day"))
                {
                    foreach (bool? forced in seasons)
                    foreach (bool en in new[] { true, false })
                    foreach (ED.EventMode m in modes)
                    foreach (var d in dates)
                    {
                        EdRun.Season(forced);
                        bool game = EdRun.SeasonNow;
                        ED.Plugin.Enabled.Value = en;
                        ED.Events.Halloween.Mode.Value = m;
                        ED.Events.Halloween.Start.Value = d.Start;
                        ED.Events.Halloween.End.Value = d.End;
                        foreach (ED.PieceMode dv in pieces)
                        foreach (ED.PieceMode gv in pieces)
                        foreach (ED.PieceMode hv in pieces)
                        {
                            combinations++;
                            ED.Halloween.Decorations.Value = dv;
                            ED.Halloween.Ghost.Value = gv;
                            ED.Halloween.ZombieHead.Value = hv;
                            string at = $"[the game's dates {(forced == null ? "as today" : forced.Value ? "made Halloween" : "made not Halloween")}, mod {(en ? "on" : "off")}, Halloween {m}, own dates {d.Name}, decorations {dv}, ghost {gv}, head {hv}]";

                            // the rules
                            bool evToday = !en ? game : m == ED.EventMode.On ? true : m == ED.EventMode.Off ? false : d.Valid ? d.In : game;
                            bool Piece(ED.PieceMode v) => !en ? game : m == ED.EventMode.Off ? false : v == ED.PieceMode.On ? true : v == ED.PieceMode.Off ? false : evToday;

                            // what the game gets
                            bool g = TemporaryEventDates.IsHalloweenEvent();
                            gate.Take(g == Piece(gv), at, $"told {g}");
                            closed.Take(ED.Events.Bypass == 0, at, $"after the gate: {ED.Events.Bypass}");
                            bool gd = TemporaryEventDates.IsHalloweenEvent(DateTime.Now);
                            gateDated.Take(gd == Piece(gv), at, $"told {gd}");
                            bool l = condition.ShouldLoad();
                            load.Take(l == Piece(dv), at, $"told {l}");
                            closed.Take(ED.Events.Bypass == 0, at, $"after ShouldLoad: {ED.Events.Bypass}");
                            int r = HalloweenHeadSkinOverride.Resolve(head);
                            swap.Take(r == (Piece(hv) ? shown : head), at, $"shown as {r}");
                            closed.Take(ED.Events.Bypass == 0, at, $"after Resolve: {ED.Events.Bypass}");
                            string i = HalloweenHeadSkinOverride.ResolveDropIconId(head.ToString("D4"));
                            icon.Take(i == (Piece(hv) ? shown : head).ToString("D4"), at, $"\"{i}\"");
                            if (rows)
                            {
                                bool s = startRow.condition.EvaluateBool(), h = homeRow.condition.EvaluateBool();
                                balance.Take(s == Piece(gv) && h == Piece(gv), at, $"start {s}, home {h}");
                            }

                            // what the options window says
                            string line = ED.OptionsPage.Today();
                            bool decoAlways = en && Piece(dv) && dv == ED.PieceMode.On && !evToday;
                            bool datesNote = en && m == ED.EventMode.Auto && (d.Name == "first day only" || d.Name == "not a date");
                            bool lineOk = line.Contains("decorations") == Piece(dv) && line.Contains("ghost") == Piece(gv) && line.Contains("zombie head") == Piece(hv) &&
                                          line.Contains("decorations (set to Always)") == decoAlways && line.Contains("don't count") == datesNote &&
                                          line.EndsWith("(the game's own dates)", StringComparison.Ordinal) == !en;
                            today.Take(lineOk, at, $"\"{line}\"");
                        }
                        // 27 combinations a frame; and no frame passes with the ghost's scripts hearing Halloween (the
                        // game's own evening row could start its walk): the ghost is on Never here, so the mod on
                        ED.Plugin.Enabled.Value = true;
                        yield return null;
                        if (Test.Aborted) yield break;
                    }
                }
            }
            finally
            {
                // while the ear is still off: back to where every test starts, then the mod told once
                EdRun.Season(null);
                ED.Halloween.Decorations.Value = ED.PieceMode.Off;
                ED.Halloween.Ghost.Value = ED.PieceMode.Off;
                ED.Halloween.ZombieHead.Value = ED.PieceMode.Event;
                ED.Plugin.Enabled.Value = true;
                ED.Events.Halloween.Mode.Value = ED.EventMode.Auto;
                ED.Events.Halloween.Start.Value = "";
                ED.Events.Halloween.End.Value = "";
                EdRun.Attach();
                ED.Decorations.OnSettingChanged(null, EventArgs.Empty);
            }
            Test.Info($"{combinations} combinations of the settings{(EdRun.ForceWorks ? ", each with the game's dates as today, made Halloween and made not Halloween" : ", with the game's dates as today only")}");
            gate.Say();
            gateDated.Say();
            load.Say();
            swap.Say();
            icon.Say();
            if (rows) balance.Say();
            closed.Say();
            today.Say();
        }

        // The two leaks of 2026-11-15, by name. The game's own ShouldLoad and Resolve only ask the gate, and the mod
        // answers the gate with the Ghost setting: a ghost on Always put the decorations up at a load and swapped the
        // zombie head out of season. And the other way round: no part's own Always starts the ghost.
        internal static IEnumerator Leaks()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            try
            {
                if (!EdRun.OutOfSeason())
                {
                    Test.Skip("the game's dates say Halloween today and can't be made to say otherwise: an out-of-season leak can't show");
                    yield break;
                }
                HalloweenConditionalLoadContentPart condition = Condition();
                List<int[]> heads = HeadPairs();
                int head = heads.Count > 0 ? heads[0][0] : 1051;
                GameLogicDef startRow = EdWorld.GhostRow("halloween_npc_ghost_start_action");

                // out of season, the ghost on Always, the rest following Halloween
                // (no frame passes while the ghost's scripts hear Halloween: the game's own evening row could start its walk)
                ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                ED.Halloween.Ghost.Value = ED.PieceMode.On;
                Test.Check(TemporaryEventDates.IsHalloweenEvent(), "out of season with the ghost on Always: the ghost's scripts are told it is Halloween");
                Test.Check(condition != null && !condition.ShouldLoad() && !EdWorld.Up, $"the decorations stay down: not asked for at a load ({condition != null && condition.ShouldLoad()}), not up in the world ({EdWorld.Up})");
                Test.Check(HalloweenHeadSkinOverride.Resolve(head) == head && !HalloweenHeadSkinOverride.IsReplaced(head), $"and zombie head {head} is shown as itself ({HalloweenHeadSkinOverride.Resolve(head)})");

                // the decorations on Always, the ghost following Halloween
                ED.Halloween.Ghost.Value = ED.PieceMode.Event;
                ED.Halloween.Decorations.Value = ED.PieceMode.On;
                yield return Test.Frames(2);
                bool row = startRow != null && startRow.condition != null && startRow.condition.EvaluateBool();
                Test.Check(EdWorld.Up && !TemporaryEventDates.IsHalloweenEvent() && !row, $"out of season with the decorations on Always: they are up ({EdWorld.Up}), and the ghost's scripts are told it isn't Halloween (gate {TemporaryEventDates.IsHalloweenEvent()}, its evening row {row})");
                Test.Check(HalloweenHeadSkinOverride.Resolve(head) == head, $"zombie head {head} is shown as itself");

                // the head on Always alone
                ED.Halloween.Decorations.Value = ED.PieceMode.Off;
                ED.Halloween.ZombieHead.Value = ED.PieceMode.On;
                yield return Test.Frames(2);
                Test.Check((HalloweenHeadSkinOverride.Resolve(head) != head) == (heads.Count > 0) && !TemporaryEventDates.IsHalloweenEvent() && !EdWorld.Up && condition != null && !condition.ShouldLoad(),
                    $"out of season with the zombie head on Always: head {head} is shown as {HalloweenHeadSkinOverride.Resolve(head)}, the ghost's scripts hear no Halloween, the decorations stay down");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // The zombie head by itself: the swap is the game's own table's, every row of it, and nothing else is touched.
        internal static IEnumerator Head()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            try
            {
                List<int[]> heads = HeadPairs();
                if (heads.Count == 0)
                {
                    Test.Warn("the game's head table has no swap to test with (or its list couldn't be read)");
                    yield break;
                }
                ED.Halloween.ZombieHead.Value = ED.PieceMode.On;
                var wrong = new List<string>();
                foreach (int[] p in heads)
                {
                    if (HalloweenHeadSkinOverride.Resolve(p[0]) != p[1] || !HalloweenHeadSkinOverride.IsReplaced(p[0]) || HalloweenHeadSkinOverride.ResolveDropIconId(p[0].ToString("D4")) != p[1].ToString("D4"))
                        wrong.Add($"{p[0]} shown as {HalloweenHeadSkinOverride.Resolve(p[0])}, not {p[1]}");
                }
                Test.Check(wrong.Count == 0, $"on Always: {heads.Count - wrong.Count} of the table's {heads.Count} head(s) shown as its Halloween head, its lying zombie's picture too{EdWorld.Some(wrong)}");
                const int other = 987654;
                Test.Check(HalloweenHeadSkinOverride.Resolve(other) == other && !HalloweenHeadSkinOverride.IsReplaced(other) && HalloweenHeadSkinOverride.ResolveDropIconId("abc") == "abc" && HalloweenHeadSkinOverride.ResolveDropIconId("") == "",
                    "a head the table doesn't name is shown as itself, and a picture name that isn't a head is left as it is");
                yield return null;

                ED.Halloween.ZombieHead.Value = ED.PieceMode.Off;
                EdRun.Season(true);
                Test.Check(HalloweenHeadSkinOverride.Resolve(heads[0][0]) == heads[0][0], $"on Never: head {heads[0][0]} is shown as itself{(EdRun.SeasonNow ? ", with the game's dates saying Halloween" : "")}");
                ED.Halloween.ZombieHead.Value = ED.PieceMode.On;
                ED.Events.Halloween.Mode.Value = ED.EventMode.Off;
                Test.Check(HalloweenHeadSkinOverride.Resolve(heads[0][0]) == heads[0][0], "on Always but Halloween on Never: shown as itself (Never is the whole event)");
                ED.Events.Halloween.Mode.Value = ED.EventMode.Auto;
                ED.Halloween.ZombieHead.Value = ED.PieceMode.Event;
                EdRun.Season(true);
                bool said1 = EdRun.SeasonNow, swapped1 = HalloweenHeadSkinOverride.Resolve(heads[0][0]) == heads[0][1];
                EdRun.Season(false);
                bool said2 = EdRun.SeasonNow, swapped2 = HalloweenHeadSkinOverride.Resolve(heads[0][0]) == heads[0][1];
                Test.Check(swapped1 == said1 && swapped2 == said2,
                    $"with Halloween: swapped when the game's dates say Halloween (they say {said1}: swapped {swapped1}; they say {said2}: swapped {swapped2})");
            }
            finally
            {
                EdRun.Arrange();
            }
        }
    }
}

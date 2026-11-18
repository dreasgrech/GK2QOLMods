extern alias EDasm;
using System;
using System.Collections;
using GK2ModTestBench;
using ED = EDasm::GK2EventDecorations;

namespace GK2QOLMods.Tests
{
    // Halloween's ghost. Its script (the game's, VisualScripts/WGODataScripts/npc_ghost) has three events:
    //   start_action  the evening's start: the ghost is put at the village's edge, then roll_action
    //   roll_action   a step: "is it Halloween?" yes: walk to a random village point (and roll_action again at its
    //                 end); no: straight back to its home point
    //   go_home       the morning: walk to the village's edge, then home
    // The mod answers the script's question with the Ghost setting, and sends home a ghost left standing in the
    // village while Halloween is off (the game loses the morning walk's last step in a save made during it). The
    // tests tell the ghost its events as the game's own evening row does (WgoData.FireEvent), never move it by hand.
    // Each makes its own start: a ghost that is out on its walk when the test begins is taken home first, and the
    // run's last check sets it on its evening again if its Halloween is on.
    internal static class EdGhostTests
    {
        private const string Start = "start_action", Step = "roll_action";

        // The ghost with its script running; null after saying why there is none to test with (a save without one:
        // nothing a test could make).
        private static EdWorld.Ghost Ghost()
        {
            EdWorld.Ghost g = EdWorld.FindGhost();
            if (g == null)
            {
                Test.Skip("this save has no ghost (npc_ghost) or no home point for it (RT_spot)");
                return null;
            }
            if (!g.HasScript)
            {
                Test.Warn("the ghost's script isn't running in this game (the game's own list of running scripts doesn't have it): it can't be told anything; not tested");
                return null;
            }
            return g;
        }

        // A ghost test brought the ghost home from a walk it was on when the run began (the last check of the run sets
        // it on its evening again when its Halloween is on).
        internal static bool TookHome;
        private static bool home;

        // Where every ghost test starts, whatever the hour and whatever the player's settings had the ghost doing: at
        // home and still. A ghost that is out is taken home by its own script's step with Halloween off (EdRun.Arrange
        // has set that); `home` says whether it is there.
        private static IEnumerator Home(EdWorld.Ghost g)
        {
            home = g.AtHome && !g.Busy;
            if (home) yield break;
            string was = $"{g.FromHome:0.0} from its home point, {(g.Busy ? "walking" : "standing")}";
            TookHome = true;
            SendHome(g);
            Wait there = Test.Until(() => g.AtHome && !g.Busy, 3f);
            yield return there;
            home = there.Met;
            if (home) Test.Info($"the ghost was out ({was}): taken home for the test");
            else Test.Warn($"the ghost was out ({was}) and its own step home didn't take it there ({g.FromHome:0.0} from its home point): not tested");
        }

        // Whatever a test did: the ghost home again, by its own script's step with Halloween off.
        internal static void SendHome(EdWorld.Ghost g)
        {
            if (g == null || g.Data == null || !g.HasScript || (g.AtHome && !g.Busy)) return;
            ED.Plugin.Enabled.Value = true;
            ED.Halloween.Ghost.Value = ED.PieceMode.Off;
            if (g.Busy) g.Data.MovementComponent.ForceStop();
            g.Data.FireEvent(Step);
        }

        // What the ghost's scripts are told, by the Ghost setting: the gate itself (the script's own question), and
        // the two rows of the balance behind the game's expression.
        internal static IEnumerator Told()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            try
            {
                GameLogicDef evening = EdWorld.GhostRow("halloween_npc_ghost_start_action"), morning = EdWorld.GhostRow("halloween_npc_ghost_go_home");
                if (evening == null || morning == null || evening.condition == null || morning.condition == null)
                {
                    Test.Fail("the game's balance has no halloween_npc_ghost_start_action or halloween_npc_ghost_go_home row with a condition any more: how the ghost starts has changed");
                    yield break;
                }
                bool Heard() => TemporaryEventDates.IsHalloweenEvent();
                bool Rows(bool yes) => evening.condition.EvaluateBool() == yes && morning.condition.EvaluateBool() == yes;
                // no frame passes while the answer is yes: the game's own evening row can't start the ghost under the test
                EdRun.Season(false);
                ED.Halloween.Ghost.Value = ED.PieceMode.On;
                bool always = Heard() && Rows(true);
                ED.Events.Halloween.Mode.Value = ED.EventMode.Off;
                bool eventNever = !Heard() && Rows(false);
                ED.Events.Halloween.Mode.Value = ED.EventMode.Auto;
                ED.Halloween.Ghost.Value = ED.PieceMode.Off;
                EdRun.Season(true);
                bool never = !Heard() && Rows(false);
                ED.Halloween.Ghost.Value = ED.PieceMode.Event;
                bool said1 = EdRun.SeasonNow, heard1 = Heard(), rows1 = Rows(said1);
                EdRun.Season(false);
                bool said2 = EdRun.SeasonNow, heard2 = Heard(), rows2 = Rows(said2);
                ED.Events.Halloween.Mode.Value = ED.EventMode.On;
                bool eventAlways = Heard() && Rows(true);
                ED.Events.Halloween.Mode.Value = ED.EventMode.Auto;
                ED.Halloween.Ghost.Value = ED.PieceMode.Off;
                Test.Check(always, $"the ghost on Always{(EdRun.SeasonNow ? "" : ", out of season")}: its script and both its rows hear Halloween");
                Test.Check(never, $"on Never{(EdRun.ForceWorks ? ", in season" : "")}: they hear none");
                Test.Check(eventNever, "on Always but Halloween on Never: none (Never is the whole event)");
                Test.Check(heard1 == said1 && heard2 == said2 && rows1 && rows2 && eventAlways, $"with Halloween: what the event says (the game's dates say {said1}: heard {heard1}; they say {said2}: heard {heard2}; Halloween on Always: heard {eventAlways})");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // The script itself, in the running game: told to start its evening, the ghost goes out only when the mod says
        // Halloween, and its next step with Halloween off takes it home.
        internal static IEnumerator Walks()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            EdWorld.Ghost g = Ghost();
            if (g == null) yield break;
            yield return Home(g);
            if (!home) yield break;
            try
            {
                g.Data.FireEvent(Start);
                yield return Test.Frames(2);
                Test.Check(g.AtHome && !g.Moving, $"the ghost on Never: told to start its evening, it is at home ({g.FromHome:0.0} from its home point): its script's first step asks, hears no, and takes it home");

                ED.Halloween.Ghost.Value = ED.PieceMode.On;
                g.Data.FireEvent(Start);
                yield return Test.Frames(2);
                Test.Check(!g.AtHome, $"on Always: told to start, it is out in the village ({g.FromHome:0} from its home point)");
                Wait walking = Test.Until(() => g.Moving, 10f);
                yield return walking;
                if (walking.Met) Test.Pass("and walks to a point of the village");
                else Test.Info("it didn't set off within 10 s (the game's own path finding, not the mod's answer): it stands at the village's edge");

                ED.Halloween.Ghost.Value = ED.PieceMode.Off;
                if (g.Busy) g.Data.MovementComponent.ForceStop(); // the walk ended here, as at its point
                g.Data.FireEvent(Step);
                yield return Test.Frames(2);
                Test.Check(g.AtHome && !g.Moving, $"set to Never while it is out: its next step takes it home ({g.FromHome:0.0} from its home point)");
            }
            finally
            {
                SendHome(g);
                EdRun.Arrange();
            }
        }

        // The mod's own fix: a ghost standing in the village while Halloween is off is sent home (the state a save
        // made during the morning walk loads into). Made here by starting its evening and stopping its walk.
        internal static IEnumerator SentHome()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            EdWorld.Ghost g = Ghost();
            if (g == null) yield break;
            yield return Home(g);
            if (!home) yield break;
            try
            {
                EdRun.Season(false); // "the mod switched off" below then hears no Halloween from the game's dates either
                ED.Halloween.Ghost.Value = ED.PieceMode.On;
                g.Data.FireEvent(Start);
                Wait walking = Test.Until(() => g.Moving, 10f);
                yield return walking;
                if (g.Busy) g.Data.MovementComponent.ForceStop();
                yield return Test.Frames(2);
                if (g.AtHome || g.Busy)
                {
                    Test.Warn($"the ghost couldn't be left standing in the village ({g.FromHome:0.0} from home, {(g.Moving ? "walking" : "standing")}): not tested");
                    yield break;
                }
                Test.Pass($"the ghost stands in the village, {g.FromHome:0} from its home point, not walking");
                int mark = EdRun.Mark;
                const string sent = "the ghost stands in the village";

                // while Halloween is on for it, it is left alone
                ED.GhostHome.OnGameStarted();
                yield return Test.Frames(3);
                Test.Check(!g.AtHome && EdRun.Count(sent, mark) == 0, "while the ghost's Halloween is on it is left where it stands");

                // Halloween off, but the fix switched off in the cfg, or the whole mod off: left alone too
                ED.Halloween.Ghost.Value = ED.PieceMode.Off;
                ED.Plugin.SendGhostHome.Value = false;
                ED.GhostHome.OnGameStarted();
                yield return Test.Frames(3);
                Test.Check(!g.AtHome && EdRun.Count(sent, mark) == 0, "Halloween off with SendGhostHome = false in the cfg: left where it stands");
                ED.Plugin.SendGhostHome.Value = true;
                ED.Plugin.Enabled.Value = false;
                ED.GhostHome.OnGameStarted();
                yield return Test.Frames(3);
                Test.Check(!g.AtHome && EdRun.Count(sent, mark) == 0, "with the mod switched off: left where it stands, as the game leaves it");
                ED.Plugin.Enabled.Value = true;

                // the fix: at the game's start (and every ten seconds after it)
                ED.GhostHome.OnGameStarted();
                Wait home = Test.Until(() => g.AtHome, 3f);
                yield return home;
                Test.Check(home.Met && EdRun.Count(sent, mark) == 1, $"Halloween off: the mod sends it home, said once in the log ({g.FromHome:0.0} from its home point, {EdRun.Count(sent, mark)} line(s))");
                ED.GhostHome.OnGameStarted();
                yield return Test.Frames(3);
                Test.Check(g.AtHome && EdRun.Count(sent, mark) == 1, "and leaves a ghost that is at home alone");
            }
            finally
            {
                SendHome(g);
                EdRun.Arrange();
            }
        }
    }
}

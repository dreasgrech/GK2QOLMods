extern alias EDasm;
using System.Collections;
using System.Collections.Generic;
using GK2ModTestBench;
using LazyBearTechnology;
using ED = EDasm::GK2EventDecorations;
using UI = EDasm::GK2DreasUI;

namespace GK2QOLMods.Tests
{
    // What the run found, read in the suite's BeforeRun, and the two closing checks: nothing went wrong in the mod but
    // what the tests provoked, and the player's settings, the world and the game are as the run found them.
    internal static class EdClosingTests
    {
        private static bool foundUp, foundPaused, foundGhost, foundGhostAtHome;
        private static string foundOff, foundLanguage;

        internal static void Remember()
        {
            foundUp = EdWorld.Up;
            foundOff = string.Join("|", EdWorld.Off(EdWorld.Look()));
            foundPaused = MainGame.IsGamePaused;
            foundLanguage = UI.Texts.LanguageOverride;
            EdWorld.Ghost g = EdWorld.FindGhost();
            foundGhost = g != null;
            foundGhostAtHome = g != null && g.AtHome && !g.Busy;
            EdGhostTests.TookHome = false;
        }

        internal static IEnumerator NoFaults()
        {
            if (!EdRun.Ready()) yield break;
            Test.Check(EdRun.Unexpected.Count == 0, $"{EdRun.Unexpected.Count} warning(s) or error(s) of the mod's during the run that no test provoked{EdWorld.Some(EdRun.Unexpected, 5)}");
            Test.Info($"{EdRun.ExpectedSeen} warning(s) the tests provoked on purpose (a date that isn't one, decorations changed behind the mod's back)");
            Test.Check(ED.Events.Bypass == 0, $"the way past the mod's patches is closed ({ED.Events.Bypass})");
            yield return null;
        }

        // The very last: everything put back by hand here (the holds would do it too, after this), then looked at.
        internal static IEnumerator AsFound()
        {
            if (!EdRun.Ready()) yield break;
            if (UI.OptionsWindow.IsOpen) UI.OptionsWindow.TestClose(back: true);
            if (UI.Texts.LanguageOverride != foundLanguage)
            {
                UI.Texts.LanguageOverride = foundLanguage;
                UI.Texts.Forget();
            }
            EdRun.Attach();
            EdRun.Season(null);
            EdWorld.Ghost g = EdWorld.FindGhost();
            bool ghostWasOut = foundGhostAtHome && g != null && (!g.AtHome || g.Busy);
            if (ghostWasOut) EdGhostTests.SendHome(g); // before the settings: its step home asks the mod, set to "no Halloween" for it
            yield return Test.Frames(2);
            int putBack = EdRun.PutBack();
            ED.Decorations.OnSettingChanged(null, System.EventArgs.Empty);
            yield return Test.Frames(3);

            List<string> changed = EdRun.Changed();
            Test.Check(changed.Count == 0, $"every setting is what the player had ({putBack} put back after the last test){EdWorld.Some(changed)}");
            Test.Check(!EdRun.FileWritten(), "the cfg file wasn't written during the run: a run that dies half-way leaves the player's file as it was");
            bool up = EdWorld.Up;
            string off = string.Join("|", EdWorld.Off(EdWorld.Look()));
            Test.Check(up == foundUp && EdWorld.Copies() == (up ? 1 : 0), $"the decorations are {(up ? "up" : "down")} as the run found them ({(foundUp ? "up" : "down")}), {EdWorld.Copies()} set(s) in the world");
            Test.Check(off == foundOff, $"and what of them is switched off is what was{(off == foundOff ? "" : $": now {off}, before {foundOff}")}");
            if (!foundGhost) Test.Info("this save has no ghost to look after");
            else if (!foundGhostAtHome)
            {
                // out when the run began. Left alone unless a ghost test took it home; then, with the player's settings
                // back: its Halloween on, its evening is started again (its script puts it at the village's edge and
                // it walks); off, home is where its next step, or the mod, would have taken it
                if (!EdGhostTests.TookHome || g == null) Test.Info("the ghost was out when the run began: left to its walk");
                else if (TemporaryEventDates.IsHalloweenEvent())
                {
                    g.Data.FireEvent("start_action");
                    yield return Test.Frames(2);
                    Test.Check(!g.AtHome, $"the ghost was out on its walk when the run began and a test took it home: its evening is started again ({g.FromHome:0} from its home point)");
                }
                else Test.Info("the ghost was out when the run began though its Halloween is off, and a test took it home: left there, where its own next step would have taken it");
            }
            else
            {
                if (ghostWasOut) Test.Warn("the ghost was out at the end of the run though it began at home (the game's own evening row may have started it while a test had its Halloween on): sent home");
                Test.Check(g != null && g.AtHome && !g.Moving, $"the ghost is at home and still, as the run found it ({(g != null ? g.FromHome.ToString("0.0") : "?")} from its home point)");
            }
            Test.Check(!UI.OptionsWindow.IsOpen && LazyWindowsStackController.ActiveWindow == null && MainGame.IsGamePaused == foundPaused,
                $"no window is left open and the game {(foundPaused ? "is paused as it was" : "runs")}");
            Test.Check(ED.Events.Bypass == 0 && !EdRun.Detached && ED.Events.Halloween != null && ED.Plugin.Status == "Active", "the mod is as it was: running, listening to its settings, its patches answering");
        }
    }
}

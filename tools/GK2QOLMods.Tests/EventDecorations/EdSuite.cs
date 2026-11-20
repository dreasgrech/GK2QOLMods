extern alias EDasm;
using System.Collections;
using BepInEx.Bootstrap;
using GK2ModTestBench;
using ED = EDasm::GK2EventDecorations;
using UI = EDasm::GK2DreasUI;

namespace GK2QOLMods.Tests
{
    // GK2 Event Decorations in the game: the suite, its holds and its tests in run order. The tests are in the files
    // beside this one, a group each; EdRun is what a run holds, EdWorld how the world is read.
    //
    // The first two groups only look (the mod as it started, the player's own settings as found). Every test after
    // them changes settings, each starting from the same known ones (EdRun.Arrange) and leaving those behind, so any
    // one of them, or any group, runs alone; the player's own settings come back at the end of the run, also when it
    // is ended from outside (the holds). Nothing is saved: the cfg file is not written during a run, and the
    // decorations are scenery that no save holds.
    //
    // What these tests don't reach is listed in docs/event-decorations-tests.md with the reason for each.
    internal static class EventDecorationsTests
    {
        internal const string ModGuid = EdRun.ModGuid;

        private const string Mod = "The mod", Found = "As the player has it", Dates = "The player's own dates", Told = "What the game is told",
            World = "The decorations in the world", Ghost = "The ghost", Window = "The options window", Languages = "Languages", Others = "Other mods and later games";

        // Nothing of the mod's own types is touched here: the suite is added whether the mod is installed or not, and
        // a method that names the mod's types is only compiled when it first runs.
        internal static void Register()
        {
            TestSuite s = TestBench.Suite(ModGuid, "GK2 Event Decorations");
            s.CanStart = () => Chainloader.PluginInfos.ContainsKey(ModGuid) ? null : "GK2 Event Decorations isn't installed";

            // Ended in reverse: the window closed, the language back, the season the calendar's again, then the
            // settings (and with them the world), the log's listener last.
            s.AddHold("the mod's log, listened to", EdRun.BeginListen, EdRun.EndListen);
            s.AddHold("the player's settings", EdRun.HoldSettings, EdRun.GiveSettingsBack);
            s.AddHold("the game's own dates, which the tests can turn", EdRun.BeginSeason, EdRun.EndSeason);
            s.AddHold("the language of the mod's windows", EdHolds.KeepLanguage, EdHolds.LanguageBack);
            s.AddHold("the options window, closed again", EdHolds.Nothing, EdHolds.CloseWindow);
            s.BeforeRun = run => EdHolds.Before();
            s.AfterEach = test => EdHolds.Between();

            s.Add(Mod, "the mod is running and knows the game's events", EdStartTests.Running);
            s.Add(Mod, "every game method the mod changes carries its patch", EdStartTests.Patched);
            s.Add(Mod, "every setting is in the cfg, and has a row in the options window or is kept to the cfg on purpose", EdStartTests.Settings);
            s.Add(Mod, "nothing of the mod's at warning level or above in this session's log", EdStartTests.SessionLog);

            s.Add(Found, "the game's answer to \"is it Halloween\" is what the settings say", EdStartTests.Gate);
            s.Add(Found, "the game's own dates can still be asked past the mod, and the way past closes again", EdStartTests.PastTheMod);
            s.Add(Found, "the decorations are up or down in the world as the settings say", EdStartTests.InTheWorld);
            s.Add(Found, "the mod's areas and kinds are the ones the game's decorations were audited for", EdStartTests.AreasAndKinds);

            s.Add(Dates, "the player's own dates decide while Halloween is By date, and only then", EdDateTests.Count);
            s.Add(Dates, "the first and the last day count", EdDateTests.Ends);
            s.Add(Dates, "dates may run past the new year", EdDateTests.NewYear);
            s.Add(Dates, "what is read as a date and what isn't; one date alone", EdDateTests.Forms);
            s.Add(Dates, "a settings file of 1.0.0 keeps its days: its month-day dates turned to day-month once (scratch files)", EdDateTests.Upgrade);

            s.Add(Told, "the tests can make the game's own dates say Halloween or not", EdAnswerTests.Force);
            s.Add(Told, "every combination of the settings: the gate, the decorations at a load, the zombie head, the ghost's rows, \"On today\"", EdAnswerTests.Table);
            s.Add(Told, "no part's setting reaches another: each of the three on Never while the other two show", EdAnswerTests.Leaks);
            s.Add(Told, "the zombie head is swapped by the game's own table, and only by it", EdAnswerTests.Head);

            s.Add(World, "put up and taken down at once by the setting, nothing of them in the save", EdWorldTests.UpAndDown);
            s.Add(World, "with every switch on the mod leaves them as the game made them", EdWorldTests.AsTheGameMakesThem);
            s.Add(World, "the game's decorations are laid out as the switches expect: every group an area, every item a kind", EdWorldTests.Layout);
            s.Add(World, "each area's switch hides its own group and nothing else", EdWorldTests.Areas);
            s.Add(World, "each kind's switch hides its own items in every area and nothing else; the bats", EdWorldTests.Kinds);
            s.Add(World, "an area and a kind switched off together, and put back one by one", EdWorldTests.AreaAndKind);
            s.Add(World, "the glow switch puts the lights out and leaves the decorations", EdWorldTests.Glow);
            s.Add(World, "\"block the way\" off lets the keeper through and leaves the bats' zones working", EdWorldTests.BlockTheWay);
            s.Add(World, "with the mod switched off the game decides, and what it puts up is as it made it", EdWorldTests.ModOff);
            s.Add(World, "what decides whether they are up: Never, Always, the player's dates, the game's dates", EdWorldTests.WhatDecides);
            s.Add(World, "at a load the game asks the mod, and the areas are switched before the world is built", EdWorldTests.AtALoad);
            s.Add(World, "after a load, decorations that don't match the settings are said and put right", EdWorldTests.AfterALoad);
            s.Add(World, "up and down many times over leaves nothing behind", EdWorldTests.ManyTimes);
            s.Add(World, "what was switched stays so while the game runs on (seven seconds)", EdWorldTests.Stays);

            s.Add(Ghost, "the ghost's script and its two rows in the balance hear what the Ghost setting says", EdGhostTests.Told);
            s.Add(Ghost, "told to start its evening, the ghost goes out only when the mod says Halloween, and its next step without takes it home", EdGhostTests.Walks);
            s.Add(Ghost, "a ghost left standing in the village while Halloween is off is sent home, and only then", EdGhostTests.SentHome);

            s.Add(Window, "the page: its tabs, its rows in order, its choices, its two lines", EdWindowTests.ThePage);
            s.Add(Window, "which rows are greyed when", EdWindowTests.Greyed);
            s.Add(Window, "the window opens over the game with both tabs drawn, and closes into the game", EdWindowTests.Opens);
            s.Add(Window, "a switch pressed in the window changes the world under it at once", EdWindowTests.Pressed);
            s.Add(Window, "Defaults sets the Halloween tab back, after asking", EdWindowTests.Defaults);
            s.Add(Window, "\"On today\" and Status say what is so", EdWindowTests.Lines);
            s.Add(Window, "the date boxes take digits and the dash, and refuse what is no day of the year", EdWindowTests.DateBoxes);

            s.Add(Languages, "every shipped language is found and read without a complaint, with a current line for every word", EdLanguageTests.Files);
            s.Add(Languages, "in every language each row has its text and the sentences are translated", EdLanguageTests.Words);
            s.Add(Languages, "in the game's own language every character of the window has a glyph", EdLanguageTests.Glyphs);

            s.Add(Others, "with GK2 Mod Framework: its Settings button opens the mod's own window", EdOtherTests.ModsMenu);
            s.Add(Others, "an event a later game adds is left to the game: named, not switched (a made-up one)", EdOtherTests.LaterEvent);

            s.AddLast("nothing went wrong in the mod during the run but what the tests provoked", EdClosingTests.NoFaults);
            s.AddVeryLast("the player's settings, the decorations, the ghost and the game are as the run found them", EdClosingTests.AsFound);
        }
    }

    // The suite's hooks and the two holds that aren't EdRun's.
    internal static class EdHolds
    {
        private static string languageWas;
        private static bool languageKept;

        internal static void Nothing()
        {
        }

        internal static void KeepLanguage()
        {
            languageWas = UI.Texts.LanguageOverride;
            languageKept = true;
        }

        // The language the run found the mod's windows in (null: the game's own), for the test that needs that one.
        internal static void GameLanguage()
        {
            if (!languageKept || UI.Texts.LanguageOverride == languageWas) return;
            UI.Texts.LanguageOverride = languageWas;
            UI.Texts.Forget();
        }

        internal static void LanguageBack()
        {
            if (!languageKept) return;
            languageKept = false;
            if (UI.Texts.LanguageOverride == languageWas) return;
            UI.Texts.LanguageOverride = languageWas;
            UI.Texts.Forget();
        }

        internal static void CloseWindow()
        {
            if (UI.OptionsWindow.IsOpen) UI.OptionsWindow.TestClose(back: true);
        }

        // Before the suite's first test: what the game's dates say and whether they can be turned, and what the run
        // finds (for the last check). A mod that didn't start is the first test's failure, and every other test's skip.
        internal static IEnumerator Before()
        {
            if (ED.Plugin.Status != "Active") yield break;
            EdRun.ProbeSeason();
            EdClosingTests.Remember();
            yield return null;
        }

        // Between tests, also after one that threw: no expectation, no window and no deaf mod carried into the next.
        internal static IEnumerator Between()
        {
            EdRun.ForgetExpectations();
            EdRun.Attach();
            CloseWindow();
            yield break;
        }
    }
}

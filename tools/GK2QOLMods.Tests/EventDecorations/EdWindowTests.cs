extern alias EDasm;
using System;
using System.Collections;
using System.Collections.Generic;
using GK2ModTestBench;
using LazyBearTechnology;
using UnityEngine;
using ED = EDasm::GK2EventDecorations;
using UI = EDasm::GK2DreasUI;

namespace GK2QOLMods.Tests
{
    // The mod's options: its page of rows, which of them are greyed when, the window itself opened over the game and
    // closed again, a switch pressed in it, Defaults, and the two lines that say what the mod is doing. The window is
    // the library's (lib\GK2DreasUI, with tests of its own in GK2 Donkey API's suite); what is tested here is this
    // mod's use of it. A test can't click: a switch is pressed through its own button, Defaults and a step back
    // through the window's own entry points for them.
    internal static class EdWindowTests
    {
        private const string General = "General", Halloween = "Halloween";
        private static readonly string[] Parts = { "Decorations", "Ghost", "ZombieHead" };
        private static readonly string[] Extras = { "CandleGlow", "BlockTheWay" };

        private static string ShownName(UI.SettingsPage.Row r) => r.ShownSection ?? r.Section;

        private static UI.SettingsPage.Row Row(UI.SettingsPage page, string key) => page.RowOfKey(key == "Enabled" ? General : Halloween, key);

        // Where every window test starts: no window open. The run itself begins with the keeper free, so none is; one
        // of ours left open is closed here, and a window of the game's opened while the run goes on is waited for.
        // `Clear` says whether the screen is free.
        internal static bool Clear;

        internal static IEnumerator Free()
        {
            if (UI.OptionsWindow.IsOpen) UI.OptionsWindow.TestClose(back: true);
            Wait free = Test.Until(() => LazyWindowsStackController.ActiveWindow == null && !UI.OptionsWindow.IsOpen, 10f);
            yield return free;
            Clear = free.Met;
            if (!Clear) Test.Warn("a window of the game's was opened while the run went on and stayed open for 10 s: the options window can't open over it; not tested");
        }

        private static bool ControlsFree() => MainGame.PlayerController != null && MainGame.PlayerController.IsControlEnabledByType(TakenControlType.ByUI);

        // ------------------------------------------------------------------ the page

        internal static IEnumerator ThePage()
        {
            if (!EdRun.Ready()) yield break;
            UI.SettingsPage page = EdRun.Page();
            if (page == null)
            {
                Test.Fail("the mod made no settings page");
                yield break;
            }
            // the rows in the window's order, by this test's own list: a key, or # and a heading
            var want = new List<string> { "Enabled", "info:Status", "info:On today", "Mode", "Start", "End", "#Parts of the event" };
            want.AddRange(Parts);
            want.Add("#Areas");
            want.AddRange(EdWorld.AreaKeys);
            want.Add("#Kinds of decorations");
            want.AddRange(EdWorld.KindKeys);
            want.Add(EdWorld.BatsKey);
            want.AddRange(Extras);
            var have = new List<string>();
            var sections = new List<string>();
            var poor = new List<string>();
            foreach (UI.SettingsPage.Row r in page.Rows)
            {
                if (!page.InOptions(r)) continue;
                if (!sections.Contains(ShownName(r))) sections.Add(ShownName(r));
                have.Add(r.Entry != null ? r.Entry.Definition.Key : r.RowKind == UI.SettingsPage.RowKind.Info ? "info:" + r.Label : "#" + r.Label);
                if (string.IsNullOrEmpty(r.Label)) poor.Add("a row without a name");
                else if (r.Entry != null && (r.Description ?? "").Length < 20) poor.Add($"'{r.Label}' has no description");
            }
            Test.Check(string.Join(", ", sections) == General + ", " + Halloween, $"its tabs: {string.Join(", ", sections)}");
            Test.Check(string.Join("|", have) == string.Join("|", want), $"its {have.Count} rows are the {want.Count} expected, in order{(string.Join("|", have) == string.Join("|", want) ? "" : ": " + string.Join(", ", have))}");
            Test.Check(poor.Count == 0, $"every row has a name, every setting a description{EdWorld.Some(poor)}");
            yield return null;

            UI.SettingsPage.Row mode = Row(page, "Mode");
            var choices = new List<string>();
            if (mode != null && mode.ChoiceLabels != null) choices.Add(string.Join("/", mode.ChoiceLabels));
            foreach (string p in Parts)
            {
                UI.SettingsPage.Row r = Row(page, p);
                if (r != null && r.ChoiceLabels != null) choices.Add(string.Join("/", r.ChoiceLabels));
            }
            Test.Check(string.Join("; ", choices) == "By date/Always/Never; With Halloween/Never; With Halloween/Never; With Halloween/Never",
                $"the choices say what they do, in the cfg's order (Auto, On, Off for Halloween; Event, Off for a part, which has no Always): {string.Join("; ", choices)}");
            string status = null, today = null;
            foreach (UI.SettingsPage.Row r in page.Rows)
            {
                if (r.RowKind != UI.SettingsPage.RowKind.Info || r.InfoValue == null) continue;
                if (r.Label == "Status") status = r.InfoValue();
                if (r.Label == "On today") today = r.InfoValue();
            }
            Test.Check(status == ED.Plugin.StatusShown() && today == ED.OptionsPage.Today(), $"its two lines read: Status \"{status}\", On today \"{today}\"");
        }

        // ------------------------------------------------------------------ what is greyed when

        // The rows whose "usable" isn't what the rule gives.
        private static List<string> Wrong(UI.SettingsPage page, Func<string, bool> rule)
        {
            var wrong = new List<string>();
            foreach (UI.SettingsPage.Row r in page.Rows)
            {
                if (r.Entry == null || !page.InOptions(r)) continue;
                string key = r.Entry.Definition.Key;
                bool usable = UI.OptionsWindow.Usable(r), should = rule(key);
                if (usable != should) wrong.Add($"{key} is {(usable ? "usable" : "greyed")}");
            }
            return wrong;
        }

        private static bool IsDate(string key) => key == "Start" || key == "End";
        private static bool IsPart(string key) => Array.IndexOf(Parts, key) >= 0;
        private static bool IsDetail(string key) => !IsDate(key) && !IsPart(key) && key != "Mode" && key != "Enabled"; // the areas, the kinds, the glow, the blocking

        internal static IEnumerator Greyed()
        {
            if (!EdRun.Ready()) yield break;
            UI.SettingsPage page = EdRun.Page();
            if (page == null)
            {
                Test.Fail("the mod made no settings page");
                yield break;
            }
            EdRun.Arrange();
            try
            {
                EdRun.Season(false);
                ED.Events.Halloween.Mode.Value = ED.EventMode.Auto;
                ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                List<string> w = Wrong(page, key => true);
                Test.Check(w.Count == 0, $"Halloween by date, the parts following it: every row can be used{EdWorld.Some(w)}");
                ED.Events.Halloween.Mode.Value = ED.EventMode.On;
                w = Wrong(page, key => !IsDate(key));
                bool up = EdWorld.Up;
                Test.Check(w.Count == 0, $"Halloween on Always: only the two dates are greyed (they count By date){EdWorld.Some(w)}");
                ED.Events.Halloween.Mode.Value = ED.EventMode.Off;
                w = Wrong(page, key => key == "Mode" || key == "Enabled");
                Test.Check(w.Count == 0, $"Halloween on Never: everything of it is greyed but the choice itself{EdWorld.Some(w)}");
                yield return null;
                ED.Events.Halloween.Mode.Value = ED.EventMode.Auto;
                ED.Halloween.Decorations.Value = ED.PieceMode.Off;
                w = Wrong(page, key => !IsDetail(key));
                Test.Check(w.Count == 0, $"the decorations on Never: their areas, kinds, glow and blocking are greyed, nothing else{EdWorld.Some(w)}");
                ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                w = Wrong(page, key => true);
                Test.Check(w.Count == 0, $"the decorations with Halloween again: all usable again{EdWorld.Some(w)}");
                ED.Halloween.Decorations.Value = ED.PieceMode.Off;
                ED.Plugin.Enabled.Value = false;
                w = Wrong(page, key => key == "Enabled");
                Test.Check(w.Count == 0, $"\"Mod enabled\" off: every row is greyed but that switch{EdWorld.Some(w)}");
                ED.Plugin.Enabled.Value = true;
                Test.Info($"(with Halloween on Always the decorations following it were {(up ? "up" : "down")})");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // ------------------------------------------------------------------ the window

        internal static IEnumerator Opens()
        {
            if (!EdRun.Ready()) yield break;
            UI.SettingsPage page = EdRun.Page();
            if (page == null)
            {
                Test.Fail("the mod made no settings page");
                yield break;
            }
            yield return Free();
            if (!Clear) yield break;
            EdRun.Arrange();
            bool pausedBefore = MainGame.IsGamePaused;
            try
            {
                UI.OptionsWindow.Open(page);
                yield return Test.Frames(2);
                Test.Check(UI.OptionsWindow.IsShown && UI.GameWindowAside.ControlsTaken, "the options window opens over the game and holds the controls");
                if (!UI.OptionsWindow.IsShown) yield break;
                Test.Check(MainGame.IsGamePaused, "the game is paused under it, as under the game's own windows");
                Test.Check(UI.OptionsWindow.TestDrawn && UI.OptionsWindow.TestScale >= 0.5f, $"it is drawn and fits the screen (scale {UI.OptionsWindow.TestScale:0.##})");
                string sections = UI.OptionsWindow.TestSections();
                Test.Check(sections == General + ", " + Halloween, $"its tabs are the page's: {sections}");

                foreach (string name in new[] { General, Halloween })
                {
                    UI.OptionsWindow.TestShowSection(name);
                    yield return Test.Frames(1);
                    int shown = 0, undrawn = 0;
                    foreach (UI.SettingsPage.Row r in page.Rows)
                    {
                        if (!page.InOptions(r) || ShownName(r) != name || (r.Visible != null && !r.Visible())) continue;
                        shown++;
                        if (UI.OptionsWindow.TestRowObject(r) == null) undrawn++;
                    }
                    string outside = UI.OptionsWindow.TestOutside();
                    Test.Check(UI.OptionsWindow.TestRowCount == shown && undrawn == 0 && UI.OptionsWindow.TestGameRows() == 0 && outside == null,
                        $"{name}: its {shown} rows are drawn ({UI.OptionsWindow.TestRowCount} in the window, {undrawn} missing), inside the column ({outside ?? "nothing outside"})");
                    Test.Info($"{name}: its rows take {UI.OptionsWindow.TestRowsHeight:0} of {UI.OptionsWindow.TestRowsRoom:0} px{(UI.OptionsWindow.TestRowsHeight > UI.OptionsWindow.TestRowsRoom + 0.5f ? ": it scrolls" : "")}");
                }

                // the line at the bottom says what a row does, and why a greyed one can't be used
                UI.SettingsPage.Row village = Row(page, "Village");
                string says = UI.OptionsWindow.TestDescription(village), text = UI.Texts.Desc(village);
                Test.Check(!string.IsNullOrEmpty(text) && says.StartsWith(text, StringComparison.Ordinal) && says.Length > text.Length,
                    $"the line at the bottom for a greyed row (the village, the decorations on Never) is its description and why it can't be used: \"{says}\"");

                UI.OptionsWindow.TestStepBack();
                yield return Test.Frames(2);
                Test.Check(!UI.OptionsWindow.IsOpen && LazyWindowsStackController.ActiveWindow == null && ControlsFree(), "a step back (Escape) closes it into the game, the controls given back");
                Test.Check(MainGame.IsGamePaused == pausedBefore, $"and the game {(pausedBefore ? "is paused as before" : "runs again")}");
            }
            finally
            {
                if (UI.OptionsWindow.IsOpen) UI.OptionsWindow.TestClose(back: true);
                EdRun.Arrange();
            }
        }

        private static bool Press(UI.SettingsPage.Row row)
        {
            GameObject go = UI.OptionsWindow.TestRowObject(row);
            Transform sw = go != null ? go.transform.Find(UI.UiOwner.NamePrefix + "options switch") : null;
            UnityEngine.UI.Button button = sw != null ? sw.GetComponent<UnityEngine.UI.Button>() : null;
            if (button == null) return false;
            button.onClick.Invoke();
            return true;
        }

        internal static IEnumerator Pressed()
        {
            if (!EdRun.Ready()) yield break;
            UI.SettingsPage page = EdRun.Page();
            if (page == null)
            {
                Test.Fail("the mod made no settings page");
                yield break;
            }
            yield return Free();
            if (!Clear) yield break;
            EdRun.Arrange();
            try
            {
                ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                yield return null;
                EdWorld.Survey s = EdWorld.Look();
                Transform group = s != null ? s.Group("HalloweenVillage") : null;
                if (group == null)
                {
                    Test.Fail("the decorations didn't go up, or have no village group");
                    yield break;
                }
                UI.OptionsWindow.Open(page);
                yield return Test.Frames(2);
                if (!UI.OptionsWindow.IsShown)
                {
                    Test.Fail("the options window didn't open");
                    yield break;
                }
                UI.OptionsWindow.TestShowSection(Halloween);
                yield return Test.Frames(1);
                UI.SettingsPage.Row village = Row(page, "Village");
                bool pressed = Press(village);
                yield return Test.Frames(1);
                Test.Check(pressed && !EdRun.Switch("Village").Value && !group.gameObject.activeSelf,
                    $"the village's switch pressed in the window, the game paused under it: the setting is off ({!EdRun.Switch("Village").Value}) and the village's decorations are gone at once ({!group.gameObject.activeSelf})");
                pressed = Press(village); // the row may have been drawn again: found again by Press
                yield return Test.Frames(1);
                Test.Check(pressed && EdRun.Switch("Village").Value && group.gameObject.activeSelf, "pressed again: on, and they are back");

                // a choice changed under the open window: the rows it rules grey at once, and the world follows
                ED.Halloween.Decorations.Value = ED.PieceMode.Off;
                UI.OptionsWindow.TestRefresh();
                yield return Test.Frames(1);
                bool greyed = UI.OptionsWindow.TestRowGreyed(village), down = !EdWorld.Up;
                ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                UI.OptionsWindow.TestRefresh();
                yield return Test.Frames(1);
                Test.Check(greyed && down && !UI.OptionsWindow.TestRowGreyed(village) && EdWorld.Up,
                    $"the decorations set to Never with the window open: the village's row greys ({greyed}) and they come down ({down}); to Always: usable again and up ({EdWorld.Up})");
                ED.Events.Halloween.Mode.Value = ED.EventMode.Auto;
                UI.OptionsWindow.TestRefresh();
                yield return Test.Frames(1);
                bool usable = !UI.OptionsWindow.TestRowGreyed(Row(page, "Start")) && !UI.OptionsWindow.TestRowGreyed(Row(page, "End"));
                ED.Events.Halloween.Mode.Value = ED.EventMode.On;
                UI.OptionsWindow.TestRefresh();
                yield return Test.Frames(1);
                Test.Check(usable && UI.OptionsWindow.TestRowGreyed(Row(page, "Start")) && UI.OptionsWindow.TestRowGreyed(Row(page, "End")), $"Halloween set to By date: the two date rows can be used ({usable}); set to Always: they grey");
                UI.OptionsWindow.TestStepBack();
                yield return Test.Frames(2);
                Test.Check(!UI.OptionsWindow.IsOpen && ControlsFree(), "closed again, the controls given back");
            }
            finally
            {
                if (UI.OptionsWindow.IsOpen) UI.OptionsWindow.TestClose(back: true);
                EdRun.Arrange();
            }
        }

        internal static IEnumerator Defaults()
        {
            if (!EdRun.Ready()) yield break;
            UI.SettingsPage page = EdRun.Page();
            if (page == null)
            {
                Test.Fail("the mod made no settings page");
                yield break;
            }
            yield return Free();
            if (!Clear) yield break;
            EdRun.Arrange();
            try
            {
                EdRun.Season(false);
                // away from the defaults: a choice, a part, an area, a kind, the glow; and the cfg-only fix
                ED.Events.Halloween.Mode.Value = ED.EventMode.On;
                ED.Halloween.ZombieHead.Value = ED.PieceMode.Off;
                EdRun.Switch("Port").Value = false;
                EdRun.Switch("Garlands").Value = false;
                ED.Halloween.Glow.Value = false;
                ED.Plugin.SendGhostHome.Value = false;
                UI.OptionsWindow.Open(page);
                yield return Test.Frames(2);
                if (!UI.OptionsWindow.IsShown)
                {
                    Test.Fail("the options window didn't open");
                    yield break;
                }
                UI.OptionsWindow.TestShowSection(Halloween);
                yield return Test.Frames(1);
                UI.OptionsWindow.TestOpenConfirm();
                yield return Test.Frames(1);
                bool asked = UI.OptionsWindow.TestConfirmOpen;
                UI.OptionsWindow.TestStepBack(); // Escape: the question goes, nothing changes
                yield return Test.Frames(1);
                Test.Check(asked && !UI.OptionsWindow.TestConfirmOpen && UI.OptionsWindow.IsShown && ED.Events.Halloween.Mode.Value == ED.EventMode.On && !EdRun.Switch("Port").Value,
                    "Defaults asks first; a step back from the question keeps the tab as set and the window open");
                UI.OptionsWindow.TestOpenConfirm();
                yield return Test.Frames(1);
                UI.OptionsWindow.TestConfirmYes();
                yield return Test.Frames(1);
                var left = new List<string>();
                foreach (UI.SettingsPage.Row r in page.Rows)
                {
                    if (page.InOptions(r) && r.Entry != null && r.Section == Halloween && !Equals(r.Entry.BoxedValue, r.Entry.DefaultValue)) left.Add($"{r.Entry.Definition.Key} = {r.Entry.BoxedValue}");
                }
                Test.Check(left.Count == 0, $"Set back: every setting of the Halloween tab is its default{EdWorld.Some(left)}");
                Test.Check(!ED.Plugin.SendGhostHome.Value, "SendGhostHome, which has no row, is left as the cfg has it (false here)");
                Test.Check(EdWorld.Up == EdRun.SeasonNow, $"and the world follows the defaults: the decorations are {(EdWorld.Up ? "up" : "down")} with the game's dates saying {(EdRun.SeasonNow ? "Halloween" : "it isn't Halloween")}");
                UI.OptionsWindow.TestStepBack();
                yield return Test.Frames(2);
            }
            finally
            {
                if (UI.OptionsWindow.IsOpen) UI.OptionsWindow.TestClose(back: true);
                EdRun.Arrange();
            }
        }

        // ------------------------------------------------------------------ the two lines

        internal static IEnumerator Lines()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            try
            {
                Test.Check(ED.Plugin.StatusShown() == "Active", $"Status with the mod on: \"{ED.Plugin.StatusShown()}\"");
                if (!EdRun.OutOfSeason())
                {
                    Test.Skip("the game's dates say Halloween today and can't be made to say otherwise: the out-of-season lines can't be read");
                    yield break;
                }
                var wrong = new List<string>();
                void Reads(string want, string when)
                {
                    string line = ED.OptionsPage.Today();
                    bool world = line.Contains("decorations") == EdWorld.Up;
                    if (line != want || !world) wrong.Add($"{when}: \"{line}\"{(line != want ? $" (expected \"{want}\")" : "")}{(world ? "" : $" while the decorations are {(EdWorld.Up ? "up" : "down")}")}");
                }
                Reads("Halloween: off", "Halloween on Always, every part on Never");
                ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                Reads("Halloween: decorations", "Halloween on Always, the decorations with it");
                // the same settings as the log's start line names them: the parts that don't follow the event
                string described = ED.Events.Describe();
                ED.Halloween.ZombieHead.Value = ED.PieceMode.Event;
                Reads("Halloween: decorations, zombie head", "Halloween on Always, the ghost on Never");
                ED.Halloween.Ghost.Value = ED.PieceMode.Event; // no frame passes while the ghost's Halloween is on
                Reads("Halloween: decorations, ghost, zombie head", "Halloween on Always, every part with it");
                ED.Halloween.Ghost.Value = ED.PieceMode.Off;
                ED.Events.Halloween.Mode.Value = ED.EventMode.Off;
                Reads("Halloween: off", "Halloween on Never, the decorations and the head with it");
                ED.Events.Halloween.Mode.Value = ED.EventMode.Auto;
                ED.Halloween.Decorations.Value = ED.PieceMode.Off;
                ED.Events.Halloween.Start.Value = "10-01";
                Reads("Halloween: off (your own dates don't count: both are needed, as month-day like 10-24, so the game's dates apply)", "a first day without a last");
                ED.Events.Halloween.Start.Value = "";
                ED.Plugin.Enabled.Value = false;
                Reads("Halloween: off (the game's own dates)", "the mod switched off, out of season");
                string statusOff = ED.Plugin.StatusShown();
                if (EdRun.InSeason())
                {
                    Reads("Halloween: decorations, ghost, zombie head (the game's own dates)", "the mod switched off, in season");
                    EdRun.Season(false);
                }
                ED.Plugin.Enabled.Value = true;
                Test.Check(wrong.Count == 0, $"\"On today\" reads as it should in {(EdRun.ForceWorks ? 8 : 7) - wrong.Count} of {(EdRun.ForceWorks ? 8 : 7)} settings, and names the decorations exactly when they are up{EdWorld.Some(wrong, 8)}");
                Test.Check(statusOff == "Off: \"Mod enabled\" is unchecked", $"Status with the mod switched off: \"{statusOff}\"");
                Test.Check(described == "Halloween (On; ghost Off, zombie head Off)", $"the log's start line would name the same settings as \"{described}\" (Halloween on Always, the ghost and the zombie head on Never)");
                yield return null;
            }
            finally
            {
                EdRun.Arrange();
            }
        }
    }
}

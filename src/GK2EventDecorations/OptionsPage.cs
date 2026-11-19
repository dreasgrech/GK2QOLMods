using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using GK2Shared;

namespace GK2EventDecorations
{
    // The rows of the options window (the window code, lib\GK2DreasUI: a "GK2 Event Decorations" row in the game's
    // Settings window). Every row binds the same section, key and type as the mod's own Config.Bind (Plugin, Events,
    // Halloween), so BepInEx hands the page the entries bound there and the window edits the values the mod reads; the
    // page keeps the cfg's sections as named (numberSections: false), and a section is a tab: General, one tab per event
    // (its mode and dates, then headings over its parts). Safety.StopOnChangedGameCode has no row: on by default, the
    // cfg's (Plugin.BindConfig). A change applies at once: the decorations go up or down
    // (Decorations.OnSettingChanged, on the cfg's SettingChanged), the rest is read where the game asks. The GK2 Mod
    // Framework's Settings button opens this same window (FrameworkLink): one set of rows for every player.
    internal static class OptionsPage
    {
        // The window's words for the choices, saying what each does (2026-11-15: "On" beside "Auto" read as "enabled", and a
        // part left on On showed the decorations out of season). The cfg keeps its values; each description names both.
        private static readonly string[] ModeLabels = { "By date", "Always", "Never" };          // EventMode Auto, On, Off
        private static readonly string[] PieceLabels = { "With Halloween", "Never" };            // PieceMode Event, Off

        internal static void Declare(SettingsPage page)
        {
            // General: the one switch, then two lines saying what the mod is doing and what shows today
            page.Toggle("General", "Enabled", true, "Mod enabled", Plugin.Enabled.Description.Description);
            page.Status("General", "Status", "Whether the mod is running. After a game update that changed the game code it relies on, it says so here and stays off until an updated version.");
            page.Info("General", "On today", Today, "What of each event shows today, with the settings as they are now.");

            // The Halloween tab: its mode and own dates, then its parts, the areas and the decorations under headings.
            // Not a row: SendGhostHome (a fix for the game's lost walk home, on by default; the cfg can switch it off).
            EventRows(page, Events.Halloween, "Halloween");
            string h = Events.HalloweenName;
            page.Heading(h, "Parts of the event");
            page.Choice(h, "Decorations", PieceMode.Event, "Decorations", Halloween.Decorations.Description.Description, labels: PieceLabels);
            page.Choice(h, "Ghost", PieceMode.Event, "Ghost walking at night", Halloween.Ghost.Description.Description, labels: PieceLabels);
            page.Choice(h, "ZombieHead", PieceMode.Event, "Halloween zombie head", Halloween.ZombieHead.Description.Description, labels: PieceLabels);
            page.Heading(h, "Areas");
            for (int i = 0; i < Halloween.Areas.Count; i++)
            {
                Halloween.Area a = Halloween.Areas[i];
                page.Toggle(h, a.Key, true, a.Label, a.Entry.Description.Description);
            }
            page.Heading(h, "Kinds of decorations");
            for (int i = 0; i < Halloween.Kinds.Count; i++)
            {
                Halloween.Kind k = Halloween.Kinds[i];
                page.Toggle(h, k.Key, true, k.Label, k.Entry.Description.Description);
            }
            page.Toggle(h, "CandleGlow", true, "Candle and lantern glow", Halloween.Glow.Description.Description);
            page.Toggle(h, "BlockTheWay", true, "Decorations block the way", Halloween.BlockWay.Description.Description);

            // Halloween Off: its parts are off too, so their rows are greyed (the mode row says why); the areas and kinds
            // greyed too while the decorations can't show at all (the event Off, or the decorations piece Off).
            Func<bool> eventNotOff = () => !Halloween.EventOff();
            page.EnableWhen(h, "Decorations", eventNotOff);
            page.EnableWhen(h, "Ghost", eventNotOff);
            page.EnableWhen(h, "ZombieHead", eventNotOff);
            for (int i = 0; i < Halloween.Areas.Count; i++)
            {
                page.EnableWhen(h, Halloween.Areas[i].Key, Halloween.DecorationsCanShow);
            }
            for (int i = 0; i < Halloween.Kinds.Count; i++)
            {
                page.EnableWhen(h, Halloween.Kinds[i].Key, Halloween.DecorationsCanShow);
            }
            page.EnableWhen(h, "CandleGlow", Halloween.DecorationsCanShow);
            page.EnableWhen(h, "BlockTheWay", Halloween.DecorationsCanShow);

            // (An event a later game adds has no tab: it is left to the game until a version of the mod knows it, Events.)

            // With "Mod enabled" off every event follows the game's dates and the decorations are as the game made them:
            // every row but that switch is greyed (its value kept).
            var greyed = new List<ConfigDefinition>();
            foreach (SettingsPage.Row r in page.Rows)
            {
                if (r.Entry != null && r.Entry.Definition.Section != "General")
                {
                    greyed.Add(r.Entry.Definition);
                }
            }
            for (int i = 0; i < greyed.Count; i++)
            {
                page.EnableWhen(greyed[i].Section, greyed[i].Key, ModOn);
            }
        }

        private static bool ModOn() => Plugin.Enabled.Value;

        // An event's mode and its own dates; the dates only count with the mode on Auto, so greyed otherwise.
        private static void EventRows(SettingsPage page, GameEvent e, string label)
        {
            page.Choice(e.Name, "Mode", EventMode.Auto, label, e.Mode.Description.Description, labels: ModeLabels);
            page.Text(e.Name, "Start", "", "Your own dates: from (month-day)", e.Start.Description.Description);
            page.Text(e.Name, "End", "", "Your own dates: to (month-day)", e.End.Description.Description);
            Func<bool> auto = () => e.Mode.Value == EventMode.Auto;
            page.EnableWhen(e.Name, "Start", auto);
            page.EnableWhen(e.Name, "End", auto);
        }

        // The "On today" line: what of Halloween shows today, e.g. "Halloween: decorations, ghost, zombie head" or
        // "Halloween: off"; with the mod off, the game's own answer, said as such. In the game's language: its words are
        // the mod's own (EdWords), and the event's name in it the window's (its tab's), so the line names it as the tab
        // beside it does.
        internal static string Today()
        {
            bool mod = Plugin.Enabled.Value;
            GameEvent e = Events.Halloween;
            bool today = mod ? e.IsOn() : Events.GameDates(e); // the event itself, by its mode and dates
            string what = Join(Part(mod ? Halloween.DecorationsOn() : today, EdWords.TodayDecorations),
                               Part(mod ? Halloween.PieceOn(Halloween.Ghost) : today, EdWords.TodayGhost),
                               Part(mod ? Halloween.PieceOn(Halloween.ZombieHead) : today, EdWords.TodayZombieHead));
            string s = EdWords.TodayEvent.Format(Texts.Of(Texts.Api, "section.halloween", Events.HalloweenName), what);
            if (mod && e.DatesDontCount()) s = EdWords.TodayNote.Format(s, EdWords.TodayDatesDontCount.Text);
            return mod ? s : EdWords.TodayGameDates.Format(s);
        }

        // A part that shows today: its name; null: it doesn't. (A part shows only while the event is on, so the line
        // never names one beside "off".)
        private static string Part(bool on, Word name) => on ? name.Text : null;

        private static string Join(string a, string b, string c)
        {
            string s = a;
            if (b != null) s = s == null ? b : EdWords.TodayAnd.Format(s, b);
            if (c != null) s = s == null ? c : EdWords.TodayAnd.Format(s, c);
            return s ?? EdWords.TodayOff.Text;
        }
    }
}

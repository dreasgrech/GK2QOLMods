using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using GK2Shared;

namespace GK2EventDecorations
{
    // The rows of the options window (the window code, lib\GK2DreasUI: a "GK2 Event Decorations" button in the game's
    // Settings window). Every row binds the same section, key and type as the mod's own Config.Bind (Plugin, Events,
    // Halloween), so BepInEx hands the page the entries bound there and the window edits the values the mod reads; the
    // page keeps the cfg's sections as named (numberSections: false). A change applies at once: the decorations go up or
    // down (Decorations.OnSettingChanged, on the cfg's SettingChanged), the rest is read where the game asks.
    internal static class OptionsPage
    {
        private static readonly string[] ModeLabels = { "Auto", "On", "Off" };   // EventMode, as in the cfg and the descriptions
        private static readonly string[] PieceLabels = { "Event", "On", "Off" }; // PieceMode

        internal static void Declare(SettingsPage page)
        {
            page.Status("General");
            page.Info("General", "Today", Today, "What is on today with these settings.");
            page.Toggle("General", "Enabled", true, "Use these settings", Plugin.Enabled.Description.Description);

            // Halloween: its mode and own dates, its pieces
            EventRows(page, Events.Halloween, "Halloween");
            string h = Events.HalloweenName;
            page.Choice(h, "Decorations", PieceMode.Event, "Decorations", Halloween.Decorations.Description.Description, labels: PieceLabels);
            page.Choice(h, "Ghost", PieceMode.Event, "Ghost walking at night", Halloween.Ghost.Description.Description, labels: PieceLabels);
            page.Choice(h, "ZombieHead", PieceMode.Event, "Halloween zombie head", Halloween.ZombieHead.Description.Description, labels: PieceLabels);
            page.Toggle(h, "SwitchWithoutReload", true, "Put up / take down without reloading", Halloween.LiveSwitch.Description.Description);
            page.Toggle(h, "SendGhostHome", true, "Send the ghost home when it's off", Plugin.SendGhostHome.Description.Description);

            // where and which decorations
            for (int i = 0; i < Halloween.Areas.Count; i++)
            {
                Halloween.Area a = Halloween.Areas[i];
                page.Toggle(Halloween.AreasSection, a.Key, true, a.Label, a.Entry.Description.Description);
            }
            for (int i = 0; i < Halloween.Kinds.Count; i++)
            {
                Halloween.Kind k = Halloween.Kinds[i];
                page.Toggle(Halloween.KindsSection, k.Key, true, k.Label, k.Entry.Description.Description);
            }
            page.Toggle(Halloween.KindsSection, "CandleGlow", true, "Candle and lantern glow", Halloween.Glow.Description.Description);
            page.Toggle(Halloween.KindsSection, "BlockTheWay", true, "Decorations block the way", Halloween.BlockWay.Description.Description);

            // an event a game update added after this version (Events.Bind found it): its own section
            for (int i = 0; i < Events.All.Count; i++)
            {
                GameEvent e = Events.All[i];
                if (!e.Known)
                {
                    EventRows(page, e, e.Name + " (new event)");
                }
            }

            page.Toggle("Safety", "StopOnChangedGameCode", false, "Stop on changed game code",
                GameDependencies.StopOnChangedGameCode.Description.Description + " Applies at the next start.");

            // With "Use these settings" off every event follows the game's dates and the decorations are as the game
            // made them: every row but that switch and Safety's is greyed (its value kept).
            var greyed = new List<ConfigDefinition>();
            foreach (SettingsPage.Row r in page.Rows)
            {
                if (r.Entry != null && r.Entry.Definition.Section != "General" && r.Entry.Definition.Section != "Safety")
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
            page.Text(e.Name, "Start", "", "Own dates: from (month-day)", e.Start.Description.Description);
            page.Text(e.Name, "End", "", "Own dates: to (month-day)", e.End.Description.Description);
            Func<bool> auto = () => e.Mode.Value == EventMode.Auto;
            page.EnableWhen(e.Name, "Start", auto);
            page.EnableWhen(e.Name, "End", auto);
        }

        // What is on today: the options window's Today line and the Mods menu's.
        internal static string Today()
        {
            if (!Plugin.Enabled.Value)
            {
                return "The game's own dates";
            }
            string s = "";
            for (int i = 0; i < Events.All.Count; i++)
            {
                GameEvent e = Events.All[i];
                s += (i == 0 ? "" : " · ") + e.Name + (e.IsOn() ? " on" : " off");
            }
            return s + " · decorations " + (Halloween.DecorationsOn() ? "on" : "off") +
                   ", ghost " + (Halloween.PieceOn(Halloween.Ghost) ? "on" : "off") +
                   ", zombie head " + (Halloween.PieceOn(Halloween.ZombieHead) ? "on" : "off");
        }
    }
}

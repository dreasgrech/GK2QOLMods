using System.Collections.Generic;

namespace GK2EventDecorations
{
    // The mod's own words (the window code's Word: a key in the mod's Languages\<id>.csv and its English, looked up
    // when drawn, in the game's language): what the options window's two lines say, "On today" and Status. Their
    // values are made in code (OptionsPage.Today, Plugin.StatusShown), so they are no row's label or description and
    // were English in every language until 2026-11-18. Listed for the translators' template through UiOwner.OwnWords
    // (Plugin.BindConfig); keys stay as they are once released (a translation is found by its key).
    // The names inside the lines are not words of their own but the window's: the event's name is its tab's
    // (section.halloween), "Mod enabled" the switch's label (Enabled.label), so a line always names a thing as the
    // row beside it does.
    internal static class EdWords
    {
        // ---- "On today"
        internal static readonly Word TodayEvent = new Word("today.event", "{0}: {1}",
            "the \"On today\" line; {0}: the event's name (Halloween), {1}: what of it shows today (the words below), e.g. \"Halloween: decorations, ghost\"");
        internal static readonly Word TodayOff = new Word("today.off", "off",
            "the \"On today\" line when nothing of the event shows today: \"Halloween: off\"");
        internal static readonly Word TodayDecorations = new Word("today.decorations", "decorations",
            "the \"On today\" line: the Halloween decorations show today (lower case in English: an item of a list after \"Halloween:\")");
        internal static readonly Word TodayGhost = new Word("today.ghost", "ghost",
            "the \"On today\" line: the ghost walks tonight (an item of the same list)");
        internal static readonly Word TodayZombieHead = new Word("today.zombie_head", "zombie head",
            "the \"On today\" line: zombies get the Halloween head today (an item of the same list)");
        internal static readonly Word TodayAnd = new Word("today.and", "{0}, {1}",
            "the \"On today\" line: how two items of the list are joined, e.g. \"decorations, ghost\"; {0} and {1}: items (or a list so far and the next item)");
        internal static readonly Word TodayNote = new Word("today.with_note", "{0} ({1})",
            "the \"On today\" line with a remark after it; {0}: the line so far, {1}: the remark (the sentence below)");
        internal static readonly Word TodayDatesDontCount = new Word("today.dates_dont_count",
            "your own dates don't count: both are needed, as month-day like 10-24, so the game's dates apply",
            "a remark shown in brackets after the \"On today\" line (the brackets are added around it: use none inside it): the player typed only one of the two dates, or something that is not a date; 10-24 is an example of the form (month-day) and stays as it is");
        internal static readonly Word TodayGameDates = new Word("today.game_dates", "{0} (the game's own dates)",
            "the \"On today\" line while the mod is switched off: what shows is decided by the game's own dates; {0}: the line so far");

        // ---- Status
        internal static readonly Word StatusActive = new Word("status.active", "Active",
            "the Status line: the mod is running");
        internal static readonly Word StatusOff = new Word("status.off", "Off: \"{0}\" is unchecked",
            "the Status line while the player has switched the mod off; {0}: the switch's name, already translated (Mod enabled)");
        internal static readonly Word StatusNotStarted = new Word("status.not_started", "not started",
            "the Status line before the mod has started (hardly ever seen)");
        internal static readonly Word StatusCheckFailed = new Word("status.check_failed",
            "Disabled: its check of the game's code could not run (see BepInEx/LogOutput.log).",
            "the Status line: the mod switched itself off at start; BepInEx/LogOutput.log is a file's path and stays as it is");
        internal static readonly Word StatusMissing = new Word("status.missing",
            "Disabled: the game no longer has what this mod uses ({0} member(s)). Update the mod.",
            "the Status line after a game update the mod doesn't work with; {0}: a number (how many pieces of the game's code are gone)");
        internal static readonly Word StatusChangedStop = new Word("status.changed_stop",
            "Disabled: the game changed {0} member(s) this mod uses, and Safety.StopOnChangedGameCode is set.",
            "the Status line after a game update; {0}: a number; Safety.StopOnChangedGameCode is a setting's name in the cfg file and stays as it is");
        internal static readonly Word StatusChangedRun = new Word("status.changed_run",
            "Running anyway: the game changed {0} member(s) this mod uses since this build. A re-audited build is pending.",
            "the Status line after a game update, the mod still running; {0}: a number");
        internal static readonly Word StatusPatchFailed = new Word("status.patch_failed",
            "Disabled: patching failed (see BepInEx/LogOutput.log).",
            "the Status line: the mod could not hook into the game at start; BepInEx/LogOutput.log is a file's path and stays as it is");

        internal static readonly List<Word> All = new List<Word>
        {
            TodayEvent, TodayOff, TodayDecorations, TodayGhost, TodayZombieHead, TodayAnd, TodayNote, TodayDatesDontCount, TodayGameDates,
            StatusActive, StatusOff, StatusNotStarted, StatusCheckFailed, StatusMissing, StatusChangedStop, StatusChangedRun, StatusPatchFailed
        };
    }
}

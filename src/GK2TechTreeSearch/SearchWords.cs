using System.Collections.Generic;

namespace GK2TechTreeSearch
{
    // The search box's own words (the window code's Word: a key in the mod's Languages\<id>.csv and its English, looked
    // up when drawn, in the game's language): what the box and its result list say themselves. The techs', tabs' and
    // unlocks' names in the list are the game's own. Listed for the translators' template through UiOwner.OwnWords
    // (Plugin.Awake); keys stay as they are once released (a translation is found by its key).
    internal static class SearchWords
    {
        internal static readonly Word Placeholder = new Word("search.placeholder", "Search...",
            "the grey text in the empty search box on the Tech Tree page; short: the box is narrow (about 12 letters)");
        internal static readonly Word NoMatch = new Word("search.no_match", "No visible tech matches",
            "the result list's one line when nothing matches what was typed (hidden techs and locked tabs are never shown)");
        internal static readonly Word More = new Word("search.more", "+{0} more: type more letters",
            "the result list's last line when there are more results than it shows; {0}: how many more (a number)");

        internal static readonly List<Word> All = new List<Word> { Placeholder, NoMatch, More };
    }
}

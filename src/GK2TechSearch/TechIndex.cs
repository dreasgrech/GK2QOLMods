using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine.UI;

namespace GK2TechSearch
{
    // Private game members the mod reads. Resolved once; a missing one throws in Verify() and disables the mod.
    internal static class Access
    {
        public static readonly AccessTools.FieldRef<TechTreePageWidget, LazyScrollRect> ScrollRect =
            AccessTools.FieldRefAccess<TechTreePageWidget, LazyScrollRect>("scrollRect");

        public static readonly AccessTools.FieldRef<TechTreePageWidget, TextMeshProUGUI> RedSpheresLabel =
            AccessTools.FieldRefAccess<TechTreePageWidget, TextMeshProUGUI>("redSpheresLabel");

        public static readonly AccessTools.FieldRef<TechTreePageWidget, TechTreeTabButton> TabButtonPrefab =
            AccessTools.FieldRefAccess<TechTreePageWidget, TechTreeTabButton>("tabButtonPrefab");

        public static readonly AccessTools.FieldRef<TechTreeElementBaseWidget, Image> Selection =
            AccessTools.FieldRefAccess<TechTreeElementBaseWidget, Image>("selection");

        // Optional: the game's styled text box to copy. Missing only means the plain box is built instead.
        public static readonly AccessTools.FieldRef<UIBugReportWindow, TMP_InputField> BugReportTitleInput =
            AccessTools.Field(typeof(UIBugReportWindow), "titleInput") != null
                ? AccessTools.FieldRefAccess<UIBugReportWindow, TMP_InputField>("titleInput")
                : null;

        public static void Verify()
        {
            if (ScrollRect == null || RedSpheresLabel == null || TabButtonPrefab == null || Selection == null)
            {
                throw new InvalidOperationException("A game member this mod relies on was not found.");
            }
        }
    }

    internal sealed class Hit
    {
        public TechDef Def;
        public string Name;     // the tech's name as its card shows it
        public string Via;      // the unlock that matched, or null when the name matched
        public int Rank;        // 0 name starts with the query, 1 name contains it, 2 an unlock matched
    }

    // Read-only: walks GameBalance.Me.techDefs and the knowledge system. Nothing here writes game state.
    internal static class TechIndex
    {
        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);

        // Every visible tech whose name, or one of whose unlocks, contains every word of the query.
        // Hidden techs ("?" cards) and techs on locked tabs are never returned: the game hides them on purpose.
        public static List<Hit> Find(string query)
        {
            List<Hit> hits = new List<Hit>();
            string[] words = Words(query);
            KnowledgeSystem knowledge = MainGame.Instance?.GameSave?.knowledgeSystem;
            if (words.Length == 0 || knowledge == null || GameBalance.Me?.techDefs == null)
            {
                return hits;
            }
            foreach (TechDef def in GameBalance.Me.techDefs)
            {
                if (def == null || string.IsNullOrEmpty(def.id) || knowledge.IsTechTabLocked(def.tab) || def.TechState == TechState.Hidden)
                {
                    continue;
                }
                string name = LLBase.HasL(def.id) ? Plain(LLBase.L(def.id)) : null;
                if (name != null && ContainsAll(name, words))
                {
                    hits.Add(new Hit { Def = def, Name = name, Rank = name.StartsWith(words[0], StringComparison.OrdinalIgnoreCase) ? 0 : 1 });
                    continue;
                }
                if (!Plugin.SearchUnlocks.Value || def.linkedEntityWidgetDatas == null)
                {
                    continue;
                }
                foreach (LinkedEntityWidgetData unlock in def.linkedEntityWidgetDatas)
                {
                    string header = Header(unlock);
                    if (header != null && ContainsAll(header, words))
                    {
                        hits.Add(new Hit { Def = def, Name = name ?? def.id, Via = header, Rank = 2 });
                        break;
                    }
                }
            }
            // Best first; then in the game's tab order, left to right, top to bottom, as the tree reads.
            hits.Sort((a, b) =>
            {
                int c = a.Rank.CompareTo(b.Rank);
                if (c == 0) c = ((int)a.Def.tab).CompareTo((int)b.Def.tab);
                if (c == 0) c = a.Def.TreePos.x.CompareTo(b.Def.TreePos.x);
                if (c == 0) c = a.Def.TreePos.y.CompareTo(b.Def.TreePos.y);
                return c;
            });
            return hits;
        }

        public static string TabName(TechTreeTab tab)
        {
            return Plain(LLBase.L("tech_tab_" + tab));
        }

        // The title of the unlock icon's tooltip (UITooltip.ShowLinkedEntity): an item, building, perk or formula
        // name. A pure localisation lookup; any surprise in one entry only skips that entry.
        private static string Header(LinkedEntityWidgetData unlock)
        {
            if (unlock == null)
            {
                return null;
            }
            try
            {
                string h = unlock.GetLinkedEntityHeader();
                return string.IsNullOrEmpty(h) ? null : Plain(h);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Plain(string s)
        {
            return string.IsNullOrEmpty(s) ? s : Tags.Replace(s, "").Trim();
        }

        private static string[] Words(string query)
        {
            return string.IsNullOrWhiteSpace(query)
                ? Array.Empty<string>()
                : query.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool ContainsAll(string text, string[] words)
        {
            foreach (string w in words)
            {
                if (text.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
}

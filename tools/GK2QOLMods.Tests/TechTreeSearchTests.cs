using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using GK2ModTestBench;
using GK2TechTreeSearch;
using LazyBearTechnology;

namespace GK2QOLMods.Tests
{
    // GK2 Tech Tree Search in the game: the search itself (TechIndex.Find), asked the way the search box asks it,
    // against the game's own tech list and names in the language the game is in. Read-only but for one test that
    // switches "search what techs unlock" off and puts it back. No test opens the Tech Tree page.
    internal static class TechTreeSearchTests
    {
        internal const string ModGuid = "dreas.gk2.techtreesearch";
        private const string Mod = "The mod", Search = "Search";
        private const char BreakMark = '​'; // the game's invisible break mark in Japanese and Chinese names
        private static readonly Regex Tags = new Regex("<[^>]*>");

        // Nothing of the mod's own types here: the suite is added whether the mod is installed or not.
        internal static void Register()
        {
            TestSuite s = TestBench.Suite(ModGuid, "GK2 Tech Tree Search");
            s.CanStart = () => Chainloader.PluginInfos.ContainsKey(ModGuid) ? null : "GK2 Tech Tree Search isn't installed";
            s.Add(Mod, "the mod is running", Running);
            s.Add(Search, "every visible tech is found by its whole name", WholeNames);
            s.Add(Search, "a name with the game's break marks in it finds its tech; no result carries a mark or a tag", BreakMarks);
            s.Add(Search, "hidden techs and techs on locked tabs are never found", HiddenStayHidden);
            s.Add(Search, "the results come best first: names that start with the query, names that hold it, then unlocks", BestFirst);
            s.Add(Search, "an empty query finds nothing", Empty);
            s.Add(Search, "with \"search what techs unlock\" off, no result comes from an unlock", UnlocksOff);
        }

        private static IEnumerator Running()
        {
            Test.Check(GK2TechTreeSearch.Plugin.Status == "Active", $"its status is \"{GK2TechTreeSearch.Plugin.Status}\" (Active when it started without a fault)");
            Test.Info($"the search box is {(GK2TechTreeSearch.Plugin.Enabled.Value ? "on" : "off")}, unlocks are {(GK2TechTreeSearch.Plugin.SearchUnlocks.Value ? "searched" : "not searched")}, the game's language is {LLBase.CurrentLang}");
            yield return null;
        }

        // The techs a player can see now, by the game's own rules (the same two the search applies), with their names
        // as the cards show them: tags and break marks taken out here, by this test's own code.
        private sealed class Seen
        {
            internal TechDef Def;
            internal string Raw, Name;
        }

        private static List<Seen> Techs(bool visible)
        {
            var list = new List<Seen>();
            KnowledgeSystem knowledge = MainGame.Instance?.GameSave?.knowledgeSystem;
            if (knowledge == null || GameBalance.Me?.techDefs == null) return null;
            foreach (TechDef def in GameBalance.Me.techDefs)
            {
                if (def == null || string.IsNullOrEmpty(def.id) || !LLBase.HasL(def.id)) continue;
                bool shown = !knowledge.IsTechTabLocked(def.tab) && def.TechState != TechState.Hidden;
                if (shown != visible) continue;
                string raw = LLBase.L(def.id);
                string name = Tags.Replace(raw ?? "", "").Replace(BreakMark.ToString(), "").Trim();
                if (name.Length > 0) list.Add(new Seen { Def = def, Raw = raw, Name = name });
            }
            return list;
        }

        private static bool Holds(List<Hit> hits, TechDef def)
        {
            foreach (Hit h in hits)
            {
                if (h.Def == def) return true;
            }
            return false;
        }

        private static string Some(List<string> names) => names.Count == 0 ? "" : ": " + string.Join(", ", names);

        private static IEnumerator WholeNames()
        {
            List<Seen> techs = Techs(visible: true);
            if (techs == null)
            {
                Test.Warn("the game's tech list or the save's knowledge isn't there");
                yield break;
            }
            Test.Check(techs.Count > 0, $"{techs.Count} visible tech(s) with a name in this save");
            int found = 0, byName = 0;
            var missed = new List<string>();
            var watch = new System.Diagnostics.Stopwatch(); // the searches alone, not the frames between them
            for (int k = 0; k < techs.Count; k++)
            {
                watch.Start();
                List<Hit> hits = TechIndex.Find(techs[k].Name);
                watch.Stop();
                bool ok = false;
                foreach (Hit h in hits)
                {
                    if (h.Def != techs[k].Def) continue;
                    ok = true;
                    if (h.Via == null && h.Name == techs[k].Name) byName++;
                    break;
                }
                if (ok) found++;
                else if (missed.Count < 5) missed.Add(techs[k].Name);
                if (k % 25 == 24) yield return null; // a few dozen searches a frame
            }
            Test.Check(found == techs.Count, $"{found} of {techs.Count} found by their whole name{Some(missed)}");
            Test.Check(byName == techs.Count, $"{byName} of {techs.Count} matched on the name itself, shown as the card shows it");
            // A fact, not a check yet: the search box runs one search a keystroke. The first run (2026-11-17) showed
            // frames of about 550 ms for 25 searches in the API's frame log, and this line is the exact number.
            double each = watch.Elapsed.TotalMilliseconds / techs.Count;
            Test.Info($"{techs.Count} searches took {watch.Elapsed.TotalMilliseconds:0} ms: {each:0.0} ms a search (the box runs one a keystroke; a frame at 60 fps is 16.7 ms)");
        }

        private static IEnumerator BreakMarks()
        {
            List<Seen> techs = Techs(visible: true);
            if (techs == null || techs.Count == 0)
            {
                Test.Warn("no visible tech to search for");
                yield break;
            }
            int marked = 0, found = 0;
            var missed = new List<string>();
            foreach (Seen t in techs)
            {
                if (t.Raw.IndexOf(BreakMark) < 0) continue;
                marked++;
                // as pasted from the game: the tags gone, the marks still in
                if (Holds(TechIndex.Find(Tags.Replace(t.Raw, "")), t.Def)) found++;
                else if (missed.Count < 5) missed.Add(t.Name);
            }
            if (marked == 0) Test.Info($"the game's language ({LLBase.CurrentLang}) puts no break mark into a tech's name: only Japanese and Simplified Chinese do");
            else Test.Check(found == marked, $"{found} of {marked} name(s) with break marks found when pasted with the marks{Some(missed)}");
            yield return null;

            // every result of a broad query: what is shown carries neither a mark nor a tag
            string query = techs[0].Name.Substring(0, 1);
            List<Hit> hits = TechIndex.Find(query);
            int dirty = 0;
            foreach (Hit h in hits)
            {
                if (Dirty(h.Name) || Dirty(h.Via)) dirty++;
            }
            Test.Check(hits.Count > 0 && dirty == 0, $"\"{query}\": {hits.Count} result(s), {dirty} with a break mark or a tag in its name or unlock");
        }

        private static bool Dirty(string s) => s != null && (s.IndexOf(BreakMark) >= 0 || s.IndexOf('<') >= 0);

        private static IEnumerator HiddenStayHidden()
        {
            List<Seen> hidden = Techs(visible: false);
            if (hidden == null)
            {
                Test.Warn("the game's tech list or the save's knowledge isn't there");
                yield break;
            }
            if (hidden.Count == 0)
            {
                Test.Skip("this save has no hidden tech and no locked tab: nothing that must stay out of the results");
                yield break;
            }
            int leaked = 0;
            var names = new List<string>();
            for (int k = 0; k < hidden.Count; k++)
            {
                if (Holds(TechIndex.Find(hidden[k].Name), hidden[k].Def))
                {
                    leaked++;
                    if (names.Count < 5) names.Add(hidden[k].Name);
                }
                if (k % 25 == 24) yield return null;
            }
            Test.Check(leaked == 0, $"{leaked} of {hidden.Count} hidden or locked tech(s) found by name{Some(names)}");
        }

        private static IEnumerator BestFirst()
        {
            List<Seen> techs = Techs(visible: true);
            if (techs == null || techs.Count == 0)
            {
                Test.Warn("no visible tech to search for");
                yield break;
            }
            // two letters of a name's first word: enough results to have an order. Of the first word, never across a
            // space: the search takes a query apart at its blanks and ranks by the first word alone.
            string name = techs[0].Name;
            string first = name.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries)[0];
            string query = first.Substring(0, first.Length >= 2 ? 2 : 1);
            List<Hit> hits = TechIndex.Find(query);
            Test.Check(hits.Count > 0 && Holds(hits, techs[0].Def), $"\"{query}\" finds {hits.Count} tech(s), \"{name}\" among them");
            int outOfOrder = 0, wrongRank = 0;
            for (int k = 0; k < hits.Count; k++)
            {
                Hit h = hits[k];
                if (k > 0)
                {
                    Hit p = hits[k - 1];
                    if (h.Rank < p.Rank || (h.Rank == p.Rank && (int)h.Def.tab < (int)p.Def.tab)) outOfOrder++;
                }
                int rank = h.Via != null ? 2 : h.Name.StartsWith(query, System.StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                if (rank != h.Rank) wrongRank++;
            }
            Test.Check(outOfOrder == 0, $"{outOfOrder} of {hits.Count} result(s) before a better one or before an earlier tab's");
            Test.Check(wrongRank == 0, $"{wrongRank} of {hits.Count} result(s) ranked other than: starts with it, holds it, an unlock");
            yield return null;
        }

        private static IEnumerator Empty()
        {
            Test.Check(TechIndex.Find("").Count == 0, "\"\" finds nothing");
            Test.Check(TechIndex.Find("   ").Count == 0, "blanks find nothing");
            Test.Check(TechIndex.Find(null).Count == 0, "no query at all finds nothing");
            Test.Check(TechIndex.Find(BreakMark.ToString()).Count == 0, "a break mark alone finds nothing");
            yield return null;
        }

        private static IEnumerator UnlocksOff()
        {
            List<Seen> techs = Techs(visible: true);
            if (techs == null || techs.Count == 0)
            {
                Test.Warn("no visible tech to search for");
                yield break;
            }
            bool was = GK2TechTreeSearch.Plugin.SearchUnlocks.Value;
            try
            {
                // broad queries: the first letter of the first few names
                var queries = new List<string>();
                for (int k = 0; k < techs.Count && queries.Count < 6; k++)
                {
                    string q = techs[k].Name.Substring(0, 1);
                    if (!queries.Contains(q)) queries.Add(q);
                }
                GK2TechTreeSearch.Plugin.SearchUnlocks.Value = true;
                int withUnlocks = 0, viaOn = 0;
                foreach (string q in queries)
                {
                    foreach (Hit h in TechIndex.Find(q))
                    {
                        withUnlocks++;
                        if (h.Via != null) viaOn++;
                    }
                }
                yield return null;
                GK2TechTreeSearch.Plugin.SearchUnlocks.Value = false;
                int without = 0, viaOff = 0;
                foreach (string q in queries)
                {
                    foreach (Hit h in TechIndex.Find(q))
                    {
                        without++;
                        if (h.Via != null) viaOff++;
                    }
                }
                Test.Check(viaOff == 0, $"off: {without} result(s) for {queries.Count} one-letter queries, {viaOff} from an unlock");
                Test.Check(without == withUnlocks - viaOn, $"on: {withUnlocks} result(s), {viaOn} from an unlock; off leaves exactly the other {withUnlocks - viaOn} ({without})");
            }
            finally
            {
                GK2TechTreeSearch.Plugin.SearchUnlocks.Value = was; // the player's own setting back, also after a throw
            }
        }
    }
}

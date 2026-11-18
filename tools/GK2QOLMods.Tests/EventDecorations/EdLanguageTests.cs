extern alias EDasm;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GK2ModTestBench;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using ED = EDasm::GK2EventDecorations;
using UI = EDasm::GK2DreasUI;

namespace GK2QOLMods.Tests
{
    // The mod's words in the game's languages, as the running game reads them from the folder shipped beside the DLL
    // (Languages\<id>.csv). The files themselves are checked offline, line by line, by the window library's
    // tests/TranslationScan and l10n-sync --check (tools/release-scans.sh); here: that the deployed mod finds each
    // file, reads it without a complaint, and has a current line for every word it shows.
    // The fonts are the game's, chosen by the game's own language, not by ours: whether every character has a glyph can
    // only be asked in the language the game is in, so that check runs in that one, and says which it was.
    internal static class EdLanguageTests
    {
        private static List<string> Shipped(out string folder)
        {
            var ids = new List<string>();
            folder = UI.Texts.BundledFolder(EdRun.ModGuid);
            if (folder == null || !Directory.Exists(folder)) return ids;
            foreach (string path in Directory.GetFiles(folder, "*.csv"))
            {
                string id = Path.GetFileNameWithoutExtension(path);
                if (!UI.Texts.IsEnglishId(id)) ids.Add(id);
            }
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        private static void Language(string id)
        {
            UI.Texts.LanguageOverride = id;
            UI.Texts.Forget();
        }

        internal static IEnumerator Files()
        {
            if (!EdRun.Ready()) yield break;
            string was = UI.Texts.LanguageOverride;
            List<string> ids = Shipped(out string folder);
            Test.Check(folder != null && File.Exists(Path.Combine(folder, "en.csv")), $"the mod's words are beside its DLL ({folder ?? "no folder"}), the English file among them");
            Test.Check(ids.Count == 10, $"{ids.Count} languages shipped besides English (the game has 10): {string.Join(", ", ids)}");
            if (ids.Count == 0) yield break;
            try
            {
                Language("en");
                List<(string Key, string English, string What)> english = UI.Texts.EnglishOf(EdRun.ModGuid);
                Test.Check(english.Count > 50, $"{english.Count} words and sentences to translate (the page's rows and the window's own words)");
                foreach (string id in ids)
                {
                    Language(id);
                    UI.Texts.Table table = UI.Texts.TableOf(EdRun.ModGuid);
                    if (table == null)
                    {
                        Test.Fail($"{id}: the mod reads no translation for it");
                        continue;
                    }
                    int missing = 0, stale = 0, same = 0;
                    var first = new List<string>();
                    foreach ((string key, string text, string _) in english)
                    {
                        if (!table.Lines.TryGetValue(key, out UI.Texts.Line line) || string.IsNullOrEmpty(line.Value))
                        {
                            missing++;
                            if (first.Count < 4) first.Add(key + " (no line)");
                        }
                        else if (!line.IsFor(text))
                        {
                            stale++;
                            if (first.Count < 4) first.Add(key + " (for an older English)");
                        }
                        else if (line.Same) same++;
                    }
                    Test.Check(missing == 0 && stale == 0 && table.Malformed == 0 && table.Unknown == 0,
                        $"{id}: {english.Count - missing - stale} of {english.Count} lines current, {table.Malformed} unreadable, {table.Unknown} for words the mod doesn't have{EdWorld.Some(first)}; {same} left as the English");
                    yield return null;
                }
            }
            finally
            {
                Language(was);
            }
        }

        // What the window would show in each language: a text for every row, the sentences that take a value still
        // taking it, and the plain sentences really translated.
        internal static IEnumerator Words()
        {
            if (!EdRun.Ready()) yield break;
            UI.SettingsPage page = EdRun.Page();
            if (page == null)
            {
                Test.Fail("the mod made no settings page");
                yield break;
            }
            string was = UI.Texts.LanguageOverride;
            List<string> ids = Shipped(out string _);
            try
            {
                foreach (string id in ids)
                {
                    Language(id);
                    int rows = 0, empty = 0, labelsSame = 0, descriptionsSame = 0, described = 0;
                    foreach (UI.SettingsPage.Row r in page.Rows)
                    {
                        if (!page.InOptions(r)) continue;
                        rows++;
                        string label = UI.Texts.Label(r), desc = UI.Texts.Desc(r);
                        if (string.IsNullOrEmpty(label) || (r.Entry != null && string.IsNullOrEmpty(desc))) empty++;
                        if (label == r.Label) labelsSame++;
                        if (r.Entry != null)
                        {
                            described++;
                            if (desc == r.Description) descriptionsSame++;
                        }
                        if (r.RowKind == UI.SettingsPage.RowKind.Choice && r.ChoiceLabels != null)
                        {
                            for (int i = 0; i < r.ChoiceLabels.Length; i++) if (string.IsNullOrEmpty(UI.Texts.Choice(r, i))) empty++;
                        }
                    }
                    string ask = UI.Words.SetBackAsk.Format("QQQ"), button = UI.Words.PageOptions.Format("QQQ");
                    bool values = ask.Contains("QQQ") && button.Contains("QQQ") && ask != UI.Words.SetBackAsk.English.Replace("{0}", "QQQ");
                    string title = UI.Texts.Title(page), general = UI.Texts.Section(page, "General");
                    // a sentence is never the same in another language; a name may be (Halloween, Port, Status)
                    Test.Check(empty == 0 && descriptionsSame == 0 && values && !string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(general),
                        $"{id}: {rows} rows with a text each ({empty} empty), {described - descriptionsSame} of {described} descriptions translated, the Defaults question and the Settings button still name what they are about ({values}); {labelsSame} name(s) the same as in English");
                    yield return null;
                }
            }
            finally
            {
                Language(was);
            }
        }

        // ------------------------------------------------------------------ the fonts, in the game's own language

        private static readonly List<TMP_Text> texts = new List<TMP_Text>();

        // The characters of a text its font (and the fonts behind it) has no glyph for: what the player would see as boxes.
        private static string NoGlyph(TMP_FontAsset font, string text)
        {
            if (font == null || string.IsNullOrEmpty(text)) return "";
            if (font.HasCharacters(text, out uint[] missing, true, true) || missing == null) return "";
            string s = "";
            foreach (uint c in missing)
            {
                if (c <= 0x20 || c == 0x200B || c == 0xA0 || c == 0xAD || (c < 0x10000 && char.IsWhiteSpace((char)c))) continue; // not drawn at all
                string one = c < 0x10000 ? ((char)c).ToString() : char.ConvertFromUtf32((int)c);
                if (s.IndexOf(one, StringComparison.Ordinal) < 0) s += one;
            }
            return s;
        }

        internal static IEnumerator Glyphs()
        {
            if (!EdRun.Ready()) yield break;
            UI.SettingsPage page = EdRun.Page();
            if (page == null)
            {
                Test.Fail("the mod made no settings page");
                yield break;
            }
            yield return EdWindowTests.Free();
            if (!EdWindowTests.Clear) yield break;
            EdRun.Arrange();
            string language = UI.Texts.Language;
            try
            {
                UI.OptionsWindow.Open(page);
                yield return Test.Frames(2);
                if (!UI.OptionsWindow.IsShown)
                {
                    Test.Fail("the options window didn't open");
                    yield break;
                }
                int drawn = 0, lines = 0;
                var boxes = new List<string>();
                foreach (string name in new[] { "General", "Halloween" })
                {
                    UI.OptionsWindow.TestShowSection(name);
                    yield return Test.Frames(1);
                    texts.Clear();
                    UI.OptionsWindow.TestRoot.GetComponentsInChildren(true, texts);
                    foreach (TMP_Text t in texts)
                    {
                        drawn++;
                        string none = NoGlyph(t.font, t.text);
                        if (none.Length > 0 && boxes.Count < 8) boxes.Add($"\"{(t.text.Length > 30 ? t.text.Substring(0, 30) + "..." : t.text)}\" has no glyph for {none}");
                    }
                    TextMeshProUGUI box = UI.OptionsWindow.TestDescriptionBox;
                    foreach (UI.SettingsPage.Row r in page.Rows)
                    {
                        if (!page.InOptions(r) || (r.ShownSection ?? r.Section) != name) continue;
                        lines++;
                        string none = box != null ? NoGlyph(box.font, UI.OptionsWindow.TestDescription(r)) : "";
                        if (none.Length > 0 && boxes.Count < 8) boxes.Add($"the description of '{r.Label}' has no glyph for {none}");
                    }
                }
                texts.Clear();
                Test.Check(drawn > 0 && boxes.Count == 0, $"in the game's language ({language}): every character of the window's {drawn} texts and {lines} descriptions has a glyph in the game's font{EdWorld.Some(boxes, 8)}");
                Test.Info($"the fonts are the game's for the language it is in ({language}): for another language's characters, set the game to it and run this test again");
                UI.OptionsWindow.TestStepBack();
                yield return Test.Frames(2);
            }
            finally
            {
                if (UI.OptionsWindow.IsOpen) UI.OptionsWindow.TestClose(back: true);
                EdRun.Arrange();
            }
        }
    }
}

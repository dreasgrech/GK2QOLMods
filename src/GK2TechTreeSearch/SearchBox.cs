using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GK2TechTreeSearch
{
    // The search box on the Tech Tree page and the result list under it. One per page (the page is built once).
    //
    // Typing: while the box has the cursor, the game's own key handling (LazyInput) is switched off, exactly as the
    // game's bug report window does (UIBugReportWindow.LateUpdate), so letters never open the map or the inventory.
    // It's switched back on when the box loses the cursor, when the page hides and when the box is destroyed.
    internal sealed class SearchBox : MonoBehaviour
    {
        private const string Placeholder = "Search...";
        // Researched techs: the green of the game's own tick on a card. tiny_font has no ✓ glyph (measured 2026-10-04).
        private const string ResearchedColor = "#8BC34A";
        private static readonly Color RowColor = new Color(0f, 0f, 0f, 0f);
        private static readonly Color RowHighlight = new Color(1f, 0.85f, 0.4f, 0.22f);
        private static readonly Color ListColor = new Color(0.1f, 0.1f, 0.12f, 0.94f);

        internal static SearchBox Instance;

        private TechTreePageWidget page;
        private CharacterWindow window;
        private TMP_InputField input;
        private RectTransform list;
        private TMP_FontAsset font;
        private float fontSize;
        private TextMeshProUGUI fontSource;   // the page's label our labels take their font from (ReadFont)
        private string fontLanguage;          // the game language the font was read for
        private bool fontFailureSaid;
        private string fontSaidFor;           // the language the dev line about the fonts was written for
        private string separator = " · "; // between a result's tab and what the tech unlocks (ReadFont)
        private const string NoMatch = "No visible tech matches";
        private const string MoreLead = "+", MoreTail = " more: type more letters";
        private readonly List<TextMeshProUGUI> ownLabels = new List<TextMeshProUGUI>(); // every label Label() made
        private readonly List<(GameObject Go, Image Back, TextMeshProUGUI Text)> rows = new List<(GameObject, Image, TextMeshProUGUI)>();
        private List<Hit> hits = new List<Hit>();
        private int highlighted;
        private bool inputSuspended;
        private bool inputWasActive = true;
        private bool listHovered;
        private bool errorLogged;

        // Builds the box under the page. Any failure is logged and leaves the page as vanilla.
        internal static void Create(TechTreePageWidget page)
        {
            if (Instance != null || page == null)
            {
                return;
            }
            GameObject root = new GameObject("GK2TechTreeSearch", typeof(RectTransform));
            root.SetActive(false);
            try
            {
                root.transform.SetParent(page.transform, false);
                SearchBox box = root.AddComponent<SearchBox>();
                box.Build(page);
                Instance = box;
                root.SetActive(Plugin.Enabled.Value);
            }
            catch (Exception e)
            {
                UnityEngine.Object.Destroy(root);
                Plugin.Log.LogError($"Tech search box could not be built; the Tech Tree works as normal without it. {e}");
            }
        }

        private void Build(TechTreePageWidget owner)
        {
            page = owner;
            window = owner.GetComponentInParent<CharacterWindow>(true);
            fontSource = Access.RedSpheresLabel(owner);
            fontSize = fontSource != null ? fontSource.fontSize : 18f;
            ReadFont();

            RectTransform rt = (RectTransform)transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            // The page is a fitted layout (CharacterWindow refreshes its content fitter): keep the box out of it, so
            // only our anchors place it.
            gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            ApplyLayout();

            input = CopyGameInput(rt) ?? BuildPlainInput(rt);
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 40;
            input.restoreOriginalTextOnEscape = false;
            input.text = string.Empty;
            if (input.placeholder is TMP_Text ph)
            {
                ph.text = Placeholder;
            }
            input.onValueChanged.AddListener(OnTextChanged);
            input.onSubmit.AddListener(_ => JumpTo(highlighted));
            input.onSelect.AddListener(_ => Refresh());

            list = BuildList(rt);
            list.gameObject.SetActive(false);
            ApplyLayout();
        }

        // The font of our own labels (the result rows; the plain box): the page's point counter's, in the game's language.
        // That counter's text style is one the game marks "always the same font" (it shows numbers): tiny_font in every
        // language, which has no Japanese, Chinese or Korean letters, while the rows show the game's own tech names
        // (read from the game's files, 2026-11-15; up to 1.0.2 the rows wore the counter's font as it was). So the
        // style's font data is asked for the language's font, as the game asks for a label it hasn't marked. Read again
        // when the language changed since (Refresh). Any failure: the counter's own font, as before.
        private void ReadFont()
        {
            TMP_FontAsset own = fontSource != null ? fontSource.font : null;
            string lang = null;
            TMP_FontAsset forLanguage = null;
            try
            {
                lang = GameLanguage();
                forLanguage = LanguageFont(fontSource, lang);
            }
            catch (Exception e)
            {
                if (!fontFailureSaid)
                {
                    fontFailureSaid = true;
                    Plugin.Log.LogWarning($"Tech search: the game's font for its language couldn't be read ({e.GetType().Name}: {e.Message}); the result list keeps the page's counter font, which has no Japanese, Chinese or Korean letters.");
                }
            }
            fontLanguage = lang;
            font = forLanguage != null ? forLanguage : own;
            if (font == null)
            {
                return;
            }
            // the dot between a tab and an unlock: the game's own Japanese and Korean text never uses it, so those fonts may
            // not have it; a hyphen then, which every font of the game has
            separator = Has(font, "·", out _) ? " · " : " - ";
            foreach (TextMeshProUGUI label in ownLabels)
            {
                if (label != null && label.font != font)
                {
                    label.font = font;
                }
            }
        }

        // The game's own, each in a method of its own: a game update that renamed one fails when its method is compiled,
        // inside ReadFont's try (the dependency check names them at start as well).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string GameLanguage() => LLBase.CurrentLang;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static TMP_FontAsset LanguageFont(TextMeshProUGUI label, string lang)
        {
            TextStyleComponent component = label != null ? label.GetComponent<TextStyleComponent>() : null;
            TextStyle style = component != null ? component.CurrentTextStyle : null;
            LazyFontData data = style != null ? style.Font : null;
            return data != null ? data.GetFontAssetFor(lang, false) : null;
        }

        // Whether a font can draw every letter of a text, and the letters it can't: its fallback fonts asked, a letter a
        // font that fills its atlas on demand can still add added (the test GK2 Donkey API's font check uses, proven in
        // the game in every language). The game's invisible break marks inside a name (U+200B) are no letters.
        private static bool Has(TMP_FontAsset f, string text, out string missing)
        {
            missing = "";
            if (f == null || string.IsNullOrEmpty(text))
            {
                return false;
            }
            try
            {
                bool all = f.HasCharacters(text.Replace("​", ""), out uint[] codes, true, true);
                if (codes != null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (uint c in codes)
                    {
                        sb.Append(char.ConvertFromUtf32((int)c));
                    }
                    missing = sb.ToString();
                }
                return all;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // With the dev marker (BepInEx\config\dreas.gk2.dev), once a language: the two fonts by name and which letters of
        // the first result's name each can't draw, so one look at the log tells the counter's font from the language's;
        // and whether the rows' font has our own separator and words.
        private void SayFonts(string name)
        {
            if (!Plugin.DevTools || font == null || string.IsNullOrEmpty(name) || fontSaidFor == (fontLanguage ?? ""))
            {
                return;
            }
            fontSaidFor = fontLanguage ?? "";
            TMP_FontAsset own = fontSource != null ? fontSource.font : null;
            bool rows = Has(font, name, out string rowsMissing);
            bool counter = Has(own, name, out string counterMissing);
            Has(font, NoMatch + MoreLead + MoreTail + "0123456789", out string oursMissing);
            Plugin.Log.LogInfo($"Tech search [dev]: language \"{fontLanguage}\", first result \"{name.Replace("​", "")}\": the rows wear {font.name} ({(rows ? "has every letter" : "LACKS " + rowsMissing)}); "
                + $"the page's counter label wears {(own != null ? own.name : "no font")} ({(counter ? "has every letter" : "LACKS " + counterMissing)}); "
                + $"between a tab and an unlock the rows put \"{separator.Trim()}\""
                + (oursMissing.Length > 0 ? "; the rows' font LACKS of our own words: " + oursMissing : "") + ".");
        }

        // The game's own styled text box, copied from the bug report window if that window is already loaded.
        // FindObjectsOfTypeAll never loads or opens a window, so nothing of the bug report runs.
        private TMP_InputField CopyGameInput(RectTransform parent)
        {
            if (Access.BugReportTitleInput == null)
            {
                return null;
            }
            foreach (UIBugReportWindow w in Resources.FindObjectsOfTypeAll<UIBugReportWindow>())
            {
                TMP_InputField template = w != null ? Access.BugReportTitleInput(w) : null;
                if (template == null)
                {
                    continue;
                }
                GameObject go = Instantiate(template.gameObject, parent, false);
                go.name = "Input";
                go.SetActive(true);
                Stretch((RectTransform)go.transform);
                TMP_InputField copy = go.GetComponent<TMP_InputField>();
                // Serialized listeners point at the bug report window; start from empty events.
                copy.onValueChanged = new TMP_InputField.OnChangeEvent();
                copy.onSubmit = new TMP_InputField.SubmitEvent();
                copy.onEndEdit = new TMP_InputField.SubmitEvent();
                copy.onSelect = new TMP_InputField.SelectionEvent();
                copy.onDeselect = new TMP_InputField.SelectionEvent();
                copy.onValidateInput = null;
                Plugin.Log.LogInfo("Tech search: using a copy of the game's text box.");
                return copy;
            }
            return null;
        }

        // A plain box in the page's own font, when the game's text box isn't available to copy.
        private TMP_InputField BuildPlainInput(RectTransform parent)
        {
            GameObject go = new GameObject("Input", typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            Image back = go.AddComponent<Image>();
            back.color = ListColor;

            GameObject area = new GameObject("Text Area", typeof(RectTransform));
            area.transform.SetParent(go.transform, false);
            RectTransform areaRt = (RectTransform)area.transform;
            Stretch(areaRt);
            areaRt.offsetMin = new Vector2(8f, 2f);
            areaRt.offsetMax = new Vector2(-8f, -2f);
            area.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = Label(area.transform, "Placeholder", Placeholder);
            placeholder.color = new Color(1f, 1f, 1f, 0.45f);
            placeholder.fontStyle = FontStyles.Italic;
            TextMeshProUGUI text = Label(area.transform, "Text", string.Empty);

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.textViewport = areaRt;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.targetGraphic = back;
            if (font != null)
            {
                field.fontAsset = font;
            }
            field.pointSize = fontSize;
            go.SetActive(true);
            Plugin.Log.LogInfo("Tech search: the game's text box wasn't loaded; using a plain one in the page's font.");
            return field;
        }

        private RectTransform BuildList(RectTransform parent)
        {
            GameObject go = new GameObject("Results", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            // Under the box, right edges aligned, with its own width (Layout.ListWidth): the box is small to fit
            // beside the tab buttons, the rows need more room. The fitter sets the height.
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(0f, -2f);
            go.AddComponent<Image>().color = ListColor;
            VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(4, 4, 4, 4);
            v.spacing = 1f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            EventTrigger trigger = go.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => listHovered = true);
            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => listHovered = false);
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
            return rt;
        }

        private TextMeshProUGUI Label(Transform parent, string name, string text)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                t.font = font;
            }
            t.fontSize = fontSize;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.alignment = TextAlignmentOptions.Left;
            t.raycastTarget = false;
            t.text = text;
            ownLabels.Add(t);
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        internal void ApplyLayout()
        {
            RectTransform rt = (RectTransform)transform;
            rt.anchoredPosition = Plugin.BoxOffset.Value;
            rt.sizeDelta = Plugin.BoxSize.Value;
            if (list != null)
            {
                list.sizeDelta = new Vector2(Mathf.Max(Plugin.ListWidth.Value, Plugin.BoxSize.Value.x), list.sizeDelta.y);
                list.anchoredPosition = new Vector2(0f, ListDrop());
            }
        }

        // How far below the box the list starts: under the sub-tab buttons, not just under the box. The box sits
        // beside the tabs, and the open tab hangs lower than the box (round 2, 2026-10-04: box bottom 157.5, tab strip
        // bottom 146.5 in page units), so a list right under the box covered the open tab's lower edge.
        private float ListDrop()
        {
            const float gap = 2f;
            TechTreeTabButton tab = page != null ? Access.TabButtonPrefab(page) : null;
            RectTransform strip = tab != null ? tab.transform.parent as RectTransform : null;
            if (strip == null)
            {
                return -gap;
            }
            Vector3[] corners = new Vector3[4];
            strip.GetWorldCorners(corners); // 0 = bottom left
            float stripBottom = transform.InverseTransformPoint(corners[0]).y;  // in the box's space; its pivot is the top
            float boxBottom = -((RectTransform)transform).rect.height;
            return Mathf.Min(-gap, stripBottom - boxBottom - gap);
        }

        // Called from the page's Redraw: the page is (re)opened.
        internal void OnPageShown()
        {
            bool show = Plugin.Enabled.Value && !LazyInput.IsGamepadActive;
            gameObject.SetActive(show);
            if (!show)
            {
                return;
            }
            ApplyLayout();
            transform.SetAsLastSibling(); // drawn after (over) the tree
            Refresh();
        }

        // Called from the page's Hide.
        internal void OnPageHidden()
        {
            if (input != null && input.isFocused)
            {
                input.DeactivateInputField();
            }
            if (list != null)
            {
                list.gameObject.SetActive(false);
            }
            listHovered = false;
            RestoreInput();
        }

        private void LateUpdate()
        {
            try
            {
                bool focused = input != null && input.isFocused;
                if (focused && !inputSuspended)
                {
                    inputWasActive = LazyInput.IsInputActive();
                    LazyInput.SetInputActivity(isInputActive: false);
                    // LazyInput keeps its last pressed keys while switched off; the click that focused the box
                    // would otherwise stay "pressed" the whole time.
                    LazyInput.ClearAllKeysDown();
                    inputSuspended = true;
                }
                else if (!focused && inputSuspended)
                {
                    RestoreInput();
                }

                if (focused)
                {
                    if (Input.GetKeyDown(KeyCode.DownArrow))
                    {
                        Highlight(highlighted + 1);
                    }
                    else if (Input.GetKeyDown(KeyCode.UpArrow))
                    {
                        Highlight(highlighted - 1);
                    }
                }
                else if (CanTakeFocusKey() && Plugin.FocusKey.Value.IsDown())
                {
                    input.Select();
                    input.ActivateInputField();
                }

                // The list shows while the box has the cursor and something is typed. A press on a row takes the
                // cursor from the box before the click lands, so the list also stays while the mouse is over it.
                bool showList = (focused || listHovered) && !string.IsNullOrWhiteSpace(input.text);
                if (list.gameObject.activeSelf != showList)
                {
                    list.gameObject.SetActive(showList);
                    if (showList)
                    {
                        list.anchoredPosition = new Vector2(0f, ListDrop());
                        transform.SetAsLastSibling();
                    }
                }
            }
            catch (Exception e)
            {
                RestoreInput();
                if (!errorLogged)
                {
                    errorLogged = true;
                    Plugin.Log.LogError($"Tech search failed; the box may not respond, the Tech Tree works as normal. {e}");
                }
            }
        }

        private bool CanTakeFocusKey()
        {
            if (input == null || !Plugin.Enabled.Value || LazyInput.IsGamepadActive)
            {
                return false;
            }
            // Only while the Tech Tree page is what the player sees: no dialog (e.g. a tech's unlock window) on top.
            return window == null || (window.IsShownAndTop && window.LastOpenedPage == CharacterWindowData.CharPage.TechTree);
        }

        private void RestoreInput()
        {
            if (inputSuspended)
            {
                inputSuspended = false;
                LazyInput.SetInputActivity(inputWasActive);
            }
        }

        private void OnDisable()
        {
            RestoreInput();
        }

        private void OnDestroy()
        {
            RestoreInput();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnTextChanged(string _)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (input == null || list == null)
            {
                return;
            }
            RefreshFont();
            hits = TechIndex.Find(input.text);
            int max = Mathf.Clamp(Plugin.MaxResults.Value, 1, 20);
            int shown = Mathf.Min(hits.Count, max);
            bool typed = !string.IsNullOrWhiteSpace(input.text);
            int needed = shown + (typed && hits.Count == 0 ? 1 : 0) + (hits.Count > max ? 1 : 0);
            while (rows.Count < needed)
            {
                rows.Add(BuildRow(rows.Count));
            }
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Go.SetActive(i < needed);
            }
            for (int i = 0; i < shown; i++)
            {
                rows[i].Text.text = RowText(hits[i]);
            }
            if (shown > 0)
            {
                SayFonts(hits[0].Name);
            }
            if (typed && hits.Count == 0)
            {
                rows[0].Text.text = "<alpha=#99>" + NoMatch;
            }
            else if (hits.Count > max)
            {
                rows[shown].Text.text = "<alpha=#99>" + MoreLead + (hits.Count - max) + MoreTail;
            }
            Highlight(0);
        }

        // The game's language changed since the font was read (the Settings window, with this page alive): read again.
        private void RefreshFont()
        {
            string lang;
            try
            {
                lang = GameLanguage();
            }
            catch (Exception)
            {
                return; // said by ReadFont
            }
            if (!string.Equals(lang, fontLanguage, StringComparison.Ordinal))
            {
                ReadFont();
            }
        }

        private string RowText(Hit h)
        {
            string name = h.Def.TechState == TechState.Unlocked ? "<color=" + ResearchedColor + ">" + h.Name + "</color>" : h.Name;
            string line = name + "  <alpha=#99>" + TechIndex.TabName(h.Def.tab);
            if (h.Via != null)
            {
                line += separator + h.Via;
            }
            return line;
        }

        private (GameObject, Image, TextMeshProUGUI) BuildRow(int index)
        {
            GameObject go = new GameObject("Row" + index, typeof(RectTransform));
            go.transform.SetParent(list, false);
            Image back = go.AddComponent<Image>();
            back.color = RowColor;
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = Mathf.Ceil(fontSize * 1.35f);
            Button button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => JumpTo(index));
            TextMeshProUGUI text = Label(go.transform, "Text", string.Empty);
            RectTransform trt = (RectTransform)text.transform;
            trt.offsetMin = new Vector2(6f, 0f);
            trt.offsetMax = new Vector2(-6f, 0f);
            // Hovering a row highlights it, like the keyboard does.
            EventTrigger trigger = go.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (index < hits.Count && index < Plugin.MaxResults.Value)
                {
                    Highlight(index);
                }
            });
            trigger.triggers.Add(enter);
            return (go, back, text);
        }

        private void Highlight(int index)
        {
            int shown = Mathf.Min(hits.Count, Mathf.Clamp(Plugin.MaxResults.Value, 1, 20));
            highlighted = shown == 0 ? 0 : (index % shown + shown) % shown;
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Back.color = i == highlighted && i < shown ? RowHighlight : RowColor;
            }
        }

        // Shows the tech's tab, scrolled to the tech, with its card framed: the game's own DisplayTab does the
        // tab and the scroll (the same call a tab click makes), the frame is the card's own hover frame.
        private void JumpTo(int index)
        {
            try
            {
                if (index < 0 || index >= hits.Count || index >= Plugin.MaxResults.Value)
                {
                    return;
                }
                TechDef def = hits[index].Def;
                KnowledgeSystem knowledge = MainGame.Instance?.GameSave?.knowledgeSystem;
                if (def == null || knowledge == null || knowledge.IsTechTabLocked(def.tab) || def.TechState == TechState.Hidden)
                {
                    return; // never show what the game hides (the result may be stale by a frame)
                }
                if (input.isFocused)
                {
                    input.DeactivateInputField();
                }
                EventSystem.current?.SetSelectedGameObject(null);
                list.gameObject.SetActive(false);
                listHovered = false;
                RestoreInput();
                page.DisplayTab(def.tab, def.id);
                transform.SetAsLastSibling();
                Frame(def);
                LazyAudio.PlayAndForget("tab_click");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Tech search: jumping to a tech failed. {e}");
            }
        }

        private void Frame(TechDef def)
        {
            LazyScrollRect scroll = Access.ScrollRect(page);
            if (scroll == null)
            {
                return;
            }
            foreach (LazyScrollableElement element in scroll.DisplayingElements)
            {
                if (element != null && element.Data is TechTreeElementBaseWidgetData data && data.techDef == def)
                {
                    // DisplayTab checks visibility before it scrolls, so a card scrolled into view may not be drawn
                    // yet: ask the list to draw it now (its normal "became visible" step).
                    LazyWidgetBase widget = element.Widget ?? element.ForceVisibilityAndGetWidget();
                    if (widget is TechTreeElementBaseWidget card)
                    {
                        Image frame = Access.Selection(card);
                        frame?.gameObject.SetActive(true); // the card's own hover frame; it goes on pointer exit or release
                    }
                    else
                    {
                        Plugin.Log.LogInfo($"Tech search: {def.id} is on its tab but its card isn't drawn yet; no frame.");
                    }
                    return;
                }
            }
        }

        // (A one-time log line of the page's and box's rectangles stood here: the 2026-10-04 measurement the layout
        // defaults in Plugin.cs come from. It shipped ungated in 1.0.1 and was removed on 2026-10-30.)
    }
}

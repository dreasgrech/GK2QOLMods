using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using FlowCanvas;
using HarmonyLib;
using UnityEngine;

namespace GK2Mounts
{
    // Investigation aid for routing picked-up items straight onto the cart (Debug tab; Round 9 of the findings).
    // Answers, inside the running game, what the source reading could not:
    //   A. Do Harmony patches on the tiny PlayerData.HasFreeOverheadSlot getter and on the small static
    //      LazyExpressionEvaluationScope.GetOverheadItemForMutation take effect when the GAME calls them (Mono may
    //      inline small methods into their callers, and an inlined call never reaches a patch)? Tested through the
    //      game's own callers: TryAddOverheadItemNoReplace, and the script functions HasPlayerOvrhdItem / InsertOvrhdItem.
    //   B. Which quests listen to the head triggers (AddOverhead, PlayerInsertOverheadToWgoAnItem, ...) and what
    //      their conditions read; quest data lives in assets, not code.
    //   C. Every expression in the game data (all GameBalance definition lists, walked by reflection) that mentions
    //      the head (Ovrhd / Ovhd / Overhead), with its owner.
    //   D. Every world-object flow script whose serialized graph mentions the head.
    //   E. The item identity mechanics, exercised on the cart's own inventory: split-on-add, silent removal of an
    //      emptied entry, and the count-restore remedy.
    // The three test patches below are dormant unless the probe arms them for one call; they are the same patch
    // points the reroute will use.
    internal static class HeadProbe
    {
        internal static bool ForceNoFreeSlot;         // A1: getter patch armed -> HasFreeOverheadSlot reports false
        internal static Item LookupOverride;          // A2: TryGetOverheadItem patch armed -> this item matches
        internal static Item MutationOverride;        // A3: GetOverheadItemForMutation patch armed -> returns this

        public static string Status { get; private set; } = "";

        private static readonly Regex HeadWords = new Regex("ovrhd|ovhd|overhead", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("GK2 Mounts head-item facts " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            PlayerData pd = MainGame.PlayerData;
            if (pd == null)
            {
                return "no player data (load a game first)";
            }
            sb.AppendLine($"[head] count {pd.OverheadCount}, limit {pd.OverheadStackLimit} (extra_overhead {pd.ExtraOverhead}), free slot {pd.HasFreeOverheadSlot}; mounted {Mount.IsMounted} style {Mount.CurrentStyle} cart attached {Cargo.Attached} cart items {Cargo.Count}");
            sb.AppendLine();
            Section(sb, "A. patch effectiveness (through the game's own callers)", () => PatchChecks(pd, sb));
            Section(sb, "B. quests listening to head triggers", () => QuestListeners(sb));
            Section(sb, "C. expressions in game data mentioning the head", () => DataExpressions(sb));
            Section(sb, "D. world-object flow scripts mentioning the head", () => FlowScripts(sb));
            Section(sb, "E. item identity mechanics on the cart inventory", () => Mechanics(sb));
            string path = Path.Combine(Paths.BepInExRootPath, "GK2-head-facts.txt");
            File.WriteAllText(path, sb.ToString());
            Status = "written " + path;
            return Status;
        }

        private static void Section(StringBuilder sb, string title, Action body)
        {
            sb.AppendLine("=== " + title + " ===");
            try
            {
                body();
            }
            catch (Exception e)
            {
                sb.AppendLine("  FAILED: " + e);
            }
            sb.AppendLine();
        }

        // ------------------------------------------------------------------ A

        private static void PatchChecks(PlayerData pd, StringBuilder sb)
        {
            // A1: the getter. With the head empty the game would take the item; with the armed patch it must refuse.
            if (pd.OverheadCount > 0)
            {
                sb.AppendLine("  A1 skipped: the head is not empty (drop what you hold and dump again)");
            }
            else
            {
                Item probe = new Item("wood", 1);
                bool direct, viaGame;
                ForceNoFreeSlot = true;
                try
                {
                    direct = pd.HasFreeOverheadSlot;                    // our own call site (compiled after the patch)
                    viaGame = pd.TryAddOverheadItemNoReplace(probe);     // the game's call site inside PlayerData
                }
                finally
                {
                    ForceNoFreeSlot = false;
                }
                if (viaGame)
                {
                    pd.RemoveOverheadItem(probe); // the patch was bypassed and the item landed on the head; take it off
                }
                sb.AppendLine($"  A1 HasFreeOverheadSlot getter patch: seen by our code = {(direct ? "NO (inlined!)" : "yes")}, honoured inside TryAddOverheadItemNoReplace = {(viaGame ? "NO: the game took the item, the getter is inlined there" : "yes: the game refused the item")}");
            }

            // A2: TryGetOverheadItem through the script function HasPlayerOvrhdItem (lambda in LazyExpression).
            {
                Item probe = new Item("wood", 1);
                LookupOverride = probe;
                string used;
                bool? r;
                try
                {
                    r = EvalBool("HasPlayerOvrhdItem('wood')", "HasPlayerOvrhdItem(\"wood\")", out used);
                }
                finally
                {
                    LookupOverride = null;
                }
                sb.AppendLine($"  A2 TryGetOverheadItem prefix via script {used}: {(r == null ? "expression failed to evaluate (see log)" : r.Value ? "honoured (the script saw the overridden item)" : "NOT honoured")}");
            }

            // A3: GetOverheadItemForMutation through the script function InsertOvrhdItem, scoped to the cart's own
            // WgoData: with the patch honoured the overridden item lands in the cart inventory.
            Inventory cart = Cargo.Inv;
            WgoData scope = Cargo.DonkeyData;
            if (cart == null || scope == null)
            {
                sb.AppendLine("  A3 skipped: mount in Cart style first (needs the cart's WgoData as the script scope)");
            }
            else
            {
                Item probe = new Item("wood", 1);
                Guid g = probe.UniqueId.Guid;
                int before = cart.Data.Inventory.Count;
                MutationOverride = probe;
                string used;
                string result;
                try
                {
                    result = Eval("InsertOvrhdItem()", null, scope, out used);
                }
                finally
                {
                    MutationOverride = null;
                }
                Item landed = null;
                foreach (Item it in cart.Data.Inventory)
                {
                    if (it != null && it.UniqueId.Guid == g)
                    {
                        landed = it;
                    }
                }
                sb.AppendLine($"  A3 GetOverheadItemForMutation postfix via script {used}: result '{result}', cart {before} -> {cart.Data.Inventory.Count} items, overridden item {(landed != null ? "LANDED in the cart (patch honoured)" : "did not land (patch NOT honoured, or the expression failed)")}");
                if (landed != null)
                {
                    cart.RemoveItemFromInventoryByUID(landed);
                    Cargo.Resync("probe");
                }
            }
        }

        private static bool? EvalBool(string a, string b, out string used)
        {
            used = a;
            try
            {
                return new LazyExpression(a).EvaluateBool();
            }
            catch (Exception e1)
            {
                Plugin.Log.LogInfo($"[head] expression {a} failed: {e1.Message}");
                if (b == null) return null;
                used = b;
                try
                {
                    return new LazyExpression(b).EvaluateBool();
                }
                catch (Exception e2)
                {
                    Plugin.Log.LogInfo($"[head] expression {b} failed: {e2.Message}");
                    return null;
                }
            }
        }

        private static string Eval(string a, string b, WgoData scope, out string used)
        {
            used = a;
            try
            {
                return new LazyExpression(a).Evaluate(scope);
            }
            catch (Exception e1)
            {
                Plugin.Log.LogInfo($"[head] expression {a} failed: {e1.Message}");
                if (b == null) return "(failed: " + e1.Message + ")";
                used = b;
                try
                {
                    return new LazyExpression(b).Evaluate(scope);
                }
                catch (Exception e2)
                {
                    return "(failed: " + e2.Message + ")";
                }
            }
        }

        // ------------------------------------------------------------------ B

        private static readonly GlobalEventsSystem.Event.Type[] HeadTriggers =
        {
            GlobalEventsSystem.Event.Type.AddOverhead,
            GlobalEventsSystem.Event.Type.PlayerInsertOverheadToWgoAnItem,
            GlobalEventsSystem.Event.Type.PlayerInsertBodyToAutopsy,
            GlobalEventsSystem.Event.Type.PlayerInsertBodyWithSkulls,
        };

        private static void QuestListeners(StringBuilder sb)
        {
            var histogram = new Dictionary<string, int>();
            int quests = 0, hits = 0;
            foreach (QuestDef q in GameBalance.Me.questDefs)
            {
                quests++;
                foreach (var pair in new[] { ("start", (QuestCheck)q.startCheck), ("finish", (QuestCheck)q.finishCheck) })
                {
                    QuestCheck c = pair.Item2;
                    if (c == null)
                    {
                        continue;
                    }
                    if (c.hasTrigger)
                    {
                        string key = c.triggerType.ToString();
                        histogram[key] = histogram.TryGetValue(key, out int n) ? n + 1 : 1;
                    }
                    bool headTrigger = c.hasTrigger && Array.IndexOf(HeadTriggers, c.triggerType) >= 0;
                    var condTexts = new List<string>();
                    bool headCond = false;
                    foreach (LazyExpression e in c.condExpressions ?? new List<LazyExpression>())
                    {
                        string t = ExprText(e);
                        condTexts.Add(t);
                        headCond |= HeadWords.IsMatch(t);
                    }
                    if (headTrigger || headCond)
                    {
                        hits++;
                        sb.AppendLine($"  quest {q.id} ({pair.Item1} check): trigger {(c.hasTrigger ? c.triggerType + " id='" + c.triggerId + "'" : "none")}, run {c.runModificator}; conditions: {string.Join(" && ", condTexts)}");
                    }
                }
                foreach (var pair in new[] { ("execStart", q.execExpressionsStart), ("execStartFail", q.execExpressionsStartFail), ("execFinish", q.execExpressionsFinish) })
                {
                    foreach (LazyExpression e in pair.Item2 ?? new List<LazyExpression>())
                    {
                        string t = ExprText(e);
                        if (HeadWords.IsMatch(t))
                        {
                            hits++;
                            sb.AppendLine($"  quest {q.id} {pair.Item1}: {t}");
                        }
                    }
                }
                foreach (ExpressionGameRes ct in q.customTriggers ?? new List<ExpressionGameRes>())
                {
                    string t = ExprText(ct.expression);
                    if (HeadWords.IsMatch(t))
                    {
                        hits++;
                        sb.AppendLine($"  quest {q.id} customTrigger {ct.name}: {t}");
                    }
                }
            }
            sb.AppendLine($"  {quests} quests scanned, {hits} head-related entries. Trigger types used by any quest check: {Join(histogram)}");
        }

        // ------------------------------------------------------------------ C

        private static void DataExpressions(StringBuilder sb)
        {
            int defs = 0, hits = 0;
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (FieldInfo listField in typeof(GameBalance).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!(listField.GetValue(GameBalance.Me) is IList list) || listField.FieldType.IsArray)
                {
                    continue;
                }
                foreach (object def in list)
                {
                    if (def == null)
                    {
                        continue;
                    }
                    defs++;
                    string id = IdOf(def);
                    Walk(def, listField.Name + "[" + id + "]", 0, visited, sb, ref hits);
                }
            }
            sb.AppendLine($"  {defs} definitions walked, {hits} expressions or strings mentioning the head.");
        }

        private static readonly string[] SkipFields = { "childDefinitionList", "parentDefinitionList", "Definition", "definition", "parentWorkbench" };

        private static void Walk(object o, string path, int depth, HashSet<object> visited, StringBuilder sb, ref int hits)
        {
            if (o == null || depth > 6)
            {
                return;
            }
            Type t = o.GetType();
            if (t == typeof(string))
            {
                string s = (string)o;
                if (HeadWords.IsMatch(s))
                {
                    hits++;
                    sb.AppendLine("  " + path + ": " + s);
                }
                return;
            }
            if (t.IsPrimitive || t.IsEnum || o is UnityEngine.Object)
            {
                return;
            }
            if (o is LazyExpression le)
            {
                string s = ExprText(le);
                if (HeadWords.IsMatch(s))
                {
                    hits++;
                    sb.AppendLine("  " + path + ": " + s);
                }
                return;
            }
            if (!t.IsValueType && !visited.Add(o))
            {
                return;
            }
            if (o is IDictionary dict)
            {
                foreach (DictionaryEntry de in dict)
                {
                    Walk(de.Key, path + ".key", depth + 1, visited, sb, ref hits);
                    Walk(de.Value, path + "[" + de.Key + "]", depth + 1, visited, sb, ref hits);
                }
                return;
            }
            if (o is IEnumerable en)
            {
                int i = 0;
                foreach (object e in en)
                {
                    if (i++ > 2000) break;
                    Walk(e, path + "[" + (i - 1) + "]", depth + 1, visited, sb, ref hits);
                }
                return;
            }
            if (t.Namespace != null && (t.Namespace.StartsWith("System") || t.Namespace.StartsWith("UnityEngine")))
            {
                return;
            }
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (Array.IndexOf(SkipFields, f.Name) >= 0 || f.FieldType.IsPointer)
                {
                    continue;
                }
                object v;
                try
                {
                    v = f.GetValue(o);
                }
                catch
                {
                    continue;
                }
                Walk(v, path + "." + f.Name, depth + 1, visited, sb, ref hits);
            }
        }

        private static string IdOf(object def)
        {
            FieldInfo f = def.GetType().GetField("id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f?.GetValue(def) as string ?? def.GetType().Name;
        }

        // The expression text: ToString() when the class prints it, else the first string field that looks like one.
        internal static string ExprText(LazyExpression e)
        {
            if (e == null)
            {
                return "";
            }
            string s = null;
            try
            {
                s = e.ToString();
            }
            catch
            {
            }
            if (!string.IsNullOrEmpty(s) && s != e.GetType().FullName)
            {
                return s;
            }
            for (Type t = e.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType == typeof(string) && f.Name.IndexOf("xpression", StringComparison.Ordinal) >= 0)
                    {
                        string v = f.GetValue(e) as string;
                        if (!string.IsNullOrEmpty(v))
                        {
                            return v;
                        }
                    }
                }
            }
            return s ?? "";
        }

        // ------------------------------------------------------------------ D

        private static void FlowScripts(StringBuilder sb)
        {
            var names = new SortedSet<string>();
            foreach (WGODef d in GameBalance.Me.wgoDefs)
            {
                if (!string.IsNullOrEmpty(d.attachedScript))
                {
                    names.Add(d.attachedScript);
                }
            }
            int loaded = 0, hits = 0;
            foreach (string name in names)
            {
                FlowGraph graph = null;
                try
                {
                    graph = FlowScriptAssetLoadPolicy.Load(WgoDataScript.WGO_DATA_SCRIPTS_RELATIVE_PATH + name);
                    if (graph == null)
                    {
                        sb.AppendLine($"  script {name}: not found");
                        continue;
                    }
                    loaded++;
                    string json = ScriptDump.FindSerializedJson(graph) ?? "";
                    var snippets = new List<string>();
                    foreach (Match m in HeadWords.Matches(json))
                    {
                        int a = Math.Max(0, m.Index - 70), b = Math.Min(json.Length, m.Index + 90);
                        snippets.Add(json.Substring(a, b - a).Replace("\\\"", "\"").Replace("\n", " "));
                        if (snippets.Count >= 12) break;
                    }
                    if (snippets.Count > 0)
                    {
                        hits++;
                        sb.AppendLine($"  script {name}: {snippets.Count}+ mentions");
                        foreach (string s in snippets)
                        {
                            sb.AppendLine("      …" + s + "…");
                        }
                    }
                }
                catch (Exception e)
                {
                    sb.AppendLine($"  script {name}: failed: {e.Message}");
                }
                finally
                {
                    if (graph != null)
                    {
                        try { FlowScriptAssetLoadPolicy.ReleaseGraph(graph); } catch { }
                    }
                }
            }
            sb.AppendLine($"  {names.Count} distinct scripts on world-object definitions, {loaded} loaded, {hits} mention the head.");
        }

        // ------------------------------------------------------------------ E

        private static void Mechanics(StringBuilder sb)
        {
            Inventory cart = Cargo.Inv;
            if (cart == null)
            {
                sb.AppendLine("  skipped: mount in Cart style first (uses the cart's own inventory)");
                return;
            }
            if (cart.Data.Inventory.Count >= cart.Data.InventorySize)
            {
                sb.AppendLine("  skipped: the cart is full (needs one free slot)");
                return;
            }
            Inventory table = Inventory.Create(2);
            int removedEvents = 0;
            Action<List<Item>> onRemove = _ => removedEvents++;
            cart.OnItemsRemove += onRemove;
            try
            {
                // E1: add to the cart: is the stored object the same reference? what happens to the source?
                Item src = new Item("wood", 1);
                Guid g = src.UniqueId.Guid;
                bool added = cart.AddItemToInventory(src);
                Item stored = FindByGuid(cart, g);
                sb.AppendLine($"  E1 cart.AddItemToInventory: added={added}, stored is same object={ReferenceEquals(stored, src)}, same GUID={stored != null && stored.UniqueId.Guid == g}, source count after={src.Count} IsEmpty={src.IsEmpty}, stored count={(stored != null ? stored.Count : -1)}");

                // E2: a consumer adds the cart's object to its own inventory first (crematorium/autopsy order), then
                // the cart removes it by GUID: does the cart's remove event fire?
                bool t = table.AddItemToInventory(stored);
                int evBefore = removedEvents;
                bool removed = cart.RemoveItemFromInventoryByUID(stored);
                bool stillThere = FindByGuid(cart, g) != null;
                sb.AppendLine($"  E2 table.AddItemToInventory(cartItem)={t}, cart item count after={stored.Count}; cart.RemoveItemFromInventoryByUID -> returned {removed}, entry still in cart={stillThere}, OnItemsRemove fired={(removedEvents > evBefore ? "yes" : "NO")}");
                table.RemoveItemFromInventoryByUID(stored);

                // E3: the take-from-table order (add to the cart first, remove from the table second), with and
                // without the count restore.
                Item body = new Item("wood", 1);
                table.AddItemToInventory(body);
                Item tableObj = FindByGuid(table, body.UniqueId.Guid);
                cart.AddItemToInventory(tableObj);                         // what the reroute would do inside AddOverheadItem
                int tableCountAfterSplit = tableObj.Count;
                bool tableRemovedNoRestore = table.RemoveItemFromInventoryByUID(tableObj); // what UIAutopsyWindowData.TakeBody does next
                bool tableEntryGone1 = FindByGuid(table, body.UniqueId.Guid) == null;
                sb.AppendLine($"  E3a without restore: table object count after the cart add={tableCountAfterSplit}; table.RemoveItemFromInventoryByUID returned {tableRemovedNoRestore} (false = no OnItemsRemove for the table), entry gone={tableEntryGone1}");
                Item cartCopy = FindByGuid(cart, body.UniqueId.Guid);
                if (cartCopy != null) cart.RemoveItemFromInventoryByUID(cartCopy);

                Item body2 = new Item("wood", 1);
                table.AddItemToInventory(body2);
                Item tableObj2 = FindByGuid(table, body2.UniqueId.Guid);
                cart.AddItemToInventory(tableObj2);
                tableObj2.Count = 1;                                       // the remedy: give the emptied shell its count back
                bool tableRemovedRestore = table.RemoveItemFromInventoryByUID(tableObj2);
                bool tableEntryGone2 = FindByGuid(table, body2.UniqueId.Guid) == null;
                Item cartCopy2 = FindByGuid(cart, body2.UniqueId.Guid);
                sb.AppendLine($"  E3b with restore: table.RemoveItemFromInventoryByUID returned {tableRemovedRestore} (true = event fired), entry gone={tableEntryGone2}; cart holds the item={cartCopy2 != null} count={(cartCopy2 != null ? cartCopy2.Count : -1)}");
                if (cartCopy2 != null) cart.RemoveItemFromInventoryByUID(cartCopy2);

                // E4: a drop keeps the reference for count 1 (what the mod's drop-at-keeper hands on)? Read-only note.
                sb.AppendLine($"  E4 (source) DropSystem.DropItem keeps the reference for count 1; RemoveItemFromInventoryByUID with count -1 removes the whole entry whatever the passed object's count.");
            }
            finally
            {
                cart.OnItemsRemove -= onRemove;
                Cargo.Resync("probe");
            }
        }

        private static Item FindByGuid(Inventory inv, Guid g)
        {
            foreach (Item it in inv.Data.Inventory)
            {
                if (it != null && it.UniqueId.Guid == g)
                {
                    return it;
                }
            }
            return null;
        }

        private static string Join(Dictionary<string, int> d)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in d)
            {
                parts.Add($"{kv.Key}×{kv.Value}");
            }
            return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }

    // ---------------------------------------------------------------------- dormant test patches (armed by the probe)

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.HasFreeOverheadSlot), MethodType.Getter)]
    internal static class PlayerData_HasFreeOverheadSlot_Probe
    {
        private static bool Prefix(ref bool __result)
        {
            if (HeadProbe.ForceNoFreeSlot)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.TryGetOverheadItem))]
    internal static class PlayerData_TryGetOverheadItem_Probe
    {
        private static bool Prefix(Predicate<Item> match, ref Item item, ref bool __result)
        {
            Item o = HeadProbe.LookupOverride;
            if (o != null && match != null && match(o))
            {
                item = o;
                __result = true;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(LazyExpressionEvaluationScope), nameof(LazyExpressionEvaluationScope.GetOverheadItemForMutation))]
    internal static class LazyExpressionEvaluationScope_GetOverheadItemForMutation_Probe
    {
        private static void Postfix(ref Item __result)
        {
            if (HeadProbe.MutationOverride != null)
            {
                __result = HeadProbe.MutationOverride;
            }
        }
    }
}

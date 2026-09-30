using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;

namespace GK2Mounts
{
    // Investigation aid for carrying cargo on the cart (Debug tab). Dumps the facts the source reading could not
    // settle statically: the donkey prefab's ConditionalDrawer rules (what switches the loaded-cart mesh on), the
    // donkey definition's inventory size and item filters, the real list of big (overhead) items, and the mesh
    // elements the keeper's overhead view can show. The test buttons put a plain item into the mounted donkey's
    // inventory and take it out again, to see whether the game's own drawer reacts. The mounted donkey is a temp
    // object, so nothing here touches the save; test items simply vanish.
    internal static class CarryProbe
    {
        public static string DumpFacts()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("GK2 Mounts carry facts " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();

                PlayerData pd = MainGame.PlayerData;
                if (pd != null)
                {
                    sb.AppendLine($"[player] overhead stack limit {pd.OverheadStackLimit} (extra_overhead={pd.ExtraOverhead}), carrying {pd.OverheadCount}: {Items(pd.OverheadItems)}");
                }

                WGODef def = GameBalance.Me.GetDataOrNull<WGODef>("npc_donkey");
                if (def == null)
                {
                    sb.AppendLine("[def] npc_donkey: not found");
                }
                else
                {
                    sb.AppendLine($"[def] npc_donkey inventorySize={def.inventorySize} craftInventorySize={def.craftInventorySize} isFuelContainer={def.isFuelContainer}");
                    sb.AppendLine($"[def] whitelist empty={def.inventoryWhiteList?.IsEmpty} items=[{Join(def.inventoryWhiteList?.itemsIds)}] groups=[{Join(def.inventoryWhiteList?.groupsIds)}]");
                    sb.AppendLine($"[def] blacklist empty={def.inventoryBlackList?.IsEmpty} items=[{Join(def.inventoryBlackList?.itemsIds)}] groups=[{Join(def.inventoryBlackList?.groupsIds)}]");
                    sb.AppendLine($"[def] startItems={def.startItems?.Count ?? 0} emptyCellStackCount={def.emptyCellStackCount}");
                }

                Wgo donkey = Mount.Donkey;
                if (donkey == null)
                {
                    sb.AppendLine("[mount] not mounted: drawer rules and inventory of our donkey not available (mount first)");
                }
                else
                {
                    Inventory inv = donkey.Data.Inventory;
                    sb.AppendLine($"[mount] our donkey inventory: size {inv.Data.InventorySize}, {inv.Data.Inventory.Count} items: {Items(inv.Data.Inventory)}; isTempObject={donkey.Data.isTempObject}");
                    DumpDrawers(donkey, sb);
                    DumpMeshStates(donkey, sb);
                }

                sb.AppendLine();
                sb.AppendLine("[items] itemSize == Big (id | iconId | stack | groups):");
                int big = 0;
                var bigIds = new HashSet<string>();
                foreach (ItemDef d in GameBalance.Me.itemDefs)
                {
                    if (d.itemSize == ItemSize.Big)
                    {
                        big++;
                        bigIds.Add(d.id);
                        sb.AppendLine($"  {d.id} | {d.iconId} | {d.stackCount} | {Join(d.itemGroupIds)}");
                    }
                }
                sb.AppendLine($"  total {big}");
                sb.AppendLine("[items] group 'overhead' but NOT Big:");
                foreach (ItemDef d in GameBalance.Me.itemDefs)
                {
                    if (d.itemGroupIds.Contains("overhead") && d.itemSize != ItemSize.Big)
                    {
                        sb.AppendLine($"  {d.id} | {d.iconId} | size {d.itemSize}");
                    }
                }

                sb.AppendLine();
                PlayerController pc = MainGame.PlayerController;
                if (pc != null)
                {
                    foreach (DropViewAtomMesh atom in pc.GetComponentsInChildren<DropViewAtomMesh>(includeInactive: true))
                    {
                        sb.AppendLine($"[overhead view] {Path(atom.transform)} isOverhead={atom.isOverhead} active={atom.gameObject.activeSelf}");
                        var names = new List<string>();
                        object elements = GetPrivate(atom, "elements");
                        if (elements is IEnumerable en)
                        {
                            foreach (object e in en)
                            {
                                if (e is Component c)
                                {
                                    names.Add(c.name);
                                }
                            }
                        }
                        sb.AppendLine($"  elements ({names.Count}): {string.Join(" ", names)}");
                        sb.AppendLine("  big items without an element here: " + MissingElements(bigIds, names));
                        sb.AppendLine("  mesh sizes per element (shared mesh bounds, element local space: size x/y/z | bottom y .. top y | element lscale):");
                        if (elements is IEnumerable en2)
                        {
                            foreach (object e in en2)
                            {
                                if (e is Component c)
                                {
                                    sb.AppendLine("    " + MeshExtent(c.transform));
                                    MeshDetails(c.transform, sb);
                                }
                            }
                        }
                        break;
                    }
                }

                sb.AppendLine();
                DumpPileLayouts(sb);

                string path = System.IO.Path.Combine(Paths.BepInExRootPath, "GK2-carry-facts.txt");
                File.WriteAllText(path, sb.ToString());
                return $"carry facts written to {path} ({big} big items; {(donkey == null ? "not mounted, drawer rules skipped" : "drawer rules included")})";
            }
            catch (Exception e)
            {
                return "dump failed: " + e;
            }
        }

        // Puts one plain item (no body properties) into the mounted donkey's inventory the way the delivery
        // script does (Inventory.AddItemToInventory) and reports what the prefab's drawer did with the cart meshes.
        public static string TestLoad(string itemId)
        {
            Wgo donkey = Mount.Donkey;
            if (donkey == null)
            {
                return "not mounted";
            }
            if (GameBalance.Me.GetDataOrNull<ItemDef>(itemId) == null)
            {
                return "unknown item id: " + itemId;
            }
            try
            {
                var item = new Item(itemId);
                bool ok = donkey.Data.Inventory.AddItemToInventory(item);
                var sb = new StringBuilder();
                sb.Append($"add '{itemId}' -> {(ok ? "accepted" : "REFUSED (filter or full)")}; inventory now {donkey.Data.Inventory.Data.Inventory.Count}/{donkey.Data.Inventory.Data.InventorySize}: {Items(donkey.Data.Inventory.Data.Inventory)} | ");
                DumpMeshStates(donkey, sb, oneLine: true);
                Mount.OnCargoChanged();
                return sb.ToString();
            }
            catch (Exception e)
            {
                return "test load failed: " + e;
            }
        }

        // Takes the first item back out (Inventory.RemoveItemById, as the conveyor cell does) and discards it.
        public static string TestUnload()
        {
            Wgo donkey = Mount.Donkey;
            if (donkey == null)
            {
                return "not mounted";
            }
            try
            {
                List<Item> list = donkey.Data.Inventory.Data.Inventory;
                if (list.Count == 0)
                {
                    return "cart inventory already empty";
                }
                string id = list[0].id;
                List<Item> removed = donkey.Data.Inventory.RemoveItemById(id, 1);
                var sb = new StringBuilder();
                sb.Append($"removed {removed.Count} x '{id}' (discarded, test only); inventory now {list.Count}/{donkey.Data.Inventory.Data.InventorySize} | ");
                DumpMeshStates(donkey, sb, oneLine: true);
                Mount.OnCargoChanged();
                return sb.ToString();
            }
            catch (Exception e)
            {
                return "test unload failed: " + e;
            }
        }

        // Every big (overhead) item id the game defines, minus the zombies, which are items linked to a zombie world
        // object and cannot be conjured from an id. Cached after the first call.
        private static List<string> bigIds;

        public static List<string> BigItemIds()
        {
            if (bigIds != null)
            {
                return bigIds;
            }
            var list = new List<string>();
            try
            {
                foreach (ItemDef d in GameBalance.Me.itemDefs)
                {
                    if (d.itemSize == ItemSize.Big && !d.itemGroupIds.Contains("zombie"))
                    {
                        list.Add(d.id);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: listing big items failed: " + e.Message);
            }
            list.Sort(string.CompareOrdinal);
            bigIds = list;
            return list;
        }

        // Puts one item on the ground in front of the keeper with the game's own drop routine (what Flow_DropItem and
        // the keeper's own drop key use), so every carryable item can be tried on the cart. A corpse is generated
        // from the same body definition the delivery script uses (body_0_1: parts, pockets, zombie skin), so it is a
        // real corpse. Test aid: the drop is a normal world drop and is saved like any other.
        public static string SpawnDrop(string itemId)
        {
            try
            {
                PlayerData pd = MainGame.PlayerData;
                if (pd == null || MainGame.Instance?.dropSystem == null)
                {
                    return "no game loaded";
                }
                ItemDef def = GameBalance.Me.GetDataOrNull<ItemDef>(itemId);
                if (def == null)
                {
                    return "unknown item id: " + itemId;
                }
                if (def.itemGroupIds.Contains("zombie"))
                {
                    return "zombies are world objects linked to their item; they cannot be spawned from an id";
                }
                Item item;
                if (def.itemGroupIds.Contains("body"))
                {
                    BodyDef body = GameBalance.Me.GetDataOrNull<BodyDef>("body_0_1");
                    item = body != null ? body.GenerateItem() : new Item(itemId);
                }
                else
                {
                    item = new Item(itemId);
                }
                Vector3 pos;
                if (!SpecialPhysicsCastUtils.GetPlayerDropPosition(pd.position.Value, pd.Direction, out pos))
                {
                    pos = pd.position.Value + new Vector3(pd.Direction.x, 0f, pd.Direction.y);
                }
                bool ok = MainGame.Instance.dropSystem.DropItem(item, pd.currentGameSceneId, pos);
                return $"{(ok ? "dropped" : "drop REFUSED")} {item.id} (size {def.itemSize}, icon {def.iconId}) at {pos}";
            }
            catch (Exception e)
            {
                return "spawn failed: " + e;
            }
        }

        private static void DumpDrawers(Wgo donkey, StringBuilder sb)
        {
            ConditionalDrawer[] drawers = donkey.GetComponentsInChildren<ConditionalDrawer>(includeInactive: true);
            sb.AppendLine($"[drawer] {drawers.Length} ConditionalDrawer component(s) on our donkey");
            foreach (ConditionalDrawer cd in drawers)
            {
                sb.AppendLine($"[drawer] on {Path(cd.transform)}: {cd.Rules.Count} rule(s)");
                int i = 0;
                foreach (ConditionalDrawerRule rule in cd.Rules)
                {
                    i++;
                    sb.AppendLine($"  rule {i}: event {rule.EventType}");
                    sb.AppendLine("    condition: " + Describe(rule.condition));
                    foreach (IConditionalDrawerAction a in rule.actions)
                    {
                        sb.AppendLine("    action: " + Describe(a));
                    }
                }
            }
        }

        private static readonly string[] MeshNames =
        {
            "donkey_cart_empty_sway", "donkey_cart_empty", "donkey_cart_body_sway", "donkey_cart_body", "drops_item_corpse_shroud",
        };

        private static void DumpMeshStates(Wgo donkey, StringBuilder sb, bool oneLine = false)
        {
            var parts = new List<string>();
            foreach (Transform t in donkey.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (Array.IndexOf(MeshNames, t.name) >= 0)
                {
                    parts.Add($"{t.name}={(t.gameObject.activeSelf ? "on" : "off")}{(t.gameObject.activeInHierarchy ? "" : "(hidden)")}");
                }
            }
            if (oneLine)
            {
                sb.Append("meshes: ").Append(string.Join(" ", parts));
            }
            else
            {
                sb.AppendLine("[meshes] " + string.Join(" ", parts));
            }
        }

        // Type name plus every public field, one level deep; Unity objects by name, lists by their elements.
        private static string Describe(object o)
        {
            if (o == null)
            {
                return "null";
            }
            var sb = new StringBuilder(o.GetType().Name).Append(" {");
            foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object v;
                try
                {
                    v = f.GetValue(o);
                }
                catch
                {
                    continue;
                }
                sb.Append(' ').Append(f.Name).Append('=').Append(Value(v));
            }
            return sb.Append(" }").ToString();
        }

        private static string Value(object v)
        {
            switch (v)
            {
                case null:
                    return "null";
                case GameObject go:
                    return $"GameObject[{Path(go.transform)} active={go.activeSelf}]";
                case Component c:
                    return $"{c.GetType().Name}[{Path(c.transform)}]";
                case string s:
                    return "\"" + s + "\"";
                case IEnumerable en when !(v is string):
                {
                    var items = new List<string>();
                    foreach (object e in en)
                    {
                        items.Add(Value(e));
                    }
                    return "[" + string.Join(", ", items) + "]";
                }
                default:
                    return v.ToString();
            }
        }

        private static object GetPrivate(object o, string field)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    return f.GetValue(o);
                }
            }
            return null;
        }

        // Union of the mesh bounds under an element, expressed in the element's own space (mesh bounds are valid on
        // inactive objects, renderer bounds are not). Tells how big each big item is and where its pivot sits.
        private static string MeshExtent(Transform element)
        {
            bool any = false;
            Bounds union = default;
            foreach (MeshFilter mf in element.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }
                Bounds b = mesh.bounds;
                Vector3[] corners =
                {
                    new Vector3(b.min.x, b.min.y, b.min.z), new Vector3(b.max.x, b.min.y, b.min.z), new Vector3(b.min.x, b.max.y, b.min.z), new Vector3(b.max.x, b.max.y, b.min.z),
                    new Vector3(b.min.x, b.min.y, b.max.z), new Vector3(b.max.x, b.min.y, b.max.z), new Vector3(b.min.x, b.max.y, b.max.z), new Vector3(b.max.x, b.max.y, b.max.z),
                };
                foreach (Vector3 corner in corners)
                {
                    Vector3 p = element.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any)
                    {
                        union = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        union.Encapsulate(p);
                    }
                }
            }
            if (!any)
            {
                return element.name + ": no mesh";
            }
            return $"{element.name}: {union.size.x:0.00}/{union.size.y:0.00}/{union.size.z:0.00} | {union.min.y:0.00} .. {union.max.y:0.00} | centre ({union.center.x:0.00}, {union.center.z:0.00}) | lscale {element.localScale}";
        }

        // What the element is made of: every mesh under it with its name (the game's suffixes mark shadow meshes),
        // vertex and triangle counts, its transform relative to the element, and a vertex-height histogram of the
        // non-shadow meshes (8 bins from bottom to top) that shows the shape: a lying log fills every bin evenly, a
        // flat plane tilted for the camera piles up at the ends.
        private static void MeshDetails(Transform element, StringBuilder sb)
        {
            var heights = new List<float>();
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (MeshFilter mf in element.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }
                bool shadow = Cargo.IsShadowMesh(mf.gameObject.name);
                Vector3 lp = element.InverseTransformPoint(mf.transform.position);
                Quaternion lr = Quaternion.Inverse(element.rotation) * mf.transform.rotation;
                string topo = "?";
                try
                {
                    topo = $"{mesh.GetTopology(0)} x{mesh.GetIndexCount(0)} idx, {mesh.subMeshCount} submesh";
                }
                catch
                {
                }
                sb.AppendLine($"      mesh {mf.gameObject.name}{(shadow ? " [shadow]" : "")}: {mesh.vertexCount} verts, {topo}, readable={mesh.isReadable}, raw bounds {mesh.bounds.size}, at {lp}, rot {lr.eulerAngles}, lossy scale {mf.transform.lossyScale}");
                if (shadow || !mesh.isReadable)
                {
                    continue; // vertex data of a GPU-only mesh reads back empty
                }
                foreach (Vector3 v in mesh.vertices)
                {
                    float y = element.InverseTransformPoint(mf.transform.TransformPoint(v)).y;
                    heights.Add(y);
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
            }
            if (heights.Count == 0 || maxY - minY < 0.001f)
            {
                return;
            }
            var bins = new int[8];
            foreach (float y in heights)
            {
                int b = Mathf.Clamp((int)((y - minY) / (maxY - minY) * 8f), 0, 7);
                bins[b]++;
            }
            sb.AppendLine($"      vertex heights {minY:0.00}..{maxY:0.00}, share per eighth (bottom to top): {string.Join(" ", Array.ConvertAll(bins, n => (100 * n / heights.Count).ToString()))} %");
        }

        // The game's own answer to "several big items on one object": every conditional drawer in the loaded scenes
        // whose action is UpdateDropViewsFromInventory (pallets, piles), with the world object it belongs to, its
        // inventory, and each mesh slot's transform relative to the object's root.
        private static void DumpPileLayouts(StringBuilder sb)
        {
            ConditionalDrawer[] drawers;
            try
            {
                drawers = UnityEngine.Object.FindObjectsByType<ConditionalDrawer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            }
            catch (Exception e)
            {
                sb.AppendLine("[piles] scan failed: " + e.Message);
                return;
            }
            int found = 0, drops = 0;
            var seenIds = new HashSet<string>();
            foreach (ConditionalDrawer cd in drawers)
            {
                bool inventoryDriven = false;
                foreach (ConditionalDrawerRule rule in cd.Rules)
                {
                    if (rule.condition is ItemCountCondition || rule.condition is ItemGroupCountCondition || rule.condition is TotalItemsCountCondition || rule.condition is ItemGroupInventoryIndexCondition)
                    {
                        inventoryDriven = true;
                    }
                    foreach (IConditionalDrawerAction a in rule.actions)
                    {
                        if (a is UpdateDropViewsFromInventory)
                        {
                            inventoryDriven = true;
                            drops++;
                        }
                    }
                }
                if (!inventoryDriven)
                {
                    continue;
                }
                Wgo wgo = cd.GetComponentInParent<Wgo>();
                string id = wgo?.Data?.id ?? cd.name;
                if (id == "npc_donkey" || !seenIds.Add(id))
                {
                    continue; // one example per object kind; the donkey is dumped above
                }
                found++;
                Transform root = wgo != null ? wgo.transform : cd.transform;
                string items = wgo?.Data != null ? Items(wgo.Data.Inventory.Data.Inventory) : "-";
                int size = wgo?.Data != null ? wgo.Data.Inventory.Data.InventorySize : -1;
                sb.AppendLine($"[piles] {id} at {root.position}: inventory {size} slots [{items}], interaction {wgo?.Data?.Definition?.interactionType}, {cd.Rules.Count} rule(s):");
                foreach (ConditionalDrawerRule rule in cd.Rules)
                {
                    sb.AppendLine("    if " + Describe(rule.condition));
                    foreach (IConditionalDrawerAction a in rule.actions)
                    {
                        switch (a)
                        {
                            case SetActiveAction sa:
                                sb.AppendLine($"      SetActive{(sa.invertLogic ? " (inverted)" : "")} {TargetInfo(sa.target, root)}");
                                break;
                            case SetActiveManyAction sm:
                                foreach (GameObject g in sm.targets ?? new List<GameObject>())
                                {
                                    sb.AppendLine($"      SetActiveMany{(sm.invertLogic ? " (inverted)" : "")} {TargetInfo(g, root)}");
                                }
                                break;
                            case ApplyVisualStateAction vs:
                                sb.AppendLine($"      ApplyVisualState '{vs.stateId}'{(vs.applyOnFalse ? " on false" : "")}");
                                break;
                            case UpdateDropViewsFromInventory u:
                                sb.AppendLine($"      UpdateDropViewsFromInventory, {u.bigDrops.Count} mesh slots:");
                                for (int i = 0; i < u.bigDrops.Count; i++)
                                {
                                    DropViewAtomMesh d = u.bigDrops[i];
                                    sb.AppendLine(d == null ? $"        slot {i}: null" : $"        slot {i}: {TargetInfo(d.gameObject, root)} icon={d.IconId ?? "-"}");
                                }
                                break;
                            default:
                                sb.AppendLine("      " + Describe(a));
                                break;
                        }
                    }
                }
                WgoPart part = wgo?.MainWgoPart;
                if (part != null && part.Variations != null && part.Variations.Count > 0)
                {
                    sb.AppendLine($"    visual states ({part.Variations.Count}), current '{part.WgoPartData?.variationId}':");
                    foreach (WgoPartState v in part.Variations)
                    {
                        sb.AppendLine($"      '{v.variationId}' rot {v.rotationIndex}{(v.isDefault ? " default" : "")} {TargetInfo(v.gameObject, root)}");
                    }
                }
            }
            sb.AppendLine($"[piles] {found} inventory-driven drawer(s) (one per object kind) in the loaded scenes, {drops} with UpdateDropViewsFromInventory. Stand near the pallets or wood piles when dumping.");
        }

        // A drawer target relative to its object's root: path, position, rotation, active state and the size and
        // bottom of the meshes under it.
        private static string TargetInfo(GameObject g, Transform root)
        {
            if (g == null)
            {
                return "null";
            }
            Vector3 rel = root.InverseTransformPoint(g.transform.position);
            Vector3 rot = (Quaternion.Inverse(root.rotation) * g.transform.rotation).eulerAngles;
            string extent = "";
            if (Cargo.MeasureMeshes(g.transform, g.transform, out Bounds b, includeInactive: true))
            {
                extent = $" mesh {b.size.x:0.00}/{b.size.y:0.00}/{b.size.z:0.00} bottom {b.min.y:0.00} top {b.max.y:0.00} centre ({b.center.x:0.00}, {b.center.z:0.00})";
            }
            return $"{Path(g.transform)} rel ({rel.x:0.00}, {rel.y:0.00}, {rel.z:0.00}) rot ({rot.x:0}, {rot.y:0}, {rot.z:0}) active={g.activeSelf}{extent}";
        }

        private static string MissingElements(HashSet<string> bigIds, List<string> elementNames)
        {
            var missing = new List<string>();
            foreach (ItemDef d in GameBalance.Me.itemDefs)
            {
                if (d.itemSize == ItemSize.Big && !elementNames.Contains(d.iconId))
                {
                    missing.Add(d.id + "(" + d.iconId + ")");
                }
            }
            return missing.Count == 0 ? "none" : string.Join(" ", missing);
        }

        private static string Items(IEnumerable<Item> items)
        {
            if (items == null)
            {
                return "-";
            }
            var l = new List<string>();
            foreach (Item it in items)
            {
                l.Add($"{it.id}x{it.Count}");
            }
            return l.Count == 0 ? "(none)" : string.Join(" ", l);
        }

        private static string Join(IEnumerable<string> list)
        {
            return list == null ? "" : string.Join(" ", list);
        }

        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null && p.parent != null; p = p.parent)
            {
                sb.Insert(0, p.name + "/");
            }
            return sb.ToString();
        }
    }
}

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
                                }
                            }
                        }
                        break;
                    }
                }

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

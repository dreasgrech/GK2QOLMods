using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2OwnedCount
{
    // Private game members the mod reads. Resolved once; a missing one throws in Verify() and disables the mod.
    internal static class Access
    {
        public static readonly AccessTools.FieldRef<UITooltip> TooltipInstance =
            AccessTools.StaticFieldRefAccess<UITooltip>(AccessTools.Field(typeof(UITooltip), "instance"));

        public static readonly AccessTools.FieldRef<UITooltip, TextStyle> SmallDescriptionStyle =
            AccessTools.FieldRefAccess<UITooltip, TextStyle>("smallDescriptionTextStyle");

        public static void Verify()
        {
            if (TooltipInstance == null || SmallDescriptionStyle == null)
            {
                throw new InvalidOperationException("A game member this mod relies on was not found.");
            }
        }
    }

    internal sealed class Totals
    {
        public int OnPlayer;
        public int InStorage;
        public int ChestsWithItem;
        public int OtherGrades;           // total across the item's star group, this item included; -1 when not graded
        public readonly List<KeyValuePair<string, int>> PerArea = new List<KeyValuePair<string, int>>();
        public int Total => OnPlayer + InStorage;
    }

    // Read-only sums over the player's inventories and every storage chest. Nothing here writes game state.
    internal static class Counter
    {
        private static readonly List<Inventory> storage = new List<Inventory>();
        private static readonly List<string> storageZone = new List<string>();
        private static int storageFrame = -1;
        private static WorldData storageWorld;

        private static Dictionary<ItemDef, List<ItemDef>> gradeFamily;
        private static int gradeFamilyCount = -1;

        public static Totals Count(ItemDef def)
        {
            Totals t = new Totals { OtherGrades = -1 };
            if (def == null || string.IsNullOrEmpty(def.id))
            {
                return t;
            }
            string id = def.id;
            PlayerData player = MainGame.PlayerData;
            t.OnPlayer = CountIn(player?.Inventory, id) + CountIn(player?.toolBeltInventory, id);

            RefreshStorage();
            Dictionary<string, int> perArea = null;
            for (int i = 0; i < storage.Count; i++)
            {
                int n = CountIn(storage[i], id);
                if (n <= 0)
                {
                    continue;
                }
                t.InStorage += n;
                t.ChestsWithItem++;
                if (Plugin.ShowPerArea.Value)
                {
                    perArea = perArea ?? new Dictionary<string, int>();
                    perArea.TryGetValue(storageZone[i], out int had);
                    perArea[storageZone[i]] = had + n;
                }
            }
            if (perArea != null)
            {
                foreach (KeyValuePair<string, int> kv in perArea)
                {
                    t.PerArea.Add(kv);
                }
                t.PerArea.Sort((a, b) => b.Value.CompareTo(a.Value));
            }

            if (Plugin.IncludeOtherGrades.Value)
            {
                List<ItemDef> family = GradeFamily(def);
                if (family != null && family.Count > 1)
                {
                    int sum = 0;
                    foreach (ItemDef other in family)
                    {
                        if (other == null || string.IsNullOrEmpty(other.id))
                        {
                            continue;
                        }
                        if (other.id == id)
                        {
                            sum += t.Total;
                            continue;
                        }
                        sum += CountIn(player?.Inventory, other.id) + CountIn(player?.toolBeltInventory, other.id);
                        for (int i = 0; i < storage.Count; i++)
                        {
                            sum += CountIn(storage[i], other.id);
                        }
                    }
                    t.OtherGrades = sum;
                }
            }
            return t;
        }

        // Item.GetTotalCountInInventory walks bags inside the inventory as well.
        private static int CountIn(Inventory inv, string id)
        {
            Item data = inv?.Data;
            return data == null ? 0 : data.GetTotalCountInInventory(id);
        }

        // Same set Global Storage treats as storage: active container zones, objects opened in the multi-inventory,
        // non-zero size, not hidden; fuel containers and conveyor chests by setting. Cached per frame.
        private static void RefreshStorage()
        {
            WorldData world = MainGame.Instance?.GameSave?.worldData;
            if (storageFrame == Time.frameCount && ReferenceEquals(storageWorld, world))
            {
                return;
            }
            storageFrame = Time.frameCount;
            storageWorld = world;
            storage.Clear();
            storageZone.Clear();
            if (world?.gameSceneDataList == null)
            {
                return;
            }
            HashSet<Item> seen = new HashSet<Item>();
            foreach (GameSceneData scene in world.gameSceneDataList)
            {
                if (scene?.worldZones == null)
                {
                    continue;
                }
                foreach (WorldZoneData zone in scene.worldZones)
                {
                    if (zone?.wgoDataList == null || !zone.IsContainer || !zone.IsActive)
                    {
                        continue;
                    }
                    foreach (SGuid guid in zone.wgoDataList)
                    {
                        WgoData wgo = world.GetWgoData(guid);
                        if (!IsStorage(wgo))
                        {
                            continue;
                        }
                        Inventory inv = wgo.Inventory;
                        if (seen.Add(inv.Data))
                        {
                            storage.Add(inv);
                            storageZone.Add(ZoneName(zone));
                        }
                    }
                }
            }
        }

        private static bool IsStorage(WgoData wgo)
        {
            WGODef def = wgo?.Definition;
            if (def == null || def.inventorySize == 0 || !def.OpenInMultiInventory)
            {
                return false;
            }
            if (wgo.IsHidden || !wgo.IsInteractable)
            {
                return false;
            }
            if (!Plugin.IncludeConveyorStorage.Value && def.conveyorType != ConveyorElementType.None)
            {
                return false;
            }
            Inventory inv = wgo.Inventory;
            if (inv?.Data == null)
            {
                return false;
            }
            if (!Plugin.IncludeFuelContainers.Value && inv.Data.HasProperty<FuelContainerSerializedItemProperty>())
            {
                return false;
            }
            return true;
        }

        private static string ZoneName(WorldZoneData zone)
        {
            string id = zone.id ?? "";
            try
            {
                if (LLBase.HasL(id))
                {
                    string name = LLBase.L(id);
                    if (!string.IsNullOrEmpty(name) && !name.StartsWith("[", StringComparison.Ordinal))
                    {
                        return name;
                    }
                }
            }
            catch (Exception)
            {
            }
            return id;
        }

        // A graded item's family. The game has two groupings, and neither alone covers all cases:
        //  - starGroupItemsCache, keyed by the id before ':' (pumpkin:1..3 share "pumpkin"; body parts like
        //    bones_1_1:2 are alone in theirs because each grade has one star level);
        //  - item groups named gr_* (gr_bones holds all six bones grades, gr_stories all stories, ...).
        // The family is the union of both, cached per definition.
        private static List<ItemDef> GradeFamily(ItemDef def)
        {
            GameBalance gb = GameBalance.Me;
            if (gb == null || def.qualityType != ItemDef.QualityType.Star)
            {
                return null;
            }
            int signature = (gb.starGroupItemsCache?.Count ?? 0) * 31 + (gb.groupItemsCache?.Count ?? 0);
            if (gradeFamily == null || gradeFamilyCount != signature)
            {
                gradeFamily = new Dictionary<ItemDef, List<ItemDef>>();
                gradeFamilyCount = signature;
            }
            if (gradeFamily.TryGetValue(def, out List<ItemDef> cached))
            {
                return cached;
            }
            HashSet<ItemDef> members = new HashSet<ItemDef> { def };
            string baseId = def.id.Split(':')[0];
            if (gb.starGroupItemsCache != null && gb.starGroupItemsCache.TryGetValue(baseId, out List<ItemDef> star) && star != null)
            {
                members.UnionWith(star);
            }
            if (def.itemGroupIds != null && gb.groupItemsCache != null)
            {
                foreach (string group in def.itemGroupIds)
                {
                    if (group != null && group.StartsWith("gr_", StringComparison.Ordinal) &&
                        gb.groupItemsCache.TryGetValue(group, out List<ItemDef> grouped) && grouped != null)
                    {
                        foreach (ItemDef g in grouped)
                        {
                            if (g != null && g.qualityType == ItemDef.QualityType.Star)
                            {
                                members.Add(g);
                            }
                        }
                    }
                }
            }
            List<ItemDef> family = new List<ItemDef>(members);
            gradeFamily[def] = family;
            return family;
        }
    }

    // Debug: one CSV of every item definition, written once the game data exists.
    internal static class ItemDefDump
    {
        public static bool Done;

        public static void TryWrite()
        {
            List<ItemDef> defs = GameBalance.Me?.itemDefs;
            if (defs == null || defs.Count == 0)
            {
                return;
            }
            Done = true;
            try
            {
                string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "GK2-itemdefs.csv");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("id,name,type,stack_count,quality,quality_type,red_skulls,white_skulls,is_fuel,groups");
                foreach (ItemDef d in defs)
                {
                    if (d == null)
                    {
                        continue;
                    }
                    string name;
                    try { name = d.GetHeader(); } catch (Exception) { name = ""; }
                    sb.Append(Csv(d.id)).Append(',').Append(Csv(name)).Append(',').Append(d.type).Append(',')
                      .Append(d.stackCount).Append(',').Append(d.quality).Append(',').Append(d.qualityType).Append(',')
                      .Append(d.redSkulls).Append(',').Append(d.whiteSkulls).Append(',').Append(d.isFuel).Append(',')
                      .Append(Csv(d.itemGroupIds == null ? "" : string.Join(";", d.itemGroupIds))).AppendLine();
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                Plugin.Log.LogInfo($"Wrote {defs.Count} item definitions to {path}.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Item definition dump failed: {e.Message}");
            }
        }

        private static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GK2GlobalStorage
{
    [BepInPlugin(Guid, "GK2 Global Storage", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.globalstorage";
        public const string Version = "1.1.3"; // one place: the plugin attribute and the log line

        // Module version id of the Assembly-CSharp.dll this plugin was audited against (Steam build 25601286).
        private static readonly System.Guid AuditedGameMvid = new System.Guid("3ffadd70-169d-402a-bcdf-59c322dfc26d");
        // And of LazyBearTechnology.dll (the engine library the plugin also uses), same build (Steam 25601286).
        private static readonly System.Guid AuditedLazyBearMvid = new System.Guid("8a7a5e04-396d-4525-a779-3f0de1341398");

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> IncludeBuildAndAlchemy;
        internal static ConfigEntry<bool> IncludeFuelContainers;
        internal static ConfigEntry<bool> IncludeConveyorStorage;
        internal static ConfigEntry<bool> IncludeZombieOrderDelivery;
        internal static ConfigEntry<bool> DisableOnGameUpdate;
        internal static ConfigEntry<bool> VerboseLogging;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true,
                "Player crafting at workbenches can use materials from storage chests in every zone.");
            IncludeBuildAndAlchemy = Config.Bind("General", "IncludeBuildAndAlchemy", true,
                "Build desks, town buildings, alchemy, gardens, graves, autopsy/embalm pickers and requirement tooltips also use storage in every zone.");
            IncludeFuelContainers = Config.Bind("Storage", "IncludeFuelContainers", false,
                "Also draw from fuel containers (e.g. furnace wood piles) in OTHER zones. Off by default so remote furnaces are not starved.");
            IncludeConveyorStorage = Config.Bind("Storage", "IncludeConveyorStorage", false,
                "Also draw from conveyor chests in OTHER zones. Off by default so remote conveyor automation is not starved.");
            IncludeZombieOrderDelivery = Config.Bind("Storage", "IncludeZombieOrderDelivery", false,
                "When you hand-deliver items a zombie crafter asked for, also take them from storage in every zone.");
            DisableOnGameUpdate = Config.Bind("Safety", "DisableOnGameUpdate", true,
                "Do not patch anything if the game's code has changed since this mod was audited. Rebuild/re-audit after game updates.");
            VerboseLogging = Config.Bind("Debug", "VerboseLogging", false,
                "Log which storage inventories were discovered (useful for troubleshooting).");

            System.Guid gameMvid = typeof(WgoData).Assembly.ManifestModule.ModuleVersionId;
            System.Guid lazyBearMvid = typeof(LazyBearTechnology.LazyAudio).Assembly.ManifestModule.ModuleVersionId;
            if (gameMvid != AuditedGameMvid || lazyBearMvid != AuditedLazyBearMvid)
            {
                if (DisableOnGameUpdate.Value)
                {
                    Log.LogError($"Game code changed since this mod was audited (Assembly-CSharp {gameMvid}, expected {AuditedGameMvid}; LazyBearTechnology {lazyBearMvid}, expected {AuditedLazyBearMvid}). Mod is DISABLED; the game runs unmodified. Set Safety.DisableOnGameUpdate=false to force-enable at your own risk.");
                    return;
                }
                Log.LogWarning($"Game code changed since this mod was audited (Assembly-CSharp {gameMvid}, LazyBearTechnology {lazyBearMvid}). Running anyway because DisableOnGameUpdate=false.");
            }

            // All-or-nothing: a partial patch set could leave the global lookup active without its guards.
            Harmony harmony = new Harmony(Guid);
            try
            {
                harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                Log.LogError($"Patching failed; all patches removed, game runs unmodified. {e}");
                return;
            }
            Log.LogInfo($"GK2 Global Storage {Version} loaded.");
        }
    }

    internal sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>
    /// Storage inventories in every zone: exactly the set the game itself treats as zone storage
    /// (active container zones, OpenInMultiInventory, non-zero size), minus hidden/non-interactable
    /// objects and, by default, fuel containers and conveyor chests.
    /// </summary>
    internal static class GlobalStorage
    {
        private static readonly List<Inventory> cache = new List<Inventory>();
        private static int cacheFrame = -1;
        private static WorldData cacheWorld;
        private static int lastLoggedCount = -1;

        // Which backing item data each MultiInventory got from us, so the Sort patch can keep them last.
        internal static readonly ConditionalWeakTable<MultiInventory, HashSet<object>> Appended =
            new ConditionalWeakTable<MultiInventory, HashSet<object>>();

        // Scoped state set by patches on specific call sites (main thread only).
        [ThreadStatic] internal static int SuppressDepth;
        [ThreadStatic] internal static int PickerDepth;
        [ThreadStatic] internal static Func<Item, bool> PickerFilter;

        public static List<Inventory> GetAll()
        {
            WorldData worldData = MainGame.Instance?.GameSave?.worldData;
            if (cacheFrame == Time.frameCount && ReferenceEquals(cacheWorld, worldData))
            {
                return cache;
            }
            cacheFrame = Time.frameCount;
            cacheWorld = worldData;
            cache.Clear();
            if (worldData?.gameSceneDataList == null)
            {
                return cache;
            }

            HashSet<object> seen = new HashSet<object>(ReferenceComparer.Instance);
            foreach (GameSceneData scene in worldData.gameSceneDataList)
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
                        WgoData wgoData = worldData.GetWgoData(guid);
                        if (!IsEligible(wgoData))
                        {
                            continue;
                        }
                        Inventory inventory = wgoData.Inventory;
                        if (seen.Add(inventory.Data))
                        {
                            cache.Add(inventory);
                        }
                    }
                }
            }

            if (Plugin.VerboseLogging.Value && cache.Count != lastLoggedCount)
            {
                lastLoggedCount = cache.Count;
                Plugin.Log.LogInfo($"Discovered {cache.Count} eligible storage inventories across all zones.");
            }
            return cache;
        }

        private static bool IsEligible(WgoData wgoData)
        {
            WGODef def = wgoData?.Definition;
            if (def == null || def.inventorySize == 0 || !def.OpenInMultiInventory)
            {
                return false;
            }
            if (wgoData.IsHidden || !wgoData.IsInteractable)
            {
                return false;
            }
            if (!Plugin.IncludeConveyorStorage.Value && def.conveyorType != ConveyorElementType.None)
            {
                return false;
            }
            Inventory inventory = wgoData.Inventory;
            if (inventory?.Data == null)
            {
                return false;
            }
            if (!Plugin.IncludeFuelContainers.Value && inventory.Data.HasProperty<FuelContainerSerializedItemProperty>())
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Appends storage after the inventories already present (dedupe by backing data), so the
        /// player's inventory and local chests are still drained first.
        /// </summary>
        public static void AppendTo(MultiInventory multiInventory)
        {
            if (multiInventory?.inventoryList == null)
            {
                return;
            }
            HashSet<object> present = new HashSet<object>(ReferenceComparer.Instance);
            foreach (Inventory inventory in multiInventory.inventoryList)
            {
                if (inventory?.Data != null)
                {
                    present.Add(inventory.Data);
                }
            }
            HashSet<object> appended = null;
            bool inPicker = PickerDepth > 0;
            foreach (Inventory inventory in GetAll())
            {
                if (present.Contains(inventory.Data))
                {
                    continue;
                }
                if (inPicker && !ContainsMatchingItem(inventory.Data, PickerFilter, 0))
                {
                    continue;
                }
                present.Add(inventory.Data);
                if (appended == null)
                {
                    appended = Appended.GetValue(multiInventory, _ => new HashSet<object>(ReferenceComparer.Instance));
                }
                appended.Add(inventory.Data);
                multiInventory.inventoryList.Add(inventory);
            }
        }

        // Item pickers draw one panel per inventory; only show remote chests that hold something pickable.
        private static bool ContainsMatchingItem(Item container, Func<Item, bool> filter, int depth)
        {
            List<Item> items = container?.Inventory;
            if (items == null || depth > 3)
            {
                return false;
            }
            foreach (Item item in items)
            {
                if (item == null || item.IsEmpty)
                {
                    continue;
                }
                bool match;
                try
                {
                    match = filter == null || filter(item);
                }
                catch
                {
                    match = false;
                }
                if (match || ContainsMatchingItem(item, filter, depth + 1))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Workbench crafting: availability checks, UI counts and material removal all go through this.
    /// Only when the player is the worker. Zombie and conveyor crafting are untouched: their
    /// overrides (ZombieWgoData/ConveyorWgoData) never call this base method, and zombie-worked
    /// benches fail the worker check.
    /// </summary>
    [HarmonyPatch(typeof(WgoData), nameof(WgoData.GetCraftableMultiInventory))]
    internal static class WgoData_GetCraftableMultiInventory_Patch
    {
        private static void Postfix(WgoData __instance, bool excludeWorkerInventory, MultiInventory __result)
        {
            try
            {
                if (!Plugin.Enabled.Value || excludeWorkerInventory || !(__instance.Worker is PlayerController))
                {
                    return;
                }
                GlobalStorage.AppendTo(__result);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"GetCraftableMultiInventory patch failed: {e}");
            }
        }
    }

    /// <summary>
    /// new MultiInventory(PlayerData, addCurrentPlayerWorldZone) backs the build desk, town buildings,
    /// alchemy, item pickers, garden seed counts and tooltips. Respects addCurrentPlayerWorldZone=false
    /// (the game's "pocket only" pickers, e.g. zombie equipment) and scoped suppressions below.
    /// </summary>
    [HarmonyPatch(typeof(MultiInventory), MethodType.Constructor, new Type[] { typeof(PlayerData), typeof(bool) })]
    internal static class MultiInventory_PlayerCtor_Patch
    {
        private static void Postfix(MultiInventory __instance, PlayerData playerData, bool addCurrentPlayerWorldZone)
        {
            try
            {
                if (!Plugin.Enabled.Value || !Plugin.IncludeBuildAndAlchemy.Value || !addCurrentPlayerWorldZone
                    || GlobalStorage.SuppressDepth > 0 || playerData == null || playerData != MainGame.PlayerData)
                {
                    return;
                }
                GlobalStorage.AppendTo(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"MultiInventory(PlayerData) patch failed: {e}");
            }
        }
    }

    /// <summary>
    /// Callers that Add() after construction re-run the game's Sort(), an unstable introsort that
    /// scrambles order once a list exceeds 16 inventories. For lists we extended, sort stably with
    /// the same rule (non-fuel first, fuel by ascending size) and keep our additions after local ones.
    /// Lists we never touched use the game's original Sort.
    /// </summary>
    [HarmonyPatch(typeof(MultiInventory), "Sort")]
    internal static class MultiInventory_Sort_Patch
    {
        private static bool Prefix(MultiInventory __instance)
        {
            try
            {
                if (!GlobalStorage.Appended.TryGetValue(__instance, out HashSet<object> appended))
                {
                    return true;
                }
                List<Inventory> list = __instance.inventoryList;
                if (list == null || list.Any(inv => inv?.Data == null))
                {
                    return true;
                }
                // A caller Add()ing an inventory we already appended would otherwise be counted twice
                // (HasItemsById over-counts, RemoveItems under-removes). Keep the first occurrence.
                HashSet<object> seen = new HashSet<object>(ReferenceComparer.Instance);
                List<(Inventory inv, int index, bool fuel)> distinct = new List<(Inventory, int, bool)>(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    if (seen.Add(list[i].Data))
                    {
                        distinct.Add((list[i], i, list[i].Data.HasProperty<FuelContainerSerializedItemProperty>()));
                    }
                    else if (Plugin.VerboseLogging.Value)
                    {
                        Plugin.Log.LogInfo("Removed a duplicate inventory from an extended MultiInventory.");
                    }
                }
                List<Inventory> sorted = distinct
                    .OrderBy(e => e.fuel ? 1 : 0)
                    .ThenBy(e => !e.fuel && appended.Contains(e.inv.Data) ? 1 : 0)
                    .ThenBy(e => e.fuel ? e.inv.Data.InventorySize : 0)
                    .ThenBy(e => e.index)
                    .Select(e => e.inv)
                    .ToList();
                list.Clear();
                list.AddRange(sorted);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"MultiInventory.Sort patch failed, using game sort: {e}");
                return true;
            }
        }
    }

    /// <summary>
    /// Item picker window: remember its item filter so remote chests without pickable items are not
    /// drawn as extra panels.
    /// </summary>
    [HarmonyPatch(typeof(UIMultiInventoryWindowData), MethodType.Constructor,
        new Type[] { typeof(PlayerData), typeof(Action<UIItemCell>), typeof(Func<Item, bool>), typeof(bool), typeof(string), typeof(Func<Item, string>) })]
    internal static class UIMultiInventoryWindowData_Ctor_Patch
    {
        private static void Prefix(Func<Item, bool> itemsAvailableCondition, out Func<Item, bool> __state)
        {
            __state = GlobalStorage.PickerFilter;
            GlobalStorage.PickerDepth++;
            GlobalStorage.PickerFilter = itemsAvailableCondition;
        }

        private static Exception Finalizer(Exception __exception, Func<Item, bool> __state)
        {
            GlobalStorage.PickerDepth = Math.Max(0, GlobalStorage.PickerDepth - 1);
            GlobalStorage.PickerFilter = __state;
            return __exception;
        }
    }

    /// <summary>
    /// Unequipping zombie gear returns it via new MultiInventory(PlayerData).AddItem; keep that
    /// going to pocket/local zone only, as in the unmodified game.
    /// </summary>
    [HarmonyPatch]
    internal static class ZombieUnequip_Suppress_Patch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZombieEquipmentInventoryWidgetData), nameof(ZombieEquipmentInventoryWidgetData.OnArmorCellPressed2));
            yield return AccessTools.Method(typeof(ZombieEquipmentInventoryWidgetData), nameof(ZombieEquipmentInventoryWidgetData.OnInstrumentCellPressed2));
        }

        private static void Prefix() => GlobalStorage.SuppressDepth++;

        private static Exception Finalizer(Exception __exception)
        {
            GlobalStorage.SuppressDepth = Math.Max(0, GlobalStorage.SuppressDepth - 1);
            return __exception;
        }
    }

    /// <summary>
    /// Hand-delivering zombie crafter orders: pocket/local zone only unless IncludeZombieOrderDelivery.
    /// </summary>
    [HarmonyPatch(typeof(PlayerOrderExecutor), MethodType.Constructor, new Type[0])]
    internal static class PlayerOrderExecutor_Ctor_Patch
    {
        private static void Prefix(out bool __state)
        {
            __state = !Plugin.IncludeZombieOrderDelivery.Value;
            if (__state)
            {
                GlobalStorage.SuppressDepth++;
            }
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                GlobalStorage.SuppressDepth = Math.Max(0, GlobalStorage.SuppressDepth - 1);
            }
            return __exception;
        }
    }
}

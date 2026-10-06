using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;

namespace GK2BuiltCount
{
    [BepInPlugin(Guid, "GK2 Built Count", Version)]
    // Soft: when GK2 Mod Framework is installed it loads first, so FrameworkLink can register; without it nothing changes.
    [BepInDependency(FrameworkLink.FrameworkGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.builtcount";
        public const string Version = "1.0.6"; // one place: the plugin attribute and the log line

        // Module version id of the Assembly-CSharp.dll this plugin was audited against (Steam build 25601286).
        private static readonly System.Guid AuditedGameMvid = new System.Guid("44edf1a9-b9b5-4276-a731-a680e9e9c44d");
        // And of LazyBearTechnology.dll (the engine library the plugin also uses), same build (Steam 25601286).
        private static readonly System.Guid AuditedLazyBearMvid = new System.Guid("02955186-b2b7-474c-a6ff-b860ac92b69c");

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> DisableOnGameUpdate;

        // Every Harmony patch class, patched by name. Not PatchAll(assembly): that lists every type in this DLL, and
        // FrameworkLink's mod class can't be loaded without GK2.Framework.dll, so Harmony would log a warning.
        // tests/FrameworkScan fails if a [HarmonyPatch] class is missing here.
        internal static readonly Type[] PatchClasses = { typeof(UIBuildingWidget_Redraw_Patch) };

        // What the mod is doing, for the Mods menu's status row.
        internal static string Status = "not started";

        private void Awake()
        {
            Log = Logger;
            BindConfig();
            Status = StartMod(); // not "Start": Unity would call that itself on the first frame and patch twice
            FrameworkLink.TryRegister(Config);
        }

        private void BindConfig()
        {
            Enabled = Config.Bind("General", "Enabled", true,
                "Build menus show how many of each building already exist in that area, e.g. \"Sawhorse (2)\". Display only.");
            DisableOnGameUpdate = Config.Bind("Safety", "DisableOnGameUpdate", true,
                "Do not patch anything if the game's code has changed since this mod was audited. Rebuild/re-audit after game updates.");
        }

        private string StartMod()
        {
            System.Guid gameMvid = typeof(WgoData).Assembly.ManifestModule.ModuleVersionId;
            System.Guid lazyBearMvid = typeof(LazyBearTechnology.LazyAudio).Assembly.ManifestModule.ModuleVersionId;
            bool forced = false;
            if (gameMvid != AuditedGameMvid || lazyBearMvid != AuditedLazyBearMvid)
            {
                if (DisableOnGameUpdate.Value)
                {
                    Log.LogError($"Game code changed since this mod was audited (Assembly-CSharp {gameMvid}, expected {AuditedGameMvid}; LazyBearTechnology {lazyBearMvid}, expected {AuditedLazyBearMvid}). Mod is DISABLED; the game runs unmodified. Set Safety.DisableOnGameUpdate=false to force-enable at your own risk.");
                    return "Disabled: the game was updated since this version was checked. Update the mod, or set Safety.DisableOnGameUpdate=false in its cfg at your own risk.";
                }
                Log.LogWarning($"Game code changed since this mod was audited (Assembly-CSharp {gameMvid}, LazyBearTechnology {lazyBearMvid}). Running anyway because DisableOnGameUpdate=false.");
                forced = true;
            }

            Harmony harmony = new Harmony(Guid);
            try
            {
                foreach (Type patch in PatchClasses)
                {
                    harmony.PatchAll(patch);
                }
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                Log.LogError($"Patching failed; all patches removed, game runs unmodified. {e}");
                return "Disabled: patching failed (see BepInEx/LogOutput.log).";
            }
            Log.LogInfo($"GK2 Built Count {Version} loaded.");
            return forced ? "Active on an unchecked game version (Safety.DisableOnGameUpdate=false)" : "Active";
        }
    }

    internal static class BuiltCounter
    {
        private static WorldZoneData cachedZone;
        private static int cachedFrame = -1;
        private static int cachedListCount = -1;
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        // Number of saved objects with this id in the zone. Counts are built once per zone per frame,
        // so drawing a whole tab walks the zone list once.
        public static int Count(WorldZoneData zone, string wgoId)
        {
            if (zone == null || string.IsNullOrEmpty(wgoId))
            {
                return 0;
            }
            if (!ReferenceEquals(zone, cachedZone) || Time.frameCount != cachedFrame || (zone.wgoDataList?.Count ?? -1) != cachedListCount)
            {
                Rebuild(zone);
            }
            return counts.TryGetValue(wgoId, out int n) ? n : 0;
        }

        private static void Rebuild(WorldZoneData zone)
        {
            counts.Clear();
            cachedZone = zone;
            cachedFrame = Time.frameCount;
            cachedListCount = zone.wgoDataList?.Count ?? -1;
            WorldData worldData = MainGame.Instance?.GameSave?.worldData;
            if (worldData == null || zone.wgoDataList == null)
            {
                return;
            }
            foreach (SGuid sGuid in zone.wgoDataList)
            {
                WgoData wgoData = worldData.GetWgoData(sGuid);
                if (wgoData == null || wgoData.isTempObject || string.IsNullOrEmpty(wgoData.id))
                {
                    continue;
                }
                counts.TryGetValue(wgoData.id, out int n);
                counts[wgoData.id] = n + 1;
            }
        }
    }

    // UIBuildingWidget.Redraw sets the row's name (plus " (x/max)" for limited buildings) and never
    // touches the label again, so appending here is safe; pooled rows get their text reset on every draw.
    [HarmonyPatch(typeof(UIBuildingWidget), nameof(UIBuildingWidget.Redraw))]
    internal static class UIBuildingWidget_Redraw_Patch
    {
        private static readonly AccessTools.FieldRef<LazyWidget<UIBuildingWidgetData>, UIBuildingWidgetData> DataRef =
            AccessTools.FieldRefAccess<LazyWidget<UIBuildingWidgetData>, UIBuildingWidgetData>("data");

        private static readonly AccessTools.FieldRef<UIBuildingWidget, TextMeshProUGUI> NameLabelRef =
            AccessTools.FieldRefAccess<UIBuildingWidget, TextMeshProUGUI>("nameLabel");

        private static bool errorLogged;

        private static void Postfix(UIBuildingWidget __instance)
        {
            if (!Plugin.Enabled.Value)
            {
                return;
            }
            try
            {
                UIBuildingWidgetData data = DataRef(__instance);
                BuildingDef def = data?.BuildData?.Definition;
                if (def == null || def.HasLimits || !CreatesObject(def.buildingMode))
                {
                    return;
                }
                // The window is only opened by BuildManager.TryEnable/Disable, which first picks the zone the desk
                // builds into (nearest zone whose builderId is this desk). That can differ from the zone the desk
                // itself stands in, which is what the row data carries.
                WorldZoneData zone = LazySingleton<BuildManager>.Instance?.WorldZone?.Data ?? data.WorldZoneData;
                if (zone == null)
                {
                    return;
                }
                int n = BuiltCounter.Count(zone, def.wgoId);
                // Only a plain id is used; a scripted expression is never evaluated here.
                WGODef wgoDef = GameBalance.Me.GetDataOrNull<WGODef>(def.wgoId);
                if (wgoDef != null && wgoDef.replaceToWgoOnDie.HasExpression && wgoDef.replaceToWgoOnDie.HasPureValue)
                {
                    string replacedId = wgoDef.replaceToWgoOnDie.GetRawExpressionString();
                    if (!string.IsNullOrEmpty(replacedId) && replacedId != def.wgoId)
                    {
                        n += BuiltCounter.Count(zone, replacedId);
                    }
                }
                TextMeshProUGUI nameLabel = NameLabelRef(__instance);
                if (n > 0 && nameLabel != null)
                {
                    nameLabel.text += $" ({n})";
                }
            }
            catch (Exception e)
            {
                if (!errorLogged)
                {
                    errorLogged = true;
                    Plugin.Log.LogError($"Built count failed; build menu shown without counts. {e}");
                }
            }
        }

        // Same set of modes the game treats as placing a new object (GameBalance.CreateBuildCache).
        private static bool CreatesObject(BuildingDef.BuildingMode mode)
        {
            switch (mode)
            {
                case BuildingDef.BuildingMode.Place:
                case BuildingDef.BuildingMode.ConveyorPlace:
                case BuildingDef.BuildingMode.FightingPlace:
                case BuildingDef.BuildingMode.FightBuilding:
                    return true;
                default:
                    return false;
            }
        }
    }
}

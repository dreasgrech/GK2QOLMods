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
    [BepInPlugin(Guid, "GK2 Built Count", "1.0.3")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.builtcount";

        // Module version id of the Assembly-CSharp.dll this plugin was audited against.
        private static readonly System.Guid AuditedGameMvid = new System.Guid("97f92f0d-a093-49fe-ba4d-74aa62fae81e");

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> DisableOnGameUpdate;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true,
                "Build menus show how many of each building already exist in that area, e.g. \"Sawhorse (2)\". Display only.");
            DisableOnGameUpdate = Config.Bind("Safety", "DisableOnGameUpdate", true,
                "Do not patch anything if the game's code has changed since this mod was audited. Rebuild/re-audit after game updates.");

            System.Guid gameMvid = typeof(WgoData).Assembly.ManifestModule.ModuleVersionId;
            if (gameMvid != AuditedGameMvid)
            {
                if (DisableOnGameUpdate.Value)
                {
                    Log.LogError($"Game code changed since this mod was audited (MVID {gameMvid}, expected {AuditedGameMvid}). Mod is DISABLED; the game runs unmodified. Set Safety.DisableOnGameUpdate=false to force-enable at your own risk.");
                    return;
                }
                Log.LogWarning($"Game code changed since this mod was audited (MVID {gameMvid}). Running anyway because DisableOnGameUpdate=false.");
            }

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
            Log.LogInfo("GK2 Built Count 1.0.3 loaded.");
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

using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GK2MoveBuilding
{
    [BepInPlugin(Guid, "GK2 Move Building", Version)]
    // Soft: when GK2 Mod Framework is installed it loads first, so FrameworkLink can register; without it nothing changes.
    [BepInDependency(FrameworkLink.FrameworkGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.movebuilding";
        public const string Version = "1.1.6";

        // Module version id of the Assembly-CSharp.dll this plugin was audited against (Steam build 25601286).
        private static readonly System.Guid AuditedGameMvid = new System.Guid("44edf1a9-b9b5-4276-a731-a680e9e9c44d");
        // And of LazyBearTechnology.dll (the engine library the plugin also uses), same build (Steam 25601286).
        private static readonly System.Guid AuditedLazyBearMvid = new System.Guid("02955186-b2b7-474c-a6ff-b860ac92b69c");

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowMoveRow;
        internal static ConfigEntry<string> MoveLabel;
        internal static ConfigEntry<KeyboardShortcut> MoveModifier;
        internal static ConfigEntry<bool> StayInMoveMode;
        internal static ConfigEntry<bool> HighlightMovable;
        internal static ConfigEntry<Color> MovableColor;
        internal static ConfigEntry<string> FaultInjection;
        internal static ConfigEntry<bool> DisableOnGameUpdate;

        // Every Harmony patch class, patched by name. Not PatchAll(assembly): that lists every type in this DLL, and
        // FrameworkLink's mod class can't be loaded without GK2.Framework.dll, so Harmony would log a warning.
        // tests/FrameworkScan fails if a [HarmonyPatch] class is missing here.
        internal static readonly Type[] PatchClasses =
        {
            typeof(UIBuildingWindowData_Ctor_Patch),
            typeof(UIBuildingWidgetData_Ctor_Patch),
            typeof(BuildManager_EnableBuildMode_Patch),
            typeof(RemovePointer_TryDoBuildAction_Patch),
            typeof(BuildPointer_TryBuildActionInput_Patch),
            typeof(BuildSelectionCell_OverlapBoxNonAlloc_Patch),
            typeof(RemovePointer_UpdateSelectionRect_Patch),
            typeof(RemovePointer_OnPointerDisable_Patch),
            typeof(BuildGrid3D_Draw_Patch),
            typeof(WgoBuildPointer_SetTarget_Patch),
        };

        // What the mod is doing, for the Mods menu's status row.
        internal static string Status = "not started";

        private Harmony harmony;
        private bool updateErrorLogged;
        private static Plugin instance;

        // Re-reads the config file (one small file read per move). Lets every setting change without a restart.
        internal static void ReloadConfig()
        {
            try
            {
                instance?.Config.Reload();
            }
            catch (Exception e)
            {
                Log.LogWarning($"Config reload failed: {e.Message}");
            }
        }

        private void Awake()
        {
            Log = Logger;
            instance = this;
            BindConfig();
            Status = StartMod(); // not "Start": Unity would call that itself on the first frame and patch twice
            FrameworkLink.TryRegister(Config);
        }

        private void BindConfig()
        {
            Enabled = Config.Bind("General", "Enabled", true,
                "Lets a built building be picked up and put down elsewhere in the same area. The building keeps its contents, crafts and identity.");
            ShowMoveRow = Config.Bind("General", "ShowMoveRow", true,
                "Adds a \"Move\" row next to \"Remove\" in the build desk menu. Pick it, then click a building.");
            MoveLabel = Config.Bind("General", "MoveLabel", "Move",
                "Text of the Move row.");
            MoveModifier = Config.Bind("General", "MoveModifier", new KeyboardShortcut(KeyCode.LeftControl),
                "Hold this key in the game's own Remove mode and click a building to move it instead of demolishing it. Set to None to disable.");
            StayInMoveMode = Config.Bind("General", "StayInMoveMode", true,
                "After a move (or after cancelling the preview with Back) return to move mode instead of the desk menu. Back in move mode still opens the menu.");
            HighlightMovable = Config.Bind("General", "HighlightMovable", true,
                "In move mode, buildings that can be moved lose the red grid patch and get a colour tint; the one under the cursor is tinted stronger. Buildings that can't be moved keep the game's red.");
            MovableColor = Config.Bind("General", "MovableColor", new Color(0.30f, 0.75f, 1f, 1f),
                "Tint colour for movable buildings in move mode.");
            // Testing only (SAVE-SAFETY.md): in the cfg only with the developer marker file BepInEx\config\dreas.gk2.dev (the
            // same marker unlocks the GK2 Donkey API's dev tools). Without it the key binds into a scratch config that is
            // never written, so a player's cfg has no [Debug] section and nothing typed there can make every move roll back
            // (the release audit of 2026-10-13).
            ConfigFile debugCfg = Config;
            try
            {
                if (!File.Exists(Path.Combine(Paths.ConfigPath, "dreas.gk2.dev")))
                {
                    debugCfg = new ConfigFile(Path.Combine(Paths.ConfigPath, "dreas.gk2.movebuilding.locked-debug.cfg"), false) { SaveOnConfigSet = false };
                }
            }
            catch
            {
            }
            FaultInjection = debugCfg.Bind("Debug", "FaultInjection", "",
                "Testing only. Throws inside the move at the named step so the restore path can be watched in the log; the move is then refused and the building must be exactly as before. " +
                "Values: AfterLinksCut, AfterZoneRemove, AfterPosition, AfterZoneAdd, WrongZone. Leave empty for normal use.");
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

            harmony = new Harmony(Guid);
            try
            {
                // Every private member the mod touches is resolved here; a missing one disables the whole mod.
                Access.Verify();
                foreach (Type patch in PatchClasses)
                {
                    harmony.PatchAll(patch);
                }
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                harmony = null;
                Log.LogError($"Patching failed; all patches removed, game runs unmodified. {e}");
                return "Disabled: patching failed (see BepInEx/LogOutput.log).";
            }
            Log.LogInfo($"GK2 Move Building {Version} loaded.");
            return forced ? "Active on an unchecked game version (Safety.DisableOnGameUpdate=false)" : "Active";
        }

        private void Update()
        {
            if (harmony == null || !Mover.HasWork)
            {
                return;
            }
            try
            {
                Mover.Tick();
            }
            catch (Exception e)
            {
                if (!updateErrorLogged)
                {
                    updateErrorLogged = true;
                    Log.LogError($"Move step failed; the move is cancelled. {e}");
                }
                Mover.Cancel("an error occurred");
            }
        }

        private void OnDestroy()
        {
            Mover.Cancel("the mod is unloading");
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}

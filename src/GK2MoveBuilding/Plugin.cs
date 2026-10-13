using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GK2Shared;
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

        // No pinned game build: at start GameDependencies compares every game member this DLL uses (the gk2deps.txt
        // manifest tools/DepManifest embedded after the build) with the installed game; docs/dependency-check.md.
        // True when the game changed the code of a member this mod uses: the mod runs, its state-changing path is off.
        internal static bool ChangedGameCode;

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowMoveRow;
        internal static ConfigEntry<string> MoveLabel;
        internal static ConfigEntry<KeyboardShortcut> MoveModifier;
        internal static ConfigEntry<bool> StayInMoveMode;
        internal static ConfigEntry<bool> HighlightMovable;
        internal static ConfigEntry<Color> MovableColor;
        internal static ConfigEntry<string> FaultInjection;

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
            GameDependencies.Bind(Config); // Safety.StopOnChangedGameCode, Safety.LastDependencyCheck
        }

        private string StartMod()
        {
            // Identical game members: patch. Changed ones: patch with a warning (or stop, Safety.StopOnChangedGameCode).
            // A member gone, or a check that could not run: stop; the game runs unmodified.
            GameDependencies.Result deps = GameDependencies.Check(typeof(Plugin).Assembly, typeof(WgoData).Assembly, typeof(LazyBearTechnology.LazyAudio).Assembly);
            if (!GameDependencies.Apply(deps, "GK2 Move Building", Log, "Running with moving off", out string status))
            {
                return status;
            }
            ChangedGameCode = deps.State == GameDependencies.State.Changed;

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
            return status;
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

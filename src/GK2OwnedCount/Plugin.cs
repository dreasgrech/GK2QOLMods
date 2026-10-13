using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GK2Shared;
using HarmonyLib;

namespace GK2OwnedCount
{
    [BepInPlugin(Guid, "GK2 Owned Count", Version)]
    // Soft: when GK2 Mod Framework is installed it loads first, so FrameworkLink can register; without it nothing changes.
    [BepInDependency(FrameworkLink.FrameworkGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.ownedcount";
        public const string Version = "1.0.4";

        // No pinned game build: at start GameDependencies compares every game member this DLL uses (the gk2deps.txt
        // manifest tools/DepManifest embedded after the build) with the installed game; docs/dependency-check.md.

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> OnlyOnOwnTooltip;
        internal static ConfigEntry<bool> ShowBreakdown;
        internal static ConfigEntry<bool> ShowPerArea;
        internal static ConfigEntry<bool> IncludeOtherGrades;
        internal static ConfigEntry<bool> IncludeFuelContainers;
        internal static ConfigEntry<bool> IncludeConveyorStorage;
        internal static ConfigEntry<bool> DumpItemDefs;

        // Every Harmony patch class, patched by name. Not PatchAll(assembly): that lists every type in this DLL, and
        // FrameworkLink's mod class can't be loaded without GK2.Framework.dll, so Harmony would log a warning.
        // tests/FrameworkScan fails if a [HarmonyPatch] class is missing here.
        internal static readonly Type[] PatchClasses = { typeof(UITooltip_AddItemWidgets_Patch) };

        // What the mod is doing, for the Mods menu's status row.
        internal static string Status = "not started";

        private Harmony harmony;

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
                "Item tooltips get a line with how many of that item you own: on you (inventory, bags, tool belt) plus every storage chest in every area. Display only.");
            OnlyOnOwnTooltip = Config.Bind("General", "OnlyOnOwnTooltip", true,
                "Show the line only on an item's own tooltip (inventory, chest, vendor, craft result). Off: also on items embedded in other tooltips (craft ingredient rows, alchemy, dialogue).");
            ShowBreakdown = Config.Bind("General", "ShowBreakdown", true,
                "Add \"on you N · in storage N (chests)\" after the total.");
            ShowPerArea = Config.Bind("General", "ShowPerArea", false,
                "Add a second line with the count per area, e.g. \"yard 80 · cellar 36\".");
            IncludeOtherGrades = Config.Bind("General", "IncludeOtherGrades", true,
                "For graded items (the ones with a star, e.g. Bones of different skull grades) also show the total across all grades.");
            IncludeFuelContainers = Config.Bind("Storage", "IncludeFuelContainers", false,
                "Count items inside fuel containers (e.g. furnace wood piles) as storage. Same meaning as in Global Storage.");
            IncludeConveyorStorage = Config.Bind("Storage", "IncludeConveyorStorage", false,
                "Count items inside conveyor chests as storage. Same meaning as in Global Storage.");
            GameDependencies.Bind(Config); // Safety.StopOnChangedGameCode, Safety.LastDependencyCheck
            DumpItemDefs = Config.Bind("Debug", "DumpItemDefs", false,
                "Once the game data is loaded, write every item definition (id, name, type, stack size, grade, star) to BepInEx\\GK2-itemdefs.csv. Off by default.");
        }

        private string StartMod()
        {
            // Identical game members: patch. Changed ones: patch with a warning (or stop, Safety.StopOnChangedGameCode).
            // A member gone, or a check that could not run: stop; the game runs unmodified.
            GameDependencies.Result deps = GameDependencies.Check(typeof(Plugin).Assembly, typeof(WgoData).Assembly, typeof(LazyBearTechnology.LazyAudio).Assembly);
            if (!GameDependencies.Apply(deps, "GK2 Owned Count", Log, "Running anyway", out string status))
            {
                return status;
            }

            harmony = new Harmony(Guid);
            try
            {
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
            Log.LogInfo($"GK2 Owned Count {Version} loaded.");
            return status;
        }

        // Only used by the debug dump; nothing runs per frame otherwise.
        private void Update()
        {
            if (harmony != null && DumpItemDefs.Value && !ItemDefDump.Done)
            {
                ItemDefDump.TryWrite();
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}

using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
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

        // Module version id of the Assembly-CSharp.dll this plugin was audited against (Steam build 25601286).
        private static readonly System.Guid AuditedGameMvid = new System.Guid("5ec8094e-4c33-4615-9eb8-011501193186");
        // And of LazyBearTechnology.dll (the engine library the plugin also uses), same build (Steam 25601286).
        private static readonly System.Guid AuditedLazyBearMvid = new System.Guid("6cd86a0b-b1ed-40ba-88b0-a9313146514b");

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> OnlyOnOwnTooltip;
        internal static ConfigEntry<bool> ShowBreakdown;
        internal static ConfigEntry<bool> ShowPerArea;
        internal static ConfigEntry<bool> IncludeOtherGrades;
        internal static ConfigEntry<bool> IncludeFuelContainers;
        internal static ConfigEntry<bool> IncludeConveyorStorage;
        internal static ConfigEntry<bool> DisableOnGameUpdate;
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
            DisableOnGameUpdate = Config.Bind("Safety", "DisableOnGameUpdate", true,
                "Do not patch anything if the game's code has changed since this mod was audited. Rebuild/re-audit after game updates.");
            DumpItemDefs = Config.Bind("Debug", "DumpItemDefs", false,
                "Once the game data is loaded, write every item definition (id, name, type, stack size, grade, star) to BepInEx\\GK2-itemdefs.csv. Off by default.");
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
            return forced ? "Active on an unchecked game version (Safety.DisableOnGameUpdate=false)" : "Active";
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

using System;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using GK2Shared;
using HarmonyLib;
using UnityEngine;

namespace GK2TechTreeSearch
{
    [BepInPlugin(Guid, "GK2 Tech Tree Search", Version)]
    // Soft: when GK2 Mod Framework is installed it loads first, so FrameworkLink can register; without it nothing changes.
    [BepInDependency(FrameworkLink.FrameworkGuid, BepInDependency.DependencyFlags.SoftDependency)]
    // Soft: the old GK2TechSearch.dll (GK2 Tech Search 1.0.0, GitHub), if a player still has it, loads first, so Awake
    // can see it in Chainloader.PluginInfos and stay off instead of adding a second search box.
    [BepInDependency(OldGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.techtreesearch";
        internal const string OldGuid = "dreas.gk2.techsearch"; // GK2 Tech Search, the name until 1.0.1 (2026-10-30)
        public const string Version = "1.0.2"; // one place: the plugin attribute and the log line

        // No pinned game build: at start GameDependencies compares every game member this DLL uses (the gk2deps.txt
        // manifest tools/DepManifest embedded after the build) with the installed game; docs/dependency-check.md.

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SearchUnlocks;
        internal static ConfigEntry<int> MaxResults;
        internal static ConfigEntry<KeyboardShortcut> FocusKey;
        internal static ConfigEntry<Vector2> BoxOffset;
        internal static ConfigEntry<Vector2> BoxSize;
        internal static ConfigEntry<float> ListWidth;

        // Every Harmony patch class, patched by name. Not PatchAll(assembly): that lists every type in this DLL, and
        // FrameworkLink's mod class can't be loaded without GK2.Framework.dll, so Harmony would log a warning.
        // tests/FrameworkScan fails if a [HarmonyPatch] class is missing here.
        internal static readonly Type[] PatchClasses =
        {
            typeof(TechTreePageWidget_Init_Patch),
            typeof(TechTreePageWidget_Redraw_Patch),
            typeof(TechTreePageWidget_Hide_Patch),
            typeof(InWorldSfxFilterController_ShouldEnable_Patch), // the window code's: the world muffled under the options window, as under the game's windows
        };

        // What the mod is doing, for the status rows (the options window, the Mods menu).
        internal static string Status = "not started";

        // The status as the rows show it: off by the player's switch is said as such.
        internal static string StatusShown() => Status == "Active" && Enabled != null && !Enabled.Value ? "Off (\"Search box on the Tech Tree\" is unchecked)" : Status;

        // A developer's machine: the marker file the dev tools of our mods share, in BepInEx\config. With it the search
        // box writes one line about its fonts a language (SearchBox.SayFonts); a player never has it.
        private const string DevMarker = "dreas.gk2.dev";
        internal static bool DevTools;

        private Harmony harmony;
        private SettingsPage page;

        private void Awake()
        {
            Log = Logger;
            try { DevTools = File.Exists(Path.Combine(Paths.ConfigPath, DevMarker)); }
            catch (Exception) { DevTools = false; }
            // The window code's owner (lib\GK2DreasUI): the settings page in the options window, opened from a button in
            // the game's Settings window. First thing after the logger, before anything of the windows runs.
            UiOwner.Guid = Guid;
            UiOwner.DisplayName = "GK2 Tech Tree Search";
            UiOwner.Log = Log;
            UiOwner.InGame = InGame;
            UiOwner.StatusText = StatusShown;
            UiOwner.KeysExact = true; // the key is read with BepInEx's IsDown (SearchBox): the exact combination, no other key held
            CarryOverOldConfig();
            BindConfig();
            if (Chainloader.PluginInfos.ContainsKey(OldGuid))
            {
                // Both would add a search box to the Tech Tree page. The old file is the player's to delete.
                Status = "Off: the old GK2TechSearch.dll is still in BepInEx\\plugins; delete it, this mod replaces it.";
                Log.LogWarning("GK2TechSearch.dll (GK2 Tech Search, the old name of this mod) is still in BepInEx\\plugins. Delete it: this mod replaces it and stays off while both are installed.");
            }
            else
            {
                Status = StartMod(); // not "Start": Unity would call that itself on the first frame and patch twice
            }
            FrameworkLink.TryRegister(Config, page); // its Mods menu's Settings button opens the same window
            page.Publish(); // the page reaches the windows the frame after, when every plugin's Awake has run
        }

        // In a game with a player (not the main menu, not loading): the window code's test for taking the controls and
        // for a usable button row. The same test as GK2 Donkey API's (PlayerController threw in the main menu, 2026-10-30).
        private static bool InGame()
        {
            try
            {
                if (MainGame.Instance == null || MainGame.PlayerData == null || MainGame.Instance.GameSave == null) return false;
                PlayerController pc = MainGame.PlayerController;
                return pc != null && pc.TryGetCurrentGameScene(out GameScene scene) && scene != null;
            }
            catch
            {
                return false;
            }
        }

        // The window code's frame step (a settings page's hand-over to the windows, the options window, the deferred cfg
        // writes) and its flush at quit.
        private void LateUpdate() => UiHost.LateUpdate();

        private void OnApplicationQuit() => UiHost.OnQuit();

        // 1.0.1 renamed the mod (GUID dreas.gk2.techsearch -> dreas.gk2.techtreesearch), and the GUID names the cfg
        // file. A player's old settings are copied to the new file once, on the first start without one; the old
        // file stays (harmless, and theirs).
        private void CarryOverOldConfig()
        {
            string oldPath = Path.Combine(Paths.ConfigPath, OldGuid + ".cfg");
            try
            {
                if (File.Exists(Config.ConfigFilePath) || !File.Exists(oldPath))
                {
                    return;
                }
                File.Copy(oldPath, Config.ConfigFilePath);
                Config.Reload();
                Log.LogInfo($"Settings copied from {OldGuid}.cfg (the mod's name until 1.0.1) to {Guid}.cfg; the old file can be deleted.");
            }
            catch (Exception e)
            {
                Log.LogWarning($"Could not copy the old settings file {oldPath}: {e.GetType().Name}: {e.Message}. Starting with defaults.");
            }
        }

        // The settings: each a cfg entry, the General and Safety ones rows of the settings page too (the options window;
        // the page binds the same section, key and type, so BepInEx hands it the entries bound here and the window edits
        // the values the mod reads). The sections stay as named (numberSections: false): the cfg had them that way before
        // the window. The Layout ones (a point, a size, a width) have no row: the cfg's.
        // Everything the mod has to translate is declared here: the page's rows, and the search box's own words
        // (UiOwner.OwnWords). The window library's tools/EnglishDump runs this method alone, without the game, to write
        // Languages\en.csv from the build: nothing in it may need a running game.
        private void BindConfig()
        {
            UiOwner.OwnWords = () => SearchWords.All;
            page = new SettingsPage(Guid, Config, "GK2 Tech Tree Search", "dreas",
                "A search box on the Tech Tree page: type part of a tech's name, or of something it unlocks, and jump to it. Display only.",
                StatusShown, numberSections: false);
            Enabled = Config.Bind("General", "Enabled", true,
                "A search box on the Tech Tree page: type part of a tech's name (or of something it unlocks) and jump to it. Hidden techs and locked tabs are never shown. Display only.");
            SearchUnlocks = Config.Bind("General", "SearchUnlocks", true,
                "Also match what a tech unlocks (the items, buildings and perks shown on its card), e.g. \"beer\" finds Brewing.");
            MaxResults = Config.Bind("General", "MaxResults", 8,
                new ConfigDescription("How many results the list under the box shows at most.", new AcceptableValueRange<int>(1, 20)));
            FocusKey = Config.Bind("General", "FocusKey", new KeyboardShortcut(KeyCode.F, KeyCode.LeftControl),
                "Puts the cursor in the search box while the Tech Tree page is open. Clicking the box works too. None: no key.");
            // Defaults from the measured page (2026-10-04): 640 x 385 units, tab buttons end about 165 right of centre,
            // tab strip centred 168.5 above it. The box fills the free strip right of the tabs.
            BoxOffset = Config.Bind("Layout", "BoxOffset", new Vector2(-8f, -13f),
                "Where the box sits, from the top right corner of the Tech Tree page (x left is negative, y down is negative). Applies the next time the page opens.");
            BoxSize = Config.Bind("Layout", "BoxSize", new Vector2(130f, 22f),
                "Width and height of the box. Applies the next time the page opens.");
            ListWidth = Config.Bind("Layout", "ListWidth", 280f,
                "Width of the result list under the box (right edges aligned; never narrower than the box). Applies the next time the page opens.");
            GameDependencies.Bind(Config); // Safety.StopOnChangedGameCode, Safety.LastDependencyCheck

            // The rows, in the window's order; the descriptions as the cfg's (a row's description is the entry's). Each
            // General one is read on every search or page open, so a change applies the next time the page opens.
            page.Status("General");
            page.Toggle("General", "Enabled", true, "Search box on the Tech Tree", Enabled.Description.Description);
            page.Toggle("General", "SearchUnlocks", true, "Also search what techs unlock", SearchUnlocks.Description.Description);
            page.Steps("General", "MaxResults", 8, 1, 20, 1, "Results shown", MaxResults.Description.Description);
            page.Key("General", "FocusKey", new KeyboardShortcut(KeyCode.F, KeyCode.LeftControl), "Key to start a search", FocusKey.Description.Description); // the mod reads the key itself (SearchBox)
            page.Toggle("Safety", "StopOnChangedGameCode", false, "Stop on changed game code",
                GameDependencies.StopOnChangedGameCode.Description.Description + " Applies at the next start.");
        }

        private string StartMod()
        {
            // Identical game members: patch. Changed ones: patch with a warning (or stop, Safety.StopOnChangedGameCode).
            // A member gone, or a check that could not run: stop; the game runs unmodified.
            GameDependencies.Result deps = GameDependencies.Check(typeof(Plugin).Assembly, typeof(WgoData).Assembly, typeof(LazyBearTechnology.LazyAudio).Assembly);
            if (!GameDependencies.Apply(deps, "GK2 Tech Tree Search", Log, "Running anyway", out string status))
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
            Log.LogInfo($"GK2 Tech Tree Search {Version} loaded.");
            return status;
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}

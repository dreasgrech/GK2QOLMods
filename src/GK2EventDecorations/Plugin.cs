using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GK2Shared;
using HarmonyLib;

namespace GK2EventDecorations
{
    [BepInPlugin(Guid, "GK2 Event Decorations", Version)]
    // Soft: when GK2 Mod Framework is installed it loads first, so FrameworkLink can register; without it nothing changes.
    [BepInDependency(FrameworkLink.FrameworkGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.eventdecorations";
        public const string Version = "1.0.0";

        // No pinned game build: at start GameDependencies compares every game member this DLL uses (the gk2deps.txt
        // manifest tools/DepManifest embedded after the build) with the installed game; docs/dependency-check.md.

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SendGhostHome;

        // Every Harmony patch class, patched by name. Not PatchAll(assembly): that lists every type in this DLL, and
        // FrameworkLink's mod class can't be loaded without GK2.Framework.dll, so Harmony would log a warning.
        // tests/FrameworkScan fails if a [HarmonyPatch] class is missing here. An event a newer game adds gets no
        // patch: it is left to the game (Events).
        internal static readonly Type[] PatchClasses =
        {
            typeof(TemporaryEventDates_IsHalloweenEvent_Patch),
            typeof(TemporaryEventDates_IsHalloweenEventDate_Patch),
            typeof(HalloweenConditionalLoadContentPart_ShouldLoad_Patch),
            typeof(HalloweenHeadSkinOverride_Resolve_Patch),
            typeof(WorldData_AddMissingConditionalContent_Patch),
            typeof(InWorldSfxFilterController_ShouldEnable_Patch) // the window code's: the world muffled under the options window, as under the game's windows
        };

        // What the mod is doing, in English: the log's, and "Active" is what FrameworkLink confirms the build on.
        internal static string Status = "not started";

        // The same as a word of the mod's with its values (EdWords), for the Status row in the player's language.
        private static Word statusWord = EdWords.StatusNotStarted;
        private static object[] statusValues = new object[0];

        // The status as the options window's row shows it, in the game's language: off by the player's switch is said
        // as such, naming the switch as its own row does.
        internal static string StatusShown()
        {
            if (Status == "Active" && Enabled != null && !Enabled.Value)
            {
                return EdWords.StatusOff.Format(Texts.Of(Texts.Api, "Enabled.label", "Mod enabled"));
            }
            return statusWord.Format(statusValues);
        }

        private static void SetStatus(Word word, params object[] values)
        {
            statusWord = word;
            statusValues = values;
        }

        private Harmony harmony;
        private bool listening;
        private SettingsPage page;

        private void Awake()
        {
            Log = Logger;
            // The window code's owner (lib\GK2DreasUI): the settings page in the options window, opened from a row in
            // the game's Settings window. First thing after the logger, before anything of the windows runs.
            UiOwner.Guid = Guid;
            UiOwner.DisplayName = "GK2 Event Decorations";
            UiOwner.Log = Log;
            UiOwner.InGame = InGame;
            UiOwner.StatusText = StatusShown;
            BindConfig();
            Status = StartMod(); // not "Start": Unity would call that itself on the first frame and patch twice
            FrameworkLink.TryRegister(Config, page); // with the framework: its Settings button opens the options window
            page.Publish(); // the page reaches the windows the frame after, when every plugin's Awake has run
        }

        // In a game with a player (not the main menu, not loading): the window code's test for taking the controls and
        // for a usable button row. The same test as GK2 Tech Tree Search's and GK2 Donkey API's.
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

        // The settings: each a cfg entry, and a row of the settings page (OptionsPage: the options window; the page binds
        // the same section, key and type, so BepInEx hands it the entries bound here). The sections stay as named
        // (numberSections: false): the Mods menu (FrameworkLink) binds them by name too.
        // Everything the mod has to translate is declared here: the page's rows, and the words of its two lines
        // (UiOwner.OwnWords). The window library's tools/EnglishDump runs this method alone, without the game, to write
        // Languages\en.csv from the build: nothing in it may need a running game.
        private void BindConfig()
        {
            UiOwner.OwnWords = () => EdWords.All;
            page = new SettingsPage(Guid, Config, "GK2 Event Decorations", "dreas",
                "Choose when the game's temporary events show (Halloween): by the game's own dates or yours, every day, or never; and " +
                "which of Halloween's decorations, ghost and zombie head, where.",
                StatusShown, numberSections: false);
            Enabled = Config.Bind("General", "Enabled", true,
                "On: the events show as set on their tabs. Off: the game decides by its own dates, as without the mod; your " +
                "settings are kept for when you switch it back on.");
            Events.Bind(Config); // Halloween's section: Mode, Start, End
            Halloween.Bind(Config); // Halloween's pieces, areas and kinds
            SendGhostHome = Config.Bind(Events.HalloweenName, "SendGhostHome", true,
                "While Halloween is off, a ghost left standing in the village is sent home. The game loses the last step of " +
                "the ghost's morning walk home when the save is made during it, and leaves the ghost in the village after the event. " +
                "Not in the options window: a fix, not a choice; false here switches it off.");
            // Safety.StopOnChangedGameCode on by default in this mod, and only in the cfg (no row: the user, 2026-11-15): bound
            // here first with true, so the shared GameDependencies.Bind (src/Shared, the same in every mod, default false)
            // gets this entry. After a game update that changed game code this mod uses, it stays off until an updated build.
            Config.Bind("Safety", "StopOnChangedGameCode", true,
                "At start the mod compares every game member it uses with the installed game. A member that is gone always stops the mod. " +
                "True (the default): a member whose code changed stops it too, until an updated build. False: it runs anyway, with a warning in the log.");
            GameDependencies.Bind(Config); // Safety.StopOnChangedGameCode (the entry above), Safety.LastDependencyCheck
            OptionsPage.Declare(page); // the rows, in the window's order
        }

        private string StartMod()
        {
            // Identical game members: patch. Changed ones: patch with a warning (or stop, Safety.StopOnChangedGameCode).
            // A member gone, or a check that could not run: stop; the game runs unmodified.
            GameDependencies.Result deps = GameDependencies.Check(typeof(Plugin).Assembly, typeof(WgoData).Assembly, typeof(LazyBearTechnology.LazyAudio).Assembly);
            bool run = GameDependencies.Apply(deps, "GK2 Event Decorations", Log, "Running anyway", out string status);
            // the same verdict as a word, for the Status row in the player's language (the English one is the log's)
            switch (deps.State)
            {
                case GameDependencies.State.Failed:
                    SetStatus(EdWords.StatusCheckFailed);
                    break;
                case GameDependencies.State.Missing:
                    SetStatus(EdWords.StatusMissing, deps.Missing.Count);
                    break;
                case GameDependencies.State.Changed:
                    SetStatus(run ? EdWords.StatusChangedRun : EdWords.StatusChangedStop, deps.Changed.Count);
                    break;
                default:
                    SetStatus(EdWords.StatusActive);
                    break;
            }
            if (!run)
            {
                return status;
            }

            harmony = new Harmony(Guid);
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
                harmony = null;
                Log.LogError($"Patching failed; all patches removed, game runs unmodified. {e}");
                SetStatus(EdWords.StatusPatchFailed);
                return "Disabled: patching failed (see BepInEx/LogOutput.log).";
            }
            MainGame.OnGameStarted = (Action)Delegate.Combine(MainGame.OnGameStarted, (Action)GhostHome.OnGameStarted);
            MainGame.OnGameStarted = (Action)Delegate.Combine(MainGame.OnGameStarted, (Action)Decorations.OnGameStarted);
            Config.SettingChanged += Decorations.OnSettingChanged; // any setting: the decorations follow at once
            listening = true;
            Log.LogInfo($"GK2 Event Decorations {Version} loaded. Events: {Events.Describe()}.");
            SayOtherEvents();
            return status;
        }

        // An event the game has that this version doesn't know (a later game's): named once, and left to the game. The
        // mod switches only what has been read (docs/seasonal-events.md); a new event gets its switches in a new version.
        private static void SayOtherEvents()
        {
            try
            {
                List<string> others = Events.OtherGates(typeof(TemporaryEventDates));
                if (others.Count > 0)
                {
                    Log.LogInfo($"The game has {others.Count} event(s) this version of the mod doesn't know ({string.Join(", ", others.ToArray())}): left to the game's own dates, as without the mod. A later version of the mod adds its options.");
                }
            }
            catch (Exception e)
            {
                Log.LogInfo($"The game's other events could not be listed ({e.GetType().Name}: {e.Message}); nothing depends on it.");
            }
        }

        private void Update()
        {
            if (harmony != null)
            {
                GhostHome.Tick();
            }
        }

        private void OnDestroy()
        {
            if (listening)
            {
                MainGame.OnGameStarted = (Action)Delegate.Remove(MainGame.OnGameStarted, (Action)GhostHome.OnGameStarted);
                MainGame.OnGameStarted = (Action)Delegate.Remove(MainGame.OnGameStarted, (Action)Decorations.OnGameStarted);
                Config.SettingChanged -= Decorations.OnSettingChanged;
                listening = false;
            }
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}

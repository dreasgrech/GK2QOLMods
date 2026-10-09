using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace GK2CorpsePickup
{
    [BepInPlugin(Guid, "GK2 Corpse Pickup", Version)]
    // Soft: when GK2 Mod Framework is installed it loads first, so FrameworkLink can register; without it nothing changes.
    [BepInDependency(FrameworkLink.FrameworkGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.corpsepickup";
        public const string Version = "1.0.0"; // one place: the plugin attribute and the log line

        // Module version id of the Assembly-CSharp.dll this plugin was audited against (Steam build 25752683).
        private static readonly System.Guid AuditedGameMvid = new System.Guid("44edf1a9-b9b5-4276-a731-a680e9e9c44d");
        // And of LazyBearTechnology.dll (the engine library the plugin also uses), same build (Steam 25752683).
        private static readonly System.Guid AuditedLazyBearMvid = new System.Guid("02955186-b2b7-474c-a6ff-b860ac92b69c");

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> DropPoint;
        internal static ConfigEntry<bool> UnloadAtCrossroads;
        internal static ConfigEntry<string> StandPoint;
        internal static ConfigEntry<float> UnloadDelay;
        internal static ConfigEntry<float> LeaveDelay;
        internal static ConfigEntry<bool> BodiesNeverRot;
        internal static ConfigEntry<string> NotificationText;
        internal static ConfigEntry<KeyboardShortcut> TestDeliveryKey;
        internal static ConfigEntry<KeyboardShortcut> SelfTestKey;
        internal static ConfigEntry<bool> SelfTestLeaveBodies;
        internal static ConfigEntry<bool> DisableOnGameUpdate;

        // The GD points by the nun at the crossroads (scene RuinedTemple; see docs/corpse-delivery.md for positions).
        internal static readonly string[] DropPointChoices =
        {
            "nun_body_drop_point",   // where the game itself drops a body by the nun during the donkey strike
            "crossroad_nun_corpses", // right beside the nun
            "donkey_01",             // where the donkey appears
        };

        // Where the donkey walks to and unloads. The game spawns it at donkey_01, which is behind a tree; the first
        // choice is the next point of its road, right by the nun.
        internal static readonly string[] StandPointChoices =
        {
            "donkey_01_01",          // on the road beside the nun
            "crossroad_nun_corpses", // right beside the nun, off the road
            "donkey_02",             // further west on the road
            "donkey_01",             // the game's own spawn point (behind the tree)
        };

        // Every Harmony patch class, patched by name. Not PatchAll(assembly): that lists every type in this DLL, and
        // FrameworkLink's mod class can't be loaded without GK2.Framework.dll, so Harmony would log a warning.
        // tests/FrameworkScan fails if a [HarmonyPatch] class is missing here.
        internal static readonly Type[] PatchClasses =
        {
            typeof(Flow_TryToAddCorpseToPallet_Patch),
            typeof(Flow_GoTo_GoTo_Patch),
            typeof(Flow_DropItem_Drop_Patch),
            typeof(UINotificator_ShowNewBodyNotification_Patch),
        };

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
                "The donkey leaves the bodies it brings at the crossroads by the nun, where it appears, instead of putting them on the morgue pallets. You carry them to the morgue yourself (or cart them). Off: the game's own delivery.");
            DropPoint = Config.Bind("General", "DropPoint", DropPointChoices[0],
                new ConfigDescription("Where the bodies are left: the game's own spot by the nun (nun_body_drop_point), right beside her (crossroad_nun_corpses) or where the donkey appears (donkey_01).",
                    new AcceptableValueList<string>(DropPointChoices)));
            UnloadAtCrossroads = Config.Bind("General", "UnloadAtCrossroads", true,
                "The donkey unloads right where it appears and vanishes again, instead of walking on to the morgue and back with nothing to deliver there. Off: it walks as in the game; the bodies still appear at the crossroads when it arrives. While a quest keeps the donkey at the morgue for a talk, it walks there either way.");
            StandPoint = Config.Bind("General", "StandPoint", StandPointChoices[0],
                new ConfigDescription("Where the donkey walks to and unloads (with UnloadAtCrossroads); it walks back and vanishes where it appeared. The game's own spawn point, donkey_01, is behind a tree; donkey_01_01 is the next point of its road, beside the nun. donkey_01 itself: it unloads on the spot after UnloadDelay.",
                    new AcceptableValueList<string>(StandPointChoices)));
            UnloadDelay = Config.Bind("Timing", "UnloadDelay", 1.5f,
                new ConfigDescription("Only when the donkey unloads on the spot (StandPoint = donkey_01, or no walk possible): seconds before the bodies appear.", new AcceptableValueRange<float>(0.1f, 10f)));
            LeaveDelay = Config.Bind("Timing", "LeaveDelay", 1f,
                new ConfigDescription("Only when the donkey unloads on the spot: seconds it stays before it vanishes. Otherwise it walks back to where it appeared and vanishes there.", new AcceptableValueRange<float>(0.1f, 10f)));
            BodiesNeverRot = Config.Bind("General", "BodiesNeverRot", false,
                "A body left on the ground normally turns into a grave mound after about 75 minutes of play out of sight (the game's rule, also for bodies it drops itself). On: bodies the donkey leaves never rot.");
            NotificationText = Config.Bind("General", "NotificationText", "The donkey left a body at the crossroads",
                "Shown instead of the game's \"New Body\" notification for each body left at the crossroads, with the body's icon. Empty: the game's own notification.");
            TestDeliveryKey = Config.Bind("Debug", "TestDeliveryKey", KeyboardShortcut.Empty,
                "For testing: makes the donkey start a delivery now, as the game does every second day in the early afternoon (the same roll and rules apply, so it may decide not to come). None: no key.");
            SelfTestKey = Config.Bind("Debug", "SelfTestKey", KeyboardShortcut.Empty,
                "For testing: runs the mod's self-tests in the loaded game (the drop step, the notification swap, twelve bodies at once, never-rot, the fall-back paths). Results go to the log as [test] lines; the test bodies are removed again. None: no key.");
            SelfTestLeaveBodies = Config.Bind("Debug", "SelfTestLeaveBodies", false,
                "Self-tests leave their bodies lying at the drop point instead of removing them, so you can try carrying and carting many. They are ordinary bodies.");
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
                Delivery.Verify();
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
            Log.LogInfo($"GK2 Corpse Pickup {Version} loaded.");
            return forced ? "Active on an unchecked game version (Safety.DisableOnGameUpdate=false)" : "Active";
        }

        private void Update()
        {
            if (harmony == null)
            {
                return;
            }
            if (TestDeliveryKey.Value.IsDown())
            {
                Delivery.StartDeliveryNow();
            }
            if (SelfTestKey.Value.IsDown())
            {
                SelfTest.Start(this);
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}

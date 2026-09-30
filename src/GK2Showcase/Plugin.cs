using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GK2Mounts.Api;
using UnityEngine;

namespace GK2Showcase
{
    // The playable mount, built only on the public GK2 Mounts API (docs/mounts-api-design.md): the example of how
    // another mod uses it. GK2 Mounts itself does nothing on its own; this mod gives the player the keys and the ride
    // behaviour switches. Placeholder name until the user picks one.
    //   RideKey (H)  summon the donkey to ride on; again: dismount; while in the cart: switch to riding
    //   CartKey (G)  summon the donkey with its cart; again: dismount; while riding: switch to the cart
    //   BellKey      ring the donkey's bell while mounted (none by default)
    // The behaviour switches are handed to the core as session overrides (Mounts.Behaviour), so the core's dev panel
    // shows them locked and set by this mod.
    [BepInPlugin(Guid, "GK2 Showcase", Version)]
    [BepInDependency("dreas.gk2.mounts", "0.1.0")]
    // Soft: with GK2 Mod Framework installed its Mods menu shows the keys and switches (FrameworkLink); without it nothing changes.
    [BepInDependency(FrameworkLink.FrameworkGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.showcase";
        public const string Version = "0.1.0";

        // The API this mod is written against: 0.4 added Mounts.Behaviour.
        private static readonly Version NeedsApi = new Version(0, 4, 0);

        internal static ManualLogSource Log;

        internal ConfigEntry<KeyboardShortcut> rideKey, cartKey, bellKey;
        internal ConfigEntry<bool> stayThroughDoors, dismountIndoors, showMessages;
        private bool ready;

        // What the mod is doing, for the Mods menu's status row.
        internal static string Status = "not started";
        internal static Plugin Instance;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            rideKey = Config.Bind("Keys", "RideKey", new KeyboardShortcut(KeyCode.H),
                "Summon the donkey and ride on its back; press again to dismount. While in the cart: switch to riding. Pick a key the game does not use.");
            cartKey = Config.Bind("Keys", "CartKey", new KeyboardShortcut(KeyCode.G),
                "Summon the donkey with its cart and ride it while it pulls; press again to dismount. While riding: switch to the cart.");
            bellKey = Config.Bind("Keys", "BellKey", KeyboardShortcut.Empty,
                "While mounted: ring the donkey's bell. None = no key.");
            stayThroughDoors = Config.Bind("Behaviour", "StayMountedThroughDoors", false,
                "Keep riding through doors that load another area (the donkey is summoned again there). Off: the ride ends at such a door.");
            dismountIndoors = Config.Bind("Behaviour", "DismountIndoors", true,
                "Dismount when entering a building, church or dungeon, and don't mount inside: the cart cannot avoid furniture and door frames.");
            showMessages = Config.Bind("Behaviour", "ShowMessages", true,
                "The keeper says in a thought bubble why he cannot mount.");

            if (Mounts.ApiVersion < NeedsApi)
            {
                Log.LogError($"GK2 Mounts API {Mounts.ApiVersion} is too old (this mod needs {NeedsApi} or newer). Update GK2 Mounts. The mount is off.");
                Status = $"Off: GK2 Mounts is too old (API {Mounts.ApiVersion}, needs {NeedsApi}).";
                FrameworkLink.TryRegister(Config);
                return;
            }
            foreach (ConfigEntry<bool> e in new[] { stayThroughDoors, dismountIndoors, showMessages })
            {
                ConfigEntry<bool> entry = e;
                entry.SettingChanged += (_, __) => ApplyBehaviour(entry);
                ApplyBehaviour(entry);
            }
            ready = true;
            Status = "Active";
            FrameworkLink.TryRegister(Config);
            Log.LogInfo($"GK2 Showcase {Version} loaded (GK2 Mounts API {Mounts.ApiVersion}): ride {rideKey.Value}, cart {cartKey.Value}.");
        }

        // The behaviour settings are this mod's; the core runs with them as session overrides.
        private static void ApplyBehaviour(ConfigEntry<bool> entry)
        {
            try
            {
                Mounts.Behaviour.Set(Guid, entry.Definition.Key, entry.Value);
            }
            catch (Exception e)
            {
                Log.LogWarning($"Could not hand {entry.Definition.Key} to GK2 Mounts: {e.Message}");
            }
        }

        private void Update()
        {
            if (!ready || !Mounts.IsAvailable)
            {
                return;
            }
            try
            {
                if (Pressed(rideKey))
                {
                    Use(MountStyle.Ride);
                }
                else if (Pressed(cartKey))
                {
                    Use(MountStyle.Cart);
                }
                if (Pressed(bellKey) && Mounts.IsMounted)
                {
                    Mounts.RingBell();
                }
            }
            catch (Exception e)
            {
                Log.LogError("Key handling failed: " + e);
            }
        }

        // KeyboardShortcut.IsDown: the main key pressed this frame with exactly its modifiers held (e.g. Shift+H).
        private static bool Pressed(ConfigEntry<KeyboardShortcut> key)
        {
            return key.Value.MainKey != KeyCode.None && key.Value.IsDown();
        }

        // On foot: mount in that style. Mounted in that style: dismount. Mounted in the other style: switch.
        private static void Use(MountStyle style)
        {
            if (!Mounts.IsMounted)
            {
                Mounts.TryMount(Guid, style);
            }
            else if (Mounts.Style == style)
            {
                Mounts.Dismount("dismounted");
            }
            else
            {
                Mounts.SwitchStyle(style);
            }
        }

        private void OnDestroy()
        {
            try
            {
                Mounts.ClearTuning(Guid);
            }
            catch
            {
            }
        }
    }
}

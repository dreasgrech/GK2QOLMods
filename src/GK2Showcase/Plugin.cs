using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GK2Mounts.Api;
using UnityEngine;

namespace GK2Showcase
{
    // The playable mount, built only on the public GK2 Mounts API (docs/mounts-api.md): the example of how another mod
    // uses it. GK2 Mounts itself does nothing on its own; this mod gives the player a donkey, the keys and the ride
    // behaviour switches. Placeholder name until the user picks one.
    //
    // The keeper's donkey is an instance in the world (Mounts.Spawn, key "donkey"), spawned the first time it is called
    // and kept from then on, also with the save (KeepWithSaves): after a load it is found again by its key
    // (Mounts.GameReady, Mounts.Find), not spawned anew. A dismount leaves it standing where the keeper got off, with its
    // cart and cargo, and the interact key next to it rides it again (the core's own control). Its cart is this mod's too: left standing when
    // the keeper rides without it (Mounts.Unhitch), brought back and hitched when he wants it (ComeToKeeper, Hitch).
    //   RideKey (H)  on foot: the donkey comes and he rides on its back; riding: get off; in the cart: leave the cart
    //   CartKey (G)  on foot: the donkey comes with its cart and he drives; driving: get off; riding: take the cart
    //   SendAwayKey  the donkey and its cart go (their cargo is set down first); none by default
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

        // The API this mod is written against: 0.10 has per-mod ids, keys, GameReady and Find.
        private static readonly Version NeedsApi = new Version(0, 11, 0);

        // Its two instances, found again after a load by these keys.
        private const string DonkeyKey = "donkey", CartKey = "cart";

        internal static ManualLogSource Log;

        internal ConfigEntry<KeyboardShortcut> rideKey, cartKey, bellKey, sendAwayKey;
        internal ConfigEntry<bool> stayThroughDoors, dismountIndoors, showMessages, idleShuffle, keepWithSaves;
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
            sendAwayKey = Config.Bind("Keys", "SendAwayKey", KeyboardShortcut.Empty,
                "Send your donkey and its cart away (what is on the cart is set down where it stands). The next ride or cart key brings a new one. None = no key.");
            stayThroughDoors = Config.Bind("Behaviour", "StayMountedThroughDoors", false,
                "Keep riding through doors that load another area (the donkey is summoned again there). Off: the ride ends at such a door.");
            dismountIndoors = Config.Bind("Behaviour", "DismountIndoors", true,
                "Dismount when entering a building, church or dungeon, and don't mount inside: the cart cannot avoid furniture and door frames.");
            showMessages = Config.Bind("Behaviour", "ShowMessages", true,
                "The keeper says in a thought bubble why he cannot mount.");
            idleShuffle = Config.Bind("Behaviour", "IdleShuffle", true,
                "Your donkey, left standing without its cart, takes a few slow steps now and then instead of standing perfectly still.");
            keepWithSaves = Config.Bind("Behaviour", "KeepWithSaves", true,
                "Your donkey and its cart stay in the world across saves: loading the save brings them back where you left them, cargo included. Off: they are gone after a reload (their cargo lies where they stood).");

            if (Mounts.ApiVersion < NeedsApi)
            {
                Log.LogError($"GK2 Mounts API {Mounts.ApiVersion} is too old (this mod needs {NeedsApi} or newer). Update GK2 Mounts. The mount is off.");
                Status = $"Off: GK2 Mounts is too old (API {Mounts.ApiVersion}, needs {NeedsApi}).";
                FrameworkLink.TryRegister(Config);
                return;
            }
            Mounts.GameReady += OnGameReady;
            Mounts.InstanceRemoved += OnInstanceRemoved;
            keepWithSaves.SettingChanged += (_, __) => ApplyKeep();
            Mounts.Hitched += OnHitched;
            Mounts.Unhitched += OnUnhitched;
            foreach (ConfigEntry<bool> e in new[] { stayThroughDoors, dismountIndoors, showMessages, idleShuffle })
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
                else if (Pressed(sendAwayKey))
                {
                    SendAway();
                }
                if (Pressed(bellKey) && Mounts.IsMounted)
                {
                    Mounts.RingBell();
                }
                if (pending.HasValue)
                {
                    ContinuePending();
                }
            }
            catch (Exception e)
            {
                Log.LogError("Key handling failed: " + e);
            }
        }

        // The main key pressed this frame with the shortcut's own modifiers held (e.g. Shift+H); other keys held don't
        // matter. Not KeyboardShortcut.IsDown(): that also demands that no other key at all is held, so it never fires
        // while the player walks (W/A/S/D down), which is exactly when mounting matters (found in game 2026-09-30).
        private static bool Pressed(ConfigEntry<KeyboardShortcut> key)
        {
            KeyboardShortcut k = key.Value;
            if (k.MainKey == KeyCode.None || !UnityInput.Current.GetKeyDown(k.MainKey))
            {
                return false;
            }
            foreach (KeyCode m in k.Modifiers)
            {
                if (!UnityInput.Current.GetKey(m))
                {
                    return false;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ the keeper's donkey (all through the API)

        private MountHandle donkey; // this mod's donkey in the world (spawned the first time it is called)
        private MountHandle cart;   // this mod's cart while it stands on its own (left by an unhitch, or not yet hitched)

        // A ride asked for on foot, finished over the next frames: the donkey's view has to load first (IsReady), and
        // hitching or unhitching it builds a new view again.
        private MountStyle? pending;
        private float pendingUntil;

        private const float PendingSeconds = 5f;

        private void Use(MountStyle style)
        {
            if (Mounts.IsMounted)
            {
                UseWhileMounted(style);
                return;
            }
            // On foot: the donkey (spawned the first time) comes to the keeper, then the ride starts once it is ready.
            if (donkey == null || !donkey.Exists)
            {
                // Placed by ComeToKeeper right below.
                donkey = Mounts.Spawn(Guid, new MountSpawn { Position = Vector3.zero, Key = DonkeyKey, Persist = keepWithSaves.Value });
                if (donkey == null)
                {
                    Log.LogWarning("Could not spawn the donkey (see the GK2 Mounts log).");
                    return;
                }
                Log.LogInfo($"Spawned the keeper's donkey {donkey.FullId}.");
            }
            MountResult come = donkey.ComeToKeeper();
            if (!come.Ok)
            {
                Log.LogInfo("The donkey cannot come: " + come.Reason);
                return;
            }
            pending = style;
            pendingUntil = Time.unscaledTime + PendingSeconds;
            ContinuePending();
        }

        // Mounted in that style: get off (the donkey stays). Mounted in the other style: the cart is left standing
        // (to Ride) or brought and hitched (to Cart).
        private void UseWhileMounted(MountStyle style)
        {
            if (Mounts.Style == style)
            {
                Mounts.Dismount("dismounted");
                return;
            }
            MountHandle riding = Mounts.Riding;
            if (riding == null)
            {
                Mounts.SwitchStyle(style); // the summon of another mod (Mounts.TryMount): its own way
                return;
            }
            if (style == MountStyle.Ride)
            {
                MountHandle left = Mounts.UnhitchRidden();
                if (left != null && riding == donkey)
                {
                    cart = left;
                }
                return;
            }
            string why = BringCart(riding);
            if (why != null)
            {
                Log.LogInfo("No cart: " + why);
            }
        }

        // This mod's cart (spawned the first time) comes to where the donkey stands (or is ridden) and is hitched.
        private string BringCart(MountHandle to)
        {
            if (cart == null || !cart.Exists)
            {
                cart = Mounts.Spawn(Guid, new MountSpawn { Position = Vector3.zero, Donkey = false, WithCart = true, Key = CartKey, Persist = keepWithSaves.Value });
                if (cart == null)
                {
                    return "could not spawn a cart";
                }
            }
            MountResult come = cart.ComeToKeeper(to.FacingRight);
            if (!come.Ok)
            {
                return come.Reason;
            }
            MountResult hitched = Mounts.Hitch(to, cart);
            return hitched.Ok ? null : hitched.Reason;
        }

        private void ContinuePending()
        {
            if (Time.unscaledTime > pendingUntil || donkey == null || !donkey.Exists)
            {
                if (donkey != null && donkey.Exists)
                {
                    Log.LogInfo("The donkey did not become ready in time; press the key again.");
                }
                pending = null;
                return;
            }
            if (!donkey.IsReady)
            {
                return; // its view is loading (after a spawn, a move into this scene, a hitch or an unhitch)
            }
            MountStyle style = pending.Value;
            if (style == MountStyle.Cart && !donkey.HasCart)
            {
                string why = BringCart(donkey);
                if (why != null)
                {
                    Log.LogInfo("No cart: " + why);
                    pending = null;
                }
                return; // hitched: a new view, ready in a frame or two
            }
            if (style == MountStyle.Ride && donkey.HasCart)
            {
                cart = Mounts.Unhitch(donkey);
                if (cart == null)
                {
                    pending = null;
                }
                return;
            }
            pending = null;
            donkey.Ride();
        }

        // The donkey and its cart go for good; whatever is on a cart is set down where it stands first.
        private void SendAway()
        {
            pending = null;
            if (donkey != null && donkey.IsRidden)
            {
                Mounts.Dismount("sent away");
            }
            foreach (MountHandle h in new[] { donkey, cart })
            {
                if (h != null && h.Exists)
                {
                    h.SetDownCargo();
                    h.Remove();
                }
            }
            donkey = null;
            cart = null;
            Log.LogInfo("Sent the donkey away.");
        }

        // A game is ready: this mod's donkey and cart are the ones kept with the save, if any (found by key; their
        // numbers are this mod's own and stay the same).
        private void OnGameReady()
        {
            pending = null;
            donkey = Mounts.Find(Guid, DonkeyKey);
            cart = Mounts.Find(Guid, CartKey);
            if (donkey != null || cart != null)
            {
                Log.LogInfo($"Found the kept {(donkey != null ? "donkey " + donkey.FullId : "")}{(donkey != null && cart != null ? " and " : "")}{(cart != null ? "cart " + cart.FullId : "")}.");
            }
        }

        private void ApplyKeep()
        {
            foreach (MountHandle h in new[] { donkey, cart })
            {
                if (h != null && h.Exists)
                {
                    h.SetPersist(Guid, keepWithSaves.Value);
                }
            }
        }

        private void OnInstanceRemoved(MountHandle h)
        {
            if (h == donkey) donkey = null;
            if (h == cart) cart = null;
        }

        private void OnHitched(MountHandle d, MountHandle c)
        {
            if (c == cart) cart = null; // it is part of the donkey now
        }

        private void OnUnhitched(MountHandle d, MountHandle c)
        {
            if (d == donkey)
            {
                cart = c;
                c.SetKey(Guid, CartKey); // the cart left standing is found by its key after a load
            }
        }

        private void OnDestroy()
        {
            try
            {
                Mounts.GameReady -= OnGameReady;
                Mounts.InstanceRemoved -= OnInstanceRemoved;
                Mounts.Hitched -= OnHitched;
                Mounts.Unhitched -= OnUnhitched;
                Mounts.ClearTuning(Guid);
            }
            catch
            {
            }
        }
    }
}

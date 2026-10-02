using System;
using System.Collections.Generic;
using UnityEngine;

namespace GK2Mounts.Api
{
    // The two ways to use the mount: sit on the donkey, or ride it while it pulls its cart.
    public enum MountStyle
    {
        Ride,
        Cart,
    }

    // Parts of a ride another mod can switch off (Mounts.AddFeatureRule). Each also has the core's own settings; a
    // feature is on when those say so and every rule allows it. Riding in Cart style at all is not a feature: refuse
    // it with a mount rule on MountStyle.Cart, which can say why.
    public enum MountFeature
    {
        Cargo,            // the cart carries items: pickups land on it, set down with the interact key
        Lantern,          // the cart's lantern pole and lamp are shown
        LanternLight,     // the lamp is lit: its lights, glow and lit glass
        Sounds,           // every mount sound (hoofs, wheels, brays, graze, bell, cargo thump)
        Bell,             // the bell rings (on setting off, the bell key, Mounts.RingBell)
        StayThroughDoors, // the ride goes on through doors (the donkey is summoned again in the next scene)
    }

    // The answer to TryMount / SwitchStyle: Ok, or the reason it was refused (the text the keeper's bubble shows).
    public readonly struct MountResult
    {
        public bool Ok { get; }
        public string Reason { get; }

        private MountResult(bool ok, string reason)
        {
            Ok = ok;
            Reason = reason;
        }

        public static MountResult Success => new MountResult(true, null);

        public static MountResult Refused(string reason) => new MountResult(false, reason ?? "refused");

        public override string ToString() => Ok ? "ok" : "refused: " + Reason;
    }

    // Public API of GK2 Mounts for other mods (docs/mounts-api-design.md).
    //
    // Reference GK2Mounts.dll and declare [BepInDependency("dreas.gk2.mounts")]. Call from the Unity main thread only
    // (Update, event handlers, Harmony patches of game code). Everything goes through the core's own safety checks:
    // rules added here can refuse more, never allow what the core refuses (co-op, indoors, busy, building, saves).
    public static class Mounts
    {
        // The API's own version, separate from the mod version: minor for additions, major for breaking changes.
        public static readonly Version ApiVersion = new Version(0, 12, 0);

        // False when the core is switched off (General.Enabled) or disabled itself after a game update.
        public static bool IsAvailable => Plugin.Patched && Plugin.Enabled != null && Plugin.Enabled.Value;

        public static bool IsMounted => Mount.IsMounted;

        // The style being ridden; meaningless while not mounted.
        public static MountStyle Style => Mount.CurrentStyle;

        // Id of the mod that started the current ride (null while not mounted).
        public static string Owner => Mount.IsMounted ? owner : null;

        // Summon the donkey under the keeper in this style. Refused (with the reason) when the core or a rule says no,
        // when already mounted, or outside a loaded game. showMessage: say the reason in the keeper's bubble.
        public static MountResult TryMount(string ownerId, MountStyle style, bool showMessage = true)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                throw new ArgumentException("An owner id (your plugin GUID) is required.", nameof(ownerId));
            }
            if (!IsAvailable)
            {
                return MountResult.Refused("GK2 Mounts is disabled.");
            }
            if (Mount.IsMounted)
            {
                return MountResult.Refused("Already mounted.");
            }
            owner = ownerId; // before the attempt: Mounted handlers may read Owner
            string reason = Mount.TryMountFromApi(style, showMessage);
            if (reason != null)
            {
                owner = null;
                return MountResult.Refused(reason);
            }
            return MountResult.Success;
        }

        // End the ride (cargo is handed back to the keeper first). Does nothing while not mounted.
        public static void Dismount(string reason = null)
        {
            if (Mount.IsMounted)
            {
                Mount.DismountFromApi(string.IsNullOrEmpty(reason) ? "dismounted" : reason);
            }
        }

        // Mount again in the other style, in place. The rules for the new style are asked first; if one refuses,
        // the ride goes on unchanged and the refusal is returned.
        public static MountResult SwitchStyle(MountStyle style, bool showMessage = true)
        {
            if (!Mount.IsMounted)
            {
                return MountResult.Refused("Not mounted.");
            }
            if (Mount.CurrentStyle == style)
            {
                return MountResult.Success;
            }
            string reason = MountRules.WhyCannotMount(style);
            if (reason != null)
            {
                if (showMessage)
                {
                    Mount.Say(reason);
                }
                return MountResult.Refused(reason);
            }
            if (!Mount.Ridden.Ephemeral)
            {
                // A donkey standing in the world has its own cart or none: to Ride leaves the cart standing where it is
                // (Unhitch), to Cart needs a cart to hitch (Hitch).
                if (style == MountStyle.Ride)
                {
                    if (UnhitchRidden() != null)
                    {
                        return MountResult.Success;
                    }
                    if (showMessage)
                    {
                        Mount.Say("Not right now.");
                    }
                    return MountResult.Refused("Not right now.");
                }
                const string noCart = "This donkey has no cart here; ride it up to one.";
                if (showMessage)
                {
                    Mount.Say(noCart);
                }
                return MountResult.Refused(noCart);
            }
            string keepOwner = owner;
            Mount.DismountFromApi("style change");
            MountResult result = TryMount(keepOwner ?? Plugin.Guid, style, showMessage);
            return result;
        }

        // Both respect the Sounds feature (and RingBell the Bell feature).
        public static void RingBell()
        {
            if (Mount.IsMounted && MountRules.Allowed(MountFeature.Sounds) && MountRules.Allowed(MountFeature.Bell))
            {
                MountSounds.RingBell();
            }
        }

        public static void Bray()
        {
            if (Mount.IsMounted && MountRules.Allowed(MountFeature.Sounds))
            {
                MountSounds.Bray();
            }
        }

        // A rule asked before every mount: return null to allow, or the reason to refuse (shown in the keeper's
        // bubble). Rules from all mods must allow. Dispose the returned handle to remove the rule.
        public static IDisposable AddMountRule(string ownerId, Func<MountStyle, string> whyNot)
        {
            return MountRules.AddMount(ownerId, whyNot);
        }

        // A rule asked about twice a second while riding: return null to keep riding, or the reason to dismount
        // (shown in the keeper's bubble). Dispose the returned handle to remove the rule.
        public static IDisposable AddStayRule(string ownerId, Func<string> whyDismount)
        {
            return MountRules.AddStay(ownerId, whyDismount);
        }

        // A rule for one part of the ride: return false to switch it off. Asked at mount, about twice a second while
        // riding, and on RefreshRules(). Switching Cargo off mid-ride hands the cargo back to the keeper.
        public static IDisposable AddFeatureRule(string ownerId, MountFeature feature, Func<bool> allowed)
        {
            return MountRules.AddFeature(ownerId, feature, allowed);
        }

        // What the feature rules say right now (the core's own settings are not part of it).
        public static bool IsFeatureAllowed(MountFeature feature)
        {
            return MountRules.Allowed(feature);
        }

        // Ask the feature rules again now instead of within the next half second (e.g. right after your mod
        // unlocked something).
        public static void RefreshRules()
        {
            MountRules.Poll(force: true);
        }

        // The tuning values of a style, to read and to override for this session (see MountTuning).
        public static MountTuning Tuning(MountStyle style)
        {
            if (!Plugin.Patched && Plugin.Ride == null)
            {
                throw new InvalidOperationException("GK2 Mounts has not loaded its settings yet.");
            }
            if (style == MountStyle.Cart)
            {
                return cartTuning ?? (cartTuning = new MountTuning(MountStyle.Cart, Plugin.Cart));
            }
            return rideTuning ?? (rideTuning = new MountTuning(MountStyle.Ride, Plugin.Ride));
        }

        // The ride behaviour switches, for both styles, overridable like tuning (keys = their [General] cfg names):
        //   StayMountedThroughDoors  keep riding through doors that load another scene
        //   DismountIndoors          dismount entering a building, church or dungeon, and refuse to mount inside
        //   ShowMessages             the keeper's thought bubble for refusals
        //   IdleShuffle              donkeys standing in the world without a cart take a few slow steps now and then
        //                            (off in the core; how they step: Mounts.Shuffle). Per donkey: MountHandle.Tuning.
        public static MountTuning Behaviour
        {
            get
            {
                if (Plugin.ShowMessages == null)
                {
                    throw new InvalidOperationException("GK2 Mounts has not loaded its settings yet.");
                }
                return behaviour ?? (behaviour = new MountTuning("General", new ITuned[]
                {
                    Plugin.StayMountedThroughDoors, Plugin.DismountIndoors, Plugin.ShowMessages, IdleShuffle.On,
                }));
            }
        }

        // How a standing donkey shuffles when IdleShuffle is on, overridable like tuning (keys = their [Shuffle] cfg
        // names): ShuffleAreaWidth / ShuffleAreaDepth (the box around its home spot, either way), ShuffleStepMin /
        // ShuffleStepMax, ShuffleSpeed, ShuffleAnimSpeed, ShufflePauseMin / ShufflePauseMax, ShuffleMaxAngle (steepest
        // step from the screen's horizontal), ShuffleTurnChance, ShuffleKeeperDistance (holds still while the keeper
        // is closer). Its home spot is where it was spawned, left or last moved (MountHandle.MoveTo, ComeToKeeper).
        public static MountTuning Shuffle
        {
            get
            {
                if (IdleShuffle.On == null)
                {
                    throw new InvalidOperationException("GK2 Mounts has not loaded its settings yet.");
                }
                return shuffle ?? (shuffle = new MountTuning(IdleShuffle.Section, IdleShuffle.Settings));
            }
        }

        // The colours of the donkey, the cart and the lantern, for both styles, overridable like tuning. Keys: the
        // globals Enabled (bool), SpriteMethod (SpriteColorMethod), GlowColor, LitGlassTint, CharacterLightColor (hex
        // RRGGBB, "" = the game's), then for every region of ColorRegions its KeyOf(setting) keys ("CoatHue",
        // "HarnessSwaps", ...; MountColorRegion.SettingNames says what each does). Changes show within a frame.
        public static MountTuning Colors
        {
            get
            {
                if (GK2Mounts.Colors.Enabled == null)
                {
                    throw new InvalidOperationException("GK2 Mounts has not loaded its settings yet.");
                }
                return GK2Mounts.Colors.Tuning;
            }
        }

        // The colour regions, in panel order: Coat, Dark, Muzzle, Eye, Harness (the donkey), CartBody, Shafts, Straps,
        // Hooks, Wheels, Hubs (the cart), Pole, Lamp, Glass (the lantern).
        public static IReadOnlyList<MountColorRegion> ColorRegions => GK2Mounts.Colors.ApiRegions ?? Array.Empty<MountColorRegion>();

        // Removes every tuning, behaviour and colour override this owner set, on everyone's values and on every
        // instance's own.
        public static void ClearTuning(string ownerId)
        {
            Tuning(MountStyle.Ride).ClearOwner(ownerId);
            Tuning(MountStyle.Cart).ClearOwner(ownerId);
            Behaviour.ClearOwner(ownerId);
            Shuffle.ClearOwner(ownerId);
            Colors.ClearOwner(ownerId);
            foreach (MountHandle h in Instances)
            {
                h.Tuning.ClearOwner(ownerId);
            }
        }

        // ------------------------------------------------------------------ instances in the world

        // A donkey (withCart: with its cart) standing at position in the keeper's current scene, with its own settings
        // (MountHandle.Tuning). The keeper rides it with the interact key next to it, or MountHandle.Ride. Null outside
        // a game or while the core is off.
        public static MountHandle SpawnDonkey(string ownerId, Vector3 position, bool withCart = false, bool facingRight = true)
        {
            return Spawn(ownerId, position, hasDonkey: true, hasCart: withCart, facingRight: facingRight);
        }

        // A cart on its own (no donkey), shafts on the ground, at position in the keeper's current scene. A donkey
        // takes it with Hitch, or the keeper rides one up to it and presses the interact key (or General.HitchKey).
        public static MountHandle SpawnCart(string ownerId, Vector3 position, bool facingRight = true)
        {
            return Spawn(ownerId, position, hasDonkey: false, hasCart: true, facingRight: facingRight);
        }

        // Any instance, with every property (MountSpawn): a donkey, a donkey with its cart, a free cart (Donkey = false),
        // and the foal properties (Scale, Rideable, Parent). Null when refused; the log says why.
        public static MountHandle Spawn(string ownerId, MountSpawn spec)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                throw new ArgumentException("An owner id (your plugin GUID) is required.", nameof(ownerId));
            }
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }
            if (!IsAvailable)
            {
                return null;
            }
            if (spec.Parent != null && !spec.Parent.Exists)
            {
                Plugin.Log.LogInfo($"API: {ownerId} could not spawn: the parent is gone.");
                return null;
            }
            Mount.Instance i = Mount.Spawn(new Mount.SpawnArgs
            {
                Owner = ownerId,
                Position = spec.Position,
                HasDonkey = spec.Donkey,
                HasCart = spec.WithCart,
                FacingRight = spec.FacingRight,
                Scale = spec.Scale,
                Rideable = spec.Rideable,
                Parent = spec.Parent?.I,
                RemoveWithParent = spec.RemoveWithParent,
                SceneId = spec.SceneId,
                Persist = spec.Persist,
                Key = spec.Key,
                Id = spec.Id,
            }, out string why);
            if (i == null)
            {
                Plugin.Log.LogInfo($"API: {ownerId} could not spawn: {why}");
            }
            return i?.Handle;
        }

        // A foal of a donkey in the world: small (scale, 0.6 by default), not rideable, next to its parent, and with its
        // own idle shuffle switched on and made for a foal (smaller, quicker steps, a wider area around the parent; its
        // own settings, owned by ownerId, changeable through MountHandle.Tuning). It trots after the parent when the
        // parent moves, and goes when the parent is removed. Null when refused (see Mounts.Spawn).
        // id, key: as MountSpawn. persist: kept with the save; null: as its parent is.
        public static MountHandle SpawnFoal(string ownerId, MountHandle parent, float scale = 0.6f, int id = 0, string key = null, bool? persist = null)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (!parent.Exists)
            {
                return null;
            }
            Mount.Instance p = parent.I;
            Vector3 at = p.BodyPoint() + new Vector3(UnityEngine.Random.Range(-1.2f, 1.2f), 0f, UnityEngine.Random.Range(-0.5f, 0.5f));
            MountHandle foal = Spawn(ownerId, new MountSpawn
            {
                Position = at,
                FacingRight = UnityEngine.Random.value < 0.5f,
                Scale = scale,
                Rideable = false,
                Parent = parent,
                Persist = persist ?? p.Persist,
                Id = id,
                Key = key,
            });
            if (foal == null)
            {
                return null;
            }
            foal.I.PlaceNearParent(); // a free slot next to the parent, checked for ground and room
            InstanceTuning t = foal.Tuning;
            t.Set(ownerId, "IdleShuffle", true);
            t.Set(ownerId, "ShuffleAreaWidth", 0.9f);
            t.Set(ownerId, "ShuffleAreaDepth", 0.45f);
            t.Set(ownerId, "ShuffleStepMin", 0.2f);
            t.Set(ownerId, "ShuffleStepMax", 0.6f);
            t.Set(ownerId, "ShuffleSpeed", 0.45f);
            t.Set(ownerId, "ShufflePauseMin", 2.5f);
            t.Set(ownerId, "ShufflePauseMax", 8f);
            t.Set(ownerId, "ShuffleTurnChance", 0.35f);
            return foal;
        }

        private static MountHandle Spawn(string ownerId, Vector3 position, bool hasDonkey, bool hasCart, bool facingRight)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                throw new ArgumentException("An owner id (your plugin GUID) is required.", nameof(ownerId));
            }
            if (!IsAvailable)
            {
                return null;
            }
            Mount.Instance i = Mount.Spawn(new Mount.SpawnArgs { Owner = ownerId, Position = position, HasDonkey = hasDonkey, HasCart = hasCart, FacingRight = facingRight }, out string why);
            if (i == null)
            {
                Plugin.Log.LogInfo($"API: {ownerId} could not spawn: {why}");
            }
            return i?.Handle;
        }

        // ------------------------------------------------------------------ identity and the game being ready

        // The game is loaded and the save's kept donkeys are back (InstancesRestored has come): spawning works from now
        // on. False in the menu, while a game loads, and for a frame at a door that loads another scene.
        public static bool IsGameReady => Mount.IsGameReady;

        // Once per loaded game (a save, or a new game), after the save's kept donkeys are back, before anything else can
        // spawn: the place for a mod's own per-game spawning. Check Find(yourId, id) first; your kept donkeys are
        // already there. Spawning before it is refused (null, "Not until the game is ready").
        public static event Action GameReady;

        internal static void RaiseGameReady()
        {
            if (GameReady == null)
            {
                return;
            }
            foreach (Delegate d in GameReady.GetInvocationList())
            {
                try
                {
                    ((Action)d)();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"API: a GameReady handler failed: {e}");
                }
            }
        }

        // An owner's instance by its number (MountHandle.Id) or its key (MountHandle.Key), or null.
        public static MountHandle Find(string ownerId, int id) => Mount.FindInstance(ownerId, id)?.Handle;

        public static MountHandle Find(string ownerId, string key) => Mount.FindInstance(ownerId, key)?.Handle;

        // Every instance of one owner, in spawn order.
        public static IReadOnlyList<MountHandle> InstancesOf(string ownerId)
        {
            if (ownerId == null)
            {
                return new List<MountHandle>();
            }
            if (ofOwnerVersion != Mount.structureVersion)
            {
                ofOwner.Clear();
                ofOwnerVersion = Mount.structureVersion;
            }
            if (!ofOwner.TryGetValue(ownerId, out List<MountHandle> l))
            {
                l = new List<MountHandle>();
                foreach (Mount.Instance i in Mount.All)
                {
                    if (!i.Ephemeral && i.OwnerId == ownerId && i.Handle != null)
                    {
                        l.Add(i.Handle);
                    }
                }
                ofOwner[ownerId] = l;
            }
            return l;
        }

        // Cached per change of the set of instances (Mount.structureVersion): a mod reading Instances or InstancesOf
        // every frame no longer builds a list every frame. Each change makes new lists, so a list a caller holds stays
        // as it was (a snapshot, as before), also while it removes instances in a loop over it.
        private static List<MountHandle> allHandles;
        private static int allVersion = -1, ofOwnerVersion = -1;
        private static readonly Dictionary<string, List<MountHandle>> ofOwner = new Dictionary<string, List<MountHandle>>(StringComparer.Ordinal);

        // Owner-only changes (MountHandle.SetPersist, SetKey): its owner, or the core and its panel.
        internal static string WhyNotOwner(MountHandle h, string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                throw new ArgumentException("An owner id (your plugin GUID) is required.", nameof(ownerId));
            }
            if (!h.Exists)
            {
                return "It is gone.";
            }
            if (ownerId == h.I.OwnerId || Mount.IsCoreOwner(ownerId))
            {
                return null;
            }
            return $"Only its owner ({h.I.OwnerId}) can change that.";
        }

        // Every instance in the world (not the summon of TryMount), in spawn order.
        public static IReadOnlyList<MountHandle> Instances
        {
            get
            {
                if (allHandles == null || allVersion != Mount.structureVersion)
                {
                    var l = new List<MountHandle>();
                    foreach (Mount.Instance i in Mount.All)
                    {
                        if (!i.Ephemeral && i.Handle != null)
                        {
                            l.Add(i.Handle);
                        }
                    }
                    allHandles = l;
                    allVersion = Mount.structureVersion;
                }
                return allHandles;
            }
        }

        // The instance being ridden; null while on foot or riding the summon of TryMount.
        public static MountHandle Riding => Mount.Ridden != null && !Mount.Ridden.Ephemeral ? Mount.Ridden.Handle : null;

        // The donkey takes the free cart: the cart's cargo and cart settings come with it, the free cart is gone. A
        // standing donkey moves to the cart; a ridden one is put there with the keeper.
        public static MountResult Hitch(MountHandle donkey, MountHandle cart)
        {
            if (donkey == null || cart == null || !donkey.Exists || !cart.Exists)
            {
                return MountResult.Refused("Gone.");
            }
            if (!Mount.Instance.IsInGame(out PlayerController pc, out _))
            {
                return MountResult.Refused("Not in a game.");
            }
            string why;
            using (Mount.Bind(donkey.I))
            {
                why = donkey.I.Hitch(cart.I, pc);
            }
            return why == null ? MountResult.Success : MountResult.Refused(why);
        }

        // The donkey leaves its cart standing where it is (a new free cart with the cargo and the cart settings) and
        // goes on without it. Returns the new cart, or null (not a donkey with a cart, or not loaded right now).
        public static MountHandle Unhitch(MountHandle donkey)
        {
            if (donkey == null || !donkey.Exists)
            {
                return null;
            }
            return UnhitchInstance(donkey.I);
        }

        // The same for whatever is ridden, the summon of TryMount included.
        public static MountHandle UnhitchRidden()
        {
            return Mount.Ridden != null ? UnhitchInstance(Mount.Ridden) : null;
        }

        private static MountHandle UnhitchInstance(Mount.Instance i)
        {
            if (!Mount.Instance.IsInGame(out PlayerController pc, out _))
            {
                return null;
            }
            Mount.Instance cart;
            string why;
            using (Mount.Bind(i))
            {
                cart = i.Unhitch(pc, out why);
            }
            if (cart == null)
            {
                Plugin.Log.LogInfo("API: unhitch refused: " + why);
            }
            return cart?.Handle;
        }

        // An instance was spawned (also: a cart left standing by unhitching) / is gone (removed, hitched away, the game
        // left). The ride's own Mounted / Dismounted events still come for instances too.
        public static event Action<MountHandle> InstanceSpawned;

        public static event Action<MountHandle> InstanceRemoved;

        // (donkey, the free cart it took, which no longer exists) / (donkey, the new free cart).
        public static event Action<MountHandle, MountHandle> Hitched;

        public static event Action<MountHandle, MountHandle> Unhitched;

        // A save was loaded and the instances kept with it are back (MountSpawn.Persist), each also announced by
        // InstanceSpawned. Their ids are new: find yours by Owner and Key.
        public static event Action<IReadOnlyList<MountHandle>> InstancesRestored;

        internal static void RaiseInstancesRestored(IReadOnlyList<MountHandle> handles)
        {
            if (InstancesRestored == null)
            {
                return;
            }
            foreach (Delegate d in InstancesRestored.GetInvocationList())
            {
                try
                {
                    ((Action<IReadOnlyList<MountHandle>>)d)(handles);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"API: an InstancesRestored handler failed: {e}");
                }
            }
        }

        internal static void RaiseInstanceSpawned(MountHandle h) => Raise(InstanceSpawned, h, "InstanceSpawned");

        internal static void RaiseInstanceRemoved(MountHandle h) => Raise(InstanceRemoved, h, "InstanceRemoved");

        internal static void RaiseHitched(MountHandle donkey, MountHandle cart) => Raise2(Hitched, donkey, cart, "Hitched");

        internal static void RaiseUnhitched(MountHandle donkey, MountHandle cart) => Raise2(Unhitched, donkey, cart, "Unhitched");

        private static void Raise2(Action<MountHandle, MountHandle> handlers, MountHandle a, MountHandle b, string name)
        {
            if (handlers == null)
            {
                return;
            }
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<MountHandle, MountHandle>)d)(a, b);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"API: a {name} handler ({d.Method.DeclaringType?.Assembly.GetName().Name}) failed: {e.Message}");
                }
            }
        }

        private static MountTuning rideTuning, cartTuning, behaviour, shuffle;

        // Removes every rule this owner added.
        public static void RemoveRules(string ownerId)
        {
            MountRules.RemoveAll(ownerId);
        }

        // After the donkey is summoned (argument: the style). Handlers that throw are logged and skipped.
        public static event Action<MountStyle> Mounted;

        // After the ride ended, cargo handed back (argument: the reason, e.g. "dismounted", "indoors", a rule's text).
        public static event Action<string> Dismounted;

        // An item went onto the cart / left it (argument: the item id, e.g. "wood", "body_corpse"). Not raised for
        // the core's own bookkeeping: the hand-back around a save, or the cart summoned again after a door.
        public static event Action<string> CargoLoaded;

        public static event Action<string> CargoUnloaded;

        // ------------------------------------------------------------------ core side

        private static string owner;

        // A world instance's ride belongs to the mod that spawned it.
        internal static void SetRideOwner(string ownerId)
        {
            owner = ownerId;
        }

        internal static void RaiseMounted(MountStyle style)
        {
            Raise(Mounted, style, "Mounted");
        }

        internal static void RaiseDismounted(string reason)
        {
            owner = null;
            Raise(Dismounted, reason, "Dismounted");
        }

        internal static void RaiseCargoLoaded(string itemId)
        {
            Raise(CargoLoaded, itemId, "CargoLoaded");
        }

        internal static void RaiseCargoUnloaded(string itemId)
        {
            Raise(CargoUnloaded, itemId, "CargoUnloaded");
        }

        private static void Raise<T>(Action<T> handlers, T arg, string name)
        {
            if (handlers == null)
            {
                return;
            }
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<T>)d)(arg);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"API: a {name} handler ({d.Method.DeclaringType?.Assembly.GetName().Name}) failed: {e.Message}");
                }
            }
        }
    }

    // Rules added by other mods (internal; reached through Mounts.Add*Rule).
    internal static class MountRules
    {
        private sealed class Rule
        {
            public string Owner;
            public Func<MountStyle, string> Mount;
            public Func<string> Stay;
            public Func<bool> Feature;
            public MountFeature Which;
            public bool Warned;
        }

        private sealed class Handle : IDisposable
        {
            private Rule rule;

            public Handle(Rule r)
            {
                rule = r;
            }

            public void Dispose()
            {
                if (rule != null)
                {
                    rules.Remove(rule);
                    bool feature = rule.Feature != null;
                    rule = null;
                    if (feature)
                    {
                        Poll(force: true);
                    }
                }
            }
        }

        private static readonly List<Rule> rules = new List<Rule>();
        private static float nextStayCheck;
        private static float nextFeatureCheck;
        private static readonly int FeatureCount = Enum.GetValues(typeof(MountFeature)).Length;
        private static readonly bool[] allowed = NewAllowed();

        private static bool[] NewAllowed()
        {
            bool[] a = new bool[Enum.GetValues(typeof(MountFeature)).Length];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = true;
            }
            return a;
        }

        // The cached answer; refreshed by Poll (at mount, twice a second while riding, on RefreshRules).
        internal static bool Allowed(MountFeature f)
        {
            int i = (int)f;
            return i < 0 || i >= allowed.Length || allowed[i];
        }

        internal static IDisposable AddFeature(string ownerId, MountFeature f, Func<bool> fn)
        {
            IDisposable h = Add(new Rule { Owner = Check(ownerId, fn), Feature = fn, Which = f });
            Poll(force: true);
            return h;
        }

        // Re-asks the feature rules (when due, or now with force) and reacts to the features that changed.
        internal static void Poll(bool force)
        {
            float now = UnityEngine.Time.unscaledTime;
            if (!force && now < nextFeatureCheck)
            {
                return;
            }
            nextFeatureCheck = now + 0.5f;
            for (int i = 0; i < FeatureCount; i++)
            {
                MountFeature f = (MountFeature)i;
                bool ok = true;
                string by = null;
                foreach (Rule r in rules.ToArray())
                {
                    if (r.Feature == null || r.Which != f)
                    {
                        continue;
                    }
                    bool yes;
                    try
                    {
                        yes = r.Feature();
                    }
                    catch (Exception e)
                    {
                        if (!r.Warned)
                        {
                            r.Warned = true;
                            Plugin.Log.LogWarning($"API: a feature rule of {r.Owner} failed and is ignored (it allows): {e.Message}");
                        }
                        yes = true;
                    }
                    if (!yes)
                    {
                        ok = false;
                        by = r.Owner;
                        break;
                    }
                }
                if (ok != allowed[i])
                {
                    allowed[i] = ok;
                    Plugin.Log.LogInfo($"API: feature {f} {(ok ? "allowed again" : "switched off by " + by)}.");
                    Mount.OnFeatureChanged(f, ok);
                }
            }
        }

        internal static IDisposable AddMount(string ownerId, Func<MountStyle, string> f)
        {
            return Add(new Rule { Owner = Check(ownerId, f), Mount = f });
        }

        internal static IDisposable AddStay(string ownerId, Func<string> f)
        {
            return Add(new Rule { Owner = Check(ownerId, f), Stay = f });
        }

        internal static void RemoveAll(string ownerId)
        {
            rules.RemoveAll(r => r.Owner == ownerId);
            Poll(force: true);
        }

        private static string Check(string ownerId, object f)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                throw new ArgumentException("An owner id (your plugin GUID) is required.", nameof(ownerId));
            }
            if (f == null)
            {
                throw new ArgumentNullException("rule");
            }
            return ownerId;
        }

        private static IDisposable Add(Rule r)
        {
            rules.Add(r);
            return new Handle(r);
        }

        // Null = every rule allows. A rule that throws allows (it is logged once): the rules only restrict, the
        // core's safety checks never depend on them.
        internal static string WhyCannotMount(MountStyle style)
        {
            foreach (Rule r in rules.ToArray())
            {
                if (r.Mount == null)
                {
                    continue;
                }
                string why = Ask(r, () => r.Mount(style));
                if (why != null)
                {
                    return why;
                }
            }
            return null;
        }

        // Polled from the mount's frame update; asks the stay rules at most twice a second.
        internal static string WhyMustDismountIfDue()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < nextStayCheck)
            {
                return null;
            }
            nextStayCheck = now + 0.5f;
            foreach (Rule r in rules.ToArray())
            {
                if (r.Stay == null)
                {
                    continue;
                }
                string why = Ask(r, r.Stay);
                if (why != null)
                {
                    return why;
                }
            }
            return null;
        }

        private static string Ask(Rule r, Func<string> f)
        {
            try
            {
                string why = f();
                return string.IsNullOrEmpty(why) ? null : why;
            }
            catch (Exception e)
            {
                if (!r.Warned)
                {
                    r.Warned = true;
                    Plugin.Log.LogWarning($"API: a rule of {r.Owner} failed and is ignored (it allows): {e.Message}");
                }
                return null;
            }
        }
    }
}

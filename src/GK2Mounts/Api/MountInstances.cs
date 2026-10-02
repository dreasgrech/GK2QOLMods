using System;
using System.Collections.Generic;
using UnityEngine;

namespace GK2Mounts.Api
{
    // A donkey, a donkey with its cart, or a free cart standing in the world (Mounts.SpawnDonkey / SpawnCart). It
    // stays until removed (or the game is left): the keeper rides it with the interact key or Ride(), a dismount
    // leaves it where it is, and its cart can be left standing (Mounts.Unhitch) and taken again (Mounts.Hitch). Each
    // has its own settings on top of everyone's (Tuning). docs/mounts-instances.md has the details.
    //
    // A handle stays valid as an object after the instance is gone: Exists turns false and the actions refuse.
    public sealed class MountHandle
    {
        internal readonly Mount.Instance I;

        internal MountHandle(Mount.Instance i)
        {
            I = i;
            Tuning = new InstanceTuning(i);
        }

        // Its number within its owner: each mod numbers its own (two mods can both have a #2), kept with the save, so
        // the same donkey has the same number after every load. Given at spawn (MountSpawn.Id, or the owner's next).
        public int Id => I.Id;

        // The mod that spawned it (a cart left standing by unhitching: the donkey's owner; the core's GUID after the summon).
        public string Owner => I.OwnerId;

        // Its identity: owner#id, e.g. dreas.gk2.showcase#1.
        public string FullId => I.FullId;

        public bool Exists => Mount.IsRegistered(I);

        public bool HasDonkey => I.HasDonkey;

        public bool HasCart => I.HasCart;

        public bool IsRidden => I.IsRidden;

        // The scene it stands in (the game's scene id, e.g. the main world or an interior). Its view is there while that
        // scene is loaded.
        public string SceneId => I.SceneId;

        // Where the donkey's root stands (a free cart: where its donkey would stand). Follows the ride while ridden.
        public Vector3 Position => I.Position;

        public bool FacingRight => I.FacingRight;

        // The item ids on its cart, in load order (empty without a cart).
        public IReadOnlyList<string> CargoIds => I.CargoRig.Ids();

        // This instance's own settings: any key of Mounts.Tuning, Mounts.Behaviour, Mounts.Shuffle or Mounts.Colors.
        // E.g. Tuning.Set(myId, "IdleShuffle", true) lets just this donkey shuffle.
        public InstanceTuning Tuning { get; }

        // Taking an idle-shuffle step right now (Mounts.Behaviour "IdleShuffle"), or a foal trotting after its parent.
        // Only a standing donkey without a cart.
        public bool IsStepping => Exists && I.IsStepping;

        // The size of its view (1 = a full-size donkey; a foal is smaller). Set at spawn (MountSpawn.Scale).
        public float Scale => I.ViewScale;

        // False for a foal (MountSpawn.Rideable): no interact prompt, Ride and Hitch refused.
        public bool IsRideable => I.Rideable && I.HasDonkey;

        // The donkey it stays with (MountSpawn.Parent, Mounts.SpawnFoal), or null.
        public MountHandle Parent => I.Parent?.Handle;

        // With a Parent: whether it is removed with the parent (MountSpawn.RemoveWithParent).
        public bool RemoveWithParent => I.RemoveWithParent;

        // Kept with the save (MountSpawn.Persist, SetPersist): after each save it is written to the slot's side file
        // (<slot>.gk2mounts.dat next to the saves, synced by Steam Cloud with them), and loading that save spawns it
        // again where it stood, with its number, cargo, own settings and data (docs/mounts-persistence.md).
        public bool Persist => I.Persist;

        // Only its owner changes it (and the core's panel): another mod keeping it while its owner spawns its own each
        // session would make two. Its children of the same owner follow. Never the summon of TryMount.
        public MountResult SetPersist(string ownerId, bool keep)
        {
            string why = Mounts.WhyNotOwner(this, ownerId);
            if (why != null)
            {
                return MountResult.Refused(why);
            }
            if (I.Ephemeral)
            {
                return MountResult.Refused("The summon of TryMount is never kept.");
            }
            I.Persist = keep;
            foreach (Mount.Instance c in I.Children())
            {
                if (c.OwnerId == I.OwnerId)
                {
                    c.Persist = keep;
                }
            }
            return MountResult.Success;
        }

        // The owner's own name for it (MountSpawn.Key), unique within the owner and kept with the save: after a load
        // Mounts.Find(owner, key) finds it.
        public string Key => I.Key;

        // Only its owner (or the core's panel); null clears it. Refused when the owner already uses the key.
        public MountResult SetKey(string ownerId, string key)
        {
            string why = Mounts.WhyNotOwner(this, ownerId);
            if (why != null)
            {
                return MountResult.Refused(why);
            }
            if (string.IsNullOrEmpty(key))
            {
                I.Key = null;
                return MountResult.Success;
            }
            if (I.Key == key)
            {
                return MountResult.Success;
            }
            if (Mount.KeyTaken(I.OwnerId, key))
            {
                return MountResult.Refused($"{I.OwnerId} already has one with the key '{key}'.");
            }
            I.Key = key;
            return MountResult.Success;
        }

        // A mod's own values on this instance, kept with the save: a name, a home, a mood. Each mod has its own
        // namespace (its ownerId), so two mods can't overwrite each other, and any mod may keep values on any instance
        // (a naming mod naming another mod's donkeys). Hitching moves the free cart's values onto the donkey.
        public string GetData(string ownerId, string key)
        {
            return ownerId != null && key != null && I.Data.TryGetValue(ownerId, out Dictionary<string, string> d) && d.TryGetValue(key, out string v) ? v : null;
        }

        // null clears it.
        public void SetData(string ownerId, string key, string value)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                throw new ArgumentException("An owner id (your plugin GUID) is required.", nameof(ownerId));
            }
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A key is required.", nameof(key));
            }
            if (!I.Data.TryGetValue(ownerId, out Dictionary<string, string> d))
            {
                if (value == null)
                {
                    return;
                }
                I.Data[ownerId] = d = new Dictionary<string, string>(StringComparer.Ordinal);
            }
            if (value == null)
            {
                d.Remove(key);
                if (d.Count == 0)
                {
                    I.Data.Remove(ownerId);
                }
            }
            else
            {
                d[key] = value;
            }
        }

        // The keys a mod keeps on this instance.
        public IReadOnlyList<string> DataKeys(string ownerId)
        {
            return ownerId != null && I.Data.TryGetValue(ownerId, out Dictionary<string, string> d) ? new List<string>(d.Keys) : new List<string>();
        }

        // The instances whose parent it is, in spawn order.
        public IReadOnlyList<MountHandle> Children
        {
            get
            {
                var l = new List<MountHandle>();
                foreach (Mount.Instance c in I.Children())
                {
                    if (c.Handle != null) l.Add(c.Handle);
                }
                return l;
            }
        }

        // The keeper gets on (it must have a donkey, stand in the keeper's scene with its view ready, and every mount
        // rule must allow). The keeper is put on its seat.
        public MountResult Ride(bool showMessage = true)
        {
            if (!Exists)
            {
                return MountResult.Refused("It is gone.");
            }
            if (!Mount.Instance.IsInGame(out PlayerController pc, out _))
            {
                return MountResult.Refused("Not in a game.");
            }
            string why;
            using (Mount.Bind(I))
            {
                why = I.RideFromWorld(pc, showMessage);
            }
            return why == null ? MountResult.Success : MountResult.Refused(why);
        }

        // A standing instance goes to another place (ground height found under it); refused while ridden.
        public MountResult MoveTo(Vector3 position, bool? facingRight = null)
        {
            if (!Exists)
            {
                return MountResult.Refused("It is gone.");
            }
            if (I.IsRidden)
            {
                return MountResult.Refused("Not while it is ridden.");
            }
            using (Mount.Bind(I))
            {
                I.MoveTo(position, facingRight.HasValue ? (facingRight.Value ? 1f : -1f) : (float?)null);
            }
            return MountResult.Success;
        }

        // Its view is spawned and prepared in the keeper's scene: Ride, and the keeper's controls, work from now on.
        // After a spawn or ComeToKeeper this takes a frame or a few (the game loads the view's parts).
        public bool IsReady
        {
            get
            {
                PlayerData pd = MainGame.Instance != null ? MainGame.PlayerData : null;
                return Exists && I.Ready && pd != null && I.SceneId == pd.currentGameSceneId;
            }
        }

        // It comes to the keeper: into his scene, to where a ride would put it, facing his way (default: as he faces),
        // so that Ride right after moves no one. A cart comes with its donkey. Refused while ridden or outside a game.
        // A donkey called while the keeper is on foot is called for a ride: when a ride would be refused here (indoors,
        // busy, building, co-op, a mod's rule, too small), it stays where it is and the refusal is shown like a ride's
        // (showMessage). forRide false brings it anyway; a cart, or anything called while the keeper rides, always comes.
        public MountResult ComeToKeeper(bool? facingRight = null, bool forRide = true, bool showMessage = true)
        {
            if (!Exists)
            {
                return MountResult.Refused("It is gone.");
            }
            if (I.IsRidden)
            {
                return MountResult.Refused("Not while it is ridden.");
            }
            if (!Mount.Instance.IsInGame(out PlayerController pc, out GameScene scene))
            {
                return MountResult.Refused("Not in a game.");
            }
            bool right = facingRight ?? pc.PlayerData.Direction.x >= 0f;
            using (Mount.Bind(I))
            {
                if (forRide && I.HasDonkey && !Mounts.IsMounted)
                {
                    string why = I.WhyCannotRideHere(pc, showMessage);
                    if (why != null)
                    {
                        return MountResult.Refused(why);
                    }
                }
                I.ComeTo(pc, scene, right ? 1f : -1f);
            }
            return MountResult.Success;
        }

        // On foot at this standing cart: the topmost item the keeper holds that the cart takes goes onto it, one per
        // call (with the perk for several held items, call again for the next). Refused while the keeper rides, for a
        // ridden or cart-less one, in another scene, when the cart is full or takes none of what is held, and when a
        // rule switched Cargo off. showMessage: the refusal in a thought bubble.
        public MountResult LoadFromHead(bool showMessage = true)
        {
            return CargoStep(showMessage, load: true);
        }

        // On foot at this standing cart: its top item into the keeper's hands. Refused as LoadFromHead, and when the
        // hands are full or the cart is empty.
        public MountResult TakeToHead(bool showMessage = true)
        {
            return CargoStep(showMessage, load: false);
        }

        private MountResult CargoStep(bool showMessage, bool load)
        {
            string why = null;
            PlayerData pd = MainGame.PlayerData;
            if (!Exists)
            {
                why = "It is gone.";
            }
            else if (!I.HasCart)
            {
                why = "It has no cart.";
            }
            else if (Mounts.IsMounted)
            {
                why = I.IsRidden ? "Ridden: pickups go onto the cart already." : "Not while riding.";
            }
            else if (pd == null || I.SceneId != pd.currentGameSceneId || !I.Ready)
            {
                why = "The cart is not here.";
            }
            if (why == null)
            {
                using (Mount.Bind(I))
                {
                    why = load ? I.CargoRig.LoadOneFromHead(pd, out _) : I.CargoRig.TakeOneToHead(pd, out _);
                }
            }
            if (why == null)
            {
                return MountResult.Success;
            }
            if (showMessage)
            {
                Mount.Say(why);
            }
            return MountResult.Refused(why);
        }

        // Everything on its cart onto the ground at the cart, for good; returns how many. Call it before Remove to keep
        // the cargo in the world. Nothing while ridden (the ride sets items down with the interact key).
        public int SetDownCargo()
        {
            if (!Exists)
            {
                return 0;
            }
            using (Mount.Bind(I))
            {
                return I.SetDownCargo();
            }
        }

        // Gone for good, with anything on its cart (SetDownCargo first to keep it). A ridden one is dismounted first.
        public void Remove()
        {
            Mount.Remove(I, "removed");
        }

        public override string ToString() => $"MountHandle {I.Describe()}{(Exists ? "" : " (gone)")}";
    }

    // What to spawn (Mounts.Spawn). Fields can be added in later API versions without breaking callers.
    public sealed class MountSpawn
    {
        // Where its root stands (ground height found under it), in the keeper's scene (a child: its parent's scene).
        public Vector3 Position;

        // Another scene to stand in (a scene id as MountHandle.SceneId gives); null: the keeper's. In another scene the
        // height is used as given (its ground can't be tested while it isn't loaded).
        public string SceneId;

        // Kept with the save (MountHandle.Persist); a foal of Mounts.SpawnFoal takes its parent's unless told.
        public bool Persist;

        // The owner's own name for it, unique within the owner, kept with the save (MountHandle.Key).
        public string Key;

        // The owner's number for it (MountHandle.Id): refused when the owner already has that number (also one kept
        // with the save that isn't back, dormant). 0: the owner's next number, never one it has had before.
        public int Id;

        public bool FacingRight = true;

        // False: a cart on its own (WithCart must be true).
        public bool Donkey = true;

        public bool WithCart;

        // The view's size (0.2 to 2; 1 = full size). Anything but 1 needs Rideable = false and no cart: the ride and the
        // cart are made for a full-size donkey.
        public float Scale = 1f;

        // False: the keeper can't ride it (no interact prompt, Ride refused) and it is never hitched.
        public bool Rideable = true;

        // A donkey in the world it stays with: its idle-shuffle home is the parent (switch the shuffle on for it, as
        // Mounts.SpawnFoal does), it trots after the parent when the parent moves, and catches up out of sight when it
        // can't. No chains (a child can't be a parent); any number of children.
        public MountHandle Parent;

        // With a Parent: removed when the parent is (default), else it stays as an orphan and shuffles where it is.
        public bool RemoveWithParent = true;
    }

    // The settings of one instance: overrides by key on top of everyone's value (the cfg and the session overrides of
    // Mounts.Tuning / Behaviour / Shuffle / Colors). Keys and types are those of Mounts.Tuning(style), Mounts.Behaviour,
    // Mounts.Shuffle and Mounts.Colors; style keys apply in whichever style the instance is in. Values are clamped like everywhere else.
    // Cart keys (Cart*, Cargo*, Lantern*, the Cart and Lantern colour regions, the lamp colours) travel with the cart
    // when it is left standing or hitched.
    public sealed class InstanceTuning
    {
        private readonly Mount.Instance i;

        internal InstanceTuning(Mount.Instance i)
        {
            this.i = i;
        }

        // The value in effect for this instance.
        public T Get<T>(string key)
        {
            ITuned t = Find(key);
            object v;
            using (Mount.Bind(i))
            {
                v = i.Overrides.TryGetValue(t.Key, out object o) ? o : t.Current;
            }
            if (!(v is T r))
            {
                throw new ArgumentException($"{key} is a {t.ValueType.Name}, not a {typeof(T).Name}.");
            }
            return r;
        }

        public bool IsSet(string key) => i.Overrides.ContainsKey(Find(key).Key);

        // Owner id of this instance's override of the key, or null.
        public string SetBy(string key) => i.OverrideOwners.TryGetValue(Find(key).Key, out string o) ? o : null;

        // Every key this instance overrides.
        public IReadOnlyList<string> Keys => new List<string>(i.Overrides.Keys);

        // Returns the value applied (clamped).
        public T Set<T>(string ownerId, string key, T value)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                throw new ArgumentException("An owner id (your plugin GUID) is required.", nameof(ownerId));
            }
            ITuned t = Find(key);
            object v = t.Clamp(value);
            i.Overrides[t.Key] = v;
            i.OverrideOwners[t.Key] = ownerId;
            Changed();
            return (T)v;
        }

        public void Clear(string key)
        {
            ITuned t = Find(key);
            if (i.Overrides.Remove(t.Key))
            {
                i.OverrideOwners.Remove(t.Key);
                Changed();
            }
        }

        // Every override of this instance.
        public void Reset()
        {
            if (i.Overrides.Count == 0)
            {
                return;
            }
            i.Overrides.Clear();
            i.OverrideOwners.Clear();
            Changed();
        }

        internal void ClearOwner(string ownerId)
        {
            bool any = false;
            foreach (var kv in new List<KeyValuePair<string, string>>(i.OverrideOwners))
            {
                if (kv.Value == ownerId)
                {
                    i.Overrides.Remove(kv.Key);
                    i.OverrideOwners.Remove(kv.Key);
                    any = true;
                }
            }
            if (any)
            {
                Changed();
            }
        }

        private void Changed()
        {
            i.ColorsRig.MarkDirty();
            using (Mount.Bind(i))
            {
                i.OnTuningChanged(i.CurrentStyle);
                i.LanternRig.Refresh();
                i.CargoRig.RefreshViews();
            }
        }

        // Style keys from the instance's own style first, then the other style, behaviour and colours.
        internal ITuned Find(string key)
        {
            MountStyle other = i.CurrentStyle == MountStyle.Cart ? MountStyle.Ride : MountStyle.Cart;
            if (Mounts.Tuning(i.CurrentStyle).TryFind(key, out ITuned t) || Mounts.Tuning(other).TryFind(key, out t)
                || Mounts.Behaviour.TryFind(key, out t) || Mounts.Shuffle.TryFind(key, out t) || Mounts.Colors.TryFind(key, out t))
            {
                return t;
            }
            throw new ArgumentException($"No setting '{key}'. See Mounts.Tuning(style).Keys, Mounts.Behaviour.Keys, Mounts.Shuffle.Keys and Mounts.Colors.Keys.", nameof(key));
        }
    }
}

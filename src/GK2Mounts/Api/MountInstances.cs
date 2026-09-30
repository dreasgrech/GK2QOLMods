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

        public int Id => I.Id;

        // The mod that spawned it (a cart left standing by unhitching: the donkey's owner; the core's GUID after the summon).
        public string Owner => I.OwnerId;

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
        public MountResult ComeToKeeper(bool? facingRight = null)
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
                I.ComeTo(pc, scene, right ? 1f : -1f);
            }
            return MountResult.Success;
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
        private ITuned Find(string key)
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

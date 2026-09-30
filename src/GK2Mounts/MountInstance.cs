using System;
using System.Collections.Generic;
using GK2Mounts.Api;
using UnityEngine;

namespace GK2Mounts
{
    // The instance half of Mount.Instance: what one donkey (or free cart) in the world is besides the view code in
    // Mount.cs. docs/mounts-instances.md has the design.
    //
    // An instance is one donkey prefab view with everything the mod does to it: the cart parts, the cargo on it
    // (Cargo.Rig), the lantern (Lantern.Rig with its hanging lamp) and the colours (Colors.Rig), plus its own setting
    // overrides. It is either ridden (Mount.Ridden: it follows the keeper, the old code path) or standing where it was
    // left (StandTick). The old summon (Mounts.TryMount, the showcase's keys) is an ephemeral instance: made at mount,
    // gone at dismount with its cargo handed back, as before. Every other one stays in the world until removed.
    internal static partial class Mount
    {
        internal sealed partial class Instance
        {
            // ------------------------------------------------------------------ identity

            internal readonly int Id;
            internal readonly string OwnerId;   // the mod that spawned it; the core's own GUID for carts left by unhitching
            internal readonly bool Ephemeral;   // the old summon: made at mount, gone at dismount
            internal bool HasDonkey;            // false: a free cart (the donkey's own renderers are kept off)
            internal bool HasCart => activeStyle == MountStyle.Cart;
            internal bool IsRidden => mounted;
            internal bool Ready => donkey != null && donkey && partsReady;

            // Where it stands: the donkey root's place (WgoData.Position; the donkey's body is drawn half a unit to its
            // facing side of it, a free cart's shafts point that way). Kept up to date while ridden (Follow).
            internal Vector3 Position;
            internal string SceneId;

            // A rider was on this view (set at mount and when the rider is attached): DestroyDonkey then also takes the
            // rider's side down (sounds, near leg, late-update hook), as the single-donkey code always did.
            private bool riderAttached;

            internal readonly Cargo.Rig CargoRig;
            internal readonly Lantern.Rig LanternRig;
            internal readonly Colors.Rig ColorsRig;
            internal MountHandle Handle;

            // Setting overrides of this instance by key name (any tuning, colour or behaviour key), read by Tuned<T>
            // while this instance is bound (Mount.Current), and the mod that set each.
            internal readonly Dictionary<string, object> Overrides = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, string> OverrideOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            internal Instance(int id, string ownerId, bool ephemeral, bool hasDonkey, bool hasCart)
            {
                Id = id;
                OwnerId = ownerId;
                Ephemeral = ephemeral;
                HasDonkey = hasDonkey;
                activeStyle = hasCart ? MountStyle.Cart : MountStyle.Ride;
                CargoRig = new Cargo.Rig(this);
                LanternRig = new Lantern.Rig(this);
                ColorsRig = new Colors.Rig();
            }

            internal string KindName => !HasDonkey ? "free cart" : HasCart ? "donkey with cart" : "donkey";

            internal string Describe() => $"#{Id} {KindName}";

            // ------------------------------------------------------------------ standing in the world

            private float nextSpawnTry;
            private int spawnFailures;

            // A frame of an instance that is not ridden (bound): its view is spawned while its scene is loaded, then
            // posed where it stands, idle, with its cart on the ground, lantern and colours as when ridden.
            internal void StandTick(PlayerController pc)
            {
                if (donkey == null || !donkey)
                {
                    if (donkey != null)
                    {
                        // Unity null: the game destroyed the view (its scene unloaded; OnViewDestroyedByGame restored it).
                        donkey = null;
                        partsReady = false;
                        cartParts.Clear();
                    }
                    GameScene scene = LoadedScene(SceneId);
                    if (scene == null || Time.unscaledTime < nextSpawnTry)
                    {
                        return;
                    }
                    if (TrySpawn(pc, scene))
                    {
                        spawnFailures = 0;
                    }
                    else
                    {
                        spawnFailures++;
                        nextSpawnTry = Time.unscaledTime + Mathf.Min(60f, Mathf.Pow(2f, spawnFailures - 1));
                    }
                    return;
                }
                if (!partsReady)
                {
                    if (donkey.MainWgoPart == null)
                    {
                        return;
                    }
                    PrepareParts(pc);
                    if (!partsReady)
                    {
                        return;
                    }
                }
                Stand();
                RevealDonkeyIfDue();
                if (HasCart)
                {
                    LanternRig.Tick();
                }
                ColorsRig.Tick();
            }

            private void Stand()
            {
                donkey.Data.MovableDirection = new Vector2(riderFacingX, 0f);
                donkey.Data.Position = VisualConsts.GetRoundedPosXYZ(Position, PixelSize());
                if (lastMirrored != AnimationState.Idle)
                {
                    lastMirrored = AnimationState.Idle;
                    donkey.Data.SetStateToAnimator(AnimationState.Idle);
                }
                if (cartParts.Count > 0)
                {
                    Vector3 bodyGround = bodySprite != null && bodySprite ? bodySprite.position : donkey.transform.position;
                    FollowCartParts(bodyGround, Position.y);
                    if (!HasDonkey)
                    {
                        LowerShafts();
                    }
                }
            }

            // A free cart rests its shafts on the ground: turned about their cart end until the harness end, which
            // otherwise hangs at the donkey's shoulder, is ShaftRestAboveGround above the ground under it. Measured once
            // per place (MoveTo measures again), in the shafts' parent space like PoseShafts.
            private const float ShaftRestAboveGround = 0.04f;
            private float shaftDropDeg = float.NaN;
            private Vector3 shaftDropFor;

            private void LowerShafts()
            {
                CartPart p = cartParts.Find(c => c.IsShafts);
                if (p == null || p.T == null || !p.T || p.T.parent == null)
                {
                    return;
                }
                // What FollowCartParts gave the shafts this frame (the cart's lift on a slope), kept on top of the turn.
                Vector3 lift = p.T.localPosition - p.Base;
                if (p.Written && p.Pitched)
                {
                    lift = Vector3.zero; // pitched about the axle: its own pose, not a lift; the turn replaces it
                }
                if (float.IsNaN(shaftDropDeg) || (Position - shaftDropFor).sqrMagnitude > 0.0001f)
                {
                    shaftDropFor = Position;
                    shaftDropDeg = 0f;
                    Transform parent = p.T.parent;
                    Vector3 harnessW = parent.TransformPoint(p.Harness + lift);
                    float g = GroundHeight(harnessW, out _);
                    if (float.IsNaN(g))
                    {
                        g = Position.y;
                    }
                    float dropW = harnessW.y - g - ShaftRestAboveGround;
                    if (dropW > 0.01f && Mathf.Abs(p.ReachX) > 0.05f)
                    {
                        float dropL = parent.InverseTransformVector(Vector3.up * dropW).y;
                        // The harness end sits -ReachX from the cart end along x; a turn by θ about z lowers it by
                        // ReachX·sin θ.
                        shaftDropDeg = Mathf.Asin(Mathf.Clamp(dropL / p.ReachX, -0.95f, 0.95f)) * Mathf.Rad2Deg;
                    }
                    Plugin.Log.LogInfo($"Mount: {Describe()}: shafts rest on the ground, harness end {dropW:F2} above it, turned {shaftDropDeg:F1}° about the cart end.");
                }
                Vector3 cartEnd = p.Harness + new Vector3(p.ReachX, 0f, 0f);
                Quaternion r = Quaternion.AngleAxis(shaftDropDeg, Vector3.forward);
                p.T.localRotation = r * p.AnimRot;
                p.T.localPosition = cartEnd + r * (p.Base - cartEnd) + lift;
                p.WrittenRot = p.T.localRotation;
                p.Written = true;
            }

            // ------------------------------------------------------------------ a free cart's look

            // The donkey's own renderers under the prefab (GK2-mount-probe.txt): the body and head sprites, the up/down
            // body, its shadow and the grass it bends. Everything else is the cart.
            private static readonly string[] DonkeyOnlyNames = { "bdy", "hed", "-bdy", "Cylinder Shadowcaster", "DeformTextureSprite" };
            private readonly HashSet<Renderer> donkeyOnly = new HashSet<Renderer>();

            private void HideDonkeyOnly()
            {
                foreach (Renderer r in donkey.GetComponentsInChildren<Renderer>(includeInactive: true))
                {
                    if (Array.IndexOf(DonkeyOnlyNames, r.name) < 0 || HasCartPartAncestor(r.transform))
                    {
                        continue;
                    }
                    donkeyOnly.Add(r);
                    r.forceRenderingOff = true;
                }
            }

            // ------------------------------------------------------------------ the view back to the game's pool

            // The prefab's own state of the cart part nodes (active, local pose), taken before the mod moves them.
            private readonly List<(Transform T, bool Active, Vector3 Pos, Quaternion Rot)> partRest = new List<(Transform, bool, Vector3, Quaternion)>();

            private void RememberPartRest()
            {
                partRest.Clear();
                foreach (Transform t in donkey.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (Array.IndexOf(CartPartNames, t.name) >= 0 && t.name != Cargo.ContainerName && !HasCartPartAncestor(t))
                    {
                        partRest.Add((t, t.gameObject.activeSelf, t.localPosition, t.localRotation));
                    }
                }
            }

            // The colliders and navmesh cuts PrepareParts switches off (and the layer it moves the colliders to): the
            // game's own donkey needs them, and it may be built from these very parts.
            private readonly List<(Collider C, bool Active, bool Enabled, int Layer)> colliderRest = new List<(Collider, bool, bool, int)>();
            private readonly List<(GameObject Go, bool Active)> cutRest = new List<(GameObject, bool)>();

            private void RememberColliders()
            {
                colliderRest.Clear();
                cutRest.Clear();
                foreach (Collider c in donkey.GetComponentsInChildren<Collider>(includeInactive: true))
                {
                    colliderRest.Add((c, c.gameObject.activeSelf, c.enabled, c.gameObject.layer));
                }
                foreach (NavMeshCutBoxCustom cut in donkey.GetComponentsInChildren<NavMeshCutBoxCustom>(includeInactive: true))
                {
                    cutRest.Add((cut.gameObject, cut.gameObject.activeSelf));
                }
            }

            private void RestoreColliders()
            {
                foreach (var c in colliderRest)
                {
                    if (c.C == null || !c.C) continue;
                    c.C.gameObject.layer = c.Layer;
                    c.C.enabled = c.Enabled;
                    if (c.C.gameObject.activeSelf != c.Active) c.C.gameObject.SetActive(c.Active);
                }
                foreach (var g in cutRest)
                {
                    if (g.Go != null && g.Go && g.Go.activeSelf != g.Active) g.Go.SetActive(g.Active);
                }
                colliderRest.Clear();
                cutRest.Clear();
            }

            // Everything the mod changed on the view back to the prefab's state, while the parts still exist: before
            // the mod destroys the view, and from Wgo.OnDestroy when the game does (its scene unloads). The game pools
            // the parts, and the next donkey built from them (another instance, or the game's own delivery donkey) must
            // inherit nothing: no hidden renderer, no colour, no posed lantern or split lamp, no cargo holder, no moved
            // or switched-off cart part. The instance keeps its cargo list (the rig's mirror), settings and place.
            internal void ReleaseView()
            {
                foreach (Renderer r in hiddenRenderers)
                {
                    if (r != null && r) r.forceRenderingOff = false;
                }
                hiddenRenderers.Clear();
                foreach (Renderer r in donkeyOnly)
                {
                    if (r != null && r) r.forceRenderingOff = false;
                }
                donkeyOnly.Clear();
                try { ColorsRig.Detach(); } catch (Exception e) { Plugin.Log.LogWarning($"Mount: {Describe()}: restoring the colours failed: {e.Message}"); }
                try { LanternRig.Detach(); } catch (Exception e) { Plugin.Log.LogWarning($"Mount: {Describe()}: restoring the lantern failed: {e.Message}"); }
                try { CargoRig.Detach(); } catch (Exception e) { Plugin.Log.LogWarning($"Mount: {Describe()}: removing the cargo holder failed: {e.Message}"); }
                foreach (var pr in partRest)
                {
                    if (pr.T == null || !pr.T) continue;
                    pr.T.localPosition = pr.Pos;
                    pr.T.localRotation = pr.Rot;
                    if (pr.T.gameObject.activeSelf != pr.Active) pr.T.gameObject.SetActive(pr.Active);
                }
                partRest.Clear();
                try { RestoreColliders(); } catch (Exception e) { Plugin.Log.LogWarning($"Mount: {Describe()}: restoring the colliders failed: {e.Message}"); }
                shaftDropDeg = float.NaN;
            }

            // Wgo.OnDestroy of this instance's view, called by the game (its scene is unloading).
            internal void OnViewDestroyedByGame()
            {
                Plugin.Log.LogInfo($"Mount: {Describe()}: the game removed its view (scene {SceneId} unloading); restored for the pool, it comes back with the scene.");
                ReleaseView();
                if (mounted)
                {
                    RiderLeg.Detach();
                }
                partsReady = false;
                cartParts.Clear();
                bodySprite = null;
                donkeyBodyRenderer = null;
            }

            // The instance goes for good (Mounts.Remove, the panel, leaving the game).
            internal void Discard()
            {
                DestroyDonkey();
                ColorsRig.Release();
            }

            // ------------------------------------------------------------------ getting on and off

            // Where the keeper stands to sit on this donkey with its root at 'root': Follow's placement solved for the
            // keeper (root = keeper + away·Depth − right·(lateral + fore)).
            private Vector3 KeeperSpotFor(Vector3 root, float facingX, StyleSettings s = null)
            {
                StyleSettings S = s ?? this.S;
                Vector3 away = AwayFromCamera();
                Vector3 right = Vector3.Cross(Vector3.up, away).normalized;
                float lateral = 0f;
                if (Mathf.Abs(S.CenterOffset.Value) > 0.0001f && bodySprite != null && bodySprite && donkey != null && donkey)
                {
                    // Measured in this view, drawn in its facing; the other facing mirrors it.
                    lateral = Vector3.Dot(bodySprite.position - donkey.transform.position, right);
                    if (facingX * riderFacingX < 0f)
                    {
                        lateral = -lateral;
                    }
                }
                float sign = Mathf.Abs(lateral) > 0.001f ? Mathf.Sign(lateral) : facingX;
                float fore = S.RiderOffsetPx.Value * 0.02f * sign;
                Vector3 k = root - away * S.Depth.Value + right * (lateral + fore);
                k.y = root.y;
                return k;
            }

            // The keeper gets onto this standing donkey (the interact key next to it, or MountHandle.Ride): the view is
            // taken over as it is (cargo, lantern, colours stay), and the keeper is put on its seat so it does not jump.
            internal string RideFromWorld(PlayerController pc, bool showMessage)
            {
                if (!HasDonkey)
                {
                    return "That is a cart without a donkey.";
                }
                if (Ridden != null)
                {
                    return "Already mounted.";
                }
                if (!Ready || SceneId != pc.PlayerData.currentGameSceneId)
                {
                    return "It isn't here right now.";
                }
                string reason = WhyCannotMount(pc) ?? MountRules.WhyCannotMount(activeStyle);
                if (reason != null)
                {
                    Plugin.Log.LogInfo($"Mount refused ({Describe()}): " + reason);
                    if (showMessage)
                    {
                        Say(reason);
                    }
                    return reason;
                }
                MountRules.Poll(force: true);
                // Called to the keeper and not moved since (ComeToKeeper, the showcase's keys): he stays where he is,
                // which is where its place was worked out from. Otherwise the spot on its seat, from its place (not its
                // view's transform: the view follows a move only in the next StandTick, and riding in the same frame put
                // the keeper at the old spot, found 2026-09-30).
                if (!(cameFor.HasValue && (Position - cameFor.Value.Place).sqrMagnitude < 0.0001f))
                {
                    pc.SetPosition(KeeperSpotFor(Position, riderFacingX));
                }
                cameFor = null;
                mounted = true;
                Ridden = this;
                riderAttached = true;
                MountSounds.OnMounted();
                pc.PhysicalBody.SetFacingDirection(new Vector2(riderFacingX, 0f));
                pc.PhysicalBody.SpeedMultiplier = S.SpeedMultiplier.Value;
                donkey.MainWgoPart?.AnimationComponent?.SetWalkAnimationSpeedMultiplier(1.5f * pc.PhysicalBody.SpeedMultiplier);
                if (!S.AutoSeat.Value)
                {
                    ApplySeat(pc, S.SeatOffset.Value);
                }
                try
                {
                    AttachRider(pc);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Mount: attaching the rider to {Describe()} failed: {e.Message}");
                }
                lastMirrored = (AnimationState)(-1);
                if (HasCart)
                {
                    CargoRig.LoadFromHead("mount");
                }
                Plugin.Log.LogInfo($"Mounted {Describe()} ({activeStyle}, speed x{S.SpeedMultiplier.Value}).");
                Mounts.SetRideOwner(OwnerId);
                Mounts.RaiseMounted(activeStyle);
                return null;
            }

            // Every dismount of an instance in the world: the rider comes off, the donkey stays where it is with its
            // cart and cargo (nothing is handed back), and stands from the next frame.
            private void Park(PlayerController pc, string reason)
            {
                mounted = false;
                if (Ridden == this)
                {
                    Ridden = null;
                }
                gearHideUntilFrame = Time.frameCount + 8;
                if (donkey != null && donkey)
                {
                    Position = donkey.Data.Position;
                    donkey.MainWgoPart?.AnimationComponent?.SetWalkAnimationSpeedMultiplier(1.5f);
                }
                RestoreSeat();
                RestoreSpeed(pc);
                RestorePose(pc);
                RiderLeg.Detach();
                UnhookKeeperLateUpdate();
                seatPending = false;
                MountSounds.OnDonkeyGone();
                riderAttached = false;
                lastMirrored = (AnimationState)(-1);
                Plugin.Log.LogInfo($"Dismounted ({reason}); {Describe()} stays at {Position:F2} in {SceneId} with {CargoRig.Count} item(s).");
                Mounts.RaiseDismounted(reason);
            }

            // A new view in another style, in place (hitching, unhitching): the old view goes back to the pool, a new
            // one is spawned (at the keeper while ridden, else where it stands) and prepared next frame, the rider
            // (if any) staying on. Cargo waiting in the rig's mirror goes onto the new cart.
            private void Restyle(PlayerController pc, GameScene scene, MountStyle style)
            {
                if (mounted)
                {
                    RestoreSeat();
                }
                DestroyDonkey();
                activeStyle = style;
                lastMirrored = (AnimationState)(-1);
                if (!TrySpawn(pc, scene))
                {
                    if (mounted)
                    {
                        Dismount(pc, "could not re-summon the donkey");
                    }
                    return;
                }
                if (mounted)
                {
                    pc.PhysicalBody.SpeedMultiplier = S.SpeedMultiplier.Value;
                    if (!S.AutoSeat.Value)
                    {
                        ApplySeat(pc, S.SeatOffset.Value);
                    }
                    ApplyPoseNow();
                    Mounts.RaiseMounted(activeStyle); // as after Mounts.SwitchStyle: the ride goes on in this style
                }
            }

            // The instance comes to the keeper (MountHandle.ComeToKeeper): into his scene, to the place a ride would put
            // it (Follow's placement for the keeper where he stands), facing his way, so that riding it moves no one.
            // Set by ComeTo: the place it came to and where the keeper stood then (RideFromWorld leaves him there).
            private (Vector3 Place, Vector3 Keeper)? cameFor;

            internal void ComeTo(PlayerController pc, GameScene scene, float facingX)
            {
                float shownFacing = riderFacingX; // the facing the view is drawn in now
                riderFacingX = facingX >= 0f ? 1f : -1f;
                PlayerData pd = pc.PlayerData;
                Rigidbody rb = pc.PhysicalBody.Rb;
                Vector3 basePos = rb != null ? rb.transform.position : pd.position.Value;
                Vector3 away = AwayFromCamera();
                Vector3 right = Vector3.Cross(Vector3.up, away).normalized;
                float lateral = 0f;
                if (Mathf.Abs(S.CenterOffset.Value) > 0.0001f)
                {
                    if (bodySprite != null && bodySprite && donkey != null && donkey)
                    {
                        // Measured in the facing the view shows; the other facing mirrors it.
                        lateral = Vector3.Dot(bodySprite.position - donkey.transform.position, right);
                        if (shownFacing * riderFacingX < 0f)
                        {
                            lateral = -lateral;
                        }
                    }
                    else
                    {
                        lateral = S.CenterOffset.Value * riderFacingX; // Follow's own fallback without a measured body
                    }
                }
                float sign = Mathf.Abs(lateral) > 0.001f ? Mathf.Sign(lateral) : riderFacingX;
                float fore = S.RiderOffsetPx.Value * 0.02f * sign;
                Vector3 target = SnapToGround(basePos + away * S.Depth.Value - right * (lateral + fore));
                if (SceneId != scene.Id)
                {
                    DestroyDonkey(); // spawned again in the keeper's scene by StandTick
                    SceneId = scene.Id;
                    nextSpawnTry = 0f;
                    spawnFailures = 0;
                }
                Position = target;
                cameFor = (target, basePos);
                shaftDropDeg = float.NaN;
                if (donkey != null && donkey && partsReady)
                {
                    Stand(); // the view there now, not next frame
                }
            }

            // Sets everything on this cart down on the ground at it, for good (a mod removing its cart keeps the cargo in
            // the world this way). Not while ridden: the ride has its own set-down. Returns how many were set down.
            internal int SetDownCargo()
            {
                if (mounted)
                {
                    return 0;
                }
                int n = 0;
                Vector3 at = CartPoint();
                foreach (Item it in CargoRig.ItemsNow())
                {
                    if (MainGame.Instance.dropSystem.DropItem(it, SceneId, at))
                    {
                        CargoRig.Forget(it);
                        n++;
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"Cargo: the game refused to set {it.id} of {Describe()} down; it stays on the cart.");
                    }
                }
                if (n > 0)
                {
                    Plugin.Log.LogInfo($"Cargo: {Describe()}: {n} item(s) set down at the cart.");
                }
                return n;
            }

            // Moves a standing instance (MountHandle.MoveTo). Ground height from the ground under the point.
            internal void MoveTo(Vector3 position, float? facingX)
            {
                Position = SnapToGround(position);
                if (facingX.HasValue)
                {
                    riderFacingX = facingX.Value >= 0f ? 1f : -1f;
                }
            }

            // ------------------------------------------------------------------ hitching

            // This donkey (without a cart) takes the free cart 'cart': the cart's cargo and its cart settings come over,
            // the free cart goes, and this instance goes on in Cart style. Ridden: the keeper is moved so the donkey
            // stands where the cart's donkey would, facing its way. Standing: this donkey moves to the cart.
            internal string Hitch(Instance cart, PlayerController pc)
            {
                if (!HasDonkey || HasCart)
                {
                    return "This donkey already pulls a cart.";
                }
                if (cart == null || cart.HasDonkey || cart.IsRidden || !IsRegistered(cart))
                {
                    return "That is not a free cart.";
                }
                if (cart.SceneId != SceneId)
                {
                    return "The cart is somewhere else.";
                }
                GameScene scene = LoadedScene(SceneId);
                if (scene == null)
                {
                    return "Not while the scene is not loaded.";
                }
                if (mounted)
                {
                    string rule = MountRules.WhyCannotMount(MountStyle.Cart);
                    if (rule != null)
                    {
                        return rule;
                    }
                }
                List<Item> items = cart.CargoRig.TakeAllForTransfer();
                MoveCartSettings(cart, this);
                Vector3 at = cart.Position;
                float facing = cart.riderFacingX;
                Unregister(cart);
                cart.Discard();
                if (mounted)
                {
                    pc.SetPosition(KeeperSpotFor(at, facing, Plugin.For(MountStyle.Cart))); // measured in the current view, before the facing changes; Cart's seat values
                    riderFacingX = facing;
                    pc.PhysicalBody.SetFacingDirection(new Vector2(facing, 0f));
                }
                else
                {
                    Position = at;
                }
                riderFacingX = facing;
                CargoRig.SetCarried(items);
                Restyle(pc, scene, MountStyle.Cart);
                Plugin.Log.LogInfo($"Mount: {Describe()} hitched cart #{cart.Id} ({items.Count} item(s) on it).");
                Mounts.RaiseHitched(Handle, cart.Handle);
                return null;
            }

            // This donkey leaves its cart standing where it is (a new free cart with the cargo and the cart settings) and
            // goes on without it. Returns the new cart.
            internal Instance Unhitch(PlayerController pc, out string why)
            {
                why = null;
                if (!HasDonkey || !HasCart)
                {
                    why = "No cart to leave.";
                    return null;
                }
                GameScene scene = LoadedScene(SceneId);
                if (scene == null || donkey == null || !donkey)
                {
                    why = "Not right now.";
                    return null;
                }
                var cart = new Instance(NextId(), OwnerId ?? Plugin.Guid, ephemeral: false, hasDonkey: false, hasCart: true)
                {
                    Position = donkey.Data.Position,
                    SceneId = SceneId,
                    riderFacingX = riderFacingX,
                };
                MoveCartSettings(this, cart);
                cart.CargoRig.SetCarried(CargoRig.TakeAllForTransfer());
                Register(cart);
                Restyle(pc, scene, MountStyle.Ride);
                Plugin.Log.LogInfo($"Mount: {Describe()} left its cart as {cart.Describe()} at {cart.Position:F2} ({cart.CargoRig.Count} item(s) on it).");
                Mounts.RaiseUnhitched(Handle, cart.Handle);
                return cart;
            }

            // The overrides that belong to the cart (IsCartKey) move from one instance to the other; the target's own
            // cart overrides are replaced.
            private static void MoveCartSettings(Instance from, Instance to)
            {
                foreach (string k in new List<string>(to.Overrides.Keys))
                {
                    if (IsCartKey(k))
                    {
                        to.Overrides.Remove(k);
                        to.OverrideOwners.Remove(k);
                    }
                }
                foreach (var kv in new List<KeyValuePair<string, object>>(from.Overrides))
                {
                    if (!IsCartKey(kv.Key)) continue;
                    to.Overrides[kv.Key] = kv.Value;
                    if (from.OverrideOwners.TryGetValue(kv.Key, out string o)) to.OverrideOwners[kv.Key] = o;
                    from.Overrides.Remove(kv.Key);
                    from.OverrideOwners.Remove(kv.Key);
                }
                from.ColorsRig.MarkDirty();
                to.ColorsRig.MarkDirty();
            }

            // ------------------------------------------------------------------ places

            // The world point of the cart body (cargo is set down there around a save); the root without a view.
            internal Vector3 CartPoint()
            {
                if (cartBodyPart != null && cartBodyPart.T != null && cartBodyPart.T)
                {
                    Vector3 w = cartBodyPart.T.parent.TransformPoint(cartBodyPart.Sample);
                    return new Vector3(w.x, Position.y, w.z);
                }
                return Position;
            }

            // Where the interaction hint stands: the prefab's own BubblePoint (the point the game puts its donkey's
            // bubbles at); a free cart's is over its cart body, since its donkey is not there.
            internal Vector3 HintPoint()
            {
                if (HasDonkey && donkey != null && donkey)
                {
                    if (bubblePoint == null || !bubblePoint)
                    {
                        bubblePoint = null;
                        foreach (Transform t in donkey.GetComponentsInChildren<Transform>(includeInactive: true))
                        {
                            if (t.name == "BubblePoint")
                            {
                                bubblePoint = t;
                                break;
                            }
                        }
                    }
                    if (bubblePoint != null)
                    {
                        return bubblePoint.position;
                    }
                }
                return CartPoint() + Vector3.up * 1.3f;
            }

            private Transform bubblePoint;

            // Where the keeper's reach is measured to: the donkey's body (a free cart: where its donkey's body would be).
            internal Vector3 BodyPoint()
            {
                if (bodySprite != null && bodySprite)
                {
                    Vector3 b = bodySprite.position;
                    return new Vector3(b.x, Position.y, b.z);
                }
                return Position;
            }
        }
    }
}

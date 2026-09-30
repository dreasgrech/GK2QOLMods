using System.Collections.Generic;
using UnityEngine;

namespace GK2Mounts
{
    // Foals (docs/mounts-foals.md): small donkeys that can't be ridden and stay with a parent donkey. Three general
    // properties of an instance, each usable on its own (Mounts.Spawn with a MountSpawn):
    //   ViewScale  the view's size: WgoData.Scale, which Wgo.Spawn puts on the view's own new root (the game's own
    //              mechanism, used for town decor), so the pooled parts are never scaled. Only for instances that can't
    //              be ridden and have no cart (the ride and cart offsets are made for a full-size donkey).
    //   Rideable   false: no interact prompt, Ride refused, never hitched.
    //   Parent     its idle-shuffle home is the parent's body instead of where it was left. When the parent moves (a
    //              shuffle step, a ride) the foal trots after it in straight lines while the way is clear; when it can't
    //              (blocked, too far, another scene) it shuffles where it is, and catches up out of sight once the
    //              parent stands again.
    // Mounts.SpawnFoal is the convenience over the three (scale 0.6, not rideable, its own shuffle switched on).
    internal static partial class Mount
    {
        internal sealed partial class Instance
        {
            internal float ViewScale = 1f;
            internal bool Rideable = true;
            internal Instance Parent;
            internal bool RemoveWithParent = true;

            private bool followLost;            // could not follow (blocked or too far): shuffles where it is until caught up
            private bool wedged;                // no step fits where it stands (spawned against a wall): caught up like a lost one
            private int pickFailsInRow;
            private float nextAheadCheck;
            private float followSpeedNow;
            private Vector3 parentSeenAt;
            private float parentStillSince;
            private int followsLogged;

            internal List<Instance> Children()
            {
                var l = new List<Instance>();
                foreach (Instance o in All)
                {
                    if (o.Parent == this)
                    {
                        l.Add(o);
                    }
                }
                return l;
            }

            private int SiblingIndex()
            {
                if (Parent == null)
                {
                    return 0;
                }
                int n = 0;
                foreach (Instance o in All)
                {
                    if (o == this)
                    {
                        return n;
                    }
                    if (o.Parent == Parent)
                    {
                        n++;
                    }
                }
                return n;
            }

            // How long the parent has stood where it is (it moves while ridden or shuffling).
            private float ParentStillFor()
            {
                Instance p = Parent;
                if ((p.Position - parentSeenAt).sqrMagnitude > 0.0004f)
                {
                    parentSeenAt = p.Position;
                    parentStillSince = Time.time;
                }
                return Time.time - parentStillSince;
            }

            // Where this foal belongs next to its parent: behind it (behind its cart when it has one), each sibling in its
            // own slot so they do not stack. Body points.
            private static readonly Vector2[] Slots =
            {
                new Vector2(-1.1f, 0.35f), new Vector2(-1.1f, -0.35f), new Vector2(-1.7f, 0f),
                new Vector2(0.9f, 0.45f), new Vector2(0.9f, -0.45f), new Vector2(-2.2f, 0.3f),
            };

            private Vector3 SlotNearParent(int index)
            {
                Instance p = Parent;
                Vector3 away = AwayFromCamera();
                Vector3 right = Vector3.Cross(Vector3.up, away).normalized;
                float worldToRight = Vector3.Dot(Vector3.right, right) >= 0f ? 1f : -1f;
                Vector3 forward = right * (p.riderFacingX * worldToRight);
                Vector2 s = Slots[index % Slots.Length];
                float x = s.x < 0f && p.HasCart ? s.x - 1.5f : s.x; // the cart is behind the donkey
                Vector3 at = p.BodyPoint() + forward * x + away * s.y;
                at.y = p.Position.y;
                return at;
            }

            // The parent's part of a shuffle frame. True when it used the frame (following, or waiting for the parent);
            // false lets the ordinary shuffle run (around the parent, or around its own spot while it could not follow).
            private bool FollowParentTick(PlayerController pc)
            {
                Instance p = Parent;
                if (p == null)
                {
                    return false;
                }
                if (!IsRegistered(p))
                {
                    Parent = null; // an orphan now: it shuffles where it is
                    return false;
                }
                float still = ParentStillFor();
                if (p.SceneId != SceneId || !p.Ready)
                {
                    StopFollowing();
                    shuffleHold = "waiting for its parent (somewhere else)";
                    if (!p.IsRidden && p.Ready && still >= 1.5f)
                    {
                        CatchUp("its parent is in another place");
                    }
                    return true;
                }
                Vector3 slot = SlotNearParent(SiblingIndex());
                Vector3 body = BodyAt(Position, riderFacingX);
                float d = FlatDistance(body, slot);                // arriving: at its slot
                float dHome = FlatDistance(body, p.BodyPoint());   // starting and giving up: from the parent itself
                float homeRange = Mathf.Max(IdleShuffle.AreaWidth.Value, IdleShuffle.AreaDepth.Value) + 0.6f;
                float maxFollow = IdleShuffle.FollowMaxDistance.Value;
                if (shufflePhase == ShufflePhase.Following)
                {
                    if (d < 0.25f)
                    {
                        StopFollowing();
                        EndStep(firstPause: true);
                        return false;
                    }
                    if (dHome > maxFollow)
                    {
                        LoseParent(body, "too far to follow");
                        EndStep(firstPause: false);
                    }
                    else
                    {
                        FollowStep(slot, body, d, Mathf.Min(Time.deltaTime, 0.1f));
                        return true;
                    }
                }
                if (shufflePhase == ShufflePhase.Turning)
                {
                    return false; // the ordinary code finishes the turn, then goes on following
                }
                if (followLost)
                {
                    if (dHome < homeRange && !wedged)
                    {
                        followLost = false; // the parent came back to it
                    }
                    else if (!p.IsRidden && still >= 1.5f && (OffScreen(body) || still >= 6f))
                    {
                        CatchUp("its parent stands again");
                        return true;
                    }
                    else
                    {
                        return false; // shuffles around its own spot (the home LoseParent set)
                    }
                }
                if (dHome > maxFollow && !IsStepping)
                {
                    LoseParent(body, "too far to follow");
                    return false;
                }
                if (dHome > homeRange && !IsStepping)
                {
                    shufflePhase = ShufflePhase.Following;
                    nextAheadCheck = 0f;
                    if (followsLogged < 5)
                    {
                        followsLogged++;
                        Plugin.Log.LogInfo($"[foal] {Describe()} follows its parent #{p.Id} ({dHome:F1} away).");
                    }
                    return true;
                }
                return false;
            }

            // One frame of trotting towards the slot: the body moves in a straight line, the root stays half a (scaled)
            // body behind it on the facing side; a clear change of side turns it around first (the body kept in place).
            private void FollowStep(Vector3 slot, Vector3 body, float dist, float dt)
            {
                Vector3 away = AwayFromCamera();
                Vector3 right = Vector3.Cross(Vector3.up, away).normalized;
                float worldToRight = Vector3.Dot(Vector3.right, right) >= 0f ? 1f : -1f;
                Vector3 to = slot - body;
                to.y = 0f;
                Vector3 dir = dist > 0.0001f ? to / dist : Vector3.zero;
                float side = Vector3.Dot(dir, right) * worldToRight; // + means world right, the facing's sign
                if (Mathf.Abs(side) > 0.35f && Mathf.Sign(side) != riderFacingX && shuffleLateralKnown)
                {
                    float nf = Mathf.Sign(side);
                    turnRoot = body - right * LateralFor(nf);
                    turnRoot.y = Position.y;
                    riderFacingX = nf;
                    shuffleTurnFrame = Time.frameCount;
                    shufflePhase = ShufflePhase.Turning;
                    afterTurn = ShufflePhase.Following;
                    return;
                }
                if (Time.time >= nextAheadCheck)
                {
                    nextAheadCheck = Time.time + 0.25f;
                    if (!LineWalkable(body, body + dir * Mathf.Min(0.6f, dist), "follow"))
                    {
                        LoseParent(body, "the way is blocked");
                        EndStep(firstPause: false);
                        return;
                    }
                }
                // Full speed until the last half unit, then slowing down to arrive.
                followSpeedNow = IdleShuffle.FollowSpeed.Value * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(dist / 0.6f));
                float move = Mathf.Min(followSpeedNow * dt, dist);
                Vector3 bodyNew = body + dir * move;
                float g = GroundHeight(bodyNew, out _);
                Vector3 root = bodyNew - right * LateralFor(riderFacingX);
                root.y = float.IsNaN(g) ? Position.y : g;
                SetShufflePosition(root);
            }

            private void StopFollowing()
            {
                if (shufflePhase == ShufflePhase.Following)
                {
                    shufflePhase = ShufflePhase.Waiting;
                }
            }

            private void LoseParent(Vector3 body, string why)
            {
                StopFollowing();
                if (!followLost)
                {
                    followLost = true;
                    shuffleHome = body;
                    Plugin.Log.LogInfo($"[foal] {Describe()} can't follow its parent #{Parent.Id} ({why}); it waits and catches up out of sight.");
                }
            }

            // Straight to its slot next to the parent (into the parent's scene if need be): when nobody sees the jump,
            // or the parent has stood a while.
            // Right after SpawnFoal: its slot next to the parent, checked for ground and room (the spawn point is not).
            internal void PlaceNearParent()
            {
                if (Parent != null && IsRegistered(Parent) && Parent.SceneId == SceneId)
                {
                    CatchUp("spawned");
                }
            }

            // A pick that found no step (IdleShuffle.cs). Three in a row with a parent: it is wedged where it stands (a
            // fence, a wall, a slope), and the catch-up moves it to a free slot.
            private void NotePickFailed()
            {
                pickFailsInRow++;
                if (Parent != null && pickFailsInRow >= 3 && !followLost)
                {
                    wedged = true;
                    LoseParent(BodyAt(Position, riderFacingX), "no step fits where it stands");
                }
            }

            private void NotePickOk()
            {
                pickFailsInRow = 0;
            }

            private void CatchUp(string why)
            {
                Instance p = Parent;
                Vector3 away = AwayFromCamera();
                Vector3 right = Vector3.Cross(Vector3.up, away).normalized;
                Vector3 slot = SlotNearParent(SiblingIndex());
                if (p.Ready)
                {
                    // Its own slot, else any free one with a clear line from the parent, else next to the parent.
                    Vector3 pb = p.BodyPoint();
                    bool found = LineWalkable(pb, slot, "catch-up");
                    for (int k = 0; !found && k < Slots.Length; k++)
                    {
                        Vector3 s = SlotNearParent(k);
                        if (LineWalkable(pb, s, "catch-up"))
                        {
                            slot = s;
                            found = true;
                        }
                    }
                    if (!found)
                    {
                        slot = pb + away * 0.4f;
                    }
                }
                if (SceneId != p.SceneId)
                {
                    DestroyDonkey(); // spawned again in the parent's scene by StandTick
                    SceneId = p.SceneId;
                    nextSpawnTry = 0f;
                    spawnFailures = 0;
                }
                Vector3 root = SnapToGround(slot - right * LateralFor(riderFacingX));
                SetShufflePosition(root);
                followLost = false;
                wedged = false;
                pickFailsInRow = 0;
                shufflePhase = ShufflePhase.Waiting;
                EndStep(firstPause: true);
                Plugin.Log.LogInfo($"[foal] {Describe()} caught up with its parent #{p.Id} ({why}) at {root:F2} in {SceneId}.");
            }

            // StandTick without a view (its scene is not loaded): a foal whose parent stands in another, loaded scene
            // goes there.
            private void FollowWithoutView()
            {
                Instance p = Parent;
                if (p == null || !IsRegistered(p) || p.SceneId == SceneId || p.IsRidden || !p.Ready)
                {
                    return;
                }
                if (ParentStillFor() >= 1.5f)
                {
                    CatchUp("its parent stands in another place");
                }
            }

            private static float FlatDistance(Vector3 a, Vector3 b)
            {
                float dx = a.x - b.x, dz = a.z - b.z;
                return Mathf.Sqrt(dx * dx + dz * dz);
            }

            private static bool OffScreen(Vector3 world)
            {
                try
                {
                    if (CameraSystem.Instance == null)
                    {
                        return true;
                    }
                    Vector3 s = CameraSystem.WorldToScreenPoint(world);
                    return s.z <= 0f || s.x < -80f || s.x > Screen.width + 80f || s.y < -80f || s.y > Screen.height + 160f;
                }
                catch
                {
                    return true;
                }
            }
        }
    }
}

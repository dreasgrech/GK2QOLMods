using System;
using BepInEx.Configuration;
using UnityEngine;

namespace GK2Mounts
{
    // Idle shuffle (docs/mounts-roaming.md): a donkey standing in the world without a cart takes a few slow steps now
    // and then, inside a small box around its home spot, so it does not stand perfectly still. Off in the core
    // (General.IdleShuffle); a mod switches it on through Mounts.Behaviour, for everyone or per instance.
    //
    // The mod moves the donkey itself, as Follow does for the ridden one: the instance's Position along a short straight
    // step, ground-snapped, the walk animation slowed to match, the facing always the step's side. The game's
    // MovementComponent is not used: every arrival fires GlobalEventsSystem WgoGoToFinished with the definition id
    // (npc_donkey), which quest checks listen to. No route finding: a step is checked for level ground and nothing in
    // the way along its line (the game's own test for where the keeper can stand: no collider of the keeper-blocking
    // layer 8 there, PlayerColliderTester), and is never steeper than ShuffleMaxAngle from the screen's horizontal, so
    // the donkey never walks straight up or down (its art is side views only). The NPC navmesh is not required: it does
    // not cover everywhere the keeper walks (found 2026-09-30: every step was refused where the keeper left it).
    internal static class IdleShuffle
    {
        internal const string Section = "Shuffle";

        // [General]: the switch, a behaviour key (Mounts.Behaviour).
        internal static Tuned<bool> On;

        // [Shuffle]: how (Mounts.Shuffle).
        internal static Tuned<float> AreaWidth, AreaDepth, StepMin, StepMax, Speed, AnimSpeed, PauseMin, PauseMax,
            MaxAngle, TurnChance, KeeperDistance, FollowSpeed, FollowMaxDistance;

        internal static void Bind(ConfigFile cfg)
        {
            On = cfg.Bind("General", "IdleShuffle", Defaults.Get("General", "IdleShuffle", false),
                "Donkeys standing in the world without a cart (Mounts.SpawnDonkey, or left by a dismount) take a few slow steps now and then, inside a small area around where they were left, so they do not stand perfectly still. Never with a cart, never while ridden, and not while the keeper is close. Off in GK2 Mounts itself; a mod switches it on (the showcase does).");
            AreaWidth = F(cfg, "ShuffleAreaWidth", 1f, 0.1f, 4f,
                "How far the donkey may wander from its home spot sideways (along the screen's horizontal), either way, world units. Its home is where it was left, spawned or moved to.");
            AreaDepth = F(cfg, "ShuffleAreaDepth", 0.5f, 0f, 2f,
                "How far the donkey may wander from its home spot towards or away from the camera (the screen's up/down), either way, world units.");
            StepMin = F(cfg, "ShuffleStepMin", 0.3f, 0.05f, 2f,
                "Shortest step, world units.");
            StepMax = F(cfg, "ShuffleStepMax", 0.9f, 0.1f, 3f,
                "Longest step, world units.");
            Speed = F(cfg, "ShuffleSpeed", 0.5f, 0.1f, 2f,
                "Walking speed of a step, world units per second (the keeper walks about 3.3; the game's donkey walk clip is made for 1.5).");
            AnimSpeed = F(cfg, "ShuffleAnimSpeed", 1f, 0.2f, 3f,
                "Walk animation rate during a step, relative to the rate that matches ShuffleSpeed (1 = the hooves match the ground). Above 1 if a very slow walk looks like a stutter.");
            PauseMin = F(cfg, "ShufflePauseMin", 5f, 0.5f, 120f,
                "Shortest pause between two steps, seconds.");
            PauseMax = F(cfg, "ShufflePauseMax", 15f, 0.5f, 300f,
                "Longest pause between two steps, seconds.");
            MaxAngle = F(cfg, "ShuffleMaxAngle", 35f, 0f, 60f,
                "Steepest step, in degrees from the screen's horizontal. The donkey has only side views, so it never steps straight up or down the screen.");
            TurnChance = F(cfg, "ShuffleTurnChance", 0.25f, 0f, 1f,
                "Chance that a step starts with turning around (the donkey always walks the way it faces). It also turns when there is no room ahead.");
            KeeperDistance = F(cfg, "ShuffleKeeperDistance", 2f, 0f, 10f,
                "The donkey holds still while the keeper is closer than this, world units, so riding it does not chase a moving target. Donkeys that can't be ridden (foals) ignore the keeper.");
            FollowSpeed = F(cfg, "ShuffleFollowSpeed", 4f, 0.3f, 8f,
                "A donkey with a parent (a foal): how fast it trots after its parent when the parent moves away, world units per second.");
            FollowMaxDistance = F(cfg, "ShuffleFollowMaxDistance", 10f, 1f, 30f,
                "A donkey with a parent (a foal): it trots after its parent only while it is closer than this, world units. Further (or blocked, or in another place), it waits, shuffling where it is, and catches up out of sight once the parent stands again.");
        }

        private static ConfigEntry<float> F(ConfigFile cfg, string key, float def, float min, float max, string desc)
        {
            return cfg.Bind(Section, key, Defaults.Get(Section, key, def), new ConfigDescription(desc, new AcceptableValueRange<float>(min, max)));
        }

        // Mounts.Shuffle, the panel and the defaults, in this order.
        internal static ITuned[] Settings => new ITuned[]
        {
            AreaWidth, AreaDepth, StepMin, StepMax, Speed, AnimSpeed, PauseMin, PauseMax, MaxAngle, TurnChance, KeeperDistance,
            FollowSpeed, FollowMaxDistance,
        };

        internal static ConfigEntryBase[] Entries
        {
            get
            {
                ITuned[] s = Settings;
                var e = new ConfigEntryBase[s.Length];
                for (int i = 0; i < s.Length; i++)
                {
                    e[i] = s[i].EntryBase;
                }
                return e;
            }
        }
    }

    internal static partial class Mount
    {
        internal sealed partial class Instance
        {
            private enum ShufflePhase
            {
                Off,      // not shuffling (switched off, ridden, a cart, no view)
                Waiting,  // standing, until shuffleAt
                Turning,  // the facing is flipped this frame; the root moves next frame, when the view shows it
                Stepping, // walking from stepFrom to stepTo
                Following, // a foal trotting after its parent (Foals.cs)
            }

            private ShufflePhase afterTurn = ShufflePhase.Stepping; // what a turn goes on with

            private ShufflePhase shufflePhase;
            private float shuffleAt;
            private bool shuffleHomeSet;
            private Vector3 shuffleHome;                     // the body point it wanders around
            private Vector3 shuffleWrote;                    // the Position the shuffle last set: any other means it was moved
            private float shuffleLateral;                    // the body's offset from the root along 'right', times the facing
            private bool shuffleLateralKnown;
            private int shuffleTurnFrame;
            private float shuffleFacingSeen;                 // the facing last seen, and the frame it last changed:
            private int shuffleFacingFrame;                  // the view shows a new facing only a frame later
            private Vector3 turnRoot;
            private Vector3 stepFrom, stepTo;
            private float stepLength, stepDone;
            private int turnLogFrames;
            private bool shuffleAnnounced;                   // the "shuffles around" log line, once per instance
            private string shuffleHold;                      // why it is not shuffling right now (null: it is), for the panel
            private string groundFail;                       // the first ground check that failed since the last log line
            private string lastPickFail;                     // the last "found no step" summary, for the panel
            private float shuffleFailLogAt;
            private int failBox, failGround, failNear, failTurnRoom, failNoOffset;

            // Walking a step right now (MountHandle.IsStepping).
            internal bool IsStepping => shufflePhase == ShufflePhase.Stepping || shufflePhase == ShufflePhase.Turning || shufflePhase == ShufflePhase.Following;

            private const float StepEase = 0.12f;        // world units eased in and out at both ends of a step
            private const int PickTries = 12;
            private const float MaxRisePerSample = 0.12f; // ground height change between two samples of a step's line
            private const float MaxRise = 0.3f;           // and over the whole step
            private const float InstanceGap = 1.1f;       // no step ends this close to another donkey or cart

            // A frame of a standing instance, before Stand(): the shuffle moves Position; Stand() draws it.
            private void ShuffleTick(PlayerController pc)
            {
                shuffleHold = !HasDonkey ? "never: a cart on its own"
                    : Ephemeral ? "never: the summoned donkey"
                    : mounted ? "off: ridden"
                    : HasCart ? "off: it pulls a cart"
                    : IdleShuffle.On == null || !IdleShuffle.On.Value ? "off: Idle shuffle is off for it"
                    : null;
                if (shuffleHold != null)
                {
                    StopShuffle();
                    return;
                }
                if (MainGame.UpdateManager != null && !MainGame.UpdateManager.IsActive)
                {
                    shuffleHold = "holding: the game is paused";
                    return; // the game is paused (its own movement system holds too)
                }
                if (riderFacingX != shuffleFacingSeen)
                {
                    shuffleFacingSeen = riderFacingX;
                    shuffleFacingFrame = Time.frameCount;
                }
                MeasureLateral();
                // A foal's home is its parent's body (while it can follow; Foals.cs).
                bool parentHome = Parent != null && !followLost && IsRegistered(Parent) && Parent.SceneId == SceneId && Parent.Ready;
                if (!shuffleHomeSet || (Position - shuffleWrote).sqrMagnitude > 0.000001f)
                {
                    // First frame, or moved by something else (MoveTo, ComeToKeeper, a dismount, a hitch): home is here.
                    followLost = false;
                    shuffleHome = parentHome ? Parent.BodyPoint() : BodyAt(Position, riderFacingX);
                    shuffleHomeSet = true;
                    shuffleWrote = Position;
                    if (shufflePhase == ShufflePhase.Turning)
                    {
                        shufflePhase = ShufflePhase.Waiting; // moved by someone else mid-turn: their place wins
                    }
                    EndStep(firstPause: true);
                    if (!shuffleAnnounced && shuffleLateralKnown)
                    {
                        shuffleAnnounced = true;
                        Plugin.Log.LogInfo($"[shuffle] {Describe()} shuffles around {shuffleHome:F2} in {SceneId} (body offset {shuffleLateral:F2}, navmesh {(MainGame.PlayerController?.SceneRecastGraph != null ? "yes" : "no")}, first step in {shuffleAt - Time.time:F1} s).");
                    }
                }
                if (parentHome && !followLost)
                {
                    shuffleHome = Parent.BodyPoint();
                }
                if (FollowParentTick(pc))
                {
                    LogTurn();
                    return;
                }
                if (KeeperClose(pc, BodyAt(Position, riderFacingX)))
                {
                    shuffleHold = $"holding: the keeper is within {IdleShuffle.KeeperDistance.Value:F1}";
                    if (IsStepping)
                    {
                        EndStep(firstPause: false);
                    }
                    shuffleAt = Mathf.Max(shuffleAt, Time.time + 1f);
                    return;
                }
                switch (shufflePhase)
                {
                    case ShufflePhase.Off:
                        EndStep(firstPause: true);
                        break;
                    case ShufflePhase.Waiting:
                        if (Time.time >= shuffleAt && !TryStartStep(pc))
                        {
                            NotePickFailed();
                            LogFailedPick();
                            shuffleAt = Time.time + UnityEngine.Random.Range(IdleShuffle.PauseMin.Value, Mathf.Max(IdleShuffle.PauseMin.Value, IdleShuffle.PauseMax.Value)) * 0.5f;
                        }
                        break;
                    case ShufflePhase.Turning:
                        if (Time.frameCount > shuffleTurnFrame)
                        {
                            SetShufflePosition(turnRoot);
                            stepFrom = Position;
                            shufflePhase = afterTurn;
                            afterTurn = ShufflePhase.Stepping;
                        }
                        break;
                    case ShufflePhase.Stepping:
                        Advance(Mathf.Min(Time.deltaTime, 0.1f));
                        break;
                }
                LogTurn();
            }

            private void StopShuffle()
            {
                FinishTurn(); // not if something else just moved it (a hitch): its place wins
                shufflePhase = ShufflePhase.Off;
                shuffleHomeSet = false;
            }

            // The step ends where the donkey is; the next one after a pause.
            private void EndStep(bool firstPause)
            {
                FinishTurn();
                float min = IdleShuffle.PauseMin.Value;
                float max = Mathf.Max(min, IdleShuffle.PauseMax.Value);
                // The first pause after being left is a random part of a full one, so donkeys left together do not step
                // in time.
                shuffleAt = Time.time + UnityEngine.Random.Range(firstPause ? 0.3f * min : min, max);
                shufflePhase = ShufflePhase.Waiting;
            }

            private void Advance(float dt)
            {
                // Eased in and out over the ends of the step (never below a quarter speed, so it does not creep).
                float fromStart = stepDone, toEnd = stepLength - stepDone;
                float ease = Mathf.Clamp01(Mathf.Min(fromStart, toEnd) / StepEase);
                float speed = IdleShuffle.Speed.Value * Mathf.Lerp(0.25f, 1f, ease * ease * (3f - 2f * ease));
                stepDone = Mathf.Min(stepLength, stepDone + speed * dt);
                Vector3 p = Vector3.Lerp(stepFrom, stepTo, stepLength > 0.0001f ? stepDone / stepLength : 1f);
                // The height of the ground under the body, not under the root half a unit beside it: that is how Follow
                // places the ridden donkey (ground under the body sprite), and on a slope the root's ground put the body
                // into the hill (found 2026-09-30).
                float g = GroundHeight(BodyAt(p, riderFacingX), out _);
                p.y = float.IsNaN(g) ? Position.y : g;
                SetShufflePosition(p);
                if (stepDone >= stepLength - 0.0001f)
                {
                    EndStep(firstPause: false);
                }
            }

            // A turn cut short (keeper close, switched off): the facing is already flipped, so the root must go to the
            // body's other side too, or the body would stand a unit off.
            private void FinishTurn()
            {
                if (shufflePhase == ShufflePhase.Turning && (Position - shuffleWrote).sqrMagnitude < 0.000001f)
                {
                    SetShufflePosition(turnRoot);
                    shufflePhase = ShufflePhase.Waiting;
                }
            }

            private void SetShufflePosition(Vector3 p)
            {
                Position = p;
                shuffleWrote = p;
            }

            // Picks a step that stays in the box around home and on walkable, level ground, forward in the facing (or
            // after turning around); false when none of the tries fits.
            private bool TryStartStep(PlayerController pc)
            {
                if (!shuffleLateralKnown)
                {
                    failNoOffset++;
                    return false; // the view's body offset is not measured yet (a turn would jump the body)
                }
                Vector3 away = AwayFromCamera();
                Vector3 right = Vector3.Cross(Vector3.up, away).normalized;
                float worldToRight = Vector3.Dot(Vector3.right, right) >= 0f ? 1f : -1f; // facing is the sign of world x
                float f = riderFacingX;
                Vector3 body = BodyAt(Position, f);
                bool turnFirst = UnityEngine.Random.value < IdleShuffle.TurnChance.Value;
                float lenMin = Mathf.Min(IdleShuffle.StepMin.Value, IdleShuffle.StepMax.Value);
                float lenMax = Mathf.Max(IdleShuffle.StepMin.Value, IdleShuffle.StepMax.Value);
                float maxAngle = IdleShuffle.MaxAngle.Value * Mathf.Deg2Rad;
                float w = IdleShuffle.AreaWidth.Value, d = IdleShuffle.AreaDepth.Value;
                for (int pass = 0; pass < 2; pass++)
                {
                    bool turn = (pass == 0) == turnFirst;
                    float nf = turn ? -f : f;
                    // Turning keeps the body where it is: the root goes to the body's other side.
                    Vector3 root = turn ? body - right * LateralFor(nf) : Position;
                    if (turn && !LineWalkable(Position, root, "turn"))
                    {
                        failTurnRoom++;
                        continue;
                    }
                    for (int t = 0; t < PickTries; t++)
                    {
                        float len = UnityEngine.Random.Range(lenMin, lenMax);
                        float a = UnityEngine.Random.Range(-maxAngle, maxAngle);
                        Vector3 dir = right * (nf * worldToRight * Mathf.Cos(a)) + away * Mathf.Sin(a);
                        Vector3 bodyTo = body + dir * len;
                        Vector3 rel = bodyTo - shuffleHome;
                        if (Mathf.Abs(Vector3.Dot(rel, right)) > w || Mathf.Abs(Vector3.Dot(rel, away)) > d)
                        {
                            failBox++;
                            continue;
                        }
                        Vector3 rootTo = root + dir * len;
                        if (NearOther(bodyTo) || KeeperClose(pc, bodyTo))
                        {
                            failNear++;
                            continue;
                        }
                        if (!LineWalkable(root, rootTo, "root") || !LineWalkable(body, bodyTo, "body"))
                        {
                            failGround++;
                            continue;
                        }
                        stepTo = rootTo;
                        stepLength = len;
                        stepDone = 0f;
                        stepsTaken++;
                        lastStepAt = Time.time;
                        NotePickOk();
                        if (turn)
                        {
                            riderFacingX = nf; // Stand() shows the new facing this frame; the view flips in the next
                            turnRoot = root;
                            afterTurn = ShufflePhase.Stepping;
                            shuffleTurnFrame = Time.frameCount;
                            shufflePhase = ShufflePhase.Turning;
                            if (Plugin.TuningWindowEnabled.Value)
                            {
                                turnLogFrames = 3;
                                Plugin.Log.LogInfo($"[shuffle] {Describe()} turns to face {(nf > 0f ? "right" : "left")}: body {BodyNowForLog()}, root {Position:F3} -> {root:F3}.");
                            }
                        }
                        else
                        {
                            stepFrom = Position;
                            shufflePhase = ShufflePhase.Stepping;
                        }
                        return true;
                    }
                }
                return false;
            }

            // The body point of the donkey with its root at 'root' facing 'facing' (the view draws the body half a unit to
            // its facing side of the root).
            private Vector3 BodyAt(Vector3 root, float facing)
            {
                Vector3 right = Vector3.Cross(Vector3.up, AwayFromCamera()).normalized;
                return root + right * LateralFor(facing);
            }

            private float LateralFor(float facing) => shuffleLateralKnown ? shuffleLateral * facing : 0f;

            // The body's offset in this view, while the view shows the facing it was given (not right after a turn).
            private void MeasureLateral()
            {
                // Not while the view may still show the previous facing (a turn of ours, or MoveTo / ComeToKeeper with a
                // facing): the offset would be taken with the wrong sign.
                if (bodySprite == null || !bodySprite || donkey == null || !donkey || shufflePhase == ShufflePhase.Turning
                    || Time.frameCount <= shuffleTurnFrame + 2 || Time.frameCount <= shuffleFacingFrame + 2)
                {
                    return;
                }
                Vector3 right = Vector3.Cross(Vector3.up, AwayFromCamera()).normalized;
                float lat = Vector3.Dot(bodySprite.position - donkey.transform.position, right);
                if (!shuffleLateralKnown || Mathf.Abs(lat * riderFacingX - shuffleLateral) > 0.01f)
                {
                    bool first = !shuffleLateralKnown;
                    shuffleLateral = lat * riderFacingX;
                    shuffleLateralKnown = true;
                    if (first)
                    {
                        // Home was taken without the offset: take it again with it.
                        shuffleHomeSet = false;
                    }
                }
            }

            // Where the keeper could stand along a line: ground under every sample (navmesh or ground raycast), level enough
            // (no step off a ledge or up a wall), and no collider of the keeper-blocking layer in the way (a building, a
            // fence, a tree; the donkey's own colliders are off). The first failure is kept for the log.
            private const int BlockingLayers = 256;          // layer 8: what PlayerColliderTester checks the keeper against
            private const float BodyRadius = 0.25f;
            private static readonly Collider[] blockers = new Collider[8];
            private static Rigidbody keeperBody;

            private bool LineWalkable(Vector3 a, Vector3 b, string what)
            {
                keeperBody = MainGame.PlayerController?.PhysicalBody?.Rb;
                float prev = float.NaN;
                float start = float.NaN;
                const int Samples = 5;
                for (int i = 0; i <= Samples; i++)
                {
                    Vector3 p = Vector3.Lerp(a, b, i / (float)Samples);
                    float g = GroundHeight(p, out string src);
                    if (float.IsNaN(g))
                    {
                        NoteGroundFail($"{what} sample {i} at {p:F2}: no ground ({src})");
                        return false;
                    }
                    if (i == 0)
                    {
                        start = g;
                    }
                    else if (Mathf.Abs(g - prev) > MaxRisePerSample || Mathf.Abs(g - start) > MaxRise)
                    {
                        NoteGroundFail($"{what} sample {i} at {p:F2}: ground {g:F2} ({src}) against {prev:F2} before, {start:F2} at the start");
                        return false;
                    }
                    prev = g;
                    Vector3 foot = new Vector3(p.x, g, p.z);
                    int n = Physics.OverlapCapsuleNonAlloc(foot + Vector3.up * (BodyRadius + 0.1f), foot + Vector3.up * 0.9f, BodyRadius,
                        blockers, BlockingLayers, QueryTriggerInteraction.Ignore);
                    for (int k = 0; k < n; k++)
                    {
                        Collider c = blockers[k];
                        if (c == null || !c || donkey != null && donkey && c.transform.IsChildOf(donkey.transform)
                            || keeperBody != null && c.attachedRigidbody == keeperBody)
                        {
                            continue; // its own view, or the keeper (KeeperClose handles him; a foal trots right behind him)
                        }
                        NoteGroundFail($"{what} sample {i} at {foot:F2}: blocked by {c.name} (layer {LayerMask.LayerToName(c.gameObject.layer)})");
                        return false;
                    }
                }
                return true;
            }

            private void NoteGroundFail(string s)
            {
                if (groundFail == null)
                {
                    groundFail = s;
                }
            }

            private bool KeeperClose(PlayerController pc, Vector3 point)
            {
                if (!Rideable)
                {
                    return false; // nobody gets on a foal, so it needs no stillness for the keeper
                }
                float dist = IdleShuffle.KeeperDistance.Value;
                if (dist <= 0f || pc == null || pc.PlayerData.currentGameSceneId != SceneId)
                {
                    return false;
                }
                Vector3 k = pc.PlayerData.position.Value;
                float dx = k.x - point.x, dz = k.z - point.z;
                return dx * dx + dz * dz < dist * dist;
            }

            private bool NearOther(Vector3 point)
            {
                foreach (Instance o in All)
                {
                    if (o == this || o.SceneId != SceneId)
                    {
                        continue;
                    }
                    Vector3 b = o.BodyPoint();
                    float dx = b.x - point.x, dz = b.z - point.z;
                    float gap = InstanceGap * 0.5f * (ViewScale + o.ViewScale); // two full donkeys 1.1, a foal and its parent less
                    if (dx * dx + dz * dz < gap * gap)
                    {
                        return true;
                    }
                }
                return false;
            }

            // Why no step could be found, at most once a minute per instance (counts since the last line).
            private void LogFailedPick()
            {
                lastPickFailAt = Time.time;
                if (Time.time < shuffleFailLogAt)
                {
                    return;
                }
                shuffleFailLogAt = Time.time + 60f;
                lastPickFail = $"outside the area {failBox}, near the keeper or another donkey {failNear}, ground {failGround}, no room to turn {failTurnRoom}, body offset not measured {failNoOffset}";
                Plugin.Log.LogInfo($"[shuffle] {Describe()} found no step at {BodyAt(Position, riderFacingX):F2} (home {shuffleHome:F2}): rejected {lastPickFail}.{(groundFail != null ? " First ground failure: " + groundFail + "." : "")}");
                failBox = failGround = failNear = failTurnRoom = failNoOffset = 0;
                groundFail = null;
            }

            // Dev log (with the tuning panel on): where the body is drawn for a few frames after a turn, to see whether
            // the one-frame hold keeps it in place.
            private void LogTurn()
            {
                if (turnLogFrames <= 0 || shufflePhase == ShufflePhase.Turning && Time.frameCount == shuffleTurnFrame)
                {
                    return;
                }
                turnLogFrames--;
                Plugin.Log.LogInfo($"[shuffle] {Describe()} frame +{Time.frameCount - shuffleTurnFrame}: body {BodyNowForLog()}, root {Position:F3}, {shufflePhase}.");
            }

            private float lastPickFailAt = -1000f;
            private float lastStepAt = -1000f;
            private int stepsTaken;

            // One line for the panel's Instances tab: what the idle shuffle is doing with this donkey right now.
            internal string ShuffleStatusText()
            {
                if (mounted)
                {
                    return "off: ridden";
                }
                if (donkey == null || !donkey || !partsReady)
                {
                    return "waiting for its view (its scene isn't loaded)";
                }
                string s;
                if (shuffleHold != null)
                {
                    s = shuffleHold;
                }
                else
                {
                    switch (shufflePhase)
                    {
                        case ShufflePhase.Waiting:
                            s = $"next step in {Mathf.Max(0f, shuffleAt - Time.time):F1} s";
                            break;
                        case ShufflePhase.Turning:
                            s = "turning around";
                            break;
                        case ShufflePhase.Stepping:
                            s = $"stepping {(riderFacingX > 0f ? "right" : "left")}, {stepDone:F2} of {stepLength:F2}";
                            break;
                        case ShufflePhase.Following:
                            s = $"trotting after its parent #{Parent?.Id} at {followSpeedNow:F1}/s";
                            break;
                        default:
                            s = "starting";
                            break;
                    }
                }
                if (shuffleHomeSet && shuffleHold == null)
                {
                    Vector3 away = AwayFromCamera();
                    Vector3 right = Vector3.Cross(Vector3.up, away).normalized;
                    Vector3 rel = BodyAt(Position, riderFacingX) - shuffleHome;
                    s += $"; {Vector3.Dot(rel, right):+0.00;-0.00} sideways, {Vector3.Dot(rel, away):+0.00;-0.00} up/down from home";
                }
                if (followLost && Parent != null)
                {
                    s += $"; lost its parent #{Parent.Id}, catches up out of sight";
                }
                if (stepsTaken > 0)
                {
                    s += $"; {stepsTaken} step(s), the last {Time.time - lastStepAt:F0} s ago";
                }
                if (lastPickFail != null && Time.time - lastPickFailAt < 90f && shuffleHold == null)
                {
                    s += $"; last try found no step ({lastPickFail})";
                }
                return s;
            }

            private string BodyNowForLog() => bodySprite != null && bodySprite ? bodySprite.position.ToString("F3") : "?";

            // The animator state Stand() keeps: Walk during a step, Idle otherwise.
            private AnimationState StandState => shufflePhase == ShufflePhase.Stepping || shufflePhase == ShufflePhase.Following ? AnimationState.Walk : AnimationState.Idle;

            // The walk cycle's rate for the speed it moves at: shorter legs (ViewScale) step faster for the same ground.
            private float StandWalkSpeed => (shufflePhase == ShufflePhase.Following ? followSpeedNow : IdleShuffle.Speed.Value)
                * IdleShuffle.AnimSpeed.Value / Mathf.Max(0.1f, ViewScale);

            private float walkSpeedApplied = -1f;

            private void ApplyStandState()
            {
                AnimationState want = StandState;
                if (lastMirrored == want)
                {
                    if (want == AnimationState.Walk && Mathf.Abs(StandWalkSpeed - walkSpeedApplied) > 0.15f)
                    {
                        walkSpeedApplied = StandWalkSpeed;
                        donkey.MainWgoPart?.AnimationComponent?.SetWalkAnimationSpeedMultiplier(walkSpeedApplied);
                    }
                    return;
                }
                lastMirrored = want;
                AnimationComponentBase anim = donkey.MainWgoPart?.AnimationComponent;
                if (want == AnimationState.Walk && anim != null)
                {
                    // As in Follow: the idle one-shots (graze, ear, lips) would finish before the walk starts.
                    anim.SetTrigger(AnimationComponentBase.RESET_TO_IDLE_TRIGGER);
                }
                donkey.Data.SetStateToAnimator(want);
                if (anim != null)
                {
                    walkSpeedApplied = want == AnimationState.Walk ? StandWalkSpeed : 1.5f;
                    anim.SetWalkAnimationSpeedMultiplier(walkSpeedApplied);
                }
            }
        }
    }
}

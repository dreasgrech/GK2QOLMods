using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using GK2Mounts.Api;
using UnityEngine;

namespace GK2Mounts
{
    // In-game self-tests (docs/mounts-self-tests.md): scripted scenarios that drive the game over frames (spawn test
    // donkeys, move the keeper, ride, wait for the levels) and check the state that decides what is seen: the
    // animator's facing, which cart parts are on, whose materials the sprites wear, the counters. Every bug of the
    // levels and view reuse found by hand on 2026-10-01 is one of them, so a fix can't pass by luck.
    //
    // Run from the panel's Debug tab. Test donkeys belong to SelfTest.Owner, are never kept with the save and are
    // removed after each scenario; the keeper is put back where he stood; nothing is saved. Results go to the log as
    // [test] PASS / FAIL / WARN / INFO lines and a summary, and to the panel.
    internal static class SelfTest
    {
        internal const string Owner = "dreas.gk2.mounts.selftest";

        internal static bool Running { get; private set; }
        internal static string Status { get; private set; } = "Not run this session.";
        internal static readonly List<string> Lines = new List<string>();

        private static int pass, fail, warn;
        private static bool abort;
        private static int spawnSeq;

        private static bool startRequested;

        // The panel's button: the run starts on the next frame (PollStart), not inside the GUI event.
        internal static void RequestStart() => startRequested = true;

        internal static void PollStart()
        {
            if (!startRequested)
            {
                return;
            }
            startRequested = false;
            if (Running || Plugin.Instance == null)
            {
                return;
            }
            Running = true; // at once, so the panel shows Abort from the next event on
            Plugin.Instance.StartCoroutine(Drive(Run()));
        }

        internal static void Abort() => abort = true;

        // ------------------------------------------------------------------ the runner

        private sealed class Scenario
        {
            internal string Name;
            internal Func<IEnumerator> Body;
        }

        private static IEnumerator Run()
        {
            abort = false;
            Lines.Clear();
            pass = fail = warn = 0;
            if (!Mount.Instance.IsInGame(out PlayerController pc, out GameScene scene) || !Mounts.IsGameReady)
            {
                Status = "Not in a game (load a save first).";
                yield break;
            }
            if (Mounts.IsMounted)
            {
                Mounts.Dismount("self-test");
                yield return null;
            }
            // The panel hidden while the tests run (its own cost would be in the frame times, its markers on screen); back,
            // with the results, at the end. Its key still opens it, to abort.
            bool panelWasHidden = Plugin.TuningWindowHidden;
            Plugin.TuningWindowHidden = true;
            bool lodOn = Lod.On, idleOff = Lod.IdleSwitchesOff;
            int perFrame = Lod.SpawnsPerFrame;
            home = Keeper();
            homeFacing = pc.PlayerData.Direction.x >= 0f ? 1f : -1f;
            Line($"[test] start: {scene.Id} at {home:F1}, {Mount.All.Count} instance(s) in the world; levels {(lodOn ? "on" : "off")}, idle {(idleOff ? "switched off" : "frozen")}.");

            // The world's own instances (not the tests'): all must come through the run as they were.
            var before = new Dictionary<string, string>();
            foreach (Mount.Instance i in Mount.All)
            {
                if (!IsTestOwner(i.OwnerId) && !i.Ephemeral) before[i.FullId] = Sig(i);
            }

            var scenarios = new List<Scenario>
            {
                new Scenario { Name = "the per-mod API (ids, keys, data, owner rules)", Body = ApiIdentity },
                new Scenario { Name = "kept-with-save text, round trip", Body = RoundTrip },
                new Scenario { Name = "hitch and unhitch", Body = HitchUnhitch },
                new Scenario { Name = "shared colour textures", Body = SharedColours },
                new Scenario { Name = "one new view per frame", Body = SpawnCap },
                new Scenario { Name = "waking (view switched off)", Body = () => Wake(switchOff: true) },
                new Scenario { Name = "waking (view frozen)", Body = () => Wake(switchOff: false) },
                new Scenario { Name = "mounted while idle (the showcase's H)", Body = MountWhileIdle },
                new Scenario { Name = "reused views", Body = Reuse },
                new Scenario { Name = "foals keep up off screen", Body = FoalsFollow },
                new Scenario { Name = "no leaks over spawn and remove", Body = Leaks },
                new Scenario { Name = "frame time by mode", Body = FrameTimes },
            };
            foreach (Scenario s in scenarios)
            {
                if (abort)
                {
                    break;
                }
                Status = $"Running: {s.Name}";
                current = s.Name;
                Lod.On = true;
                Lod.IdleSwitchesOff = idleOff;
                Lod.SpawnsPerFrame = perFrame;
                yield return s.Body();
                // Between scenarios: everything back.
                yield return CleanUp();
            }
            Lod.On = lodOn;
            Lod.IdleSwitchesOff = idleOff;
            Lod.SpawnsPerFrame = perFrame;
            current = "the world's own donkeys";
            var after = new Dictionary<string, string>();
            foreach (Mount.Instance i in Mount.All)
            {
                if (!IsTestOwner(i.OwnerId) && !i.Ephemeral) after[i.FullId] = Sig(i);
            }
            var changed = new List<string>();
            foreach (var kv in before)
            {
                if (!after.TryGetValue(kv.Key, out string now)) changed.Add(kv.Key + " gone");
                else if (now != kv.Value) changed.Add($"{kv.Key} {kv.Value} -> {now}");
            }
            foreach (string k in after.Keys)
            {
                if (!before.ContainsKey(k)) changed.Add(k + " new");
            }
            Check(changed.Count == 0, changed.Count == 0
                ? $"all {before.Count} came through unchanged (owner, number, scene, kind, kept)"
                : $"{changed.Count} changed: {string.Join("; ", changed.GetRange(0, Math.Min(8, changed.Count)))}");
            int leftover = 0;
            foreach (Mount.Instance i in Mount.All)
            {
                if (IsTestOwner(i.OwnerId)) leftover++;
            }
            Check(leftover == 0, $"no test donkey left behind ({leftover})");
            string summary = $"{(abort ? "aborted" : "done")}: {pass} passed, {fail} failed, {warn} warning(s).";
            Line("[test] " + summary);
            Status = summary;
            Plugin.TuningWindowHidden = panelWasHidden;
        }

        // Runs a coroutine with its nested coroutines itself, so an exception anywhere ends only that scenario (FAIL)
        // and the run goes on (Unity would stop the whole coroutine).
        private static IEnumerator Drive(IEnumerator root)
        {
            Running = true; // set and cleared here only
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                object cur = null;
                bool more;
                try
                {
                    more = top.MoveNext();
                    if (more) cur = top.Current;
                }
                catch (Exception e)
                {
                    more = false;
                    Fail("threw " + e.GetType().Name + ": " + e.Message);
                    Plugin.Log.LogWarning("[test] " + e);
                    // The scenario that threw ends; the runner (the bottom of the stack) goes on with its cleanup.
                    while (stack.Count > 1)
                    {
                        stack.Pop();
                    }
                    continue;
                }
                if (!more)
                {
                    stack.Pop();
                    continue;
                }
                if (cur is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                yield return cur;
            }
            Running = false;
        }

        private static string current = "";

        private static void Line(string s)
        {
            Lines.Add(s);
            Plugin.Log.LogInfo(s);
        }

        private static void Pass(string what) { pass++; Line($"[test] PASS {current}: {what}"); }

        private static void Fail(string what) { fail++; Line($"[test] FAIL {current}: {what}"); }

        private static void Warn(string what) { warn++; Line($"[test] WARN {current}: {what}"); }

        private static void Info(string what) => Line($"[test] INFO {current}: {what}");

        private static void Check(bool ok, string what)
        {
            if (ok) Pass(what); else Fail(what);
        }

        // ------------------------------------------------------------------ helpers

        private static Vector3 home;
        private static float homeFacing;
        private static bool waited;

        private static PlayerController PC => MainGame.PlayerController;

        private static Vector3 Keeper() => PC.PlayerData.position.Value;

        private static void PutKeeper(Vector3 p, float facing)
        {
            PC.SetPosition(p);
            PC.PhysicalBody.SetFacingDirection(new Vector2(facing, 0f));
        }

        // The keeper moved and checked two frames later: a spot he can't stand on pushes him elsewhere, and the checks
        // after it would run against a keeper who isn't where they think (moved false: the scenario skips).
        private static bool moved;

        private static IEnumerator MoveKeeper(Vector3 p, float facing)
        {
            PutKeeper(p, facing);
            yield return Frames(2);
            Vector3 k = Keeper();
            moved = new Vector2(k.x - p.x, k.z - p.z).magnitude < 1f;
            if (!moved)
            {
                Warn($"the keeper didn't stay at {p:F1} (he is at {k:F1}): that spot is blocked; skipped");
            }
        }

        private static readonly Collider[] overlap = new Collider[8];

        // Nothing of the keeper-blocking layer (8, Obstacle) where he would stand.
        private static bool FreeSpot(Vector3 p)
        {
            Rigidbody keeper = PC?.PhysicalBody?.Rb;
            int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * 0.35f, p + Vector3.up * 1.2f, 0.35f, overlap, 1 << 8, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                if (overlap[k] != null && overlap[k].attachedRigidbody != keeper)
                {
                    return false;
                }
            }
            return true;
        }

        // A point with ground and room for the keeper about 'dist' from 'from' (eight directions, sideways first).
        private static Vector3? Spot(Vector3 from, float dist)
        {
            float[] angles = { 0f, 180f, 30f, 150f, 210f, 330f, 90f, 270f };
            foreach (float a in angles)
            {
                float r = a * Mathf.Deg2Rad;
                Vector3 p = from + new Vector3(Mathf.Cos(r) * dist, 0f, Mathf.Sin(r) * dist * 0.6f);
                float g = Mount.Instance.GroundHeight(p, out _);
                if (!float.IsNaN(g))
                {
                    p.y = g;
                    if (FreeSpot(p))
                    {
                        return p;
                    }
                }
            }
            return null;
        }

        // The levels have settled: no view made or released and no level changed for 2 s (at most timeout seconds).
        private static IEnumerator Settle(float timeout)
        {
            float end = Time.unscaledTime + timeout;
            string last = null;
            float since = Time.unscaledTime;
            while (Time.unscaledTime < end && !abort)
            {
                int a = 0, i = 0, s = 0;
                foreach (Mount.Instance x in Mount.All)
                {
                    if (x.tier == Mount.Tier.Active) a++; else if (x.tier == Mount.Tier.Idle) i++; else s++;
                }
                string now = $"{Lod.SpawnedTotal}/{Lod.ReleasedTotal}/{a}/{i}/{s}";
                if (now != last)
                {
                    last = now;
                    since = Time.unscaledTime;
                }
                else if (Time.unscaledTime - since >= 2f)
                {
                    yield break;
                }
                yield return null;
            }
            Info("the levels didn't settle within " + timeout + " s; going on");
        }

        private static IEnumerator Until(Func<bool> cond, float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (!cond() && Time.unscaledTime < end && !abort)
            {
                yield return null;
            }
            waited = cond();
        }

        private static IEnumerator Frames(int n)
        {
            for (int k = 0; k < n; k++)
            {
                yield return null;
            }
        }

        private static IEnumerator Seconds(float s)
        {
            float end = Time.unscaledTime + s;
            while (Time.unscaledTime < end && !abort)
            {
                yield return null;
            }
        }

        // Two more test owners for the API's per-mod rules (not the core's: they can't change each other's instances).
        private const string OwnerA = Owner + ".a", OwnerB = Owner + ".b";

        private static bool IsTestOwner(string o) => o == Owner || o == OwnerA || o == OwnerB;

        // What must not change for the world's own instances during the run.
        private static string Sig(Mount.Instance i) => $"{i.OwnerId}|{i.SceneId}|{i.HasDonkey}|{i.HasCart}|{i.Persist}|{i.Key}";

        private static MountHandle Spawn(Vector3 at, bool right, float scale = 1f, bool withCart = false, string owner = Owner,
            int id = 0, string key = null, bool persist = false, bool donkey = true)
        {
            return Mounts.Spawn(owner, new MountSpawn
            {
                Position = at,
                FacingRight = right,
                Scale = scale,
                Rideable = donkey && Mathf.Approximately(scale, 1f),
                Donkey = donkey,
                WithCart = withCart || !donkey,
                Persist = persist,
                Id = id,
                Key = key ?? "test-" + (++spawnSeq),
            });
        }

        private static IEnumerator CleanUp()
        {
            if (Mounts.IsMounted)
            {
                Mounts.Dismount("self-test");
                yield return null;
            }
            foreach (string o in new[] { Owner, OwnerA, OwnerB })
            {
                foreach (MountHandle h in Mounts.InstancesOf(o))
                {
                    if (h.Exists) h.Remove();
                }
            }
            PutKeeper(home, homeFacing);
            yield return Seconds(1f);
        }

        private static bool IsActive(MountHandle h) => h.Exists && h.I.tier == Mount.Tier.Active;

        private static string Dir(float? d) => d.HasValue ? d.Value.ToString("F0") : "none";

        // Every instance in the world with its view on: the view's size is its own, and its cart parts match whether it
        // has a cart. A reused view, taken by whichever donkey woke first, must fit that donkey (any owner).
        private static IEnumerator WorldViewsRight()
        {
            yield return Frames(3);
            var bad = new List<string>();
            int seen = 0;
            foreach (Mount.Instance i in Mount.All)
            {
                if (i.Ephemeral || !i.Ready || !i.TestViewActive || !i.HasDonkey)
                {
                    continue;
                }
                seen++;
                if (Mathf.Abs(i.TestViewScale - i.ViewScale) > 0.01f) bad.Add($"{i.FullId} view size {i.TestViewScale:F2}, is {i.ViewScale:F2}");
                int cart = i.TestCartPartsOn();
                if (!i.HasCart && cart > 0) bad.Add($"{i.FullId} has {cart} cart part(s) on without a cart");
                if (i.HasCart && cart == 0) bad.Add($"{i.FullId} has a cart with no cart part on");
            }
            Check(bad.Count == 0, bad.Count == 0
                ? $"all {seen} donkeys with a view on: right size, cart parts as they should be"
                : $"{bad.Count} wrong: {string.Join("; ", bad.GetRange(0, Math.Min(6, bad.Count)))}");
        }

        // Everything that decides an instance's level and look, for a WARN or FAIL that has to say why.
        private static string State(MountHandle h)
        {
            if (h == null || !h.Exists) return "gone";
            Vector3 k = Keeper();
            float dist = new Vector2(h.Position.x - k.x, h.Position.z - k.z).magnitude;
            Vector3 s = CameraSystem.Instance != null ? CameraSystem.WorldToScreenPoint(h.Position) : Vector3.zero;
            return $"level {h.I.tier}, frozen parts {h.I.TestQuietRenderers}, switched off {h.I.TestSwitchedOff}, view on {h.I.TestViewActive}, ridden {h.IsRidden}, {dist:F1} from the keeper, on screen at ({s.x:F0}, {s.y:F0}) of {Screen.width}x{Screen.height}";
        }

        // ------------------------------------------------------------------ the scenarios

        // The API's per-mod contracts (docs/mounts-api.md, "Several mods"): each mod numbers its own, a taken number or
        // key is refused, Find and InstancesOf, numbers aren't reused, only the owner changes kept and key, data per mod.
        private static IEnumerator ApiIdentity()
        {
            Vector3 near = Spot(home, 4f) ?? home;
            MountHandle a1 = Spawn(near, true, owner: OwnerA, id: 1, key: "same");
            MountHandle b1 = Spawn(near + new Vector3(1.5f, 0f, 0f), false, owner: OwnerB, id: 1, key: "same");
            if (a1 == null || b1 == null)
            {
                Fail("could not spawn the two #1s");
                yield break;
            }
            Check(a1.Id == 1 && b1.Id == 1 && a1.FullId != b1.FullId, $"two mods each have their own #1 ({a1.FullId}, {b1.FullId})");
            Check(Spawn(near, true, owner: OwnerA, id: 1) == null, "asking again for a number the mod has is refused");
            Check(Spawn(near, true, owner: OwnerA, key: "same") == null, "a key the mod already uses is refused");
            Check(Mounts.Find(OwnerA, 1) == a1 && Mounts.Find(OwnerB, "same") == b1, "Find by number and by key, per mod");
            Check(Mounts.InstancesOf(OwnerA).Count == 1 && Mounts.InstancesOf(OwnerB).Count == 1, "InstancesOf: one each");
            MountHandle a2 = Spawn(near, true, owner: OwnerA);
            int id2 = a2 != null ? a2.Id : -1;
            a2?.Remove();
            MountHandle a3 = Spawn(near, true, owner: OwnerA);
            // Relative, not #2 then #3: the owner's counter lives for the session, so a second run starts higher.
            Check(a3 != null && id2 > 1 && a3.Id > id2, $"a removed number isn't handed out again (got #{id2}, removed it, then #{(a3 != null ? a3.Id : -1)})");
            MountResult keepByOther = b1.SetPersist(OwnerA, true);
            MountResult keyByOther = b1.SetKey(OwnerA, "taken");
            Check(!keepByOther.Ok && !keyByOther.Ok, $"another mod can't change kept or key ({keepByOther.Reason})");
            Check(b1.SetPersist(OwnerB, true).Ok && b1.Persist, "its own mod can");
            b1.SetPersist(OwnerB, false);
            b1.SetData(OwnerA, "name", "from a");
            b1.SetData(OwnerB, "name", "from b");
            Check(b1.GetData(OwnerA, "name") == "from a" && b1.GetData(OwnerB, "name") == "from b", "each mod has its own data on the same donkey");
            b1.SetData(OwnerA, "name", null);
            Check(b1.GetData(OwnerA, "name") == null && b1.GetData(OwnerB, "name") == "from b" && b1.DataKeys(OwnerB).Count == 1, "clearing one mod's value leaves the other's");
            yield return null;
        }

        // The side file's text for kept instances (a parent with a foal, a donkey with its cart, a free cart; a key,
        // own settings, data with tabs and line breaks) written and read back in memory: every field the same. No file
        // is written and the game's save isn't touched.
        private static IEnumerator RoundTrip()
        {
            Vector3 near = Spot(home, 5f) ?? home;
            MountHandle parent = Spawn(near, false, persist: true, key: "rt parent");
            MountHandle cartDonkey = Spawn(near + new Vector3(3f, 0f, 0f), true, withCart: true, persist: true, key: "rt cart");
            MountHandle freeCart = Spawn(near + new Vector3(-3f, 0f, 0f), true, donkey: false, persist: true, key: "rt free");
            if (parent == null || cartDonkey == null || freeCart == null)
            {
                Fail("could not spawn");
                yield break;
            }
            MountHandle foal = Mounts.SpawnFoal(Owner, parent, 0.6f, key: "rt foal", persist: true);
            string v2 = "tab\there, new\nline, back\\slash"; // a tab, a line break and a backslash in one value
            parent.SetData(Owner, "tricky", v2);
            parent.Tuning.Set(Owner, "CoatColorize", "00FF80");
            yield return null;
            List<Mount.Instance> kept = Mount.KeptInstances(i => i.OwnerId == Owner, log: false);
            Check(kept.Count == (foal != null ? 4 : 3), $"the kept list has the test's {kept.Count} (parent, foal, donkey with cart, free cart)");
            string text = Mount.BuildSideText("selftest", "saved at test time", 42, kept, withDormant: false, out _);
            var header = new Dictionary<string, string>();
            var nexts = new List<string[]>();
            var records = new List<Mount.Record>();
            Mount.ParseSideText(text.Split('\n'), header, nexts, records);
            Check(header.TryGetValue("slot", out string slot) && slot == "selftest" && header["saved"] == "saved at test time" && header["day"] == "42",
                "the header comes back (slot, save time, day)");
            Check(records.Count == kept.Count, $"{records.Count} record(s) for {kept.Count} instance(s)");
            foreach (Mount.Instance i in kept)
            {
                Mount.Record r = records.Find(x => x.Owner == i.OwnerId && x.Id == i.Id);
                if (r == null)
                {
                    Fail($"{i.FullId}: no record");
                    continue;
                }
                var bad = new List<string>();
                if (r.Key != i.Key) bad.Add($"key '{r.Key}'");
                if (r.Donkey != i.HasDonkey || r.Cart != i.HasCart) bad.Add("kind");
                if (r.Scene != i.SceneId) bad.Add("scene");
                if ((r.Position - i.Position).sqrMagnitude > 1e-6f) bad.Add($"position {r.Position}");
                if (r.FacingRight != i.FacingRight) bad.Add("facing");
                if (Mathf.Abs(r.Scale - i.ViewScale) > 1e-5f || r.Rideable != i.Rideable) bad.Add("scale or rideable");
                if (r.RemoveWithParent != i.RemoveWithParent) bad.Add("removeWithParent");
                string wantParent = i.Parent != null ? i.Parent.FullId : null;
                if (r.ParentFull != wantParent) bad.Add($"parent {r.ParentFull}");
                foreach (var kv in i.Overrides)
                {
                    if (!r.Sets.Exists(x => x[0] == kv.Key)) bad.Add("setting " + kv.Key);
                }
                foreach (var mod in i.Data)
                {
                    foreach (var kv in mod.Value)
                    {
                        if (!r.DataLines.Exists(x => x[0] == mod.Key && x[1] == kv.Key && x[2] == kv.Value)) bad.Add("data " + kv.Key);
                    }
                }
                Check(bad.Count == 0, $"{i.FullId} ({r.Kind}) comes back the same{(bad.Count > 0 ? ": " + string.Join(", ", bad) : "")}");
            }
            Mount.Record pr = records.Find(x => x.Key == "rt parent");
            Check(pr != null && pr.DataLines.Exists(x => x[1] == "tricky" && x[2] == v2), "a value with a tab, a line break and a backslash comes back exactly");
        }

        // A donkey takes a free cart and leaves it again: one instance with the cart (the cart's data merged, its parts
        // on), then a new free cart of the same owner and kept setting, the donkey's cart parts off.
        private static IEnumerator HitchUnhitch()
        {
            Vector3 near = Spot(home, 4f) ?? home;
            MountHandle d = Spawn(near, true);
            MountHandle c = Spawn(near + new Vector3(2.5f, 0f, 0f), true, donkey: false);
            if (d == null || c == null)
            {
                Fail("could not spawn");
                yield break;
            }
            c.SetData(Owner, "cart", "its own");
            yield return Until(() => d.IsReady && c.IsReady, 6f);
            if (!waited)
            {
                Fail("never got their views");
                yield break;
            }
            MountResult h = Mounts.Hitch(d, c);
            Check(h.Ok, $"hitched{(h.Ok ? "" : " (" + h.Reason + ")")}");
            if (!h.Ok) yield break;
            Check(!c.Exists && d.HasCart, "the free cart is gone into the donkey");
            Check(d.GetData(Owner, "cart") == "its own", "the cart's data came with it");
            yield return Until(() => d.IsReady, 4f);
            yield return Frames(3);
            Check(d.I.TestCartPartsOn() > 0, $"the cart's parts are on ({d.I.TestCartPartsOn()})");
            MountHandle left = Mounts.Unhitch(d);
            Check(left != null && left.Exists && !left.HasDonkey && left.HasCart, "unhitched: a free cart again");
            if (left == null) yield break;
            Check(left.Owner == d.Owner && left.Persist == d.Persist, "the new free cart has the donkey's owner and kept setting");
            Check(!d.HasCart, "the donkey has no cart now");
            yield return Until(() => d.IsReady && left.IsReady, 4f);
            yield return Frames(3);
            Check(d.I.TestCartPartsOn() == 0, $"the donkey's cart parts are off ({d.I.TestCartPartsOn()} on)");
        }

        // Donkeys with the same colours share their colour textures; another colour adds its own; all removed, the
        // count comes back.
        private static IEnumerator SharedColours()
        {
            Vector3 near = Spot(home, 4f) ?? home;
            yield return Seconds(0.5f);
            int s0 = Colors.SharedTextures.Count;
            MountHandle a = Spawn(near, true);
            a?.Tuning.Set(Owner, "CoatColorize", "8040C0");
            a?.Tuning.Set(Owner, "CoatColorizeAmount", 1f);
            yield return Until(() => a != null && a.IsReady && a.I.TestColoursOurs() != null, 6f);
            if (!waited)
            {
                Warn("colours never attached (colours off?): skipped");
                yield break;
            }
            yield return Frames(3);
            int s1 = Colors.SharedTextures.Count;
            MountHandle b = Spawn(near + new Vector3(1.5f, 0f, 0f), false);
            b?.Tuning.Set(Owner, "CoatColorize", "8040C0");
            b?.Tuning.Set(Owner, "CoatColorizeAmount", 1f);
            yield return Until(() => b != null && b.IsReady && b.I.TestColoursOurs() != null, 6f);
            yield return Frames(3);
            int s2 = Colors.SharedTextures.Count;
            Check(s2 == s1, $"a second donkey with the same colours adds no texture ({s1} -> {s2})");
            MountHandle c = Spawn(near + new Vector3(-1.5f, 0f, 0f), true);
            c?.Tuning.Set(Owner, "CoatColorize", "20C060");
            c?.Tuning.Set(Owner, "CoatColorizeAmount", 1f);
            yield return Until(() => c != null && c.IsReady && c.I.TestColoursOurs() != null, 6f);
            yield return Frames(3);
            int s3 = Colors.SharedTextures.Count;
            if (s3 > s2) Pass($"another colour adds its own ({s2} -> {s3})");
            else Warn($"another colour added none ({s2} -> {s3}): someone in the world has it already?");
            a?.Remove();
            b?.Remove();
            c?.Remove();
            yield return Frames(2);
            Check(Colors.SharedTextures.Count == s0, $"all removed: back to {s0} ({Colors.SharedTextures.Count})");
        }

        // New views are made at most Lod.SpawnsPerFrame a frame (50 in one frame made a 20 ms frame).
        private static IEnumerator SpawnCap()
        {
            Lod.MaxSpawnsInAFrame = 0;
            var made = new List<MountHandle>();
            for (int k = 0; k < 20; k++)
            {
                Vector3 at = home + new Vector3(UnityEngine.Random.Range(-8f, 8f), 0f, UnityEngine.Random.Range(-4f, 4f));
                MountHandle h = Spawn(at, k % 2 == 0);
                if (h != null) made.Add(h);
            }
            yield return Until(() => made.TrueForAll(h => !h.Exists || h.I.HasView), 10f);
            Check(waited, $"{made.Count} views made within 10 s");
            Check(Lod.MaxSpawnsInAFrame <= Lod.SpawnsPerFrame, $"at most {Lod.SpawnsPerFrame} made in a frame (most: {Lod.MaxSpawnsInAFrame})");
        }

        // A donkey that goes idle off screen, gets a facing while idle, and wakes again: its animator faces that way,
        // no cart part comes on, its own colours are on, nothing stays frozen; and riding it then works.
        private static IEnumerator Wake(bool switchOff)
        {
            Lod.IdleSwitchesOff = switchOff;
            Vector3 near = Spot(home, 3f) ?? home;
            Vector3? far = Spot(home, 26f);
            if (!far.HasValue)
            {
                Warn("no ground 26 units around the keeper: skipped");
                yield break;
            }
            MountHandle h = Spawn(near, right: false);
            if (h == null)
            {
                Fail("could not spawn");
                yield break;
            }
            h.Tuning.Set(Owner, "CoatColorize", "FF00FF");
            h.Tuning.Set(Owner, "CoatColorizeAmount", 1f);
            yield return Until(() => h.IsReady && IsActive(h), 6f);
            if (!waited)
            {
                Fail("never got ready and active next to the keeper");
                yield break;
            }
            yield return MoveKeeper(far.Value, homeFacing);
            if (!moved) yield break;
            yield return Until(() => h.Exists && h.I.tier == Mount.Tier.Idle, 6f);
            if (!waited)
            {
                Warn($"not idle with the keeper 26 away (level {h.I.tier}; on screen there?): skipped");
                yield break;
            }
            if (switchOff)
            {
                Check(h.I.TestSwitchedOff && !h.I.TestViewActive, "idle: its view is switched off");
                Check(!h.IsReady, "idle and switched off: IsReady is false");
                MountResult early = h.Ride(showMessage: false);
                Check(!early.Ok, $"idle and switched off: Ride refuses ({early.Reason})");
            }
            else
            {
                Check(h.I.TestQuietRenderers > 0 && h.I.TestViewActive, "idle: its view is frozen, still on");
            }
            h.MoveTo(h.Position, facingRight: true); // a new facing while idle: it must reach the animator on waking
            yield return Seconds(0.6f);
            Check(h.I.tier == Mount.Tier.Idle, "still idle after a move while far");
            PutKeeper(home, homeFacing);
            yield return Until(() => h.IsReady && IsActive(h) && h.I.TestViewActive, 5f);
            if (!waited)
            {
                Fail("did not wake with the keeper next to it");
                yield break;
            }
            yield return Frames(4);
            float? d = h.I.TestAnimatorDirection();
            Check(d.HasValue && Mathf.Abs(d.Value) < 1f, $"woken: the animator faces right as set while idle (Direction {Dir(d)})");
            Check(h.I.TestCartPartsOn() == 0, $"woken: no cart part on ({h.I.TestCartPartsOn()} on)");
            bool? c = h.I.TestColoursOurs();
            if (c == null) Warn("woken: colours not attached (colours off?)");
            else Check(c.Value, "woken: its own colours are on");
            Check(h.I.TestQuietRenderers == 0, "woken: nothing left frozen");
            MountResult ride = h.Ride(showMessage: false);
            Check(ride.Ok, $"woken: riding it works{(ride.Ok ? "" : " (" + ride.Reason + ")")}");
            yield return Frames(3);
            Check(Mounts.IsMounted && h.IsRidden && h.I.TestViewActive && h.I.TestQuietRenderers == 0, "ridden: awake and drawn");
            d = h.I.TestAnimatorDirection();
            bool wantRight = h.FacingRight;
            Check(d.HasValue && (wantRight ? Mathf.Abs(d.Value) < 1f : Mathf.Abs(Mathf.Abs(d.Value) - 180f) < 1f),
                $"ridden: the animator faces the ride's way ({(wantRight ? "right" : "left")}, Direction {Dir(d)})");
            Mounts.Dismount("self-test");
            yield return Frames(2);
            if (switchOff)
            {
                yield break;
            }
            // Frozen views are ready, so a ride can start on one directly (the keeper is put on its seat): the case of
            // the invisible ridden donkey. Riding must wake it fully (WakeForRide). The far spot is taken from where the
            // donkey stands now: the ride moved it, and 22 from the home spot it was still within the 40% screen margin
            // an active donkey needs to leave (first run, 2026-10-01).
            Vector3? farAgain = Spot(h.Position, 30f);
            if (!farAgain.HasValue)
            {
                Warn("no ground 30 units from the donkey: the frozen ride skipped");
                yield break;
            }
            yield return MoveKeeper(farAgain.Value, homeFacing);
            if (!moved) yield break;
            yield return Until(() => h.Exists && h.I.tier == Mount.Tier.Idle, 8f);
            if (!waited)
            {
                Warn($"not idle again with the keeper 26 away ({State(h)}): the frozen ride skipped");
                yield break;
            }
            yield return Frames(2);
            Check(h.I.TestQuietRenderers > 0 && h.I.TestViewActive, $"idle again after a ride: frozen ({State(h)})");
            if (h.I.TestQuietRenderers == 0)
            {
                yield break;
            }
            MountResult frozenRide = h.Ride(showMessage: false);
            Check(frozenRide.Ok, $"a frozen donkey can be ridden{(frozenRide.Ok ? "" : " (" + frozenRide.Reason + ")")}");
            yield return Frames(3);
            Check(h.IsRidden && h.I.TestViewActive && h.I.TestQuietRenderers == 0, "ridden from frozen: drawn and animated (not invisible)");
            yield return WorldViewsRight();
            float? fd = h.I.TestAnimatorDirection();
            bool fr = h.FacingRight;
            Check(fd.HasValue && (fr ? Mathf.Abs(fd.Value) < 1f : Mathf.Abs(Mathf.Abs(fd.Value) - 180f) < 1f),
                $"ridden from frozen: faces the ride's way ({(fr ? "right" : "left")}, Direction {Dir(fd)})");
            Mounts.Dismount("self-test");
            yield return Frames(2);
        }

        // The showcase's H on a donkey frozen off screen: brought to the keeper and ridden in the same frame. Riding a
        // switched-off view must wait for it to wake; once ridden it is drawn and faces the keeper's way.
        private static IEnumerator MountWhileIdle()
        {
            Lod.IdleSwitchesOff = true;
            Vector3 near = Spot(home, 3f) ?? home;
            Vector3? far = Spot(home, 26f);
            if (!far.HasValue)
            {
                Warn("no ground 26 units around the keeper: skipped");
                yield break;
            }
            float keeperFacing = -homeFacing; // the donkey faces the other way, so it has to turn
            MountHandle h = Spawn(near, right: keeperFacing < 0f);
            if (h == null)
            {
                Fail("could not spawn");
                yield break;
            }
            yield return Until(() => h.IsReady && IsActive(h), 6f);
            far = Spot(h.Position, 30f) ?? far; // from where it stands: 26 from home was within the screen margin once
            yield return MoveKeeper(far.Value, keeperFacing);
            if (!moved) yield break;
            yield return Until(() => h.Exists && h.I.tier == Mount.Tier.Idle, 6f);
            if (!waited)
            {
                Warn($"not idle with the keeper 26 away (level {h.I.tier}): skipped");
                yield break;
            }
            PutKeeper(home, keeperFacing);
            MountResult come = h.ComeToKeeper(keeperFacing > 0f);
            MountResult sameFrame = h.Ride(showMessage: false);
            Check(come.Ok, "ComeToKeeper works on an idle donkey");
            Check(!sameFrame.Ok, $"riding it in the same frame waits (refused: {sameFrame.Reason})");
            yield return Until(() => h.IsReady, 3f);
            Check(waited, "ready within 3 s after coming");
            MountResult ride = h.Ride(showMessage: false);
            Check(ride.Ok, $"ridden once ready{(ride.Ok ? "" : " (" + ride.Reason + ")")}");
            yield return Frames(4);
            float? d = h.I.TestAnimatorDirection();
            bool right = keeperFacing > 0f;
            Check(h.I.TestViewActive && h.I.TestQuietRenderers == 0, "ridden: drawn (not frozen)");
            Check(d.HasValue && (right ? Mathf.Abs(d.Value) < 1f : Mathf.Abs(Mathf.Abs(d.Value) - 180f) < 1f),
                $"ridden: faces the keeper's way ({(right ? "right" : "left")}, Direction {Dir(d)})");
            Mounts.Dismount("self-test");
            yield return Frames(2);
        }

        // Views released far away are parked and taken again by the next donkey of the same scene, kind and size: a
        // foal's view never under a full donkey, no cart parts, its own colours.
        private static IEnumerator Reuse()
        {
            Vector3 near = Spot(home, 3f) ?? home;
            Vector3? far = Spot(home, 50f) ?? Spot(home, 42f);
            if (!far.HasValue)
            {
                Warn("no ground 42 to 50 units around the keeper: skipped");
                yield break;
            }
            MountHandle d1 = Spawn(near, right: true);
            MountHandle f1 = Spawn(near + new Vector3(1.5f, 0f, 0f), right: false, scale: 0.6f);
            yield return Until(() => d1.IsReady && f1.IsReady, 6f);
            if (!waited)
            {
                Fail("the first two never got their views");
                yield break;
            }
            int parked0 = Lod.ParkedTotal, poolBefore = ViewPool.Count;
            yield return MoveKeeper(far.Value, homeFacing);
            if (!moved) yield break;
            yield return Until(() => d1.I.tier == Mount.Tier.Asleep && f1.I.tier == Mount.Tier.Asleep, 6f);
            if (!waited)
            {
                Warn($"not asleep with the keeper far away (levels {d1.I.tier} / {f1.I.tier}): skipped");
                yield break;
            }
            yield return Frames(2);
            Check(Lod.ParkedTotal - parked0 >= 2, $"both views parked ({Lod.ParkedTotal - parked0} parked; pool {poolBefore} -> {ViewPool.Count})");
            int reused0 = Lod.ReusedTotal; // from before the keeper comes back: the world's donkeys wake with him
            d1.Remove();
            f1.Remove();
            PutKeeper(home, homeFacing);
            yield return Seconds(0.5f);
            MountHandle d2 = Spawn(near, right: false);
            MountHandle f2 = Spawn(near + new Vector3(1.5f, 0f, 0f), right: true, scale: 0.6f);
            yield return Until(() => d2.IsReady && f2.IsReady, 6f);
            Check(waited, "the second two got their views");
            yield return Frames(3);
            // Anyone may take a parked view: the world's own donkeys waking as the keeper comes back take them too
            // (third run, 2026-10-01: the world's carts and donkeys woke with the test's two and took one).
            int reusedNow = Lod.ReusedTotal - reused0;
            Check(reusedNow >= 2, $"parked views were taken again ({reusedNow} reused, by the test's donkeys or the world's waking)");
            yield return WorldViewsRight();
            Check(Mathf.Abs(d2.I.TestViewScale - 1f) < 0.01f, $"the donkey's view is full size ({d2.I.TestViewScale:F2})");
            Check(Mathf.Abs(f2.I.TestViewScale - 0.6f) < 0.01f, $"the foal's view is foal size ({f2.I.TestViewScale:F2})");
            Check(d2.I.TestCartPartsOn() == 0 && f2.I.TestCartPartsOn() == 0, "no cart part on either");
            float? dd = d2.I.TestAnimatorDirection(), fd = f2.I.TestAnimatorDirection();
            Check(dd.HasValue && Mathf.Abs(Mathf.Abs(dd.Value) - 180f) < 1f, $"the donkey faces left as spawned (Direction {Dir(dd)})");
            Check(fd.HasValue && Mathf.Abs(fd.Value) < 1f, $"the foal faces right as spawned (Direction {Dir(fd)})");
        }

        // A parent ridden away: its foals stay active and trot after it, also off screen, without jumps.
        private static IEnumerator FoalsFollow()
        {
            Vector3 near = Spot(home, 3f) ?? home;
            Vector3? far = Spot(home, 22f);
            if (!far.HasValue)
            {
                Warn("no ground 22 units around the keeper: skipped");
                yield break;
            }
            MountHandle p = Spawn(near, right: true);
            if (p == null)
            {
                Fail("could not spawn the parent");
                yield break;
            }
            yield return Until(() => p.IsReady, 6f);
            var foals = new List<MountHandle>();
            for (int k = 0; k < 2; k++)
            {
                MountHandle f = Mounts.SpawnFoal(Owner, p, 0.6f, persist: false);
                if (f != null)
                {
                    f.Tuning.Set(Owner, "ShuffleFollowAlways", true); // trot however far, so a jump would be a failure
                    foals.Add(f);
                }
            }
            yield return Until(() => foals.TrueForAll(f => f.IsReady), 6f);
            if (foals.Count == 0 || !waited)
            {
                Fail("the foals never got their views");
                yield break;
            }
            MountResult ride = p.Ride(showMessage: false);
            if (!ride.Ok)
            {
                Fail("could not ride the parent: " + ride.Reason);
                yield break;
            }
            yield return Frames(3);
            int jumps0 = Mount.CatchUpCount;
            yield return MoveKeeper(far.Value, homeFacing); // the parent goes with the keeper
            if (!moved) yield break;
            yield return Seconds(0.6f);
            bool allActive = true;
            float end = Time.unscaledTime + 25f;
            while (Time.unscaledTime < end && !abort)
            {
                foreach (MountHandle f in foals)
                {
                    if (f.Exists && f.I.tier != Mount.Tier.Active) allActive = false;
                }
                if (foals.TrueForAll(f => f.Exists && Vector3.Distance(f.Position, p.Position) < 4f))
                {
                    break;
                }
                yield return null;
            }
            var dist = new StringBuilder();
            bool close = true;
            foreach (MountHandle f in foals)
            {
                float dd = Vector3.Distance(f.Position, p.Position);
                close &= dd < 4f;
                dist.Append($" {f.FullId} {dd:F1}");
            }
            int jumps = Mount.CatchUpCount - jumps0;
            Check(allActive, "the foals stayed active while their parent was");
            yield return WorldViewsRight();
            if (close && jumps == 0) Pass($"both reached their parent 22 away by trotting, no jumps (distances{dist})");
            else if (close) Warn($"both reached their parent, with {jumps} jump(s) (blocked on the way?){dist}");
            else Fail($"not with their parent after 25 s (distances{dist}; {jumps} jump(s))");
            Mounts.Dismount("self-test");
            yield return Frames(2);
        }

        // Spawning and removing many, twice: every count of what the mod makes itself comes back to where it was.
        private static IEnumerator Leaks()
        {
            yield return Settle(15f); // the world's own donkeys still waking and sleeping after the last scenario's moves
            int tex0 = Live.Textures, mat0 = Live.Materials, lamp0 = Live.LampObjects, mesh0 = Live.Meshes, cargo0 = Live.CargoHolders;
            int shared0 = Colors.SharedTextures.Count, inst0 = Mount.All.Count;
            for (int cycle = 1; cycle <= 2; cycle++)
            {
                var made = new List<MountHandle>();
                for (int k = 0; k < 80; k++)
                {
                    Vector2 r = UnityEngine.Random.insideUnitCircle * 40f;
                    MountHandle h = Spawn(home + new Vector3(r.x, 0f, r.y), k % 2 == 0, withCart: k % 10 == 0);
                    if (h != null) made.Add(h);
                }
                yield return Seconds(4f);
                foreach (MountHandle h in made)
                {
                    if (h.Exists) h.Remove();
                }
                yield return Seconds(2f);
                Info($"cycle {cycle}: {made.Count} spawned and removed; pool {ViewPool.Count}");
            }
            yield return Settle(15f);
            Check(Mount.All.Count == inst0, $"instances back ({inst0} -> {Mount.All.Count})");
            Check(Live.Materials == mat0, $"colour materials back ({mat0} -> {Live.Materials})");
            Check(Live.Textures == tex0 && Colors.SharedTextures.Count == shared0, $"colour textures back ({tex0} -> {Live.Textures}, shared {shared0} -> {Colors.SharedTextures.Count})");
            Check(Live.LampObjects == lamp0 && Live.Meshes == mesh0, $"lamps and meshes back ({lamp0}/{mesh0} -> {Live.LampObjects}/{Live.Meshes})");
            Check(Live.CargoHolders == cargo0, $"cargo holders back ({cargo0} -> {Live.CargoHolders})");
        }

        // Informational: the frame time at the keeper's spot with 300 test donkeys around, in each mode. The camera
        // doesn't move, so the modes compare.
        private static IEnumerator FrameTimes()
        {
            var made = new List<MountHandle>();
            for (int k = 0; k < 300; k++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * 60f;
                MountHandle h = Spawn(home + new Vector3(r.x, 0f, r.y), k % 2 == 0);
                if (h != null) made.Add(h);
            }
            yield return Seconds(6f);
            var ms = new double[4];
            string[] names = { "levels on, idle switched off", "levels on, idle frozen", "levels off", "levels on again" };
            for (int mode = 0; mode < 4; mode++)
            {
                Lod.On = mode != 2;
                Lod.IdleSwitchesOff = mode != 1;
                yield return Seconds(mode == 2 ? 6f : 3f); // levels off: every view is made again, one a frame
                double sum = 0;
                int n = 0;
                float end = Time.unscaledTime + 3f;
                while (Time.unscaledTime < end && !abort)
                {
                    sum += Time.unscaledDeltaTime * 1000.0;
                    n++;
                    yield return null;
                }
                ms[mode] = n > 0 ? sum / n : 0;
                Info($"{names[mode]}: {ms[mode]:F2} ms a frame ({(ms[mode] > 0 ? 1000.0 / ms[mode] : 0):F0} fps) with {made.Count} test donkeys, the mod {Perf.AvgMs[(int)Perf.S.Tick]:F2} ms");
            }
            if (ms[2] > 0)
            {
                Info($"levels save {ms[2] - ms[0]:F2} ms a frame here; switching off instead of freezing saves {ms[1] - ms[0]:F2} ms");
            }
            Lod.On = true;
        }
    }
}

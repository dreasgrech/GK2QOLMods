using System;
using System.Collections;
using System.Collections.Generic;
using LazyBearTechnology;
using UnityEngine;

namespace GK2CorpsePickup
{
    // In-game self-tests (Debug.SelfTestKey): the redirect itself, the notification swap, many bodies at once, the
    // never-rot property and the fall-back paths, run against the loaded save. Every test body is removed again at
    // the end (unless Debug.SelfTestLeaveBodies), and cur_bodies_count is never touched: the tests call the mod's
    // drop step directly, not the game's graph, so the game's count node never runs. Lines go to the log as [test].
    internal static class SelfTest
    {
        internal static bool Running { get; private set; }
        internal static string Status { get; private set; } = "Not run this session.";

        private static int pass, fail, warn;
        private static string current = "";
        private static readonly List<DropData> testDrops = new List<DropData>();

        internal static void Start(MonoBehaviour host)
        {
            if (Running)
            {
                return;
            }
            host.StartCoroutine(Drive(Run()));
        }

        private sealed class Scenario
        {
            internal string Name;
            internal Func<IEnumerator> Body;
        }

        private static IEnumerator Run()
        {
            pass = fail = warn = 0;
            testDrops.Clear();
            if (MainGame.Instance?.GameSave?.worldData == null || MainGame.PlayerData == null || MainGame.PlayerController == null)
            {
                Status = "Not in a game (load a save first).";
                Plugin.Log.LogInfo("[test] " + Status);
                yield break;
            }
            Status = "Running...";
            int countBefore = MainGame.PlayerData.GetResInt("cur_bodies_count");
            bool rotBefore = Plugin.BodiesNeverRot.Value;
            string pointBefore = Plugin.DropPoint.Value;
            Line($"[test] start: game build {Application.version}, drop point {pointBefore}, never rot {rotBefore}, cur_bodies_count {countBefore}, keeper at {Keeper():F1}");

            var scenarios = new List<Scenario>
            {
                new Scenario { Name = "drop points", Body = DropPoints },
                new Scenario { Name = "one body redirected", Body = OneBody },
                new Scenario { Name = "notification swapped once", Body = Notification },
                new Scenario { Name = "many bodies at once", Body = ManyBodies },
                new Scenario { Name = "never rot", Body = NeverRot },
                new Scenario { Name = "unknown point steps aside", Body = UnknownPoint },
                new Scenario { Name = "walk decisions", Body = WalkDecisions },
            };
            foreach (Scenario s in scenarios)
            {
                current = s.Name;
                Line($"[test] --- {s.Name}");
                yield return s.Body();
                // Each scenario's settings changes are undone here too, in case it threw half way.
                Plugin.BodiesNeverRot.Value = rotBefore;
                Plugin.DropPoint.Value = pointBefore;
            }

            current = "cleanup";
            if (Plugin.SelfTestLeaveBodies.Value)
            {
                Info($"{testDrops.Count} test bodies left lying for you (Debug.SelfTestLeaveBodies); they are real bodies the game will count only when you put them away");
            }
            else
            {
                yield return RemoveTestDrops();
                Check(CountTestDropsInWorld() == 0, "no test body left in the world");
            }
            Check(MainGame.PlayerData.GetResInt("cur_bodies_count") == countBefore, $"cur_bodies_count unchanged ({countBefore})");
            Delivery.ResetWarnings();
            string summary = $"{pass} passed, {fail} failed, {warn} warning(s).";
            Line("[test] done: " + summary);
            Status = summary;
        }

        // Runs a coroutine with its nested coroutines itself, so an exception anywhere ends only that scenario (FAIL)
        // and the run goes on (Unity would stop the whole coroutine).
        private static IEnumerator Drive(IEnumerator root)
        {
            Running = true;
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

        // ------------------------------------------------------------------ scenarios

        private static IEnumerator DropPoints()
        {
            GDPointData first = null;
            foreach (string id in Plugin.DropPointChoices)
            {
                GDPointData p = Point(id);
                Check(p != null && !string.IsNullOrEmpty(p.GameSceneDataId), $"{id} exists{(p == null ? "" : $" at {p.Position:F1} in {p.GameSceneDataId}")}");
                if (p == null) continue;
                if (first == null) first = p;
                else Check(Vector3.Distance(first.Position, p.Position) < 12f && first.GameSceneDataId == p.GameSceneDataId, $"{id} is by the nun too ({Vector3.Distance(first.Position, p.Position):F1} from {Plugin.DropPointChoices[0]})");
            }
            foreach (string id in Plugin.StandPointChoices)
            {
                GDPointData sp = Point(id);
                Check(sp != null && first != null && Vector3.Distance(first.Position, sp.Position) < 15f, $"stand point {id} exists{(sp == null ? "" : $" at {sp.Position:F1}, {Vector3.Distance(first.Position, sp.Position):F1} from the drop point")}");
            }
            // The walk out: the game's spawn point links to the default stand point on the GD graph.
            GDPointData spawn = Point("donkey_01"), road = Point("donkey_01_01");
            Check(spawn != null && road != null && Vector3.Distance(spawn.Position, road.Position) < 5f, $"the walk out is short ({(spawn == null || road == null ? -1f : Vector3.Distance(spawn.Position, road.Position)):F1} units)");
            GDPointData spot = Point("RT_spot");
            Check(spot != null, "RT_spot (the hidden donkey's parking) exists");
            Check(Point("RT_body_spawn") != null && Point("RT_morgue_body_spawn") != null, "the graph's two fallback drop points exist (no door / pallets full)");
            Info(Delivery.DescribeMorgue());
            yield break;
        }

        private static IEnumerator OneBody()
        {
            GDPointData point = Point(Plugin.DropPoint.Value);
            if (point == null) { Fail("no drop point"); yield break; }
            Item body = NewBody();
            int parts = body.Inventory.Count;
            Check(body.Count == 1 && parts > 0, $"generated {body.id} with {parts} items inside (parts, pockets, reward)");
            bool ok = Delivery.TryLeaveAtCrossroads(body);
            Check(ok, "the mod's drop step took it");
            if (!ok) yield break;
            Check(Delivery.TakeRedirectedNotification(), "one notification is pending for it (same frame)");
            Check(!Delivery.TakeRedirectedNotification(), "and only one");
            DropData drop = FindDrop(body);
            Check(drop != null, "it is a drop in the point's scene");
            if (drop == null) yield break;
            testDrops.Add(drop);
            Check(ReferenceEquals(drop.Item, body), "the very same item instance (nothing rebuilt)");
            Check(drop.Item.Inventory.Count == parts, $"its inside is intact ({drop.Item.Inventory.Count} items)");
            Check(drop.DropType == DropType.Item, $"a plain item drop ({drop.DropType})");
            // The game nudges a new drop a little on placement (ground snap, separation), so within one unit.
            Check(Vector3.Distance(drop.Position, point.Position) < 1f, $"placed at the point ({Vector3.Distance(drop.Position, point.Position):F2} from it, {drop.Position:F2})");
            Check(drop.AutoDestroyTimer > 0f, $"the game's rot timer started ({drop.AutoDestroyTimer:F0} s), as for its own ground bodies");
            yield return Frames(3);
            DropView view = FindView(body);
            if (view != null) Pass($"it has a view at {view.transform.position:F1}");
            else if (Vector3.Distance(Keeper(), point.Position) > 60f) Warn("no view (the keeper is far away; the scene may not draw it there)");
            else Fail("no view although the keeper is near");
        }

        private static IEnumerator Notification()
        {
            UINotificator notificator = LazySingleton<UINotificator>.Instance;
            if (notificator == null) { Fail("no UINotificator"); yield break; }
            string text = Plugin.NotificationText.Value;
            Item body = NewBody();
            if (!Delivery.TryLeaveAtCrossroads(body)) { Fail("drop step refused"); yield break; }
            DropData drop = FindDrop(body);
            if (drop != null) testDrops.Add(drop);
            int oursBefore = CountNotifications<UISimpleTextWithIconNotification>(text), stockBefore = CountNotifications<UINewBodyNotification>(null);
            notificator.ShowNewBodyNotification("body_0_1"); // what the graph's node calls, same frame as the redirect
            if (string.IsNullOrWhiteSpace(text))
            {
                Check(CountNotifications<UINewBodyNotification>(null) == stockBefore + 1, "empty text: the game's own New Body notification shows");
            }
            else
            {
                Check(CountNotifications<UISimpleTextWithIconNotification>(text) == oursBefore + 1, $"our notification shows (\"{text}\")");
                Check(CountNotifications<UINewBodyNotification>(null) == stockBefore, "and the game's New Body does not");
            }
            yield return null; // next frame: nothing pending any more
            stockBefore = CountNotifications<UINewBodyNotification>(null);
            notificator.ShowNewBodyNotification("body_0_1");
            Check(CountNotifications<UINewBodyNotification>(null) == stockBefore + 1, "without a redirected body the game's own notification shows (the nun's script path)");
            string icon = Delivery.IconFor("body_0_1");
            Check(!string.IsNullOrEmpty(icon) && LazySingletonSO<EasySpritesCollection>.Instance.GetSprite(icon) != null, $"the icon resolves ({icon})");
        }

        private static IEnumerator ManyBodies()
        {
            const int n = 12;
            GDPointData point = Point(Plugin.DropPoint.Value);
            if (point == null) { Fail("no drop point"); yield break; }
            var bodies = new List<Item>();
            int took = 0;
            for (int i = 0; i < n; i++)
            {
                Item body = NewBody();
                bodies.Add(body);
                if (Delivery.TryLeaveAtCrossroads(body)) took++;
            }
            Check(took == n, $"{took} of {n} bodies taken in one frame (the graph's loop delivers them like this)");
            int pending = 0;
            while (Delivery.TakeRedirectedNotification()) pending++;
            Check(pending == took, $"{pending} notifications pending, one per body");
            var drops = new List<DropData>();
            foreach (Item b in bodies)
            {
                DropData d = FindDrop(b);
                if (d != null) { drops.Add(d); testDrops.Add(d); }
            }
            Check(drops.Count == took, $"{drops.Count} drops in the scene, all count 1: {drops.TrueForAll(d => d.Count == 1)}");
            yield return Seconds(1.5f); // the views settle and push apart
            var views = new List<DropView>();
            foreach (Item b in bodies)
            {
                DropView v = FindView(b);
                if (v != null) views.Add(v);
            }
            if (views.Count == 0 && Vector3.Distance(Keeper(), point.Position) > 60f)
            {
                Warn("no views (the keeper is far away); positions not judged");
                yield break;
            }
            Check(views.Count == took, $"{views.Count} views");
            int tooClose = 0, far = 0, sunk = 0;
            for (int i = 0; i < views.Count; i++)
            {
                Vector3 p = views[i].transform.position;
                if (Vector3.Distance(p, point.Position) > 8f) far++;
                if (p.y < point.Position.y - 1f || p.y > point.Position.y + 2.5f) sunk++;
                for (int j = i + 1; j < views.Count; j++)
                {
                    if (Vector3.Distance(p, views[j].transform.position) < 0.3f) tooClose++;
                }
            }
            Check(tooClose == 0, $"no two bodies on the same spot ({tooClose} pairs closer than 0.3)");
            Check(far == 0, $"all within 8 of the point ({far} further)");
            Check(sunk == 0, $"all at ground level ({sunk} off in height)");
            int still = 0;
            foreach (DropData d in drops) if (FindDrop(d.Item) != null) still++;
            Check(still == drops.Count, $"all {still} still separate drops after settling (none merged, none lost)");
        }

        private static IEnumerator NeverRot()
        {
            Plugin.BodiesNeverRot.Value = true;
            Item body = NewBody();
            bool ok = Delivery.TryLeaveAtCrossroads(body);
            Plugin.BodiesNeverRot.Value = false;
            Check(ok, "drop step took it");
            DropData drop = FindDrop(body);
            if (drop == null) { Fail("no drop"); yield break; }
            testDrops.Add(drop);
            Delivery.TakeRedirectedNotification();
            Check(body.HasProperty<NeverAutoDestroyDropSerializedItemProperty>(), "the game's never-auto-destroy property is on the body");
            Check(drop.CanNotBeAutoDestroyed.ResultFlag, "the drop is flagged as never auto-destroyed");
            Item plain = NewBody();
            if (Delivery.TryLeaveAtCrossroads(plain))
            {
                Delivery.TakeRedirectedNotification();
                DropData d2 = FindDrop(plain);
                if (d2 != null) testDrops.Add(d2);
                Check(d2 != null && !plain.HasProperty<NeverAutoDestroyDropSerializedItemProperty>(), "with the setting off a body gets no such property");
            }
            yield break;
        }

        private static IEnumerator UnknownPoint()
        {
            Item body = NewBody();
            int dropsBefore = CountAllDrops();
            bool ok = Delivery.TryLeaveAt(body, "gk2_corpse_pickup_no_such_point");
            Check(!ok, "the drop step steps aside (the game's own delivery would run)");
            Check(CountAllDrops() == dropsBefore, "and nothing was dropped");
            Check(!Delivery.TakeRedirectedNotification(), "and no notification is pending");
            Check(FindDrop(body) == null, "the unused body is nowhere in the world");
            yield break;
        }

        private static IEnumerator WalkDecisions()
        {
            Vector3 start = new Vector3(77.6f, 3.6f, 14.65f), morgue = new Vector3(24.96f, 3.6f, 15.6f);
            Check(Delivery.Decide("full_cycle_back", true, start, null, null) == Delivery.WalkAction.UnloadHere, "delivery walk, no quest talk: unload where it stands");
            Check(Delivery.Decide("full_cycle_back", true, start, null, "21_base_donkey_nun") == Delivery.WalkAction.Vanilla, "delivery walk while a quest talk waits at the morgue: the game's walk");
            Check(Delivery.Decide("Disable", true, start, start, null) == Delivery.WalkAction.LeaveHere, "walk back to where it already stands: leave from here");
            Check(Delivery.Decide("Disable", true, morgue, start, null) == Delivery.WalkAction.Vanilla, "walk back from the morgue (after a talk): the game's walk");
            Vector3 stand = new Vector3(74.64f, 3.6f, 15.6f);
            Check(Delivery.Decide("Disable", true, stand, start, null) == Delivery.WalkAction.Vanilla, "walk back from the stand point to the spawn point: the game's walk (it vanishes there)");
            Check(Delivery.Decide("Disable", true, start, null, null) == Delivery.WalkAction.Vanilla, "walk back with no known target: the game's walk");
            Check(Delivery.Decide("stay_morgue", true, start, null, null) == Delivery.WalkAction.Vanilla, "any other walk of the donkey: the game's walk");
            Check(Delivery.Decide("full_cycle_back", false, start, null, null) == Delivery.WalkAction.Vanilla, "another WGO's walk: the game's walk");
            string hold = Delivery.QuestHoldingDonkeyAtMorgue();
            Info(hold == null ? "no quest holds the donkey at the morgue in this save" : $"quest {hold} holds the donkey at the morgue in this save (deliveries walk as vanilla until it is done)");
            WgoData donkey = MainGame.Instance.GameSave.worldData.GetWgoDataByCustomTag("npc_donkey") ?? MainGame.Instance.GameSave.worldData.GetWgoData("npc_donkey");
            Check(donkey != null && Delivery.IsDonkey(donkey), $"the donkey WGO is found and recognised{(donkey == null ? "" : $" (id {donkey.id}, tag {donkey.CustomTag}, at {donkey.Position:F1})")}");
            yield break;
        }

        // ------------------------------------------------------------------ helpers

        private static Vector3 Keeper() => MainGame.PlayerData.position.Value;

        private static GDPointData Point(string id) => MainGame.Instance.GameSave.worldData.gdPointsData.GetGDPointDataById(id);

        // A body the way the game's "Generate Body" node makes one for the player's tier (any def of that tier).
        private static Item NewBody()
        {
            int tier = MainGame.PlayerData.GetResInt("bodies_tier");
            BodyDef pick = null;
            foreach (BodyDef def in GameBalance.Me.bodyDefs)
            {
                if (def.tier == tier) { pick = def; break; }
            }
            pick = pick ?? GameBalance.Me.GetData<BodyDef>("body_0_1") ?? GameBalance.Me.bodyDefs[0];
            return pick.GenerateItem();
        }

        private static DropData FindDrop(Item item)
        {
            foreach (GameSceneData scene in MainGame.Instance.GameSave.worldData.gameSceneDataList)
            {
                foreach (DropData d in scene.droppedItems) if (d.Item == item || d.UniqueId == item.UniqueId) return d;
                foreach (DropData d in scene.queuedDrops) if (d.Item == item || d.UniqueId == item.UniqueId) return d;
            }
            return null;
        }

        private static DropView FindView(Item item)
        {
            foreach (GameScene scene in LazySingleton<GameSceneManager>.Instance.LoadedGameScenes)
            {
                if (scene.TryGetDropView(item, out DropView v) && v != null) return v;
            }
            return null;
        }

        private static int CountAllDrops()
        {
            int n = 0;
            foreach (GameSceneData scene in MainGame.Instance.GameSave.worldData.gameSceneDataList) n += scene.droppedItems.Count + scene.queuedDrops.Count;
            return n;
        }

        private static int CountTestDropsInWorld()
        {
            int n = 0;
            foreach (DropData d in testDrops) if (FindDrop(d.Item) != null) n++;
            return n;
        }

        private static int CountNotifications<T>(string text) where T : UIBaseNotification
        {
            int n = 0;
            foreach (UIBaseNotification note in LazySingleton<UINotificator>.Instance.DisplayingItems)
            {
                if (note is T t && t.gameObject.activeSelf && (text == null || (t as UISimpleTextWithIconNotification)?.Text == text)) n++;
            }
            return n;
        }

        private static IEnumerator RemoveTestDrops()
        {
            foreach (DropData d in testDrops)
            {
                DropData live = FindDrop(d.Item);
                if (live != null)
                {
                    MainGame.Instance.dropSystem.RemoveDrop(live, live.WorldId);
                }
            }
            yield return Frames(2);
            Info($"{testDrops.Count} test bodies removed again");
        }

        private static IEnumerator Frames(int n)
        {
            for (int k = 0; k < n; k++) yield return null;
        }

        private static IEnumerator Seconds(float s)
        {
            float end = Time.unscaledTime + s;
            while (Time.unscaledTime < end) yield return null;
        }

        private static void Line(string s) => Plugin.Log.LogInfo(s);
        private static void Pass(string what) { pass++; Line($"[test] PASS {current}: {what}"); }
        private static void Fail(string what) { fail++; Line($"[test] FAIL {current}: {what}"); }
        private static void Warn(string what) { warn++; Line($"[test] WARN {current}: {what}"); }
        private static void Info(string what) => Line($"[test] INFO {current}: {what}");
        private static void Check(bool ok, string what) { if (ok) Pass(what); else Fail(what); }
    }
}

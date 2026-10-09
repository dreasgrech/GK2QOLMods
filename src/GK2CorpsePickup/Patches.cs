using System;
using FlowCanvas;
using GK2.FlowCanvasNodes;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2CorpsePickup
{
    // How the game delivers bodies (docs/corpse-delivery.md): the donkey's visual script (FlowCanvas graph
    // `npc_donkey`) walks the donkey from the crossroads (GD point donkey_01, by the nun) to the morgue, and there,
    // per body, runs "Generate Body" -> "Try To Add Corpse To Pallet" -> "Show New Body Notification" ->
    // cur_bodies_count + 1. The bodies are made at the morgue; the donkey only carries a dummy.
    //
    // This mod swaps the pallet step: the generated body is dropped at the crossroads with the game's own drop system
    // and the node's "yes" output is called, so the graph itself still shows the notification, counts the body and
    // fires its quest triggers. The notification prefix then replaces the game's "New Body" with our text while the
    // graph is in that loop (same frame), and never otherwise (the nun's own body script uses the same node).
    internal static class Delivery
    {
        // Set by the pallet prefix for each body it redirected, consumed by the notification prefix in the same frame.
        private static int redirectedPending;
        private static int redirectedFrame = -1;

        private static bool pointWarned;
        private static bool failWarned;

        internal static void ResetWarnings() { pointWarned = false; failWarned = false; }

        public static void Verify()
        {
            // The private ports Harmony injects into the prefixes: fail at startup, not at the first delivery, if a
            // game update renamed them.
            if (AccessTools.Field(typeof(Flow_TryToAddCorpseToPallet), "item") == null
                || AccessTools.Field(typeof(Flow_TryToAddCorpseToPallet), "yes") == null)
            {
                throw new InvalidOperationException("Flow_TryToAddCorpseToPallet no longer has the ports item / yes.");
            }
            foreach (string f in new[] { "out", "onFinish", "fireEventOnFinish", "gdPointData", "gdPointId", "useGdDataInsteadId", "navigation", "speed" })
            {
                if (AccessTools.Field(typeof(Flow_GoTo), f) == null)
                {
                    throw new InvalidOperationException("Flow_GoTo no longer has the field " + f + ".");
                }
            }
            if (GetWgoDataMethod == null)
            {
                throw new InvalidOperationException("GKCustomFlowNodeWithWgoData.GetWgoData not found.");
            }
            foreach (string f in new[] { "out", "item", "gdPointDataDropOn" })
            {
                if (AccessTools.Field(typeof(Flow_DropItem), f) == null)
                {
                    throw new InvalidOperationException("Flow_DropItem no longer has the field " + f + ".");
                }
            }
        }

        // While the donkey unloads at the crossroads (from the unload decision until well after the arrival chain),
        // so the graph's own fallback drops of that chain can be told from any other body drop of the donkey's graph.
        internal static float UnloadWindowUntil = -1f;
        internal static bool InUnloadWindow => Time.time < UnloadWindowUntil;

        // What the arrival chain will find, for the log: the morgue door decides pallets vs. the graph's own fallback.
        internal static string DescribeMorgue()
        {
            WorldData world = MainGame.Instance?.GameSave?.worldData;
            bool door = world?.GetWgoData("morgue_entrance_door") != null || world?.GetWgoDataByCustomTag("morgue_entrance_door") != null;
            int pallets = world?.GetWgoDataListByGroup("morgue_pallets")?.Count ?? 0;
            return $"morgue door {(door ? "present" : "absent")}, {pallets} corpse pallet(s), cur_bodies_count {MainGame.PlayerData?.GetResInt("cur_bodies_count")}";
        }

        // ---------------------------------------------------------------- the walk

        // The game's graph checks these before deciding whether the donkey waits at the morgue for a scripted talk
        // (nodes 49-56 of npc_donkey). While one holds, the walk stays vanilla, or the talk would start at the wrong
        // place.
        private static readonly (string quest, QuestStatus status)[] MorgueTalks =
        {
            ("21_base_donkey_nun", QuestStatus.Awaiting),
            ("137_base_resurrection_donkey", QuestStatus.InProgress),
            ("165_base_donkey1_speak", QuestStatus.InProgress),
            ("166_base_donkey2_speak", QuestStatus.InProgress),
            ("167_base_donkey3_speak", QuestStatus.InProgress),
        };

        public static string QuestHoldingDonkeyAtMorgue()
        {
            QuestSystemData quests = MainGame.Instance?.GameSave?.questSystemData;
            if (quests == null)
            {
                return null;
            }
            foreach (var (quest, status) in MorgueTalks)
            {
                if (quests.IsQuestInStatus(quest, status))
                {
                    return quest;
                }
            }
            return null;
        }

        public enum WalkAction { Vanilla, UnloadHere, LeaveHere }

        // What to do with a GoTo of the donkey's graph, by the event it fires on arrival. Pure, so the self-tests can
        // drive it with made-up positions.
        //  full_cycle_back: the delivery walk to the morgue. Unload here instead, unless a quest talk waits there.
        //  Disable: the walk back to the start before vanishing. When the donkey already stands there (it never left),
        //           the game's movement code would only call the node's onFinish and never fire Disable, leaving the
        //           donkey standing; fire it ourselves after a beat.
        //  (the walk back from the stand point to the spawn point is a real walk and stays the game's.)
        public static WalkAction Decide(string finishEvent, bool isDonkey, Vector3 wgoPos, Vector3? target, string questHold)
        {
            if (!isDonkey)
            {
                return WalkAction.Vanilla;
            }
            if (finishEvent == "full_cycle_back")
            {
                return questHold == null ? WalkAction.UnloadHere : WalkAction.Vanilla;
            }
            if (finishEvent == "Disable" && target.HasValue)
            {
                Vector3 d = wgoPos - target.Value;
                d.y = 0f;
                return d.magnitude < 1f ? WalkAction.LeaveHere : WalkAction.Vanilla;
            }
            return WalkAction.Vanilla;
        }

        // The stand point, when it is a real walk away from where the donkey is; null to unload on the spot.
        public static GDPointData StandPointToWalkTo(WgoData wgo)
        {
            GDPointData stand = MainGame.Instance?.GameSave?.worldData?.gdPointsData?.GetGDPointDataById(Plugin.StandPoint.Value);
            if (stand == null)
            {
                Plugin.Log.LogWarning($"Corpse Pickup: stand point \"{Plugin.StandPoint.Value}\" not found; the donkey unloads where it appears.");
                return null;
            }
            Vector3 d = wgo.Position - stand.Position;
            d.y = 0f;
            return d.magnitude > 0.5f ? stand : null;
        }

        public static bool IsDonkey(WgoData wgo) => wgo != null && (wgo.id == "npc_donkey" || wgo.CustomTag == "npc_donkey");

        private static readonly System.Reflection.MethodInfo GetWgoDataMethod = AccessTools.Method(typeof(GKCustomFlowNodeWithWgoData), "GetWgoData");

        public static WgoData WgoOf(GKCustomFlowNodeWithWgoData node)
        {
            try
            {
                return GetWgoDataMethod?.Invoke(node, null) as WgoData;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The body goes to the ground at the configured GD point. True = done, the caller calls "yes" and skips the
        // game's pallet code. False = nothing happened to the world; the caller lets the game's code run unchanged
        // (it generates a body of its own; the instance we read is simply never used).
        public static bool TryLeaveAtCrossroads(Item body) => TryLeaveAt(body, Plugin.DropPoint.Value);

        // pointId separately, so the self-tests can hand in a point that doesn't exist (the cfg entry only accepts
        // the three known ones).
        public static bool TryLeaveAt(Item body, string pointId)
        {
            try
            {
                if (body == null || body.IsEmpty || body.Definition == null)
                {
                    Warn(ref failWarned, "the generated body is empty; the game's own delivery runs");
                    return false;
                }
                // DropSystem.DropItem keeps the instance only for a single item; its Count > 1 path builds fresh items
                // by id, which would lose a body's parts, pockets and burial reward. Bodies are always 1.
                if (body.Count != 1)
                {
                    Warn(ref failWarned, $"body {body.id} has count {body.Count}; the game's own delivery runs");
                    return false;
                }
                WorldData world = MainGame.Instance?.GameSave?.worldData;
                GDPointData point = world?.gdPointsData?.GetGDPointDataById(pointId);
                if (point == null || string.IsNullOrEmpty(point.GameSceneDataId))
                {
                    Warn(ref pointWarned, $"drop point \"{pointId}\" not found in this world; the game's own delivery runs");
                    return false;
                }
                if (Plugin.BodiesNeverRot.Value && !body.HasProperty<NeverAutoDestroyDropSerializedItemProperty>())
                {
                    body.AddProperty(new NeverAutoDestroyDropSerializedItemProperty());
                }
                if (!MainGame.Instance.dropSystem.DropItem(body, point.GameSceneDataId, point.Position))
                {
                    Warn(ref failWarned, $"the game refused to drop {body.id} at {point.Id}; the game's own delivery runs");
                    return false;
                }
                if (redirectedFrame != Time.frameCount)
                {
                    redirectedPending = 0;
                    redirectedFrame = Time.frameCount;
                }
                redirectedPending++;
                Plugin.Log.LogInfo($"Corpse Pickup: {body.id} left at {point.Id} {point.Position:F1} in {point.GameSceneDataId}{(Plugin.BodiesNeverRot.Value ? ", never rots" : "")}; {MainGame.PlayerData.GetResInt("cur_bodies_count") + 1} bodies about after this one.");
                return true;
            }
            catch (Exception e)
            {
                Warn(ref failWarned, "failed; the game's own delivery runs. " + e);
                return false;
            }
        }

        // The notification that follows a redirected body, in the same frame (the graph's loop is synchronous).
        public static bool TakeRedirectedNotification()
        {
            if (redirectedFrame != Time.frameCount)
            {
                redirectedPending = 0;
                return false;
            }
            if (redirectedPending <= 0)
            {
                return false;
            }
            redirectedPending--;
            return true;
        }

        // The body item's icon as the game's notification shows it, or the game's generic body icon.
        public static string IconFor(string bodyId)
        {
            try
            {
                BodyDef body = GameBalance.Me.GetData<BodyDef>(bodyId);
                ItemDef item = body == null ? null : GameBalance.Me.GetData<ItemDef>(body.linkedBodyItemId);
                string iconId = item?.iconId;
                if (!string.IsNullOrEmpty(iconId) && LazySingletonSO<EasySpritesCollection>.Instance.GetSprite(iconId) != null)
                {
                    return iconId;
                }
            }
            catch (Exception)
            {
                // the generic icon below
            }
            return "i_body";
        }

        // Debug key: the same event the game's schedule sends (GameLogicDef "donkey": WGOFireEvent("npc_donkey", "full_cycle")).
        public static void StartDeliveryNow()
        {
            // The game's expression resolves the name as a custom tag (LazyExpression "WGOFireEvent"); the id as a fallback.
            WorldData world = MainGame.Instance?.GameSave?.worldData;
            WgoData donkey = world?.GetWgoDataByCustomTag("npc_donkey") ?? world?.GetWgoData("npc_donkey");
            if (donkey == null)
            {
                Plugin.Log.LogWarning("Corpse Pickup: no npc_donkey in this world (not in a game, or the donkey isn't in the save yet).");
                return;
            }
            // Away from its off-map parking (RT_spot) the donkey is already on a trip, or waiting at the morgue for a
            // quest talk; a second full_cycle would teleport it back to the start mid-walk.
            GDPointData spot = world.gdPointsData?.GetGDPointDataById("RT_spot");
            if (spot != null && Vector3.Distance(donkey.Position, spot.Position) > 3f)
            {
                Plugin.Log.LogInfo($"Corpse Pickup: test key ignored, the donkey is already out at {donkey.Position:F1} (a delivery under way, or a quest keeps it at the morgue).");
                return;
            }
            Plugin.Log.LogInfo($"Corpse Pickup: test key, full_cycle sent to the donkey (the game's roll decides whether it comes).");
            donkey.FireEvent("full_cycle");
        }

        private static void Warn(ref bool already, string what)
        {
            if (already)
            {
                return;
            }
            already = true;
            Plugin.Log.LogWarning("Corpse Pickup: " + what);
        }
    }

    // The node's private handler (GK2.FlowCanvasNodes.Flow_TryToAddCorpseToPallet.TryToAddCorpseToPallet(Flow)):
    // vanilla reads item.value once per pallet it tries (the port re-evaluates "Generate Body" on every read), adds
    // it to the first morgue pallet with room, and calls yes, else no. Used by the npc_donkey graph and two dev graphs
    // only (every bundle scanned, 2026-10-09).
    [HarmonyPatch(typeof(Flow_TryToAddCorpseToPallet), "TryToAddCorpseToPallet")]
    internal static class Flow_TryToAddCorpseToPallet_Patch
    {
        private static bool Prefix(Flow flow, ValueInput<Item> ___item, FlowOutput ___yes)
        {
            if (!Plugin.Enabled.Value || ___item == null || ___yes == null)
            {
                return true;
            }
            Item body = ___item.value; // exactly once: every read generates a new body
            if (!Delivery.TryLeaveAtCrossroads(body))
            {
                return true; // nothing changed; the game's pallet code runs with a body of its own
            }
            // Outside Delivery's try/catch on purpose: the rest of the graph runs from here (notification, count,
            // quest triggers), and a failure in it must surface as it would in the unmodified game, not re-run the
            // pallet code on top of a body that is already on the ground.
            ___yes.Call(flow);
            return false;
        }
    }

    // Flow_GoTo.GoTo (GK2.FlowCanvasNodes, protected virtual): starts the WGO's walk to a GD point and names the WGO
    // event to fire on arrival. The donkey's graph uses "full_cycle_back" for its delivery walk (start -> morgue) and
    // "Disable" for the walk back before it vanishes. With UnloadAtCrossroads the delivery walk is replaced by the
    // arrival event itself, a beat later, so the graph unloads right where the donkey appeared; the walk back is then
    // a walk to where it already stands, which the game's movement code finishes silently, so Disable is fired by us.
    // Every other GoTo in the game (other WGOs, other events, the player) is untouched: the prefix returns true.
    [HarmonyPatch(typeof(Flow_GoTo), "GoTo")]
    internal static class Flow_GoTo_GoTo_Patch
    {
        private static string holdLogged;

        private static bool Prefix(Flow_GoTo __instance, Flow flow, FlowOutput ___out, FlowOutput ___onFinish, ValueInput<string> ___fireEventOnFinish,
            ValueInput<GDPointData> ___gdPointData, ValueInput<string> ___gdPointId, bool ___useGdDataInsteadId,
            ValueInput<MovementType> ___navigation, ValueInput<float> ___speed)
        {
            if (!Plugin.Enabled.Value || !Plugin.UnloadAtCrossroads.Value || ___out == null)
            {
                return true;
            }
            try
            {
                string finishEvent = ___fireEventOnFinish?.value;
                if (finishEvent != "full_cycle_back" && finishEvent != "Disable")
                {
                    return true; // not one of the donkey's two walks: nothing to look at
                }
                WgoData wgo = Delivery.WgoOf(__instance);
                if (!Delivery.IsDonkey(wgo))
                {
                    return true;
                }
                Vector3? target = null;
                if (finishEvent == "Disable")
                {
                    GDPointData p = ___useGdDataInsteadId ? ___gdPointData?.value
                        : MainGame.Instance?.GameSave?.worldData?.gdPointsData?.GetGDPointDataById(___gdPointId?.value);
                    target = p?.Position;
                }
                string hold = finishEvent == "full_cycle_back" ? Delivery.QuestHoldingDonkeyAtMorgue() : null;
                Delivery.WalkAction action = Delivery.Decide(finishEvent, true, wgo.Position, target, hold);
                switch (action)
                {
                    case Delivery.WalkAction.UnloadHere:
                    {
                        // As vanilla: Out first (the graph fires its DonkeyStart quest trigger from it), then the walk.
                        ___out.Call(flow);
                        Delivery.UnloadWindowUntil = Time.time + 60f; // closed again when the walk back starts
                        GDPointData stand = Delivery.StandPointToWalkTo(wgo);
                        if (stand != null && wgo.MovementComponent != null)
                        {
                            // The node's own call with our destination: the same navigation, speed and arrival event, so the
                            // game's movement fires full_cycle_back when the donkey gets there and the graph unloads.
                            MovementComponent.StartPathResult r = wgo.MovementComponent.StartPath(stand.Position, wgo.WorldId, stand.GameSceneDataId,
                                ___navigation.value, ___speed.value, "full_cycle_back", delegate { ___onFinish.Call(flow); }, null);
                            if (r == MovementComponent.StartPathResult.Started)
                            {
                                Plugin.Log.LogInfo($"Corpse Pickup: the donkey walks out to {stand.Id} {stand.Position:F1} and unloads there instead of walking to the morgue ({Delivery.DescribeMorgue()}).");
                                return false;
                            }
                            Plugin.Log.LogWarning($"Corpse Pickup: the walk to {stand.Id} did not start ({r}); the donkey unloads where it appears.");
                        }
                        wgo.AddDelayedEvent("full_cycle_back", Mathf.Max(0.1f, Plugin.UnloadDelay.Value));
                        Plugin.Log.LogInfo($"Corpse Pickup: the donkey unloads where it appears ({wgo.Position:F1}) in {Plugin.UnloadDelay.Value:F1} s instead of walking to the morgue ({Delivery.DescribeMorgue()}).");
                        return false;
                    }
                    case Delivery.WalkAction.LeaveHere:
                        // It never left its spot (stand point = spawn point): the game's walk back would end at once
                        // without firing Disable, so fire it after a beat.
                        ___out.Call(flow);
                        Delivery.UnloadWindowUntil = -1f;
                        wgo.AddDelayedEvent("Disable", Mathf.Max(0.1f, Plugin.LeaveDelay.Value));
                        Plugin.Log.LogInfo($"Corpse Pickup: the donkey leaves from {wgo.Position:F1} in {Plugin.LeaveDelay.Value:F1} s.");
                        return false;
                    default:
                        if (finishEvent == "Disable" && Delivery.InUnloadWindow)
                        {
                            Delivery.UnloadWindowUntil = -1f; // the unload is over; the walk back is the game's
                            Plugin.Log.LogInfo($"Corpse Pickup: the donkey walks back from {wgo.Position:F1} and vanishes there, as in the game.");
                        }
                        if (hold != null && holdLogged != hold)
                        {
                            holdLogged = hold;
                            Plugin.Log.LogInfo($"Corpse Pickup: quest {hold} wants the donkey at the morgue for a talk; it walks there as in the unmodified game (the bodies still go to the crossroads).");
                        }
                        return true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Corpse Pickup: walk check failed; the donkey walks as in the unmodified game. " + e);
                return true;
            }
        }
    }

    // Flow_DropItem.Drop: the graph's own body drops. Its arrival chain uses two as fallbacks instead of the pallet
    // node: at RT_body_spawn (beside the morgue entrance) when the save has no morgue_entrance_door yet, and at
    // RT_morgue_body_spawn (inside the morgue) when every pallet is full. Both go to the crossroads too, but only for
    // the donkey's own graph, only those two points, and only while the donkey is unloading at the crossroads (the
    // graph's quest event drop_body uses the same node and point and stays the game's).
    [HarmonyPatch(typeof(Flow_DropItem), "Drop")]
    internal static class Flow_DropItem_Drop_Patch
    {
        private static bool Prefix(Flow_DropItem __instance, Flow flow, FlowOutput ___out, ValueInput<Item> ___item, ValueInput<GDPointData> ___gdPointDataDropOn)
        {
            if (!Plugin.Enabled.Value || !Delivery.InUnloadWindow || ___out == null || ___item == null)
            {
                return true;
            }
            try
            {
                if (!__instance.dropBody || __instance.dropTargetType != Flow_DropItem.DropTargetType.GDPoint)
                {
                    return true;
                }
                WgoDataScript script = CustomFlowNode.GetComponentFromNode<WgoDataScript>(__instance);
                if (script == null || !Delivery.IsDonkey(script.wgoData))
                {
                    return true;
                }
                string target = ___gdPointDataDropOn?.value?.Id;
                if (target != "RT_body_spawn" && target != "RT_morgue_body_spawn")
                {
                    return true;
                }
                Item body = ___item.value; // exactly once: this port, too, is a fresh "Generate Body" per read
                if (!Delivery.TryLeaveAtCrossroads(body))
                {
                    return true; // the game's drop with a body of its own
                }
                string why = target == "RT_body_spawn" ? "no morgue door yet" : "pallets full";
                Plugin.Log.LogInfo($"Corpse Pickup: the graph's own fallback drop at {target} ({why}) went to the crossroads instead.");
                ___out.Call(flow);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Corpse Pickup: fallback drop check failed; the game's own drop runs. " + e);
                return true;
            }
        }
    }

    // UINotificator.ShowNewBodyNotification(string bodyId): the pooled "New Body" notification with the body's icon.
    // The parameterless overload forwards to this one, so this is the only one to patch. Replaced only for a body
    // this mod just left at the crossroads (same frame), never for the game's other uses of the node.
    [HarmonyPatch(typeof(UINotificator), nameof(UINotificator.ShowNewBodyNotification), new[] { typeof(string) })]
    internal static class UINotificator_ShowNewBodyNotification_Patch
    {
        private static bool Prefix(UINotificator __instance, string bodyId)
        {
            if (!Plugin.Enabled.Value || !Delivery.TakeRedirectedNotification())
            {
                return true;
            }
            string text = Plugin.NotificationText.Value;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true; // the game's own "New Body"
            }
            try
            {
                // LLBase.L returns an unknown key verbatim, so the text itself serves as the key.
                __instance.ShowSimpleTextWithIconNotification(text, Delivery.IconFor(bodyId));
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Corpse Pickup: our notification failed, showing the game's. " + e);
                return true;
            }
        }
    }
}

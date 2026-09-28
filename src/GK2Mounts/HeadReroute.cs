using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GK2Mounts
{
    // While mounted in Cart style the cart is the keeper's hands: what the game would put on his head lands on the
    // cart, and what the game would take from his head it finds on the cart. Round 9 of the findings (three source
    // passes and the in-game probe GK2-head-facts.txt) established the exact points:
    //   sink      PlayerData.AddOverheadItem            every pickup (ground, piles, scripts, tables, zombies)
    //   gate      PlayerData.HasFreeOverheadSlot        every "may I take?" check (piles, scripts, stations, tables)
    //   lookup    PlayerData.TryGetOverheadItem         every consumer (piles, tables, graves, quests, scripts)
    //   mutation  PlayerData.RemoveOverheadItem(Item), PlayerData.InsertOverheadItemTo(WgoData, Item),
    //             LazyExpressionEvaluationScope.GetOverheadItemForMutation (script executions)
    // Everything that draws the head or drops what is held (HasOverheadItem, the counts, OverheadItems) keeps
    // reading the real head, which stays empty while riding, so nothing is drawn on the keeper and nothing is dumped
    // when he starts work. Zombies are the one exception: the cart refuses them (linked world objects), so they keep
    // the head, with its own limit honoured here because the gate now answers for the cart.
    //
    // Item identity (probe section E): adding to an inventory stores a NEW object with the same GUID and empties the
    // source. Take-from-table paths add first and remove from the table by GUID second, so the source shell gets its
    // count back after the split (the table's removal then fires its event). Consumers that add the cart's object to
    // their own inventory before removing it leave the cart entry at count 0, and the cart's removal is then silent,
    // so the cart is resynced after every removal.
    internal static class HeadReroute
    {
        private static readonly MethodInfo PlayTakeSound = AccessTools.Method(typeof(PlayerData), "PlayOverheadItemTakeSound");
        private static Item lastCartMatch;

        // The reroute is on only while the cart is the keeper's hands. During the mod's own unload the head is the
        // destination again.
        internal static bool Active => Mount.IsMounted && Mount.CurrentStyle == MountStyle.Cart && Cargo.Attached && !Cargo.Unloading;

        // Puts an item the game wanted on the head onto the cart, with the pickup's side effects. False when the cart
        // is full or refuses the item (the caller then falls back).
        internal static bool TakeToCart(PlayerData pd, Item item)
        {
            Inventory inv = Cargo.Inv;
            if (inv == null || !Cargo.Allowed(item) || !inv.CanAddItemToInventory(item))
            {
                return false;
            }
            int count = item.Count;
            if (!inv.AddItemToInventory(item))
            {
                return false;
            }
            item.Count = count; // the source shell: callers that still hold it remove it from its table by GUID next
            if (pd.HasInteractingItem)
            {
                pd.RemoveInteractingItem();
            }
            GlobalEventsSystem.FireTrigger(GlobalEventsSystem.Event.Type.AddOverhead, item.id);
            try
            {
                PlayTakeSound?.Invoke(pd, new object[] { item });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: take sound failed: " + e.Message);
            }
            Plugin.Log.LogInfo($"Cargo: picked up {item.id} straight onto the cart ({inv.Data.Inventory.Count}/{inv.Data.InventorySize}).");
            return true;
        }

        internal static Item FindOnCart(Item item)
        {
            Inventory inv = Cargo.Inv;
            if (inv == null || item == null)
            {
                return null;
            }
            Guid g = item.UniqueId.Guid;
            List<Item> list = inv.Data.Inventory;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Item it = list[i];
                if (it != null && it.UniqueId.Guid == g)
                {
                    return it;
                }
            }
            return null;
        }

        // Removes a cart entry by GUID. The inventory stays silent when the entry was already emptied by a split
        // elsewhere, so the cart is resynced whatever the inventory reported.
        internal static bool RemoveFromCart(Item stored)
        {
            Inventory inv = Cargo.Inv;
            if (inv == null)
            {
                return false;
            }
            bool announced = inv.RemoveItemFromInventoryByUID(stored);
            bool gone = FindOnCart(stored) == null;
            if (!announced)
            {
                Cargo.Resync(gone ? "silent removal" : "removal failed");
            }
            return gone;
        }

        internal static Item LastCartMatch => lastCartMatch != null && FindOnCart(lastCartMatch) != null ? lastCartMatch : null;

        internal static void RememberMatch(Item item)
        {
            lastCartMatch = item;
        }
    }

    // ---------------------------------------------------------------------- the sink

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.AddOverheadItem))]
    internal static class PlayerData_AddOverheadItem_Patch
    {
        private static bool Prefix(PlayerData __instance, Item item)
        {
            try
            {
                if (!HeadReroute.Active || item == null || item.IsEmpty)
                {
                    return true;
                }
                if (!Cargo.Allowed(item))
                {
                    // A zombie keeps the head. The original drops what the head holds when the (now cart-answering)
                    // gate says full, so the head's own limit is applied here first.
                    if (__instance.OverheadCount >= __instance.OverheadStackLimit)
                    {
                        __instance.DropOverheadItem();
                    }
                    return true;
                }
                if (HeadReroute.TakeToCart(__instance, item))
                {
                    return false;
                }
                // Cart full: the item has already left wherever it was (the drop is gone, the pile removed it), so it
                // reappears in front of the keeper instead of vanishing or landing on the head.
                Cargo.DropAtKeeper(__instance, item);
                Mount.Say("The cart is full.");
                Plugin.Log.LogInfo($"Cargo: cart full, {item.id} dropped in front of the keeper.");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: pickup reroute failed, the game's own path runs: " + e.Message);
                return true;
            }
        }
    }

    // ---------------------------------------------------------------------- the gate

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.HasFreeOverheadSlot), MethodType.Getter)]
    internal static class PlayerData_HasFreeOverheadSlot_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (HeadProbe.ForceNoFreeSlot)
            {
                __result = false;
                return false;
            }
            if (!HeadReroute.Active)
            {
                return true;
            }
            Inventory inv = Cargo.Inv;
            __result = inv != null && inv.Data.Inventory.Count < inv.Data.InventorySize;
            return false;
        }
    }

    // ---------------------------------------------------------------------- the lookup

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.TryGetOverheadItem))]
    internal static class PlayerData_TryGetOverheadItem_Patch
    {
        private static bool Prefix(Predicate<Item> match, ref Item item, ref bool __result)
        {
            Item o = HeadProbe.LookupOverride;
            if (o != null && match != null && match(o))
            {
                item = o;
                __result = true;
                return false;
            }
            return true;
        }

        // The head first (the original), then the cart, top item first, as the head list is scanned.
        private static void Postfix(Predicate<Item> match, ref Item item, ref bool __result)
        {
            if (__result || match == null || !HeadReroute.Active)
            {
                return;
            }
            try
            {
                List<Item> list = Cargo.Inv.Data.Inventory;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    Item it = list[i];
                    if (it != null && !it.IsEmpty && match(it))
                    {
                        item = it;
                        __result = true;
                        HeadReroute.RememberMatch(it);
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: cart lookup failed: " + e.Message);
            }
        }
    }

    // ---------------------------------------------------------------------- the mutations

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.RemoveOverheadItem), typeof(Item))]
    internal static class PlayerData_RemoveOverheadItem_Patch
    {
        private static bool Prefix(PlayerData __instance, Item item)
        {
            try
            {
                if (!Cargo.Attached || item == null)
                {
                    return true;
                }
                // An item that is on the head is the head's business, even when the cart already holds an object
                // with its GUID: the game's own InsertOverheadItemTo (used by the mod to move hand items onto the
                // cart) adds to the cart first and then removes from the head, and taking the cart copy here instead
                // left an emptied entry on the head (seen in game 2026-09-28).
                foreach (Item h in __instance.OverheadItems)
                {
                    if (h != null && (ReferenceEquals(h, item) || (h.UniqueId.Guid == item.UniqueId.Guid && h.id == item.id)))
                    {
                        return true;
                    }
                }
                Item stored = HeadReroute.FindOnCart(item);
                if (stored == null)
                {
                    return true;
                }
                HeadReroute.RemoveFromCart(stored);
                Plugin.Log.LogInfo($"Cargo: {item.id} taken off the cart by the game ({Cargo.Count} left).");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: cart removal failed, the game's own path runs: " + e.Message);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.InsertOverheadItemTo), typeof(WgoData), typeof(Item))]
    internal static class PlayerData_InsertOverheadItemTo_Patch
    {
        private static bool Prefix(WgoData wgoData, Item item)
        {
            try
            {
                if (!Cargo.Attached || wgoData == null || item == null)
                {
                    return true;
                }
                Item stored = HeadReroute.FindOnCart(item);
                if (stored == null)
                {
                    return true; // from the head (the mod's own load, or a zombie): the game's path
                }
                if (wgoData == Cargo.DonkeyData)
                {
                    return false; // already there
                }
                if (!wgoData.Inventory.CanAddItemToInventory(stored))
                {
                    Plugin.Log.LogInfo($"Cargo: {wgoData.id} cannot take {item.id}; it stays on the cart.");
                    return false;
                }
                // Cart first, target second: the target's add splits the object, and a removal after that would be
                // silent (probe E2).
                string id = stored.id;
                HeadReroute.RemoveFromCart(stored);
                wgoData.Inventory.AddItemToInventory(stored);
                GlobalEventsSystem.FireTrigger(GlobalEventsSystem.Event.Type.PlayerInsertOverheadToWgoAnItem, id);
                Plugin.Log.LogInfo($"Cargo: {id} put into {wgoData.id} from the cart ({Cargo.Count} left).");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: insert from the cart failed, the game's own path runs: " + e.Message);
                return true;
            }
        }
    }

    // Script executions (InsertOvrhdItem and friends) take "the held item" from here; with one head item that is the
    // one their condition matched. With two different cart items it must be the matched one, which the lookup
    // remembered a moment earlier in the same interaction.
    [HarmonyPatch(typeof(LazyExpressionEvaluationScope), nameof(LazyExpressionEvaluationScope.GetOverheadItemForMutation))]
    internal static class LazyExpressionEvaluationScope_GetOverheadItemForMutation_Patch
    {
        private static void Postfix(ref Item __result)
        {
            if (HeadProbe.MutationOverride != null)
            {
                __result = HeadProbe.MutationOverride;
                return;
            }
            if (__result != null || !HeadReroute.Active)
            {
                return;
            }
            Item m = HeadReroute.LastCartMatch;
            if (m == null)
            {
                List<Item> list = Cargo.Inv.Data.Inventory;
                m = list.Count > 0 ? list[list.Count - 1] : null;
            }
            __result = m;
        }
    }
}

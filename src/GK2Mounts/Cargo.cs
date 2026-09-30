using System;
using System.Collections.Generic;
using UnityEngine;

using GK2Mounts.Api;

namespace GK2Mounts
{
    // Cargo on the cart. The cart's cargo is the mounted donkey's own inventory, exactly as in the game's delivery
    // routine (Flow_AddItem puts the body into npc_donkey's 2-slot inventory). Loading and unloading follow the
    // game's town-pallet and conveyor-cell handlers: the keeper's overhead item goes in with
    // PlayerData.InsertOverheadItemTo, and comes back out with the inventory's remove call + TryAddOverheadItemNoReplace,
    // or is dropped beside the keeper when his hands are full.
    //
    // Drawing: the game only ever draws a corpse on the cart (its prefab's drawer switches one shroud mesh on while
    // a body_corpse is aboard) and arranges nothing; every pile in the game is hand-made art switched by item count.
    // So the mod draws the cargo itself with copies of the keeper's overhead mesh atom (one mesh per big item) and
    // places them with a size-based packer (RefreshViews): every item's box is measured, the bigger footprint goes to
    // the bottom or the far side, two items lie side by side across the bed when they fit and stack otherwise, at
    // the density of the game's own timber stockpile (touching, one width apart, one height up). Corrections come
    // from cargo-layout.cfg (CargoLayout), keyed by size class, so the 27 items reduce to seven classes.
    //
    // Save safety: the donkey is a temp object, so its inventory never reaches a save. Everything is returned to the
    // keeper (hands first, then dropped at his feet) whenever the ride ends (dismount, cleanup, sleep) and right
    // before the game writes a save (prefix on SaveSystem.Save). Across a door with StayMountedThroughDoors the items
    // are kept in memory and put back on the re-summoned cart.
    internal static class Cargo
    {
        private const string CorpseId = "body_corpse";
        internal const string ContainerName = "cart_cargo";

        private static Wgo donkey;
        private static Inventory inv;
        private static readonly List<Item> mirror = new List<Item>(); // what is on the cart, kept across scene changes
        private static Transform container;
        private static float bedWidth = 0.75f;  // measured from the empty cart body (its size across, local z)
        private static float bedLength = 0.9f;  // measured from the empty cart body (its size along, local x)
        private static readonly List<DropViewAtomMesh> views = new List<DropViewAtomMesh>();
        private static bool subscribed;
        private static bool unloading;
        internal static bool Unloading => unloading;

        // What the packer placed last, for the panel: item id and whether it sits on top of another item.
        public struct PlacedItem
        {
            public string Id;
            public bool OnTop;
            public Vector3 Box;
        }

        public static readonly List<PlacedItem> Placed = new List<PlacedItem>();
        public static string LastLayoutNote { get; private set; } = "";

        public static int Count => mirror.Count;
        public static bool Attached => inv != null;
        internal static Inventory Inv => inv;
        internal static WgoData DonkeyData => donkey != null ? donkey.Data : null;

        // Re-read the cart inventory and redraw, for changes the inventory did not announce (an entry emptied by a
        // split elsewhere is removed silently, see Round 9 of the findings) and for probes that bypass the events.
        internal static void Resync(string why)
        {
            if (inv == null)
            {
                return;
            }
            SyncMirror();
            Mount.OnCargoChanged();
            Plugin.Log.LogInfo($"Cargo: resynced ({why}); {mirror.Count} item(s) on the cart.");
        }

        // Called once the cart parts are prepared (cart style only). Items carried over from the previous view go
        // back onto the new inventory.
        public static void Attach(Wgo d)
        {
            Detach();
            if (d == null || d.Data == null)
            {
                return;
            }
            donkey = d;
            inv = d.Data.Inventory;
            EnsureContainer();
            if (mirror.Count > 0)
            {
                var carried = new List<Item>(mirror);
                mirror.Clear();
                int back = 0;
                foreach (Item it in carried)
                {
                    if (it == null || it.IsEmpty)
                    {
                        continue;
                    }
                    if (inv.AddItemToInventory(it))
                    {
                        back++;
                    }
                    else
                    {
                        DropAtKeeper(MainGame.PlayerData, it);
                    }
                }
                Plugin.Log.LogInfo($"Cargo: {back} of {carried.Count} item(s) put back on the re-summoned cart.");
            }
            inv.OnItemsAdd += OnChanged;
            inv.OnItemsRemove += OnChanged;
            subscribed = true;
            SyncMirror();
            LoadFromHead("mount");
            RefreshViews();
        }

        // There is no head storage while riding the cart: whatever allowed items the keeper holds go onto the cart
        // while there is room (at mount, and again after a save put them back in his hands). The game's own insert
        // path is used: add to the cart inventory, then remove from the head by GUID.
        public static void LoadFromHead(string reason)
        {
            PlayerData pd = MainGame.PlayerData;
            if (inv == null || donkey == null || pd == null || !pd.HasOverheadItem || !MountRules.Allowed(MountFeature.Cargo))
            {
                return;
            }
            // Repair: an emptied entry (count 0) on the head draws like the item but can neither be used nor dropped
            // (the game skips empty items everywhere). The first build of the reroute could leave one behind.
            int purged = 0;
            foreach (Item it in new List<Item>(pd.OverheadItems))
            {
                if (it != null && it.IsEmpty)
                {
                    pd.RemoveOverheadItem(it);
                    purged++;
                }
            }
            if (purged > 0)
            {
                Plugin.Log.LogWarning($"Cargo: removed {purged} emptied item entr{(purged == 1 ? "y" : "ies")} from the head ({reason}).");
            }
            int moved = 0;
            foreach (Item it in new List<Item>(pd.OverheadItems))
            {
                if (Allowed(it) && inv.CanAddItemToInventory(it))
                {
                    pd.InsertOverheadItemTo(donkey.Data, it);
                    moved++;
                }
            }
            if (moved > 0)
            {
                if (reason == "mount")
                {
                    MountSounds.CargoLanded(); // not when the cargo goes back on after a save (waking up)
                }
                Plugin.Log.LogInfo($"Cargo: {moved} item(s) moved from the hands onto the cart ({reason}); cart {inv.Data.Inventory.Count}/{inv.Data.InventorySize}.");
            }
        }

        // The view is gone (dismount or scene change); the mirror is kept, Unload clears it.
        public static void Detach()
        {
            if (subscribed && inv != null)
            {
                inv.OnItemsAdd -= OnChanged;
                inv.OnItemsRemove -= OnChanged;
            }
            subscribed = false;
            views.Clear();
            Placed.Clear();
            container = null;
            donkey = null;
            inv = null;
        }

        private static void OnChanged(List<Item> _)
        {
            SyncMirror();
            if (!unloading)
            {
                Mount.OnCargoChanged(); // refreshes the views and re-collects the lifted cart parts
            }
        }

        private static void SyncMirror()
        {
            mirror.Clear();
            if (inv != null)
            {
                mirror.AddRange(inv.Data.Inventory);
            }
            Announce();
        }

        // What the API events last reported as on the cart (item GUID -> id). Diffing against it, rather than raising
        // at each call site, reports every way items come and go exactly once, and nothing for the core's own
        // bookkeeping: the cart summoned again after a door holds the same GUIDs, and around a save the announcing is
        // held (SuppressAnnounce) until the cargo is back.
        private static readonly Dictionary<string, string> announced = new Dictionary<string, string>();
        internal static bool SuppressAnnounce;

        internal static void Announce()
        {
            if (SuppressAnnounce)
            {
                return;
            }
            var now = new Dictionary<string, string>();
            foreach (Item it in inv != null ? inv.Data.Inventory : mirror)
            {
                if (it != null && !it.IsEmpty && it.UniqueId != null)
                {
                    now[it.UniqueId.ToString()] = it.id;
                }
            }
            foreach (var kv in new List<KeyValuePair<string, string>>(announced))
            {
                if (!now.ContainsKey(kv.Key))
                {
                    announced.Remove(kv.Key);
                    Mounts.RaiseCargoUnloaded(kv.Value);
                }
            }
            foreach (var kv in now)
            {
                if (!announced.ContainsKey(kv.Key))
                {
                    announced[kv.Key] = kv.Value;
                    Mounts.RaiseCargoLoaded(kv.Value);
                }
            }
        }

        // Sets the top cart item down on the ground in front of the keeper: what the game's interact press does with
        // a held item when there is nothing to interact with (PlayerInputHandler.UpdateInput), one item per press.
        // Called from that press while riding the cart (PlayerInputHandler_UpdateInput_Patch). Silent when the cart
        // is empty, as the game is with empty hands.
        public static void SetDownLast(PlayerData pd)
        {
            if (inv == null || donkey == null || pd == null)
            {
                return;
            }
            try
            {
                List<Item> list = inv.Data.Inventory;
                if (list.Count == 0)
                {
                    return;
                }
                Item last = list[list.Count - 1];
                if (!inv.RemoveItemFromInventoryByUID(last))
                {
                    Plugin.Log.LogWarning("Cargo: could not take the item off the cart.");
                    Resync("take failed");
                    return;
                }
                DropAtKeeper(pd, last);
                Plugin.Log.LogInfo($"Cargo: set {last.id} down in front of the keeper; cart {list.Count}/{inv.Data.InventorySize}.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: setting the item down failed: " + e);
            }
        }
        internal static bool Allowed(Item item)
        {
            return item != null && !item.IsEmpty && item.Definition != null && !item.Definition.itemGroupIds.Contains("zombie");
        }

        // Everything back to the keeper: hands while they are free, the rest dropped at his feet (the game's own
        // drop position in front of him). Used at every ride end and before a save.
        public static void Unload(string reason) => Unload(reason, toHands: true);

        // toHands false: straight onto the ground (used while still riding, when the hands must stay empty); the
        // hands are then only the fallback for an item the game refuses to drop, so nothing is lost.
        public static void Unload(string reason, bool toHands)
        {
            if (CargoSheet.Running)
            {
                // The sheet's items are test copies made in memory; they must not be set down in the world.
                CargoSheet.Abort("unload on " + reason);
            }
            List<Item> items = inv != null ? new List<Item>(inv.Data.Inventory) : new List<Item>(mirror);
            if (items.Count == 0)
            {
                mirror.Clear();
                Announce();
                return;
            }
            if (Plugin.Quitting)
            {
                // The game is closing: its world and sound system are being torn down, and closing never saves (the
                // only save is on waking, EnergySystem.StopSleeping, which hands the cargo back first). Handing items
                // back now could only fail; the last save already has everything where it was.
                Plugin.Log.LogInfo($"Cargo: the game is closing with {items.Count} item(s) on the cart; not handed back (closing does not save, the last save is unaffected).");
                mirror.Clear();
                return;
            }
            PlayerData pd = MainGame.PlayerData;
            int hands = 0, dropped = 0, lost = 0;
            unloading = true;
            try
            {
                foreach (Item it in items)
                {
                    if (it == null || it.IsEmpty)
                    {
                        continue;
                    }
                    // One item at a time, so a failure with one never loses the others.
                    try
                    {
                        if (inv != null)
                        {
                            inv.RemoveItemFromInventoryByUID(it);
                        }
                        if (pd == null)
                        {
                            lost++;
                            Plugin.Log.LogWarning($"Cargo: {it.id} LOST on unload ({reason}): no player data.");
                        }
                        else if (toHands && pd.TryAddOverheadItemNoReplace(it))
                        {
                            hands++;
                        }
                        else if (DropAtKeeper(pd, it))
                        {
                            dropped++;
                        }
                        else if (!toHands && pd.TryAddOverheadItemNoReplace(it))
                        {
                            hands++;
                        }
                        else
                        {
                            lost++;
                        }
                    }
                    catch (Exception e)
                    {
                        // The game adds the item to the hands and then plays its pickup sound; a failure after the add
                        // (a torn-down sound system) leaves the item in hand. Look before calling it lost, and try the
                        // ground when it is not there.
                        if (pd != null && InHands(pd, it))
                        {
                            hands++;
                            Plugin.Log.LogWarning($"Cargo: {it.id} handed over on unload ({reason}), but the game's pickup side effects failed: {e.Message}");
                            continue;
                        }
                        try
                        {
                            if (pd != null && DropAtKeeper(pd, it))
                            {
                                dropped++;
                                Plugin.Log.LogWarning($"Cargo: {it.id} set down on unload ({reason}) after handing it over failed: {e.Message}");
                                continue;
                            }
                        }
                        catch
                        {
                        }
                        lost++;
                        Plugin.Log.LogWarning($"Cargo: {it.id} LOST on unload ({reason}): {e}");
                    }
                }
            }
            finally
            {
                unloading = false;
            }
            mirror.Clear();
            Announce();
            if (inv != null)
            {
                Mount.OnCargoChanged();
            }
            Plugin.Log.LogInfo($"Cargo: unloaded on {reason}: {hands} in hand, {dropped} dropped{(lost > 0 ? $", {lost} LOST (see warnings)" : "")}.");
        }

        // Sets whatever the keeper holds down in front of him, the way the game's own drop does it (drop, then take it
        // off the head), for the empty-hands rule while riding. An item the game refuses to drop stays in hand.
        internal static int SetDownHeld(PlayerData pd)
        {
            if (pd == null || !pd.HasOverheadItem)
            {
                return 0;
            }
            int n = 0;
            foreach (Item it in new List<Item>(pd.OverheadItems))
            {
                if (it == null || it.IsEmpty || refusedDrops.Contains(it.UniqueId.ToString()))
                {
                    continue; // called every frame while riding: an item the game refused is tried once, not 60 times a second
                }
                Vector3 pos;
                if (!SpecialPhysicsCastUtils.GetPlayerDropPosition(pd.position.Value, pd.Direction, out pos))
                {
                    pos = pd.position.Value;
                }
                if (MainGame.Instance.dropSystem.DropItem(it, pd.currentGameSceneId, pos))
                {
                    pd.RemoveOverheadItem(it);
                    n++;
                }
                else
                {
                    refusedDrops.Add(it.UniqueId.ToString());
                    Plugin.Log.LogWarning($"Mount: the game refused to set {it.id} down; it stays in the keeper's hands.");
                }
            }
            return n;
        }

        private static readonly HashSet<string> refusedDrops = new HashSet<string>();

        private static bool InHands(PlayerData pd, Item item)
        {
            foreach (Item h in pd.OverheadItems)
            {
                if (h != null && h.UniqueId == item.UniqueId)
                {
                    return true;
                }
            }
            return false;
        }

        // Sets an item down in front of the keeper with the game's drop routine. False when the game refused the
        // drop (it refuses fuel and tech points, and an empty scene id); the caller then keeps the item somewhere
        // else rather than letting it vanish.
        internal static bool DropAtKeeper(PlayerData pd, Item item)
        {
            if (pd == null || item == null || item.IsEmpty)
            {
                return false;
            }
            Vector3 pos;
            if (!SpecialPhysicsCastUtils.GetPlayerDropPosition(pd.position.Value, pd.Direction, out pos))
            {
                pos = pd.position.Value;
            }
            if (MainGame.Instance.dropSystem.DropItem(item, pd.currentGameSceneId, pos))
            {
                return true;
            }
            // The head as the last resort: an item in view beats an item gone. The game's own path, not the
            // reroute (which may be what called here with a full cart), so the flag sends it to the head.
            if (!unloading)
            {
                HeadReroute.GateAnswersForHead = true;
                try
                {
                    pd.AddOverheadItem(item);
                }
                finally
                {
                    HeadReroute.GateAnswersForHead = false;
                }
                Plugin.Log.LogWarning($"Cargo: the game refused to drop {item.id} (scene '{pd.currentGameSceneId}'); it went to the keeper's hands.");
                return true;
            }
            Plugin.Log.LogWarning($"Cargo: the game refused to drop {item.id} (scene '{pd.currentGameSceneId}'); it is LOST.");
            return false;
        }

        // ------------------------------------------------------------------ views and the packer

        private class Drawn
        {
            public Item Item;
            public DropViewAtomMesh View;
            public Bounds Box;      // in the view's own space: size, bottom (min.y) and footprint centre
            public int LoadOrder;
        }

        // One mesh copy per cargo item, placed by the size-based packer. Called on every inventory change and when
        // a setting or a layout entry moves.
        public static void RefreshViews()
        {
            if (inv == null || donkey == null || !donkey)
            {
                return;
            }
            try
            {
                EnsureContainer();
                if (container == null)
                {
                    return;
                }
                StyleSettings s = Mount.S;
                bool modDrawsCorpse = s.CargoDrawCorpse.Value;

                // Items the mod draws. Without CargoDrawCorpse the game's shroud stands for the first corpse and the
                // other items ride on top of it.
                var drawn = new List<Drawn>();
                bool gameCorpse = false;
                for (int k = 0; k < inv.Data.Inventory.Count; k++)
                {
                    Item it = inv.Data.Inventory[k];
                    if (it == null || it.IsEmpty || it.Definition == null)
                    {
                        continue;
                    }
                    if (it.id == CorpseId && !gameCorpse && !modDrawsCorpse)
                    {
                        gameCorpse = true;
                        continue;
                    }
                    drawn.Add(new Drawn { Item = it, LoadOrder = k });
                }
                SetGameLoadedBody(!modDrawsCorpse);

                // Views: one per drawn item, activated with the item's mesh, then measured.
                for (int i = 0; i < drawn.Count; i++)
                {
                    DropViewAtomMesh view = i < views.Count ? views[i] : null;
                    if (view == null)
                    {
                        view = MakeView(i);
                        if (view == null)
                        {
                            return;
                        }
                        if (i < views.Count)
                        {
                            views[i] = view;
                        }
                        else
                        {
                            views.Add(view);
                        }
                    }
                    view.gameObject.SetActive(true);
                    string icon = ResolveIcon(drawn[i].Item.Definition);
                    if (view.IconId != icon)
                    {
                        view.Activate(icon);
                        view.SetInteractionState(isUnderInteraction: false);
                        foreach (Collider c in view.GetComponentsInChildren<Collider>(includeInactive: true))
                        {
                            c.enabled = false;
                        }
                    }
                    drawn[i].View = view;
                    if (!MeasureMeshes(view.transform, view.transform, out drawn[i].Box))
                    {
                        drawn[i].Box = new Bounds(Vector3.zero, new Vector3(0.5f, 0.4f, 0.5f));
                    }
                }
                for (int j = drawn.Count; j < views.Count; j++)
                {
                    if (views[j] != null)
                    {
                        views[j].Deactivate();
                        views[j].gameObject.SetActive(false);
                    }
                }

                Pack(drawn, s, gameCorpse ? LoadedBodyExtra() : 0f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: refreshing the cart views failed: " + e);
            }
        }

        // The packer. Container space: origin at the measured top of the empty cart body, x along the cart, z across
        // (+z away from the camera), y up. Every item: bottom on the floor (bed top + CargoBedDepth), footprint
        // centre at its place. Order independent: the larger footprint is placed first (bottom / far side), ties by
        // load order. Two items lie side by side across the bed, touching within CargoSideGap and centred as a pair,
        // when their widths fit within the bed plus CargoOverhang; otherwise the second stacks on the first, sunk by
        // CargoNestle. A third or later item stacks on the previous one. Layout-file corrections apply per size class
        // (item entry: base or top role) and per pair (mode override and a shift of the second item).
        private static void Pack(List<Drawn> drawn, StyleSettings s, float floorExtra)
        {
            Placed.Clear();
            if (drawn.Count == 0)
            {
                LastLayoutNote = "empty";
                return;
            }
            drawn.Sort((a, b) =>
            {
                float areaA = a.Box.size.x * a.Box.size.z, areaB = b.Box.size.x * b.Box.size.z;
                int c = areaB.CompareTo(areaA);
                return c != 0 ? c : a.LoadOrder.CompareTo(b.LoadOrder);
            });
            float floor = s.CargoBedDepth.Value + floorExtra;
            var note = new System.Text.StringBuilder();

            // Footprint centres (x, z) and bottoms (y) each item is placed at, before pivot correction.
            var target = new Vector3[drawn.Count];
            var onTop = new bool[drawn.Count];
            Drawn first = drawn[0];
            CargoLayout.ItemEntry e0 = CargoLayout.ForItem(first.Item.id);
            target[0] = new Vector3(0f, floor, 0f) + (e0 != null ? e0.Base : Vector3.zero);

            for (int i = 1; i < drawn.Count; i++)
            {
                Drawn prev = drawn[i - 1];
                Drawn cur = drawn[i];
                CargoLayout.ItemEntry e = CargoLayout.ForItem(cur.Item.id);
                CargoLayout.PairEntry pair = i == 1 ? CargoLayout.ForPair(first.Item.id, cur.Item.id) : null;
                string mode = pair != null ? pair.Mode : "auto";
                float gap = s.CargoSideGap.Value, over = s.CargoOverhang.Value;
                float w0 = first.Box.size.z, w1 = cur.Box.size.z; // across the bed
                float l0 = first.Box.size.x, l1 = cur.Box.size.x; // along the cart
                // Two ways to lie next to each other: across the bed (the game's timber pile density; the default
                // when the widths fit) or end to end along the cart (when the lengths fit but the widths do not, e.g.
                // two crates); otherwise stacked. A pair entry can force any of the three.
                bool side = i == 1 && (mode == "side" || (mode == "auto" && w0 + gap + w1 <= bedWidth + over));
                bool along = i == 1 && !side && (mode == "along" || (mode == "auto" && l0 + gap + l1 <= bedLength + over));
                if (side)
                {
                    float total = w0 + gap + w1;
                    target[0].z = total * 0.5f - w0 * 0.5f + (e0 != null ? e0.Base.z : 0f);
                    target[i] = new Vector3(0f, floor, -total * 0.5f + w1 * 0.5f) + (e != null ? e.Base : Vector3.zero);
                    note.Append($"{first.Item.id} | {cur.Item.id} side by side across ({w0:0.00}+{w1:0.00} on {bedWidth:0.00})");
                }
                else if (along)
                {
                    float total = l0 + gap + l1;
                    target[0].x = total * 0.5f - l0 * 0.5f + (e0 != null ? e0.Base.x : 0f);
                    target[i] = new Vector3(-total * 0.5f + l1 * 0.5f, floor, 0f) + (e != null ? e.Base : Vector3.zero);
                    note.Append($"{first.Item.id} | {cur.Item.id} end to end along ({l0:0.00}+{l1:0.00} on {bedLength:0.00})");
                }
                else
                {
                    float prevTop = target[i - 1].y + prev.Box.size.y;
                    target[i] = new Vector3(target[i - 1].x, prevTop - s.CargoNestle.Value, target[i - 1].z) + (e != null ? e.Top : Vector3.zero);
                    onTop[i] = true;
                    note.Append($"{cur.Item.id} on top of {prev.Item.id}{(mode != "auto" ? " (" + mode + ")" : "")}");
                }
                if (pair != null)
                {
                    target[i] += pair.Second;
                }
            }
            if (drawn.Count == 1)
            {
                note.Append(first.Item.id + " alone");
            }

            for (int i = 0; i < drawn.Count; i++)
            {
                Bounds b = drawn[i].Box;
                drawn[i].View.transform.localPosition = new Vector3(target[i].x - b.center.x, target[i].y - b.min.y, target[i].z - b.center.z);
                Placed.Add(new PlacedItem { Id = drawn[i].Item.id, OnTop = onTop[i], Box = b.size });
            }
            LastLayoutNote = note.ToString();
        }

        // ------------------------------------------------------------------ meshes

        private static HashSet<string> elementNames;
        private static readonly Dictionary<string, string> iconFallback = new Dictionary<string, string>
        {
            { "i_marble", "i_stone" }, // marble has no overhead mesh in the game; the slab is the nearest shape
        };

        // The overhead element name to activate for an item: its icon id when an element of that name exists; else
        // an element whose name is a prefix of the icon id (the game's box_engineering_mechanism element is named
        // i_box_engineering_mec, truncated); else a stand-in from the table; else the icon id (the game then shows
        // its placeholder and logs an error, which is the honest outcome for an unknown future item).
        internal static string ResolveIcon(ItemDef def)
        {
            if (def == null)
            {
                return "";
            }
            HashSet<string> names = ElementNames();
            if (names.Contains(def.iconId))
            {
                return def.iconId;
            }
            foreach (string n in names)
            {
                if (n.Length >= 8 && def.iconId.StartsWith(n, StringComparison.Ordinal))
                {
                    return n;
                }
            }
            if (iconFallback.TryGetValue(def.iconId, out string alt) && names.Contains(alt))
            {
                return alt;
            }
            return def.iconId;
        }

        internal static Transform FindOverheadElement(ItemDef def)
        {
            DropViewAtomMesh src = FindSourceAtom();
            if (src == null || def == null)
            {
                return null;
            }
            string icon = ResolveIcon(def);
            foreach (Transform t in src.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t.name == icon && t.GetComponent<DropViewAtomMeshElement>() != null)
                {
                    return t;
                }
            }
            return null;
        }

        private static HashSet<string> ElementNames()
        {
            if (elementNames != null && elementNames.Count > 0)
            {
                return elementNames;
            }
            elementNames = new HashSet<string>(StringComparer.Ordinal);
            DropViewAtomMesh src = FindSourceAtom();
            if (src != null)
            {
                foreach (DropViewAtomMeshElement el in src.GetComponentsInChildren<DropViewAtomMeshElement>(includeInactive: true))
                {
                    elementNames.Add(el.name);
                }
            }
            return elementNames;
        }

        // Union of the mesh bounds under root, in the given transform's space, ignoring the root's own local
        // position so the result describes the model itself. Mesh bounds are valid on inactive objects; renderer
        // bounds are not, which is why meshes are used. Shadow planes do not count.
        internal static bool MeasureMeshes(Transform root, Transform space, out Bounds bounds, bool includeInactive = false)
        {
            bounds = default;
            bool any = false;
            Vector3 rootPos = root.localPosition;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(includeInactive))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || IsShadowMesh(mf.gameObject.name))
                {
                    continue;
                }
                Bounds b = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 p = space.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (space != root)
                    {
                        p -= rootPos;
                    }
                    if (!any)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }
            return any;
        }

        // The game's own naming rule for shadow meshes (Object3DSubMesh.MeshNameFlags): a flat plane under the object
        // that must not count towards its size.
        internal static bool IsShadowMesh(string name)
        {
            return name.EndsWith("_sh") || name.EndsWith("-sh") || name.EndsWith("_sh2") || name.EndsWith("-sh2");
        }

        // allowGame = true: the prefab's own drawer decides (loaded body while a corpse is aboard); false: the empty
        // body stays on and the game's loaded body off, because the mod draws the corpses itself. The drawer only
        // re-evaluates on inventory changes, and this runs after each of them, so the override holds.
        private static void SetGameLoadedBody(bool allowGame)
        {
            if (donkey == null)
            {
                return;
            }
            if (allowGame)
            {
                foreach (ConditionalDrawer cd in donkey.GetComponentsInChildren<ConditionalDrawer>(includeInactive: true))
                {
                    cd.UpdateVisualsByConditions();
                }
                return;
            }
            foreach (Transform t in donkey.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t.name == "donkey_cart_body_sway" && t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(false);
                }
                else if (t.name == "donkey_cart_empty_sway" && !t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                }
            }
        }

        // How much higher the loaded cart body (the corpse shroud the game turns on) reaches than the empty bed top,
        // so items put on the cart with a game-drawn corpse ride on top of the shroud. 0 when it is not active.
        private static float LoadedBodyExtra()
        {
            if (donkey == null || container == null)
            {
                return 0f;
            }
            foreach (Transform t in donkey.GetComponentsInChildren<Transform>(includeInactive: false))
            {
                if (t.name != "donkey_cart_body_sway")
                {
                    continue;
                }
                Renderer best = null;
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(includeInactive: false))
                {
                    if (best == null || r.bounds.max.y > best.bounds.max.y)
                    {
                        best = r;
                    }
                }
                if (best == null)
                {
                    return 0f;
                }
                float top = container.parent.InverseTransformPoint(new Vector3(best.bounds.center.x, best.bounds.max.y, best.bounds.center.z)).y;
                return Mathf.Max(0f, top - container.localPosition.y);
            }
            return 0f;
        }

        // The container sits under the cart's parent ("Base") at the top centre of the empty cart body, measured
        // from its renderer; it is named so Mount lifts it with the other cart parts on slopes.
        private static void EnsureContainer()
        {
            if (container != null && container)
            {
                return;
            }
            container = null;
            Transform emptySway = null, emptyBody = null;
            foreach (Transform t in donkey.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t.name == "donkey_cart_empty_sway")
                {
                    emptySway = t;
                }
                else if (t.name == "donkey_cart_empty")
                {
                    emptyBody = t;
                }
                else if (t.name == ContainerName)
                {
                    container = t;
                }
            }
            if (container != null)
            {
                return;
            }
            if (emptySway == null || emptySway.parent == null)
            {
                Plugin.Log.LogWarning("Cargo: the cart body was not found; cargo views off.");
                return;
            }
            Transform parent = emptySway.parent;
            var go = new GameObject(ContainerName);
            container = go.transform;
            container.SetParent(parent, worldPositionStays: false);
            container.localRotation = Quaternion.identity;
            container.localScale = Vector3.one;
            Renderer r = (emptyBody ?? emptySway).GetComponentInChildren<Renderer>(includeInactive: true);
            if (r != null)
            {
                Bounds b = r.bounds;
                Vector3 top = parent.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
                container.localPosition = top;
                bedWidth = Mathf.Max(0.3f, b.size.z / Mathf.Max(0.01f, Mathf.Abs(parent.lossyScale.z)));
                bedLength = Mathf.Max(0.3f, b.size.x / Mathf.Max(0.01f, Mathf.Abs(parent.lossyScale.x)));
                Plugin.Log.LogInfo($"Cargo: bed top at local {top}, bed width {bedWidth:0.00}, length {bedLength:0.00} (cart body bounds centre {b.center}, size {b.size}).");
            }
            else
            {
                container.localPosition = emptySway.localPosition + Vector3.up * 0.3f;
                Plugin.Log.LogInfo("Cargo: no renderer on the cart body, bed guessed 0.3 above it.");
            }
            go.layer = (emptyBody ?? emptySway).gameObject.layer;
        }

        private static DropViewAtomMesh MakeView(int index)
        {
            DropViewAtomMesh source = FindSourceAtom();
            if (source == null)
            {
                Plugin.Log.LogWarning("Cargo: the keeper's overhead mesh atom was not found; cargo views off.");
                return null;
            }
            DropViewAtomMesh view = UnityEngine.Object.Instantiate(source, container);
            view.name = $"cargo_{index}";
            view.transform.SetParent(container, worldPositionStays: false);
            view.transform.localRotation = Quaternion.identity;
            view.transform.localScale = Vector3.one;
            view.isOverhead = false;
            int layer = container.gameObject.layer;
            foreach (Transform t in view.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                t.gameObject.layer = layer;
            }
            foreach (Collider c in view.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                c.enabled = false;
            }
            return view;
        }

        internal static DropViewAtomMesh FindSourceAtom()
        {
            PlayerController pc = MainGame.PlayerController;
            if (pc == null)
            {
                return null;
            }
            DropViewAtomMesh first = null;
            foreach (DropViewAtomMesh atom in pc.GetComponentsInChildren<DropViewAtomMesh>(includeInactive: true))
            {
                if (atom.transform.parent != null && atom.transform.parent.name == "overhead")
                {
                    return atom;
                }
                if (first == null)
                {
                    first = atom;
                }
            }
            return first;
        }

        // For the panel and the geometry log.
        public static string Summary()
        {
            if (inv == null)
            {
                return "cargo: none";
            }
            var l = new List<string>();
            foreach (Item it in inv.Data.Inventory)
            {
                l.Add(it.id);
            }
            return $"cargo {inv.Data.Inventory.Count}/{inv.Data.InventorySize} [{string.Join(" ", l)}]; layout: {LastLayoutNote}; bed {bedLength:0.00} long × {bedWidth:0.00} across";
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GK2Mounts
{
    // Cargo on the cart (proof of concept). The cart's cargo is the mounted donkey's own inventory, exactly as in the
    // game's delivery routine (Flow_AddItem puts the body into npc_donkey's 2-slot inventory; the prefab's
    // ConditionalDrawer turns the loaded cart body with the corpse shroud on while a body_corpse is inside). Loading
    // and unloading follow the game's town-pallet and conveyor-cell handlers: the keeper's overhead item goes in with
    // PlayerData.InsertOverheadItemTo, and comes back out with Inventory.RemoveItemById + TryAddOverheadItemNoReplace,
    // or is dropped beside the keeper when his hands are full.
    //
    // The game only draws a corpse on the cart. Every other item is shown by a copy of the keeper's own overhead
    // mesh atom (the DropViewAtomMesh under PlayerView2D/.../overhead/item, which holds a mesh per big item), placed
    // on the cart bed, one copy per inventory slot; this is what AnimationComponentBase.EnsureExtraOverheadViews
    // does for a stacked overhead load. Zombies are refused here: they are drawn through a head-id path this proof
    // of concept does not cover.
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
        private static float bedWidth = 0.75f; // measured from the empty cart body (its size across, local z)
        private static readonly List<DropViewAtomMesh> views = new List<DropViewAtomMesh>();
        private static bool subscribed;
        private static bool unloading;

        public static int Count => mirror.Count;
        public static bool Attached => inv != null;

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
            RefreshViews();
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
        }

        // The cargo key: with something overhead, put it on the cart; with empty hands, take the last cargo item.
        public static void Toggle(PlayerController pc)
        {
            if (inv == null || donkey == null || pc?.PlayerData == null)
            {
                return;
            }
            PlayerData pd = pc.PlayerData;
            try
            {
                if (pd.HasOverheadItem)
                {
                    if (!pd.TryGetOverheadItem(i => Allowed(i) && inv.CanAddItemToInventory(i), out Item item))
                    {
                        bool full = inv.Data.Inventory.Count >= inv.Data.InventorySize;
                        Mount.Say(full ? "The cart is full." : "That can't go on the cart.");
                        return;
                    }
                    string id = item.id;
                    pd.InsertOverheadItemTo(donkey.Data, item);
                    Plugin.Log.LogInfo($"Cargo: loaded {id}; cart {inv.Data.Inventory.Count}/{inv.Data.InventorySize}.");
                    return;
                }
                List<Item> list = inv.Data.Inventory;
                if (list.Count == 0)
                {
                    Mount.Say("Nothing on the cart.");
                    return;
                }
                Item last = list[list.Count - 1];
                bool taken = inv.RemoveItemFromInventoryByUID(last);
                if (!taken)
                {
                    Plugin.Log.LogWarning("Cargo: could not take the item off the cart.");
                    return;
                }
                if (pd.TryAddOverheadItemNoReplace(last))
                {
                    Plugin.Log.LogInfo($"Cargo: took {last.id} in hand; cart {list.Count}/{inv.Data.InventorySize}.");
                }
                else
                {
                    DropAtKeeper(pd, last);
                    Plugin.Log.LogInfo($"Cargo: hands full, dropped {last.id} beside the keeper; cart {list.Count}/{inv.Data.InventorySize}.");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: load/unload failed: " + e);
            }
        }

        private static bool Allowed(Item item)
        {
            return item != null && !item.IsEmpty && item.Definition != null && !item.Definition.itemGroupIds.Contains("zombie");
        }

        // Everything back to the keeper: hands while they are free, the rest dropped at his feet (the game's own
        // drop position in front of him). Used at every ride end and before a save.
        public static void Unload(string reason)
        {
            List<Item> items = inv != null ? new List<Item>(inv.Data.Inventory) : new List<Item>(mirror);
            if (items.Count == 0)
            {
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
                    if (inv != null)
                    {
                        inv.RemoveItemFromInventoryByUID(it);
                    }
                    if (pd == null)
                    {
                        lost++;
                    }
                    else if (pd.TryAddOverheadItemNoReplace(it))
                    {
                        hands++;
                    }
                    else
                    {
                        DropAtKeeper(pd, it);
                        dropped++;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: unloading failed: " + e);
            }
            finally
            {
                unloading = false;
            }
            mirror.Clear();
            if (inv != null)
            {
                Mount.OnCargoChanged();
            }
            Plugin.Log.LogInfo($"Cargo: unloaded on {reason}: {hands} in hand, {dropped} dropped{(lost > 0 ? $", {lost} LOST (no player)" : "")}.");
        }

        private static void DropAtKeeper(PlayerData pd, Item item)
        {
            if (pd == null || item == null || item.IsEmpty)
            {
                return;
            }
            Vector3 pos;
            if (!SpecialPhysicsCastUtils.GetPlayerDropPosition(pd.position.Value, pd.Direction, out pos))
            {
                pos = pd.position.Value;
            }
            MainGame.Instance.dropSystem.DropItem(item, pd.currentGameSceneId, pos);
        }

        // ------------------------------------------------------------------ views

        // One mesh copy per cargo item that the game does not draw itself (everything but the corpse), on the cart
        // bed, spread along the cart. Called on every inventory change and when the placement settings move.
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
                // Items the mod draws, with the inventory slot each occupies. The game's drawer shows one shroud while
                // any corpse is aboard (its rule is "body_corpse count > 0"), so the first corpse is the game's and
                // keeps its slot (a log loaded after it takes slot 2); every further corpse is drawn by the mod with
                // the corpse mesh, which the auto-fit stacks on the shroud (two corpses are wider than the bed).
                // With CargoDrawCorpse the mod draws every corpse itself (same rule and sliders as any item) and keeps
                // the game's loaded body off; otherwise the game's shroud stands for the first corpse.
                StyleSettings s = Mount.S;
                bool modDrawsCorpse = s.CargoDrawCorpse.Value;
                var shown = new List<Item>();
                var slotOf = new List<int>();
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
                    shown.Add(it);
                    slotOf.Add(k);
                }
                SetGameLoadedBody(!modDrawsCorpse);
                int n = shown.Count;
                float floor = gameCorpse ? LoadedBodyExtra() : 0f; // items ride on the shroud while a corpse is aboard
                var placed = new List<Bounds>(); // footprints already laid out, container space, auto-fit only
                for (int i = 0; i < n; i++)
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
                    string icon = shown[i].Definition.iconId;
                    if (view.IconId != icon)
                    {
                        view.Activate(icon);
                        view.SetInteractionState(isUnderInteraction: false);
                        foreach (Collider c in view.GetComponentsInChildren<Collider>(includeInactive: true))
                        {
                            c.enabled = false;
                        }
                    }
                    // Items lie along the cart (local x, the log's long axis) in tunable slots: slot 1 for the first
                    // item, slot 2 for the second (Cargo1*/Cargo2*; local z is the width, +z away from the camera).
                    Vector3 slot = s.CargoSlot(slotOf[i]) + Vector3.up * floor;
                    if (!s.CargoAutoFit.Value || !MeasureMeshes(view.transform, view.transform, out Bounds b))
                    {
                        view.transform.localPosition = slot; // raw: the pivot goes to the slot point
                        continue;
                    }
                    // Auto-fit: the mesh's footprint centre goes to the slot point and its bottom onto the bed, whatever
                    // the model's pivot (every big item's pivot is at its bottom, but 0.02 to 0.19 off centre across).
                    // A second item is pushed away from the first when the two would overlap across the bed, and put on
                    // top of the first when the two are wider than the bed together (two crates: 1.0 on a 0.75 bed).
                    Vector3 pos = new Vector3(slot.x - b.center.x, slot.y - b.min.y, slot.z - b.center.z);
                    if (placed.Count > 0)
                    {
                        Bounds first = placed[0];
                        float across = b.size.z + first.size.z;
                        // Up to 0.12 of overhang over the rails is fine (corpse + log = 0.78 on the 0.75 bed); two
                        // crates (1.0) stack. When stacked, slot 2's values act as offsets from the first item.
                        if (across > bedWidth + 0.12f)
                        {
                            // Stacked: this slot's values minus the first slot's act as offsets from the first item;
                            // across is limited so the item stays on top of the one below.
                            Vector3 rel = s.CargoSlot(slotOf[i]) - s.CargoSlot(0);
                            float m = Mathf.Max(0f, (first.size.z - b.size.z) * 0.5f);
                            rel.z = Mathf.Clamp(rel.z, -m, m);
                            pos = new Vector3(first.center.x + rel.x - b.center.x, first.max.y + rel.y - b.min.y, first.center.z + rel.z - b.center.z);
                        }
                        // Side by side: the slot values rule, no pushing apart (it felt like a collider when tuning).
                    }
                    view.transform.localPosition = pos;
                    placed.Add(new Bounds(b.center + pos, b.size));
                }
                for (int j = n; j < views.Count; j++)
                {
                    if (views[j] != null)
                    {
                        views[j].Deactivate();
                        views[j].gameObject.SetActive(false);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: refreshing the cart views failed: " + e);
            }
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
                Plugin.Log.LogInfo($"Cargo: bed top at local {top}, bed width {bedWidth:0.00} (cart body bounds centre {b.center}, size {b.size}).");
            }
            else
            {
                container.localPosition = emptySway.localPosition + Vector3.up * 0.3f;
                Plugin.Log.LogInfo("Cargo: no renderer on the cart body, bed guessed 0.3 above it.");
            }
            go.layer = (emptyBody ?? emptySway).gameObject.layer;
        }

        // Union of the mesh bounds under root, in the given transform's space, ignoring the root's own local
        // position so the result describes the model itself. Mesh bounds are valid on inactive objects; renderer
        // bounds are not, which is why meshes are used.
        internal static bool MeasureMeshes(Transform root, Transform space, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            Vector3 rootPos = root.localPosition;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(includeInactive: false))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }
                Bounds b = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 p = space.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (space == root)
                    {
                        // already relative to root
                    }
                    else
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
        // so items put on the cart with a corpse ride on top of the shroud. 0 when it is not active.
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

        private static DropViewAtomMesh FindSourceAtom()
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

        // For the geometry log.
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
            return $"cargo {inv.Data.Inventory.Count}/{inv.Data.InventorySize} [{string.Join(" ", l)}], {views.Count} view(s), bed {(container != null ? container.localPosition.ToString() : "-")}";
        }
    }
}

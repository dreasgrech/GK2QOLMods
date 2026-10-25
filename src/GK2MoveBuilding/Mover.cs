using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2MoveBuilding
{
    // Private game members the mod reads or writes. Resolved once; any missing member throws in Verify().
    internal static class Access
    {
        public static readonly AccessTools.FieldRef<BuildManager, Wgo> CurrentBuildDesk =
            AccessTools.FieldRefAccess<BuildManager, Wgo>("currentBuildDesk");

        public static readonly AccessTools.FieldRef<BuildManager, List<Inventory>> AdditionalInventories =
            AccessTools.FieldRefAccess<BuildManager, List<Inventory>>("additionalInventories");

        public static readonly AccessTools.FieldRef<BuildController, BuildPointer> BuildPointerOf =
            AccessTools.FieldRefAccess<BuildController, BuildPointer>("buildPointer");

        public static readonly AccessTools.FieldRef<BuildController, bool> InputLocked =
            AccessTools.FieldRefAccess<BuildController, bool>("isBuildModeInputLocked");

        public static readonly AccessTools.FieldRef<WgoBuildPointer, Func<bool>> CanTakeResources =
            AccessTools.FieldRefAccess<WgoBuildPointer, Func<bool>>("canTakeResources");

        public static readonly AccessTools.FieldRef<BuildPointerObject, bool> ShownAsActive =
            AccessTools.FieldRefAccess<BuildPointerObject, bool>("shownAsActive");

        public static readonly AccessTools.FieldRef<IBuildRemovable> CurrentRemovingSelection =
            AccessTools.StaticFieldRefAccess<IBuildRemovable>(AccessTools.Field(typeof(RemovePointer), "currentRemovingSelection"));

        public static readonly Action<Wgo> RegisterInChunks =
            AccessTools.MethodDelegate<Action<Wgo>>(AccessTools.Method(typeof(Wgo), "TryRegisterInChunkManager"));

        public static readonly Action<Wgo> UnregisterFromChunks =
            AccessTools.MethodDelegate<Action<Wgo>>(AccessTools.Method(typeof(Wgo), "TryUnregisterInChunkManager"));

        public static readonly Action<UIBuildingWidgetData, string> SetWidgetName =
            AccessTools.MethodDelegate<Action<UIBuildingWidgetData, string>>(AccessTools.PropertySetter(typeof(UIBuildingWidgetData), "Name"));

        // What the view runs at its first spawn to register baked navigation points at the object's position.
        public static readonly Action<Wgo> RegisterGdPoints =
            AccessTools.MethodDelegate<Action<Wgo>>(AccessTools.Method(typeof(Wgo), "RegisterGDPointsFromBakedData"));

        // What the view runs at its first spawn to link workbench extensions by collider overlap (a coroutine).
        public static readonly Func<Wgo, bool, IEnumerator> LinkExtensions =
            AccessTools.MethodDelegate<Func<Wgo, bool, IEnumerator>>(AccessTools.Method(typeof(Wgo), "TryRegisterWorkbenchExtensionDelayed"));

        // The drawer's handler for a parent workbench's craft status; needed to unsubscribe it from old parents.
        public static readonly System.Reflection.MethodInfo DrawerParentCraftHandler =
            AccessTools.Method(typeof(ConditionalDrawer), "OnParentCraftStatusChanged");

        // Grid cell state bits (the struct only exposes AddState).
        public static readonly AccessTools.StructFieldRef<BuildCellData, int> CellState =
            AccessTools.StructFieldRefAccess<BuildCellData, int>("state");

        // Touching the type runs the static initializer above; FieldRefAccess/MethodDelegate throw on a missing member.
        public static void Verify()
        {
            if (CurrentBuildDesk == null || AdditionalInventories == null || BuildPointerOf == null || InputLocked == null ||
                CanTakeResources == null || ShownAsActive == null || CurrentRemovingSelection == null ||
                RegisterInChunks == null || UnregisterFromChunks == null || SetWidgetName == null ||
                RegisterGdPoints == null || LinkExtensions == null || DrawerParentCraftHandler == null || CellState == null)
            {
                throw new InvalidOperationException("A game member this mod relies on was not found.");
            }
        }
    }

    internal enum MoveState
    {
        Idle,      // nothing going on
        Pending,   // a building was picked up in remove mode; placing starts next frame
        Placing,   // the game's build preview is up for the picked building
        Done       // the building was relocated; build mode is closed next frame
    }

    internal static class Mover
    {
        public static MoveState State = MoveState.Idle;

        // Set when the player picked the Move row; the remove pointer then moves instead of demolishing.
        public static bool Armed;

        // The Move row of the most recently opened build window (compared by reference).
        public static BuildData MoveEntry;

        public static Wgo Source;
        public static WgoData Data;
        public static BuildingDef Def;
        public static WorldZoneData Zone;

        private static bool abortPending;
        private static int rotationMatchFrames;
        private static bool pickedViaRow;

        public static bool HasWork => State != MoveState.Idle || Armed || abortPending || linkCheckData != null;

        public static bool ModifierHeld()
        {
            KeyCode key = Plugin.MoveModifier.Value.MainKey;
            return key != KeyCode.None && UnityInput.Current.GetKey(key);
        }

        // ---- pick up -------------------------------------------------------------------------

        // Decides whether this building can be moved. Everything refused here is something a same-object move
        // would leave inconsistent; the message is shown to the player.
        public static bool CanMove(Wgo wgo, out string reason, out BuildingDef def, out WorldZoneData zone)
        {
            reason = null;
            def = null;
            zone = null;
            WgoData data = wgo != null ? wgo.Data : null;
            // Not isRemovingFromData: the game sets it on every construction site that turns into its building
            // (WorldData.ChangeWgoData runs OnRemove on the same object) and never clears it.
            if (data == null || data.isTempObject || data.IsHidden || string.IsNullOrEmpty(data.id))
            {
                reason = "This can't be moved.";
                return false;
            }
            if (!IsLive(data))
            {
                reason = "The building is gone.";
                return false;
            }
            if (Plugin.ChangedGameCode)
            {
                // The move rewrites zone lists and positions through game code that changed since this build was read.
                reason = "Moving is off: the game changed code this mod uses. Wait for an updated mod.";
                return false;
            }
            if (IsCoop())
            {
                // The game's own remove is replicated to the other player; our zone writes are not.
                reason = "Moving isn't available in co-op.";
                return false;
            }
            string id = data.id;
            WGODef wgoDef = data.Definition;
            if (data is ConveyorWgoData || (wgoDef != null && wgoDef.conveyorType != 0) || (GameBalance.Me.conveyorWgosCache?.ContainsKey(id) ?? false))
            {
                reason = "Conveyors can't be moved.";
                return false;
            }
            if ((wgoDef != null && wgoDef.wgoGroup == "graves") || id.StartsWith("grave", StringComparison.Ordinal) || id.StartsWith("graveyard_module", StringComparison.Ordinal))
            {
                reason = "Graves and graveyard layouts can't be moved.";
                return false;
            }
            if (!wgo.IsBuildRemovable())
            {
                // The game's own rule for demolition (protected buildings, things standing in a strict area).
                reason = "The game won't let this one be picked up.";
                return false;
            }
            Dictionary<string, BuildingDef> buildable = GameBalance.Me.buildableWgos;
            if (buildable == null || (!buildable.TryGetValue(id, out def) && !buildable.TryGetValue(id + "_place", out def)) || def == null)
            {
                reason = "There's no build entry for this one.";
                return false;
            }
            if (def.buildingMode != BuildingDef.BuildingMode.Place)
            {
                // Conveyor, fighting and base buildings have their own placement logic and bookkeeping.
                reason = "This kind of building can't be moved.";
                return false;
            }
            if (def.chooseCustomBuildAreaType != BuildingDef.BuildAreaChoosingType.None &&
                def.chooseCustomBuildAreaType != BuildingDef.BuildAreaChoosingType.Soft)
            {
                // Strict / FullCover buildings live inside another building's area (grave places, workbench extensions).
                reason = "This one belongs to a special build area.";
                return false;
            }
            if (data.Worker != null)
            {
                reason = "Take the zombie off it first.";
                return false;
            }
            WgoPartData part = data.MainWgoPartData;
            if (part?.DockPointDataList != null && part.DockPointDataList.Exists(p => p != null && p.IsOccupied))
            {
                reason = "Someone is using it right now.";
                return false;
            }
            if (GardenBedNavigation.IsGardenPlot(data))
            {
                // Beds link approach points with their neighbours; other baked navigation points are re-registered.
                reason = "Garden beds can't be moved.";
                return false;
            }
            if (wgoDef != null && !string.IsNullOrEmpty(wgoDef.npcLifeSimGroup))
            {
                reason = "Villagers use this one; it can't be moved.";
                return false;
            }
            if (wgoDef != null && wgoDef.isMovable)
            {
                // Dynamic objects are tracked by a different chunk layer and adjust their own position.
                reason = "This one moves on its own.";
                return false;
            }
            WorldZoneData deskZone = LazySingleton<BuildManager>.Instance?.WorldZone?.Data;
            if (deskZone == null || data.WorldZoneData == null || !ReferenceEquals(data.WorldZoneData, deskZone))
            {
                reason = "It's outside this desk's area.";
                return false;
            }

            zone = deskZone;
            return true;
        }

        public static bool TryPickUp(Wgo wgo, out string reason)
        {
            if (!CanMove(wgo, out reason, out BuildingDef def, out WorldZoneData zone))
            {
                return false;
            }
            Source = wgo;
            Data = wgo.Data;
            Def = def;
            Zone = zone;
            pickedViaRow = Armed;
            State = MoveState.Pending;
            Plugin.Log.LogInfo($"Move: picked up {Data.id} at {Data.Position} (build entry {def.id}).");
            return true;
        }

        // ---- per-frame ------------------------------------------------------------------------

        public static void Tick()
        {
            TickLinkCheck();
            BuildController bc = BuildController.Instance;
            bool buildActive = bc != null && bc.IsBuildModeActive;

            if (abortPending)
            {
                abortPending = false;
                LeaveBuildMode(bc);
                Say("This one can't be moved.");
                return;
            }
            switch (State)
            {
                case MoveState.Pending:
                    StartPlacing(bc);
                    break;
                case MoveState.Placing:
                    if (!buildActive)
                    {
                        // Back / right click closed the preview (the game reopened the desk menu). Nothing was changed.
                        bool viaRow = pickedViaRow;
                        Cancel("the preview was closed");
                        if (Plugin.StayInMoveMode.Value)
                        {
                            ReenterMoveMode(bc, viaRow, closeMenu: true);
                        }
                    }
                    else if (rotationMatchFrames > 0)
                    {
                        // The preview's parts load asynchronously; keep trying briefly until they are there.
                        rotationMatchFrames = MatchRotation(bc) ? 0 : rotationMatchFrames - 1;
                    }
                    break;
                case MoveState.Done:
                    // The build key handler may have a one-frame input lock coroutine running that still touches
                    // the pointer; wait for it before tearing the pointer down.
                    if (!buildActive)
                    {
                        Cancel(null);
                    }
                    else if (!Access.InputLocked(bc))
                    {
                        bool viaRow = pickedViaRow;
                        Cancel(null);
                        if (Plugin.StayInMoveMode.Value)
                        {
                            ReenterMoveMode(bc, viaRow, closeMenu: false);
                        }
                        else
                        {
                            LeaveBuildMode(bc);
                        }
                    }
                    break;
                default:
                    if (Armed && !buildActive)
                    {
                        Armed = false;
                    }
                    break;
            }
        }

        private static void StartPlacing(BuildController bc)
        {
            BuildManager bm = LazySingleton<BuildManager>.Instance;
            if (bm == null || bc == null || Source == null || Data == null || Def == null)
            {
                Cancel("the build desk is gone");
                return;
            }
            Wgo desk = Access.CurrentBuildDesk(bm);
            List<Inventory> inventories = Access.AdditionalInventories(bm);
            try
            {
                if (bc.IsBuildModeActive)
                {
                    bc.DisableBuildMode();
                }
                CursorController.RemoveAllWithType(CursorType.BuildingModeDestroy);
                State = MoveState.Placing;
                // No item list: the pointer's cost check passes and its take-resources action does nothing.
                bm.EnableBuildMode(desk, BuildData.GetDataForBuild(Def), null, inventories);
                rotationMatchFrames = MatchRotation(bc) ? 0 : 60;
                Plugin.Log.LogInfo($"Move: placing {Data.id}; the game's preview decides where it may go.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Move: could not start the preview: {e}");
                Cancel("the preview could not be started");
                LeaveBuildMode(bc);
            }
        }

        // Turns the preview to the building's current rotation when both are the same object type.
        // Returns false only while the preview's main part hasn't loaded yet.
        private static bool MatchRotation(BuildController bc)
        {
            try
            {
                BuildPointer bp = Access.BuildPointerOf(bc);
                WgoBuildPointer pointer = bp?.PointerObject as WgoBuildPointer;
                Wgo ghost = pointer?.Target;
                WgoPartData part = Data?.MainWgoPartData;
                if (ghost == null || ghost.Data == null || part == null || ghost.Data.id != Data.id || part.rotationIndex < 0)
                {
                    return true;
                }
                WgoPartData ghostPart = ghost.MainWgoPart?.WgoPartData;
                if (ghostPart == null)
                {
                    return false;
                }
                if (!ghost.CanBeRotated())
                {
                    return true;
                }
                for (int i = 0; i < 4 && ghostPart.rotationIndex != part.rotationIndex; i++)
                {
                    bp.Rotate();
                }
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Move: could not match the rotation: {e.Message}");
                return true;
            }
        }

        // Called from the SetTarget postfix: a pointer subclass means the game placed this def specially.
        public static void RefusePointer(WgoBuildPointer pointer)
        {
            Plugin.Log.LogWarning($"Move: {Def?.id} is placed by {pointer.GetType().Name}, which a move can't finish; the move is called off.");
            Cancel(null);
            abortPending = true;
        }

        // ---- commit ---------------------------------------------------------------------------

        // The build key was pressed while our preview is up. Returns true when the building was relocated.
        public static bool TryCommit()
        {
            BuildController bc = BuildController.Instance;
            BuildPointer bp = bc != null ? Access.BuildPointerOf(bc) : null;
            WgoBuildPointer pointer = bp?.PointerObject as WgoBuildPointer;
            if (pointer == null || pointer.Target == null || pointer.Target.Data == null)
            {
                Plugin.Log.LogWarning("Move: the preview is gone; the move is called off.");
                Cancel(null);
                abortPending = true;
                return false;
            }
            if (!Access.ShownAsActive(pointer))
            {
                return false; // red preview: not a valid spot, same as the game
            }
            Wgo ghost = pointer.Target;
            Vector3 newPos = ghost.Data.Position;
            int newRot = -1;
            WgoPartData ghostPart = ghost.MainWgoPart?.WgoPartData;
            if (ghostPart != null && ghost.Data.id == Data.id && ghost.CanBeRotated())
            {
                newRot = ghostPart.rotationIndex;
            }
            if (!Relocate(newPos, newRot, out string reason))
            {
                Say(reason);
                return false;
            }
            State = MoveState.Done;
            LazyAudio.PlayAndForget("build_place");
            return true;
        }

        // Moves the existing object: out of its zone, new position/rotation, back into the zone the game picks.
        // Anything other than landing in the same zone is rolled back. Nothing is removed from or added to the scene,
        // so the object's id, contents, crafts, scripts and every reference to it stay as they were.
        private static bool Relocate(Vector3 newPos, int newRot, out string reason)
        {
            reason = null;
            WgoData data = Data;
            WorldZoneData zone = Zone;
            Wgo view = Source;
            if (view == null && data != null)
            {
                view = GameScene.GetWgoViewGlobal(data.UniqueId);
            }
            if (data == null || zone == null || view == null || !IsLive(data))
            {
                reason = "The building is gone.";
                return false;
            }
            if (!ReferenceEquals(data.WorldZoneData, zone))
            {
                reason = "The building changed areas.";
                return false;
            }
            GameSceneData scene = MainGame.Instance?.GameSave?.worldData?.GetGameSceneDataById(data.WorldId);
            if (scene == null)
            {
                reason = "The building's scene was not found.";
                return false;
            }
            WgoPartData part = data.MainWgoPartData;
            Vector3 oldPos = data.Position;
            int oldRot = part?.rotationIndex ?? -1;
            bool rotate = part != null && newRot >= 0 && newRot != oldRot;
            if (oldPos == newPos && !rotate)
            {
                Plugin.Log.LogInfo("Move: same spot and rotation; nothing to do.");
                return true;
            }

            bool hadGdPoints = data.gdPointsData != null && data.gdPointsData.Count > 0;
            int cutLinks = 0;
            Plugin.ReloadConfig(); // so Debug.FaultInjection can be changed between moves without a restart

            // ---- saved data. Everything in here is undone if any step fails or the landing zone is wrong. ----
            // Zone membership lists are persisted and never recomputed at load, so a half-done move must not survive.
            try
            {
                // Workbench links come from collider overlap at placement; cut them now and let the game's own
                // link routine rebuild them at the new spot (or the old one after a rollback).
                cutLinks = DetachLinks(data, view);
                Fault("AfterLinksCut");
                zone.RemoveWgoData(data);             // zone list, nav cut, custom-quality list
                Fault("AfterZoneRemove");
                data.Position = newPos;               // the view follows through Wgo.HandleChangedPos
                if (rotate)
                {
                    part.rotationIndex = newRot;      // before the zone re-adds the nav cut (it hashes the part state)
                }
                Fault("AfterPosition");
                WorldZoneData landed = AddToZones(scene, data);
                Fault("AfterZoneAdd");
                if (Plugin.FaultInjection.Value == "WrongZone")
                {
                    Plugin.Log.LogWarning("Move: fault injection: pretending the building landed in another zone.");
                    landed = null;
                }
                if (!ReferenceEquals(landed, zone))
                {
                    Restore(scene, zone, data, view, oldPos, oldRot, rotate);
                    VerifyRestore(scene, zone, data, oldPos, oldRot, cutLinks);
                    reason = landed == null ? "That spot is outside the area." : "That spot belongs to another area.";
                    Plugin.Log.LogWarning($"Move: {data.id} would land in {(landed == null ? "no zone" : landed.id)} instead of {zone.id}; rolled back.");
                    return false;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Move: relocating {data.id} failed; undoing. {e}");
                try
                {
                    Restore(scene, zone, data, view, oldPos, oldRot, rotate);
                    VerifyRestore(scene, zone, data, oldPos, oldRot, cutLinks);
                }
                catch (Exception e2)
                {
                    Plugin.Log.LogError($"Move: undo failed too; {data.id} may be missing from its area list. Reload the last save. {e2}");
                }
                reason = "The move failed and was undone.";
                return false;
            }

            // ---- view / runtime. Failures here are logged; the saved data above is already consistent. ----
            try
            {
                if (rotate)
                {
                    view.UpdateWgoPartState();        // applies the new part state and refreshes its nav cut
                }
                data.RefreshCustomNavMeshCutUnit();
                Physics.SyncTransforms();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Move: view refresh for {data.id} failed: {e.Message}");
            }
            if (hadGdPoints)
            {
                // Baked navigation points are stored in world space (and saved); register them again as the first
                // spawn did. If that fails, leave none and let the next spawn (reload) register them afresh.
                try
                {
                    Access.RegisterGdPoints(view);
                    view.BindGDPointViews();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Move: navigation points of {data.id} could not be re-registered; they return on the next load. {e.Message}");
                    try
                    {
                        data.RewriteGdPointsData(new List<GDPointData>());
                        data.gdPointsRegistered = false;
                    }
                    catch (Exception e2)
                    {
                        Plugin.Log.LogWarning($"Move: could not clear the navigation points either: {e2.Message}");
                    }
                }
            }
            RelinkExtensions(data, view);
            try
            {
                // Chunk membership is decided at registration from the position; re-register at the new spot.
                Access.UnregisterFromChunks(view);
                Access.RegisterInChunks(view);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Move: chunk re-registration for {data.id} failed (visibility only): {e.Message}");
            }
            Plugin.Log.LogInfo($"Move: {data.id} moved from {oldPos} to {newPos}{(rotate ? $", rotation {oldRot} -> {newRot}" : "")} in zone {zone.id}" +
                               $"{(cutLinks > 0 ? $"; {cutLinks} workbench link(s) cut and re-checked" : "")}{(hadGdPoints ? "; navigation points re-registered" : "")}.");
            return true;
        }

        // Testing hook: throws at the named step when Debug.FaultInjection names it.
        private static void Fault(string point)
        {
            if (Plugin.FaultInjection.Value == point)
            {
                throw new InvalidOperationException($"fault injection at {point}");
            }
        }

        // Logs a PASS/FAIL line for the saved state after a restore. Links are re-made by a coroutine one fixed
        // update later, so that part is checked from Tick a few frames on.
        private static void VerifyRestore(GameSceneData scene, WorldZoneData zone, WgoData data, Vector3 oldPos, int oldRot, int linksBefore)
        {
            try
            {
                SGuid uid = data.UniqueId;
                int lists = 0;
                int entries = 0;
                foreach (WorldZoneData z in scene.worldZones ?? new List<WorldZoneData>())
                {
                    int n = z?.wgoDataList?.FindAll(g => g == uid).Count ?? 0;
                    if (n > 0)
                    {
                        lists++;
                        entries += n;
                    }
                }
                bool posOk = data.Position == oldPos;
                bool rotOk = data.MainWgoPartData == null || data.MainWgoPartData.rotationIndex == oldRot;
                bool zoneOk = ReferenceEquals(data.WorldZoneData, zone) && zone.wgoDataList.Contains(uid);
                bool listOk = lists == 1 && entries == 1;
                bool live = IsLive(data);
                bool ok = posOk && rotOk && zoneOk && listOk && live;
                string line = $"Move restore check for {data.id}: {(ok ? "PASS" : "FAIL")} (position {(posOk ? "ok" : "WRONG " + data.Position)}, " +
                              $"rotation {(rotOk ? "ok" : "WRONG")}, zone {(zoneOk ? zone.id : "WRONG " + (data.WorldZoneData?.id ?? "none"))}, " +
                              $"in {lists} zone list(s) with {entries} entr{(entries == 1 ? "y" : "ies")}, in world {(live ? "yes" : "NO")}); links: {linksBefore} before, checked shortly.";
                if (ok)
                {
                    Plugin.Log.LogInfo(line);
                }
                else
                {
                    Plugin.Log.LogError(line);
                }
                linkCheckData = data;
                linkCheckExpected = linksBefore;
                linkCheckFrames = 5;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Move restore check could not run: {e.Message}");
            }
        }

        private static WgoData linkCheckData;
        private static int linkCheckExpected;
        private static int linkCheckFrames;

        private static void TickLinkCheck()
        {
            if (linkCheckData == null || --linkCheckFrames > 0)
            {
                return;
            }
            WgoData data = linkCheckData;
            linkCheckData = null;
            int now = (data.AttachedWorkbenchExtensions?.Count ?? 0) + (data.WorkbenchParents?.Count ?? 0);
            string line = $"Move restore check for {data.id}: links {(now == linkCheckExpected ? "PASS" : "FAIL")} ({now} now, {linkCheckExpected} before).";
            if (now == linkCheckExpected)
            {
                Plugin.Log.LogInfo(line);
            }
            else
            {
                Plugin.Log.LogError(line);
            }
        }

        // Puts the object back exactly where it was: out of every zone list it may be in (a zone add can fail
        // half-way, after the list entry but before the back-reference), old position and rotation, back into its
        // original zone, links re-checked there.
        private static void Restore(GameSceneData scene, WorldZoneData zone, WgoData data, Wgo view, Vector3 oldPos, int oldRot, bool rotate)
        {
            SGuid uid = data.UniqueId;
            if (scene.worldZones != null)
            {
                foreach (WorldZoneData z in scene.worldZones)
                {
                    for (int guard = 0; z != null && z.wgoDataList != null && z.wgoDataList.Contains(uid) && guard < 4; guard++)
                    {
                        z.RemoveWgoData(data);
                    }
                }
            }
            data.WorldZoneData = null;
            data.Position = oldPos;
            if (rotate && data.MainWgoPartData != null)
            {
                data.MainWgoPartData.rotationIndex = oldRot;
            }
            if (!zone.TryAddWgoData(data) || !zone.wgoDataList.Contains(uid))
            {
                Plugin.Log.LogError($"Move: {data.id} could not be put back into zone {zone.id}. Reload the last save.");
            }
            Physics.SyncTransforms();
            RelinkExtensions(data, view);
        }

        // Same test the game's bug reporter uses for "is this a co-op game".
        private static bool IsCoop()
        {
            try
            {
                return LazyNetwork.IsInitialized && LazyNetwork.NetworkManager != null && LazyNetwork.NetworkManager.IsCoopGame;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // The object still belongs to the world (same instance the world resolves for its id).
        private static bool IsLive(WgoData data)
        {
            WorldData world = MainGame.Instance?.GameSave?.worldData;
            return world != null && ReferenceEquals(world.GetWgoData(data.UniqueId), data);
        }

        // Removes every extension/parent link on both sides. The moved object's drawers stop listening to the old
        // parents' craft status (the game only does that when a view despawns).
        private static int DetachLinks(WgoData data, Wgo view)
        {
            WorldData world = MainGame.Instance.GameSave.worldData;
            int n = 0;
            foreach (SGuid ext in new List<SGuid>(data.AttachedWorkbenchExtensions ?? (IReadOnlyList<SGuid>)Array.Empty<SGuid>()))
            {
                world.GetWgoData(ext)?.RemoveWorkbenchParent(data.UniqueId);
                data.RemoveWorkbenchExtension(ext);
                n++;
            }
            ConditionalDrawer[] drawers = view.GetComponentsInChildren<ConditionalDrawer>(true);
            foreach (SGuid parentId in new List<SGuid>(data.WorkbenchParents ?? (IReadOnlyList<SGuid>)Array.Empty<SGuid>()))
            {
                WgoData parent = world.GetWgoData(parentId);
                if (parent?.CraftComponent != null)
                {
                    foreach (ConditionalDrawer drawer in drawers)
                    {
                        var handler = (Action<CraftComponentStatus>)Delegate.CreateDelegate(typeof(Action<CraftComponentStatus>), drawer, Access.DrawerParentCraftHandler);
                        parent.CraftComponent.OnStatusChanged -= handler;
                    }
                    parent.RemoveWorkbenchExtension(data.UniqueId);
                }
                data.RemoveWorkbenchParent(parentId);
                n++;
            }
            return n;
        }

        // Runs the game's spawn-time link routine for the object at its current spot: as a workbench it takes the
        // extensions overlapping its area; as an extension it attaches to the workbench it overlaps.
        private static void RelinkExtensions(WgoData data, Wgo view)
        {
            try
            {
                GameBalance gb = GameBalance.Me;
                WGODef logic = gb.GetWorkbenchExtensionLogicDef(data.id);
                bool isWorkbench = logic != null && gb.workbenchesWhichUseExtensions.Contains(logic);
                bool isExtension = gb.IsWorkbenchExtensionId(data.id);
                if (isWorkbench)
                {
                    view.StartCoroutine(Access.LinkExtensions(view, true));
                }
                if (isExtension)
                {
                    view.StartCoroutine(Access.LinkExtensions(view, false));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Move: could not re-check workbench links for {data.id}: {e.Message}");
            }
        }

        // Same order the game uses when it adds an object to a scene: highest processing priority first.
        private static WorldZoneData AddToZones(GameSceneData scene, WgoData data)
        {
            if (scene.WorldZonesByProcessingPriority != null)
            {
                KeyValuePair<int, List<WorldZoneData>>[] byPriority = new KeyValuePair<int, List<WorldZoneData>>[scene.WorldZonesByProcessingPriority.Count];
                scene.WorldZonesByProcessingPriority.CopyTo(byPriority, 0);
                for (int i = byPriority.Length - 1; i >= 0; i--)
                {
                    foreach (WorldZoneData z in byPriority[i].Value)
                    {
                        if (z != null && z.TryAddWgoData(data))
                        {
                            return z;
                        }
                    }
                }
            }
            return null;
        }

        // ---- teardown -------------------------------------------------------------------------

        // Back into the same remove/move mode the player was in before the pick-up, as if the row had been picked
        // again: armed move mode when it came from the Move row, the plain Remove mode otherwise.
        private static void ReenterMoveMode(BuildController bc, bool viaRow, bool closeMenu)
        {
            try
            {
                BuildManager bm = LazySingleton<BuildManager>.Instance;
                Wgo desk = bm != null ? Access.CurrentBuildDesk(bm) : null;
                if (bc == null || desk == null)
                {
                    LeaveBuildMode(bc);
                    return;
                }
                if (bc.IsBuildModeActive)
                {
                    bc.DisableBuildMode();
                }
                BuildData entry;
                if (viaRow)
                {
                    entry = MoveEntry ?? (MoveEntry = BuildData.GetDataForRemove());
                }
                else
                {
                    entry = BuildData.GetDataForRemove();
                }
                bm.EnableBuildMode(desk, entry, null, Access.AdditionalInventories(bm));
                if (closeMenu)
                {
                    LazyUI.GetWindow<UIBuildingWindow>()?.Close(); // same order as the game's row press
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Move: could not return to move mode: {e.Message}");
                LeaveBuildMode(bc);
            }
        }

        // Same as the game's Back key: close the preview and reopen the desk's window.
        private static void LeaveBuildMode(BuildController bc)
        {
            try
            {
                if (bc != null && bc.IsBuildModeActive)
                {
                    bc.DisableBuildMode();
                    LazySingleton<BuildManager>.Instance?.Disable();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Move: could not leave build mode: {e.Message}");
            }
        }

        public static void Cancel(string why)
        {
            if (State != MoveState.Idle && why != null)
            {
                Plugin.Log.LogInfo($"Move cancelled: {why}.");
            }
            State = MoveState.Idle;
            Armed = false;
            rotationMatchFrames = 0;
            Source = null;
            Data = null;
            Def = null;
            Zone = null;
        }

        public static void Say(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            try
            {
                Bubble.Talk(new PhraseData(true, null, text, null, null, SpeechBubbleType.Think));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Move: could not show \"{text}\": {e.Message}");
            }
        }
    }
}

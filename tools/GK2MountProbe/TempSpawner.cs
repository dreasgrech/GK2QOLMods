using System;
using UnityEngine;

namespace GK2MountProbe
{
    // Spawns a WGO view the same way the game's build preview does: a temp WgoData (isTempObject) that is never
    // added to WorldData or any zone list, so it can never reach the save file. Torn down with Object.Destroy,
    // which is also what the build pointer does (Wgo.OnDestroy -> DeInit).
    internal static class TempSpawner
    {
        // Set while our WgoData is being constructed: WgoData.PrepareForGame starts the definition's attached
        // flow script (npc_donkey has one that teleports it to its delivery point and walks its route), which a
        // purely visual temp object must never run. The Harmony prefix below skips CreateScript while this is set.
        internal static bool SuppressScripts;

        // Cart and lantern parts of the donkey prefab (names from the probe report); toggled by SetCartVisible.
        private static readonly string[] CartPartNames =
        {
            "wheel_left", "wheel_right", "light_sway",
            "donkey_cart_wheel_f", "donkey_cart_wheel_b", "donkey_cart_bars",
            "donkey_cart_empty_sway", "donkey_cart_body_sway", "donkey_cart_lantern_sway",
        };

        public static Wgo Spawn(string wgoId, Vector3 position, GameScene scene, bool disableColliders)
        {
            WgoData data;
            SuppressScripts = true;
            try
            {
                data = new WgoData(wgoId, position, scene.Id) { isTempObject = true };
            }
            finally
            {
                SuppressScripts = false;
            }
            Wgo wgo = Wgo.Spawn(data, scene.transform, registerInChunkManagerIfStatic: true, ignoreChunkRegistration: true, applyDefaultWgoPartState: true);
            wgo.UpdateChunkVisibility(isVisible: true);
            if (disableColliders)
            {
                DisableColliders(wgo);
            }
            Plugin.Log.LogInfo($"Spawned temp [{wgoId}] uid={data.UniqueId} at {position} (isTempObject={data.isTempObject}, zone={(data.WorldZoneData == null ? "none" : data.WorldZoneData.id)})");
            return wgo;
        }

        // Same treatment the build preview and the carried fight flag give their WGO: no physics blocking,
        // no interaction targeting, no navmesh cut.
        public static void DisableColliders(Wgo wgo)
        {
            if (wgo == null)
            {
                return;
            }
            try
            {
                wgo.SetInteractableCollidersState(isActive: false);
                wgo.SetLayerToAllColliders(26);
                Collider[] colliders = wgo.GetComponentsInChildren<Collider>(includeInactive: true);
                foreach (Collider collider in colliders)
                {
                    int layer = collider.gameObject.layer;
                    if (layer == 8 || layer == 11 || layer == 6)
                    {
                        collider.gameObject.SetActive(value: false);
                    }
                    else
                    {
                        collider.enabled = false;
                    }
                }
                NavMeshCutBoxCustom[] cuts = wgo.GetComponentsInChildren<NavMeshCutBoxCustom>(includeInactive: true);
                for (int i = 0; i < cuts.Length; i++)
                {
                    cuts[i].gameObject.SetActive(value: false);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"DisableColliders failed: {e.Message}");
            }
        }

        // Shows or hides the cart, wheels and lantern. Returns how many parts were found.
        public static int SetCartVisible(Wgo wgo, bool visible)
        {
            if (wgo == null)
            {
                return 0;
            }
            int found = 0;
            Transform[] all = wgo.GetComponentsInChildren<Transform>(includeInactive: true);
            foreach (Transform t in all)
            {
                if (Array.IndexOf(CartPartNames, t.name) >= 0)
                {
                    // donkey_cart_body_sway is off in the prefab (it holds the corpse shroud); never turn it on.
                    if (visible && t.name == "donkey_cart_body_sway")
                    {
                        continue;
                    }
                    t.gameObject.SetActive(visible);
                    found++;
                }
            }
            return found;
        }

        public static void Destroy(ref Wgo wgo)
        {
            if (wgo != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(wgo.gameObject);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Destroy failed: {e.Message}");
                }
            }
            wgo = null;
        }

        [HarmonyLib.HarmonyPatch(typeof(WgoDataScriptsManager), nameof(WgoDataScriptsManager.CreateScript))]
        private static class WgoDataScriptsManager_CreateScript_Patch
        {
            private static bool Prefix(WgoData wgoData, string scriptName)
            {
                if (!SuppressScripts)
                {
                    return true;
                }
                Plugin.Log.LogInfo($"Skipped attached script [{scriptName}] for temp [{wgoData?.id}].");
                return false;
            }
        }

        // Direction pointing away from the camera along the ground, so a positive depth offset draws behind the player.
        public static Vector3 AwayFromCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return Vector3.forward;
            }
            Vector3 f = cam.transform.forward;
            f.y = 0f;
            return f.sqrMagnitude < 0.0001f ? Vector3.forward : f.normalized;
        }
    }
}

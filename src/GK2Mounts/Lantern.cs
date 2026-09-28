using System;
using UnityEngine;

namespace GK2Mounts
{
    // Moves and tilts the cart's lantern pole, which the prefab puts at the front rail where the rider's head hides it.
    //
    // What the prefab has (GK2-lantern-facts.txt, Round 8 of the findings): under the animator root, two sibling
    // nodes share one pivot at (0.36, 0.267, 0): donkey_cart_lantern_sway holds the 3D pole mesh, light_sway holds
    // the glow sprite, the lit lantern-glass sprite and two point lights. The walk animation rocks the whole cart
    // (cart body, bars, both lantern nodes) about that pivot by up to 3 degrees in the screen plane, writing the
    // sway nodes' rotation every frame; it never touches their children. So the mod poses the children, once, and
    // the animation keeps rocking the posed pole with the cart. Nothing is reparented (the animator binds by path).
    //
    // Geometry is in the sway pivot's space: x along the cart, y up, z across (towards or away from the camera).
    // The pole's foot and head come from its mesh bounds, so the tilt turns the pole about its own foot, not about
    // the axle. The light rig is only moved, never rotated (its sprites are upright pixel art): it follows the
    // displacement of the pole's head, so the lamp stays on the pole. Which way is "back" and "towards the camera"
    // is measured from the cart body and the camera axis, not assumed, so a mirrored root (the animator root has
    // scale -1 in one facing) changes nothing.
    internal static class Lantern
    {
        private const float ShaftHalf = 0.04f; // half the pole shaft's thickness: the foot's axis sits this far inside the mesh box

        private static Transform pole, light;
        private static Vector3 poleRest, lightRest, lightScaleRest;
        private static Quaternion poleRotRest, lightRotRest;
        private static Vector3 foot, head;
        private static float backSign = -1f, camSign = -1f;
        private static Vector3 lastPolePos, lastLightPos;
        private static Quaternion lastPoleRot;
        private static float aAlong, aUp, aAcross, aTilt, aLean;
        private static bool aFlip;
        private static bool applied, overwriteLogged;
        private static int overwrites;

        public static bool Attached => pole != null && pole;
        public static string Note { get; private set; } = "";

        public static void Attach(Wgo donkey)
        {
            Detach();
            try
            {
                Transform root = donkey.transform;
                Transform poleSway = FindTopLevel(root, "donkey_cart_lantern_sway");
                Transform lightSway = FindTopLevel(root, "light_sway");
                if (poleSway == null || lightSway == null)
                {
                    Note = "lantern nodes not found on this prefab; the lantern settings do nothing";
                    Plugin.Log.LogWarning("Lantern: " + Note);
                    return;
                }
                pole = poleSway.Find("donkey_cart_lantern") ?? (poleSway.childCount > 0 ? poleSway.GetChild(0) : null);
                light = lightSway.Find("light") ?? (lightSway.childCount > 0 ? lightSway.GetChild(0) : null);
                if (pole == null || light == null)
                {
                    pole = light = null;
                    Note = "lantern child nodes not found; the lantern settings do nothing";
                    Plugin.Log.LogWarning("Lantern: " + Note);
                    return;
                }
                poleRest = pole.localPosition; poleRotRest = pole.localRotation;
                lightRest = light.localPosition; lightRotRest = light.localRotation; lightScaleRest = light.localScale;
                if (!Cargo.MeasureMeshes(poleSway, poleSway, out Bounds b))
                {
                    b = new Bounds(new Vector3(-0.46f, 0.55f, 0.26f), new Vector3(0.52f, 1.03f, 0.28f)); // measured 2026-09-28
                }
                // Back of the cart: the side of the animator root the cart body's centre lies on, along the cart.
                Transform baseT = poleSway.parent;
                Transform body = FindTopLevel(root, "donkey_cart_empty_sway");
                Renderer br = body != null ? body.GetComponentInChildren<Renderer>(includeInactive: false) : null;
                float bodyX = float.NaN;
                if (br != null && baseT != null)
                {
                    bodyX = baseT.InverseTransformPoint(br.bounds.center).x;
                    backSign = bodyX < 0f ? -1f : 1f;
                }
                // The pole is slanted: its foot stands at the end of its box on the cart's back side and the shaft
                // leans forward over the donkey, with the hook and the lamp at the front end (in-game screenshots,
                // Round 8; the mesh is GPU-only, so this cannot be read from vertices). The foot is the pivot for the
                // tilt, the lean and the mirror, so it stays put whatever is set.
                float footX = backSign > 0f ? b.max.x - ShaftHalf : b.min.x + ShaftHalf;
                foot = new Vector3(footX, b.min.y, b.center.z);
                head = new Vector3(backSign > 0f ? b.min.x + ShaftHalf : b.max.x - ShaftHalf, b.max.y, b.center.z);
                // Towards the camera: the local z direction that points against the camera's forward axis.
                Camera cam = CameraSystem.Instance != null ? CameraSystem.Instance.WorldCamera : Camera.main;
                if (cam != null && baseT != null)
                {
                    camSign = Vector3.Dot(baseT.TransformDirection(Vector3.forward), cam.transform.forward) > 0f ? -1f : 1f;
                }
                applied = false;
                overwrites = 0;
                overwriteLogged = false;
                Note = $"pole box x {b.min.x:F2}..{b.max.x:F2}, y {b.min.y:F2}..{b.max.y:F2}; foot at the back end ({foot.x:F2}, {foot.y:F2}, {foot.z:F2}), top at the front end (x {head.x:F2}, y {head.y:F2}); back = {(backSign < 0 ? "-x" : "+x")} (cart body at x {bodyX:F2}), camera = {(camSign < 0 ? "-z" : "+z")}";
                Plugin.Log.LogInfo("Lantern: attached; " + Note);
                Apply(true);
            }
            catch (Exception e)
            {
                pole = light = null;
                Note = "attach failed: " + e.Message;
                Plugin.Log.LogWarning("Lantern: " + Note);
            }
        }

        public static void Detach()
        {
            try
            {
                if (pole != null && pole)
                {
                    pole.localPosition = poleRest;
                    pole.localRotation = poleRotRest;
                    pole.localScale = Vector3.one;
                }
                if (light != null && light)
                {
                    light.localPosition = lightRest;
                    light.localRotation = lightRotRest;
                    light.localScale = lightScaleRest;
                }
            }
            catch
            {
                // the donkey may be half destroyed
            }
            pole = light = null;
            applied = false;
        }

        // Called by the panel when a slider moves; Tick also picks up changes made any other way (config reload).
        public static void Refresh()
        {
            Apply(true);
        }

        // Once per frame after the animator (the plugin's LateUpdate): re-applies when a setting changed, and when
        // something else moved the children, which the design says never happens; if it does, the log says so once
        // and the pose is simply written every frame from then on.
        public static void Tick()
        {
            if (!Attached)
            {
                return;
            }
            try
            {
                if (applied && (pole.localPosition != lastPolePos || Quaternion.Angle(pole.localRotation, lastPoleRot) > 0.05f || light.localPosition != lastLightPos))
                {
                    overwrites++;
                    if (!overwriteLogged)
                    {
                        overwriteLogged = true;
                        Plugin.Log.LogInfo($"Lantern: the pole's child node was moved by something else (pos {pole.localPosition:F3} vs {lastPolePos:F3}, rot {pole.localEulerAngles:F1}); re-applying the lantern pose every frame from now on.");
                    }
                    Apply(true);
                    return;
                }
                // The sway: a slow pendulum whose amplitude drifts (two incommensurate sines) and follows the game's
                // wind value (WeatherSystem.WindValue, the same number its cloth shader reads). The game never sways
                // the pole itself except by rocking the whole cart while it rolls; this adds to that.
                StyleSettings s = Mount.S;
                float degrees = s.LanternSwayDegrees.Value;
                float newSway = 0f;
                if (degrees > 0f)
                {
                    float period = Mathf.Max(0.5f, s.LanternSwaySeconds.Value);
                    float t = Time.time;
                    float drift = 0.65f + 0.35f * Mathf.Sin(t * 0.37f + 1.3f);
                    float wind = 0.5f + Mathf.Clamp01(WindValue());
                    newSway = degrees * drift * wind * Mathf.Sin(t * (2f * Mathf.PI / period));
                }
                if (Mathf.Abs(newSway - swayTilt) > 0.001f)
                {
                    swayTilt = newSway;
                    Apply(true);
                    return;
                }
                Apply(false);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Lantern: tick failed: " + e.Message);
                pole = light = null;
            }
        }

        private static float swayTilt;
        private static float windCached;
        private static float windCheckedAt = -1f;

        private static float WindValue()
        {
            if (Time.unscaledTime - windCheckedAt > 1f)
            {
                windCheckedAt = Time.unscaledTime;
                try
                {
                    WeatherSystem w = WeatherSystem.Instance;
                    windCached = w != null ? w.WindValue : 0.5f;
                }
                catch
                {
                    windCached = 0.5f;
                }
            }
            return windCached;
        }

        private static void Apply(bool force)
        {
            if (!Attached)
            {
                return;
            }
            StyleSettings s = Mount.S;
            float along = s.LanternAlong.Value, up = s.LanternUp.Value, across = s.LanternAcross.Value, tilt = s.LanternTilt.Value, lean = s.LanternLean.Value;
            bool flip = s.LanternFlip.Value;
            if (!force && applied && along == aAlong && up == aUp && across == aAcross && tilt == aTilt && lean == aLean && flip == aFlip)
            {
                return;
            }
            // A rotation about +z moves +y towards -x, so the top leans towards backSign*x with angle -backSign*tilt.
            // A rotation about +x moves +y towards +z, so the top leans towards camSign*z with angle camSign*lean.
            Quaternion r = Quaternion.AngleAxis(-backSign * (tilt + swayTilt), Vector3.forward) * Quaternion.AngleAxis(camSign * lean, Vector3.right);
            Vector3 offset = new Vector3(backSign * along, up, camSign * across);
            // The flip is a mirror in the plane through the foot, across the cart: the slanted pole then leans out
            // over the back instead of forward over the donkey, and the foot stays where it is. For the child that is
            // a negative x scale (Unity flips the face winding for a mirrored mesh, so it draws normally) with its
            // origin moved to the mirrored spot: a mesh point q lands at M(rest) + S q = M(rest + q).
            Vector3 poleAt = poleRest;
            Vector3 poleScale = Vector3.one;
            if (flip)
            {
                poleAt.x = 2f * foot.x - poleRest.x;
                poleScale.x = -1f;
            }
            // Turn the pole about its foot: a point of the child lands at foot + r*(point - foot).
            pole.localRotation = r * poleRotRest;
            pole.localScale = poleScale;
            pole.localPosition = foot + r * (poleAt - foot) + offset;
            // The lamp rig (sprites and lights) is never rotated, its sprites are upright pixel art. Flipped, it is
            // mirrored in the same plane: the node goes to the mirrored spot and its x scale turns negative, which
            // mirrors sprites, glow offset and light positions alike (negative x scale is how the game draws the
            // donkey facing the other way, so its sprite shader is known to take it). Then the rig follows the top
            // of the pole.
            Vector3 rig = lightRest;
            Vector3 scale = lightScaleRest;
            if (flip)
            {
                rig.x = 2f * foot.x - lightRest.x;
                scale.x = -lightScaleRest.x;
            }
            Vector3 top = head;
            if (flip)
            {
                top.x = 2f * foot.x - head.x;
            }
            Vector3 headShift = foot + r * (top - foot) - top;
            light.localRotation = lightRotRest;
            light.localScale = scale;
            light.localPosition = rig + headShift + offset;
            lastPolePos = pole.localPosition;
            lastPoleRot = pole.localRotation;
            lastLightPos = light.localPosition;
            aAlong = along; aUp = up; aAcross = across; aTilt = tilt; aLean = lean; aFlip = flip;
            applied = true;
        }

        public static string Summary()
        {
            if (!Attached)
            {
                return Note;
            }
            return $"{Note}; pole child at {pole.localPosition:F2}, {pole.localEulerAngles:F0}°{(aFlip ? ", mirrored" : "")}" + (overwrites > 0 ? $"; overwritten by the game in {overwrites} frames (re-applied)" : "");
        }

        // The shallowest transform with that name: the prefab reuses names down the tree (each Object3D's mesh child
        // is named like its parent).
        private static Transform FindTopLevel(Transform root, string name)
        {
            Transform best = null;
            int bestDepth = int.MaxValue;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t.name != name)
                {
                    continue;
                }
                int d = 0;
                for (Transform p = t; p != null && p != root; p = p.parent)
                {
                    d++;
                }
                if (d < bestDepth)
                {
                    bestDepth = d;
                    best = t;
                }
            }
            return best;
        }
    }
}

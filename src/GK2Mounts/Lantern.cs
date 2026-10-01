using System;
using UnityEngine;

using GK2Mounts.Api;

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
    internal static partial class Lantern
    {
        // The bound instance's lantern (Mount.Current, see Mount.Instance): what the panel shows and edits.
        internal static Rig Current => Mount.Current?.LanternRig;
        public static bool Attached => Current != null && Current.Attached;
        public static string Note => Current?.Note ?? "";
        public static string Summary() => Current?.Summary() ?? "";
        public static string LightSummary() => Current?.LightSummary() ?? "";
        internal static Color ParseColor(string hex, Color fallback) => Rig.ParseColor(hex, fallback);
        internal static string ToHex(Color c) => Rig.ToHex(c);

        // A lantern setting changed (panel, API): every instance re-applies its pose, each with its own settings.
        public static void Refresh()
        {
            Mount.ForEachInstance(i => i.LanternRig.Refresh());
        }

        // One per instance (Mount.Instance.Lantern).
        internal sealed partial class Rig
        {
            private readonly Mount.Instance owner;
            internal readonly LampHang.Rig Lamp;

            internal Rig(Mount.Instance owner)
            {
                this.owner = owner;
                Lamp = new LampHang.Rig(owner);
            }

            private const float ShaftHalf = 0.04f; // half the pole shaft's thickness: the foot's axis sits this far inside the mesh box

            private Transform pole, light;
            private Vector3 poleRest, lightRest, lightScaleRest;
            private Quaternion poleRotRest, lightRotRest;
            private Vector3 foot, head;
            private Vector3 rigPosed;          // the rig's pose as Apply set it, before LampHang hangs it
            private Quaternion rigRotPosed = Quaternion.identity;
            private float backSign = -1f, camSign = -1f;
            private Vector3 lastPolePos, lastLightPos;
            private Quaternion lastPoleRot;
            private float aAlong, aUp, aAcross, aTilt, aLean, aScale;
            private bool aFlip;
            private bool applied, overwriteLogged;
            private int overwrites;

            // The lamp rig's parts (GK2-lantern-facts.txt): the main point light (range 4.5, intensity 3, orange, soft
            // shadows, no day-night component, so lit around the clock), the "self" light that lights only characters and
            // sprites (range 2, intensity 10, culling mask CustomLightingObject; its SyncedLight has no source and does
            // nothing), the additive glow sprite and the lit glass sprite. Their authored values are kept and every
            // setting is a multiplier or a switch on top, so a game update to the prefab still shows through.
            private Light mainLight, selfLight;
            private SpriteRenderer glow, glass;
            private float mainIntensity, mainRange, selfIntensity, selfRange;
            private Color mainColor, selfColor, glowColor;
            private LightShadows mainShadows;
            private Vector3 glowScaleRest;
            private bool poleShown = true;

            public bool Attached => pole != null && pole;

            public string Note { get; private set; } = "";

            public void Attach(Wgo donkey)
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
                    mainLight = selfLight = null;
                    glow = glass = null;
                    foreach (Light l in light.GetComponentsInChildren<Light>(includeInactive: true))
                    {
                        if (l.name == "light_source") mainLight = l;
                        else if (l.name == "light_source_self") selfLight = l;
                    }
                    foreach (SpriteRenderer sr in light.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
                    {
                        if (sr.name == "light_lt") glow = sr;
                        else if (sr.name == "light_ut") glass = sr;
                    }
                    if (mainLight != null) { mainIntensity = mainLight.intensity; mainRange = mainLight.range; mainColor = mainLight.color; mainShadows = mainLight.shadows; }
                    if (selfLight != null) { selfIntensity = selfLight.intensity; selfRange = selfLight.range; selfColor = selfLight.color; }
                    if (glow != null) { glowColor = glow.color; glowScaleRest = glow.transform.localScale; }
                    poleShown = true;
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

            public void Detach()
            {
                Lamp.Detach(); // the original mesh back before the pole is reset (and measured again on attach)
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
                    if (!poleShown)
                    {
                        if (pole != null && pole) pole.gameObject.SetActive(true);
                        if (light != null && light) light.gameObject.SetActive(true);
                    }
                    if (mainLight != null && mainLight) { mainLight.enabled = true; mainLight.intensity = mainIntensity; mainLight.range = mainRange; mainLight.color = mainColor; mainLight.shadows = mainShadows; }
                    if (selfLight != null && selfLight) { selfLight.enabled = true; selfLight.intensity = selfIntensity; selfLight.range = selfRange; selfLight.color = selfColor; }
                    if (glow != null && glow) { glow.enabled = true; glow.color = glowColor; glow.transform.localScale = glowScaleRest; }
                    if (glass != null && glass) { glass.enabled = true; }
                    owner.ColorsRig.RestoreGlass();
                }
                catch
                {
                    // the donkey may be half destroyed
                }
                pole = light = null;
                mainLight = selfLight = null;
                glow = glass = null;
                applied = false;
            }

            // The lamp's lights and sprites, every frame: multipliers and switches on the authored values, and the
            // day-night dimming the game gives its own hand lantern (DayNightLight, Night mode: intensity × (1 − sun))
            // but not the cart's: here intensity × lerp(1, DayDim, sun), with sun the game's global shader value.
            private void ApplyLight(StyleSettings s)
            {
                bool show = s.LanternShow.Value && MountRules.Allowed(MountFeature.Lantern);
                bool lit = MountRules.Allowed(MountFeature.LanternLight);
                if (show != poleShown)
                {
                    poleShown = show;
                    pole.gameObject.SetActive(show);
                    light.gameObject.SetActive(show);
                }
                if (!show)
                {
                    return;
                }
                float sun = 0f;
                try
                {
                    sun = Mathf.Clamp01(Shader.GetGlobalFloat(GlobalShaderParameters.idSunLight));
                }
                catch
                {
                }
                float day = Mathf.Lerp(1f, Mathf.Clamp01(s.LanternLightDayDim.Value), sun);
                Color color = ParseColor(s.LanternLightColor.Value, mainColor);
                if (mainLight != null && mainLight)
                {
                    mainLight.enabled = lit && s.LanternLightOn.Value;
                    mainLight.intensity = mainIntensity * s.LanternLightIntensity.Value * day;
                    mainLight.range = mainRange * s.LanternLightRange.Value;
                    mainLight.color = color;
                    mainLight.shadows = s.LanternLightShadows.Value ? mainShadows : LightShadows.None;
                }
                if (selfLight != null && selfLight)
                {
                    selfLight.enabled = lit && s.LanternSelfLightOn.Value;
                    selfLight.intensity = selfIntensity * s.LanternSelfLightIntensity.Value * day;
                    selfLight.range = selfRange * s.LanternSelfLightRange.Value;
                    selfLight.color = Colors.CharacterLight(string.IsNullOrEmpty(s.LanternLightColor.Value) ? selfColor : color);
                }
                if (glow != null && glow)
                {
                    glow.enabled = lit && s.LanternGlowOn.Value;
                    Color c = Colors.GlowRgb(glowColor); // Colors tab: its own colour, the game's alpha
                    c.a = Mathf.Clamp01(glowColor.a * s.LanternGlowAlpha.Value * day);
                    glow.color = c;
                    glow.transform.localScale = glowScaleRest * s.LanternGlowScale.Value;
                }
                if (glass != null && glass)
                {
                    glass.enabled = lit && s.LanternGlassOn.Value;
                    owner.ColorsRig.ApplyGlass(glass);
                }
            }

            internal static Color ParseColor(string hex, Color fallback)
            {
                if (string.IsNullOrEmpty(hex))
                {
                    return fallback;
                }
                // Every frame per lamp: read in place (Colors.TryHex), not trimmed and joined into new strings.
                return Colors.TryHex(hex, out Color32 c) ? (Color)c : fallback;
            }

            internal static string ToHex(Color c)
            {
                return ColorUtility.ToHtmlStringRGB(c);
            }

            public string LightSummary()
            {
                if (!Attached)
                {
                    return "";
                }
                float sun = 0f;
                try { sun = Shader.GetGlobalFloat(GlobalShaderParameters.idSunLight); } catch { }
                return $"Game values: main light {mainIntensity:F1} × {mainRange:F1} u {ToHex(mainColor)} shadows {mainShadows}; character light {selfIntensity:F1} × {selfRange:F1} u; glow alpha {glowColor.a:F2}. Sun now {sun:F2}. Live: main {(mainLight != null && mainLight ? mainLight.intensity.ToString("F2") : "-")}, character {(selfLight != null && selfLight ? selfLight.intensity.ToString("F2") : "-")}.";
            }

            // Called by the panel when a slider moves; Tick also picks up changes made any other way (config reload).
            public void Refresh()
            {
                Apply(true);
            }

            // Once per frame after the animator (the plugin's LateUpdate): re-applies when a setting changed, and when
            // something else moved the children, which the design says never happens; if it does, the log says so once
            // and the pose is simply written every frame from then on.
            public void Tick()
            {
                TickPose();
                if (!Attached)
                {
                    return;
                }
                try
                {
                    bool want = owner.S.LanternHangs.Value && poleShown;
                    if (want && !Lamp.Active)
                    {
                        Lamp.Attach(pole);
                    }
                    else if (!want && Lamp.Active)
                    {
                        Lamp.Detach();
                        Apply(true); // the rig back to the pole's pose
                    }
                    if (Lamp.Active)
                    {
                        Lamp.Tick(light, rigPosed, rigRotPosed);
                        lastLightPos = light.localPosition; // the hang, not someone else moving the rig
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Lantern: hanging the lamp failed, it turns with the pole: " + e.Message);
                    Lamp.Detach();
                }
            }

            private void TickPose()
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
                    StyleSettings s = owner.S;
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
                    ApplyLight(s);
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

            private float swayTilt;
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

            private void Apply(bool force)
            {
                if (!Attached)
                {
                    return;
                }
                StyleSettings s = owner.S;
                float along = s.LanternAlong.Value, up = s.LanternUp.Value, across = s.LanternAcross.Value, tilt = s.LanternTilt.Value, lean = s.LanternLean.Value;
                bool flip = s.LanternFlip.Value;
                float scale = Mathf.Clamp(s.LanternScale.Value, 0.2f, 3f);
                if (!force && applied && along == aAlong && up == aUp && across == aAcross && tilt == aTilt && lean == aLean && flip == aFlip && scale == aScale)
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
                Vector3 poleScale = Vector3.one * scale;
                if (flip)
                {
                    poleAt.x = 2f * foot.x - poleRest.x;
                    poleScale.x = -scale;
                }
                // Turn the pole about its foot, scaled about the foot: a point of the child lands at foot + r*s*(point - foot).
                pole.localRotation = r * poleRotRest;
                pole.localScale = poleScale;
                pole.localPosition = foot + r * (scale * (poleAt - foot)) + offset;
                // The lamp rig (glow, lit glass, both lights) gets exactly the pole's transform: the same turn about the same
                // foot, the same mirror and the same scale. The game does the same: the walk animation rocks the pole's
                // sway node and the rig's (light_sway) by the identical angle about the identical pivot (GK2-lantern-facts),
                // and it works because the pole is an Object3D, i.e. baked vertical sprites, and the lit glass is a vertical
                // sprite too: the same kind of drawing under the same transform stays on top of it at any angle. (Moving the
                // rig without turning it, as this code did before, put a second lamp beside the pole's own at any tilt or
                // lean; only tilt 0 looked right. User tests 2026-09-29.)
                Vector3 rig = lightRest;
                Vector3 rigScale = lightScaleRest * scale;
                if (flip)
                {
                    rig.x = 2f * foot.x - lightRest.x;
                    rigScale.x = -rigScale.x;
                }
                light.localRotation = r * lightRotRest;
                light.localScale = rigScale;
                light.localPosition = foot + r * (scale * (rig - foot)) + offset;
                rigPosed = light.localPosition;
                rigRotPosed = light.localRotation;
                lastPolePos = pole.localPosition;
                lastPoleRot = pole.localRotation;
                lastLightPos = light.localPosition;
                aAlong = along; aUp = up; aAcross = across; aTilt = tilt; aLean = lean; aFlip = flip; aScale = scale;
                applied = true;
            }


            public string Summary()
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
}

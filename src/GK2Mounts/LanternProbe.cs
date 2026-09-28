using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace GK2Mounts
{
    // Investigation aid for moving and tilting the cart's lantern pole (Debug tab). The pole is two sibling nodes
    // under the donkey's animator root that share one pivot: donkey_cart_lantern_sway (the 3D pole mesh) and
    // light_sway (glow sprite, lit lantern-glass sprite, two Unity point lights). Their "_sway" names suggest the walk
    // animation swings them, which decides whether a rotation written by the mod survives the animator. This probe
    // samples the nodes at the end of every frame for a few seconds (after the animator has evaluated), then writes
    // BepInEx/GK2-lantern-facts.txt: the subtrees with every light and sprite, the pole mesh's extent relative to its
    // pivot (foot and head), where the lights sit, how the root mirrors when the donkey turns, what the animator
    // played, and how the pole and the keeper's body overlap on screen. Read-only: nothing is moved.
    internal static class LanternProbe
    {
        public static bool Running { get; private set; }
        public static string Status { get; private set; } = "";

        private static readonly string[] NodeNames =
        {
            "Base", "donkey_cart_lantern_sway", "light_sway", "donkey_cart_empty_sway", "donkey_cart_bars", "donkey_cart_wheel_f",
        };

        private sealed class Track
        {
            public Transform T;
            public Vector3 PosMin, PosMax, RotMin, RotMax, ScaleMin, ScaleMax;
            public Vector3 FirstPos, FirstRot;
            public int Frames, RotChanged, PosChanged;
            public readonly List<string> Samples = new List<string>();
        }

        public static string Start(float seconds)
        {
            if (Running)
            {
                return "already running";
            }
            if (Mount.Donkey == null || Mount.CurrentStyle != MountStyle.Cart)
            {
                return "mount in Cart style first";
            }
            if (Plugin.Instance == null)
            {
                return "plugin instance missing";
            }
            Plugin.Instance.StartCoroutine(Run(Mathf.Clamp(seconds, 1f, 15f)));
            return $"sampling for {seconds:0} s; ride around and turn once so both facings are seen";
        }

        private static IEnumerator Run(float seconds)
        {
            Running = true;
            Status = "sampling…";
            var tracks = new List<Track>();
            var clips = new Dictionary<string, int>();
            var facings = new Dictionary<string, int>();
            int frames = 0, movingFrames = 0;
            Wgo donkey = Mount.Donkey;
            Animator animator = null;
            try
            {
                foreach (string name in NodeNames)
                {
                    Transform t = FindTopLevel(donkey.transform, name);
                    if (t != null)
                    {
                        tracks.Add(new Track { T = t });
                    }
                }
                animator = donkey.GetComponentInChildren<Animator>(includeInactive: true);
            }
            catch (Exception e)
            {
                Status = "failed: " + e.Message;
                Running = false;
                yield break;
            }

            float until = Time.unscaledTime + seconds;
            Vector3 lastKeeper = MainGame.PlayerController != null ? MainGame.PlayerController.transform.position : Vector3.zero;
            while (Time.unscaledTime < until && Mount.Donkey != null)
            {
                yield return new WaitForEndOfFrame();
                try
                {
                    frames++;
                    PlayerController pc = MainGame.PlayerController;
                    bool moving = false;
                    if (pc != null)
                    {
                        Vector3 k = pc.transform.position;
                        moving = (k - lastKeeper).sqrMagnitude > 1e-6f;
                        lastKeeper = k;
                        string facing = pc.PlayerData.Direction.x < -0.05f ? "left" : "right";
                        facings.TryGetValue(facing, out int fc);
                        facings[facing] = fc + 1;
                    }
                    if (moving)
                    {
                        movingFrames++;
                    }
                    if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
                    {
                        AnimatorClipInfo[] info = animator.GetCurrentAnimatorClipInfo(0);
                        string clip = info.Length > 0 && info[0].clip != null ? info[0].clip.name : "(none)";
                        clips.TryGetValue(clip, out int c);
                        clips[clip] = c + 1;
                    }
                    foreach (Track tr in tracks)
                    {
                        if (tr.T == null || !tr.T)
                        {
                            continue;
                        }
                        Vector3 p = tr.T.localPosition;
                        Vector3 r = Wrap(tr.T.localEulerAngles);
                        Vector3 s = tr.T.localScale;
                        if (tr.Frames == 0)
                        {
                            tr.PosMin = tr.PosMax = tr.FirstPos = p;
                            tr.RotMin = tr.RotMax = tr.FirstRot = r;
                            tr.ScaleMin = tr.ScaleMax = s;
                        }
                        else
                        {
                            tr.PosMin = Vector3.Min(tr.PosMin, p); tr.PosMax = Vector3.Max(tr.PosMax, p);
                            tr.RotMin = Vector3.Min(tr.RotMin, r); tr.RotMax = Vector3.Max(tr.RotMax, r);
                            tr.ScaleMin = Vector3.Min(tr.ScaleMin, s); tr.ScaleMax = Vector3.Max(tr.ScaleMax, s);
                            if ((r - tr.FirstRot).sqrMagnitude > 0.01f * 0.01f) tr.RotChanged++;
                            if ((p - tr.FirstPos).sqrMagnitude > 0.001f * 0.001f) tr.PosChanged++;
                        }
                        tr.Frames++;
                        if (tr.Samples.Count < 12 && (tr.Frames == 1 || tr.Frames % Mathf.Max(1, Mathf.RoundToInt(seconds * 60f / 12f)) == 0))
                        {
                            tr.Samples.Add($"    f{Time.frameCount} {(moving ? "moving" : "still ")} lpos={p:F3} lrot={r:F1} lscale={s:F2} world={tr.T.position:F2} wrot={Wrap(tr.T.eulerAngles):F1}");
                        }
                    }
                }
                catch (Exception e)
                {
                    Status = "sampling error: " + e.Message;
                }
            }

            string result;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("GK2 Mounts lantern facts " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine($"[sampling] {frames} frames over {seconds:0} s, moving in {movingFrames}; facings: {Join(facings)}; animator clips seen: {Join(clips)}");
                sb.AppendLine("Rotation and position are read at the end of each frame, after the donkey's animator has evaluated, so a changing value here means the animator (or the game) writes it. The mod itself only writes the cart parts' local position (the ground lift).");
                sb.AppendLine();
                foreach (Track tr in tracks)
                {
                    if (tr.T == null || !tr.T)
                    {
                        continue;
                    }
                    sb.AppendLine($"[node] {Path(donkey.transform, tr.T)}  frames={tr.Frames}  rotation changed in {tr.RotChanged} frames, position changed in {tr.PosChanged} frames");
                    sb.AppendLine($"    lpos   min={tr.PosMin:F3} max={tr.PosMax:F3}");
                    sb.AppendLine($"    lrot   min={tr.RotMin:F1} max={tr.RotMax:F1}");
                    sb.AppendLine($"    lscale min={tr.ScaleMin:F2} max={tr.ScaleMax:F2}");
                    foreach (string line in tr.Samples)
                    {
                        sb.AppendLine(line);
                    }
                }
                sb.AppendLine();
                DumpStatic(donkey, sb);
                string path = System.IO.Path.Combine(Paths.BepInExRootPath, "GK2-lantern-facts.txt");
                File.WriteAllText(path, sb.ToString());
                result = "written " + path;
            }
            catch (Exception e)
            {
                result = "writing the facts failed: " + e.Message;
            }
            Status = result;
            Plugin.Log.LogInfo("[lantern] " + result);
            Running = false;
        }

        // Rest-state facts: the two subtrees with their lights and sprites, the pole's extent about its pivot, the
        // light positions relative to the pivot, how the root is mirrored right now, and the screen overlap with the
        // keeper's body.
        private static void DumpStatic(Wgo donkey, StringBuilder sb)
        {
            Transform root = donkey.transform;
            Transform pole = FindTopLevel(root, "donkey_cart_lantern_sway");
            Transform light = FindTopLevel(root, "light_sway");
            Transform baseT = FindTopLevel(root, "Base");
            PlayerController pc = MainGame.PlayerController;
            sb.AppendLine($"[facing] keeper direction {(pc != null ? pc.PlayerData.Direction.ToString("F2") : "?")}; donkey root lossyScale={root.lossyScale:F2}; Base lpos={(baseT ? baseT.localPosition.ToString("F3") : "?")} lrot={(baseT ? baseT.localEulerAngles.ToString("F0") : "?")} lscale={(baseT ? baseT.localScale.ToString("F2") : "?")} lossy={(baseT ? baseT.lossyScale.ToString("F2") : "?")}");
            sb.AppendLine();
            if (pole != null)
            {
                sb.AppendLine("[pole subtree] " + Path(root, pole));
                DumpTree(pole, pole, sb, 1);
                if (Cargo.MeasureMeshes(pole, pole, out Bounds inPivot))
                {
                    sb.AppendLine($"  pole meshes in the pivot node's space (x along, y up, z across; 0,0,0 = the pivot the animator would rotate about):");
                    sb.AppendLine($"    min={inPivot.min:F3} max={inPivot.max:F3} size={inPivot.size:F3}  -> foot at y={inPivot.min.y:F3}, head at y={inPivot.max.y:F3}, centre x={inPivot.center.x:F3} z={inPivot.center.z:F3}");
                }
                if (pole.parent != null && Cargo.MeasureMeshes(pole, pole.parent, out Bounds inParent))
                {
                    sb.AppendLine($"  pole meshes in {pole.parent.name}'s space with the pivot offset removed (the model itself): min={inParent.min:F3} max={inParent.max:F3}");
                }
                Renderer pr = pole.GetComponentInChildren<Renderer>(includeInactive: false);
                if (pr != null)
                {
                    sb.AppendLine($"  pole renderer world bounds: centre={pr.bounds.center:F3} size={pr.bounds.size:F3}");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("[pole subtree] donkey_cart_lantern_sway not found");
            }
            if (light != null)
            {
                sb.AppendLine("[light subtree] " + Path(root, light));
                DumpTree(light, light, sb, 1);
                foreach (Light l in light.GetComponentsInChildren<Light>(includeInactive: true))
                {
                    Vector3 rel = light.InverseTransformPoint(l.transform.position);
                    Vector3 relPole = pole != null ? pole.InverseTransformPoint(l.transform.position) : Vector3.zero;
                    sb.AppendLine($"  light '{l.name}': type={l.type} range={l.range:F2} intensity={l.intensity:F2} color={l.color} shadows={l.shadows} enabled={l.enabled} active={l.gameObject.activeInHierarchy} cullingMask={l.cullingMask} renderMode={l.renderMode}");
                    sb.AppendLine($"      position relative to light_sway pivot={rel:F3}, relative to the pole pivot={relPole:F3}, world={l.transform.position:F3}");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("[light subtree] light_sway not found");
            }

            // Screen overlap: where the pole and the keeper's body land on screen right now.
            try
            {
                Camera cam = CameraSystem.Instance != null ? CameraSystem.Instance.WorldCamera : Camera.main;
                SpriteRenderer body = pc != null ? FindBody(pc) : null;
                Renderer pr = pole != null ? pole.GetComponentInChildren<Renderer>(includeInactive: false) : null;
                if (cam != null && pr != null)
                {
                    Rect poleRect = ScreenRect(cam, pr.bounds);
                    sb.AppendLine($"[screen] camera '{cam.name}' {cam.pixelWidth}x{cam.pixelHeight}; pole rect x={poleRect.xMin:F0}..{poleRect.xMax:F0} y={poleRect.yMin:F0}..{poleRect.yMax:F0} (y up)");
                    if (body != null)
                    {
                        Rect bodyRect = ScreenRect(cam, body.bounds);
                        sb.AppendLine($"[screen] keeper body '{body.sprite?.name}' rect x={bodyRect.xMin:F0}..{bodyRect.xMax:F0} y={bodyRect.yMin:F0}..{bodyRect.yMax:F0}; overlap {(poleRect.Overlaps(bodyRect) ? "YES" : "no")}: x {Mathf.Max(poleRect.xMin, bodyRect.xMin):F0}..{Mathf.Min(poleRect.xMax, bodyRect.xMax):F0}, y {Mathf.Max(poleRect.yMin, bodyRect.yMin):F0}..{Mathf.Min(poleRect.yMax, bodyRect.yMax):F0}");
                        sb.AppendLine($"[screen] keeper body world bounds centre={body.bounds.center:F3} size={body.bounds.size:F3}; pole world centre={pr.bounds.center:F3}; pole minus body = {(pr.bounds.center - body.bounds.center):F3} (x along the screen, y up, z depth: negative = towards the camera)");
                        sb.AppendLine($"[screen] draw order: pole material queue={pr.sharedMaterial?.renderQueue}, body queue={body.sharedMaterial?.renderQueue}; body sortingOrder={body.sortingOrder}");
                    }
                }
            }
            catch (Exception e)
            {
                sb.AppendLine("[screen] failed: " + e.Message);
            }
        }

        private static void DumpTree(Transform t, Transform space, StringBuilder sb, int depth)
        {
            string pad = new string(' ', depth * 2);
            var comps = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform)
                {
                    continue;
                }
                switch (c)
                {
                    case SpriteRenderer sr:
                        comps.Add($"SpriteRenderer(sprite={sr.sprite?.name}, enabled={sr.enabled}, color={sr.color}, shader={sr.sharedMaterial?.shader?.name}, order={sr.sortingOrder})");
                        break;
                    case MeshFilter mf:
                        comps.Add($"MeshFilter(mesh={mf.sharedMesh?.name}, bounds size={(mf.sharedMesh != null ? mf.sharedMesh.bounds.size.ToString("F3") : "?")} centre={(mf.sharedMesh != null ? mf.sharedMesh.bounds.center.ToString("F3") : "?")})");
                        break;
                    case MeshRenderer mr:
                        comps.Add($"MeshRenderer(enabled={mr.enabled}, shader={mr.sharedMaterial?.shader?.name}, queue={mr.sharedMaterial?.renderQueue})");
                        break;
                    case Light l:
                        comps.Add($"Light({l.type}, range={l.range:F2}, intensity={l.intensity:F2}, enabled={l.enabled})");
                        break;
                    default:
                        comps.Add(c.GetType().Name);
                        break;
                }
            }
            sb.AppendLine($"{pad}- {t.name} [{(t.gameObject.activeSelf ? "on" : "off")}] layer={LayerMask.LayerToName(t.gameObject.layer)}({t.gameObject.layer}) lpos={t.localPosition:F3} lrot={Wrap(t.localEulerAngles):F1} lscale={t.localScale:F3} inPivotSpace={space.InverseTransformPoint(t.position):F3}{(comps.Count > 0 ? "  :: " + string.Join("; ", comps) : "")}");
            for (int i = 0; i < t.childCount; i++)
            {
                DumpTree(t.GetChild(i), space, sb, depth + 1);
            }
        }

        // The top-level occurrence of a prefab name: the prefab reuses names down the tree (each Object3D's mesh child
        // is named like its parent), so the shallowest match is the node the animator and the mod deal with.
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

        private static SpriteRenderer FindBody(PlayerController pc)
        {
            Animator animator = pc.View?.PlayerAnimation?.Animator;
            if (animator == null)
            {
                return null;
            }
            foreach (SpriteRenderer sr in animator.GetComponentsInChildren<SpriteRenderer>(includeInactive: false))
            {
                if (sr.name == "bdy")
                {
                    return sr;
                }
            }
            return null;
        }

        private static Rect ScreenRect(Camera cam, Bounds b)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                Vector3 s = cam.WorldToScreenPoint(corner);
                minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x);
                minY = Mathf.Min(minY, s.y); maxY = Mathf.Max(maxY, s.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static string Path(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (Transform p = t; p != null && p != root; p = p.parent)
            {
                parts.Insert(0, p.name);
            }
            return string.Join("/", parts);
        }

        private static Vector3 Wrap(Vector3 euler)
        {
            return new Vector3(Wrap(euler.x), Wrap(euler.y), Wrap(euler.z));
        }

        private static float Wrap(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            if (a < -180f) a += 360f;
            return a;
        }

        private static string Join(Dictionary<string, int> d)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in d)
            {
                parts.Add($"{kv.Key}×{kv.Value}");
            }
            return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
        }
    }
}

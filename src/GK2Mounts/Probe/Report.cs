using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace GK2MountProbe
{
    internal static class Report
    {
        public static readonly string OutputPath = System.IO.Path.Combine(Paths.BepInExRootPath, "GK2-mount-probe.txt");

        private static readonly List<Wgo> samples = new List<Wgo>();
        private static readonly Assembly GameAssembly = typeof(WgoData).Assembly;
        private static readonly Assembly LbtAssembly = typeof(LazyBearTechnology.GameKey).Assembly;

        public static IEnumerator WriteCoroutine()
        {
            StringBuilder sb = new StringBuilder(1 << 20);
            if (!Plugin.IsInGame(out PlayerController pc, out GameScene scene))
            {
                Plugin.Log.LogWarning("Report: not in game.");
                yield break;
            }
            Plugin.Log.LogInfo("Report: collecting...");

            Section(sb, "HEADER");
            sb.AppendLine($"time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"game version: {Application.version}  unity: {Application.unityVersion}");
            sb.AppendLine($"Assembly-CSharp MVID: {GameAssembly.ManifestModule.ModuleVersionId}");
            sb.AppendLine($"scene: {scene.Id}  player pos: {F(pc.PlayerData.position.Value)}  dir: {F(pc.PlayerData.Direction)}");
            sb.AppendLine($"fixedDeltaTime: {Time.fixedDeltaTime}  gravity: {F(Physics.gravity)}");

            Safe(sb, "CAMERA", () => DumpCamera(sb));
            Safe(sb, "PLAYER: controller and components", () => DumpPlayer(sb, pc));
            Safe(sb, "PLAYER: hierarchy", () => DumpHierarchy(sb, pc.transform));
            Safe(sb, "WGO DEFINITIONS", () =>
            {
                foreach (string id in SampleIdList())
                {
                    DumpWgoDef(sb, id);
                }
            });
            Safe(sb, "EXISTING WGOS IN THE WORLD", () =>
            {
                foreach (string id in SampleIdList())
                {
                    DumpExistingWgos(sb, id);
                }
            });

            // Spawn temp samples next to the player, give the views a moment to load their prefabs, then dump them.
            DestroySamples();
            List<string> ids = SampleIdList();
            Vector3 right = Vector3.Cross(Vector3.up, TempSpawner.AwayFromCamera()).normalized;
            Vector3 basePos = pc.PlayerData.position.Value;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                Vector3 pos = basePos + right * (Plugin.SampleSpacing.Value * (i + 1));
                try
                {
                    if (GameBalance.Me.GetData<WGODef>(id) == null)
                    {
                        sb.AppendLine($"[sample {id}] no WGODef with this id, skipped");
                        continue;
                    }
                    samples.Add(TempSpawner.Spawn(id, pos, scene, disableColliders: true));
                }
                catch (Exception e)
                {
                    sb.AppendLine($"[sample {id}] spawn FAILED: {e}");
                }
            }
            for (int f = 0; f < 90; f++)
            {
                yield return null;
            }
            for (int i = 0; i < samples.Count; i++)
            {
                Wgo wgo = samples[i];
                if (wgo == null)
                {
                    sb.AppendLine("[sample] view was destroyed before it could be dumped");
                    continue;
                }
                Safe(sb, $"TEMP SAMPLE: {wgo.Id}", () => DumpWgoView(sb, wgo));
            }

            Section(sb, "END");
            File.WriteAllText(OutputPath, sb.ToString());
            Plugin.Log.LogInfo($"Report written: {OutputPath} ({sb.Length} chars). Samples stay for {Plugin.SampleLifetimeSeconds.Value}s.");

            yield return new WaitForSeconds(Plugin.SampleLifetimeSeconds.Value);
            DestroySamples();
        }

        public static void DestroySamples()
        {
            for (int i = 0; i < samples.Count; i++)
            {
                Wgo w = samples[i];
                TempSpawner.Destroy(ref w);
            }
            samples.Clear();
        }

        private static List<string> SampleIdList()
        {
            List<string> list = new List<string>();
            foreach (string s in (Plugin.SampleIds.Value ?? "").Split(','))
            {
                string t = s.Trim();
                if (t.Length > 0 && !list.Contains(t))
                {
                    list.Add(t);
                }
            }
            return list;
        }

        // ---------------------------------------------------------------- sections

        private static void DumpCamera(StringBuilder sb)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                sb.AppendLine("Camera.main is null");
            }
            else
            {
                sb.AppendLine($"name: {cam.name}  pos: {F(cam.transform.position)}  euler: {F(cam.transform.eulerAngles)}  forward: {F(cam.transform.forward)}");
                sb.AppendLine($"orthographic: {cam.orthographic}  orthoSize: {cam.orthographicSize}  fov: {cam.fieldOfView}  near/far: {cam.nearClipPlane}/{cam.farClipPlane}  pixelRect: {cam.pixelRect}");
                sb.AppendLine($"transparencySortMode: {cam.transparencySortMode}  transparencySortAxis: {F(cam.transparencySortAxis)}  depth: {cam.depth}  cullingMask: {cam.cullingMask}");
                DumpComponentsShallow(sb, cam.gameObject, "  ");
            }
            sb.AppendLine($"VisualConsts.CAMERA_ANGLE={VisualConsts.CAMERA_ANGLE} rad ({VisualConsts.CAMERA_ANGLE * Mathf.Rad2Deg:F2} deg)  Y_TO_Z={VisualConsts.Y_TO_Z}  XYZ_STEP={F(VisualConsts.XYZ_STEP)}  GRAPHICAL_PIXEL_SIZE={VisualConsts.GRAPHICAL_PIXEL_SIZE}");
            try
            {
                sb.AppendLine($"MainCamera render type: {CameraSystem.Instance.MainCamera.GetRenderType()}");
            }
            catch (Exception e)
            {
                sb.AppendLine($"MainCamera render type: n/a ({e.Message})");
            }
        }

        private static void DumpPlayer(StringBuilder sb, PlayerController pc)
        {
            sb.AppendLine($"parent chain: {ParentChain(pc.transform)}");
            sb.AppendLine($"SSM state: {pc.Ssm?.CurState?.GetType().Name}  controlsEnabled: {pc.IsControlsEnabled}");
            sb.AppendLine($"charState: {pc.PlayerData.charState.Value}  isDirectionLocked: {pc.PlayerData.isDirectionLocked}  isWispEnabled: {pc.PlayerData.isWispEnabled}");
            PlayerPhysicalBody body = pc.PhysicalBody;
            sb.AppendLine($"SpeedMultiplier: {body.SpeedMultiplier}  IsMuted: {body.IsMuted}");
            sb.AppendLine("PlayerPhysicsConfig:");
            DumpFields(sb, body.PhysicsConfig, "  ");
            sb.AppendLine("PlayerController fields:");
            DumpFields(sb, pc, "  ");
            sb.AppendLine("PlayerPhysicalBody fields:");
            DumpFields(sb, body, "  ");
            sb.AppendLine("PlayerView fields:");
            DumpFields(sb, pc.View, "  ");
            sb.AppendLine("PlayerAnimation fields (incl. AnimationComponentBase):");
            DumpFields(sb, pc.View.PlayerAnimation, "  ");
            sb.AppendLine("Player animator:");
            DumpAnimator(sb, pc.View.PlayerAnimation.Animator, "  ");
            sb.AppendLine("Player sprite renderers (PlayerAnimation list order):");
            SpriteRenderer[] srs = pc.View.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            foreach (SpriteRenderer sr in srs)
            {
                sb.AppendLine("  " + Path(sr.transform, pc.transform) + " :: " + SpriteInfo(sr));
            }
        }

        private static void DumpWgoDef(StringBuilder sb, string id)
        {
            WGODef def = GameBalance.Me.GetData<WGODef>(id);
            if (def == null)
            {
                sb.AppendLine($"[{id}] no WGODef");
                return;
            }
            sb.AppendLine($"[{id}] WGODef:");
            DumpFields(sb, def, "  ", includePrivate: false);
            try
            {
                sb.AppendLine($"  ResolveAssetId -> {def.ResolveAssetId(id)}");
                sb.AppendLine($"  OpenInMultiInventory: {def.OpenInMultiInventory}  MasteryLock: {def.MasteryLock}");
            }
            catch (Exception e)
            {
                sb.AppendLine($"  (derived values failed: {e.Message})");
            }
        }

        private static void DumpExistingWgos(StringBuilder sb, string id)
        {
            List<WgoData> list = MainGame.WorldData.GetWgoDataList(id);
            sb.AppendLine($"[{id}] {list?.Count ?? 0} instance(s) in WorldData");
            if (list == null)
            {
                return;
            }
            foreach (WgoData d in list)
            {
                DumpWgoData(sb, d, "  ");
                Wgo view = GameScene.GetWgoViewGlobal(d.UniqueId);
                if (view != null)
                {
                    sb.AppendLine("  view present in loaded scene:");
                    DumpWgoView(sb, view, "    ");
                }
                else
                {
                    sb.AppendLine("  no view in loaded scene (other scene, unloaded chunk or hidden)");
                }
            }
        }

        private static void DumpWgoData(StringBuilder sb, WgoData d, string ind)
        {
            sb.AppendLine($"{ind}uid={d.UniqueId} pos={F(d.Position)} scale={F(d.Scale)} world={d.WorldId} zone={d.WorldZoneData?.id ?? "none"} hidden={d.IsHidden} temp={d.isTempObject} dir={F(d.MovableDirection)} customTag={d.CustomTag}");
            sb.AppendLine($"{ind}moving={d.MovementComponent?.IsMoving} hp={d.HpComponent?.Hp}/{d.HpComponent?.MaxHpValue}");
            WgoPartData m = d.MainWgoPartData;
            if (m != null)
            {
                sb.AppendLine($"{ind}main part: id={m.id} variation={m.variationId} rotation={m.rotationIndex} availableVariations={Variations(m)}");
            }
            if (d.AdditionalWgoPartsData != null)
            {
                foreach (WgoPartData p in d.AdditionalWgoPartsData)
                {
                    sb.AppendLine($"{ind}additional part: id={p.id} variation={p.variationId} rotation={p.rotationIndex} availableVariations={Variations(p)}");
                }
            }
        }

        private static string Variations(WgoPartData p)
        {
            if (p.AvailableVariations == null)
            {
                return "null";
            }
            List<string> parts = new List<string>();
            foreach (WgoPartStateData v in p.AvailableVariations)
            {
                parts.Add($"{v.id}/{v.rotationIndex}{(v.mirror ? "/mirror" : "")}");
            }
            return "[" + string.Join(", ", parts) + "]";
        }

        private static void DumpWgoView(StringBuilder sb, Wgo wgo, string ind = "")
        {
            DumpWgoData(sb, wgo.Data, ind);
            sb.AppendLine($"{ind}view: active={wgo.gameObject.activeInHierarchy} pos={F(wgo.transform.position)} UpdatePosByData={wgo.UpdatePosByData} mainPart={(wgo.MainWgoPart ? wgo.MainWgoPart.name : "null")} additionalParts={wgo.AdditionalWgoParts?.Count}");
            WgoPart part = wgo.MainWgoPart;
            if (part != null)
            {
                sb.AppendLine($"{ind}main part variations (WgoPartState list):");
                if (part.Variations != null)
                {
                    foreach (WgoPartState st in part.Variations)
                    {
                        sb.AppendLine($"{ind}  variationId={st.variationId} rotation={st.rotationIndex} mirror={st.mirror} isDefault={st.isDefault} go={(st.gameObject ? st.gameObject.name + (st.gameObject.activeSelf ? " (active)" : " (inactive)") : "null")}");
                    }
                }
                sb.AppendLine($"{ind}main part AnimationComponent: {(part.AnimationComponent ? part.AnimationComponent.GetType().Name : "null")}  Object3D: {(part.Object3D ? part.Object3D.name : "null")}  interactableColliders={part.InteractableColliders?.Count}");
                if (part.AnimationComponent != null)
                {
                    DumpFields(sb, part.AnimationComponent, ind + "  ");
                    DumpAnimator(sb, part.AnimationComponent.Animator, ind + "  ");
                }
            }
            sb.AppendLine($"{ind}hierarchy:");
            DumpHierarchy(sb, wgo.transform, ind + "  ");
        }

        // ---------------------------------------------------------------- generic dumpers

        private static void DumpHierarchy(StringBuilder sb, Transform root, string ind = "")
        {
            int count = 0;
            DumpTransform(sb, root, ind, 0, ref count);
            if (count >= 3000)
            {
                sb.AppendLine(ind + "... truncated at 3000 nodes");
            }
        }

        private static void DumpTransform(StringBuilder sb, Transform t, string ind, int depth, ref int count)
        {
            if (++count > 3000 || depth > 14)
            {
                return;
            }
            sb.AppendLine($"{ind}- {t.name} [{(t.gameObject.activeSelf ? "on" : "OFF")}] layer={LayerMask.LayerToName(t.gameObject.layer)}({t.gameObject.layer}) lpos={F(t.localPosition)} lrot={F(t.localEulerAngles)} lscale={F(t.localScale)} wpos={F(t.position)}");
            DumpComponentsShallow(sb, t.gameObject, ind + "    ");
            for (int i = 0; i < t.childCount; i++)
            {
                DumpTransform(sb, t.GetChild(i), ind + "  ", depth + 1, ref count);
            }
        }

        private static void DumpComponentsShallow(StringBuilder sb, GameObject go, string ind)
        {
            Component[] comps = go.GetComponents<Component>();
            foreach (Component c in comps)
            {
                if (c == null)
                {
                    sb.AppendLine(ind + "(missing script)");
                    continue;
                }
                if (c is Transform)
                {
                    continue;
                }
                try
                {
                    DumpComponent(sb, c, ind);
                }
                catch (Exception e)
                {
                    sb.AppendLine($"{ind}{c.GetType().Name}: dump failed ({e.Message})");
                }
            }
        }

        private static void DumpComponent(StringBuilder sb, Component c, string ind)
        {
            Type type = c.GetType();
            Behaviour beh = c as Behaviour;
            string en = beh != null ? (beh.enabled ? "" : " (disabled)") : "";
            switch (c)
            {
                case SpriteRenderer sr:
                    sb.AppendLine($"{ind}SpriteRenderer{en}: {SpriteInfo(sr)}");
                    return;
                case Animator an:
                    sb.AppendLine($"{ind}Animator{en}:");
                    DumpAnimator(sb, an, ind + "  ");
                    return;
                case Rigidbody rb:
                    sb.AppendLine($"{ind}Rigidbody: mass={rb.mass} linDamp={rb.linearDamping} angDamp={rb.angularDamping} gravity={rb.useGravity} kinematic={rb.isKinematic} interp={rb.interpolation} ccd={rb.collisionDetectionMode} constraints={rb.constraints} vel={F(rb.linearVelocity)}");
                    return;
                case CapsuleCollider cc:
                    sb.AppendLine($"{ind}CapsuleCollider{(cc.enabled ? "" : " (disabled)")}: trigger={cc.isTrigger} center={F(cc.center)} radius={cc.radius} height={cc.height} dir={cc.direction} material={(cc.sharedMaterial ? cc.sharedMaterial.name : "none")}");
                    return;
                case BoxCollider bc:
                    sb.AppendLine($"{ind}BoxCollider{(bc.enabled ? "" : " (disabled)")}: trigger={bc.isTrigger} center={F(bc.center)} size={F(bc.size)} material={(bc.sharedMaterial ? bc.sharedMaterial.name : "none")}");
                    return;
                case SphereCollider sc:
                    sb.AppendLine($"{ind}SphereCollider{(sc.enabled ? "" : " (disabled)")}: trigger={sc.isTrigger} center={F(sc.center)} radius={sc.radius}");
                    return;
                case MeshCollider mc:
                    sb.AppendLine($"{ind}MeshCollider{(mc.enabled ? "" : " (disabled)")}: trigger={mc.isTrigger} convex={mc.convex} mesh={(mc.sharedMesh ? mc.sharedMesh.name : "none")} bounds={F(mc.bounds.size)} material={(mc.sharedMaterial ? mc.sharedMaterial.name : "none")}");
                    return;
                case Collider col:
                    sb.AppendLine($"{ind}{type.Name}{(col.enabled ? "" : " (disabled)")}: trigger={col.isTrigger} bounds={F(col.bounds.size)}");
                    return;
                case SortingGroup sg:
                    sb.AppendLine($"{ind}SortingGroup{en}: layer={sg.sortingLayerName} order={sg.sortingOrder}");
                    return;
                case MeshFilter mf:
                    sb.AppendLine($"{ind}MeshFilter: mesh={(mf.sharedMesh ? mf.sharedMesh.name : "none")} verts={(mf.sharedMesh ? mf.sharedMesh.vertexCount : 0)} bounds={(mf.sharedMesh ? F(mf.sharedMesh.bounds.size) : "-")}");
                    return;
                case Renderer r:
                    sb.AppendLine($"{ind}{type.Name}{(r.enabled ? "" : " (disabled)")}: sortLayer={r.sortingLayerName} order={r.sortingOrder} shader={ShaderName(r)} bounds={F(r.bounds.size)}");
                    return;
            }
            if (type.Assembly == GameAssembly || type.Assembly == LbtAssembly)
            {
                sb.AppendLine($"{ind}{type.Name}{en}:");
                DumpFields(sb, c, ind + "  ");
            }
            else
            {
                sb.AppendLine($"{ind}{type.FullName}{en}");
            }
        }

        private static void DumpAnimator(StringBuilder sb, Animator a, string ind)
        {
            if (a == null)
            {
                sb.AppendLine(ind + "animator: null");
                return;
            }
            RuntimeAnimatorController rc = a.runtimeAnimatorController;
            sb.AppendLine($"{ind}controller={(rc ? rc.name : "null")} type={(rc ? rc.GetType().Name : "-")} enabled={a.enabled} active={a.isActiveAndEnabled} speed={a.speed} culling={a.cullingMode} updateMode={a.updateMode} rootMotion={a.applyRootMotion} keepOnDisable={a.keepAnimatorStateOnDisable} avatar={(a.avatar ? a.avatar.name : "none")}");
            if (rc != null)
            {
                AnimatorOverrideController oc = rc as AnimatorOverrideController;
                if (oc != null)
                {
                    sb.AppendLine($"{ind}override of: {(oc.runtimeAnimatorController ? oc.runtimeAnimatorController.name : "null")}");
                }
                AnimationClip[] clips = rc.animationClips;
                sb.AppendLine($"{ind}clips ({clips.Length}):");
                HashSet<string> seen = new HashSet<string>();
                foreach (AnimationClip clip in clips)
                {
                    if (clip == null || !seen.Add(clip.name))
                    {
                        continue;
                    }
                    sb.AppendLine($"{ind}  {clip.name}  len={clip.length:F3}s fps={clip.frameRate} loop={clip.isLooping} events={clip.events.Length}");
                }
            }
            if (!a.isActiveAndEnabled || rc == null)
            {
                return;
            }
            sb.AppendLine($"{ind}parameters ({a.parameterCount}):");
            foreach (AnimatorControllerParameter p in a.parameters)
            {
                string v = p.type switch
                {
                    AnimatorControllerParameterType.Float => a.GetFloat(p.nameHash).ToString("F3"),
                    AnimatorControllerParameterType.Int => a.GetInteger(p.nameHash).ToString(),
                    AnimatorControllerParameterType.Bool => a.GetBool(p.nameHash).ToString(),
                    _ => "(trigger)",
                };
                sb.AppendLine($"{ind}  {p.type} {p.name} = {v}");
            }
            sb.AppendLine($"{ind}layers ({a.layerCount}):");
            for (int i = 0; i < a.layerCount; i++)
            {
                AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(i);
                AnimatorClipInfo[] ci = a.GetCurrentAnimatorClipInfo(i);
                string clipName = ci.Length > 0 && ci[0].clip != null ? ci[0].clip.name : "-";
                sb.AppendLine($"{ind}  [{i}] {a.GetLayerName(i)} weight={a.GetLayerWeight(i):F2} stateHash={st.shortNameHash} loop={st.loop} nTime={st.normalizedTime:F2} clip={clipName}");
            }
        }

        // Instance fields of game/LBT types: public ones and private [SerializeField] ones, formatted shallowly.
        private static void DumpFields(StringBuilder sb, object o, string ind, bool includePrivate = true)
        {
            if (o == null)
            {
                sb.AppendLine(ind + "null");
                return;
            }
            Type t = o.GetType();
            while (t != null && t != typeof(object) && t != typeof(MonoBehaviour) && t != typeof(Behaviour) && t != typeof(Component) && t != typeof(ScriptableObject) && t != typeof(UnityEngine.Object))
            {
                FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo f in fields)
                {
                    if (typeof(Delegate).IsAssignableFrom(f.FieldType))
                    {
                        continue;
                    }
                    bool serialized = f.IsPublic || f.GetCustomAttribute<SerializeField>() != null;
                    if (!serialized && !(includePrivate && IsInteresting(f)))
                    {
                        continue;
                    }
                    if (!f.IsPublic && !includePrivate)
                    {
                        continue;
                    }
                    object v;
                    try
                    {
                        v = f.GetValue(o);
                    }
                    catch (Exception e)
                    {
                        v = "(error " + e.GetType().Name + ")";
                    }
                    sb.AppendLine($"{ind}{(t == o.GetType() ? "" : t.Name + ".")}{f.Name} = {F(v)}");
                }
                t = t.BaseType;
            }
        }

        // A few private, non-serialized fields worth seeing anyway.
        private static bool IsInteresting(FieldInfo f)
        {
            string n = f.Name;
            return n == "steppedRotationPreset" || n == "animationState" || n == "isOnGround" || n == "isMoving" || n == "isMovementLocked"
                   || n == "cachedDirectionAngle" || n == "pixelSizeForPosRounding" || n == "isInitialized" || n == "skinPreset";
        }

        // ---------------------------------------------------------------- formatting

        internal static string F(object v)
        {
            switch (v)
            {
                case null: return "null";
                case string s: return "\"" + s + "\"";
                case float fl: return fl.ToString("0.###");
                case double d: return d.ToString("0.###");
                case Vector3 v3: return $"({v3.x:0.###}, {v3.y:0.###}, {v3.z:0.###})";
                case Vector2 v2: return $"({v2.x:0.###}, {v2.y:0.###})";
                case Quaternion q: return $"euler{F(q.eulerAngles)}";
                case Color c: return $"rgba({c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##})";
                case Enum e: return e.ToString();
                case UnityEngine.Object uo: return uo ? $"{uo.GetType().Name}[{uo.name}]" : "null(Object)";
                case LazyExpression le: return "expr\"" + SafeToString(le) + "\"";
                case IList list:
                {
                    StringBuilder b = new StringBuilder();
                    b.Append($"{v.GetType().Name}(count {list.Count})[");
                    int n = Math.Min(list.Count, 12);
                    for (int i = 0; i < n; i++)
                    {
                        if (i > 0) b.Append(", ");
                        b.Append(F(list[i]));
                    }
                    if (list.Count > n) b.Append(", ...");
                    b.Append("]");
                    return b.ToString();
                }
            }
            Type t = v.GetType();
            if (t.IsPrimitive || v is decimal)
            {
                return v.ToString();
            }
            if (t.Assembly == GameAssembly || t.Assembly == LbtAssembly)
            {
                string s = SafeToString(v);
                return s == t.FullName || s == t.Name ? t.Name + "{...}" : t.Name + "{" + s + "}";
            }
            return t.Name;
        }

        private static string SafeToString(object o)
        {
            try
            {
                return o.ToString();
            }
            catch
            {
                return o.GetType().Name;
            }
        }

        private static string SpriteInfo(SpriteRenderer sr)
        {
            Sprite sp = sr.sprite;
            string spInfo = sp ? $"sprite={sp.name} tex={(sp.texture ? sp.texture.name : "-")} rect={sp.rect.width}x{sp.rect.height} pivot={F(sp.pivot)} ppu={sp.pixelsPerUnit} boundsSize={F(sp.bounds.size)}" : "sprite=null";
            return $"{spInfo} enabled={sr.enabled} sortLayer={sr.sortingLayerName} order={sr.sortingOrder} flipX={sr.flipX} flipY={sr.flipY} color={F(sr.color)} drawMode={sr.drawMode} maskInteraction={sr.maskInteraction} shader={ShaderName(sr)} rendererBounds={F(sr.bounds.size)}";
        }

        private static string ShaderName(Renderer r)
        {
            Material m = r.sharedMaterial;
            return m ? (m.name + "/" + (m.shader ? m.shader.name : "?") + " q=" + m.renderQueue) : "none";
        }

        private static string ParentChain(Transform t)
        {
            List<string> names = new List<string>();
            Transform p = t.parent;
            while (p != null)
            {
                names.Insert(0, p.name);
                p = p.parent;
            }
            return names.Count == 0 ? "(root)" : string.Join(" / ", names);
        }

        private static string Path(Transform t, Transform stopAt)
        {
            List<string> names = new List<string>();
            Transform p = t;
            while (p != null && p != stopAt)
            {
                names.Insert(0, p.name);
                p = p.parent;
            }
            return string.Join("/", names);
        }

        private static void Section(StringBuilder sb, string title)
        {
            sb.AppendLine();
            sb.AppendLine("==================================================================");
            sb.AppendLine("== " + title);
            sb.AppendLine("==================================================================");
        }

        private static void Safe(StringBuilder sb, string title, Action a)
        {
            Section(sb, title);
            try
            {
                a();
            }
            catch (Exception e)
            {
                sb.AppendLine($"!! section failed: {e}");
                Plugin.Log.LogWarning($"Report section '{title}' failed: {e.Message}");
            }
        }
    }
}

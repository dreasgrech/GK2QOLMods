using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace GK2Mounts
{
    // The cart lantern's lamp hangs straight from its hook (Round 13 of PROBE-FINDINGS). The pole is an Object3D: its
    // wooden shaft and its iron lamp are separate baked sprites, but merged into one mesh
    // (donkey_cart_lantern-MERGE S_OBJ), so the game can only turn them together. That mesh is not readable at runtime,
    // so its vertex and index buffers ship with the mod (assets/, read from the game's bundle); on attach they are
    // checked against the live mesh (vertex count, submeshes, bounds) and split into "pole without the lamp", which
    // replaces our donkey's mesh (only this MeshFilter; the game's asset is untouched), and "lamp only" on a new
    // renderer with the same materials. Every frame the lamp, and the lamp rig (lit glass, glow, lights) with it, are
    // turned about the hook so the lamp's up is the world's up, whatever turns the pole: the slope pitch, the walk
    // rock, the Lantern tab's tilt, lean and sway. The rig is computed from Lantern's own stored pose, so nothing
    // accumulates. Any mismatch (a game update to the model) leaves the lantern as it was.
    internal static class LampHang
    {
        private const int VertexCount = 135;
        private const int Stride = 32;
        private static readonly int[] SubStart = { 0, 144 };
        private static readonly int[] SubCount = { 144, 51 };
        private static readonly Vector3 BoundsCenter = new Vector3(-0.1f, 0.8166633f, -0.0375f);
        private static readonly Vector3 BoundsExtent = new Vector3(0.26f, 0.5166646f, 0.1375f);

        private static MeshFilter mf;
        private static MeshRenderer mr;
        private static Mesh original, poleMesh, lampMesh;
        private static GameObject lampGo;
        private static MeshRenderer lampMr;
        private static Vector3 hookLocal;
        private static MaterialPropertyBlock mpb;
        private static bool failed;
        private static Material[] copiedMaterials;

        public static bool Active => lampGo != null && mf != null && mf;
        public static string Note { get; private set; } = "off";

        // Called by Lantern.Detach (before the pole is measured again) and when the setting is switched off.
        public static void Detach()
        {
            try
            {
                if (mf != null && mf && original != null)
                {
                    mf.sharedMesh = original;
                }
                if (lampGo != null)
                {
                    UnityEngine.Object.Destroy(lampGo);
                }
                if (poleMesh != null) UnityEngine.Object.Destroy(poleMesh);
                if (lampMesh != null) UnityEngine.Object.Destroy(lampMesh);
            }
            catch
            {
                // the donkey may be half destroyed
            }
            mf = null;
            mr = null;
            original = poleMesh = lampMesh = null;
            lampGo = null;
            lampMr = null;
            copiedMaterials = null;
            failed = false;
            Note = "off";
        }

        // Builds the split for this pole once; false (and a note) when anything does not match.
        public static bool Attach(Transform pole)
        {
            if (Active)
            {
                return true;
            }
            if (failed)
            {
                return false;
            }
            try
            {
                foreach (MeshFilter f in pole.GetComponentsInChildren<MeshFilter>(includeInactive: true))
                {
                    if (f.sharedMesh != null && f.sharedMesh.name.StartsWith("donkey_cart_lantern", StringComparison.Ordinal))
                    {
                        mf = f;
                        break;
                    }
                }
                mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
                if (mf == null || mr == null)
                {
                    return Fail("the lantern mesh was not found");
                }
                original = mf.sharedMesh;
                string mismatch = Fingerprint(original);
                if (mismatch != null)
                {
                    return Fail("the lantern model differs from the one the mod knows (" + mismatch + "); the lamp keeps turning with the pole");
                }
                byte[] vertices = Resource("GK2Mounts.lantern-vertices.bin");
                byte[] indexBytes = Resource("GK2Mounts.lantern-indices.bin");
                if (vertices == null || vertices.Length != VertexCount * Stride || indexBytes == null || indexBytes.Length != (SubCount[0] + SubCount[1]) * 2)
                {
                    return Fail("the mod's copy of the lantern mesh is missing or damaged");
                }
                var positions = new Vector3[VertexCount];
                for (int i = 0; i < VertexCount; i++)
                {
                    positions[i] = new Vector3(BitConverter.ToSingle(vertices, i * Stride), BitConverter.ToSingle(vertices, i * Stride + 4), BitConverter.ToSingle(vertices, i * Stride + 8));
                }
                var indices = new ushort[indexBytes.Length / 2];
                for (int i = 0; i < indices.Length; i++)
                {
                    indices[i] = BitConverter.ToUInt16(indexBytes, i * 2);
                }
                if (!Classify(positions, indices, out bool[] lampTri, out Vector3 hook, out int lampPieces, out int polePieces))
                {
                    return Fail($"the lamp could not be told apart from the pole ({lampPieces} lamp pieces, {polePieces} pole pieces)");
                }
                hookLocal = hook;
                poleMesh = Build("donkey_cart_lantern pole (mod)", vertices, indices, lampTri, keepLamp: false);
                lampMesh = Build("donkey_cart_lantern lamp (mod)", vertices, indices, lampTri, keepLamp: true);
                mf.sharedMesh = poleMesh;

                lampGo = new GameObject("gk2_hanging_lamp");
                lampGo.layer = mf.gameObject.layer;
                lampGo.transform.SetParent(mf.transform, worldPositionStays: false);
                lampGo.AddComponent<MeshFilter>().sharedMesh = lampMesh;
                lampMr = lampGo.AddComponent<MeshRenderer>();
                lampMr.shadowCastingMode = mr.shadowCastingMode;
                lampMr.receiveShadows = mr.receiveShadows;
                lampMr.lightProbeUsage = mr.lightProbeUsage;
                lampMr.reflectionProbeUsage = mr.reflectionProbeUsage;
                lampMr.renderingLayerMask = mr.renderingLayerMask;
                lampMr.sortingLayerID = mr.sortingLayerID;
                lampMr.sortingOrder = mr.sortingOrder;
                lampMr.allowOcclusionWhenDynamic = mr.allowOcclusionWhenDynamic;
                lampMr.motionVectorGenerationMode = mr.motionVectorGenerationMode;
                CopyLook();
                Note = $"lamp hangs from its hook at {hookLocal:F2} (mesh space); {lampPieces} lamp pieces split from {polePieces} pole pieces";
                Plugin.Log.LogInfo("LampHang: " + Note + ".");
                return true;
            }
            catch (Exception e)
            {
                Detach();
                return Fail("setting up failed: " + e.Message);
            }
        }

        private static bool Fail(string why)
        {
            if (mf != null && mf && original != null)
            {
                mf.sharedMesh = original;
            }
            failed = true;
            Note = why;
            Plugin.Log.LogWarning("LampHang: " + why + ".");
            return false;
        }

        private static string Fingerprint(Mesh m)
        {
            if (m.vertexCount != VertexCount) return $"{m.vertexCount} vertices";
            if (m.subMeshCount != 2) return $"{m.subMeshCount} submeshes";
            for (int i = 0; i < 2; i++)
            {
                SubMeshDescriptor d = m.GetSubMesh(i);
                if (d.indexStart != SubStart[i] || d.indexCount != SubCount[i]) return $"submesh {i} indices {d.indexStart}+{d.indexCount}";
            }
            if ((m.bounds.center - BoundsCenter).magnitude > 0.001f || (m.bounds.extents - BoundsExtent).magnitude > 0.001f) return $"bounds {m.bounds.center:F3} {m.bounds.extents:F3}";
            return null;
        }

        private static byte[] Resource(string name)
        {
            using (Stream s = typeof(LampHang).Assembly.GetManifestResourceStream(name))
            {
                if (s == null)
                {
                    return null;
                }
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // The mesh is 35 separate baked sprites. A piece (connected triangles) is lamp when it lies wholly in the lamp's
        // region, measured from the game's data (Round 13): max x at or below -0.07 and y within 0.50..1.21; every other
        // piece reaches x above -0.07 (the shaft) or y above 1.21 (the arm). The hook is the lamp piece reaching highest.
        private static bool Classify(Vector3[] p, ushort[] idx, out bool[] lampTri, out Vector3 hook, out int lampPieces, out int polePieces)
        {
            int tris = idx.Length / 3;
            var parent = new int[p.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            for (int t = 0; t < tris; t++)
            {
                int a = Find(idx[3 * t]);
                parent[Find(idx[3 * t + 1])] = a;
                parent[Find(idx[3 * t + 2])] = a;
            }
            var minX = new Dictionary<int, float>(); var maxX = new Dictionary<int, float>();
            var minY = new Dictionary<int, float>(); var maxY = new Dictionary<int, float>();
            for (int t = 0; t < tris; t++)
            {
                int r = Find(idx[3 * t]);
                for (int k = 0; k < 3; k++)
                {
                    Vector3 v = p[idx[3 * t + k]];
                    minX[r] = minX.TryGetValue(r, out float a) ? Mathf.Min(a, v.x) : v.x;
                    maxX[r] = maxX.TryGetValue(r, out float b) ? Mathf.Max(b, v.x) : v.x;
                    minY[r] = minY.TryGetValue(r, out float c) ? Mathf.Min(c, v.y) : v.y;
                    maxY[r] = maxY.TryGetValue(r, out float d) ? Mathf.Max(d, v.y) : v.y;
                }
            }
            var isLamp = new Dictionary<int, bool>();
            int hookPiece = -1;
            lampPieces = polePieces = 0;
            foreach (int r in maxX.Keys)
            {
                bool lamp = maxX[r] <= -0.07f && minY[r] >= 0.50f && maxY[r] <= 1.21f;
                isLamp[r] = lamp;
                if (lamp)
                {
                    lampPieces++;
                    if (hookPiece < 0 || maxY[r] > maxY[hookPiece]) hookPiece = r;
                }
                else
                {
                    polePieces++;
                }
            }
            lampTri = new bool[tris];
            for (int t = 0; t < tris; t++) lampTri[t] = isLamp[Find(idx[3 * t])];
            hook = Vector3.zero;
            if (hookPiece >= 0)
            {
                var seen = new HashSet<int>();
                Vector3 sum = Vector3.zero;
                for (int t = 0; t < tris; t++)
                {
                    if (Find(idx[3 * t]) != hookPiece) continue;
                    for (int k = 0; k < 3; k++) if (seen.Add(idx[3 * t + k])) sum += p[idx[3 * t + k]];
                }
                sum /= Mathf.Max(1, seen.Count);
                hook = new Vector3(sum.x, maxY[hookPiece], sum.z);
            }
            return lampPieces >= 5 && polePieces >= 5 && hookPiece >= 0;
        }

        // A mesh with the game's exact vertex buffer (position, normal, colour, UV, 32 bytes each, unchanged) and only
        // the lamp's or only the pole's triangles, keeping the two submeshes (two materials).
        private static Mesh Build(string name, byte[] vertices, ushort[] idx, bool[] lampTri, bool keepLamp)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertexBufferParams(VertexCount,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float16, 4),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length);
            var kept = new List<ushort>(idx.Length);
            var counts = new int[2];
            for (int s = 0; s < 2; s++)
            {
                int first = SubStart[s] / 3, n = SubCount[s] / 3;
                for (int t = first; t < first + n; t++)
                {
                    if (lampTri[t] != keepLamp) continue;
                    kept.Add(idx[3 * t]); kept.Add(idx[3 * t + 1]); kept.Add(idx[3 * t + 2]);
                    counts[s] += 3;
                }
            }
            mesh.SetIndexBufferParams(kept.Count, IndexFormat.UInt16);
            mesh.SetIndexBufferData(kept.ToArray(), 0, 0, kept.Count);
            mesh.subMeshCount = 2;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, counts[0]));
            mesh.SetSubMesh(1, new SubMeshDescriptor(counts[0], counts[1]));
            mesh.RecalculateBounds();
            return mesh;
        }

        // The original renderer's look on the lamp: materials (Object3DSubMesh may swap them) and every property block
        // (textures, LUT, transparency when the keeper is behind it, selection tint, wind), and its enabled state.
        private static void CopyLook()
        {
            Material[] mats = mr.sharedMaterials;
            if (copiedMaterials == null || copiedMaterials.Length != mats.Length || !SameMaterials(copiedMaterials, mats))
            {
                lampMr.sharedMaterials = mats;
                copiedMaterials = mats;
            }
            if (mpb == null)
            {
                mpb = new MaterialPropertyBlock();
            }
            mr.GetPropertyBlock(mpb);
            lampMr.SetPropertyBlock(mpb);
            for (int i = 0; i < mats.Length; i++)
            {
                mr.GetPropertyBlock(mpb, i);
                lampMr.SetPropertyBlock(mpb, i);
            }
            lampMr.enabled = mr.enabled;
        }

        private static bool SameMaterials(Material[] a, Material[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // Each frame, after the pole and the rig have their pose (Lantern.Tick): the turn about the hook that brings the
        // lamp's up to the world's up, applied to the lamp (a child of the pole's mesh node) and to the rig node.
        public static void Tick(Transform rigNode, Vector3 rigPos, Quaternion rigRot)
        {
            if (!Active || lampMr == null || rigNode == null || rigNode.parent == null)
            {
                return;
            }
            Matrix4x4 m = mf.transform.localToWorldMatrix;
            Vector3 h = m.MultiplyPoint3x4(hookLocal);
            Vector3 up = m.MultiplyVector(Vector3.up).normalized;
            Quaternion d = Quaternion.FromToRotation(up, Vector3.up);
            Matrix4x4 c = Matrix4x4.Translate(h) * Matrix4x4.Rotate(d) * Matrix4x4.Translate(-h);
            Matrix4x4 lampLocal = m.inverse * c * m;
            lampGo.transform.localPosition = lampLocal.GetColumn(3);
            lampGo.transform.localRotation = lampLocal.rotation;
            lampGo.transform.localScale = Vector3.one;
            Matrix4x4 p = rigNode.parent.localToWorldMatrix;
            Matrix4x4 q = p.inverse * c * p;
            rigNode.localPosition = q.MultiplyPoint3x4(rigPos);
            rigNode.localRotation = q.rotation * rigRot;
            LastTurn = Quaternion.Angle(Quaternion.identity, d);
            CopyLook();
        }

        public static float LastTurn { get; private set; }
    }
}

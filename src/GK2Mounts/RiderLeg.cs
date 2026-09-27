using System;
using System.Collections.Generic;
using UnityEngine;

namespace GK2Mounts
{
    // The rider's legs, drawn in front of the donkey while the keeper's own (baked-in) legs are hidden inside it.
    //
    // Source "Body" (default): the bottom LegHeightPx rows of the keeper's current body frame, re-sliced from the
    // same atlas texture at runtime, so they are his real trousers and boots in the current pose and follow the
    // animation frame by frame. Rendered with the keeper's body material, so the outfit palette applies unchanged.
    // Source "Drawn": a 12x14 hand-drawn bent leg in the body's index colours (older fallback).
    //
    // The overlay is parented under the donkey's body sprite: that transform mirrors with the donkey's facing, sits on
    // the donkey's depth plane, and has the same 1.667 vertical stretch as the keeper's sprites. Laterally the donkey
    // body pivot coincides with the keeper's pivot (that is what the mount's centering does), so a slice placed with
    // its original horizontal offset lines up under the torso.
    internal static class RiderLeg
    {
        private const float PixelsPerUnit = 50f;
        private const float Px = 1f / PixelsPerUnit;

        private static SpriteRenderer renderer;
        private static Sprite currentSource;
        private static bool loggedSlice;
        private static readonly Dictionary<Sprite, Sprite> sliceCache = new Dictionary<Sprite, Sprite>();

        public static bool IsAttached => renderer != null && renderer;

        public static void ClearCache()
        {
            sliceCache.Clear();
            currentSource = null;
        }

        public static void Attach(SpriteRenderer donkeyBody, SpriteRenderer keeperBody)
        {
            Detach();
            if (donkeyBody == null || keeperBody == null)
            {
                return;
            }
            GameObject go = new GameObject("mount_rider_legs");
            go.layer = keeperBody.gameObject.layer;
            go.transform.SetParent(donkeyBody.transform, worldPositionStays: false);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            renderer = go.AddComponent<SpriteRenderer>();
            renderer.sharedMaterial = keeperBody.sharedMaterial;
            renderer.sortingLayerID = keeperBody.sortingLayerID;
            renderer.sortingOrder = keeperBody.sortingOrder + 1;
            renderer.drawMode = SpriteDrawMode.Simple;
            currentSource = null;
            Update(donkeyBody, keeperBody);
        }

        // Every frame: pick the slice for the keeper's current body frame and place its top edge on the donkey's
        // back line (24 px above the donkey's ground; the frames have a 2 px transparent border, pivot at the frame
        // centre). Horizontally the slice keeps the offset it has in the keeper's frame.
        public static void Update(SpriteRenderer donkeyBody, SpriteRenderer keeperBody)
        {
            if (!IsAttached || donkeyBody == null || donkeyBody.sprite == null || keeperBody == null || keeperBody.sprite == null)
            {
                return;
            }
            Sprite body = keeperBody.sprite;
            if (Plugin.LegSourceSetting.Value == LegSource.Body)
            {
                if (body != currentSource)
                {
                    currentSource = body;
                    renderer.sprite = SliceFor(body);
                }
            }
            else if (renderer.sprite == null || renderer.sprite.name != "mount_rider_leg")
            {
                currentSource = null;
                renderer.sprite = DrawnLeg.Get();
            }

            // The outfit palette lives on the keeper body renderer's own material instance; track it every frame in
            // case the game re-instances it (armour, colour change, skin reapply).
            if (renderer.sharedMaterial != keeperBody.sharedMaterial)
            {
                renderer.sharedMaterial = keeperBody.sharedMaterial;
            }

            // Mirroring: the keeper mirrors with flipX (or a negative parent scale), the donkey with a negative
            // parent scale, and the two are not the same convention for the same facing. The slice must end up
            // mirrored exactly like the keeper, so undo the donkey's parent mirroring when it differs.
            bool keeperMirrored = keeperBody.flipX ^ (keeperBody.transform.lossyScale.x < 0f);
            bool donkeyMirrored = donkeyBody.transform.lossyScale.x < 0f;
            renderer.flipX = keeperMirrored ^ donkeyMirrored;

            // Horizontal: anchor to the torso itself (the keeper body renderer's pivot in world space), plus the
            // pixel adjustment towards the rear (rear = opposite to facing; art faces left, mirrored = faces right).
            // Vertical: the donkey's back line for the current frame, so the legs bob with it.
            Sprite d = donkeyBody.sprite;
            float rearSign = keeperMirrored ? -1f : 1f;
            Vector3 worldRight = Vector3.right;
            Vector3 anchor = keeperBody.transform.position + worldRight * (rearSign * Plugin.LegAdjustXPx.Value * Px);
            Vector3 local = donkeyBody.transform.InverseTransformPoint(anchor);
            float groundPx = d.textureRectOffset.y + 2f;
            float topYPx = groundPx + Mount.BackHeightPx(d) + Plugin.LegAdjustYPx.Value;
            renderer.transform.localPosition = new Vector3(local.x, (topYPx - d.pivot.y) * Px, -0.01f);
        }

        public static void Detach()
        {
            if (renderer != null)
            {
                try
                {
                    if (renderer)
                    {
                        UnityEngine.Object.Destroy(renderer.gameObject);
                    }
                }
                catch
                {
                }
            }
            renderer = null;
            currentSource = null;
        }

        // Bottom LegHeightPx rows of the body frame's trimmed rect (feet are always the bottom row, so the belt is
        // a constant distance above them). Pivot: top edge, and horizontally the original frame's pivot, so the
        // slice's local origin is the keeper's own origin. Unity accepts pivots outside the rect.
        private static Sprite SliceFor(Sprite body)
        {
            if (sliceCache.TryGetValue(body, out Sprite cached) && cached != null)
            {
                return cached;
            }
            Rect tr = body.textureRect;
            int h = Mathf.Clamp(Plugin.LegHeightPx.Value, 1, Mathf.RoundToInt(tr.height));
            Rect rect = new Rect(tr.x, tr.y, tr.width, h);
            Vector2 off = body.textureRectOffset;
            // pivot in the slice's pixel space: frame pivot minus trimmed offset, top edge
            Vector2 pivotPx = new Vector2(body.pivot.x - off.x, h);
            Vector2 pivot = new Vector2(pivotPx.x / rect.width, pivotPx.y / rect.height);
            // Sprites can carry secondary textures the shader samples alongside the main atlas (palette index, light
            // masks); copy them so the slice renders exactly like the body.
            SecondarySpriteTexture[] secondary = null;
            try
            {
                int n = body.GetSecondaryTextureCount();
                if (n > 0)
                {
                    secondary = new SecondarySpriteTexture[n];
                    body.GetSecondaryTextures(secondary);
                }
            }
            catch
            {
                secondary = null;
            }
            Sprite slice = secondary != null
                ? Sprite.Create(body.texture, rect, pivot, body.pixelsPerUnit, 0, SpriteMeshType.FullRect, Vector4.zero, false, secondary)
                : Sprite.Create(body.texture, rect, pivot, body.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            slice.name = "mount_rider_legs:" + body.name;
            if (!loggedSlice)
            {
                loggedSlice = true;
                Plugin.Log.LogInfo($"Mount: leg slice from {body.name} tex={body.texture?.name} rect={rect} secondaryTextures={(secondary?.Length ?? 0)} material={renderer?.sharedMaterial?.name}/{renderer?.sharedMaterial?.shader?.name}");
            }
            sliceCache[body] = slice;
            return slice;
        }

        // Older hand-drawn fallback (12x14, index colours T trousers, h highlight, t shade, B boot, s sole).
        private static class DrawnLeg
        {
            private static Sprite sprite;
            private const int HipX = 10;
            private const int Width = 12;
            private const int Height = 14;
            private static readonly string[] Map =
            {
                "......tTTTTt",
                "...ttTTThTTt",
                ".tTTTTThTTt.",
                "tTTTTTTTtt..",
                "tTTTTttt....",
                "tTTTt.......",
                ".tTTt.......",
                ".tTTt.......",
                ".tTTt.......",
                ".tTTt.......",
                ".tBBt.......",
                "BBBBB.......",
                "BBBBBt......",
                ".sssss......",
            };

            public static Sprite Get()
            {
                if (sprite != null)
                {
                    return sprite;
                }
                Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGBA32, mipChain: false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "mount_rider_leg" };
                Color32[] pixels = new Color32[Width * Height];
                for (int row = 0; row < Height; row++)
                {
                    for (int col = 0; col < Width; col++)
                    {
                        pixels[(Height - 1 - row) * Width + col] = ColorFor(Map[row][col]);
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                sprite = Sprite.Create(tex, new Rect(0, 0, Width, Height), new Vector2((HipX + 0.5f) / Width, 1f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
                sprite.name = "mount_rider_leg";
                return sprite;
            }

            private static Color32 ColorFor(char c)
            {
                switch (c)
                {
                    case 'T': return new Color32(0xb8, 0x01, 0xc8, 255);
                    case 'h': return new Color32(0xeb, 0x07, 0xff, 255);
                    case 't': return new Color32(0x3e, 0x01, 0x44, 255);
                    case 'B': return new Color32(0x0e, 0x01, 0xc9, 255);
                    case 's': return new Color32(0x53, 0x33, 0x20, 255);
                    default: return new Color32(0, 0, 0, 0);
                }
            }
        }
    }
}

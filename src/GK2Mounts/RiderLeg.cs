using System;
using UnityEngine;

namespace GK2Mounts
{
    // The rider's near leg: a 12x14 pixel sprite hanging down the donkey's flank, drawn in the keeper body art's
    // palette-index colours (the game's keeper sprites are flat index colours that the sprite shader maps to the
    // outfit palette), and rendered with the keeper's own body material so it recolours with any outfit.
    //
    // It is parented under the donkey's body sprite: that transform is mirrored with the donkey's facing, sits on
    // the donkey's depth plane (so the leg draws over the donkey while the keeper's real legs stay hidden behind
    // it), and its sprite units are the same 50 px/unit as the art. Position is computed from the donkey sprite's
    // trimmed-rect offset: hip = saddle point on the back line.
    internal static class RiderLeg
    {
        private const float PixelsPerUnit = 50f;
        private const float Px = 1f / PixelsPerUnit;

        // Hip pixel (where the leg attaches to the rider) in the map below: column 10, top row.
        private const int HipX = 10;
        private const int Width = 12;
        private const int Height = 14;

        // T trousers, h highlight, t shade/outline, B boot, s sole. Index colours copied from 9008_bdy_static_left.
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

        private static Sprite legSprite;
        private static SpriteRenderer renderer;

        // Extra offset in art pixels, tuned in game (Ride.LegAdjustXPx / LegAdjustYPx plus the tuning keys).
        internal static int AdjustX;
        internal static int AdjustY;

        public static bool IsAttached => renderer != null && renderer;

        public static void Attach(SpriteRenderer donkeyBody, SpriteRenderer keeperBody)
        {
            Detach();
            if (donkeyBody == null || keeperBody == null)
            {
                return;
            }
            if (legSprite == null)
            {
                legSprite = BuildSprite();
            }
            GameObject go = new GameObject("mount_rider_leg");
            go.layer = keeperBody.gameObject.layer;
            go.transform.SetParent(donkeyBody.transform, worldPositionStays: false);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = legSprite;
            renderer.sharedMaterial = keeperBody.sharedMaterial; // same palette recolouring as the keeper's body
            renderer.sortingLayerID = keeperBody.sortingLayerID;
            renderer.sortingOrder = keeperBody.sortingOrder + 1;
            renderer.drawMode = SpriteDrawMode.Simple;
            UpdatePosition(donkeyBody);
        }

        // Saddle point: horizontally the middle of the donkey frame plus 2 px towards the rear (the frame faces
        // left, so the rear is +x), vertically the back line, 24 px above the ground for the static frame. The
        // trimmed rect has a 2 px transparent border, and the pivot is the frame centre (48,48 of 96).
        public static void UpdatePosition(SpriteRenderer donkeyBody)
        {
            if (!IsAttached || donkeyBody == null || donkeyBody.sprite == null)
            {
                return;
            }
            Sprite s = donkeyBody.sprite;
            Vector2 off = s.textureRectOffset;
            Rect tr = s.textureRect;
            Vector2 pivot = s.pivot;
            float groundPx = off.y + 2f;
            float hipYPx = groundPx + 24f + AdjustY;
            float hipXPx = off.x + tr.width * 0.5f + 2f + AdjustX;
            renderer.transform.localPosition = new Vector3((hipXPx - pivot.x) * Px, (hipYPx - pivot.y) * Px, -0.01f);
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
        }

        private static Sprite BuildSprite()
        {
            Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "mount_rider_leg",
            };
            Color32[] pixels = new Color32[Width * Height];
            for (int row = 0; row < Height; row++)
            {
                string line = Map[row];
                for (int col = 0; col < Width; col++)
                {
                    // Texture rows go bottom-up; the map is written top-down.
                    pixels[(Height - 1 - row) * Width + col] = ColorFor(line[col]);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            // Pivot at the hip pixel's centre, top row.
            Vector2 pivot = new Vector2((HipX + 0.5f) / Width, 1f);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, Width, Height), pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
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

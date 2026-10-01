using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

using GK2Mounts.Api;

namespace GK2Mounts
{
    // Recolouring of the donkey, the cart and the lantern (Round 16 of PROBE-FINDINGS, docs/mounts-colors.md).
    //
    // Everything goes through the game's own recolour paths, found in its shaders:
    // - The donkey's sprites (body, head) and the cart's wheel sprites use Custom/3D Sprite. With the keyword
    //   USE_PALETTE_COLOR_REPLACE it loops over a palette texture (width N, height 2: row 1 the source colours, row 0
    //   their replacements) and replaces every texel within 0.004 of a source colour: an exact colour swap, the one the
    //   keeper's outfit uses. The mod builds that palette from the art's own colours (embedded below, read from the
    //   game's atlas) mapped through the region settings, so every region changes on its own.
    // - The cart's 3D parts (body, shafts, wheels, lantern pole and lamp) are Object3D meshes. Object3DMesh.SetLutTexture
    //   swaps them to the game's LUT material (zombies and building variants use it): a 32x32x32 colour cube laid out
    //   as 1024x32 (x = floor(b*31)*32 + round(r*31), y = round(g*31)). The mod's cube is point-sampled, identity
    //   everywhere, and every colour the art uses is written into its own cell with its exact mapped value. It is state
    //   the game keeps (ApplyMaterial re-applies it), so it survives the transparency fade when the keeper walks behind
    //   the cart.
    // Nothing is touched while every region is at its defaults. The donkey's parts come from the game's pool (the
    // delivery donkey reuses them), so Detach puts every material, keyword and LUT back before the view is destroyed.
    internal static partial class Colors
    {
        internal const string Section = "Colors";

        // Where each colour list lives. A region can cover several (the wheels are sprites and meshes).
        private enum Group
        {
            DonkeySprites,
            WheelSprites,
            CartBody,
            Bars,
            Wheels3D,
            Lantern,
        }

        private const int GroupCount = 6;

        private static bool IsSpriteGroup(Group g) => g == Group.DonkeySprites || g == Group.WheelSprites;

        private sealed class RegionDef
        {
            public string Key, Name, Area;
            public (Group G, string[] Colors)[] Parts;
            public RegionSettings S;
            public MountColorRegion Api;
        }

        // The art's exact colours by region, most used first (read from the game's atlas and the cart textures,
        // tools in docs/mounts-colors.md). Every colour of every group is in exactly one region of that group.
        private static readonly RegionDef[] Regions =
        {
            R("Coat", "Donkey coat", "Donkey", (Group.DonkeySprites, "736c57 4a3d20 665b44 584c33 3e3019 54503b 655c49 4d4539 362815 453b2e 3c3223 3a3428 534f3a 443b31")),
            R("Dark", "Donkey outline, mane and hooves", "Donkey", (Group.DonkeySprites, "1e190e 301f0f 292316 352e1e 130a0c 2f2c1f")),
            R("Muzzle", "Donkey muzzle and belly", "Donkey", (Group.DonkeySprites, "42422c abb298 858e6b 858f6c 75755a a9a7a2")),
            R("Eye", "Donkey eye", "Donkey", (Group.DonkeySprites, "ffffff")),
            R("Harness", "Donkey harness", "Donkey", (Group.DonkeySprites, "62411b 3c2a14 2d210f 443017 885923 d68e38 2f1d08 3a2003 a06a29 5f3d14 765024 342918 2d2114 463017 472d0e")),
            R("CartBody", "Cart body", "Cart", (Group.CartBody, "885923 784c1d 443017 4f361a 634623 2d210f 593b21 392516 b97b30 a06a29 402a13")),
            R("Shafts", "Cart shafts", "Cart", (Group.Bars, "473015 885923 2d210f 62411b a06a29 b97b30 463017")),
            R("Straps", "Shaft straps", "Cart", (Group.Bars, "3a2003 2f1d08 5f3d14 482a04 886031 765024")),
            R("Hooks", "Shaft hooks", "Cart", (Group.Bars, "392619 2a1c13 848e8c 635042 412f26")),
            R("Wheels", "Wheels", "Cart",
                (Group.Wheels3D, "2d210f 885923 b97b30 62411b a06a29 d68e38 975f23 6f451a 473015 82521e 503314 3a250f ae6e29"),
                (Group.WheelSprites, "2d210f 885923 62411b 473015 b97b30 a06a29 975f23 d68e38 503314 82521e 6f451a 392619 3a250f 3c2a14")),
            R("Hubs", "Wheel hubs", "Cart", (Group.Wheels3D, "635042 392619 848e8c 7d6f63 2a1c13 493b31")),
            R("Pole", "Lantern pole", "Lantern", (Group.Lantern, "2d210f 593b21 443017 885923 a06a29")),
            R("Lamp", "Lamp", "Lantern", (Group.Lantern, "30251d 1c1611 635042 493b31 372921 736f67 412f26 2b211a 848e8c a2b4b5")),
            R("Glass", "Lamp glass", "Lantern", (Group.Lantern, "3d455a 687eae 283042")),
        };

        // Colours a group's art has that belong to no region (left as they are): a magenta marker in the wheel texture.
        private static readonly Dictionary<Group, string[]> Unassigned = new Dictionary<Group, string[]>
        {
            { Group.Wheels3D, new[] { "ac25a5" } },
        };

        // The texture name each cart mesh group's meshes are drawn with (Object3DSubMesh.Texture, e.g.
        // donkey_cart_body_prt1): cargo, the corpse shroud and anything else keep their own look.
        private static readonly (string Prefix, Group G)[] MeshTextures =
        {
            ("donkey_cart_body", Group.CartBody),
            ("donkey_cart_bars", Group.Bars),
            ("donkey_cart_wheel", Group.Wheels3D),
            ("donkey_cart_lantern", Group.Lantern),
        };

        private static RegionDef R(string key, string name, string area, params (Group, string)[] parts)
        {
            var d = new RegionDef { Key = key, Name = name, Area = area, Parts = new (Group, string[])[parts.Length] };
            var all = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string[] cols = parts[i].Item2.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                d.Parts[i] = (parts[i].Item1, cols);
                foreach (string c in cols)
                {
                    string u = c.ToUpperInvariant();
                    if (!all.Contains(u)) all.Add(u);
                }
            }
            d.Api = new MountColorRegion(key, name, area, all.AsReadOnly());
            return d;
        }

        // ------------------------------------------------------------------ settings

        internal sealed class RegionSettings
        {
            public Tuned<float> Hue, Saturation, Brightness, Contrast, ColorizeAmount, OverlayAmount;
            public Tuned<string> Colorize, Tint, Overlay, Swaps;

            public ITuned[] All => new ITuned[] { Hue, Saturation, Brightness, Contrast, Colorize, ColorizeAmount, Tint, Overlay, OverlayAmount, Swaps };

            public ConfigEntryBase[] Entries => new ConfigEntryBase[] { Hue.Entry, Saturation.Entry, Brightness.Entry, Contrast.Entry, Colorize.Entry, ColorizeAmount.Entry, Tint.Entry, Overlay.Entry, OverlayAmount.Entry, Swaps.Entry };

            // True when this region changes nothing (then its colours map to themselves).
            public bool IsIdentity =>
                Mathf.Abs(Hue.Value) < 0.01f && Mathf.Abs(Saturation.Value - 1f) < 0.001f && Mathf.Abs(Brightness.Value - 1f) < 0.001f
                && Mathf.Abs(Contrast.Value - 1f) < 0.001f && (!TryHex(Colorize.Value, out _) || ColorizeAmount.Value <= 0f)
                && (!TryHex(Tint.Value, out Color32 t) || (t.r == 255 && t.g == 255 && t.b == 255))
                && (!TryHex(Overlay.Value, out _) || OverlayAmount.Value <= 0f) && ParseSwaps(Swaps.Value).Count == 0;
        }

        internal static Tuned<bool> Enabled;
        internal static Tuned<SpriteColorMethod> SpriteMethod;
        internal static Tuned<string> GlowColor, LitGlassTint, CharacterLightColor;

        internal static MountColorRegion[] ApiRegions;
        private static MountTuning tuning;

        internal static RegionSettings SettingsOf(string regionKey)
        {
            foreach (RegionDef d in Regions)
            {
                if (d.Key == regionKey) return d.S;
            }
            return null;
        }

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind(Section, "Enabled", Defaults.Get(Section, "Enabled", true),
                "Master switch for the colour settings below. Off: the donkey, cart and lantern look exactly as the game draws them.");
            SpriteMethod = cfg.Bind(Section, "SpriteMethod", Defaults.Get(Section, "SpriteMethod", SpriteColorMethod.Palette),
                "How the donkey's sprites are recoloured. Palette: the game's exact colour swap, every region on its own. Lut: the game's colour cube (32 steps per channel), a fallback if the palette does not show on your setup; a few near-identical colours of neighbouring regions then change together. The cart and lantern always use the colour cube.");
            GlowColor = cfg.Bind(Section, "GlowColor", Defaults.Get(Section, "GlowColor", ""),
                "Cart style: colour of the soft glow around the lamp, hex RRGGBB. Empty = the game's. Its opacity stays on the Lantern tab.");
            LitGlassTint = cfg.Bind(Section, "LitGlassTint", Defaults.Get(Section, "LitGlassTint", ""),
                "Cart style: tint multiplied into the lit lantern-glass sprite, hex RRGGBB (FFFFFF = unchanged). Empty = the game's.");
            CharacterLightColor = cfg.Bind(Section, "CharacterLightColor", Defaults.Get(Section, "CharacterLightColor", ""),
                "Cart style: colour of the lamp's character light (lights only people and sprites nearby), hex RRGGBB. Empty = the same as the main light (Cart.LanternLightColor).");
            foreach (RegionDef d in Regions)
            {
                string sec = Section + "." + d.Key;
                string who = d.Name.ToLowerInvariant();
                var s = new RegionSettings();
                s.Hue = F(cfg, sec, d.Key + "Hue", 0f, -180f, 180f, $"Hue shift of the {who}, degrees (0 = unchanged).");
                s.Saturation = F(cfg, sec, d.Key + "Saturation", 1f, 0f, 3f, $"Saturation of the {who}, multiplier (1 = unchanged, 0 = grey).");
                s.Brightness = F(cfg, sec, d.Key + "Brightness", 1f, 0f, 3f, $"Brightness of the {who}, multiplier (1 = unchanged).");
                s.Contrast = F(cfg, sec, d.Key + "Contrast", 1f, 0f, 3f, $"Contrast of the {who} about mid grey, multiplier (1 = unchanged).");
                s.Colorize = S(cfg, sec, d.Key + "Colorize", $"Give the {who} this colour's hue and saturation, keeping each pixel's own brightness (the shading stays), hex RRGGBB. Empty = off.");
                s.ColorizeAmount = F(cfg, sec, d.Key + "ColorizeAmount", 1f, 0f, 1f, "How much of the Colorize colour is mixed in (1 = all).");
                s.Tint = S(cfg, sec, d.Key + "Tint", $"Colour multiplied into the {who}, hex RRGGBB (FFFFFF = unchanged). Empty = off.");
                s.Overlay = S(cfg, sec, d.Key + "Overlay", $"Flat colour mixed over the {who} after everything else, hex RRGGBB. Empty = off.");
                s.OverlayAmount = F(cfg, sec, d.Key + "OverlayAmount", 0.5f, 0f, 1f, "How much of the Overlay colour is mixed in (1 = flat colour).");
                s.Swaps = S(cfg, sec, d.Key + "Swaps", $"Exact colour replacements for the {who}: SRC=DST pairs of hex colours separated by ';' (SRC one of the region's own colours). A swapped colour ignores the other settings.");
                d.S = s;
            }
            var list = new List<MountColorRegion>();
            foreach (RegionDef d in Regions)
            {
                list.Add(d.Api);
            }
            ApiRegions = list.ToArray();
            cfg.SettingChanged += (sender, e) =>
            {
                if (e?.ChangedSetting?.Definition.Section != null && e.ChangedSetting.Definition.Section.StartsWith(Section, StringComparison.Ordinal))
                {
                    MarkDirty();
                }
            };
        }

        private static ConfigEntry<float> F(ConfigFile cfg, string section, string key, float def, float min, float max, string desc)
        {
            return cfg.Bind(section, key, Defaults.Get(section, key, def), new ConfigDescription(desc, new AcceptableValueRange<float>(min, max)));
        }

        private static ConfigEntry<string> S(ConfigFile cfg, string section, string key, string desc)
        {
            return cfg.Bind(section, key, Defaults.Get(section, key, ""), desc);
        }

        // Every colour setting, globals first, for Defaults, the reset button and the API.
        internal static IEnumerable<ConfigEntryBase> AllEntries()
        {
            yield return Enabled.Entry;
            yield return SpriteMethod.Entry;
            yield return GlowColor.Entry;
            yield return LitGlassTint.Entry;
            yield return CharacterLightColor.Entry;
            foreach (RegionDef d in Regions)
            {
                foreach (ConfigEntryBase e in d.S.Entries)
                {
                    yield return e;
                }
            }
        }

        internal static MountTuning Tuning
        {
            get
            {
                if (tuning == null)
                {
                    var all = new List<ITuned> { Enabled, SpriteMethod, GlowColor, LitGlassTint, CharacterLightColor };
                    foreach (RegionDef d in Regions)
                    {
                        all.AddRange(d.S.All);
                    }
                    tuning = new MountTuning(Section, all);
                }
                return tuning;
            }
        }

        internal static void ResetRegion(string key)
        {
            RegionSettings s = SettingsOf(key);
            if (s == null) return;
            foreach (ConfigEntryBase e in s.Entries)
            {
                Defaults.ResetToDefault(e);
            }
            MarkDirty();
        }

        internal static void ResetAll()
        {
            foreach (ConfigEntryBase e in AllEntries())
            {
                Defaults.ResetToDefault(e);
            }
            MarkDirty();
        }

        // The panel's test: obvious colours on every area as a session override (owner TestOwner), never written to
        // the cfg, so one press shows whether each path renders; ClearTest (or ClearTuning) takes them away.
        internal const string TestOwner = "dreas.gk2.mounts.colortest";

        internal static void ApplyTest()
        {
            MountTuning t = Tuning;
            t.Set(TestOwner, "CoatColorize", "C040FF");
            t.Set(TestOwner, "CoatColorizeAmount", 1f);
            t.Set(TestOwner, "HarnessSwaps", "885923=FF2020;D68E38=FF8080;A06A29=D01010;765024=A00000;62411B=800000");
            t.Set(TestOwner, "EyeSwaps", "FFFFFF=40FF40");
            t.Set(TestOwner, "CartBodyHue", 180f);
            t.Set(TestOwner, "WheelsColorize", "20E0FF");
            t.Set(TestOwner, "HubsOverlay", "FFFF00");
            t.Set(TestOwner, "HubsOverlayAmount", 0.8f);
            t.Set(TestOwner, "ShaftsTint", "60FF60");
            t.Set(TestOwner, "PoleColorize", "FF40A0");
            t.Set(TestOwner, "GlassSwaps", "687EAE=FF3030;3D455A=A01010;283042=600808");
            t.Set(TestOwner, "GlowColor", "40A0FF");
            t.Set(TestOwner, "LitGlassTint", "60C0FF");
        }

        internal static bool TestActive
        {
            get
            {
                foreach (string k in Tuning.Keys)
                {
                    if (Tuning.OverriddenBy(k) == TestOwner) return true;
                }
                return false;
            }
        }

        internal static void ClearTest()
        {
            Tuning.ClearOwner(TestOwner);
            MarkDirty();
        }

        // ------------------------------------------------------------------ colour maths

        internal static bool TryHex(string hex, out Color32 c)
        {
            c = new Color32(255, 255, 255, 255);
            if (string.IsNullOrEmpty(hex)) return false;
            string h = hex.Trim().TrimStart('#');
            if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
            c = new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
            return true;
        }

        internal static string Hex(Color32 c) => $"{c.r:X2}{c.g:X2}{c.b:X2}";

        internal static Dictionary<string, string> ParseSwaps(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return d;
            foreach (string pair in text.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string a = pair.Substring(0, eq).Trim().TrimStart('#'), b = pair.Substring(eq + 1).Trim().TrimStart('#');
                if (TryHex(a, out _) && TryHex(b, out _))
                {
                    d[a.ToUpperInvariant()] = b.ToUpperInvariant();
                }
            }
            return d;
        }

        internal static string FormatSwaps(Dictionary<string, string> d)
        {
            var parts = new List<string>();
            foreach (var kv in d)
            {
                parts.Add(kv.Key.ToUpperInvariant() + "=" + kv.Value.ToUpperInvariant());
            }
            return string.Join(";", parts);
        }

        // A region's colour function: swaps win; else hue, saturation, brightness, contrast, colorize, tint, overlay.
        private sealed class RegionMap
        {
            public bool Identity;
            public float Hue, Sat, Val, Contrast, ColorizeAmount, OverlayAmount;
            public bool HasColorize, HasTint, HasOverlay;
            public float CzH, CzS;
            public Color Tint, Overlay;
            public Dictionary<string, string> Swaps;

            public RegionMap(RegionSettings s)
            {
                Identity = s.IsIdentity;
                Hue = s.Hue.Value; Sat = s.Saturation.Value; Val = s.Brightness.Value; Contrast = s.Contrast.Value;
                HasColorize = TryHex(s.Colorize.Value, out Color32 cz) && s.ColorizeAmount.Value > 0f;
                ColorizeAmount = Mathf.Clamp01(s.ColorizeAmount.Value);
                if (HasColorize)
                {
                    Color.RGBToHSV(cz, out CzH, out CzS, out _);
                }
                HasTint = TryHex(s.Tint.Value, out Color32 t);
                Tint = t;
                HasOverlay = TryHex(s.Overlay.Value, out Color32 o) && s.OverlayAmount.Value > 0f;
                Overlay = o;
                OverlayAmount = Mathf.Clamp01(s.OverlayAmount.Value);
                Swaps = ParseSwaps(s.Swaps.Value);
            }

            public Color32 Apply(Color32 src)
            {
                if (Identity) return src;
                if (Swaps.Count > 0 && Swaps.TryGetValue(Hex(src), out string dst) && TryHex(dst, out Color32 sw))
                {
                    return sw;
                }
                Color c = src;
                Color.RGBToHSV(c, out float h, out float s, out float v);
                h = Mathf.Repeat(h + Hue / 360f, 1f);
                s = Mathf.Clamp01(s * Sat);
                v = Mathf.Clamp01(v * Val);
                c = Color.HSVToRGB(h, s, v);
                if (Mathf.Abs(Contrast - 1f) > 0.001f)
                {
                    c.r = Mathf.Clamp01((c.r - 0.5f) * Contrast + 0.5f);
                    c.g = Mathf.Clamp01((c.g - 0.5f) * Contrast + 0.5f);
                    c.b = Mathf.Clamp01((c.b - 0.5f) * Contrast + 0.5f);
                }
                if (HasColorize)
                {
                    Color.RGBToHSV(c, out _, out _, out float cv);
                    c = Color.Lerp(c, Color.HSVToRGB(CzH, CzS, cv), ColorizeAmount);
                }
                if (HasTint)
                {
                    c.r *= Tint.r; c.g *= Tint.g; c.b *= Tint.b;
                }
                if (HasOverlay)
                {
                    c = Color.Lerp(c, Overlay, OverlayAmount);
                }
                c.a = 1f;
                return c;
            }
        }

        // ------------------------------------------------------------------ what is on the donkey

        private sealed class SpriteTarget
        {
            public SpriteRenderer R;
            public Group G;
            public Material Orig, Mine;
            public bool Warned;
        }

        private sealed class MeshTarget
        {
            public Object3DMesh M;
            public Group G;
            public bool Applied;
            public Texture2D OrigLut;
            public string OrigMaterials;
            public Material[] OrigMats;
            public bool Warned;
        }

        // ------------------------------------------------------------------ per instance (the bound one: Mount.Current)

        private static Rig Current => Mount.Current?.ColorsRig;
        public static string Note => Current?.Note ?? "not attached";
        internal static IReadOnlyList<MountColorRegion> RegionList => ApiRegions;
        internal static string Diagnose() => Current?.Diagnose() ?? "[colors] no donkey";
        internal static Color GlowRgb(Color game) => Rig.GlowRgb(game);
        internal static Color CharacterLight(Color current) => Rig.CharacterLight(current);
        internal static Color32 Preview(string regionKey, Color32 src) => Rig.Preview(regionKey, src);
        internal static bool RegionChanged(string regionKey) => Rig.RegionChanged(regionKey);
        internal static int LutIndex(Color32 c) => Rig.LutIndex(c);

        // A colour setting changed (cfg, panel, API, an instance override): every instance rebuilds at its next frame,
        // each with its own values.
        internal static void MarkDirty()
        {
            Mount.ForEachInstance(i => i.ColorsRig.MarkDirty(), bind: false);
        }

        // One per instance (Mount.Instance.Colors).
        internal sealed partial class Rig
        {
            private readonly List<SpriteTarget> sprites = new List<SpriteTarget>();
            private readonly List<MeshTarget> meshes = new List<MeshTarget>();
            private readonly Texture2D[] palettes = new Texture2D[GroupCount];
            private readonly Texture2D[] luts = new Texture2D[GroupCount];
            private readonly bool[] groupActive = new bool[GroupCount];
            private Wgo attachedTo;
            private bool dirty = true;
            private float nextVerify;
            private float nextSpriteCheck = UnityEngine.Random.value * 0.2f; // spread over the donkeys' frames
            private SpriteColorMethod appliedMethod;
            public string Note { get; private set; } = "not attached";

            private static readonly int IdPalette = Shader.PropertyToID("_Palette");
            private static readonly int IdLut = Shader.PropertyToID("_ReplaceLUT");
            private const string KwPalette = "USE_PALETTE_COLOR_REPLACE";
            private const string KwLut = "USE_LUT_COLOR_REPLACE";

            internal void MarkDirty()
            {
                dirty = true;
            }

            // After PrepareParts: the view's parts are in place (and again after a cargo change: nothing new to find, the
            // lists are rebuilt the same way).
            internal void Attach(Wgo donkey)
            {
                Detach();
                if (donkey == null || !donkey)
                {
                    return;
                }
                attachedTo = donkey;
                try
                {
                    foreach (SpriteRenderer sr in donkey.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
                    {
                        if (UnderCargo(sr.transform)) continue;
                        if (sr.name == "bdy" || sr.name == "hed")
                        {
                            sprites.Add(new SpriteTarget { R = sr, G = Group.DonkeySprites });
                        }
                        else if (HasAncestorNamed(sr.transform, "wheel_left") || HasAncestorNamed(sr.transform, "wheel_right"))
                        {
                            sprites.Add(new SpriteTarget { R = sr, G = Group.WheelSprites });
                        }
                    }
                    foreach (Object3DMesh m in donkey.GetComponentsInChildren<Object3DMesh>(includeInactive: true))
                    {
                        if (UnderCargo(m.transform)) continue;
                        Group? g = MeshGroup(m);
                        if (g.HasValue)
                        {
                            meshes.Add(new MeshTarget { M = m, G = g.Value });
                        }
                    }
                    Note = $"{sprites.Count} sprites ({Count(sprites, Group.DonkeySprites)} donkey, {Count(sprites, Group.WheelSprites)} wheel), {meshes.Count} cart meshes";
                    Mount.DiagLog("Colors: attached; " + Note + "."); // once per view: shares the 4-a-second budget
                }
                catch (Exception e)
                {
                    Note = "attach failed: " + e.Message;
                    Plugin.Log.LogWarning("Colors: " + Note);
                }
                dirty = true;
                Tick();
            }

            private int Count(List<SpriteTarget> list, Group g)
            {
                int n = 0;
                foreach (SpriteTarget t in list) if (t.G == g) n++;
                return n;
            }

            private bool UnderCargo(Transform t)
            {
                return HasAncestorNamed(t, Cargo.ContainerName);
            }

            private bool HasAncestorNamed(Transform t, string name)
            {
                for (Transform p = t; p != null; p = p.parent)
                {
                    if (p.name == name) return true;
                }
                return false;
            }

            private Group? MeshGroup(Object3DMesh m)
            {
                for (int i = 0; i < m.SubMeshCount; i++)
                {
                    string tex = m.GetSubMesh(i)?.MainTextureName;
                    if (string.IsNullOrEmpty(tex)) continue;
                    foreach (var mt in MeshTextures)
                    {
                        if (tex.StartsWith(mt.Prefix, StringComparison.Ordinal)) return mt.G;
                    }
                    return null; // a texture of something else
                }
                // No texture known yet (a part not initialised): by the cart node it hangs under. The loaded-cart body
                // (donkey_cart_body_sway, the corpse shroud) is left out on purpose.
                if (HasAncestorNamed(m.transform, "donkey_cart_empty_sway")) return Group.CartBody;
                if (HasAncestorNamed(m.transform, "donkey_cart_bars")) return Group.Bars;
                if (HasAncestorNamed(m.transform, "donkey_cart_wheel_f") || HasAncestorNamed(m.transform, "donkey_cart_wheel_b")) return Group.Wheels3D;
                if (HasAncestorNamed(m.transform, "donkey_cart_lantern_sway")) return Group.Lantern;
                return null;
            }

            // Every frame while mounted, after the lantern (LampHang copies the pole's look onto the hanging lamp, LUT
            // included, so the lamp needs nothing of its own).
            internal void Tick()
            {
                if (attachedTo == null)
                {
                    return;
                }
                try
                {
                    if (dirty)
                    {
                        dirty = false;
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        int uploaded = Rebuild();
                        ApplyAll();
                        double ms = watch.Elapsed.TotalMilliseconds;
                        // A slider drag rebuilds every frame: say so when that is not cheap (at most every 2 s).
                        if (ms > 4.0 && Time.unscaledTime >= nextSlowLog)
                        {
                            nextSlowLog = Time.unscaledTime + 2f;
                            Plugin.Log.LogInfo($"Colors: an update took {ms:F1} ms ({uploaded} texture(s) uploaded).");
                        }
                        return;
                    }
                    // The game may re-skin the donkey (SkinChangerGK2 takes sprite.material and clears the replace
                    // keywords): put ours back. Five times a second, not every frame: with 63 donkeys the check of every
                    // sprite every frame was a large part of Colors (measured 2026-10-01); a re-skin shows for 0.2 s at most.
                    if (Time.unscaledTime < nextSpriteCheck)
                    {
                        return;
                    }
                    nextSpriteCheck = Time.unscaledTime + 0.2f;
                    foreach (SpriteTarget t in sprites)
                    {
                        if (t.Mine == null || t.R == null || !t.R) continue;
                        string kw = appliedMethod == SpriteColorMethod.Palette ? KwPalette : KwLut;
                        if (t.R.sharedMaterial != t.Mine || !t.Mine.IsKeywordEnabled(kw))
                        {
                            if (!t.Warned)
                            {
                                t.Warned = true;
                                Plugin.Log.LogInfo($"Colors: the game changed the material of '{t.R.name}' ({t.R.sharedMaterial?.name}); the colours are put back.");
                            }
                            t.R.sharedMaterial = t.Mine;
                            SetSpriteKeywords(t);
                        }
                    }
                    if (Time.unscaledTime >= nextVerify)
                    {
                        nextVerify = Time.unscaledTime + 1f;
                        foreach (MeshTarget t in meshes)
                        {
                            if (!t.Applied || t.M == null || !t.M) continue;
                            Object3DSubMesh sm = t.M.GetSubMesh(0);
                            // An inactive part (Ride style's cart, the loaded-cart body) was never initialised by the game:
                            // its LUT is stored and applies when it starts; its materials say nothing yet.
                            bool slotsOk = !t.M.gameObject.activeInHierarchy || AllSlotsLut(t.M);
                            if ((sm != null && sm.LutTexture != luts[(int)t.G]) || !slotsOk)
                            {
                                if (!t.Warned)
                                {
                                    t.Warned = true;
                                    Plugin.Log.LogInfo($"Colors: '{t.M.name}' lost its colours ({MaterialNames(t.M.ObjRenderer)}); set again.");
                                }
                                SetLut(t.M, luts[(int)t.G]);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Colors: update failed, colours off for this ride: " + e.Message);
                    Detach();
                }
            }

            // Wgo.OnDestroy prefix: any Wgo; only ours (still attached) matters.
            internal void OnWgoDestroyed(Wgo wgo)
            {
                if (attachedTo != null && ReferenceEquals(wgo, attachedTo))
                {
                    Plugin.Log.LogInfo("Colors: the donkey view was destroyed by the game (scene change); colours restored first.");
                    Detach();
                }
            }

            // Before the donkey view is destroyed (its parts go back to the game's pool) and when attaching again.
            // The instance is gone for good: its textures too (Unity does not collect them). After Detach.
            internal void Release()
            {
                for (int i = 0; i < GroupCount; i++)
                {
                    if (palettes[i] != null) UnityEngine.Object.Destroy(palettes[i]);
                    if (luts[i] != null) UnityEngine.Object.Destroy(luts[i]);
                    palettes[i] = null;
                    luts[i] = null;
                    builtFor[i] = false;
                    lastDst[i] = null;
                }
                dirty = true;
            }

            internal void Detach()
            {
                foreach (SpriteTarget t in sprites)
                {
                    RestoreSprite(t);
                }
                foreach (MeshTarget t in meshes)
                {
                    RestoreMesh(t);
                }
                sprites.Clear();
                meshes.Clear();
                RestoreGlass(); // the lit glass's property block too (idempotent: Lantern.Detach calls it again)
                attachedTo = null;
                Note = "not attached";
            }

            private float nextSlowLog;
            private readonly Color32[][] lastDst = new Color32[GroupCount][];
            private readonly bool[] builtFor = new bool[GroupCount]; // textures hold this group's current mapping

            // Returns how many textures were uploaded: only the groups whose mapping changed.
            private int Rebuild()
            {
                int uploads = 0;
                bool on = Enabled.Value;
                var maps = new Dictionary<RegionDef, RegionMap>();
                foreach (RegionDef d in Regions)
                {
                    maps[d] = new RegionMap(d.S);
                }
                for (int gi = 0; gi < GroupCount; gi++)
                {
                    Group g = (Group)gi;
                    // source colour -> mapped, for every colour the group's art has
                    var pairs = new List<(Color32 Src, Color32 Dst)>();
                    bool any = false;
                    foreach (RegionDef d in Regions)
                    {
                        RegionMap map = maps[d];
                        foreach (var part in d.Parts)
                        {
                            if (part.G != g) continue;
                            foreach (string hex in part.Colors)
                            {
                                TryHex(hex, out Color32 src);
                                Color32 dst = on ? map.Apply(src) : src;
                                pairs.Add((src, dst));
                                any |= !Same(src, dst);
                            }
                        }
                    }
                    if (Unassigned.TryGetValue(g, out string[] extra))
                    {
                        foreach (string hex in extra)
                        {
                            TryHex(hex, out Color32 src);
                            pairs.Add((src, src));
                        }
                    }
                    groupActive[gi] = on && any;
                    if (!groupActive[gi])
                    {
                        continue;
                    }
                    if (builtFor[gi] && SameDst(lastDst[gi], pairs))
                    {
                        continue; // this change was in another group
                    }
                    if (IsSpriteGroup(g))
                    {
                        palettes[gi] = BuildPalette(palettes[gi], pairs, "gk2mounts palette " + g);
                        uploads++;
                    }
                    luts[gi] = BuildLut(luts[gi], pairs, "gk2mounts lut " + g);
                    uploads++;
                    var mapped = new Color32[pairs.Count];
                    for (int i = 0; i < mapped.Length; i++) mapped[i] = pairs[i].Dst;
                    lastDst[gi] = mapped;
                    builtFor[gi] = true;
                }
                return uploads;
            }

            private static bool SameDst(Color32[] last, List<(Color32 Src, Color32 Dst)> pairs)
            {
                if (last == null || last.Length != pairs.Count) return false;
                for (int i = 0; i < last.Length; i++)
                {
                    if (!Same(last[i], pairs[i].Dst)) return false;
                }
                return true;
            }

            private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

            // Width N, height 2: row 1 the source colours, row 0 their replacements (the game's layout,
            // PaletteReplaceHelper.CombinePalettes). Point-sampled, uncompressed, no mipmaps: the shader samples texel centres.
            private static Texture2D BuildPalette(Texture2D tex, List<(Color32 Src, Color32 Dst)> pairs, string name)
            {
                int n = pairs.Count;
                if (tex == null || tex.width != n)
                {
                    if (tex != null) UnityEngine.Object.Destroy(tex);
                    tex = new Texture2D(n, 2, TextureFormat.RGBA32, mipChain: false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                }
                var px = new Color32[n * 2];
                for (int i = 0; i < n; i++)
                {
                    px[i] = pairs[i].Dst;
                    px[n + i] = pairs[i].Src;
                }
                tex.SetPixels32(px);
                tex.Apply(updateMipmaps: false);
                return tex;
            }

            // The game's 32-cube as 1024x32 (x = blue slice * 32 + red, y = green), point-sampled. Identity everywhere,
            // then each known colour's own cell holds its exact mapped value. The shaders look up
            // u = r*31/1024 + 0.5/1024 + floor(b*31)/32, v = g*31/32 + 0.5/32 (Round 16).
            private static Texture2D BuildLut(Texture2D tex, List<(Color32 Src, Color32 Dst)> pairs, string name)
            {
                if (tex == null)
                {
                    tex = new Texture2D(1024, 32, TextureFormat.RGBA32, mipChain: false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                }
                var px = (Color32[])IdentityLut.Clone();
                foreach (var p in pairs)
                {
                    px[LutIndex(p.Src)] = p.Dst;
                }
                tex.SetPixels32(px);
                tex.Apply(updateMipmaps: false);
                return tex;
            }

            private static Color32[] identityLut;

            private static Color32[] IdentityLut
            {
                get
                {
                    if (identityLut != null) return identityLut;
                    var px = new Color32[1024 * 32];
                    for (int g = 0; g < 32; g++)
                    {
                        byte gv = (byte)Mathf.RoundToInt(g * 255f / 31f);
                        for (int b = 0; b < 32; b++)
                        {
                            // a blue cell k holds b in [k/31, (k+1)/31): its middle, the last cell 255
                            byte bv = (byte)Mathf.Min(255, Mathf.RoundToInt((b + (b == 31 ? 0f : 0.5f)) * 255f / 31f));
                            for (int r = 0; r < 32; r++)
                            {
                                px[g * 1024 + b * 32 + r] = new Color32((byte)Mathf.RoundToInt(r * 255f / 31f), gv, bv, 255);
                            }
                        }
                    }
                    identityLut = px;
                    return px;
                }
            }

            internal static int LutIndex(Color32 c)
            {
                int r = Mathf.FloorToInt(c.r / 255f * 31f + 0.5f);
                int g = Mathf.FloorToInt(c.g / 255f * 31f + 0.5f);
                int b = Mathf.FloorToInt(c.b / 255f * 31f);
                return Mathf.Clamp(g, 0, 31) * 1024 + Mathf.Clamp(b, 0, 31) * 32 + Mathf.Clamp(r, 0, 31);
            }

            private void ApplyAll()
            {
                appliedMethod = SpriteMethod.Value;
                foreach (SpriteTarget t in sprites)
                {
                    if (t.R == null || !t.R) continue;
                    if (!groupActive[(int)t.G])
                    {
                        RestoreSprite(t);
                        continue;
                    }
                    if (t.Mine == null)
                    {
                        t.Orig = t.R.sharedMaterial;
                        if (t.Orig == null) continue;
                        t.Mine = new Material(t.Orig) { name = t.Orig.name + " (GK2 Mounts colours)" };
                        t.R.sharedMaterial = t.Mine;
                        SetSpriteKeywords(t);
                    }
                    else if (!SpriteUpToDate(t))
                    {
                        SetSpriteKeywords(t); // the method changed (the textures themselves are updated in place)
                    }
                }
                foreach (MeshTarget t in meshes)
                {
                    if (t.M == null || !t.M) continue;
                    if (!groupActive[(int)t.G])
                    {
                        RestoreMesh(t);
                        continue;
                    }
                    if (!t.Applied)
                    {
                        t.OrigLut = t.M.GetSubMesh(0)?.LutTexture;
                        t.OrigMaterials = MaterialNames(t.M.ObjRenderer);
                        t.OrigMats = t.M.ObjRenderer != null ? t.M.ObjRenderer.sharedMaterials : null;
                        t.Applied = true;
                    }
                    if (t.M.GetSubMesh(0)?.LutTexture == luts[(int)t.G])
                    {
                        continue; // already ours: the texture was updated in place
                    }
                    SetLut(t.M, luts[(int)t.G]);
                    if (t.M.gameObject.activeInHierarchy && !AllSlotsLut(t.M))
                    {
                        Plugin.Log.LogWarning($"Colors: '{t.M.name}' has {MaterialNames(t.M.ObjRenderer)} after the LUT was set; some of it keeps the game's colours.");
                    }
                }
            }

            // Object3DSubMesh keeps its own copy of the renderer's material list (cachedSharedMaterials) and, on a LUT
            // change, writes its slot into that copy and assigns the whole copy to the renderer. On a mesh already
            // initialised, submesh 1's older copy then overwrites submesh 0's new material: only the last submesh changed
            // (found in game 2026-09-30: the cart body's corner posts, submesh 1, recoloured and the planks, submesh 0,
            // did not). The game only sets LUTs before a part initialises, while every cache is still empty, so each
            // submesh reads the renderer's current list in turn. Emptying the caches first gives that same order.
            private static readonly AccessTools.FieldRef<Object3DSubMesh, Material[]> SubMeshMaterialCache =
                AccessTools.FieldRefAccess<Object3DSubMesh, Material[]>("cachedSharedMaterials");

            private void SetLut(Object3DMesh m, Texture2D lut)
            {
                for (int i = 0; i < m.SubMeshCount; i++)
                {
                    Object3DSubMesh sm = m.GetSubMesh(i);
                    if (sm != null) SubMeshMaterialCache(sm) = null;
                }
                m.SetLutTexture(lut);
            }

            // True when every material slot of the mesh has the game's LUT material (the check behind SetLut).
            private bool AllSlotsLut(Object3DMesh m)
            {
                Renderer r = m.ObjRenderer;
                Material lutMat = LazyBearTechnology.LazySingletonSO<GlobalResources>.Instance?.matObject3DLUT;
                if (r == null || lutMat == null) return true; // nothing to compare against
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length && i < m.SubMeshCount; i++)
                {
                    if (mats[i] != lutMat) return false;
                }
                return true;
            }

            private bool SpriteUpToDate(SpriteTarget t)
            {
                Material m = t.Mine;
                int gi = (int)t.G;
                return appliedMethod == SpriteColorMethod.Palette
                    ? m.IsKeywordEnabled(KwPalette) && !m.IsKeywordEnabled(KwLut) && m.GetTexture(IdPalette) == palettes[gi]
                    : m.IsKeywordEnabled(KwLut) && !m.IsKeywordEnabled(KwPalette) && m.GetTexture(IdLut) == luts[gi];
            }

            private void SetSpriteKeywords(SpriteTarget t)
            {
                Material m = t.Mine;
                int gi = (int)t.G;
                if (appliedMethod == SpriteColorMethod.Palette)
                {
                    m.DisableKeyword(KwLut);
                    m.EnableKeyword(KwPalette);
                    m.SetTexture(IdPalette, palettes[gi]);
                }
                else
                {
                    m.DisableKeyword(KwPalette);
                    m.EnableKeyword(KwLut);
                    m.SetTexture(IdLut, luts[gi]);
                }
            }

            private void RestoreSprite(SpriteTarget t)
            {
                if (t.Mine == null) return;
                try
                {
                    if (t.R != null && t.R && t.R.sharedMaterial == t.Mine)
                    {
                        t.R.sharedMaterial = t.Orig;
                    }
                    UnityEngine.Object.Destroy(t.Mine);
                }
                catch
                {
                    // the view may be half destroyed
                }
                t.Mine = null;
                t.Orig = null;
                t.Warned = false;
            }

            private void RestoreMesh(MeshTarget t)
            {
                if (!t.Applied) return;
                try
                {
                    if (t.M != null && t.M)
                    {
                        SetLut(t.M, t.OrigLut); // null: the game's own material again (matObject3D)
                        Renderer r = t.M.ObjRenderer;
                        string now = MaterialNames(r);
                        if (now != t.OrigMaterials && r != null && t.OrigMats != null)
                        {
                            // exactly what the part had, for the pool; the submeshes then re-read it
                            r.sharedMaterials = t.OrigMats;
                            for (int i = 0; i < t.M.SubMeshCount; i++)
                            {
                                Object3DSubMesh sm = t.M.GetSubMesh(i);
                                if (sm != null) SubMeshMaterialCache(sm) = null;
                            }
                            Plugin.Log.LogInfo($"Colors: '{t.M.name}' had {now} after the LUT was removed; put back to {t.OrigMaterials}.");
                        }
                    }
                }
                catch
                {
                }
                t.Applied = false;
                t.OrigLut = null;
            }

            private static string MaterialNames(Renderer r)
            {
                if (r == null) return "-";
                var names = new List<string>();
                foreach (Material m in r.sharedMaterials)
                {
                    names.Add(m != null ? m.name : "null");
                }
                return string.Join("+", names);
            }

            // ------------------------------------------------------------------ lamp colours (called by Lantern.ApplyLight)

            internal static Color GlowRgb(Color game)
            {
                if (Enabled.Value && TryHex(GlowColor.Value, out Color32 c))
                {
                    Color k = c;
                    k.a = game.a;
                    return k;
                }
                return game;
            }

            internal static Color CharacterLight(Color current)
            {
                return Enabled.Value && TryHex(CharacterLightColor.Value, out Color32 c) ? (Color)c : current;
            }

            private static readonly int IdTintColor = Shader.PropertyToID("_TintColor");
            private SpriteRenderer glassTinted;
            private bool glassHadBlock;
            private MaterialPropertyBlock glassOrigBlock, glassBlock;

            // The lit glass (Custom/Day Night Object (Unlit)): its _TintColor times the tint, through a property block.
            internal void ApplyGlass(SpriteRenderer glass)
            {
                if (glass == null || !glass) return;
                bool want = Enabled.Value && TryHex(LitGlassTint.Value, out Color32 tint) && !(tint.r == 255 && tint.g == 255 && tint.b == 255);
                if (!want)
                {
                    RestoreGlass();
                    return;
                }
                TryHex(LitGlassTint.Value, out Color32 t);
                if (glassTinted != glass)
                {
                    RestoreGlass();
                    glassTinted = glass;
                    glassHadBlock = glass.HasPropertyBlock();
                    glassOrigBlock = new MaterialPropertyBlock();
                    if (glassHadBlock) glass.GetPropertyBlock(glassOrigBlock);
                    glassBlock = new MaterialPropertyBlock();
                }
                Material mat = glass.sharedMaterial;
                Color baseTint = mat != null && mat.HasProperty(IdTintColor) ? mat.GetColor(IdTintColor) : new Color(0.5f, 0.5f, 0.5f, 0.5f);
                Color tc = t;
                glass.GetPropertyBlock(glassBlock);
                glassBlock.SetColor(IdTintColor, new Color(baseTint.r * tc.r, baseTint.g * tc.g, baseTint.b * tc.b, baseTint.a));
                glass.SetPropertyBlock(glassBlock);
            }

            internal void RestoreGlass()
            {
                if (glassTinted == null) return;
                try
                {
                    if (glassTinted)
                    {
                        glassTinted.SetPropertyBlock(glassHadBlock ? glassOrigBlock : null);
                    }
                }
                catch
                {
                }
                glassTinted = null;
            }

            // ------------------------------------------------------------------ panel helpers

            internal static IReadOnlyList<MountColorRegion> RegionList => ApiRegions;

            // What a source colour of a region becomes with the current settings (the panel's swatches).
            internal static Color32 Preview(string regionKey, Color32 src)
            {
                foreach (RegionDef d in Regions)
                {
                    if (d.Key == regionKey)
                    {
                        return Enabled.Value ? new RegionMap(d.S).Apply(src) : src;
                    }
                }
                return src;
            }

            internal static bool RegionChanged(string regionKey)
            {
                RegionSettings s = SettingsOf(regionKey);
                return s != null && !s.IsIdentity;
            }

            // One line per recoloured renderer, for reports from the game (the panel's Diagnose button).
            internal string Diagnose()
            {
                var sb = new StringBuilder();
                sb.Append($"[colors] enabled {Enabled.Value}, sprite method {SpriteMethod.Value}; {Note}; active groups:");
                for (int gi = 0; gi < GroupCount; gi++)
                {
                    if (groupActive[gi]) sb.Append(' ').Append((Group)gi);
                }
                sb.AppendLine();
                foreach (SpriteTarget t in sprites)
                {
                    if (t.R == null || !t.R) { sb.AppendLine("  sprite (destroyed)"); continue; }
                    Material m = t.R.sharedMaterial;
                    sb.AppendLine($"  sprite '{Path(t.R.transform)}' {t.G}: sprite {t.R.sprite?.name}, enabled {t.R.enabled}, color {t.R.color}, material {m?.name} (ours {m == t.Mine && m != null}), shader {m?.shader?.name}, keywords [{(m != null ? string.Join(" ", m.shaderKeywords) : "")}], palette {Tex(m, IdPalette)}, lut {Tex(m, IdLut)}");
                }
                foreach (MeshTarget t in meshes)
                {
                    if (t.M == null || !t.M) { sb.AppendLine("  mesh (destroyed)"); continue; }
                    Object3DSubMesh sm = t.M.GetSubMesh(0);
                    sb.AppendLine($"  mesh '{Path(t.M.transform)}' {t.G}: texture {sm?.MainTextureName}, submeshes {t.M.SubMeshCount}, lut {(sm?.LutTexture != null ? sm.LutTexture.name : "none")}, active {t.M.gameObject.activeInHierarchy}, materials {MaterialNames(t.M.ObjRenderer)}");
                }
                sb.Append($"  glass tint {(glassTinted != null ? "on" : "off")}, glow {GlowColor.Value}, character light {CharacterLightColor.Value}");
                return sb.ToString();
            }

            private static string Tex(Material m, int id)
            {
                if (m == null || !m.HasProperty(id)) return "-";
                Texture t = m.GetTexture(id);
                return t != null ? $"{t.name} {t.width}x{t.height}" : "none";
            }

            private static string Path(Transform t)
            {
                string p = t.name;
                for (Transform q = t.parent; q != null && q.parent != null; q = q.parent)
                {
                    p = q.name + "/" + p;
                    if (p.Length > 90) break;
                }
                return p;
            }
        }
    }
}

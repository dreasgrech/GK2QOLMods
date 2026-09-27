using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace GK2MountProbe
{
    // Dumps the character sprite atlases (keeper and NPC/donkey) as PNGs plus a CSV index of every sprite on them
    // (name, atlas, pixel rect, pivot), and the palette textures the skin shader recolours them with. Atlas textures
    // are not CPU-readable, so they are blitted through a RenderTexture first. Output: BepInEx\GK2-sprites\.
    internal static class SpriteDump
    {
        public static readonly string OutputDir = Path.Combine(Paths.BepInExRootPath, "GK2-sprites");

        public static void Run(StringBuilder sb)
        {
            Directory.CreateDirectory(OutputDir);
            Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
            sb.AppendLine($"sprites loaded in memory: {all.Length}");

            var textures = new Dictionary<Texture2D, string>();
            var csv = new StringBuilder();
            csv.AppendLine("sprite,atlas,x,y,w,h,pivot_x,pivot_y,ppu,frame_w,frame_h,offset_x,offset_y");
            int rows = 0;
            foreach (Sprite s in all)
            {
                if (s == null)
                {
                    continue;
                }
                Texture2D tex;
                try
                {
                    tex = s.texture;
                }
                catch
                {
                    continue;
                }
                if (tex == null)
                {
                    continue;
                }
                string tn = tex.name ?? "";
                bool wanted = tn.Contains("Characters") || tn.Contains("MainCharacter") || s.name.StartsWith("0301_") || s.name.StartsWith("90");
                if (!wanted)
                {
                    continue;
                }
                if (!textures.ContainsKey(tex))
                {
                    textures[tex] = SafeFileName(tn) + ".png";
                }
                Rect r;
                try
                {
                    r = s.textureRect;
                }
                catch
                {
                    r = s.rect;
                }
                Vector2 off = Vector2.zero; try { off = s.textureRectOffset; } catch { }
                csv.AppendLine($"{s.name},{textures[tex]},{r.x:0},{r.y:0},{r.width:0},{r.height:0},{s.pivot.x:0.##},{s.pivot.y:0.##},{s.pixelsPerUnit:0.##},{s.rect.width:0},{s.rect.height:0},{off.x:0},{off.y:0}");
                rows++;
            }
            File.WriteAllText(Path.Combine(OutputDir, "sprites.csv"), csv.ToString());
            sb.AppendLine($"sprite index rows: {rows} -> {Path.Combine(OutputDir, "sprites.csv")}");

            foreach (KeyValuePair<Texture2D, string> kv in textures)
            {
                string path = Path.Combine(OutputDir, kv.Value);
                try
                {
                    DumpTexture(kv.Key, path);
                    sb.AppendLine($"atlas {kv.Key.name} {kv.Key.width}x{kv.Key.height} format={kv.Key.format} -> {kv.Value}");
                }
                catch (Exception e)
                {
                    sb.AppendLine($"atlas {kv.Key.name} FAILED: {e.Message}");
                }
            }

            DumpPalettes(sb);
        }

        private static void DumpPalettes(StringBuilder sb)
        {
            try
            {
                SkinPresetGK2 keeper = PlayerSkinHelper.CurrentPreset;
                DumpPreset(sb, "keeper", keeper);
            }
            catch (Exception e)
            {
                sb.AppendLine("keeper preset: " + e.Message);
            }
            try
            {
                SkinPresetGK2 donkey = SkinPresetGK2.LoadAsset("0301_donkey");
                DumpPreset(sb, "donkey", donkey);
            }
            catch (Exception e)
            {
                sb.AppendLine("donkey preset: " + e.Message);
            }
        }

        private static void DumpPreset(StringBuilder sb, string label, SkinPresetGK2 preset)
        {
            if (preset == null)
            {
                sb.AppendLine($"{label} preset: null");
                return;
            }
            sb.AppendLine($"{label} preset {preset.name}: hue={preset.hue} sat={preset.saturation} vel={preset.velocity} contrast={preset.contrast} bright={preset.brightness} palette={(preset.palette ? preset.palette.name : "none")}");
            DumpPalette(sb, label + "_preset", preset.palette);
            DumpPart(sb, label + "_body", preset.body);
            DumpPart(sb, label + "_head", preset.head);
            DumpPart(sb, label + "_arms", preset.arms);
            DumpPart(sb, label + "_beard", preset.beard);
            DumpPart(sb, label + "_hair", preset.hairstyle);
        }

        private static void DumpPart(StringBuilder sb, string label, SkinPresetPartGK2 part)
        {
            if (part == null)
            {
                return;
            }
            sb.AppendLine($"  {label}: id={part.id} color={Report.F(part.color)} hue={part.hue} sat={part.saturation} vel={part.velocity} replace={part.colorReplaceType} palette={(part.palette ? part.palette.name + " " + part.palette.width + "x" + part.palette.height : "none")}");
            DumpPalette(sb, label, part.palette);
        }

        private static void DumpPalette(StringBuilder sb, string label, Texture2D palette)
        {
            if (palette == null)
            {
                return;
            }
            try
            {
                DumpTexture(palette, Path.Combine(OutputDir, "palette_" + label + "_" + SafeFileName(palette.name) + ".png"));
            }
            catch (Exception e)
            {
                sb.AppendLine($"  palette {label} FAILED: {e.Message}");
            }
        }

        private static void DumpTexture(Texture2D tex, string path)
        {
            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture prev = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                copy.Apply();
                File.WriteAllBytes(path, copy.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null)
                {
                    UnityEngine.Object.Destroy(copy);
                }
            }
        }

        private static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, '_');
            }
            return string.IsNullOrEmpty(s) ? "unnamed" : s;
        }
    }
}

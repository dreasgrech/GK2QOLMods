using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;

namespace GK2Mounts
{
    // The mod's default values live in defaults.cfg next to the sources, embedded into the DLL at build time.
    // Config.Bind uses them as the defaults (so a fresh install, "Reset" and right-click reset all land there), and
    // the tuning panel can write the current values back to that file, so tuning never needs a code edit: tune,
    // export, rebuild. Values missing from the file fall back to the constants in code.
    internal static class Defaults
    {
        private const string ResourceName = "GK2Mounts.defaults.cfg";
        private static Dictionary<string, string> values;

        private static void Load()
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (Stream s = typeof(Defaults).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (s == null)
                    {
                        return;
                    }
                    using (var r = new StreamReader(s))
                    {
                        Parse(r.ReadToEnd());
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("Defaults: could not read embedded defaults: " + e.Message);
            }
        }

        private static void Parse(string text)
        {
            string section = "";
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                values[section + "." + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }

        public static T Get<T>(string section, string key, T fallback)
        {
            if (values == null)
            {
                Load();
            }
            if (!values.TryGetValue(section + "." + key, out string raw))
            {
                return fallback;
            }
            try
            {
                Type t = typeof(T);
                if (t == typeof(float)) return (T)(object)float.Parse(raw, CultureInfo.InvariantCulture);
                if (t == typeof(int)) return (T)(object)int.Parse(raw, CultureInfo.InvariantCulture);
                if (t == typeof(bool)) return (T)(object)bool.Parse(raw);
                if (t.IsEnum) return (T)Enum.Parse(t, raw, ignoreCase: true);
                if (t == typeof(string)) return (T)(object)raw;
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"Defaults: bad value for {section}.{key} = '{raw}' ({e.Message}); using {fallback}.");
            }
            return fallback;
        }

        // Writes the current values of both styles (and the General keys that shape the look) in the embedded
        // file's format to `path`, so the next build ships them as defaults.
        public static string Export(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# GK2 Mounts: default values embedded into the DLL at build time and used as the mod's defaults.");
            sb.AppendLine("# Written by the tuning panel's \"Export current values as mod defaults\" button; rebuild to bake them in.");
            sb.AppendLine("# Format: [Section] then Key = Value, same names as anvil.gk2.mounts.cfg.");
            sb.AppendLine();
            sb.AppendLine("[General]");
            Line(sb, Plugin.SideViewOnly);
            Line(sb, Plugin.StayMountedThroughDoors);
            Line(sb, Plugin.DismountIndoors);
            Line(sb, Plugin.ShowMessages);
            sb.AppendLine();
            foreach (StyleSettings s in new[] { Plugin.Ride, Plugin.Cart })
            {
                sb.AppendLine("[" + s.Section + "]");
                Line(sb, s.SpeedMultiplier); Line(sb, s.Depth); Line(sb, s.CenterOffset); Line(sb, s.RiderOffsetPx); Line(sb, s.TorsoOffsetPx);
                Line(sb, s.AutoSeat); Line(sb, s.SeatAdjustPx); Line(sb, s.SeatOffset); Line(sb, s.Bob); Line(sb, s.Pose); Line(sb, s.FreezePoseWhenIdle); Line(sb, s.PoseSpeed); Line(sb, s.PoseSpeedFollowsDonkey);
                Line(sb, s.NearLeg); Line(sb, s.LegSource); Line(sb, s.LegHeightPx); Line(sb, s.LegAdjustXPx); Line(sb, s.LegAdjustYPx); Line(sb, s.SnapToGround); Line(sb, s.CartFollowsGround); Line(sb, s.CartGroundMaxShift); Line(sb, s.CargoDrawCorpse); Line(sb, s.CargoBedDepth); Line(sb, s.CargoSideGap); Line(sb, s.CargoOverhang); Line(sb, s.CargoNestle);
                Line(sb, s.LanternAlong); Line(sb, s.LanternUp); Line(sb, s.LanternAcross); Line(sb, s.LanternTilt); Line(sb, s.LanternLean); Line(sb, s.LanternFlip);
                Line(sb, s.LanternSwayDegrees); Line(sb, s.LanternSwaySeconds); Line(sb, s.IdleFidgetEverySeconds); Line(sb, s.IdleFidgetSpeed); Line(sb, s.IdleCrawlSpeed);
                sb.AppendLine();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        private static void Line<T>(StringBuilder sb, ConfigEntry<T> e)
        {
            string v = e.Value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : e.Value.ToString();
            sb.AppendLine(e.Definition.Key + " = " + v);
        }
    }
}

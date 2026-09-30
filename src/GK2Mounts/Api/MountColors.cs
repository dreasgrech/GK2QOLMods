using System.Collections.Generic;

namespace GK2Mounts.Api
{
    // How the donkey's sprites (body, head, cart wheel sprites) are recoloured (Mounts.Colors key "SpriteMethod").
    // Palette: the game's exact colour swap (the keeper's outfit uses it): every colour of the art is matched exactly,
    // so each region changes alone. Lut: the game's colour lookup cube (zombie recolours use it): 32 steps per
    // channel, so a few near-identical colours of neighbouring regions change together. A fallback in case the
    // palette does not render on some setup; the cart and lantern are always done with the lookup cube.
    public enum SpriteColorMethod
    {
        Palette,
        Lut,
    }

    // One colour region of the mount (Mounts.ColorRegions): a named set of the art's own colours, recoloured
    // together. Its settings are Mounts.Colors keys: Key + one of SettingNames, e.g. "CoatHue", "HarnessSwaps".
    public sealed class MountColorRegion
    {
        // The settings every region has, in panel order. Hue: degrees, -180..180. Saturation, Brightness, Contrast:
        // multipliers, 0..3 (1 = unchanged). Colorize: hex RRGGBB giving every colour that hue and saturation at
        // its own brightness ("" = off), mixed in by ColorizeAmount (0..1). Tint: hex multiplied in ("" = off).
        // Overlay: hex mixed over the result by OverlayAmount (0..1; "" = off). Swaps: exact replacements that win
        // over everything else, "SRC=DST" pairs of hex colours separated by ';' (SRC one of SourceColors).
        public static readonly IReadOnlyList<string> SettingNames = new[]
        {
            "Hue", "Saturation", "Brightness", "Contrast", "Colorize", "ColorizeAmount", "Tint", "Overlay", "OverlayAmount", "Swaps",
        };

        // "Coat", "Harness", "CartBody", ...
        public string Key { get; }

        // For people: "Donkey coat".
        public string Name { get; }

        // "Donkey", "Cart" or "Lantern".
        public string Area { get; }

        // The game's colours this region covers (hex RRGGBB, the art's exact values), most used first.
        public IReadOnlyList<string> SourceColors { get; }

        internal MountColorRegion(string key, string name, string area, IReadOnlyList<string> sourceColors)
        {
            Key = key;
            Name = name;
            Area = area;
            SourceColors = sourceColors;
        }

        // The Mounts.Colors key of one of this region's settings: KeyOf("Hue") = "CoatHue".
        public string KeyOf(string setting) => Key + setting;

        public override string ToString() => Key;
    }
}

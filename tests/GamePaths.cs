using System.IO;
using System.Linq;
using System.Reflection;

// The game install the tests check against: GameDir from the repo's Directory.Build.props, recorded at build time
// (tests/Directory.Build.props). Folders end in a separator so `Managed + "Assembly-CSharp.dll"` works.
internal static class GamePaths
{
    public static readonly string GameDir = typeof(GamePaths).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "GameDir").Value
        ?? throw new InvalidDataException("GameDir was recorded empty: check GameDir in Directory.Build.props.");

    public static string Managed => Path.Combine(GameDir, "GraveyardKeeper2_Data", "Managed") + Path.DirectorySeparatorChar;

    public static string BepInExCore => Path.Combine(GameDir, "BepInEx", "core") + Path.DirectorySeparatorChar;
}

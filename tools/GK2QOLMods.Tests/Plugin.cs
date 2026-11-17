using BepInEx;

namespace GK2QOLMods.Tests
{
    // Dev-only: the in-game tests of the mods in this repository. At its start it adds a suite for each mod it has
    // tests for to GK2 Mod Test Bench, whose panel (F4) lists them and runs them. Nothing here touches a mod's types
    // before its suite runs: a mod that isn't installed has its tests skipped with that reason, and this plugin still
    // loads.
    [BepInPlugin(Guid, "GK2 QOL Mods: in-game tests", "0.1.0")]
    [BepInDependency(GK2ModTestBench.TestBench.Guid)]
    [BepInDependency(TechTreeSearchTests.ModGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(EventDecorationsTests.ModGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.qolmods.tests";

        private void Awake()
        {
            TechTreeSearchTests.Register();
            EventDecorationsTests.Register();
            Logger.LogInfo("The QOL mods' in-game tests are in GK2 Mod Test Bench (its panel's key is in dreas.gk2.modtestbench.cfg, F4 unless changed).");
        }
    }
}

using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using GK2Mounts.Api;

namespace GK2MountsTechGate
{
    // Example for mod authors (docs/mounts-api.md): tie parts of the mount to the game's tech tree.
    //   - A mount rule: the cart style only once the tech in CartTech is unlocked; the refusal is the keeper's bubble.
    //   - A feature rule: the cart lamp is lit only once the tech in LanternLightTech is unlocked.
    //   - KnowledgeSystem.OnTechUnlocked: the moment a tech is learned, ask the rules again (RefreshRules), so the lamp
    //     lights at once instead of within half a second.
    // Rules are asked when they matter, so they read the game's state live: no need to remember what was unlocked,
    // and a loaded save is simply what the next question sees. At every game start the unlocked techs are logged, to
    // pick ids from.
    [BepInPlugin(Guid, "GK2 Mounts example: tech gate", "0.1.0")]
    [BepInDependency("dreas.gk2.mounts", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.mounts.example.techgate";

        private ConfigEntry<string> cartTech, lanternLightTech;

        private void Awake()
        {
            cartTech = Config.Bind("Gates", "CartTech", "resurrection_advanced",
                "Tech id the cart needs (empty = no gate). The unlocked ones are listed in the log at every game start.");
            lanternLightTech = Config.Bind("Gates", "LanternLightTech", "",
                "Tech id the cart lamp's light needs (empty = no gate).");

            if (Mounts.ApiVersion < new Version(0, 2, 0))
            {
                Logger.LogError($"GK2 Mounts API {Mounts.ApiVersion} is too old for this example (needs 0.2 or newer).");
                return;
            }

            // Asked before every mount: null = allowed, a text = refused with that text.
            Mounts.AddMountRule(Guid, style =>
                style == MountStyle.Cart && !Has(cartTech.Value)
                    ? $"I don't know how to drive a cart yet ({cartTech.Value})."
                    : null);

            // Asked at mount and about twice a second while riding: false switches the lamp's light off.
            Mounts.AddFeatureRule(Guid, MountFeature.LanternLight, () => Has(lanternLightTech.Value));

            // Static game event, survives save loads: re-ask the rules as soon as a tech is learned.
            KnowledgeSystem.OnTechUnlocked += (techId, silent) =>
            {
                Logger.LogInfo($"Tech unlocked: {techId}.");
                Mounts.RefreshRules();
            };
            MainGame.OnGameStarted += LogTechs;

            Logger.LogInfo($"Tech gate loaded: cart needs '{cartTech.Value}', lamp light needs '{lanternLightTech.Value}'.");
        }

        // Empty id = no gate. Outside a loaded game nothing is unlocked.
        private static bool Has(string techId)
        {
            if (string.IsNullOrEmpty(techId))
            {
                return true;
            }
            KnowledgeSystem k = MainGame.Instance?.GameSave?.knowledgeSystem;
            return k != null && k.IsTechUnlocked(techId);
        }

        private void LogTechs()
        {
            try
            {
                KnowledgeSystem k = MainGame.Instance?.GameSave?.knowledgeSystem;
                var all = GameBalance.Me.techDefs;
                var unlocked = new List<string>();
                foreach (TechDef t in all)
                {
                    if (t != null && k != null && k.IsTechUnlocked(t.id))
                    {
                        unlocked.Add(t.id);
                    }
                }
                Logger.LogInfo($"Unlocked techs ({unlocked.Count} of {all.Count}): {string.Join(", ", unlocked)}.");
                Logger.LogInfo($"Cart gate '{cartTech.Value}': {(Has(cartTech.Value) ? "open" : "closed")}; lamp light gate '{lanternLightTech.Value}': {(Has(lanternLightTech.Value) ? "open" : "closed")}.");
            }
            catch (Exception e)
            {
                Logger.LogWarning("Listing the techs failed: " + e.Message);
            }
        }
    }
}

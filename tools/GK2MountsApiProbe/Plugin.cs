using System;
using BepInEx;
using GK2Mounts.Api;
using UnityEngine;

namespace GK2MountsApiProbe
{
    // Dev-only test of the GK2 Mounts API, written like another author's mod would be (docs/mounts-api-design.md).
    //   F6  mount in Cart style through the API as this mod (owner = this GUID); again: dismount
    //   F7  while mounted: switch to the other style
    //   F9  toggle a "blocked" rule: refuses mounting, and dismounts a ride within half a second
    //   F10 step through the features: each press switches exactly the next one off (Cargo, Lantern, LanternLight,
    //       Sounds, Bell, StayThroughDoors), then one more press has all on again
    // Every event and result is written to the log with the prefix [api probe].
    [BepInPlugin(Guid, "GK2 Mounts API probe", "0.1.0")]
    [BepInDependency("dreas.gk2.mounts")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.mountsapiprobe";

        private static readonly MountFeature[] Features = (MountFeature[])Enum.GetValues(typeof(MountFeature));

        private bool blocked;
        private int featureOff = -1; // index into Features of the one switched off, -1 = all on

        private void Awake()
        {
            Logger.LogInfo($"[api probe] GK2 Mounts API {Mounts.ApiVersion}, available {Mounts.IsAvailable}.");
            Mounts.Mounted += style => Logger.LogInfo($"[api probe] event Mounted: {style}, owner {Mounts.Owner}.");
            Mounts.Dismounted += reason => Logger.LogInfo($"[api probe] event Dismounted: {reason}.");
            Mounts.CargoLoaded += id => Logger.LogInfo($"[api probe] event CargoLoaded: {id}.");
            Mounts.CargoUnloaded += id => Logger.LogInfo($"[api probe] event CargoUnloaded: {id}.");
            Mounts.AddMountRule(Guid, style => blocked ? "The API probe says no." : null);
            Mounts.AddStayRule(Guid, () => blocked ? "The API probe ended the ride." : null);
            foreach (MountFeature f in Features)
            {
                MountFeature feature = f;
                Mounts.AddFeatureRule(Guid, feature, () => featureOff < 0 || Features[featureOff] != feature);
            }
        }

        private void Update()
        {
            try
            {
                if (UnityInput.Current.GetKeyDown(KeyCode.F6))
                {
                    if (Mounts.IsMounted)
                    {
                        Mounts.Dismount("the API probe dismounted");
                        Logger.LogInfo("[api probe] F6: Dismount called.");
                    }
                    else
                    {
                        MountResult r = Mounts.TryMount(Guid, MountStyle.Cart);
                        Logger.LogInfo($"[api probe] F6: TryMount(Cart) -> {r}.");
                    }
                }
                if (UnityInput.Current.GetKeyDown(KeyCode.F7))
                {
                    MountStyle other = Mounts.Style == MountStyle.Cart ? MountStyle.Ride : MountStyle.Cart;
                    MountResult r = Mounts.SwitchStyle(other);
                    Logger.LogInfo($"[api probe] F7: SwitchStyle({other}) -> {r}; now {(Mounts.IsMounted ? Mounts.Style.ToString() : "on foot")}, owner {Mounts.Owner}.");
                }
                if (UnityInput.Current.GetKeyDown(KeyCode.F9))
                {
                    blocked = !blocked;
                    Logger.LogInfo($"[api probe] F9: blocked = {blocked}.");
                }
                if (UnityInput.Current.GetKeyDown(KeyCode.F10))
                {
                    featureOff = featureOff + 1 >= Features.Length ? -1 : featureOff + 1;
                    Mounts.RefreshRules();
                    Logger.LogInfo($"[api probe] F10: {(featureOff < 0 ? "all features on" : Features[featureOff] + " off")}; allowed now: {string.Join(", ", Array.ConvertAll(Features, f => f + "=" + Mounts.IsFeatureAllowed(f)))}.");
                }
            }
            catch (Exception e)
            {
                Logger.LogError("[api probe] " + e);
            }
        }
    }
}

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
    //   F8  tuning: first press overrides speed x3.5, pose Walk and lantern scale 1.6 on the style being ridden (and
    //       logs a clamp test: speed 99 -> the allowed maximum); second press clears every override of this mod
    // Every event and result is written to the log with the prefix [api probe].
    [BepInPlugin(Guid, "GK2 Mounts API probe", "0.1.0")]
    [BepInDependency("dreas.gk2.mounts")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.mountsapiprobe";

        private static readonly MountFeature[] Features = (MountFeature[])Enum.GetValues(typeof(MountFeature));

        private bool blocked;
        private int featureOff = -1; // index into Features of the one switched off, -1 = all on
        private bool tuned;

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
                if (UnityInput.Current.GetKeyDown(KeyCode.F8))
                {
                    MountStyle style = Mounts.IsMounted ? Mounts.Style : MountStyle.Cart;
                    MountTuning t = Mounts.Tuning(style);
                    if (!tuned)
                    {
                        float before = t.Get<float>("SpeedMultiplier");
                        float clamped = t.Set(Guid, "SpeedMultiplier", 99f);
                        t.Set(Guid, "SpeedMultiplier", 3.5f);
                        t.Set(Guid, "Pose", RiderPose.Walk);
                        t.Set(Guid, "LanternScale", 1.6f);
                        tuned = true;
                        Logger.LogInfo($"[api probe] F8: {style}: {t.Keys.Count} keys; speed {before} -> 99 applied as {clamped}; now speed {t.Get<float>("SpeedMultiplier")}, pose {t.Get<RiderPose>("Pose")}, lantern scale {t.Get<float>("LanternScale")}; overridden by {t.OverriddenBy("SpeedMultiplier")}.");
                    }
                    else
                    {
                        Mounts.ClearTuning(Guid);
                        tuned = false;
                        Logger.LogInfo($"[api probe] F8: overrides cleared; {style} speed {t.Get<float>("SpeedMultiplier")}, pose {t.Get<RiderPose>("Pose")}, lantern scale {t.Get<float>("LanternScale")}, overridden {t.IsOverridden("SpeedMultiplier")}.");
                    }
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

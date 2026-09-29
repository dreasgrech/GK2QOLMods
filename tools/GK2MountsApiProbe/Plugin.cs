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
    // Every event and result is written to the log with the prefix [api probe].
    [BepInPlugin(Guid, "GK2 Mounts API probe", "0.1.0")]
    [BepInDependency("dreas.gk2.mounts")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.mountsapiprobe";

        private bool blocked;

        private void Awake()
        {
            Logger.LogInfo($"[api probe] GK2 Mounts API {Mounts.ApiVersion}, available {Mounts.IsAvailable}.");
            Mounts.Mounted += style => Logger.LogInfo($"[api probe] event Mounted: {style}, owner {Mounts.Owner}.");
            Mounts.Dismounted += reason => Logger.LogInfo($"[api probe] event Dismounted: {reason}.");
            Mounts.AddMountRule(Guid, style => blocked ? "The API probe says no." : null);
            Mounts.AddStayRule(Guid, () => blocked ? "The API probe ended the ride." : null);
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
            }
            catch (Exception e)
            {
                Logger.LogError("[api probe] " + e);
            }
        }
    }
}

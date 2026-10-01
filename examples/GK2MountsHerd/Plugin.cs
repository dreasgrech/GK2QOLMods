using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using GK2Mounts.Api;
using UnityEngine;

namespace GK2MountsHerd
{
    // Test mod (docs/mounts-persistence-tests.md, part J): a second mod using the GK2 Mounts API at the same time as the
    // panel and the showcase, keeping its own donkeys with the save. It exercises every multi-mod contract:
    //   - its own numbers: a herd of five donkeys asked for as #1 to #5, the same numbers the panel's first spawns get;
    //   - keys (herd-1 .. herd-5) and its own data on each (a name), found again after a load (Mounts.GameReady, Find);
    //   - a cross-mod foal: one of its foals on the nearest donkey of another mod;
    //   - the ready gate: one spawn tried while a game is still loading, which must be refused;
    //   - a clash: asking again for a number it already has, which must be refused.
    //   SpawnKey (K)        spawn the missing herd donkeys around the keeper; all there: call them to the keeper
    //   RemoveKey (Shift+K) the herd and its foals go (cargo set down first)
    //   FoalKey (U)         a herd foal for the nearest donkey of another mod (or of the herd, if there is none)
    //   ReportKey (Shift+U) every instance of every mod in the log: number, key, kept, data
    // Every line it logs starts with "Herd:". Remove its DLL to test dormant records: its donkeys stay in the side file.
    [BepInPlugin(Guid, "GK2 Herd (test)", "0.1.0")]
    [BepInDependency("dreas.gk2.mounts", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.mounts.example.herd";

        private static readonly Version NeedsApi = new Version(0, 10, 0);
        private static readonly string[] Names = { "Bella", "Moss", "Turnip", "Clover", "Barley" };
        private static readonly string[] FoalNames = { "Pip", "Bean", "Nettle", "Sprout", "Wren", "Fig", "Pebble", "Thistle" };
        private const int HerdSize = 5;

        private ConfigEntry<KeyboardShortcut> spawnKey, removeKey, foalKey, reportKey;
        private ConfigEntry<bool> keepWithSaves;
        private bool ready;
        private object earlyTriedFor; // the game save an early spawn was tried for (once per load)

        private void Awake()
        {
            spawnKey = Config.Bind("Keys", "SpawnKey", new KeyboardShortcut(KeyCode.K), "Spawn the missing herd donkeys around the keeper; all there: call them to him.");
            removeKey = Config.Bind("Keys", "RemoveKey", new KeyboardShortcut(KeyCode.K, KeyCode.LeftShift), "Remove the herd and its foals (cargo set down first).");
            foalKey = Config.Bind("Keys", "FoalKey", new KeyboardShortcut(KeyCode.U), "A herd foal for the nearest donkey of another mod (or of the herd).");
            reportKey = Config.Bind("Keys", "ReportKey", new KeyboardShortcut(KeyCode.U, KeyCode.LeftShift), "Every instance of every mod in the log.");
            keepWithSaves = Config.Bind("Herd", "KeepWithSaves", true, "The herd and its foals are kept with the save.");

            if (Mounts.ApiVersion < NeedsApi)
            {
                Logger.LogError($"Herd: GK2 Mounts API {Mounts.ApiVersion} is too old (needs {NeedsApi}).");
                return;
            }
            Mounts.GameReady += OnGameReady;
            Mounts.InstancesRestored += OnRestored;
            ready = true;
            Logger.LogInfo($"Herd: loaded (API {Mounts.ApiVersion}): spawn {spawnKey.Value}, remove {removeKey.Value}, foal {foalKey.Value}, report {reportKey.Value}.");
        }

        private void OnDestroy()
        {
            Mounts.GameReady -= OnGameReady;
            Mounts.InstancesRestored -= OnRestored;
        }

        // ------------------------------------------------------------------ the load

        private void OnRestored(IReadOnlyList<MountHandle> handles)
        {
            int mine = 0, others = 0;
            foreach (MountHandle h in handles)
            {
                if (h.Owner == Guid) mine++; else others++;
            }
            Logger.LogInfo($"Herd: InstancesRestored: {handles.Count} back, {mine} of them the herd's, {others} of other mods (IsGameReady {Mounts.IsGameReady}).");
        }

        private void OnGameReady()
        {
            var found = new List<string>();
            for (int n = 1; n <= HerdSize; n++)
            {
                MountHandle h = Mounts.Find(Guid, n);
                if (h == null) continue;
                MountHandle byKey = Mounts.Find(Guid, "herd-" + n);
                string check = byKey == h ? "" : " (KEY MISMATCH: Find by key gave " + (byKey?.FullId ?? "nothing") + ")";
                found.Add($"#{n} {h.GetData(Guid, "name") ?? "(no name)"}{check}");
            }
            int foals = 0;
            foreach (MountHandle h in Mounts.InstancesOf(Guid))
            {
                if (h.Parent != null) foals++;
            }
            Logger.LogInfo($"Herd: game ready: {found.Count} of {HerdSize} herd donkeys back [{string.Join(", ", found)}], {foals} foal(s). Mods in the world: {OwnersSummary()}.");

            // A number it already has must be refused.
            if (Mounts.Find(Guid, 1) != null)
            {
                MountHandle again = Mounts.Spawn(Guid, new MountSpawn { Position = Keeper(), Id = 1 });
                Logger.LogInfo(again == null ? "Herd: asking for #1 again was refused, as it should be." : $"Herd: FAIL: asking for #1 again gave {again.FullId}.");
                again?.Remove();
            }
        }

        private void Update()
        {
            if (!ready || !Mounts.IsAvailable)
            {
                return;
            }
            try
            {
                TryEarlySpawn();
                if (!Mounts.IsGameReady)
                {
                    return;
                }
                if (Pressed(removeKey)) RemoveHerd();
                else if (Pressed(spawnKey)) SpawnOrCall();
                else if (Pressed(reportKey)) Report();
                else if (Pressed(foalKey)) AddFoal();
            }
            catch (Exception e)
            {
                Logger.LogError("Herd: " + e);
            }
        }

        // Once per loaded game, while it is still loading: a spawn must be refused (the ready gate).
        private void TryEarlySpawn()
        {
            object save = MainGame.Instance != null ? MainGame.Instance.GameSave : null;
            if (save == null || Mounts.IsGameReady || ReferenceEquals(save, earlyTriedFor))
            {
                return;
            }
            earlyTriedFor = save;
            MountHandle h = Mounts.Spawn(Guid, new MountSpawn { Position = Vector3.zero });
            Logger.LogInfo(h == null ? "Herd: a spawn while the game is still loading was refused, as it should be." : $"Herd: FAIL: a spawn before GameReady gave {h.FullId}.");
            h?.Remove();
        }

        // ------------------------------------------------------------------ keys

        private void SpawnOrCall()
        {
            Vector3 keeper = Keeper();
            int made = 0, called = 0;
            for (int n = 1; n <= HerdSize; n++)
            {
                Vector3 at = Around(keeper, n);
                bool right = n % 2 == 0;
                MountHandle h = Mounts.Find(Guid, n);
                if (h != null)
                {
                    if (!h.IsRidden && h.MoveTo(at, right).Ok) called++;
                    continue;
                }
                h = Mounts.Spawn(Guid, new MountSpawn { Position = at, FacingRight = right, Id = n, Key = "herd-" + n, Persist = keepWithSaves.Value });
                if (h == null)
                {
                    Logger.LogWarning($"Herd: #{n} could not be spawned (see the GK2 Mounts log).");
                    continue;
                }
                h.SetData(Guid, "name", Names[n - 1]);
                h.Tuning.Set(Guid, "IdleShuffle", true);
                made++;
            }
            Logger.LogInfo($"Herd: {made} spawned, {called} called to the keeper.");
        }

        private void RemoveHerd()
        {
            int n = 0;
            foreach (MountHandle h in Mounts.InstancesOf(Guid))
            {
                if (!h.Exists) continue; // a foal that went with its parent
                h.SetDownCargo();
                h.Remove();
                n++;
            }
            Logger.LogInfo($"Herd: removed {n} instance(s).");
        }

        // A herd-owned foal on the nearest grown donkey of another mod: the cross-mod parent link.
        private void AddFoal()
        {
            Vector3 keeper = Keeper();
            MountHandle best = null, bestOwn = null;
            float bestD = float.MaxValue, bestOwnD = float.MaxValue;
            foreach (MountHandle h in Mounts.Instances)
            {
                if (!h.HasDonkey || h.HasCart || h.Parent != null || !h.IsRideable || h.IsRidden) continue;
                float d = Vector3.Distance(h.Position, keeper);
                if (h.Owner != Guid && d < bestD) { best = h; bestD = d; }
                if (h.Owner == Guid && d < bestOwnD) { bestOwn = h; bestOwnD = d; }
            }
            MountHandle parent = best ?? bestOwn;
            if (parent == null)
            {
                Logger.LogInfo("Herd: no donkey without a cart to give a foal to.");
                return;
            }
            int count = Mounts.InstancesOf(Guid).Count;
            MountHandle foal = Mounts.SpawnFoal(Guid, parent, 0.6f, persist: keepWithSaves.Value);
            if (foal == null)
            {
                Logger.LogWarning("Herd: the foal could not be spawned (see the GK2 Mounts log).");
                return;
            }
            foal.SetData(Guid, "name", FoalNames[count % FoalNames.Length]);
            Logger.LogInfo($"Herd: foal {foal.FullId} '{foal.GetData(Guid, "name")}' for {parent.FullId} ({(parent.Owner == Guid ? "the herd's own" : "another mod's")}).");
        }

        private void Report()
        {
            var sb = new StringBuilder();
            sb.Append($"Herd: report, {Mounts.Instances.Count} instance(s):");
            foreach (MountHandle h in Mounts.Instances)
            {
                sb.Append($"\n  {h.FullId}{(h.Key != null ? $" '{h.Key}'" : "")} {(h.HasDonkey ? h.HasCart ? "donkey+cart" : h.Parent != null ? "foal of " + h.Parent.FullId : "donkey" : "free cart")}, kept {h.Persist}, in {h.SceneId}");
                foreach (string k in h.DataKeys(Guid))
                {
                    sb.Append($", herd.{k} = {h.GetData(Guid, k)}");
                }
            }
            Logger.LogInfo(sb.ToString());
        }

        // ------------------------------------------------------------------ helpers

        private static string OwnersSummary()
        {
            var counts = new Dictionary<string, int>();
            foreach (MountHandle h in Mounts.Instances)
            {
                counts.TryGetValue(h.Owner, out int c);
                counts[h.Owner] = c + 1;
            }
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add($"{kv.Key} {kv.Value}");
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        private static Vector3 Keeper()
        {
            PlayerData pd = MainGame.Instance != null ? MainGame.PlayerData : null;
            return pd != null ? pd.position.Value : Vector3.zero;
        }

        // A loose ring around the keeper, wider sideways than up/down (the camera's view).
        private static Vector3 Around(Vector3 keeper, int n)
        {
            float a = (n - 1) * (Mathf.PI * 2f / HerdSize) + 0.4f;
            return keeper + new Vector3(Mathf.Cos(a) * 3f, 0f, Mathf.Sin(a) * 1.6f);
        }

        // The main key pressed this frame with the shortcut's own modifiers held; Shift+K is checked before K.
        private static bool Pressed(ConfigEntry<KeyboardShortcut> key)
        {
            KeyboardShortcut k = key.Value;
            if (k.MainKey == KeyCode.None || !UnityInput.Current.GetKeyDown(k.MainKey))
            {
                return false;
            }
            foreach (KeyCode m in k.Modifiers)
            {
                if (!UnityInput.Current.GetKey(m))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

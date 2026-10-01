using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using GK2Mounts.Api;
using UnityEngine;

namespace GK2Mounts
{
    // Donkeys and carts kept with a save (docs/mounts-persistence.md). The game's save is never touched: right after a
    // save is written (the game's own success callback, SaveSystem_Save_Patch) every instance marked Persist is written
    // to a side file of that slot, <slot>.gk2mounts.dat next to the game's saves. The cargo is in the save
    // already as ground drops at each cart (LendCargoToSave); the side file keeps those items' GUIDs. When that save is
    // loaded again (a new GameSave in Tick) the instances are spawned again, in their own scenes, and their items are
    // taken off the ground back onto their carts by GUID.
    //
    // The side file records the slot's save time and day (GameSave.PrepareToSave sets both on every save). A loaded slot
    // whose values differ (saved again without the mod, a backup, a cloud copy) ignores the file: nothing comes back and
    // the cargo stays on the ground, which is what the save itself says.
    //
    // Why that name and place: Steam Auto-Cloud syncs only the top level of the save folder, and only files its rule
    // for the game matches (*.dat and *.info by what it synced; measured 2026-10-01: a GK2Mounts/ subfolder was never
    // synced, <slot>.gk2mounts.dat was). The game itself never lists or opens it: its save list reads *.info only,
    // a .dat is opened by its exact slot name, and backups are <slot>_backup_N.dat. Text inside, despite the .dat.
    internal static partial class Mount
    {
        internal sealed partial class Instance
        {
            // Kept with the save (MountSpawn.Persist, MountHandle.Persist); never the summon.
            internal bool Persist;

            // The owner's own name for it (MountSpawn.Key), kept with the save, so the owner can find it after a load.
            internal string Key;
        }

        private const int SideFileFormat = 1;

        // For the panel: what the last write or restore did.
        internal static string PersistenceNote { get; private set; } = "Nothing saved or restored yet this session.";

        internal static string SideFile(string slotName) => Path.Combine(SaveSystem.SaveFolder, slotName + ".gk2mounts.dat");

        // ------------------------------------------------------------------ writing (after a successful save)

        internal static void WriteSideFile(SaveSlotData slot)
        {
            if (slot == null || string.IsNullOrEmpty(slot.slotName))
            {
                return;
            }
            string path = SideFile(slot.slotName);

            // Parents first, so a child's parent index points back; a child whose parent isn't kept goes only when it
            // would outlive the parent anyway (RemoveWithParent false), as an orphan.
            var kept = new List<Instance>();
            foreach (Instance i in instances)
            {
                if (i.Persist && !i.Ephemeral && i.Parent == null)
                {
                    kept.Add(i);
                }
            }
            foreach (Instance i in instances)
            {
                if (i.Persist && !i.Ephemeral && i.Parent != null && (kept.Contains(i.Parent) || !i.RemoveWithParent))
                {
                    kept.Add(i);
                }
            }

            if (kept.Count == 0)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Plugin.Log.LogInfo($"Persistence: nothing to keep with {slot.slotName}; its side file was removed.");
                }
                PersistenceNote = $"Last save ({slot.slotName}): nothing kept.";
                return;
            }

            var sb = new StringBuilder();
            sb.Append("# GK2 Mounts: the donkeys and carts kept with this save, written after each save (docs/mounts-persistence.md).\n");
            sb.Append("# The game's own save does not need this file; without it the donkeys simply don't come back.\n");
            Line(sb, "format", SideFileFormat.ToString(CultureInfo.InvariantCulture));
            Line(sb, "slot", slot.slotName);
            Line(sb, "saved", slot.saveDateTime ?? "");
            Line(sb, "day", slot.day.ToString(CultureInfo.InvariantCulture));
            int items = 0;
            foreach (Instance i in kept)
            {
                sb.Append("instance\n");
                Line(sb, "owner", i.OwnerId ?? "");
                if (!string.IsNullOrEmpty(i.Key))
                {
                    Line(sb, "key", i.Key);
                }
                Line(sb, "donkey", Bool(i.HasDonkey));
                Line(sb, "cart", Bool(i.HasCart));
                Line(sb, "scene", i.SceneId ?? "");
                Line(sb, "position", F(i.Position.x), F(i.Position.y), F(i.Position.z));
                Line(sb, "facing", i.FacingRight ? "right" : "left");
                Line(sb, "scale", F(i.ViewScale));
                Line(sb, "rideable", Bool(i.Rideable));
                Line(sb, "parent", (i.Parent != null ? kept.IndexOf(i.Parent) : -1).ToString(CultureInfo.InvariantCulture));
                Line(sb, "removeWithParent", Bool(i.RemoveWithParent));
                foreach (string guid in LentGuidsOf(i))
                {
                    Line(sb, "cargo", guid);
                    items++;
                }
                foreach (var kv in i.Overrides)
                {
                    string value;
                    try
                    {
                        value = TomlTypeConverter.ConvertToString(kv.Value, kv.Value.GetType());
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Persistence: {i.Describe()}'s setting {kv.Key} can't be written ({e.Message}); it isn't kept.");
                        continue;
                    }
                    i.OverrideOwners.TryGetValue(kv.Key, out string by);
                    Line(sb, "set", kv.Key, by ?? "", value);
                }
                sb.Append("end\n");
            }

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
            PersistenceNote = $"Last save ({slot.slotName}): {kept.Count} kept, {items} cargo item(s).";
            Plugin.Log.LogInfo($"Persistence: {kept.Count} donkey(s)/cart(s) with {items} cargo item(s) kept with {slot.slotName} ({path}).");
        }

        internal static void DeleteSideFile(SaveSlotData slot)
        {
            if (slot == null || string.IsNullOrEmpty(slot.slotName))
            {
                return;
            }
            string path = SideFile(slot.slotName);
            if (File.Exists(path))
            {
                File.Delete(path);
                Plugin.Log.LogInfo($"Persistence: save {slot.slotName} was deleted; so was its side file.");
            }
        }

        // ------------------------------------------------------------------ restoring (a save was loaded)

        private sealed class Record
        {
            internal string Owner, Key, Scene;
            internal bool Donkey = true, Cart, FacingRight = true, Rideable = true, RemoveWithParent = true;
            internal Vector3 Position;
            internal float Scale = 1f;
            internal int Parent = -1;
            internal readonly List<string> Cargo = new List<string>();
            internal readonly List<string[]> Sets = new List<string[]>();
        }

        // Called once per loaded game, on its first frame in a scene (MountRegistry.Tick), before the keeper can move.
        internal static void RestoreFromSideFile()
        {
            SaveSlotData slot = MainGame.Instance != null ? MainGame.Instance.SaveSlotData : null;
            if (slot == null || string.IsNullOrEmpty(slot.slotName))
            {
                return;
            }
            string path = SideFile(slot.slotName);
            if (!File.Exists(path))
            {
                PersistenceNote = $"Loaded {slot.slotName}: no side file, nothing to restore.";
                return;
            }

            var header = new Dictionary<string, string>();
            var records = new List<Record>();
            Record cur = null;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (raw.Length == 0 || raw[0] == '#')
                {
                    continue;
                }
                string[] f = raw.Split('\t');
                switch (f[0])
                {
                    case "instance":
                        cur = new Record();
                        records.Add(cur);
                        continue;
                    case "end":
                        cur = null;
                        continue;
                }
                if (cur == null)
                {
                    header[f[0]] = f.Length > 1 ? f[1] : "";
                    continue;
                }
                string a = f.Length > 1 ? f[1] : "";
                switch (f[0])
                {
                    case "owner": cur.Owner = a; break;
                    case "key": cur.Key = a; break;
                    case "donkey": cur.Donkey = a == "true"; break;
                    case "cart": cur.Cart = a == "true"; break;
                    case "scene": cur.Scene = a; break;
                    case "position":
                        if (f.Length >= 4)
                        {
                            cur.Position = new Vector3(P(f[1]), P(f[2]), P(f[3]));
                        }
                        break;
                    case "facing": cur.FacingRight = a != "left"; break;
                    case "scale": cur.Scale = P(a); break;
                    case "rideable": cur.Rideable = a == "true"; break;
                    case "parent": int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out cur.Parent); break;
                    case "removeWithParent": cur.RemoveWithParent = a == "true"; break;
                    case "cargo": cur.Cargo.Add(a); break;
                    case "set":
                        if (f.Length >= 4)
                        {
                            cur.Sets.Add(new[] { f[1], f[2], f[3] });
                        }
                        break;
                }
            }

            header.TryGetValue("format", out string format);
            header.TryGetValue("saved", out string saved);
            header.TryGetValue("day", out string day);
            if (format != SideFileFormat.ToString(CultureInfo.InvariantCulture))
            {
                Plugin.Log.LogWarning($"Persistence: {path} has format {format}, this version reads {SideFileFormat}; nothing restored.");
                PersistenceNote = $"Loaded {slot.slotName}: side file of another format, ignored.";
                return;
            }
            if (saved != (slot.saveDateTime ?? "") || day != slot.day.ToString(CultureInfo.InvariantCulture))
            {
                // The slot was saved again without the side file being written (the mod was off), or the save came
                // back from a backup or the cloud: what the file holds is from another point in time.
                Plugin.Log.LogWarning($"Persistence: {path} belongs to the save of {saved} (day {day}), but {slot.slotName} was saved {slot.saveDateTime} (day {slot.day}); ignored, nothing restored. Any cargo lies on the ground where the carts stood.");
                PersistenceNote = $"Loaded {slot.slotName}: the side file is from another save time, ignored.";
                return;
            }

            var made = new Instance[records.Count];
            var restored = new List<MountHandle>();
            int items = 0, missing = 0;
            for (int k = 0; k < records.Count; k++)
            {
                Record r = records[k];
                Instance parent = r.Parent >= 0 && r.Parent < k ? made[r.Parent] : null;
                if (r.Parent >= 0 && parent == null && r.RemoveWithParent)
                {
                    Plugin.Log.LogInfo($"Persistence: record {k} is a foal whose parent didn't come back; skipped.");
                    continue;
                }
                Instance i = Spawn(string.IsNullOrEmpty(r.Owner) ? Plugin.Guid : r.Owner, r.Position, r.Donkey, r.Cart, r.FacingRight,
                    r.Scale, r.Rideable, parent, r.RemoveWithParent, r.Scene, false, true, string.IsNullOrEmpty(r.Key) ? null : r.Key,
                    out string why, register: false);
                if (i == null)
                {
                    Plugin.Log.LogWarning($"Persistence: record {k} ({r.Owner}, {r.Scene}) could not be spawned again: {why}");
                    continue;
                }
                made[k] = i;
                foreach (string[] s in r.Sets)
                {
                    RestoreSetting(i, s[0], s[1], s[2]);
                }
                i.ColorsRig.MarkDirty(); // its own colours, built with its view
                var carried = new List<Item>();
                foreach (string guid in r.Cargo)
                {
                    Item it = TakeDropBack(i.SceneId, guid);
                    if (it != null)
                    {
                        carried.Add(it);
                    }
                    else
                    {
                        missing++;
                    }
                }
                if (carried.Count > 0)
                {
                    i.CargoRig.SetCarried(carried); // onto its inventory when its view is prepared
                    items += carried.Count;
                }
                Register(i); // InstanceSpawned, now that it is complete
                restored.Add(i.Handle);
            }
            string note = $"{restored.Count} of {records.Count} donkey(s)/cart(s) back, {items} cargo item(s) back on their carts{(missing > 0 ? $", {missing} not found on the ground" : "")}";
            PersistenceNote = $"Loaded {slot.slotName}: {note}.";
            Plugin.Log.LogInfo($"Persistence: {slot.slotName}: {note}.");
            if (restored.Count > 0)
            {
                Mounts.RaiseInstancesRestored(restored);
            }
        }

        private static void RestoreSetting(Instance i, string key, string by, string value)
        {
            try
            {
                ITuned t = i.Handle != null ? i.Handle.Tuning.Find(key) : new InstanceTuning(i).Find(key);
                object v = t.Clamp(TomlTypeConverter.ConvertToValue(value, t.ValueType));
                i.Overrides[t.Key] = v;
                i.OverrideOwners[t.Key] = string.IsNullOrEmpty(by) ? Plugin.Guid : by;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Persistence: {i.Describe()}'s setting {key} = {value} wasn't restored: {e.Message}");
            }
        }

        // The item with this GUID off the ground of its scene (or the scene's queue), or null when it isn't there.
        private static Item TakeDropBack(string sceneId, string guid)
        {
            GameSceneData sd;
            try
            {
                sd = MainGame.WorldData.GetGameSceneDataById(sceneId);
            }
            catch
            {
                return null;
            }
            if (sd == null)
            {
                return null;
            }
            DropData d = sd.droppedItems.Find(x => SameGuid(x, guid)) ?? sd.queuedDrops.Find(x => SameGuid(x, guid));
            if (d == null)
            {
                return null;
            }
            try
            {
                Item it = d.Item;
                MainGame.Instance.dropSystem.RemoveDrop(d, sceneId);
                return it;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Persistence: taking {d.Item?.id} back off the ground failed; it stays there: {e.Message}");
                return null;
            }
        }

        private static bool SameGuid(DropData d, string guid)
        {
            try
            {
                return d?.Item?.UniqueId != null && string.Equals(d.Item.UniqueId.Id, guid, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static void Line(StringBuilder sb, string name, params string[] values)
        {
            sb.Append(name);
            foreach (string v in values)
            {
                sb.Append('\t').Append(Clean(v));
            }
            sb.Append('\n');
        }

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

        private static string Bool(bool b) => b ? "true" : "false";

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static float P(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }
}

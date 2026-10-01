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
    // to a side file of that slot, <slot>.gk2mounts.dat next to the game's saves. The cargo is in the save already as
    // ground drops at each cart (LendCargoToSave); the side file keeps those items' GUIDs. When that save is loaded
    // again the instances are spawned again (BecomeReady, before Mounts.GameReady), in their own scenes, under their
    // own owner and number, and their items are taken off the ground back onto their carts by GUID.
    //
    // The side file records the slot's save time and day (GameSave.PrepareToSave sets both on every save). A loaded slot
    // whose values differ (saved again without the mod, a backup, a cloud copy) ignores the file: nothing comes back and
    // the cargo stays on the ground, which is what the save itself says.
    //
    // Several mods: one file for all, every record carrying its owner and its number within that owner, links by
    // (owner, id). An owner that isn't installed: its records are dormant, not spawned but written back unchanged, so
    // its donkeys come back with the mod (their numbers stay reserved meanwhile).
    //
    // Why that name and place: Steam Auto-Cloud syncs *.dat and *.info at the top of the save folder only (the game's
    // ufs rule, read from Steam's appinfo.vdf 2026-10-01). The game itself never lists or opens it: its save list reads
    // *.info only, a .dat is opened by its exact slot name, and backups are <slot>_backup_N.dat. Text inside.
    internal static partial class Mount
    {
        internal sealed partial class Instance
        {
            // Kept with the save (MountSpawn.Persist, MountHandle.SetPersist); never the summon.
            internal bool Persist;

            // The owner's own name for it (MountSpawn.Key), unique within the owner, kept with the save.
            internal string Key;

            // Any mod's own values on it, by that mod's id, then key (MountHandle.SetData); kept with the save.
            internal readonly Dictionary<string, Dictionary<string, string>> Data = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        }

        // Hitching: the free cart's data goes onto the donkey, without replacing the donkey's own values.
        internal static void MergeData(Instance from, Instance to)
        {
            foreach (var mod in from.Data)
            {
                if (!to.Data.TryGetValue(mod.Key, out Dictionary<string, string> d))
                {
                    to.Data[mod.Key] = d = new Dictionary<string, string>(StringComparer.Ordinal);
                }
                foreach (var kv in mod.Value)
                {
                    if (!d.ContainsKey(kv.Key))
                    {
                        d[kv.Key] = kv.Value;
                    }
                }
            }
        }

        private const int SideFileFormat = 2;

        // How far from where its cart stood an item may lie to be taken back onto it (the lend drops at the cart).
        private const float ReclaimRadius = 3f;

        // For the panel: what the last write or restore did.
        internal static string PersistenceNote { get; private set; } = "Nothing saved or restored yet this game.";

        internal static string SideFile(string slotName) => Path.Combine(SaveSystem.SaveFolder, slotName + ".gk2mounts.dat");

        // ------------------------------------------------------------------ owners

        // The core and its panel: always there, and allowed to change any instance (the panel is the player's tool).
        // Exactly these two: the example mods' GUIDs also start with dreas.gk2.mounts.
        internal static bool IsCoreOwner(string owner) => owner == Plugin.Guid || owner == TuningWindow.PanelOwner;

        // Any owner but the core's is a BepInEx plugin GUID.
        internal static bool OwnerPresent(string owner)
        {
            if (string.IsNullOrEmpty(owner))
            {
                return false;
            }
            if (IsCoreOwner(owner))
            {
                return true;
            }
            try
            {
                return BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(owner);
            }
            catch
            {
                return true; // can't tell: treat as there rather than hide its donkeys
            }
        }

        // The owner's name for the panel: the plugin's own name, else its id.
        internal static string OwnerName(string owner)
        {
            if (owner == Plugin.Guid)
            {
                return "GK2 Mounts";
            }
            if (owner == TuningWindow.PanelOwner)
            {
                return "GK2 Mounts panel";
            }
            try
            {
                if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(owner, out BepInEx.PluginInfo p) && p?.Metadata != null)
                {
                    return p.Metadata.Name;
                }
            }
            catch
            {
            }
            return owner;
        }

        // ------------------------------------------------------------------ dormant records (owner not installed)

        internal sealed class DormantRecord
        {
            internal string Owner, Key, Scene, Kind, Parent;
            internal int Id, Cargo;
            internal Vector3 Position;
            internal string Why;
            internal List<string> Lines; // the record as read, written back unchanged
            internal string FullId => FullIdOf(Owner, Id);
        }

        private static readonly List<DormantRecord> dormant = new List<DormantRecord>();

        internal static IReadOnlyList<DormantRecord> Dormant => dormant;

        internal static bool IsDormant(string owner, int id)
        {
            foreach (DormantRecord d in dormant)
            {
                if (d.Id == id && d.Owner == owner)
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool DormantKeyTaken(string owner, string key)
        {
            foreach (DormantRecord d in dormant)
            {
                if (d.Owner == owner && d.Key == key)
                {
                    return true;
                }
            }
            return false;
        }

        // The panel's "Forget": not written into the next side file, so it is gone with the next save (its cargo, if
        // any, stays where it lies). Its children's records follow it.
        internal static void ForgetDormant(DormantRecord d)
        {
            if (!dormant.Remove(d))
            {
                return;
            }
            Plugin.Log.LogInfo($"Persistence: forgot the dormant {d.FullId} {d.Kind}; it is gone with the next save.");
            foreach (DormantRecord c in dormant.ToArray())
            {
                if (c.Parent == d.FullId)
                {
                    ForgetDormant(c);
                }
            }
        }

        // ------------------------------------------------------------------ writing (after a successful save)

        internal static void WriteSideFile(SaveSlotData slot)
        {
            if (slot == null || string.IsNullOrEmpty(slot.slotName))
            {
                return;
            }
            string path = SideFile(slot.slotName);

            // Parents first (only for reading the file by eye: links are by identity); a child whose parent isn't kept
            // goes only when it would outlive the parent anyway (RemoveWithParent false), as an orphan.
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
                if (!i.Persist || i.Ephemeral || i.Parent == null)
                {
                    continue;
                }
                if (kept.Contains(i.Parent) || !i.RemoveWithParent)
                {
                    kept.Add(i);
                }
                else
                {
                    Plugin.Log.LogInfo($"Persistence: {i.Describe()} is kept, but its parent {i.Parent.FullId} isn't; it goes with its parent at a reload.");
                }
            }

            if (kept.Count == 0 && dormant.Count == 0)
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
            foreach (var kv in nextIds)
            {
                Line(sb, "next", kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture));
            }
            int items = 0;
            foreach (Instance i in kept)
            {
                sb.Append("instance\n");
                Line(sb, "owner", i.OwnerId);
                Line(sb, "id", i.Id.ToString(CultureInfo.InvariantCulture));
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
                if (i.Parent != null && kept.Contains(i.Parent))
                {
                    Line(sb, "parent", i.Parent.OwnerId, i.Parent.Id.ToString(CultureInfo.InvariantCulture));
                }
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
                foreach (var mod in i.Data)
                {
                    foreach (var kv in mod.Value)
                    {
                        Line(sb, "data", mod.Key, kv.Key, kv.Value);
                    }
                }
                sb.Append("end\n");
            }
            foreach (DormantRecord d in dormant)
            {
                foreach (string l in d.Lines)
                {
                    sb.Append(l).Append('\n');
                }
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
            string dormantNote = dormant.Count > 0 ? $", {dormant.Count} dormant carried over" : "";
            PersistenceNote = $"Last save ({slot.slotName}): {kept.Count} kept, {items} cargo item(s){dormantNote}.";
            Plugin.Log.LogInfo($"Persistence: {kept.Count} donkey(s)/cart(s) with {items} cargo item(s) kept with {slot.slotName}{dormantNote} ({path}).");
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
            internal string Owner, Key, Scene, ParentOwner;
            internal int Id, ParentId;
            internal bool Donkey = true, Cart, FacingRight = true, Rideable = true, RemoveWithParent = true;
            internal Vector3 Position;
            internal float Scale = 1f;
            internal readonly List<string> Cargo = new List<string>();
            internal readonly List<string[]> Sets = new List<string[]>();
            internal readonly List<string[]> DataLines = new List<string[]>();
            internal readonly List<string> Lines = new List<string>();
            internal string FullId => FullIdOf(Owner, Id);
            internal string ParentFull => ParentOwner != null ? FullIdOf(ParentOwner, ParentId) : null;
            internal string Kind => !Donkey ? "free cart" : Cart ? "donkey with cart" : !Rideable && Scale < 1f ? "foal" : "donkey";
        }

        // Called once per loaded game (BecomeReady), before Mounts.GameReady: the kept ones back, the dormant ones held.
        // Returns the handles that came back.
        internal static List<MountHandle> RestoreFromSideFile()
        {
            var restored = new List<MountHandle>();
            SaveSlotData slot = MainGame.Instance != null ? MainGame.Instance.SaveSlotData : null;
            if (slot == null || string.IsNullOrEmpty(slot.slotName))
            {
                PersistenceNote = "A new game: nothing to restore.";
                return restored;
            }
            string path = SideFile(slot.slotName);
            if (!File.Exists(path))
            {
                PersistenceNote = $"Loaded {slot.slotName}: no side file, nothing to restore.";
                return restored;
            }

            var header = new Dictionary<string, string>();
            var nexts = new List<string[]>();
            var records = new List<Record>();
            Record cur = null;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (raw.Length == 0 || raw[0] == '#')
                {
                    continue;
                }
                string[] f = raw.Split('\t');
                for (int k = 0; k < f.Length; k++)
                {
                    f[k] = Unesc(f[k]);
                }
                if (f[0] == "instance")
                {
                    cur = new Record();
                    records.Add(cur);
                    cur.Lines.Add(raw);
                    continue;
                }
                if (cur == null)
                {
                    if (f[0] == "next" && f.Length >= 3)
                    {
                        nexts.Add(f);
                    }
                    else
                    {
                        header[f[0]] = f.Length > 1 ? f[1] : "";
                    }
                    continue;
                }
                cur.Lines.Add(raw);
                if (f[0] == "end")
                {
                    cur = null;
                    continue;
                }
                string a = f.Length > 1 ? f[1] : "";
                switch (f[0])
                {
                    case "owner": cur.Owner = a; break;
                    case "id": int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out cur.Id); break;
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
                    case "parent":
                        if (f.Length >= 3)
                        {
                            cur.ParentOwner = a;
                            int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out cur.ParentId);
                        }
                        break;
                    case "removeWithParent": cur.RemoveWithParent = a == "true"; break;
                    case "cargo": cur.Cargo.Add(a); break;
                    case "set":
                        if (f.Length >= 4)
                        {
                            cur.Sets.Add(new[] { f[1], f[2], f[3] });
                        }
                        break;
                    case "data":
                        if (f.Length >= 4)
                        {
                            cur.DataLines.Add(new[] { f[1], f[2], f[3] });
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
                return restored;
            }
            if (saved != (slot.saveDateTime ?? "") || day != slot.day.ToString(CultureInfo.InvariantCulture))
            {
                // The slot was saved again without the side file being written (the mod was off), or the save came
                // back from a backup or the cloud: what the file holds is from another point in time.
                Plugin.Log.LogWarning($"Persistence: {path} belongs to the save of {saved} (day {day}), but {slot.slotName} was saved {slot.saveDateTime} (day {slot.day}); ignored, nothing restored. Any cargo lies on the ground where the carts stood.");
                PersistenceNote = $"Loaded {slot.slotName}: the side file is from another save time, ignored.";
                return restored;
            }

            // The owners' counters, then every number in the file (a counter can never fall behind a number in use).
            foreach (string[] n in nexts)
            {
                if (int.TryParse(n[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0)
                {
                    nextIds[n[1]] = v;
                }
            }

            // Sort the records out: bad ones dropped, owners not installed (and their foals' foals) dormant, children
            // whose parent is missing orphaned or dropped.
            var byFull = new Dictionary<string, Record>(StringComparer.Ordinal);
            var valid = new List<Record>();
            foreach (Record r in records)
            {
                if (string.IsNullOrEmpty(r.Owner) || r.Id <= 0)
                {
                    Plugin.Log.LogWarning($"Persistence: a record without an owner or number ({r.Owner}#{r.Id}) was skipped.");
                    continue;
                }
                if (byFull.ContainsKey(r.FullId))
                {
                    Plugin.Log.LogWarning($"Persistence: {r.FullId} is in the file twice; the second one was skipped.");
                    continue;
                }
                byFull[r.FullId] = r;
                valid.Add(r);
                NoteIdUsed(r.Owner, r.Id);
            }
            var isDormant = new HashSet<Record>();
            var dropped = new HashSet<Record>();
            foreach (Record r in valid)
            {
                if (!OwnerPresent(r.Owner))
                {
                    isDormant.Add(r);
                }
            }
            foreach (Record r in valid)
            {
                if (r.ParentFull == null)
                {
                    continue;
                }
                if (!byFull.TryGetValue(r.ParentFull, out Record p))
                {
                    if (r.RemoveWithParent)
                    {
                        dropped.Add(r);
                        Plugin.Log.LogInfo($"Persistence: {r.FullId} {r.Kind}: its parent {r.ParentFull} isn't kept; it went with it.");
                    }
                    else
                    {
                        r.ParentOwner = null; // an orphan, as it would have become
                    }
                }
                else if (isDormant.Contains(p))
                {
                    isDormant.Add(r); // back when its parent is
                }
            }
            foreach (Record r in valid)
            {
                if (!isDormant.Contains(r))
                {
                    continue;
                }
                dormant.Add(new DormantRecord
                {
                    Owner = r.Owner,
                    Id = r.Id,
                    Key = r.Key,
                    Scene = r.Scene,
                    Kind = r.Kind,
                    Parent = r.ParentFull,
                    Position = r.Position,
                    Cargo = r.Cargo.Count,
                    Why = OwnerPresent(r.Owner) ? $"its parent {r.ParentFull} is dormant" : $"{r.Owner} isn't installed",
                    Lines = r.Lines,
                });
            }
            foreach (DormantRecord d in dormant)
            {
                Plugin.Log.LogInfo($"Persistence: {d.FullId} {d.Kind} is dormant ({d.Why}); kept in the file, back when its mod is.");
            }

            // Parents before children (no chains: two passes).
            var made = new Dictionary<string, Instance>(StringComparer.Ordinal);
            int items = 0, missing = 0, failed = 0, total = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (Record r in valid)
                {
                    if (isDormant.Contains(r) || dropped.Contains(r) || (r.ParentFull == null) != (pass == 0))
                    {
                        continue;
                    }
                    total++;
                    Instance parent = null;
                    if (r.ParentFull != null && !made.TryGetValue(r.ParentFull, out parent))
                    {
                        if (r.RemoveWithParent)
                        {
                            Plugin.Log.LogWarning($"Persistence: {r.FullId} {r.Kind}: its parent {r.ParentFull} didn't come back; neither did it.");
                            failed++;
                            continue;
                        }
                        parent = null;
                    }
                    Instance i = Spawn(new SpawnArgs
                    {
                        Owner = r.Owner,
                        Id = r.Id,
                        Key = string.IsNullOrEmpty(r.Key) ? null : r.Key,
                        Position = r.Position,
                        HasDonkey = r.Donkey,
                        HasCart = r.Cart,
                        FacingRight = r.FacingRight,
                        Scale = r.Scale,
                        Rideable = r.Rideable,
                        Parent = parent,
                        RemoveWithParent = r.RemoveWithParent,
                        SceneId = r.Scene, // its own scene, also a foal left behind by its parent: it catches up from there
                        Snap = false,
                        Persist = true,
                        Register = false,
                        Restoring = true,
                    }, out string why);
                    if (i == null)
                    {
                        Plugin.Log.LogWarning($"Persistence: {r.FullId} {r.Kind} ({r.Scene}) could not be spawned again: {why}");
                        failed++;
                        continue;
                    }
                    made[r.FullId] = i;
                    foreach (string[] s in r.Sets)
                    {
                        RestoreSetting(i, s[0], s[1], s[2]);
                    }
                    foreach (string[] d in r.DataLines)
                    {
                        if (!i.Data.TryGetValue(d[0], out Dictionary<string, string> m))
                        {
                            i.Data[d[0]] = m = new Dictionary<string, string>(StringComparer.Ordinal);
                        }
                        m[d[1]] = d[2];
                    }
                    i.ColorsRig.MarkDirty(); // its own colours, built with its view
                    var carried = new List<Item>();
                    foreach (string guid in r.Cargo)
                    {
                        Item it = TakeDropBack(i.SceneId, guid, r.Position);
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
            }
            string note = $"{restored.Count} of {total} donkey(s)/cart(s) back, {items} cargo item(s) back on their carts"
                + (missing > 0 ? $", {missing} not found on the ground" : "")
                + (dormant.Count > 0 ? $", {dormant.Count} dormant" : "")
                + (dropped.Count + failed > 0 ? $", {dropped.Count + failed} not back (see above)" : "");
            PersistenceNote = $"Loaded {slot.slotName}: {note}.";
            Plugin.Log.LogInfo($"Persistence: {slot.slotName}: {note}.");
            return restored;
        }

        private static void RestoreSetting(Instance i, string key, string by, string value)
        {
            try
            {
                ITuned t = new InstanceTuning(i).Find(key);
                object v = t.Clamp(TomlTypeConverter.ConvertToValue(value, t.ValueType));
                i.Overrides[t.Key] = v;
                i.OverrideOwners[t.Key] = string.IsNullOrEmpty(by) ? Plugin.Guid : by;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Persistence: {i.Describe()}'s setting {key} = {value} wasn't restored: {e.Message}");
            }
        }

        // The item with this GUID off the ground of its scene (or the scene's queue) within ReclaimRadius of where its
        // cart stood, or null when it isn't there (picked up, moved, gone).
        private static Item TakeDropBack(string sceneId, string guid, Vector3 near)
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
            Vector3 at = d.Position;
            if (new Vector2(at.x - near.x, at.z - near.z).magnitude > ReclaimRadius)
            {
                Plugin.Log.LogInfo($"Persistence: {d.Item?.id} lies {Vector3.Distance(at, near):F1} from where its cart stood; it stays where it is.");
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
                sb.Append('\t').Append(Esc(v));
            }
            sb.Append('\n');
        }

        // Tabs separate fields and a line is a field list: tabs, line breaks and backslashes inside a value are escaped.
        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string Unesc(string s)
        {
            if (s.IndexOf('\\') < 0)
            {
                return s;
            }
            var sb = new StringBuilder(s.Length);
            for (int k = 0; k < s.Length; k++)
            {
                char c = s[k];
                if (c == '\\' && k + 1 < s.Length)
                {
                    char n = s[++k];
                    sb.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n == 'r' ? '\r' : n);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string Bool(bool b) => b ? "true" : "false";

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static float P(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }
}

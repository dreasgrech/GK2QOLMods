using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;

namespace GK2Mounts
{
    // Corrections on top of the size-based cargo packer, kept in a small text file (cargo-layout.cfg):
    //
    //   [item <id>]   applies to every item whose measured box matches <id>'s box within the size tolerance, i.e. to
    //                 the whole size class (stone's entry also covers iron ore; box_appliances_1's covers 15 crates).
    //                 along/up/across: offsets when the item is alone, at the bottom or beside another item.
    //                 top_along/top_up/top_across: offsets when it is stacked on top of another item.
    //   [pair <a>+<b>] applies to that pair of size classes in either order: mode = auto|side|along|stack, and
    //                 along/up/across shift the second-placed item relative to where the packer put it.
    //
    // The file ships embedded in the DLL (the repo copy). On the machine with the sources (Defaults.RepoFile) the mod
    // reads the repo file itself and the panel's corrections are written back to it as you edit, so there is one copy
    // and a rebuild ships it; elsewhere the embedded copy is used and edits last for the session. Entries are keyed by item
    // id and resolved through the items' measured boxes at runtime, so a game update that adds an item of a known
    // size is covered automatically, and an item whose mesh changes size simply stops matching (the packer's
    // defaults then apply, and the sheet shows it).
    internal static class CargoLayout
    {
        public const float SizeTolerance = 0.05f;
        public const string RepoFileName = "cargo-layout.cfg";

        // True on the machine with the sources: edits are saved to the repo file.
        public static bool SavesToRepo => Defaults.RepoFile(RepoFileName) != null;

        private static bool dirty;
        private static float lastChange;

        // A correction changed on the panel; written to the repo file shortly after the last change (SaveIfDue).
        public static void MarkChanged()
        {
            dirty = true;
            lastChange = UnityEngine.Time.unscaledTime;
        }

        public static string LastSaveNote { get; private set; } = "";

        public static void SaveIfDue(bool now = false)
        {
            if (!dirty || (!now && UnityEngine.Time.unscaledTime - lastChange < 0.75f))
            {
                return;
            }
            dirty = false;
            string repo = Defaults.RepoFile(RepoFileName);
            if (repo == null)
            {
                return;
            }
            try
            {
                Save(repo);
                LastSaveNote = "Saved to " + repo + " at " + DateTime.Now.ToString("HH:mm:ss") + ".";
            }
            catch (Exception e)
            {
                LastSaveNote = "Saving the layout failed: " + e.Message;
                Plugin.Log.LogWarning("Cargo layout: " + LastSaveNote);
            }
        }

        public class ItemEntry
        {
            public string Id;
            public Vector3 Base;   // along, up, across
            public Vector3 Top;    // when stacked on top
            public bool IsEmpty => Base == Vector3.zero && Top == Vector3.zero;
        }

        public class PairEntry
        {
            public string A, B;
            public string Mode = "auto"; // auto | side (across) | along (end to end) | stack
            public Vector3 Second;       // offset of the second-placed item
            public bool IsEmpty => Mode == "auto" && Second == Vector3.zero;
        }

        public static readonly Dictionary<string, ItemEntry> Items = new Dictionary<string, ItemEntry>(StringComparer.Ordinal);
        public static readonly List<PairEntry> Pairs = new List<PairEntry>();
        public static string Source { get; private set; } = "(not loaded)";

        // Boxes by item id, measured from the keeper's overhead mesh set; filled lazily, once per session.
        private static readonly Dictionary<string, Vector3> boxes = new Dictionary<string, Vector3>(StringComparer.Ordinal);

        public static void Load()
        {
            Items.Clear();
            Pairs.Clear();
            dirty = false;
            string repo = Defaults.RepoFile(RepoFileName);
            try
            {
                if (repo != null)
                {
                    Parse(File.ReadAllText(repo));
                    Source = repo;
                    return;
                }
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("GK2Mounts.cargo-layout.cfg"))
                {
                    if (s != null)
                    {
                        using (var r = new StreamReader(s))
                        {
                            Parse(r.ReadToEnd());
                        }
                        Source = "embedded";
                        return;
                    }
                }
                Source = "(no file, packer defaults)";
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo layout: reading the layout file failed, packer defaults in use: " + e.Message);
                Source = "(read failed)";
            }
        }

        private static void Parse(string text)
        {
            ItemEntry item = null;
            PairEntry pair = null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                int hash = line.IndexOf('#');
                if (hash >= 0)
                {
                    line = line.Substring(0, hash).Trim();
                }
                if (line.Length == 0)
                {
                    continue;
                }
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string head = line.Substring(1, line.Length - 2).Trim();
                    item = null;
                    pair = null;
                    if (head.StartsWith("item ", StringComparison.OrdinalIgnoreCase))
                    {
                        item = new ItemEntry { Id = head.Substring(5).Trim() };
                        Items[item.Id] = item;
                    }
                    else if (head.StartsWith("pair ", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] ab = head.Substring(5).Split('+');
                        if (ab.Length == 2)
                        {
                            pair = new PairEntry { A = ab[0].Trim(), B = ab[1].Trim() };
                            Pairs.Add(pair);
                        }
                    }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f);
                if (item != null)
                {
                    switch (key)
                    {
                        case "along": item.Base.x = f; break;
                        case "up": item.Base.y = f; break;
                        case "across": item.Base.z = f; break;
                        case "top_along": item.Top.x = f; break;
                        case "top_up": item.Top.y = f; break;
                        case "top_across": item.Top.z = f; break;
                    }
                }
                else if (pair != null)
                {
                    switch (key)
                    {
                        case "mode": pair.Mode = val.ToLowerInvariant(); break;
                        case "along": pair.Second.x = f; break;
                        case "up": pair.Second.y = f; break;
                        case "across": pair.Second.z = f; break;
                    }
                }
            }
        }

        // Writes the current entries (empty ones dropped) to path.
        private static void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# GK2 Mounts cargo layout: corrections on top of the size-based cargo packer (see CargoLayout.cs).");
            sb.AppendLine("# Embedded into the DLL at build time. The tuning panel writes this file as you edit; rebuild to ship it.");
            sb.AppendLine("# [item <id>] applies to every item of the same size class (measured box within " + SizeTolerance.ToString("0.00", CultureInfo.InvariantCulture) + ").");
            sb.AppendLine("#   along / up / across: alone, at the bottom or beside another item; top_along / top_up / top_across: stacked on top.");
            sb.AppendLine("# [pair <a>+<b>] applies to that pair of size classes in either order: mode = auto | side (across the bed) | along (end to end) | stack; along / up / across move the second item.");
            sb.AppendLine("#");
            sb.AppendLine("# Size classes at the time of writing (27 items): log (wood); flour sack (box_flour); tall crate (box_beer,");
            sb.AppendLine("# box_clothes_1/2, box_fabric, box_vine); body (body_corpse, body_wild_zombie); low crate (15 box_* incl. preserves);");
            sb.AppendLine("# slab (stone, iron_ore_2h); no mesh (marble -> shown with the slab mesh, box_engineering_mechanism -> generic crate).");
            sb.AppendLine("version = 1");
            var ids = new List<string>(Items.Keys);
            ids.Sort(string.CompareOrdinal);
            foreach (string id in ids)
            {
                ItemEntry e = Items[id];
                if (e.IsEmpty)
                {
                    continue;
                }
                sb.AppendLine().AppendLine("[item " + id + "]");
                Line(sb, "along", e.Base.x); Line(sb, "up", e.Base.y); Line(sb, "across", e.Base.z);
                Line(sb, "top_along", e.Top.x); Line(sb, "top_up", e.Top.y); Line(sb, "top_across", e.Top.z);
            }
            foreach (PairEntry p in Pairs)
            {
                if (p.IsEmpty)
                {
                    continue;
                }
                sb.AppendLine().AppendLine("[pair " + p.A + "+" + p.B + "]");
                sb.AppendLine("mode = " + p.Mode);
                Line(sb, "along", p.Second.x); Line(sb, "up", p.Second.y); Line(sb, "across", p.Second.z);
            }
            File.WriteAllText(path, sb.ToString());
            Source = path;
        }

        private static void Line(StringBuilder sb, string key, float v)
        {
            if (Mathf.Abs(v) > 0.0001f)
            {
                sb.AppendLine(key + " = " + v.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        // ------------------------------------------------------------------ size classes

        public static Vector3 BoxOf(string itemId)
        {
            if (boxes.TryGetValue(itemId, out Vector3 b))
            {
                return b;
            }
            b = Vector3.zero;
            try
            {
                ItemDef def = GameBalance.Me.GetDataOrNull<ItemDef>(itemId);
                Transform el = def != null ? Cargo.FindOverheadElement(def) : null;
                if (el != null && Cargo.MeasureMeshes(el, el, out Bounds bb, includeInactive: true))
                {
                    b = bb.size;
                }
            }
            catch
            {
            }
            boxes[itemId] = b;
            return b;
        }

        public static bool SameClass(string a, string b)
        {
            if (a == b)
            {
                return true;
            }
            Vector3 d = BoxOf(a) - BoxOf(b);
            return Mathf.Abs(d.x) <= SizeTolerance && Mathf.Abs(d.y) <= SizeTolerance && Mathf.Abs(d.z) <= SizeTolerance;
        }

        // The entry covering this item's size class, or null. The exact id wins over a class match.
        public static ItemEntry ForItem(string itemId)
        {
            if (Items.TryGetValue(itemId, out ItemEntry exact))
            {
                return exact;
            }
            foreach (ItemEntry e in Items.Values)
            {
                if (SameClass(e.Id, itemId))
                {
                    return e;
                }
            }
            return null;
        }

        public static PairEntry ForPair(string a, string b)
        {
            foreach (PairEntry p in Pairs)
            {
                if ((SameClass(p.A, a) && SameClass(p.B, b)) || (SameClass(p.A, b) && SameClass(p.B, a)))
                {
                    return p;
                }
            }
            return null;
        }

        // Entry to edit for this item: the existing class entry, or a new one keyed by this item's id.
        public static ItemEntry EditItem(string itemId)
        {
            ItemEntry e = ForItem(itemId);
            if (e == null)
            {
                e = new ItemEntry { Id = itemId };
                Items[itemId] = e;
            }
            return e;
        }

        public static PairEntry EditPair(string a, string b)
        {
            PairEntry p = ForPair(a, b);
            if (p == null)
            {
                p = new PairEntry { A = a, B = b };
                Pairs.Add(p);
            }
            return p;
        }

        public static string Summary()
        {
            int items = 0, pairs = 0;
            foreach (ItemEntry e in Items.Values)
            {
                if (!e.IsEmpty)
                {
                    items++;
                }
            }
            foreach (PairEntry p in Pairs)
            {
                if (!p.IsEmpty)
                {
                    pairs++;
                }
            }
            return $"{items} item entr{(items == 1 ? "y" : "ies")}, {pairs} pair entr{(pairs == 1 ? "y" : "ies")}, from {Source}";
        }
    }
}

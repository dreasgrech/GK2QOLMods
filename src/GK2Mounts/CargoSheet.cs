using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace GK2Mounts
{
    // Development tool: photographs every cargo combination on the mounted cart and writes an HTML contact sheet, so
    // the placement of all item pairs can be judged in one view instead of one load at a time. Runs as a coroutine
    // while mounted in Cart style: for each combination the cart's inventory is cleared, the items are put in (the
    // same call the delivery script uses), two frames pass for the game's drawer and the mod's views, the screen is
    // captured at the end of the frame and cropped around the cart. The cargo that was aboard before is put back.
    // Items with the same mesh (fourteen crates share one model) are represented once. Nothing here touches the
    // save: the donkey's inventory is a temp object's, the test items are created and discarded in memory.
    internal static class CargoSheet
    {
        public static bool Running { get; private set; }
        public static string Status { get; private set; } = "";

        private static readonly Vector3 CropHalfExtents = new Vector3(1.25f, 1.1f, 0.9f); // world box around the cart bed
        private static bool cropLogged;

        public static string Start()
        {
            if (Running)
            {
                return "already running";
            }
            if (Mount.Donkey == null || Mount.CurrentStyle != MountStyle.Cart || !Cargo.Attached)
            {
                return "mount in Cart style first";
            }
            if (Plugin.Instance == null)
            {
                return "plugin instance missing";
            }
            Plugin.Instance.StartCoroutine(Run());
            return "started; stand still until the panel comes back";
        }

        // One representative item per distinct mesh, by the names of the meshes the overhead element is made of.
        private static List<string> Representatives()
        {
            var result = new List<string>();
            var seenShapes = new HashSet<string>();
            var elementsByIcon = new Dictionary<string, Transform>();
            PlayerController pc = MainGame.PlayerController;
            if (pc != null)
            {
                foreach (DropViewAtomMesh atom in pc.GetComponentsInChildren<DropViewAtomMesh>(includeInactive: true))
                {
                    if (atom.transform.parent == null || atom.transform.parent.name != "overhead")
                    {
                        continue;
                    }
                    foreach (Transform t in atom.GetComponentsInChildren<Transform>(includeInactive: true))
                    {
                        if (t.GetComponent<DropViewAtomMeshElement>() != null && !elementsByIcon.ContainsKey(t.name))
                        {
                            elementsByIcon[t.name] = t;
                        }
                    }
                    break;
                }
            }
            foreach (string id in CarryProbe.BigItemIds())
            {
                ItemDef def = GameBalance.Me.GetDataOrNull<ItemDef>(id);
                if (def == null)
                {
                    continue;
                }
                string shape = "placeholder";
                if (elementsByIcon.TryGetValue(def.iconId, out Transform el))
                {
                    var names = new List<string>();
                    foreach (MeshFilter mf in el.GetComponentsInChildren<MeshFilter>(includeInactive: true))
                    {
                        if (!Cargo.IsShadowMesh(mf.gameObject.name) && !mf.gameObject.name.EndsWith("-blackout"))
                        {
                            names.Add(mf.gameObject.name);
                        }
                    }
                    names.Sort(string.CompareOrdinal);
                    shape = string.Join("|", names);
                }
                if (seenShapes.Add(shape))
                {
                    result.Add(id);
                }
            }
            return result;
        }

        private static IEnumerator Run()
        {
            Running = true;
            string dir = Path.Combine(Paths.BepInExRootPath, "GK2-cargo-sheet");
            var log = new StringBuilder();
            List<Item> saved = null;
            Inventory inv = null;
            try
            {
                Directory.CreateDirectory(dir);
                inv = Mount.Donkey.Data.Inventory;
                saved = new List<Item>(inv.Data.Inventory);
                List<string> reps = Representatives();
                Status = $"0 / {reps.Count * (reps.Count + 1)}";
                Plugin.Log.LogInfo($"[sheet] {reps.Count} distinct shapes: {string.Join(" ", reps)}");

                var cells = new Dictionary<string, string>(); // "a|b" -> file name
                int done = 0, total = reps.Count * (reps.Count + 1);
                var combos = new List<KeyValuePair<string, string>>();
                foreach (string a in reps)
                {
                    combos.Add(new KeyValuePair<string, string>(a, null));
                }
                foreach (string a in reps)
                {
                    foreach (string b in reps)
                    {
                        combos.Add(new KeyValuePair<string, string>(a, b));
                    }
                }
                foreach (KeyValuePair<string, string> combo in combos)
                {
                    inv.Clear();
                    inv.AddItemToInventory(new Item(combo.Key));
                    if (combo.Value != null)
                    {
                        inv.AddItemToInventory(new Item(combo.Value));
                    }
                    yield return null;
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    string file = combo.Key + "__" + (combo.Value ?? "none") + ".png";
                    string err = Capture(Path.Combine(dir, file));
                    if (err != null)
                    {
                        log.AppendLine(file + ": " + err);
                    }
                    cells[combo.Key + "|" + (combo.Value ?? "")] = file;
                    done++;
                    Status = $"{done} / {total}";
                }
                WriteHtml(Path.Combine(dir, "index.html"), reps, cells, log.ToString());
                Plugin.Log.LogInfo($"[sheet] {done} pictures written to {dir}\\index.html" + (log.Length > 0 ? " with problems:\n" + log : ""));
                Status = "done: " + Path.Combine(dir, "index.html");
            }
            finally
            {
                if (inv != null)
                {
                    inv.Clear();
                    if (saved != null)
                    {
                        foreach (Item it in saved)
                        {
                            if (it != null && !it.IsEmpty)
                            {
                                inv.AddItemToInventory(it);
                            }
                        }
                    }
                }
                Running = false;
            }
        }

        // Screen capture cropped to the cart: the world box around the cargo holder projected to the screen.
        private static string Capture(string path)
        {
            Texture2D full = null;
            Texture2D crop = null;
            try
            {
                // The game's world camera (CameraSystem.WorldCamera); Camera.main is the deform-texture camera, which
                // has its own 1280x720 target and put the crops on the wrong spot. Its screen coordinates are in the
                // camera's pixel rect, which may be smaller than the screenshot (the game renders at a pixel size),
                // so they are scaled to the screenshot's size.
                Camera cam = CameraSystem.Instance != null ? CameraSystem.Instance.WorldCamera : Camera.main;
                Transform anchor = Mount.Donkey != null ? Mount.Donkey.transform : null;
                if (cam == null || anchor == null)
                {
                    return "no camera or donkey";
                }
                Vector3 centre = anchor.position + Vector3.up * 0.5f;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int c = 0; c < 8; c++)
                {
                    var corner = centre + new Vector3((c & 1) == 0 ? -CropHalfExtents.x : CropHalfExtents.x, (c & 2) == 0 ? -CropHalfExtents.y : CropHalfExtents.y, (c & 4) == 0 ? -CropHalfExtents.z : CropHalfExtents.z);
                    Vector3 sp = cam.WorldToScreenPoint(corner);
                    minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
                    minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
                }
                full = ScreenCapture.CaptureScreenshotAsTexture();
                float sx = full.width / Mathf.Max(1f, cam.pixelWidth), sy = full.height / Mathf.Max(1f, cam.pixelHeight);
                minX *= sx; maxX *= sx; minY *= sy; maxY *= sy;
                if (!cropLogged)
                {
                    cropLogged = true;
                    Plugin.Log.LogInfo($"[sheet] camera '{cam.name}' pixel rect {cam.pixelWidth}x{cam.pixelHeight}, screenshot {full.width}x{full.height}, crop x {minX:0}..{maxX:0} y {minY:0}..{maxY:0}");
                }
                int x0 = Mathf.Clamp(Mathf.FloorToInt(minX), 0, full.width - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(minY), 0, full.height - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(maxX), x0 + 1, full.width);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(maxY), y0 + 1, full.height);
                int w = x1 - x0, h = y1 - y0;
                crop = new Texture2D(w, h, TextureFormat.RGB24, false);
                crop.SetPixels(full.GetPixels(x0, y0, w, h));
                crop.Apply();
                File.WriteAllBytes(path, crop.EncodeToPNG());
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
            finally
            {
                if (full != null)
                {
                    UnityEngine.Object.Destroy(full);
                }
                if (crop != null)
                {
                    UnityEngine.Object.Destroy(crop);
                }
            }
        }

        private static void WriteHtml(string path, List<string> reps, Dictionary<string, string> cells, string problems)
        {
            StyleSettings s = Mount.S;
            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>GK2 Mounts cargo sheet</title>");
            sb.AppendLine("<style>body{font-family:sans-serif;background:#222;color:#ddd;margin:16px}table{border-collapse:collapse}td,th{border:1px solid #444;padding:4px;text-align:center;vertical-align:top;font-size:12px}th{background:#333;position:sticky;top:0}th.row{position:sticky;left:0;background:#333}img{display:block;max-width:260px;height:auto}.cap{color:#999;font-size:11px}pre{background:#111;padding:8px}</style></head><body>");
            sb.AppendLine($"<h1>GK2 Mounts cargo sheet</h1><p>{DateTime.Now:yyyy-MM-dd HH:mm}. Rows: first item (slot 1). Columns: second item (slot 2); first column: the item alone. {reps.Count} distinct shapes, {cells.Count} pictures.</p>");
            sb.AppendLine($"<p>Settings [{s.Section}]: CargoAutoFit={s.CargoAutoFit.Value} CargoDrawCorpse={s.CargoDrawCorpse.Value} slot1=({s.Cargo1Along.Value}, {s.Cargo1Up.Value}, {s.Cargo1Across.Value}) slot2=({s.Cargo2Along.Value}, {s.Cargo2Up.Value}, {s.Cargo2Across.Value})</p>");
            sb.Append("<table><tr><th></th><th>alone</th>");
            foreach (string b in reps)
            {
                sb.Append("<th>").Append(b).Append("</th>");
            }
            sb.AppendLine("</tr>");
            foreach (string a in reps)
            {
                sb.Append("<tr><th class=\"row\">").Append(a).Append("</th>");
                sb.Append(Cell(cells, a, "", a));
                foreach (string b in reps)
                {
                    sb.Append(Cell(cells, a, b, a + " + " + b));
                }
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</table>");
            if (!string.IsNullOrEmpty(problems))
            {
                sb.AppendLine("<h2>Problems</h2><pre>").Append(problems).AppendLine("</pre>");
            }
            sb.AppendLine("</body></html>");
            File.WriteAllText(path, sb.ToString());
        }

        private static string Cell(Dictionary<string, string> cells, string a, string b, string caption)
        {
            return cells.TryGetValue(a + "|" + b, out string file)
                ? $"<td><img src=\"{file}\" alt=\"{caption}\"><div class=\"cap\">{caption}</div></td>"
                : "<td>-</td>";
        }
    }
}

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
            return Begin(Run());
        }

        // Second mode: every spawnable item alone, grouped by the shape bucket the code assigns, to prove the buckets.
        public static string StartShapes()
        {
            return Begin(RunShapes());
        }

        private static string Begin(IEnumerator routine)
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
            Plugin.Instance.StartCoroutine(routine);
            return "started; stand still until the panel comes back";
        }

        // The shape key of an item: the names of the non-shadow, non-blackout meshes its overhead element is made of
        // ("placeholder" when the element is missing, e.g. marble), plus the element it was found under.
        private static Dictionary<string, Transform> OverheadElements()
        {
            var elementsByIcon = new Dictionary<string, Transform>();
            PlayerController pc = MainGame.PlayerController;
            if (pc == null)
            {
                return elementsByIcon;
            }
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
            return elementsByIcon;
        }

        private const float SizeTolerance = 0.05f; // boxes within this in every dimension count as one size

        // The item's measured box (long, high, across); zero when it has no element.
        private static Vector3 ItemBox(ItemDef def, Dictionary<string, Transform> elementsByIcon)
        {
            if (def != null && elementsByIcon.TryGetValue(def.iconId, out Transform el) && Cargo.MeasureMeshes(el, el, out Bounds b, includeInactive: true))
            {
                return b.size;
            }
            return Vector3.zero;
        }

        private static string ShapeKey(ItemDef def, Dictionary<string, Transform> elementsByIcon, out string sizeText)
        {
            sizeText = "no mesh";
            if (def == null || !elementsByIcon.TryGetValue(def.iconId, out Transform el))
            {
                return "placeholder";
            }
            var names = new List<string>();
            foreach (MeshFilter mf in el.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                if (!Cargo.IsShadowMesh(mf.gameObject.name) && !mf.gameObject.name.EndsWith("-blackout"))
                {
                    names.Add(mf.gameObject.name);
                }
            }
            names.Sort(string.CompareOrdinal);
            if (Cargo.MeasureMeshes(el, el, out Bounds b, includeInactive: true))
            {
                sizeText = $"{b.size.x:0.00} long, {b.size.y:0.00} high, {b.size.z:0.00} across; bottom {b.min.y:0.00}, footprint centre ({b.center.x:0.00}, {b.center.z:0.00})";
            }
            return names.Count == 0 ? "placeholder" : string.Join("|", names);
        }

        private static IEnumerator RunShapes()
        {
            Running = true;
            string dir = Path.Combine(Paths.BepInExRootPath, "GK2-cargo-sheet");
            List<Item> saved = null;
            Inventory inv = null;
            var log = new StringBuilder();
            try
            {
                Directory.CreateDirectory(dir);
                inv = Mount.Donkey.Data.Inventory;
                saved = new List<Item>(inv.Data.Inventory);
                Dictionary<string, Transform> elements = OverheadElements();
                List<string> ids = CarryProbe.BigItemIds();
                // Size buckets: items whose boxes agree within SizeTolerance in every dimension are one bucket (the
                // first item seen is the bucket's reference). Each bucket lists the meshes it contains, so "same size,
                // different model" (stone vs ore) is visible as one bucket with two mesh names.
                var bucketSizes = new List<Vector3>();
                var bucketRows = new List<List<string>>();
                var bucketMeshes = new List<HashSet<string>>();
                int done = 0;
                Status = $"0 / {ids.Count}";
                foreach (string id in ids)
                {
                    ItemDef def = GameBalance.Me.GetDataOrNull<ItemDef>(id);
                    inv.Clear();
                    inv.AddItemToInventory(new Item(id));
                    yield return null;
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    string file = "shape__" + id + ".png";
                    string err = Capture(Path.Combine(dir, file));
                    if (err != null)
                    {
                        log.AppendLine(file + ": " + err);
                    }
                    string meshKey = ShapeKey(def, elements, out string size);
                    Vector3 box = ItemBox(def, elements);
                    int bi = -1;
                    for (int k = 0; k < bucketSizes.Count; k++)
                    {
                        Vector3 d = bucketSizes[k] - box;
                        if (Mathf.Abs(d.x) <= SizeTolerance && Mathf.Abs(d.y) <= SizeTolerance && Mathf.Abs(d.z) <= SizeTolerance)
                        {
                            bi = k;
                            break;
                        }
                    }
                    if (bi < 0)
                    {
                        bucketSizes.Add(box);
                        bucketRows.Add(new List<string>());
                        bucketMeshes.Add(new HashSet<string>());
                        bi = bucketSizes.Count - 1;
                    }
                    bucketMeshes[bi].Add(meshKey);
                    bucketRows[bi].Add($"<td><img src=\"{file}\" alt=\"{id}\" title=\"{id}\" onclick=\"show('{file}','{id}')\"><div class=\"cap\"><b>{id}</b><br>icon {def?.iconId}<br>mesh {meshKey}<br>{size}</div></td>");
                    done++;
                    Status = $"{done} / {ids.Count}";
                }
                // Largest boxes first, for reading.
                var order = new List<int>();
                for (int k = 0; k < bucketSizes.Count; k++)
                {
                    order.Add(k);
                }
                order.Sort((p, q) => (bucketSizes[q].x * bucketSizes[q].y * bucketSizes[q].z).CompareTo(bucketSizes[p].x * bucketSizes[p].y * bucketSizes[p].z));
                var buckets = new List<KeyValuePair<string, List<string>>>();
                foreach (int k in order)
                {
                    Vector3 sz = bucketSizes[k];
                    var meshes = new List<string>(bucketMeshes[k]);
                    meshes.Sort(string.CompareOrdinal);
                    buckets.Add(new KeyValuePair<string, List<string>>($"{sz.x:0.00} long × {sz.y:0.00} high × {sz.z:0.00} across (±{SizeTolerance:0.00}); {meshes.Count} mesh(es): {string.Join(", ", meshes)}", bucketRows[k]));
                }
                var sb = new StringBuilder();
                sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>GK2 Mounts cargo shapes</title>");
                sb.AppendLine("<style>:root{--w:220px}body{font-family:sans-serif;background:#222;color:#ddd;margin:16px}table{border-collapse:collapse;margin-bottom:24px}td{border:1px solid #444;padding:4px;text-align:center;vertical-align:top;font-size:12px}img{display:block;width:var(--w);height:auto;cursor:zoom-in;margin:auto}.cap{color:#bbb;font-size:11px;max-width:var(--w);white-space:normal}h2{margin:18px 0 6px;font-size:15px}h2 span{color:#888;font-weight:normal;font-size:12px}#bar{position:sticky;top:0;background:#222;padding:6px 0;z-index:2;display:flex;gap:8px;align-items:center}#bar button{background:#444;color:#ddd;border:1px solid #666;padding:3px 10px;cursor:pointer}#bar input{width:220px}#big{position:fixed;inset:0;background:rgba(0,0,0,.85);display:none;align-items:center;justify-content:center;flex-direction:column;cursor:zoom-out;z-index:9}#big img{width:auto;max-width:95vw;max-height:88vh;image-rendering:pixelated}#big div{color:#ddd;margin-top:8px}</style></head><body>");
                sb.AppendLine($"<h1>GK2 Mounts cargo shapes</h1><p>{DateTime.Now:yyyy-MM-dd HH:mm}. Every carryable item alone on the cart, grouped into size buckets: items whose measured boxes agree within {SizeTolerance:0.00} in length, height and width are one bucket, whatever their model. A bucket may hold several meshes (stone and ore are the same box). Items in one bucket are placed identically by the layout, so tuning one tunes all; if two pictures in one bucket sit differently on the cart, the bucket rule needs a tighter tolerance or a per-mesh correction. {ids.Count} items, {buckets.Count} buckets, largest first. Click a picture for full size.</p>");
                sb.AppendLine("<div id=\"bar\"><button onclick=\"zoom(-30)\">&minus;</button><input type=\"range\" id=\"z\" min=\"60\" max=\"600\" value=\"220\" oninput=\"setW(+this.value)\"><button onclick=\"zoom(30)\">+</button><button onclick=\"setW(220)\">Reset</button><span id=\"zl\">220 px</span></div>");
                sb.AppendLine("<script>function setW(w){w=Math.max(40,Math.min(1200,Math.round(w)));document.documentElement.style.setProperty('--w',w+'px');document.getElementById('z').value=w;document.getElementById('zl').textContent=w+' px';}function zoom(d){setW(+document.getElementById('z').value+d);}function show(src,cap){var b=document.getElementById('big');b.querySelector('img').src=src;b.querySelector('div').textContent=cap;b.style.display='flex';}</script>");
                sb.AppendLine("<div id=\"big\" onclick=\"this.style.display='none'\"><img alt=\"\"><div></div></div>");
                int n = 0;
                foreach (KeyValuePair<string, List<string>> kv in buckets)
                {
                    n++;
                    sb.AppendLine($"<h2>Size bucket {n}: {kv.Value.Count} item(s) <span>{kv.Key}</span></h2><table><tr>");
                    foreach (string cell in kv.Value)
                    {
                        sb.Append(cell);
                    }
                    sb.AppendLine("</tr></table>");
                }
                if (log.Length > 0)
                {
                    sb.AppendLine("<h2>Problems</h2><pre>").Append(log).AppendLine("</pre>");
                }
                sb.AppendLine("</body></html>");
                File.WriteAllText(Path.Combine(dir, "shapes.html"), sb.ToString());
                Plugin.Log.LogInfo($"[sheet] {done} items in {buckets.Count} shape buckets written to {dir}\\shapes.html");
                Status = "done: " + Path.Combine(dir, "shapes.html");
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
            sb.AppendLine("<style>:root{--w:260px}body{font-family:sans-serif;background:#222;color:#ddd;margin:16px}table{border-collapse:collapse}td,th{border:1px solid #444;padding:2px;text-align:center;vertical-align:top;font-size:12px}th{background:#333;position:sticky;top:34px;z-index:1}th.row{position:sticky;left:0;background:#333}img{display:block;width:var(--w);height:auto;cursor:zoom-in}.cap{color:#999;font-size:11px;max-width:var(--w);overflow:hidden;text-overflow:ellipsis;white-space:nowrap}pre{background:#111;padding:8px}#bar{position:sticky;top:0;background:#222;padding:6px 0;z-index:2;display:flex;gap:8px;align-items:center}#bar button{background:#444;color:#ddd;border:1px solid #666;padding:3px 10px;cursor:pointer}#bar input{width:220px}.small .cap{display:none}#big{position:fixed;inset:0;background:rgba(0,0,0,.85);display:none;align-items:center;justify-content:center;flex-direction:column;cursor:zoom-out;z-index:9}#big img{width:auto;max-width:95vw;max-height:88vh;image-rendering:pixelated}#big div{color:#ddd;margin-top:8px}</style></head><body>");
            sb.AppendLine($"<h1>GK2 Mounts cargo sheet</h1><p>{DateTime.Now:yyyy-MM-dd HH:mm}. Rows: first item (slot 1). Columns: second item (slot 2); first column: the item alone. {reps.Count} distinct shapes, {cells.Count} pictures. Click a picture to see it full size.</p>");
            sb.AppendLine("<div id=\"bar\"><button onclick=\"zoom(-30)\">&minus;</button><input type=\"range\" id=\"z\" min=\"40\" max=\"600\" value=\"260\" oninput=\"setW(+this.value)\"><button onclick=\"zoom(30)\">+</button><button onclick=\"fitAll()\">Fit everything on one page</button><button onclick=\"fitWidth()\">Fit width</button><button onclick=\"setW(260)\">Reset</button><span id=\"zl\">260 px</span></div>");
            sb.AppendLine($"<script>var COLS={reps.Count + 1},ROWS={reps.Count};function setW(w){{w=Math.max(30,Math.min(1200,Math.round(w)));document.documentElement.style.setProperty('--w',w+'px');document.getElementById('z').value=w;document.getElementById('zl').textContent=w+' px';document.body.classList.toggle('small',w<110);}}function zoom(d){{setW(+document.getElementById('z').value+d);}}function fitWidth(){{var lab=document.querySelector('th.row').offsetWidth;setW((window.innerWidth-lab-40-COLS*6)/COLS);}}function fitAll(){{var lab=document.querySelector('th.row').offsetWidth;var img=document.querySelector('td img');var ratio=img?img.naturalHeight/img.naturalWidth:1.1;var top=document.querySelector('table').getBoundingClientRect().top+window.scrollY+30;var byW=(window.innerWidth-lab-40-COLS*6)/COLS;var byH=(window.innerHeight-top-ROWS*6)/ROWS/ratio;setW(Math.min(byW,byH));}}function show(src,cap){{var b=document.getElementById('big');b.querySelector('img').src=src;b.querySelector('div').textContent=cap;b.style.display='flex';}}</script>");
            sb.AppendLine("<div id=\"big\" onclick=\"this.style.display='none'\"><img alt=\"\"><div></div></div>");
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
                ? $"<td><img src=\"{file}\" alt=\"{caption}\" title=\"{caption}\" onclick=\"show('{file}','{caption}')\"><div class=\"cap\">{caption}</div></td>"
                : "<td>-</td>";
        }
    }
}

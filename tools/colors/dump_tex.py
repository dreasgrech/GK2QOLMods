import UnityPy, os, json
UnityPy.config.FALLBACK_UNITY_VERSION = "6000.3.9f1"
root = r"C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2\GraveyardKeeper2_Data"
out = os.path.dirname(os.path.abspath(__file__))
scan = json.load(open(os.path.join(out, "tint_scan.json")))
want_lut = {"bdy_lut_01", "hed_lut_01", "hed_lut_03", "lut_05'00"}
bundles = {os.path.join(root, l[3]) for l in scan["luts"] if l[0] in want_lut}
# the cart's own textures: look through every bundle for Texture2D names with donkey / cart
paths = [os.path.join(dp, f) for dp, dn, fn in os.walk(root) for f in fn if f.endswith(".bundle")]
found = []
for p in paths:
    try:
        env = UnityPy.load(p)
    except Exception:
        continue
    for obj in env.objects:
        if obj.type.name != "Texture2D":
            continue
        try:
            d = obj.read()
        except Exception:
            continue
        n = d.m_Name
        if n in want_lut or "donkey" in n.lower() or "cart" in n.lower():
            found.append((n, d.m_Width, d.m_Height, str(d.m_TextureFormat), os.path.relpath(p, root)))
            try:
                d.image.save(os.path.join(out, "tex_" + n.replace("'", "_") + ".png"))
            except Exception as e:
                found.append(("save failed " + n, str(e)))
json.dump(found, open(os.path.join(out, "tex_found.json"), "w"), indent=1)
print("DONE", len(found))

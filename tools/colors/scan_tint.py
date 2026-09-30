import UnityPy, os, sys, json, re
UnityPy.config.FALLBACK_UNITY_VERSION = "6000.3.9f1"
root = r"C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2\GraveyardKeeper2_Data"
outdir = os.path.dirname(os.path.abspath(__file__))
prog = open(os.path.join(outdir, "progress.txt"), "w")
paths = []
for dp, dn, fn in os.walk(root):
    for f in fn:
        if f.endswith((".bundle", ".assets")):
            paths.append(os.path.join(dp, f))
shaders = {}
luts = []
skins = {}
donkey_tex = []
for i, p in enumerate(paths):
    if i % 300 == 0:
        prog.write(f"{i}/{len(paths)}\n"); prog.flush()
    try:
        env = UnityPy.load(p)
    except Exception:
        continue
    rel = os.path.relpath(p, root)
    for obj in env.objects:
        t = obj.type.name
        try:
            if t == "Shader":
                d = obj.read_typetree()
                name = d.get("m_ParsedForm", {}).get("m_Name", "?")
                if name in shaders:
                    continue
                pf = d.get("m_ParsedForm", {})
                props = [(pp.get("m_Name"), pp.get("m_Description"), pp.get("m_Type"), pp.get("m_DefValue[0]"), pp.get("m_DefValue[1]"), pp.get("m_DefValue[2]"), pp.get("m_DefValue[3]")) for pp in pf.get("m_PropInfo", {}).get("m_Props", [])]
                kws = d.get("m_ParsedForm", {}).get("m_KeywordNames", None) or d.get("keywordNames", None)
                shaders[name] = {"bundle": rel, "props": props, "keywords": kws}
            elif t == "Texture2D":
                raw = obj.get_raw_data()
                m = re.match(rb"\x00*", raw)
                d = obj.read()
                n = d.m_Name
                if "lut" in n.lower() or n.lower().startswith("pal") or "palette" in n.lower():
                    luts.append((n, d.m_Width, d.m_Height, rel))
            elif t == "MonoBehaviour":
                raw = obj.get_raw_data()
                if b"0301" in raw[:64] or b"donkey" in raw[:80]:
                    try:
                        d = obj.read_typetree()
                        skins[d.get("m_Name", "?") + "@" + rel] = d
                    except Exception as e:
                        skins["raw@" + rel + str(obj.path_id)] = raw[:400].hex()
        except Exception as e:
            continue
json.dump({"shaders": shaders, "luts": luts, "skins": skins}, open(os.path.join(outdir, "tint_scan.json"), "w"), indent=1, default=str)
prog.write("DONE\n"); prog.close()

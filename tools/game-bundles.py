"""Read the game's addressable bundles with UnityPy (read-only; nothing in the game folder is written).

Subcommands:
  containers <out.tsv>             every bundle's container paths: bundle<TAB>asset path (~10 s, 10 processes)
  words <out.tsv> <word>...        case-insensitive byte search through every object (meshes/textures/audio skipped)
  graph <bundle>                   a FlowCanvas graph (visual script): nodes + connections
  part <bundle>                    a SceneWgoContentPart: flags, wgo/wso/zone counts, GameObject names
  sceneconfig <bundle>             a GameSceneConfig: content refs, autoload flags, conditional loads
  scripts <bundle> <script-bundle> MonoBehaviour classes in a bundle, MonoScripts resolved from the second bundle
  tree <bundle> [depth] [--names]  the GameObject hierarchy of a prefab bundle: per node its name, children, component
                                   kinds and how many objects/colliders lie below; --names also prints, per subtree,
                                   the object names below it grouped by prefix (to plan name-based filters)
  loose <file> <word>              a word in a loose data file (relative to GraveyardKeeper2_Data, e.g.
                                   resources.assets = GameBalance; no typetrees there): object, script class, context

The game folder comes from --game, else GK2_GAME_DIR, else <GameDir> in the repo's Directory.Build.props.
Needs UnityPy (pip install UnityPy). Written for docs/seasonal-events.md (2026-11-11, build 26404424)."""
import argparse, collections, glob, json, os, re, sys, time, warnings
from multiprocessing import Pool

warnings.filterwarnings("ignore")
UNITY_VERSION = "6000.3.9f1"
SKIP_TYPES = ("Mesh", "Texture2D", "AudioClip", "Shader", "AnimationClip")


def game_dir(arg):
    if arg:
        return arg
    if os.environ.get("GK2_GAME_DIR"):
        return os.environ["GK2_GAME_DIR"]
    props = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Directory.Build.props")
    m = re.search(r"<GameDir[^>]*>([^<]+)</GameDir>", open(props, encoding="utf-8").read())
    if not m:
        sys.exit("no <GameDir> in Directory.Build.props; pass --game")
    return m.group(1)


def bundle_dir(game):
    d = os.path.join(game, "GraveyardKeeper2_Data", "StreamingAssets", "aa", "StandaloneWindows64")
    if not os.path.isdir(d):
        sys.exit("no bundle folder at " + d)
    return d


def unitypy():
    try:
        import UnityPy
    except ImportError:
        sys.exit("UnityPy missing: pip install UnityPy")
    UnityPy.config.FALLBACK_UNITY_VERSION = UNITY_VERSION
    return UnityPy


def safe_out(path):
    p = os.path.abspath(path).replace("\\", "/").lower()
    if "/release/" in p or "/dist/" in p or "/graveyard keeper 2/" in p:
        sys.exit("refusing to write into release/, dist/ or the game folder: " + path)
    return open(path, "w", encoding="utf-8")


# ---- containers -------------------------------------------------------------------------------------------------
def _containers(p):
    UnityPy = unitypy()
    b = os.path.basename(p)
    try:
        rows = []
        for o in UnityPy.load(p).objects:
            if o.type.name == "AssetBundle":
                for k, _ in o.read_typetree().get("m_Container", []):
                    rows.append(f"{b}\t{k}")
        return rows or [f"{b}\t<none>"]
    except Exception as e:
        return [f"{b}\tERR {e}"]


# ---- words ------------------------------------------------------------------------------------------------------
_RX = None


def _init_words(words):
    global _RX
    _RX = re.compile(b"(" + b"|".join(re.escape(w.encode()) for w in words) + b")", re.I)


def _words(p):
    UnityPy = unitypy()
    b = os.path.basename(p)
    rows = []
    try:
        for o in UnityPy.load(p).objects:
            if o.type.name in SKIP_TYPES:
                continue
            try:
                raw = o.get_raw_data()
            except Exception:
                continue
            for m in _RX.finditer(raw):
                ctx = raw[max(0, m.start() - 60):m.end() + 60]
                ctx = "".join(chr(c) if 32 <= c < 127 else "." for c in ctx)
                rows.append(f"{b}\t{o.type.name}\t{o.path_id}\t{m.group(0).decode('ascii', 'replace')}\t{ctx}")
                if len(rows) > 200:
                    return rows
    except Exception as e:
        rows.append(f"{b}\tERR\t0\t-\t{e}")
    return rows


def run_pool(fn, files, out, init=None, initargs=()):
    t0 = time.time()
    with safe_out(out) as f, Pool(10, initializer=init, initargs=initargs) as pool:
        for i, rows in enumerate(pool.imap_unordered(fn, files, chunksize=40)):
            if rows:
                f.write("\n".join(rows) + "\n")
            if i % 5000 == 0:
                print(i, f"{time.time() - t0:.0f}s", flush=True)
    print("done", len(files), "bundles", f"{time.time() - t0:.0f}s")


# ---- single-bundle readers --------------------------------------------------------------------------------------
def load(bdir, *names):
    UnityPy = unitypy()
    return UnityPy.load(*[os.path.join(bdir, n) for n in names])


def cmd_graph(env):
    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        tt = o.read_typetree()
        if not tt.get("_serializedGraph"):
            continue
        print("graph of", tt.get("m_Name"))
        g = json.loads(tt["_serializedGraph"])
        for i, n in enumerate(g.get("nodes", [])):
            t = n.get("$type", "?").replace("FlowCanvas.Nodes.", "").replace("GK2.FlowCanvasNodes.", "")
            extra = {k: v for k, v in n.items() if k not in ("$type", "$id", "_position", "_comment") and v not in (None, "", [], {})}
            print(f"[{i}] id={n.get('$id')} {t} {json.dumps(extra, default=str)[:400]}")
        for c in g.get("connections", []):
            print(f"  {c.get('_sourceNode', {}).get('$ref')} -> {c.get('_targetNode', {}).get('$ref')}")


def cmd_part(env):
    names = collections.Counter()
    for o in env.objects:
        if o.type.name == "GameObject":
            names[o.read_typetree().get("m_Name", "")] += 1
        elif o.type.name == "MonoBehaviour":
            tt = o.read_typetree()
            if "autoLoadContent" in tt:
                print("flags:", {k: tt.get(k) for k in ("autoLoadContent", "keepLoaded", "conditionalLoad", "explicitSetWorldId", "explicitWorldId")})
                print("counts:", {k: len(tt.get(k, [])) for k in ("wgos", "wsos", "worldZones", "wgoUniqueIds", "wsoUniqueIds")})
    print("GameObject names (top 80):")
    for k, v in names.most_common(80):
        print(f"  {v:4d} {k}")


def cmd_sceneconfig(env):
    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        d = o.read_typetree()
        if "autoLoadFlags" not in d:
            continue
        refs, fl, cl = d.get("contentDataRefs", []), d.get("autoLoadFlags", []), d.get("conditionalLoads", [])
        names = d.get("assetNames", [])
        print("config", d.get("m_Name"), "| contents:", len(refs))
        for i, r in enumerate(refs):
            print(i, r.get("m_AssetGUID"), names[i] if i < len(names) else "", "auto=", fl[i] if i < len(fl) else None,
                  "cond=", cl[i] if i < len(cl) else None)


def cmd_scripts(env):
    scripts = {}
    for o in env.objects:
        if o.type.name == "MonoScript":
            s = o.read()
            scripts[o.path_id] = f"{s.m_Namespace}.{s.m_ClassName}".lstrip(".")
    c = collections.Counter()
    for o in env.objects:
        if o.type.name == "MonoBehaviour":
            tt = o.read_typetree()
            if "_serializedGraph" not in tt:
                pid = tt["m_Script"]["m_PathID"]
                c[scripts.get(pid, f"?{pid}")] += 1
    for k, v in c.most_common():
        print(f"{v:5d} {k}")


def cmd_tree(env, depth, names):
    gos, tfs = {}, {}
    for o in env.objects:
        if o.type.name == "GameObject":
            gos[o.path_id] = o.read_typetree()
        elif o.type.name in ("Transform", "RectTransform"):
            tfs[o.path_id] = o.read_typetree()
    kind_of = {o.path_id: o.type.name for o in env.objects}
    tf_of_go = {}
    for pid, t in tfs.items():
        tf_of_go[t["m_GameObject"]["m_PathID"]] = pid

    def go_of(tf_pid):
        return tfs[tf_pid]["m_GameObject"]["m_PathID"]

    def comps(go_pid):
        c = collections.Counter()
        for x in gos[go_pid].get("m_Component", []):
            k = kind_of.get(x["component"]["m_PathID"], "?")
            if k not in ("Transform", "RectTransform"):
                c[k] += 1
        return c

    def below(tf_pid):
        n, col, nm = 0, 0, collections.Counter()
        for ch in tfs[tf_pid].get("m_Children", []):
            cp = ch["m_PathID"]
            if cp not in tfs:
                continue
            g = go_of(cp)
            n += 1
            cc = comps(g)
            col += sum(v for k, v in cc.items() if k.endswith("Collider"))
            nm[re.sub(r"[ _-]*(\(\d+\)|\d+|pivot|MERGE S_OBJ|_sh\d*|_lt\d*|_ut|_gnd|-blackout)*$", "", gos[g].get("m_Name", ""))] += 1
            a, b, c2 = below(cp)
            n, col = n + a, col + b
            nm.update(c2)
        return n, col, nm

    roots = [pid for pid, t in tfs.items() if t["m_Father"]["m_PathID"] == 0]

    def show(tf_pid, d):
        g = go_of(tf_pid)
        go = gos[g]
        n, col, nm = below(tf_pid)
        cc = comps(g)
        kids = tfs[tf_pid].get("m_Children", [])
        active = "" if go.get("m_IsActive", True) else " [inactive]"
        print(f"{'  ' * d}{go.get('m_Name')}{active}: {len(kids)} children, {n} below, {col} colliders below; own {dict(cc)}")
        if names and d < depth and n:
            print(f"{'  ' * d}  names below: " + ", ".join(f"{k} {v}" for k, v in nm.most_common(25)))
        if d < depth:
            for ch in kids:
                if ch["m_PathID"] in tfs:
                    show(ch["m_PathID"], d + 1)

    for r in roots:
        show(r, 0)


def cmd_loose(path, word, before=500, after=500):
    UnityPy = unitypy()
    if not os.path.isfile(path):
        sys.exit("no such file: " + path)
    rx = re.compile(re.escape(word.encode()), re.I)
    for o in UnityPy.load(path).objects:
        if o.type.name in SKIP_TYPES:
            continue
        try:
            raw = o.get_raw_data()
        except Exception:
            continue
        hits = list(rx.finditer(raw))
        if not hits:
            continue
        cls = "?"
        try:
            sc = o.read(check_read=False).m_Script.read()
            cls = f"{sc.m_Namespace}.{sc.m_ClassName}".lstrip(".")
        except Exception:
            pass
        print(f"=== {o.type.name} path_id={o.path_id} class={cls} hits={len(hits)}")
        last = -1
        for m in hits:
            if m.start() < last:
                continue
            s, e = max(0, m.start() - before), min(len(raw), m.end() + after)
            last = e
            txt = re.sub(r"\.{4,}", " | ", "".join(chr(c) if 32 <= c < 127 else "." for c in raw[s:e]))
            print(f"--- at {m.start()}\n{txt}\n")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--game")
    ap.add_argument("cmd", choices=("containers", "words", "graph", "part", "sceneconfig", "scripts", "loose", "tree"))
    ap.add_argument("--names", action="store_true", help="tree: name groups below each node")
    ap.add_argument("args", nargs="*")
    a = ap.parse_args()
    game = game_dir(a.game)
    if a.cmd == "loose":
        if len(a.args) != 2:
            sys.exit("usage: loose <file under GraveyardKeeper2_Data> <word>")
        cmd_loose(os.path.join(game, "GraveyardKeeper2_Data", a.args[0]), a.args[1])
        return
    bdir = bundle_dir(game)
    if a.cmd in ("containers", "words"):
        if not a.args or (a.cmd == "words" and len(a.args) < 2):
            sys.exit("usage: containers <out.tsv> | words <out.tsv> <word>...")
        files = sorted(glob.glob(os.path.join(bdir, "*.bundle")))
        if a.cmd == "containers":
            run_pool(_containers, files, a.args[0])
        else:
            run_pool(_words, files, a.args[0], _init_words, (a.args[1:],))
        return
    if not a.args:
        sys.exit(f"usage: {a.cmd} <bundle file name>")
    if a.cmd == "tree":
        cmd_tree(load(bdir, a.args[0]), int(a.args[1]) if len(a.args) > 1 else 2, a.names)
        return
    env = load(bdir, *a.args)
    {"graph": cmd_graph, "part": cmd_part, "sceneconfig": cmd_sceneconfig, "scripts": cmd_scripts}[a.cmd](env)


if __name__ == "__main__":
    main()

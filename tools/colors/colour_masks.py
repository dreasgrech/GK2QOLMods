import json, collections, os, sys
from PIL import Image, ImageDraw
d = r"C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2\BepInEx\GK2-sprites"
out = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(d, "sprites-data.js"), encoding="utf-8").read()
rows = json.loads(src[src.index("["):src.rindex("]") + 1])
atl = {"C0": Image.open(os.path.join(d, "sactx-0-2048x2048-Uncompressed-Characters-a31f6aed.png")).convert("RGBA"),
       "C1": Image.open(os.path.join(d, "sactx-1-2048x2048-Uncompressed-Characters-a31f6aed.png")).convert("RGBA")}


def crop(name):
    for r in rows:
        if r[0] == name:
            a = atl[r[1]]
            x, y, w, h = r[2], r[3], r[4], r[5]
            y = a.height - y - h
            return a.crop((x, y, x + w, y + h))
    return None


def sheet(kind, frames, tag, scale=4):
    ims = [crop(f) for f in frames]
    ims = [i for i in ims if i is not None]
    cols = collections.Counter()
    for r in rows:
        n = r[0]
        if not n.startswith("0301_"):
            continue
        g = "hed" if "_hed_" in n else ("wheel" if "_wheel_" in n else "bdy")
        if g != kind:
            continue
        im = crop(n)
        for p in im.getdata():
            if p[3] > 0:
                cols["%02x%02x%02x" % p[:3]] += 1
    names = [c for c, _ in cols.most_common()]
    base = ims[0]
    W, H = base.width * scale, base.height * scale
    per = 6
    rowsn = (len(names) + per - 1) // per
    sh = Image.new("RGBA", (per * (W + 8), rowsn * (H + 18)), (40, 40, 60, 255))
    dr = ImageDraw.Draw(sh)
    for i, c in enumerate(names):
        rgb = tuple(int(c[k:k + 2], 16) for k in (0, 2, 4))
        im = base.copy()
        px = im.load()
        for yy in range(im.height):
            for xx in range(im.width):
                p = px[xx, yy]
                if p[3] == 0:
                    continue
                if p[:3] == rgb:
                    px[xx, yy] = (255, 0, 255, 255)
                else:
                    g = (p[0] + p[1] + p[2]) // 3
                    px[xx, yy] = (g // 2 + 60, g // 2 + 60, g // 2 + 60, 255)
        im = im.resize((W, H), Image.NEAREST)
        ox, oy = (i % per) * (W + 8), (i // per) * (H + 18)
        sh.paste(im, (ox, oy + 16))
        dr.rectangle((ox, oy, ox + 14, oy + 14), fill=rgb + (255,))
        dr.text((ox + 18, oy + 2), f"{i} {c} {cols[c]}", fill=(255, 255, 255, 255))
    sh.save(os.path.join(out, f"mask_{tag}.png"))
    json.dump(names, open(os.path.join(out, f"colours_{kind}.json"), "w"))
    print(tag, len(names))


sheet("bdy", ["0301_bdy_static_left"], "bdy")
sheet("hed", ["0301_hed_static_down"], "hed", 6)
sheet("wheel", ["0301_wheel_walk_left_01"], "wheel", 6)

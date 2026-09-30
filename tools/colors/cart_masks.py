import os, collections, colorsys, json
from PIL import Image, ImageDraw
out = os.path.dirname(os.path.abspath(__file__))
res = {}
for part in ["bars", "body", "wheel_f", "lantern"]:
    for pg in ["prt1", "prt2"]:
        im = Image.open(os.path.join(out, f"tex_donkey_cart_{part}_{pg}.png")).convert("RGBA")
        bb = im.getbbox()
        im = im.crop(bb)
        cols = collections.Counter("%02x%02x%02x" % p[:3] for p in im.getdata() if p[3] > 0)
        names = [c for c, _ in cols.most_common()]
        res[f"{part}_{pg}"] = names
        S = 6
        W, H = im.width * S, im.height * S
        per = 6
        rn = (len(names) + per - 1) // per
        sh = Image.new("RGBA", (per * (W + 8), rn * (H + 18)), (40, 40, 60, 255))
        dr = ImageDraw.Draw(sh)
        for i, c in enumerate(names):
            rgb = tuple(int(c[k:k + 2], 16) for k in (0, 2, 4))
            m = im.copy(); px = m.load()
            for y in range(m.height):
                for x in range(m.width):
                    p = px[x, y]
                    if p[3] == 0:
                        continue
                    px[x, y] = (255, 0, 255, 255) if p[:3] == rgb else ((p[0] + p[1] + p[2]) // 6 + 60,) * 3 + (255,)
            m = m.resize((W, H), Image.NEAREST)
            ox, oy = (i % per) * (W + 8), (i // per) * (H + 18)
            sh.paste(m, (ox, oy + 16))
            dr.rectangle((ox, oy, ox + 14, oy + 14), fill=rgb + (255,))
            dr.text((ox + 18, oy + 2), f"{i} {c}", fill=(255, 255, 255, 255))
        sh.save(os.path.join(out, f"mask_{part}_{pg}.png"))
json.dump(res, open(os.path.join(out, "colours_cart.json"), "w"), indent=0)
for k, v in res.items():
    print(k, v)

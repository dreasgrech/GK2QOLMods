"""Cut a box out of a screenshot for a mod page and scale it to a width (media/README.md lists each picture's box).

    python tools/crop-shot.py <screenshot> <left,top-right,bottom> -o <out.png> [--width 560] [--height H]

The box is in the screenshot's pixels, left,top-right,bottom (the form media/README.md uses, so a re-shoot from the
same spot is cut the same way). --width scales the cut to that width, its height following (Lanczos); 0 keeps the
cut as it is. --height with --width scales to exactly that size (a Nexus header: --width 1300 --height 372, from a
box of the same 1300:372 shape). Prints the box, the cut's size and the output's size. Pillow only.
"""
import argparse
import os
import re
import sys

from PIL import Image


def parse_box(text):
    m = re.fullmatch(r"\s*(\d+)\s*,\s*(\d+)\s*-\s*(\d+)\s*,\s*(\d+)\s*", text)
    if not m:
        sys.exit(f"box must be left,top-right,bottom, not {text!r}")
    l, t, r, b = (int(v) for v in m.groups())
    if r <= l or b <= t:
        sys.exit(f"empty box {text!r}")
    return l, t, r, b


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("shot")
    ap.add_argument("box", help="left,top-right,bottom in the screenshot's pixels")
    ap.add_argument("-o", "--out", required=True)
    ap.add_argument("--width", type=int, default=560, help="output width (0: the cut's own)")
    ap.add_argument("--height", type=int, default=0, help="output height, with --width (0: keep the cut's shape)")
    a = ap.parse_args()
    if not os.path.isfile(a.shot):
        sys.exit(f"no such file: {a.shot}")
    l, t, r, b = parse_box(a.box)
    im = Image.open(a.shot).convert("RGB")
    if r > im.width or b > im.height:
        sys.exit(f"box {a.box} is outside the {im.width}x{im.height} screenshot")
    cut = im.crop((l, t, r, b))
    out = cut
    if a.width > 0:
        h = a.height if a.height > 0 else max(1, round(cut.height * a.width / cut.width))
        if a.height > 0 and abs(cut.width / cut.height - a.width / a.height) > 0.01:
            print(f"note: the box is {cut.width}x{cut.height}, not the {a.width}:{a.height} shape; it will be stretched")
        out = cut.resize((a.width, h), Image.LANCZOS)
    elif a.height > 0:
        sys.exit("--height needs --width")
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    out.save(a.out, optimize=True)
    print(f"{os.path.basename(a.shot)} box {l},{t}-{r},{b} ({cut.width}x{cut.height}) -> {a.out} {out.width}x{out.height}, {os.path.getsize(a.out):,} bytes")


if __name__ == "__main__":
    main()

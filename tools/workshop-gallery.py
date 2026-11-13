"""A picture for a Steam Workshop item's image gallery: a box cut out of a screenshot at full size, never scaled.

    python tools/workshop-gallery.py <screenshot> <left,top-right,bottom> <Mod> <name> [--overwrite]

The gallery opens a picture in a viewer about 1500 px wide, so the page pictures (560 wide, for the description) come
out blurred there (GK2DonkeyAPI, 2026-10-31); a gallery picture is the same cut at the screenshot's own pixels. The
box is in the screenshot's pixels, left,top-right,bottom (the form media/README.md uses). <Mod> is the mod's folder
in media/workshop/ (GK2TechTreeSearch, ...); the picture is written as media/workshop/<Mod>/gallery/<name>.jpg, a
JPEG under 1 MB (quality 95, lower only to get under it; the gallery takes up to 2 MB a picture, so 1 MB leaves room).
The release tool takes only Thumbnail.* from media/workshop/<Mod>/, so the gallery folder never reaches the item's
files. The gallery is uploaded by hand on the item's page (Add/Remove Images); an existing picture is replaced only
with --overwrite (it may be on Steam already).
"""
import os
import re
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LIMIT = 1_000_000


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(args) != 4:
        sys.exit(__doc__)
    shot, box, mod, name = args
    m = re.fullmatch(r"\s*(\d+)\s*,\s*(\d+)\s*-\s*(\d+)\s*,\s*(\d+)\s*", box)
    if not m:
        sys.exit(f"box must be left,top-right,bottom, not {box!r}")
    l, t, r, b = (int(v) for v in m.groups())
    if r <= l or b <= t:
        sys.exit(f"empty box {box!r}")
    if not os.path.isfile(os.path.join(ROOT, "src", mod, mod + ".csproj")):
        sys.exit(f"no such mod: src/{mod}/{mod}.csproj")
    if not os.path.isfile(shot):
        sys.exit(f"no such file: {shot}")
    out = os.path.join(ROOT, "media", "workshop", mod, "gallery", name + ".jpg")
    if os.path.exists(out) and "--overwrite" not in sys.argv:
        sys.exit(f"{os.path.relpath(out, ROOT)} exists (it may be on Steam already): --overwrite to replace it")
    im = Image.open(shot).convert("RGB")
    if r > im.width or b > im.height:
        sys.exit(f"box {box} is outside the {im.width}x{im.height} screenshot")
    cut = im.crop((l, t, r, b))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    for quality in (95, 92, 90, 88, 85):
        cut.save(out, "JPEG", quality=quality, optimize=True)
        if os.path.getsize(out) < LIMIT:
            break
    else:
        os.remove(out)
        sys.exit(f"{name}: over 1 MB even at quality 85; cut a smaller box")
    print(f"{os.path.relpath(out, ROOT)}: {cut.width}x{cut.height}, quality {quality}, {os.path.getsize(out):,} bytes "
          f"(from {os.path.basename(shot)} box {l},{t}-{r},{b})")


if __name__ == "__main__":
    main()

"""A Steam Workshop preview picture for a mod's item, cut from a screenshot: media/workshop/<Mod>/Thumbnail.jpg.

    python tools/workshop-thumbnail.py <screenshot> <left,top-right,bottom> <Mod> [--width W] [--nearest]

<Mod> is the mod's folder name as tools/make-release-zip.py stages it (GK2TechTreeSearch, GK2OwnedCount, ...). The box
is in the screenshot's pixels, left,top-right,bottom (the form media/README.md uses), and should be square: Steam shows
the preview in square tiles and letterboxes anything else (the GK2DonkeyAPI repo's first, 16:9 previews had bars,
2026-10-31). The cut is scaled to --width (default 1024; never upscaled above 2x) and written as a JPEG under 1 MB
(Steam's limit for the preview); --nearest keeps pixel art crisp when it is scaled up by a whole number. The tool
keeps the aspect it is given and says so when it is not square. make-release-zip.py copies the file into
workshop/<Mod>/ as Thumbnail.jpg, which the game's uploader sends as the preview.
"""
import os
import re
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main():
    argv = sys.argv[1:]
    args = [a for k, a in enumerate(argv) if not a.startswith("--") and (k == 0 or argv[k - 1] != "--width")]
    if len(args) != 3:
        sys.exit(__doc__)
    width = 1024
    if "--width" in argv:
        width = int(argv[argv.index("--width") + 1])
    nearest = "--nearest" in argv
    shot, box, mod = args
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
    im = Image.open(shot).convert("RGB")
    if r > im.width or b > im.height:
        sys.exit(f"box {box} is outside the {im.width}x{im.height} screenshot")
    im = im.crop((l, t, r, b))
    w, h = im.size
    if w != h:
        print(f"note: {w}x{h} is not square; Steam's tiles will show bars")
    scale = width / w
    if scale > 2:
        scale = 2
    if nearest:
        scale = max(1, int(scale)) if scale >= 1 else scale
    size = (round(w * scale), round(h * scale))
    im = im.resize(size, Image.NEAREST if nearest and scale >= 1 else Image.LANCZOS)
    out_dir = os.path.join(ROOT, "media", "workshop", mod)
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, "Thumbnail.jpg")
    for quality in (92, 88, 84, 80, 75, 70):
        im.save(out, "JPEG", quality=quality, optimize=True)
        if os.path.getsize(out) < 1_000_000:
            break
    print(f"{os.path.relpath(out, ROOT)}: {size[0]}x{size[1]}, quality {quality}, {os.path.getsize(out):,} bytes "
          f"(from {os.path.basename(shot)} box {l},{t}-{r},{b}, {w}x{h})")


if __name__ == "__main__":
    main()

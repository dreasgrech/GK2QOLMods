"""The release zip of a mod for Nexus Mods (docs/release-checklist.md, section 7).

    python tools/make-release-zip.py GK2TechTreeSearch [GK2OwnedCount ...]

One zip per mod, named after the mod's title with its words dashed: release/GK2-Tech-Tree-Search-1.0.1.zip, holding
BepInEx/plugins/<Mod>/<Mod>.dll from dist/ and nothing else (a folder per mod, as the Saddle Up zips; BepInEx finds
plugins in subfolders; mod managers see the BepInEx layout), so the zip extracts straight into the game folder. The
version is the mod's Plugin.cs Version constant; it must be the csproj's <Version>, the first version heading of the
mod in CHANGELOG.md (no Unreleased left) and present in the dist DLL (a dist not rebuilt since the bump is refused).
Runs tools/check-dist.py first and stops if it fails. Every zip is read back and its DLL compared with dist/ byte for
byte. The zips in release/ are kept in git, and a zip that exists there is never overwritten (a released version is
never rebuilt: raise the version; --overwrite only for one that was never uploaded; 2026-10-30).
"""
import hashlib
import os
import re
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
DIST = os.path.join(ROOT, "dist")
OUT = os.path.join(ROOT, "release")
INNER = "BepInEx/plugins/"


def read(rel):
    return open(os.path.join(ROOT, rel), encoding="utf-8").read()


def version_of(mod):
    src = read(f"src/{mod}/Plugin.cs")
    m = re.search(r'public const string Version = "([0-9.]+)"', src)
    if not m:
        sys.exit(f"{mod}: no Version constant in src/{mod}/Plugin.cs")
    v = m.group(1)
    proj = read(f"src/{mod}/{mod}.csproj")
    pv = re.search(r"<Version>([^<]+)</Version>", proj)
    if not pv or pv.group(1).strip() != v:
        sys.exit(f"{mod}: Plugin.cs says {v}, the csproj says {pv.group(1).strip() if pv else 'nothing'}")
    return v


def changelog_version(mod, title):
    """The first version heading under the mod's '## <title>' in CHANGELOG.md; 'Unreleased' there means not released."""
    text = read("CHANGELOG.md")
    m = re.search(rf"^## {re.escape(title)}\s*$(.*?)(?=^## |\Z)", text, re.M | re.S)
    if not m:
        sys.exit(f"{mod}: no '## {title}' section in CHANGELOG.md")
    h = re.search(r"^### (\S+)", m.group(1), re.M)
    if not h:
        sys.exit(f"{mod}: no version heading under '## {title}' in CHANGELOG.md")
    return h.group(1)


def title_of(mod):
    """'GK2 Tech Tree Search': the plugin's name in its BepInPlugin attribute (the zip is named after it)."""
    m = re.search(r'\[BepInPlugin\(Guid, "([^"]+)", Version\)\]', read(f"src/{mod}/Plugin.cs"))
    if not m:
        sys.exit(f"{mod}: no BepInPlugin(Guid, \"name\", Version) attribute")
    return m.group(1)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write_zip(mod, v, overwrite=False):
    dll = os.path.join(DIST, mod + ".dll")
    if not os.path.isfile(dll):
        sys.exit(f"{mod}: no dist/{mod}.dll (bash tools/build-dist.sh {mod})")
    data = open(dll, "rb").read()
    if v.encode("utf-8") not in data and v.encode("utf-16le") not in data:
        sys.exit(f"{mod}: dist/{mod}.dll does not carry version {v}: rebuild it (bash tools/build-dist.sh {mod})")
    path = os.path.join(OUT, "-".join(title_of(mod).split()) + f"-{v}.zip")  # GK2-Tech-Tree-Search-1.0.1.zip
    entries = ["BepInEx/", INNER, INNER + mod + "/", INNER + mod + "/" + mod + ".dll"]
    os.makedirs(OUT, exist_ok=True)
    if os.path.isfile(path) and not overwrite:
        sys.exit(f"{path} exists: a released zip is never rebuilt (raise the version), or pass --overwrite for one that was never uploaded")
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for name in entries[:-1]:  # the folders as entries of their own: some extractors and managers want them
            z.writestr(zipfile.ZipInfo(name), b"")
        z.write(dll, entries[-1])
    with zipfile.ZipFile(path) as z:
        if z.testzip() is not None:
            sys.exit(f"{path} reads back broken")
        if z.namelist() != entries:
            sys.exit(f"{path}: unexpected entries {z.namelist()}")
        if sha(z.read(entries[-1])) != sha(data):
            sys.exit(f"{mod}.dll in {path} differs from dist/")
    print(f"{path}: {title_of(mod)} {v}, {os.path.getsize(path):,} bytes")
    for n in entries:
        print("  " + n)


def main():
    overwrite = "--overwrite" in sys.argv[1:]
    mods = [a for a in sys.argv[1:] if a != "--overwrite"]
    if not mods:
        sys.exit(__doc__.strip())
    for mod in mods:
        if not os.path.isfile(os.path.join(SRC, mod, mod + ".csproj")):
            sys.exit(f"no such mod: src/{mod}/{mod}.csproj")
    if subprocess.run([sys.executable, os.path.join(ROOT, "tools", "check-dist.py")]).returncode != 0:
        sys.exit("check-dist.py failed: no zip written")
    for mod in mods:
        v = version_of(mod)
        title = title_of(mod)
        cv = changelog_version(mod, title[len("GK2 "):] if title.startswith("GK2 ") else title)
        if cv != v:
            sys.exit(f"{mod}: version {v} but CHANGELOG.md's first heading for it is {cv}")
        write_zip(mod, v, overwrite)


if __name__ == "__main__":
    main()

"""The release zip of a mod for Nexus Mods and its Steam Workshop staging folder (docs/release-checklist.md, section 7).

    python tools/make-release-zip.py GK2TechTreeSearch [GK2OwnedCount ...]
    python tools/make-release-zip.py GK2TechTreeSearch --no-workshop   # the zip only
    python tools/make-release-zip.py GK2TechTreeSearch --no-zip        # the Workshop folder only (a zip already uploaded)
    python tools/make-release-zip.py GK2TechTreeSearch --dry-run       # every check, then what the zip and the Workshop
                                                                       # folder would hold; nothing written

One zip per mod, named after the mod's title with its words dashed: release/GK2-Tech-Tree-Search-1.0.2.zip, holding
BepInEx/plugins/<Mod>/<Mod>.dll from dist/ and, for a mod with translations, BepInEx/plugins/<Mod>/Languages/*.csv
from src/<Mod>/Languages/ (the English template en.csv and each language's file, read beside the DLL by the window
code; they ship as the working tree has them, so uncommitted ones are refused), nothing else (a folder per mod, as the
Saddle Up zips; BepInEx finds
plugins in subfolders; mod managers see the BepInEx layout), so the zip extracts straight into the game folder. No
modinfo.json in the zip: next to the exe nothing reads it (the GK2DonkeyAPI repo's docs/mods-window-and-workshop.md,
2026-10-30). The version is the mod's Plugin.cs Version constant; it must be the csproj's <Version>, the first version
heading of the mod in CHANGELOG.md (no Unreleased left) and present in the dist DLL (a dist not rebuilt since the bump
is refused). Runs tools/check-dist.py on the named mods first and stops if it fails. Every zip is read back and its
DLL compared with dist/ byte for byte. The zips in release/ are kept in git, and a zip that exists there is never
overwritten (a released version is never rebuilt: raise the version; --overwrite only for one that was never
uploaded; 2026-10-30).

The Workshop folder, workshop/<Mod>/ (not in git), is what the game's Workshop creator (Shift+F11 in the main menu,
the game launched through Steam) uploads as the item: the same plugin folder as the zip, plus modinfo.json (id = the
plugin GUID, name = the plugin's title, version: the Mods window's tile reads them), and the uploader's own files, which
it never sends: Thumbnail.jpg|png (the preview, from media/workshop/<Mod>/, tools/workshop-thumbnail.py),
_description.bbcode (from media/GK2_<Short>_Workshop.txt, Steam's BBCode; over 8,000 bytes Steam refuses the whole
upload) and _changenote.txt (docs/nexus/<title>-<version>-workshop-changenote.txt, else the Nexus note
docs/nexus/<title>-<version>-changelog.md, unwrapped to one line per bullet: Steam shows every line break).
"""
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
DIST = os.path.join(ROOT, "dist")
OUT = os.path.join(ROOT, "release")
MEDIA = os.path.join(ROOT, "media")
NOTES = os.path.join(ROOT, "docs", "nexus")
WORKSHOP = os.path.join(ROOT, "workshop")
INNER = "BepInEx/plugins/"
STEAM_DESCRIPTION_MAX = 8000  # bytes; over it the game's uploader gets InvalidParam and nothing goes up (GK2DonkeyAPI, 2026-10-30)


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


def guid_of(mod):
    m = re.search(r'public const string Guid = "([^"]+)"', read(f"src/{mod}/Plugin.cs"))
    if not m:
        sys.exit(f"{mod}: no Guid constant in src/{mod}/Plugin.cs")
    return m.group(1)


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


def short_title(mod):
    """'Tech Tree Search': the title without the GK2 prefix (the CHANGELOG section, the note names)."""
    t = title_of(mod)
    return t[len("GK2 "):] if t.startswith("GK2 ") else t


def sha(data):
    return hashlib.sha256(data).hexdigest()


def dist_dll(mod, v):
    dll = os.path.join(DIST, mod + ".dll")
    if not os.path.isfile(dll):
        sys.exit(f"{mod}: no dist/{mod}.dll (bash tools/build-dist.sh {mod})")
    data = open(dll, "rb").read()
    if v.encode("utf-8") not in data and v.encode("utf-16le") not in data:
        sys.exit(f"{mod}: dist/{mod}.dll does not carry version {v}: rebuild it (bash tools/build-dist.sh {mod})")
    return dll, data


def languages_of(mod):
    """The mod's translation files (src/<Mod>/Languages/*.csv), sorted; [] without any."""
    folder = os.path.join(SRC, mod, "Languages")
    if not os.path.isdir(folder):
        return []
    return [os.path.join(folder, n) for n in sorted(os.listdir(folder)) if n.lower().endswith(".csv")]


def uncommitted_languages(mods):
    """What git says of the mods' Languages folders: the files ship as the working tree has them (the DLL as dist/ was
    built), so only committed ones go out. "" when all are."""
    dirs = [os.path.relpath(os.path.join(SRC, m, "Languages"), ROOT) for m in mods if os.path.isdir(os.path.join(SRC, m, "Languages"))]
    if not dirs:
        return ""
    return subprocess.run(["git", "-C", ROOT, "status", "--porcelain", "--"] + dirs, capture_output=True, text=True).stdout.strip()


def plugin_entries(mod, v):
    """[(source file or None for a folder, the path under BepInEx/plugins/<Mod>/ or the folders above it)], zip order."""
    dll, _ = dist_dll(mod, v)
    entries = [(None, "BepInEx/"), (None, INNER), (None, INNER + mod + "/"), (dll, INNER + mod + "/" + mod + ".dll")]
    langs = languages_of(mod)
    if langs:  # its translations beside it
        entries.append((None, INNER + mod + "/Languages/"))
        entries += [(p, INNER + mod + "/Languages/" + os.path.basename(p)) for p in langs]
    return entries


def write_zip(mod, v, overwrite=False, dry=False):
    entries = plugin_entries(mod, v)
    path = os.path.join(OUT, "-".join(title_of(mod).split()) + f"-{v}.zip")  # GK2-Tech-Tree-Search-1.0.2.zip
    if os.path.isfile(path) and not overwrite:
        sys.exit(f"{path} exists: a released zip is never rebuilt (raise the version), or pass --overwrite for one that was never uploaded")
    if dry:
        print(f"{path}: would hold ({title_of(mod)} {v})")
        for src, name in entries:
            print("  " + name + (f" ({os.path.getsize(src):,} bytes)" if src else ""))
        return
    os.makedirs(OUT, exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for src, name in entries:
            if src is None:  # the folders as entries of their own: some extractors and managers want them
                z.writestr(zipfile.ZipInfo(name), b"")
            else:
                z.write(src, name)
    with zipfile.ZipFile(path) as z:
        if z.testzip() is not None:
            sys.exit(f"{path} reads back broken")
        if z.namelist() != [name for _, name in entries]:
            sys.exit(f"{path}: unexpected entries {z.namelist()}")
        for src, name in entries:
            if src is not None and sha(z.read(name)) != sha(open(src, "rb").read()):
                sys.exit(f"{name} in {path} differs from {os.path.relpath(src, ROOT)}")
    print(f"{path}: {title_of(mod)} {v}, {os.path.getsize(path):,} bytes")
    for _, n in entries:
        print("  " + n)


def unwrap(note):
    """One line per paragraph and per bullet. Steam shows every line break of a change note, and the note files are
    wrapped at ~118 columns, so each bullet would break mid-sentence on the item's page (GK2DonkeyAPI, 2026-10-31)."""
    out = []
    for line in note.split("\n"):
        s = line.strip()
        if s and out and out[-1] and not s.startswith("- "):
            out[-1] += " " + s
        else:
            out.append(line.rstrip() if s.startswith("- ") else s)
    return "\n".join(out).strip("\n") + "\n"


def changenote(mod, v):
    """The item's change note: the Workshop note written for the release (it may speak of the Workshop; the Nexus
    note never sends players to Steam, the user 2026-10-30), else the Nexus note, both unwrapped."""
    stem = "-".join(short_title(mod).lower().split()) + "-" + v  # tech-tree-search-1.0.2
    workshop_note = os.path.join(NOTES, stem + "-workshop-changenote.txt")
    nexus_note = os.path.join(NOTES, stem + "-changelog.md")
    for path in (workshop_note, nexus_note):
        if os.path.isfile(path):
            print(f"  change note: {os.path.relpath(path, ROOT)}")
            return unwrap(open(path, encoding="utf-8").read().replace("\r\n", "\n"))
    sys.exit(f"{title_of(mod)} {v}: no player note; write {os.path.relpath(workshop_note, ROOT)} (or the Nexus note "
             f"{os.path.relpath(nexus_note, ROOT)}) before staging its Workshop folder")


def stage_workshop(mod, v, dry=False):
    """workshop/<Mod>/: the item content as the zip holds it, plus modinfo.json and the uploader's own files."""
    entries = plugin_entries(mod, v)
    dest = os.path.join(WORKSHOP, mod)
    text = os.path.join(MEDIA, f"GK2_{mod[len('GK2'):]}_Workshop.txt")  # media/GK2_TechTreeSearch_Workshop.txt
    if os.path.isfile(text):
        size = len(open(text, encoding="utf-8").read().replace("\r\n", "\n").encode("utf-8"))
        if size > STEAM_DESCRIPTION_MAX:
            sys.exit(f"{os.path.relpath(text, ROOT)} is {size:,} bytes; over {STEAM_DESCRIPTION_MAX:,} Steam fails the whole upload "
                     f"(InvalidParam): shorten it. {dest} left as it was")
    note = changenote(mod, v)  # stops without a player note, before the folder is cleared
    if dry:
        print(f"{dest}: would hold ({title_of(mod)} {v}), with modinfo.json, the thumbnail, the description and the change note")
        for src, name in entries:
            if src is not None:
                print(f"  {name} ({os.path.getsize(src):,} bytes)")
        return
    if os.path.isdir(dest):
        shutil.rmtree(dest)
    for src, name in entries:  # the plugin's folder as the zip holds it: the DLL, its Languages
        target = os.path.join(dest, *name.split("/"))
        if src is None:
            os.makedirs(target, exist_ok=True)
            continue
        shutil.copyfile(src, target)
        if sha(open(target, "rb").read()) != sha(open(src, "rb").read()):
            sys.exit(f"{target} differs from {src}")
    modinfo = {"schemaVersion": 1, "id": guid_of(mod), "name": title_of(mod), "version": v}
    open(os.path.join(dest, "modinfo.json"), "w", encoding="utf-8", newline="\n").write(json.dumps(modinfo, indent=2) + "\n")
    notes = []
    thumb = None
    for ext in ("png", "jpg", "jpeg"):
        cand = os.path.join(MEDIA, "workshop", mod, "Thumbnail." + ext)
        if os.path.isfile(cand):
            thumb = cand
            break
    if thumb:
        shutil.copyfile(thumb, os.path.join(dest, os.path.basename(thumb)))
        if os.path.getsize(thumb) >= 1_000_000:
            notes.append(f"Thumbnail is {os.path.getsize(thumb):,} bytes: Steam wants the preview under 1 MB")
    else:
        notes.append(f"no media/workshop/{mod}/Thumbnail.png (or .jpg): the item uploads without a preview")
    if os.path.isfile(text):
        desc = open(text, encoding="utf-8").read().replace("\r\n", "\n")
        open(os.path.join(dest, "_description.bbcode"), "w", encoding="utf-8", newline="\n").write(desc)
        if "{{" in desc:
            notes.append(f"{os.path.relpath(text, ROOT)} still has a {{{{ placeholder }}}}: fill it in before uploading")
    else:
        notes.append(f"no {os.path.relpath(text, ROOT)}: the item uploads without a description (write it in Steam's BBCode)")
    open(os.path.join(dest, "_changenote.txt"), "w", encoding="utf-8", newline="\n").write(note)
    print(f"{dest}: the Workshop folder ({title_of(mod)} {v})")
    for dirpath, _, names in os.walk(dest):
        for n in sorted(names):
            p = os.path.join(dirpath, n)
            print(f"  {os.path.relpath(p, dest).replace(os.sep, '/')} ({os.path.getsize(p):,} bytes)")
    for n in notes:
        print("  note: " + n)


def main():
    flags = {"--overwrite", "--no-workshop", "--no-zip", "--dry-run"}
    overwrite = "--overwrite" in sys.argv[1:]
    dry = "--dry-run" in sys.argv[1:]
    workshop = "--no-workshop" not in sys.argv[1:]
    zips = "--no-zip" not in sys.argv[1:]
    unknown = [a for a in sys.argv[1:] if a.startswith("--") and a not in flags]
    if unknown:
        sys.exit(f"unknown option {unknown[0]}\n\n" + __doc__.strip())
    mods = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not mods:
        sys.exit(__doc__.strip())
    for mod in mods:
        if not os.path.isfile(os.path.join(SRC, mod, mod + ".csproj")):
            sys.exit(f"no such mod: src/{mod}/{mod}.csproj")
    if subprocess.run([sys.executable, os.path.join(ROOT, "tools", "check-dist.py")] + mods).returncode != 0:
        sys.exit("check-dist.py failed: nothing written")
    dirty = uncommitted_languages(mods)
    if dirty:
        sys.exit("translation files not committed (a zip takes Languages/ from the working tree): commit them first, nothing written\n" + dirty)
    for mod in mods:
        v = version_of(mod)
        cv = changelog_version(mod, short_title(mod))
        if cv != v:
            sys.exit(f"{mod}: version {v} but CHANGELOG.md's first heading for it is {cv}")
        if zips:
            write_zip(mod, v, overwrite, dry)
        if workshop:
            stage_workshop(mod, v, dry)


if __name__ == "__main__":
    main()

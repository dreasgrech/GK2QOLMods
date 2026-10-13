"""Checks the release DLLs in dist/ without the game: run `python tools/check-dist.py` from the repo root.

A release build (-p:ForDist=true) must carry no debug directory (its PDB path would hold this machine's user folder),
no path into this machine, and the audited game MVID the plugin pins (the mod disables itself on any other game build,
so a stale dist would switch itself off on every player's first start; found 2026-10-13: every dist DLL pinned an
older build).
Exit code 1 on any finding."""
import glob, os, re, sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PINNED = {}  # dll name -> the Assembly-CSharp MVID in its Plugin.cs (read below)
for plugin in glob.glob(os.path.join(REPO, "src", "*", "Plugin.cs")):
    src = open(plugin, encoding="utf-8").read()
    m = re.search(r'AuditedGameMvid = new System\.Guid\("([0-9a-f-]{36})"\)', src)
    name = os.path.basename(os.path.dirname(plugin))
    PINNED[name + ".dll"] = m.group(1) if m else None

bad = 0
for path in sorted(glob.glob(os.path.join(REPO, "dist", "*.dll"))):
    name = os.path.basename(path)
    b = open(path, "rb").read()
    findings = []
    # A drive letter, a colon and a backslash occur by chance in binary data (the Donkey API's lantern mesh gave "d:\BZ"): a path
    # has a folder name and a second separator after them.
    for pat in (rb"[A-Za-z]:\\[A-Za-z0-9_. ()-]{1,80}\\[^\x00\"]{0,160}", rb"[^\x00]{0,160}\.pdb"):
        for hit in re.findall(pat, b):
            findings.append("path or pdb: " + hit[:120].decode("latin-1"))
    for hit in re.findall(rb"(?:[A-Za-z]\x00:\x00\\\x00(?:[A-Za-z0-9_. ()-]\x00){1,80}\\\x00(?:[^\x00]\x00){0,120})", b):
        findings.append("path (utf-16): " + hit.decode("utf-16le", "ignore")[:120])
    if "Documents\\GitHub".encode("utf-16le") in b:
        findings.append("a path into this repo is baked in: not a -p:ForDist=true build")
    mvid = PINNED.get(name)
    if mvid and mvid[:8].encode("utf-16le") not in b:
        findings.append(f"the audited MVID {mvid} of {name[:-4]}/Plugin.cs is not in the DLL: rebuild")
    status = "ok" if not findings else "FAIL"
    print(f"{status}  {name}  {len(b):,} bytes  pinned {mvid or '(nothing)'}")
    for f in findings:
        print("      " + f)
    bad += len(findings)
sys.exit(1 if bad else 0)

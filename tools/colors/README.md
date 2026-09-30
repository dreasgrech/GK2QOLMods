# Colour investigation scripts (GK2 Mounts, Colors tab)

Offline scripts behind `docs/mounts-colors.md`. Python 3 with UnityPy, Pillow and lz4; they read the Steam install and
write next to themselves. Run in this order:

1. `scan_tint.py`: every shader's properties and keywords, every LUT/palette texture, the skin presets (`tint_scan.json`).
2. `dump_tex.py`: the zombie LUTs and the cart textures as PNGs (needs `tint_scan.json`).
3. `dxbc.py`: unpacks `Custom/3D Sprite` and `Custom/3D Object` and disassembles every program with Windows'
   `D3DCompiler_47.dll` (one text file per program).
4. `colour_masks.py`: the donkey's colours (from the probe's atlas dump in `BepInEx/GK2-sprites`) with a mask sheet
   per colour; `cart_masks.py` the same for the cart textures from step 2.

The colour lists in `src/GK2Mounts/Colors.cs` come from steps 4. Only the donkey's frames (`0301_*`) are read.

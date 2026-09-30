# GK2 Mounts: colours of the donkey, the cart and the lantern

Investigated and built on 2026-09-30, against Steam build 25601286. **Not yet tested in game.**

The question was how granularly the donkey, the cart and everything around them can be recoloured. The answer:
**every single colour of the art, on its own.**
- **Donkey sprites:** exactly, through the game's own palette swap.
- **Cart and lantern (3D):** exactly for every colour the art uses, through the game's own colour cube.
- **Lamp:** its lights, glow and lit glass on top.

## What the game has

### Two shaders, two recolour paths

**The donkey** (`bdy`, `hed`) and **the cart's wheel sprites** (`wheel_left`, `wheel_right`) are sprites on the shader
`Custom/3D Sprite`. **The cart body, shafts, wheels and lantern pole** are `Object3D` meshes (baked sprites) on
`Custom/3D Object`.

**How I read the shaders:**
- **Property lists:** from the bundles (UnityPy).
- **Compiled code:** UnityPy can't parse Unity 6 shaders, so I unpacked the LZ4 blob myself and disassembled every
  DXBC program with Windows' `D3DCompiler_47.dll` (`D3DDisassemble`): 7,402 programs for the sprite shader and 3,268
  for the 3D one.
- **Constant slots:** mapped to property names from the parameter blob in the same data. Each entry is the name,
  then the type, the size and the byte offset.

**`Custom/3D Sprite`, pixel shader, in order:**
1. `c = texture × vertex colour`. The vertex colour carries `SpriteRenderer.color` and `_Color`.
2. One of these, per keyword:
   - **`USE_PALETTE_COLOR_REPLACE`:** loops `i < _Palette_TexelSize.z`, the palette's width, with **no fixed
     cap**. It samples row v=1 as the source and row v=0 as the replacement. A texel within 0.004 of a source colour
     (distance in RGB) becomes that replacement. **An exact colour swap**, the one the keeper's outfit uses
     (`main_hero_primary_palette`, 32 wide).
   - **`USE_LUT_COLOR_REPLACE`:** a single sample of `_ReplaceLUT` at
     `u = r·31/1024 + 0.5/1024 + floor(b·31)/32`, `v = g·31/32 + 0.5/32`. That's a 32×32×32 colour cube laid out as
     1024×32. There's no blending between blue slices; filtering is the LUT texture's own. The zombies use it
     (`bdy_lut_01..05`, `hed_lut_01..05`; `bdy_lut_01` is an exact identity).
3. `lerp(c, _AdditiveTintColor.rgb, _AdditiveTintColor.a)`. Despite its name, it's an overlay.
4. `× _TintColor`, then the lighting.

**`Custom/3D Object`, pixel shader:**
- The texture goes through the same LUT lookup, with the same layout (`USE_LUT_COLOR_REPLACE` only; there is no
  palette mode).
- Then `lerp(c, _SelectionTintColor, _SelectionTintAmount)`, the game's build-mode tint.

**Other facts:**
- **Colour space:** Gamma (`PlayerSettings.m_ActiveColorSpace = 0`), so the shaders see raw byte/255 values. A
  colour read from the art is exactly what the shader compares.
- **Not usable:** `_HueShift`, `_Sat`, `_Val`, `_Brightness` and `_Contrast`. They belong to `Sprites/ColorReplace`,
  a UI shader. `SkinPresetGK2.TryToApply` sets them on the donkey's material, but `Custom/3D Sprite` has none of
  them, so they do nothing there.

### The game's C# around it

- **`SkinPresetGK2` / `SkinChangerGK2`:**
  - A skin part (body, head) sets `_Color`, plus a palette or a LUT with its keyword, on the sprite's own material
    instance (`sprite.material`).
  - The donkey's preset `0301_donkey` has no palette and white colours: the donkey is drawn as painted.
  - Zombies are recoloured with runtime presets (`ZombieSkinHelper.GetPresetForCustomizationData`: LUT per part).
- **`ColorReplacePalette` / `PaletteReplaceHelper.CombinePalettes`:** a palette is N×2, with row 1 the source
  colours and row 0 the new ones.
- **`Object3DMesh.SetLutTexture(tex)`:**
  - The game's path for building variants (`ConstructorPart`) and zombie drop views.
  - It stores the LUT on each submesh and swaps the material to `GlobalResources.matObject3DLUT`.
  - `ApplyMaterial` re-applies it from that stored state whenever the game rebuilds the property block, for example
    when the keeper walks behind the cart and it fades.
  - `null` goes back to `matObject3D`.
  - Anything written straight into a property block would be wiped by that rebuild.
- **`Object3DMesh.SetSelectionTint(color, amount)`:** stored too; build mode uses green at 0.35. Not needed here,
  because the overlay is folded into the colour maps per region.

### The art

**The donkey is painted in final colours, not index colours.** It has 42 distinct colours over all its body and head
frames; the head's 16 are a subset. Rendering where each colour sits (magenta masks per colour, scratchpad
`tint/mask_*.png`) gives clean regions:

| Region (key) | Colours | What it is |
|---|---|---|
| `Coat` | 14 | the grey-brown coat |
| `Dark` | 6 | outline, mane, ear tips, hooves |
| `Muzzle` | 6 | muzzle, belly shading |
| `Eye` | 1 | the eye's white (`FFFFFF`) |
| `Harness` | 15 | the orange collar and strap |

**The cart textures** (`donkey_cart_*_prt1`/`prt2`, 96×96, 5 to 19 colours each) split the same way:

| Region (key) | Group | What it is |
|---|---|---|
| `CartBody` | body meshes | planks (11 colours) |
| `Shafts` | shafts mesh | the wooden shafts |
| `Straps` | shafts mesh | the leather wrap at the harness end |
| `Hooks` | shafts mesh | the iron hooks |
| `Wheels` | wheel meshes and wheel sprites | wooden rims (the sprites' 14 colours included) |
| `Hubs` | wheel meshes | the iron hubs |
| `Pole` | lantern mesh | the wooden pole |
| `Lamp` | lantern mesh (and the hanging lamp) | the lamp's iron |
| `Glass` | lantern mesh | the painted blue glass of the lamp |

The wheel texture also has one magenta marker (`AC25A5`), which is left alone.

**LUT cells.** On the 32-step cube, the cart's known colours never share a cell across regions; the only shared cell
holds two shaft colours of the same region. The donkey through the cube would bleed in 7 cells, for example `292316`
(dark) with `2D2114` (harness). That's why the donkey uses the palette.

## What the mod does (`src/GK2Mounts/Colors.cs`)

**Settings.** One set for both styles, since it's the same donkey:
- **`[Colors]`:**
  - `Enabled`;
  - `SpriteMethod` (Palette or Lut);
  - `GlowColor`, `LitGlassTint`, `CharacterLightColor`.
- **`[Colors.<Region>]`, 10 each:**
  - `Hue` (degrees);
  - `Saturation`, `Brightness`, `Contrast` (multipliers);
  - `Colorize` (a hex colour whose hue and saturation replace the pixel's, keeping its brightness) with
    `ColorizeAmount`;
  - `Tint` (multiplied in);
  - `Overlay` with `OverlayAmount`;
  - `Swaps` (`SRC=DST;...`, exact replacements that win over everything else).

**Colour maps.** A region's settings are a function of the colour. For every colour of the art (the lists are
embedded in `Colors.cs`), the mod computes what it becomes, then builds per group:
- **Sprites:** an N×2 palette (point filter, clamp, RGBA32). With `SpriteMethod = Lut`, a LUT instead.
- **Meshes:** a 1024×32 LUT. It's identity everywhere, point-sampled, and every known colour's own cell holds its
  exact mapped value.

**Applying them:**
- **Sprites:** a copy of the renderer's material with the keyword and texture. The game re-skinning the donkey is
  noticed and undone every frame.
- **Meshes:** `Object3DMesh.SetLutTexture`. Meshes are chosen by their texture name (`donkey_cart_body…`,
  `…_bars…`, `…_wheel…`, `…_lantern…`), so cargo and the corpse shroud keep their own look. The hanging lamp copies
  the pole's look every frame (LampHang), so it gets the Lantern group's LUT with it.
- **The lamp's lights:** the glow colour and the character light go through `Lantern.ApplyLight`. The lit glass
  (`Custom/Day Night Object (Unlit)`) gets `_TintColor × tint` in a property block.

**Zero footprint and pooling:**
- A group whose regions are all at their defaults is never touched.
- The donkey's parts come from the game's pool (the delivery donkey reuses them). So before the view is destroyed,
  every material goes back to the game's, and every LUT is reset (`SetLutTexture(null)`) and the glass's property
  block restored. It's the same rule as the `forceRenderingOff` fix of 2026-09-30.

**API** (0.5.0, additions only):
- `Mounts.Colors`, a `MountTuning` over all these keys: session overrides, cyan locked rows, `ClearTuning` clears them.
- `Mounts.ColorRegions`: key, name, area and source colours of every region.
- `MountColorRegion.SettingNames` and `KeyOf`.
- `SpriteColorMethod`.
- Documented in `docs/mounts-api.md`.

**Panel, Colors tab** (a dev tool, as the rest of the panel):
- the master switch and the sprite method;
- a **Test** button that sets obvious colours everywhere as a session override, and a **colour report** button that
  writes to the log;
- region buttons by area, with `*` marking a changed region;
- per region:
  - swatches: the game's colour on top, the result below; click one to swap it for an exact colour;
  - sliders for hue, saturation, brightness and contrast;
  - on/off colour rows for colorize, tint and overlay;
  - a reset button;
- the lamp's light, glow and glass colours;
- reset all.

## Fix after the first test (2026-09-30): only half of each cart part recoloured

**Seen:** the user's screenshot. The cart body's four corner posts turned turquoise; the planks stayed as they were.

**Cause:**
- Every cart mesh has two submeshes, one per texture page (prefab dump: body, shafts, wheels and lantern each have 2).
  The planks are `prt1` (submesh 0) and the posts `prt2` (submesh 1).
- `Object3DSubMesh.ApplyAppropriateMaterial` writes its slot into its **own cached copy** of the renderer's material
  list (`cachedSharedMaterials`) and assigns that whole copy.
- On a mesh that is already initialised, submesh 1's older copy (slot 0 still the game's material) overwrites
  submesh 0's LUT material, so only the last submesh changes.
- The game itself only sets LUTs before a part initialises, when every cache is empty.

**Fix:** `Colors.SetLut` empties every submesh's cache first, which gives the game's own working order. It also:
- checks that every material slot is the LUT material, warning and setting it again otherwise;
- after removing the LUT, puts the renderer's exact original materials back.

## Fix: the panel flickered while dragging a slider (2026-09-30)

**Seen:** the user's 120 fps video. For a frame or two, about 50 times in 2 s, the whole panel was drawn at its
minimum size with no contents. Neither the BepInEx log nor Player.log had an error. The colour updates were cheap
(under 8 ms, only on changes).

**Measured:** the `[panel]` log lines showed `GUILayout.Window` handing back height 0 on some repaints, on every tab,
not only Colors.

**Cause:** `Draw` set the height to 0 before every call so the window could shrink, and kept whatever came back.
Events the window hadn't laid out yet returned 0, and the next repaint drew that. Dragging a slider sends extra
events every frame.

**Fix:** reset the height in the Layout event only, and never keep a result under 1 px. Confirmed by the user, with
no `[panel]` lines in the log afterwards. The `[panel]` logging stays, at most once a second, only when it happens.

## Test list (game closed until then; the DLL is deployed)

With `Debug.TuningWindow = true` in `dreas.gk2.mounts.cfg`, mount (the showcase: H or G) and open the **Colors** tab.

1. **Nothing changes by default:** mount and look. The log has `Colors: attached; N sprites (…), M cart meshes.`
   Nothing looks different from before.
2. **The test button** (in Cart style, G) should show:
   - a purple coat;
   - a red harness;
   - a green eye;
   - a turquoise cart body;
   - cyan wheels (3D rims and the sprite wheels);
   - yellow hubs;
   - green shafts;
   - a pink pole;
   - red lamp glass;
   - a blue glow and a bluish lit glass.

   Walk in both facings, behind the cart (it fades), and on a slope. Then **Write a colour report to the log** and
   send me the `[colors]` lines.
   - **If the donkey isn't purple:** switch "Donkey sprites recoloured with" to **Lut** and look again. Tell me which
     one worked.
   - **If the cart parts don't change:** the report says what they have.
3. **Clear the test colours:** everything is back to the game's look at once.
4. **One region by hand:**
   - pick Harness, click an orange swatch, move its sliders: only those pixels change;
   - Hue on Coat: the coat changes, the harness doesn't.
5. **Ride style (H):** the donkey keeps its colours; there's no cart.
6. **Pooling:** set a colour, dismount, then start a real delivery (Debug tab, "Start a real delivery now"). **The
   game's delivery donkey and cart must look normal.**
7. **Enabled off:** everything is back to the game's look, and on again brings it back.
8. **Restart the game:** your colours are kept (the cfg), and the test colours are gone (they were session only).

## Tools

The scripts that produced the colour lists and read the shaders are in `tools/colors/` (UnityPy, Pillow):
- `scan_tint.py`: shaders, LUT textures, skin presets;
- `dump_tex.py`: the zombie LUTs and the cart textures;
- `dxbc.py`: unpacks and disassembles the shader programs;
- `colour_masks.py` / `cart_masks.py`: the per-colour mask sheets and colour lists.

After a game update, re-run the mask scripts. If the art's colours changed, the lists in `Colors.cs` need the new
values: unknown colours simply stay as the game draws them.

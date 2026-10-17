# Start Workshop signage authoring source (corebuster_art_003)

The Start Workshop's slogans, labels and stencils are first-principles textures drawn by Python scripts in this folder. They need Pillow only. Every run produces byte-identical PNGs, because all randomness is seeded.

```text
python Tools/Art/generate_start_workshop_signage.py
python Tools/Art/generate_exit_portal.py
```

## Lettering and licence

`stencil_font.py` is a hand-built stencil alphabet, so the project ships no font file and needs no font licence.

- It covers A-Z, 0-9 and `- / . :`.
- Each glyph is made of rectangles, rounded rectangles and straight strokes on a 100-unit cap height.
- Letters with enclosed counters have the bridges a real cut stencil needs:
  - split stems on B, D, E, F, K, P and R;
  - split bowls on O, Q, 0, 6, 8 and 9;
  - a split apex on A.
- The stroke weight is a parameter: 18 for display lettering, 9-11 for small annotations.
- `place()` draws a line of text into a mask. `fit_cap()` sizes a line to a width.

No stock image, decal pack, photograph or third-party font is used. The earlier corebuster_env_005 and env_013 textures used Pillow's bundled default font; the restyled `Portal_To_Corridors.png` no longer does.

## Wear

The shared helpers in `generate_start_workshop_signage.py` bake the worn edges into the textures:

- `worn_paint`: stencil overspray, uneven coverage, patchy chips biased to letter edges, and scratches;
- `weather_panel`: edge grime and run-down streaks, chipped edges showing bare steel, scratches, and rust runs below bolts.

## Outputs

All outputs are in `Assets/FPS/Art/Textures/StartWorkshop/`.

| Texture | Size | Use |
| --- | --- | --- |
| `Slogan_Tools_People_Progress.png` | 1024 x 1024, alpha | Stencil paint on the west wall, 4.6 x 4.6 m |
| `Slogan_Maintain_Build_Improve_Survive.png` | 1024 x 1280, alpha | Stencil paint on the east wall, 3.68 x 4.6 m |
| `Slogan_Small_Robots_Big_Future.png` | 1024 x 1536 | Dark steel panel on the north wall, 2.6 x 3.9 m |
| `Tank_ID_P3.png` | 800 x 512 | P-3 ID plate wrapped around each pressure tank |
| `Hazard_Chevron.png` | 512 x 128, tiles in U | Hazard chevron strips, pointing along +U |
| `Blueprint_Robot_Arm.png` | 1536 x 1024 | "Better machines / brighter tomorrow" poster of the art_002 arm, 1.8 x 1.2 m |
| `Portal_To_Corridors.png` | 1536 x 320 | The env_013 exit board, restyled in place (same path and aspect) |

The slogan wording follows `target_design/start_workshop.png`; the owner confirmed it on 2026-09-29. The env_010 `Pegboard_Tools.png` was reviewed and kept as it is.

## Placement

`Tools/CoreBuster/Build Start Workshop Signage` (`StartWorkshopSignage`) places the graphics in `Level/Start_Workshop/Signage_Art003`. `ARCHITECTURE.md` describes the placement and `TESTING.md` describes the validation and legibility captures.

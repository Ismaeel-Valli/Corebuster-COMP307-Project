# Start Workshop grime decal authoring source (corebuster_render_002)

The Start Workshop's surface grime textures are first-principles images synthesised by one Python script. It needs numpy and Pillow. Every run produces byte-identical PNGs, because each texture draws from its own seeded generator (`SEED` and the texture's name).

```text
python Tools/Art/generate_start_workshop_grime.py
```

No photograph, scan, stock decal or third-party font is used. The outputs are in `Assets/FPS/Art/Textures/StartWorkshop/Grime/`, and are used as the base and normal maps of project-owned URP decal materials (`Assets/FPS/Art/Materials/Grime/`) by `StartWorkshopGrime` (`Tools/CoreBuster/Build Start Workshop Grime`).

## Method

- **Spectral noise.** `fractal()` shapes white noise with a 1/f^beta amplitude spectrum in the frequency domain. A low beta gives grain, a high beta gives large, soft shapes.
- **Gaussian profiles and warped distance fields.** Streaks, stains and puddles are gaussian or smoothstep profiles of a distance that a noise field has warped, so no edge is a clean geometric curve.
- **Drawn strokes.** The scuffs are hand-placed polylines and tapered ellipses (Pillow), blurred and modulated by grain.
- **Height to normal.** The scuff grooves and the bolt plate's rim and bolts are height fields turned into tangent-space normals with a finite-difference gradient (`normal_from_height`).
- **Alpha is the decal.** The alpha channel is coverage. The colour is defined everywhere, including where the alpha is 0, so filtering and mipmaps never pull in a dark fringe. Every texture fades to alpha 0 at its borders (`window()`), so no decal shows its projector box; the edge strip keeps its wall side at full strength because its box ends at the wall.

## Outputs

| Texture | Size | Use |
| --- | --- | --- |
| `Grime_Rust_Streak.png` | 256 x 1024 | Six wandering rust runs hanging from the top edge, with a bleed at the source and drip tips. Projected on walls under the pipe runs. Source at the TOP row. |
| `Grime_Leak_Stain.png` | 512 x 512 | A dried leak: noise-warped outline, damp core and three ochre tide-mark rings. Floor stains at drop and tank bases, and damp patches on walls. |
| `Grime_Scuff.png` + `_N` | 512 x 256 | Dark rubber drag marks and pale scrape lines, all running along U, with a groove normal map. Floor traffic wear. |
| `Grime_Oil_Puddle.png` | 512 x 512 | A near-black spill with a faint teal-to-violet rim sheen, satellite drops and a smear tail. Floor beside the work zones. |
| `Grime_Edge_Strip.png` | 1024 x 128 | Dust and grease collected at a wall's foot, heaviest at the WALL side (bottom rows), blotchy. Floor, along the walls. |
| `Grime_Bolt_Plate.png` + `_N` | 256 x 256 | A riveted repair plate: weld bead, six bolts with rust running down, a dark halo, and a raised normal map. Floor under drops and tanks, and walls. |

## Import settings

`StartWorkshopGrime` sets them on first use: trilinear, anisotropic 4, mipmaps, clamp, high-quality compression, sRGB for the base maps, the normal-map type for `_N`, and `alphaIsTransparency` off (the alpha is data).

## Not covered

URP 17's stock decal graph writes albedo and normal only, so the oil puddle is dark, not glossy. A glossy puddle needs a decal shader that also affects smoothness.

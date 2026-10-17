# North-West Room materials and rendering

`corebuster_env_036` is built by `Assets/FPS/Scripts/Environment/Editor/NorthWestRoomRendering.cs`. Nothing is copied from a third-party asset except the Modular Pack battery's palette texture, which the owned copy references unchanged.

## First-principles textures

Three greyscale albedo textures are painted in C# from seeded, tileable value noise, scratches and chips. Nothing is imported or traced. `EnsureTextures` rewrites a PNG only when its bytes change, so a rebuild does not churn the assets.

| Texture | Size | Tile | Detail | Used by |
| --- | ---: | --- | --- | --- |
| `T_Room3_WeatheredMetal_Albedo.png` | 512 | one fine-grain tile per mesh face | mottling, grain, grime streaks, scratches, chips | every textured machine material |
| `T_Room3_WallPlating_Albedo.png` | 1024 | 3.2 m wall panel | bevelled seam groove, rivet ring, grime toward the base | wall and ceiling skin copy |
| `T_Room3_FloorPlate_Albedo.png` | 1024 | 2.05 m floor plate | seam groove, corner bolts, faint diamond tread, scuffs | floor skin copy |

Each is near-neutral with a mean linear value of about 0.84. Colour comes from the material tint.

## Why the tint is compensated

The kit's `T_HeavyPipe_BareSteel_Albedo.png` and `T_HeavyPipe_RedOxide_Albedo.png` average 0.07 and 0.04 linear luminance. Any tint multiplies that, so the old turbine and electrical metal could never reach the palette's 0.22 floor, whatever colour it was set to. The Room 3 textures replace them, and `_BaseColor = swatch / texture mean` (clamped to 1) makes the **effective** albedo equal the `NorthWestRoomPalette.json` swatch. The validator multiplies the tint by the measured texture mean and checks that result.

## Shell skins

The Centre Room `Room_Surfaces` technique, rewritten for Room 3:

- The inward-facing triangles of every measured shell piece are copied, clamped onto the measured interior plane and lifted 8 mm (6 mm for the floor, under the 20 mm `Floor_Detail` overlays), then clipped to the interior volume.
- Each piece is split into 2 x 2 patches so a skin cannot collect more than the pipeline's four additional lights. `Patches` is the one place to change that.
- UVs are world-space planar (3.2 m walls, 2.05 m floor), so patches and pieces tile without seams.
- Skins have no collider, cast no shadow, and sit in `Level/NorthWest_Room/Rendering/Room_Surfaces`. Meshes are `Rendering/Shell/Surface_<prefabId>_<objectId>_<submesh>_<patch>.asset`.
- Material mapping: kit floor -> floor plate copy, kit walls -> wall plating copy, ceiling -> the same wall plating copy, door frame -> `M_Electrical_Graphite` (dark gunmetal, "frames, ribs and rails").
- Colour: walls and ceiling are the warm-grey swatch (0.50/0.48/0.42); the floor is that swatch times 0.72 (effective luminance about 0.35, above the 0.22 floor). The first build used the tan swatch on the walls; review captures showed it turning vivid orange under warm light, so it was changed. Both skin materials use metallic 0.15 and smoothness 0.30, so they take light as diffuse plating. `Tools/CoreBuster/Refresh North-West Room Materials` re-applies colours without touching the scene.

## Materials added

All under `Assets/FPS/Art/NorthWestRoom/Rendering/Materials/`, URP/Lit, flattened:

- `Copies/<source GUID>.mat` for the kit floor and kit walls (shell) and the pack's `base color` palette repaint (battery).
- `M_Room3_Electric_Emissive.mat` (electric blue-white swatch) replaces the pack's `blue emission` on the battery.
- `M_Room3_Warning_Red.mat` (warning-red swatch) is created for the beacons and warning labels in env_037 and env_038. No renderer uses it yet.

The copper/brass material the task lists already exists as `M_Pressure_Brass` (0.62/0.42/0.16, metallic 0.38), so no duplicate was made.

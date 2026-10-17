# Corridor Lamp authoring source (corebuster_art_007)

`Assets/FPS/Scripts/Environment/Editor/CorridorLampBuilder.cs` is the reproducible source for the first-principles caged corridor lamp: a vapour-proof "jelly jar" fitting with a cast housing, red glass globe, hot bulb and four-bar wire guard. In Unity 6000.5.7f1, run `Tools/CoreBuster/Build Corridor Lamp`. The batch equivalent is:

```text
Unity.exe -batchmode -projectPath <project> -executeMethod CorridorLampBuilder.BuildBatch -quit
```

The builder writes only below `Assets/FPS/Art/Corridors/CorridorLamp/`. It never opens or edits `MainScene`, and it places no lamps in any level. It creates:

- two prefabs: `P_CorridorLamp_Wall` and `P_CorridorLamp_Ceiling`;
- five mesh assets: the shared head (housing, collar, socket and guard), globe and bulb, plus the wall and ceiling mounts;
- three URP/Lit materials:
  - `CastPaint`: grey cast-metal paint, with a procedural 512 px albedo and normal map;
  - `RedGlass`: transparent, with an emissive vertical glow strip that peaks at the bulb's height;
  - `Bulb`: an opaque emissive filament;
- `CorridorLamp_Review.unity`, a 3 m x 3 m mock corridor with three wall lamps and one ceiling lamp. Its untextured `M_CorridorLamp_ReviewSurface` material and its bloom/ACES `CorridorLamp_ReviewProfile` are used only by this review scene.

## Placement convention

- Unity units are metres. The globe is 0.136 m in diameter, and the head is 0.39 m tall from hub to bottom nut.
- `P_CorridorLamp_Wall`: the root is the wall-face mounting point. Local +Z is the wall's outward normal, and the head hangs 0.22 m out from the wall. In the review scene the lamp centre is at 2.3 m.
- `P_CorridorLamp_Ceiling`: the root is the ceiling-face mounting point, and the head hangs 0.15 m below it.
- Each lamp carries one capsule collider around its head.
- Each lamp carries one `Lamp_Light`: a realtime, shadowless red point light (colour 1, 0.14, 0.055; range 5 m; intensity 2), placed at the bulb.
  - The project uses URP's forward renderer, which gives each object at most four additional lights. A long corridor floor or wall mesh will show only four lamps' light at a time, so space the lamps out or split long surfaces.
  - Tune intensity and range per corridor on the instance. Do not change them in the prefab or builder unless every corridor should change.
- When placing lamps in a room or area that has its own bounds in `SYSTEM_BOUNDS.md` (Start Workshop, North-West Room, Boss Hall), those bounds govern placement and light containment.

## Mesh winding

`ProceduralMeshBuilder.AddLatheY` winds its side faces outward only when the profile's Y decreases. Every lathe in this builder therefore lists its profile from top to bottom, without caps. The validator fails if the globe or bulb has inward-facing normals.

The existing increasing-Y lathes have inward normals: the P-3 tank body in `HeavyIndustrialKitBuilder` and the lathes in `RoboticArmBuilder`. Measured on 2026-10-05, all 250 side normals of `SM_PressureTank_P3_Body` face inward. This builder leaves them unchanged.

## Validation and budgets

Run `Tools/CoreBuster/Validate Corridor Lamp`. It checks that:

- both prefabs exist, and each stays at or below 2,000 triangles;
- each prefab has a collider, uses URP/Lit throughout, and carries exactly one realtime, shadowless point light;
- the globe and bulb face outward;
- the paint has its textures;
- the glass is transparent, and the glass and bulb keep `_EMISSION` with the `AnyEmissive` GI flag after import;
- the review scene exists.

Current triangle counts: wall lamp 1,884 and ceiling lamp 1,724. The head is 1,148, the globe 288 and the bulb 144 of those; the wall mount is 304 and the ceiling mount 144.

`Tools/CoreBuster/Capture Corridor Lamp Review` opens the review scene on its own, renders `ReviewCaptures/CorridorLamp/Corridor.png` and `Closeup.png` at 1280x720 (git-ignored), and then restores the open scene setup. It refuses to run while an open scene has unsaved changes.

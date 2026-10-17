# Centre Room reactor asset provenance

`corebuster_env_020` is reproduced by `Assets/FPS/Scripts/Environment/Editor/CentreRoomReactor.cs`, using `ProceduralMeshBuilder.cs` without modifying its existing primitives. Run **Tools/CoreBuster/Build Centre Room Reactor** with a clean saved `MainScene` open. Generated meshes and materials live under `Assets/FPS/Art/CentreRoom/Reactor/`; the builder preserves their GUIDs on subsequent runs.

- **Original geometry:** the closed stepped base's 32 radial sectors, drums, containment columns and anchors, base ribs and vent slots, collector/cooling rings, magnetic spine and induction rings. The base profile and sectors are constructed directly from vertices and triangles. Other parts use the project's first-principles cylinder, torus and block routines.
- **Reused project art:** `P_HeavyPipe_Straight_2m` and `P_HeavyPipe_Flange` from the Heavy Industrial Kit (corebuster_art_001), plus its worn bare-steel/yellow surface textures. Reactor finishes are separate material copies; source art is unchanged.
- **Original shader:** `Assets/FPS/Art/Shaders/CoreBuster/ReactorContainment.shader`. ShaderLab defines a URP forward additive pass; HLSL computes moving filaments from cylindrical UVs, a helical signal, view-dependent edge emission and a vertical end fade. The field has no gameplay script or executable-core component. The induction rings are standard URP/Lit emission materials (blue since corebuster_render_008, below).
- **Design reference:** `target_design/reactor_room.png` supplies the industrial containment silhouette and warm-metal/cool-energy contrast. No pixels or geometry are extracted from it. The measured scene determines placement and scale.

The assembly has a 5.4 m footprint and 8.6 m height. The broad base partially breaks low sightlines; the six-column upper cage stays visually open. Its single circular carving obstacle protects navigation without rebaking the level. Room lights and the finished warm/cool illumination are owned by corebuster_env_026.

Build, saved-scene, navigation and visual-review evidence are recorded in `TESTING.md`, `CENTRE_ROOM.md` and `ReviewCaptures/CentreRoom/020/`. A successful source build alone is not proof of live combat or manual player movement.

## Unstable blue energy (corebuster_render_008)

The field and rings were re-coloured and made unstable in place; geometry, footprint, collision and carving are unchanged. `CentreRoomReactor.RefreshEnergyMaterials` rewrites only the three energy materials, and the Centre Room lighting build must follow it, because the rings render through its lit copies.

- **Palette.** The field `_Color` (blue) is (0.08, 0.32, 1), `_Accent` (ice) (0.3, 0.75, 1) and `_Core` (blue-white) (0.78, 0.9, 1), at `_Intensity` 2.2 (was violet (0.24, 0.09, 0.65) and teal at 1.2). The ring assets keep their names and GUIDs: `M_Reactor_Teal` is now ice blue (0.18, 0.62, 1) and `M_Reactor_Violet` deep blue (0.08, 0.28, 1), both at 2.6x emission (was 1.4x).
- **Shader.**
  - The filament tempo rises from 0.55x to 1.6x shader time with instability, plus noise jitter, a third warp term and a finer "crackle" filament set.
  - A view-facing term adds the blue-white core column.
  - `ReactorSurge` multiplies the whole field with value-noise swells at 0.83 and 3.7 Hz, and adds brief dropouts and hot flashes that also whiten the filaments. Dropouts and flashes each occupy a 0.5 s event slot.
  - The hash, noise and surge are written once in HLSL and once in `ReactorInstabilityLight`, and must stay identical.
- **Control.** `_Instability` is Range(0, 1), default 1. At 0 the field is steady; at 1 it is the full design. It scales amplitude and filament tempo, but never the event-slot rate. Flashes cannot fall on consecutive slots, so no setting can exceed two flashes in any second (the task cap is three). The room's reactor lights read the same value from the material. `_ReviewTime` is hidden and set to -1 in the asset; it exists only so review captures can pin one instant.

The matching room-light flicker, its evidence (`ReviewCaptures/CentreRoom/render_008/`) and the remaining owner comfort review are recorded in `TESTING.md` and `CENTRE_ROOM.md`.

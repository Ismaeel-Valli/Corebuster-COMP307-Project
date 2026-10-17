# North-West Room practical lighting (corebuster_env_037)

`NorthWestRoomLighting.cs` and `NorthWestRoomLightingReview.cs` belong to the editor-only environment assembly. They rebuild `Level/NorthWest_Room/Lighting` and write fixture meshes/materials under `Assets/FPS/Art/NorthWestRoom/Lighting/`. `NorthWestRoomLightingSurfaces.cs` also partitions six broad wall/trench renderers into 13 patches to meet the four-light budget, preserving source mesh assets, area, UVs and materials. Derived surface meshes stay in `Rendering/Shell/Lighting037/` and `Floor_Detail/Lighting037/`, keyed by source GUID and renderer ID. Rebuild lighting after either surface owner. The saved scene and matching asset metadata are the playable deliverables.

## First-principles fixtures

The builder constructs a hard-normal, six-face box and a capped 16-sided cylinder from vertices and triangles. These two persistent meshes form the steel trays, twin light tubes, end caps, guard ribs, caged lenses and suspension rods. No external model or texture is used. Suspensions terminate on measured overhead renderer bounds or the room ceiling. Fixtures have no colliders, navigation obstacles or gameplay scripts.

Warm-grey housings and electric lenses reuse room-owned materials from env_036. Two new flattened URP/Lit materials supply amber and red emission (with `BakedEmissive` flags to survive URP import; the objects do not contribute GI and no bake is run); emission is accompanied by real spot lights. Material assets preserve their GUIDs on rebuild. No runtime material property block is needed.

## Lighting and ownership

The density plan's bay fronts determine the perimeter practicals. Linear aisle fixtures light the open floor, blue-white keys mark the electrical banks, and red beacons draw the westward boss approach. All lights are realtime and use the ordinary actor-compatible culling mask. No renderer-layer, shared-pack material, pipeline, environment-lighting, door or terminal override is made. The existing room-owned electric lens receives the same emission-flag repair, also preserved in `NorthWestRoomRendering` for later regeneration.

`BuildBatch` validates before saving, then reopens and validates the saved scene. It checks the room framework, density targets, protected scene component state, shared-pack hashes, isolated Undo, material/shader references, full square spot-frustum containment, a maximum of four additional lights on room renderers and actor-sized samples, and direct cone coverage at each bay front and open-space centre. Neighbour captures use the env_036 cameras and the same narrowly defined visible-interior windows above the boss-door leaves.

## Commands and evidence

- Editor: `Tools/CoreBuster/Build North-West Room Lighting`, `Validate North-West Room Lighting`, `Capture North-West Room Lighting`.
- Batch: Unity 6000.5.7f1 with `-batchmode -projectPath <project> -executeMethod NorthWestRoomLighting.BuildBatch -quit -logFile <log>`. Graphics are required for the rendered comparisons.
- Read-only saved-scene checks: `NorthWestRoomLighting.ValidateBatch` and `NorthWestRoomLighting.CaptureBatch`.
- Evidence: `ReviewCaptures/NorthWestRoom/037/Validation.txt`, `Build.txt`, `After/` and `Neighbours/`.

Direct cone coverage is a geometric check, not proof of unobstructed sightlines, combat readability or acceptable exposure. Review the rendered room and both live waves before signing off. `TESTING.md` records executed checks and remaining owner verification.

## Executed validation (2026-10-02)

Unity 6000.5.7f1 `BuildBatch` exited 0 (`Logs/env037_final.log`): 24 lights, 23 room materials, 2,319 room renderers and 480 actor-sized samples at most 4/4 lights, no outside renderer hits, all 26 bay fronts/five open-space centres directly covered, protected-state checks, density, rendering/floor validators and save/reload passed. The 38 scene-lit captures and seven neighbour comparisons are under the evidence folder above. The owner reported checking the result and approved it on 2026-10-03; env_037 is complete in `DONE_TASKS.md`. No separate instrumented live-wave performance measurement was run; the independent env_043 review remains separate.

# North-West Room survey evidence

This folder holds the accepted Unity Editor measurement for `corebuster_env_029` and a plan drawn from it.

- `Measurement_029.json` is the measurement snapshot.
- `Layout_029.svg` is the plan, with north (+Z) up.
- `DensityPlan_040.svg` is the corebuster_env_040 bay, open-space, lane and loop plan, drawn from the tracked bay map at `Assets/FPS/Art/NorthWestRoom/NorthWestRoomDensityPlan.json`. Regenerate both with `python Tools/Art/plan_north_west_room_density.py` (`Tools/Art/NorthWestRoomDensity.md`).
- The full room specification and the remaining owner review are in `NORTH_WEST_ROOM.md` and `PLAN.md` at the repository root.

## What the JSON records

The source scene's SHA-256 and Unity version, then:

- **Room frame:** world-space interior bounds, per-wall collider samples, and every shell renderer's state.
- **Doors and approach:** door openings (fixed-collider and visible), and the boss-door approach profile up to the Boss Hall floor.
- **Terminals:** access, reach and sightlines for both terminals, including the Master Terminal's three slots and `LookAt`.
- **Encounter:** all eight spawn references, with NavMesh and warp projections, and the wave composition.
- **Navigation:** NavMesh samples and path corners.
- **Derived zones:** the safe dressing zones.
- **Game flow:** objective and game-flow settings.

Object IDs refer to the saved `MainScene`. A later scene save may need a fresh survey.

## Reading the fields

- **`onNavMesh = false`.** `navPosition` is then only a finite placeholder copied from `position`, not a successful projection.
- **Door offsets.** `clearMin` / `clearMax` and `visibleOpenMin` / `visibleOpenMax` are offsets in metres along that door's `along` vector from its `position`.
  - The visible aperture includes fixed shell and frame triangles, plus the leaves translated to their configured fully-open positions. The live scene is not moved.
  - `lintelY` and `visibleLintelY` are absolute world heights.
- **Terminal eye.** `eyeY` is physical ground plus the player prefab's camera height (1.44 m), not the visible floor plus an assumed height.
- **`pitchToFocus`.** Positive means the focus is below eye level.
- **Terminal reach.** `reachUsable` / `reachSamples` count 0.25 m floor points whose eye is in range, whose capsule is clear and whose sightlines are clear.
- **Zones.** Each `zones` entry is a design reservation box. Its `kind` is one of `heavy-perimeter`, `wall-flush`, `flush-only`, `reserved`, `column`, `wall-services`, `corner` or `overhead`. None is a scene object.
- **Blocked ring samples.** `ringBodyBlocked` counts static capsule overlaps; `ringActorOverlaps` counts overlaps with mobile agents.
- **Partial paths.** The partial across-door paths describe the saved locked doors.

This snapshot does not certify unlocked runtime connectivity, a player walk, or wave AI behaviour.

## To regenerate

1. Close any other Editor on the project, and open a clean saved `Assets/FPS/Scenes/MainScene.unity` alone in Unity 6000.5.7f1.
2. Run `Tools/CoreBuster/Measure North-West Room`, or `NorthWestRoomSurvey.MeasureBatch` from the command line.
3. Review `ReviewCaptures/NorthWestRoom/029/Measurement.json`, then copy the accepted snapshot here as `Measurement_029.json`.
4. Run `python Tools/Art/render_north_west_room_survey.py Docs/NorthWestRoom/Measurement_029.json Docs/NorthWestRoom/Layout_029.svg`. The renderer is a local authoring tool under the `Tools/Art/*.py` ignore rule.

`Tools/CoreBuster/Capture North-West Room Survey` creates eight review images in the ignored capture folder. Neither command saves scene assets or rebakes navigation.

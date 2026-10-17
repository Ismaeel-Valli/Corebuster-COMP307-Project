# North-West Room density plan (corebuster_env_040)

`plan_north_west_room_density.py` is a local authoring tool, covered by the `Tools/Art/*.py` ignore rule. It writes the Room 3 bay map and its drawing from the accepted corebuster_env_029 survey.

- **Input.** `Docs/NorthWestRoom/Measurement_029.json`, the measured interior, centre, keep-outs, spawn discs, annulus and connector paths.
- **Outputs.**
  - `Assets/FPS/Art/NorthWestRoom/NorthWestRoomDensityPlan.json`: the tracked bay map in world X/Z, which `NorthWestRoomDensity` and later builders read.
  - `Docs/NorthWestRoom/DensityPlan_040.svg`: the plan drawing, north up.
- **Run.** `python Tools/Art/plan_north_west_room_density.py`. It uses Python 3 and the standard library only.

## What the script checks before writing

- Every bay and loop-anchor footprint is tested against the movement-band reservations, with the same rule as `NorthWestRoomFramework.Layout.KeepOutViolation`:
  - the room volume;
  - both door approaches and both terminal keep-outs;
  - the 2 m spawn discs;
  - the radius-4–8 m annulus;
  - the 1.5 m connector half-width.
- Each planned lane and each survey connector is at least as wide as required. Widths are measured with perpendicular probes against the planned footprints.
- Each loop encloses a loop anchor and keeps 0.4 m from every footprint.

If any check fails, the script stops without writing.

## How it is laid out

- **Coordinates.** All footprints are written in room-local metres: `u` east from the west wall and `v` north from the south wall. They are converted to world coordinates on output.
- **Built bays.** The four built bays copy the saved collider extents of corebuster_env_031 and corebuster_env_032. They are derived from those builders' survey-relative offsets, not hand-measured.
- **Projections.** These assume each planned footprint is fully solid in the movement band, which is the worst case for clear floor:
  - clear floor on the same 0.25 m grid the validator uses;
  - occupied wall line, where a bay counts when it touches the 3 m wall column;
  - lane widths.

## Changing the plan

1. Edit the `BAYS`, `ANCHORS`, `OPEN_SPACES`, `LANES`, `LOOPS`, `BUDGETS` or `TARGETS` tables.
2. Rerun the script.
3. Run `NorthWestRoomDensity.MeasureBatch` and review the JSON and SVG diff.

A change to the targets or budgets needs the owner's approval (`NORTH_WEST_ROOM.md`).

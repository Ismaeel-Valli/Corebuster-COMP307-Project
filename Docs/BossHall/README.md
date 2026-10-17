# Boss Hall survey evidence

This folder holds the accepted Unity measurement and Play-mode traverse for `corebuster_env_047`, and a plan drawn from them.

- `Measurement_047.json` is the measurement snapshot from `BossHallSurvey` (scene SHA-256 `4ab15c73…0dd0af`, Unity 6000.5.7f1, 2026-10-04).
- `Traverse_047.txt` is the `BossHallTraverse` Play-mode result. It covers the route legs, the stair matrix (every stair walked up and down) and the under-wall capsule probes.
- `Layout_047.svg` is the plan, with north (+Z) up.
- The room specification, findings and safe zones are in `BOSS_HALL.md`. The remaining owner verification is in `PLAN.md`.

## Later Boss Hall evidence

- `Rendering_010.txt` is the corebuster_render_010 rendering-base verification record.
- `Backdrop_049.txt` is the corebuster_env_049 assembly backdrop record: placement envelopes, reserved attachment envelopes, budgets, turret-line and boundary results.
- `Navigation_049.txt` is that task's Play-mode navigation and player-capsule probe result.
- `MechanicalArms_006.txt`, `Navigation_006.txt`, `SceneScope_006.txt`, `Reconciliation_006.txt`, `ReconciliationBindings_006.txt` and `Review_006.md` are the corebuster_art_006 records.
- `Services_050.txt` is the corebuster_env_050 services record. It covers the measured bands, hulls, backdrop contacts, envelopes, budgets, attachment, intersection and turret results, and the neighbour counts. `Navigation_050.txt` is its Play-mode probe, `SceneScope_050.txt` its YAML audit and `Review_050.md` its visual review.
- `Meltdown_054.txt`, `MeltdownPreflight_054.txt`, `MeltdownCapture_054.txt`, `MeltdownRuntime_054.txt`, `SceneScope_054.txt` and `Review_054.md` are the corebuster_env_054 visual-only meltdown records: recipes and envelopes, boundary/coverage/cue checks, the Play-mode pause/registry/execution probe, the YAML audit and the visual review.

## What the JSON records

The source scene's SHA-256 and Unity version, then:

- **Player body:** eye height, capsule radius and height, crouch height, step offset and slope limit, all read from the saved player.
- **Hall frame:**
  - interior bounds and per-wall collider samples (`walls[].lowGaps` holds the floor +0.5 m probes);
  - floor, tier, ceiling and skylight levels;
  - shell inventory, duplicates (`duplicateRenderers`, `duplicateColliders`), partial tile overlaps, `negativeScaleColliders` and `materials`.
- **Entry:** the east opening (`entry`), the incline profile from the Boss Hall Door (`incline`, with per-sample turret visibility) and `turretRevealDistance`.
- **Decks:**
  - `decks` holds the per-cluster footprint, tops, widths, pinches, edges, joins, headroom and NavMesh coverage;
  - `deckFootprints` holds every deck piece's collider box (the walking reservation is their union).
- **Stairs:** foot and top points, rise, run, slope, tread and clear width, risers (`typicalRiser`, `riserHeights`, the raw `profile`), collider layers and the NavMesh foot-to-top path.
- **Bay doors:** bounds, face, closure and approach.
- **Rails:** every rail collider (`rails`, with `crossesEye`) and the firing stations (`railTests`).
- **Clearances:** vertical column classes (`columns`) and `highestStructureTopY`.
- **Actors:** all 11 starting transforms, with agent, detection and health values, envelopes, the path to the entry, the path to the North-West Room and entry visibility.
- **Turret:** `turret` holds the footprint, head sweep, muzzle origin, rest axis, sight corridor and firing-line visibility per level.
- **Focal axis:** `focalYaw`, `focalDistance`, `focalOffset` and `focalWestPoint`.
- **Navigation:** settings and data asset, floor and roof coverage, `keyPoints` (the traverse waypoints) and `routes` (agent paths).
- **Derived data:** `zones` (design reservations), `objectives`, `findings` and `notes`.

Object IDs refer to the saved `MainScene`. A later scene save needs a fresh survey.

## Reading the fields

- **`onNavMesh = false`.** `navPosition` is then only a finite placeholder copied from `position`.
- **Deck widths.** Raw widths count every supported cell, including cells inside wall and rail colliders. `clearWidth*` also needs the saved player capsule to fit, so use the clear values.
- **Stair risers.** `typicalRiser` is the repeating step. A single larger `maxRiser` is a narrow dip at a landing join; check it against `profile`.
- **Zone kinds.** `deep-pocket`, `heavy-perimeter`, `wall-flush`, `flush-only`, `reserved`, `corner`, `wall-services`, `column`, `overhead` and `reserved-volume`. None is a scene object.
- **Routes.** These are Edit-mode baked NavMesh paths, agent evidence only. Player traversal is in `Traverse_047.txt`.

## To regenerate

1. Close any other Editor on the project. Open a clean, saved `Assets/FPS/Scenes/MainScene.unity` on its own in Unity 6000.5.7f1.
2. Run `Tools/CoreBuster/Measure Boss Hall`, or `BossHallSurvey.MeasureBatch` from the command line.
3. Run `Tools/CoreBuster/Run Boss Hall Traverse`, or `BossHallTraverse.RunBatch` without `-quit`.
4. Review `ReviewCaptures/BossHall/047/Measurement.json` and `Traverse.txt`, then copy the accepted files here as `Measurement_047.json` and `Traverse_047.txt`.
5. Run `python Tools/Art/render_boss_hall_survey.py Docs/BossHall/Measurement_047.json Docs/BossHall/Layout_047.svg`. The renderer is a local authoring tool under the `Tools/Art/*.py` ignore rule.

`Tools/CoreBuster/Capture Boss Hall Survey` writes ten review images to the ignored capture folder. None of these commands saves scene assets or rebakes navigation.

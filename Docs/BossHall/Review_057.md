# Boss Hall graphics - corebuster_env_057

Implemented 2026-10-07 in Unity 6000.5.7f1. The owner waived the encounter prerequisite
for this task only, and explicitly accepted the audited `3eb1892` baseline.
Final encounter verification remains corebuster_env_058.

## Saved result

Three 4 x 1 m **FINAL ASSEMBLY** identity plates occupy Wayfinding: the env_056
cradle reservation and two measured first-tier gallery wall patches above walking
headroom. Cream stencil lettering and amber safety bands use the project's
first-principles glyph source. One flattened opaque URP/Unlit material maintains
print contrast without a new light, emission, collision, script, navigation
change or interaction prompt. There are three renderers/six triangles, no added
prefab/assembly roots, and the hall stays at 20/20 assigned materials.
Provenance and reproducible texture hash: `Tools/Art/BossHallGraphics.md`.

The cradle-only first review failed catwalk readability: the north rail hid the
plate and the south view was too oblique. Gallery-facing copies fix that on
independently measured backing, with the same texture/material. Gallery patch
selection checks corner backing, empty front collision/renderer envelopes and
all ordinary keep-outs; no stair, walkway or upper finale platform is changed.

## Approved baseline reconciliation

The worktree started clean at HEAD `3eb1892`. The local env_056 handoff predates
its corridor/quality merge, so the first build stopped before scene authoring.
`BaselineAudit_057.txt` proves all 3,055 Boss Hall blocks exactly unchanged;
102 corridor-light blocks were added and the root list extended. The merged
quality work adds two URP pipelines/renderers, changes Graphics/Quality settings
and IntroMenu, and normalizes tutorial glass. Both pipelines retain four lights
per object. This is audited prior work, not graphics-task authoring.

The owner approved accepting this exact baseline. Reconciliation exited 0
(`Logs/env057_reconcile.log`) and kept MainScene byte-identical. Only protected
fingerprints/counts and skin-manifest binding fields changed; survey and skin
records are unchanged. Framework, installed recipes, rendering, neighbouring
frameworks and sign reservation checks pass (`Reconciliation_057.txt`).
This does not certify all-quality appearance or gameplay.

## Saved-state verification

- Read-only preflight exited 0 (`GraphicsPreflight_057.txt`).
- Revised three-plate build exited 0 (`Logs/env057_build_gallery.log`): five
  negative guards (reversed face, moved plate, repeated lettering, collision and
  wrong owner), isolated clear/Undo, save/reload and installed framework checks.
- 65,731 protected outside/other-section states and 3,508 shared asset entries
  are unchanged. No foreign material or property block is allowed.
- `SceneScope_057.txt`: twelve added YAML blocks and one changed Wayfinding child
  list, zero semantic changes elsewhere. Unity reordered existing blocks during
  save; the larger textual diff does not indicate other authored changes.
- All six strict neighbouring/backward views are pixel-identical. Threshold and
  landing forward views change only inside the hall projection. The forward
  incline has existing foreground variation (53,020 outside-projection pixels
  above delta 4); it is recorded, not claimed identical. No global render,
  shared material or foreground scene state is changed by this task.
- PNG regeneration is byte-identical. Saved texture reads left-to-right with
  asset-backed unit tiling and zero offset; no ephemeral property blocks.

## Approach readability / agent review

Actual standing eye height is 1.44 m, saved vertical FOV 60 degrees, captures 960 x 540,
with saved lighting and no extra lamps. Floor/gallery cameras look toward the
plate; the five reveal views retain their original gaze. Collider-clear rays
are reported separately from visual review in `Saved/Approaches.txt`.

| Approach | Distance | Sign width | Clear sample rays |
| --- | ---: | ---: | ---: |
| Cradle floor | 8 / 12 / 20 m | 233 / 156 / 94 px | 15/15 each |
| North gallery, direct | 5.23 m | 339 px | 15/15 |
| North gallery, along deck | 10.93 m | 74 px | 15/15 |
| South gallery, direct | 5.33 m | 333 px | 15/15 |
| South gallery, along deck | 10.97 m | 75 px | 15/15 |
| Arena threshold | 61.67 m | 31 px | 12/15 |

Agent review: correct, unmirrored wording; clear near-floor and both gallery
identity; a small secondary accent in the full-arena reveal. The distant entry
and upper reverse cannot read the cradle text; the latter is occluded by the
protected structure. Gallery copies supply nearby catwalk identity. There is no
arrow, status display, button, control-bank graphic or interaction instruction.

## Play-mode and fresh verification

The corrected probe passed (`Logs/env057_runtime_verified.log`,
`GraphicsRuntime_057.txt`), returning to Edit mode with the exact saved scene
and all shared asset bytes unchanged. It walked three threshold-to-arena runs
(20.6-21.0 m), the 69.5 m upper ring and short approaches on both original
first-tier galleries. Hall AI remained live and fired throughout; the player was
invincible, its control/input disabled and 21 actor colliders disabled to permit
the controlled walk. The door remained locked; no keycard-to-win claim is made.
The real tutorial weapon locker armed the player in Play mode only.

Static Wayfinding bindings and floor/gallery capture state passed in Play.
An initial whole-scene capture assertion was too broad for live animated actor
poses; the corrected runtime capture checks static owned graphics, while saved
scene/shared hashes remain mandatory after exit. Edit-mode captures continue to
protect all scene state. The known headless InGameMenuManager startup exception,
original negative-scale colliders and shared shadow-atlas pressure remain
recorded limitations, not successful gameplay verification.

Fresh post-Play `BossHallGraphics.ValidateBatch` exited 0
(`Logs/env057_validate.log`), including the framework and neighbouring framework
validators. No new runtime script, objective, actor or execution behavior was
added. Normal moving human combat, progression, profiling and all-quality
rendering acceptance remain corebuster_env_058.

## Owner acceptance

The owner accepted the final graphics on 2026-10-07, including exact wording,
hierarchy and decorative-terminal distinction. All required implementation and
verification are recorded. The full task block was removed from `PLAN.md` and
the exact ID/date/evidence/limits recorded in `DONE_TASKS.md`. This acceptance
does not clear the independent gameplay/quality gates for corebuster_env_058.

Saved MainScene SHA-256:
`bf7e765e42c0a0239e051904ab827735ba573e5a451490fba8594ba16e6b4f3f`.

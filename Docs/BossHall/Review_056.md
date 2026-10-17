# Boss reveal and entrance composition — corebuster_env_056

Reviewed 2026-10-07 in Unity 6000.5.7f1 on the reconciled finale merge (`389eff4`).

The owner waived this task's encounter prerequisite and delegated the entry-sequence review to the agent.
This record is agent review, not human sign-off. Neither decision clears corebuster_env_058.

## What changed the brief

The merged giant-core finale (`dcda0f5`, `FinaleBuilder`) hangs an armoured core at about (-296, 20, 115.3).
It sits above the turret, under the skylight, and its ramp/bridge platform is on the upper ring. The core sat
between env_055's overhead `Turret_Key` and the turret, so from the entrance the turret was shadowed and the
core read as a black dome. The reveal now frames two focal objects: the unchanged turret and the giant core.

## Prerequisite: explicit baseline reconciliation

- `MergeAudit_056.txt`: all 3,035 `Boss_Hall_Dressing` YAML blocks are semantically unchanged by the merge.
  Four differ only in Unity's line wrapping. The merge changed three protected enemy prefabs and added
  142 protected material/prefab paths.
- `BossHallBaselineReconciliation.AcceptFinaleMergeBatch` (owner-approved) re-accepted both protected
  digests: scene 55,245 → 59,929 states, shared 3,358 → 3,500 entries. Every Boss Hall, rendering, Centre
  and Room 3 validator passes. Finale core and platform touch no dressing bounds (`Reconciliation_056.txt`).
- The merged tutorial wing's hidden text MeshFilters are never saved, so their snapshot keys were
  session entity IDs. `BossHallSnapshot` now keys unsaved hierarchy objects by hierarchy position and
  component order, making the digest reproducible across sessions and graphics modes (`BUGS.md`).

## Five fixed actual-eye views

Player eye 1.44 m, vertical FOV 60° (the saved `PlayerWeaponsManager.DefaultFov`), 960×540. Forward views
look west along the route, level, as a walking player does. Positions are measured live on real collision.

| View | Eye (X, Y, Z) | Gaze | Surface |
| --- | --- | --- | --- |
| Door_Side | (-252.20, 1.62, 115.45), 1.00 m from the door | yaw 270, pitch 0 | Corridor Incline Full |
| Middle_Incline | (-257.25, 4.66, 115.46), 6.05 m | yaw 270, pitch 0 | Corridor Incline Full |
| Arena_Threshold | (-263.04, 7.64, 115.30), 11.85 m | yaw 270, pitch 0 | Floor Tile 04 |
| Just_Inside | (-266.04, 7.64, 115.30), 14.85 m | yaw 270, pitch 0 | Floor Tile 04 |
| Upper_Reverse | (-313.53, 18.12, 115.30) | yaw 90, pitch -8 | Catwalk Long (86) |

## Before and after

Visible area is counted by repainting each group in two flat colours; luma is the mean displayed
luminance (0–255) of those pixels in the post-processed frame. Before = reconciled merge; after = saved.

| Measure | Before | After |
| --- | ---: | ---: |
| Turret luma, threshold / just inside | 41.9 / 41.4 | 95.0 / 93.4 |
| Giant core luma, threshold / just inside | 3.5 / 3.3 | 74.4 / 73.6 |
| Giant core luma, middle incline | 2.7 | 84.2 |
| Turret visible px, threshold (no dressing) | 1,375 (1,374) | 1,374 (1,374) |
| Giant core visible px, threshold | 3,476 | 3,477 |
| Assembly / upper tiers / floor px, threshold | 10,613 / 76,096 / 245,463 | 10,612 / 76,099 / 245,464 |
| Central floor cells in sight, threshold | 2,504 / 2,695 | 2,504 / 2,695 |
| Door status lamp px, incline / Room 3 | 1,120 / 1,162 | 1,120 / 1,162 |

Dressing hides no turret or core pixel in any view. Along the walk the core's underside appears at 5 m,
the assembly at 7 m and the turret at 9 m (top landing).

## Refinement (through the Lighting owner)

Only `Lighting/Turret_Key` changed, rebuilt by `BossHallLighting.BuildRevealBatch` (owner tool, env_055
recipe, evidence revision 056). Old: (-293.70, 24.80, 115.30) aimed at the turret head, range 20, 48°,
intensity 5000. New: (-283.70, 12.50, 115.30) on the entry axis, aimed 9.5° up at (-295.70, 14.50, 115.30),
range 18, 56°, intensity 1500, still the one low-tier shadow light. The core sits in the hotspot and the
turret in the soft edge. Rejected variants (kept in `ReviewCaptures/BossHall/056/`): level aim at 5000
(turret luma 196, flat white in the ring aperture) and 1800 (turret 177, core 28); range 20 crossed the
ceiling by 8 cm. Light count stays 24/24; no geometry, material, collider, audio or gameplay change.

## Sign reservation for corebuster_env_057

`BossHallReveal.SignReservations` resolves one envelope against real collision and the contract on every
run: `Sign_Final_Assembly`, a 4.0 × 1.0 m plate on the cradle front at (-324.37, 8.15, 121.80), facing +X,
north of the turret and below the inter-tier firing volume. It shows 232/234 px from the threshold and
just inside, and leaves turret and core pixels unchanged. Gallery-wall bands had no collision-valid
candidate inside the forward frame from either approach eye, so gallery signage belongs to env_057's
own floor/gallery approaches. No sign geometry is created.

## Verification

| Check | Result and evidence |
| --- | --- |
| Lighting build | Exit 0: five guards, clear/Undo, save/reload; 63,051 outside states and 3,500 shared entries unchanged (`Lighting_056.txt`, `Logs/env056_lighting_build.log`) |
| Boundary | Strict neighbours: zero changed pixels outside the measured hall projection. Room3_BossDoor changes 22 px, all hall-projected: the lit core through the gap above the door leaves (`LightingCapture_056.txt`) |
| Lighting analysis | 24/24, 0 errors, 0 outside-renderer hits; renderer peak 8/4 (baseline 5/4), actor bounds 6/4, saved-core stress 7/4 (baseline 5/4); steady cones 110/248 |
| Reveal review | Before/proposals/saved, read-only, scene/shared unchanged (`RevealBefore_056.txt`, `RevealProposed_056.txt`, `Reveal_056.txt`) |
| Lighting runtime | Exit 0: registries 18 enemies/19 actors/12 targets with no dressing entries; three lamps pause/resume; real armour-break → removed from vulnerable pool → core consumed before death on floor and gallery (`LightingRuntime_056.txt`) |
| Reveal runtime | Exit 0: three door-threshold → incline → arena entries (20.7–20.9 m, 5.2 s each) with live AI, door still locked; upper reverse walk 69.4 m around the ring (`RevealRuntime_056.txt`) |
| Fresh validation | `ValidateRevealBatch` and `BossHallFrameworkVerification.ValidateBatch` exit 0, Centre/Room 3 pass |
| Scene scope | Net authored change: Turret_Key Transform and Light (`SceneScope_056.txt`) |

Editor FrameTiming GPU off/on (ms): entry 17.09/17.06, arena 15.22/15.27, gallery 36.16/36.85,
upper 34.41/32.33. These are dual-camera Editor samples, not player FPS.

Final MainScene SHA-256: `fae109bf7cff7f620c51354b50eb78eb27a5434109785fd718951f3a1bca94bb`.

## Agent review and limitations

From the threshold the hierarchy now reads as: lit armoured core above, turret centred in the bright
assembly ring below, galleries and upper decks framing both sides, warm open floor ahead. The live
walk-in shows the same composition, then hall enemies close in.

- The protected upper bridge still hides the core's upper half from the entrance; only its underside is
  lit. From the upper reverse the core reads as a dark silhouette (luma 7.7, unchanged); lighting its
  west face would need a 25th light.
- Emergency-light contribution from the entrance is small (1,445 px at the threshold, 57 just inside);
  the red warnings read on the upper level.
- Through the incline step grating, most of the climb shows the magenta error-shader `Plane (2)` (shell
  finding 6). It is outside the hall and the dressing root; the repair needs a separately scoped shell task.
- The live entry shows pinkish square particles near close-range enemies, likely a particle material
  with an error shader. Not investigated here; env_058 should check it.
- During Play, URP rewrites the teammate's `M_TW_PodGlass.mat` (`_SrcBlend` 5 → 1) and the Editor saves it
  on quit. The probes revert exactly that change in memory; the asset and its builder are unchanged.
- The finale makes the old kill-every-enemy objective optional, so win ordering differs from the plan's
  description. Full progression, human moving combat and player-build profiling remain env_058's.

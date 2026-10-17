# Saved meltdown VFX review — corebuster_env_054, 2026-10-05

Unity 6000.5.7f1 built, saved, reloaded and validated the pass, then produced read-only graphics evidence and a controlled Play-mode probe. The agent compared `target_design/boss_room.png` with the saved and neutral views in `ReviewCaptures/BossHall/054/`: entry, arena, gallery, upper deck, ring, clamp and runway details, the time sequences, the moving Play-mode views and `Runtime/Execution.png`.

## What is saved

`Level/Boss_Hall_Dressing/Meltdown_VFX` holds 11 effects:

- **Ring_Discharge.** One emitter of rare, brief violet bolts (1–2 billboards, 0.08–0.14 s) on the assembly ring's outer field band.
- **Ring arcs.** Four script-free LineRenderer arcs, each bridging a stator spoke through the outer field band:
  - the upper pair clears the turret silhouette from the entry;
  - the lower pair reads from the arena flanks and galleries.
- **Clamp_Steam_South/North.** Soft steam plumes vent from the two upper hydraulic ram clamps beside the ring. They drift outward along the wall and stay below the lintel underside.
- **Runway_Sparks_South/North.** Amber sparks with a white-hot core drip from the overhead feeders' runway conductor boxes above the assembly. They stay above the upper-deck walking volume.
- **Runway_Scorch_South/North.** Soot decals on the outer faces of the two conductor boxes.

## How it reads

The instability sits on the final machinery, not in the fighting space:

- **Ring.** The arcs crackle across the spokes of the contained field. The bolts flash briefly.
- **Ram clamps.** They vent like overheated hydraulics.
- **Runway feeders.** The overloaded feeders shed sparks onto nothing below.

From the entry the effects are small accents on the focal ring (27 changed pixels at the t = 3 s pose), partly behind the turret and bridge. They read clearly from the arena close to the assembly, the galleries, the upper deck and detail views. The machinery remains the dominant mass. Saved lighting is still dark; env_055 owns focal and emergency light and will bind the reserved warning flicker.

## Cue checks

- **Execution cue.** The execution capture keeps the enemy's exposed core and absorption cue fully readable; no effect is near the target.
- **Placement.** Effects never sit in actor envelopes, the turret's head sweep or sight corridor, the walking or firing reservations, or the entry approach.
- **Spark colour.** The sparks are amber (hue 34°), while weapon impacts are red (0°), green (117°) and blue or cyan (199–212°).
- **Spark behaviour.** Sparks loop under gravity at two fixed overhead points. Weapon impacts are one-shot radial bursts wherever a shot lands.
- **Flicker comfort.**
  - Bolt events: at most 0.53 per second.
  - Arc strobe: 0.81–1.20× brightness (`_Flicker` 0.3, gentler than Room 3's 0.38).
  - There is no full-screen flash.

## Iteration notes

The first saved candidate failed visual review:

- **Problem.** Steam leaked from the under-gallery cooling valves, and sparks fell from the east intakes. `Steam.png` peaks at alpha 59/255, so that steam and the soot were nearly invisible. The deck also capped the plume at about 0.6 m, and the east intakes are outside the entry and machinery views.
- **Fix.** The final recipe moves steam to the ring clamps and sparks to the runway conductor boxes. It renders the sparks with the Electric Arc shader tinted amber by vertex colour. Steam and soot use an owned texture: `Steam.png` with only its alpha scaled ×3.4.

The builder replaced each outdated installation inside its own section only. The final audit compares the saved scene with HEAD.

## Boundaries and evidence

Six strict neighbour and backward views are pixel-identical: Room3_Master, Room3_BossDoor, Incline_Back, Landing_Back, Centre_Room and Workshop.

The forward Incline_Reveal pair changes 139 pixels, 123 of them outside the hall projection and none above delta 32. Its unchanged-scene hidden/hidden control changes 1,465 pixels (1,463 outside). This is existing transparent-foreground variation, not a pixel-identical forward proof.

The effects add no shadow caster, light, probe use or scene setting.

- **Reports:** `Meltdown_054.txt`, `MeltdownPreflight_054.txt`, `MeltdownCapture_054.txt`, `MeltdownRuntime_054.txt`, `SceneScope_054.txt`.
- **Logs:** `Logs/env054_build.log`, `env054_capture.log`, `env054_runtime.log` and `env054_validate.log`, all exit 0.
- **Saved MainScene SHA-256:** `4580ae2aa656db743e2cd93e1d2b32a609953e11114b246197ff30ec5cd83ff5`.

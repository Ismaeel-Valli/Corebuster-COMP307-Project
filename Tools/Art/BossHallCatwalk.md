# Boss Hall catwalk maintenance - corebuster_env_051

Completed 2026-10-04. The owner approved six first-tier racks, deferred all upper-tier/stair/rail dressing, and waived the independent gameplay prerequisite for env_051 only. The six racks are saved in MainScene; build, final-source Play-mode regression, saved capture and fresh read-only reload validation exited 0. Evidence is in `Docs/BossHall/*_051.txt`; visual findings and limits are in `Docs/BossHall/Review_051.md`. Later gameplay gates remain open.

## Ownership and recipe

`Assets/FPS/Scripts/Environment/Editor/BossHallCatwalk.cs` owns only `Level/Boss_Hall_Dressing/Catwalk_Dressing` and `Assets/FPS/Art/BossHall/Catwalk_Dressing/Meshes/`. `BossHallCatwalkReview.cs` owns read-only temporary previews and captures. All source and Unity metadata travel with the assets.

The three first-principles meshes use the existing `ProceduralMeshBuilder` block and capped-cylinder primitives:

- Variant 0: a bolted wall rack with two closed service covers, supply conduits and an open cable ladder.
- Variant 1: the same supported rack carrying two parked, flanged hose drums and an open ladder.
- Variant 2: two bolted covers with shallow ventilation ribs, supply conduits and an open ladder.

They have no screen, controls, status lamp, terminal affordance or inherited prefab component. UVs live in the mesh. The only finish is the existing Boss Hall `Gunmetal`; no source material or texture is edited. Three unique meshes contain 2,912 triangles; two instances of each contain 5,824 triangles. Six plain MeshFilter/MeshRenderer objects are saved, with zero prefab instances, collision, carving, lights, particles or audio. Renderers receive shadows and cast none, avoiding the documented shell shadow-leak issue.

## Placement

The plan selects one free mount in each of the three north and three south first-tier wall-service zones in the accepted contract. Each rack is 2.20 m wide, 1.90 m high and at most 0.64 m deep. It starts 0.08 m above the reserved gallery headroom, at Y approximately 13.38, and ends at 15.28. A ray against original collision measures the wall face; the rear mounting feet sit 8 mm inward of it, just inside the existing 6 mm visual liner. Candidate intervals are searched around each zone centre with no copied world coordinates. The planner rejects collisions with existing renderer bounds, occupied service pockets and every ordinary framework reservation.

No deck, landing, stair, bridge, rail gap or bay-door approach is dressed. All second-tier placement is deferred by the recorded owner decision. The accepted scope is six first-tier mounts preserving existing widths, routes and firing positions; the original upper stair/rail defects remain open.

The framework contract is intentionally trimmed and omits deck-width and rail-test arrays. The tools freshly measure the original scene for those fields, caching only under the accepted contract hash. An absent array never counts as a successful firing or width check. The current preflight tests 5,351 entry/turret/deck/rail-station rays against the complete planned rack bounds, with zero new obstruction.

## Safe preparation commands

Close other Unity Editors. Commands use Unity 6000.5.7f1 and an explicit project path.

```text
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod BossHallCatwalk.PreflightBatch -quit -logFile Logs/env051_preflight_final.log
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod BossHallTraverse.RunCatwalkBaselineBatch -logFile Logs/env051_traverse_baseline.log
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod BossHallCatwalk.AssetsBatch -quit -logFile Logs/env051_assets.log
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod BossHallCatwalk.DryRunBatch -quit -logFile Logs/env051_dry_run.log
Unity.exe -batchmode -projectPath <project> -executeMethod BossHallCatwalkReview.PreviewBatch -quit -logFile Logs/env051_preview.log
```

Preflight and baseline traversal do not create dressing assets or save the scene. Run the baseline command only before first placement; a populated scene cannot recreate pre-placement evidence. The baseline variant retains the old env_047 survey/traverse files and writes its full intermediate measurement only under ignored `ReviewCaptures/BossHall/051/`. The immutable relevant records are archived separately in Docs for fresh-clone regression. The asset-only command writes three owned meshes and validates byte-identical regeneration, full unchanged scene state and unchanged shared assets. DryRunBatch verifies a transient isolated section rebuild, exact recipe/material/component validation, five rejection guards and clear/Undo, then discards every test object and reloads the unchanged saved scene. Preview creates and destroys transient meshes, objects, cameras and neutral review lamps; no preview object is saved.

## Scoped authoring and saved verification

The isolated menu is `Tools/CoreBuster/Build Boss Hall First-Tier Maintenance`. It is undoable and unsaved. The guarded saved workflow is `BossHallCatwalk.BuildBatch`; saved read-only validation is `BossHallCatwalk.ValidateBatch`, and saved captures are `BossHallCatwalkReview.CaptureBatch`. These commands apply only to the recorded first-tier scope.

```text
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod BossHallCatwalk.BuildBatch -quit -logFile Logs/env051_build.log
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod BossHallTraverse.RunCatwalkSavedBatch -logFile Logs/env051_traverse_saved.log
Unity.exe -batchmode -projectPath <project> -executeMethod BossHallCatwalkReview.CaptureBatch -quit -logFile Logs/env051_capture.log
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod BossHallCatwalk.ValidateBatch -quit -logFile Logs/env051_reload.log
python Tools/Art/audit_boss_hall_scene.py --before-revision 2fc4052 --root Level/Boss_Hall_Dressing/Catwalk_Dressing --report Docs/BossHall/SceneScope_051.txt
```

BuildBatch generates only the owned meshes, rebuilds only Catwalk_Dressing, validates exact recipes/transforms/finish, exercises headroom/material/collider/light/wrong-owner guards, tests section clear/Undo, saves/reloads, preserves other sections/shared assets and verifies deterministic mesh regeneration. Scene Undo does not undo asset writes. RunCatwalkSavedBatch compares freshly measured complete deck/stair/rail/firing records with the immutable pre-placement `Docs/BossHall/CatwalkBaselineGeometry_051.json`. It enforces unchanged route/stair dispositions, no unreached intended leg, ten humanoid paths, 23 connectors and eleven live agents on the runtime NavMesh. Its reports preserve baseline and env_047 outputs. CaptureBatch compares saved racks enabled/disabled from identical neighbouring views, records state/hash preservation, and uses temporary neutral lamps for hardware inspection.

## Spawn-room merge prerequisite

Before this preparation, the user explicitly requested merge resolution and synchronization. Merge `c73a4ac` retains both parents' disjoint Unity object edits, including the remote spawn room and Player start. The read-only `Tools/Art/audit_unity_scene_merge.py` verifies every parent change/deletion, duplicate IDs, conflict markers and newly dangling scene references. `BossHallBaselineReconciliation.AcceptSpawnRoomMergeBatch` accepts only the exact merged scene/audit/prior-contract/prior-manifest hashes. It changes only protected scene fingerprint/count and matching manifest binding fields; all survey, reservation and skin data, shared assets and MainScene bytes remain unchanged. It is never called automatically by a builder.

Evidence is in `Docs/BossHall/*_051.txt`; the local canonical status, measurements and remaining gates live in `BOSS_HALL.md`, `TESTING.md`, `CONTEXT.md` and `PLAN.md`. This pass does not verify containment, progression/win ordering, live combat, the remote spawn-room gameplay or a player build. Saved lighting stays dark until env_055; neutral capture lamps are discarded.

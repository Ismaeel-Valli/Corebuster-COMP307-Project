# Boss Hall mechanical arms - corebuster_art_006

`BossHallMechanicalArms` owns `Level/Boss_Hall_Dressing/Mechanical_Arms` and `Assets/FPS/Art/BossHall/Mechanical_Arms/`. It creates two assembly cells (`Assembly_Arms_South`, `Assembly_Arms_North`), each carrying two parked arms, bolted gantry hangers, hydraulic housings/pistons, supply hoses and gripper fixtures. The owner waived the separate pre-dressing gameplay prerequisite for this task only on 2026-10-04. Later gameplay and final-verification gates remain open.

## Recipe and provenance

The source language is `Assets/FPS/Art/StartWorkshop/RoboticArm/Prefabs/P_RoboticArm.prefab`, the first-principles 4,320-triangle Workshop arm. The builder reads its visual joint hierarchy and seven mesh assets in place; it creates plain visual objects in a temporary Unity preview scene, retaining the original joint offsets and mesh references. It never runs `RoboticArmBuilder`, overwrites its assets, inherits its materials, imports its colliders or adds gameplay components.

Four arms at uniform 1.55x scale form two separate work cells, rather than one giant arm. Each pair uses two slightly different folded poses: shoulder/elbow/wrist pitches 15/95/50? and 32/74/54?. All are inverted from the gantry and mirror toward the focal axis. The original slewing housings, joint drives, coaxial hubs and rigid-part hose loops remain connected. Unique `Arm_A_`/`Arm_B_` prefixes prevent generated prefab identity churn.

Six first-principles meshes are authored with the existing `ProceduralMeshBuilder`: one hanger, one hydraulic/supply group and one fixture group per cell. The hangers have bolted base plates, a braced drop housing and upper flanges grasping the existing runway's lower flange. A manifold supplies each pedestal through two bowed hoses and end fittings. A housed cylinder and exposed piston rod run along each lower boom with clevis ends. Each parked gripper holds a small tooling mandrel connected to an overhead jig post. These are static assembly fixtures with no interaction, executable core or moving hazard.

The whole composition reuses render_010's `Gunmetal` and `HazardYellow` finishes. Original yellow paint slots become HazardYellow; steel and hoses use Gunmetal. No material, shader or texture is created or modified. Two owned prefabs contain no nested source prefab instances. The scene has two prefab instances, 38 renderers (32 reused source parts, six owned support meshes), 23,296 triangles, and an explicit 36,000-triangle validation ceiling. Assigned hall materials remain within twenty.

## Placement and rebuild

Placement derives from `BossHallBackdrop.Measure(layout)` and the accepted contract. The two reserved bays lie under the west gantry, either side of its central hoist and the west bay-door approach. Arm parts stay within the published art_006 envelopes; only owned supporting meshes extend upward to attach to the runway's lower flange, at ceiling -1.30 m. No part descends into the upper walking/headroom reservation. Everything is unreachable and has no collider or carving obstacle. Original stairs, rails, routes, enemies, turret, navigation and backdrop remain unchanged.

The scoped rebuild uses `BossHallFramework.RebuildSection` with owner `corebuster_art_006`. It reapplies owned materials, validates and supports section Undo. Asset writes precede the scene transaction and are separately owned; scene Undo does not revert generated mesh/prefab writes.

Unity commands (close other Editors):

```text
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod BossHallMechanicalArms.PrepareBatch -quit -logFile Logs/art006_assets.log
Unity.exe -batchmode -projectPath <project> -executeMethod BossHallMechanicalArms.BuildBatch -quit -logFile Logs/art006_build.log
Unity.exe -batchmode -projectPath <project> -executeMethod BossHallBackdropNavigation.RunMechanicalArmsBatch -logFile Logs/art006_navigation.log
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod BossHallMechanicalArms.ValidateBatch -quit -logFile Logs/art006_reload.log
```

BuildBatch requires graphics for boundary/review captures. The Play-mode probe must omit `-quit`; its callback exits after the probe. Editor menus build, validate, capture and run the arm navigation probe under `Tools/CoreBuster`.

## Explicit env_046 reconciliation

Room 3's separately verified drop-bay repairs changed the scene after env_049. `Tools/Art/audit_boss_hall_scene.py` compared commit `ba18420`'s saved scene with approved env_046 SHA-256 `37aa4910a9d2d9cb1f89caae9956b5f1b65899cefad3eab53f334bc6a6e859d7`. Only `NorthWest_Room/Pressure_Systems` and `Lighting/Bay_E5`, `Lighting/Bay_W2` changed. The read-only audit is `Docs/BossHall/Reconciliation_006.txt`.

`BossHallBaselineReconciliation.AcceptEnv046Batch` accepts exactly that scene, audit and prior contract/skin manifest hashes, checks unchanged protected assets, and refreshes only the contract's protected scene fingerprint/count and the skin manifest's corresponding binding fields. It revalidates all original skin bindings, backdrop and neighbouring room frameworks without saving MainScene. This is an explicit one-time action; no builder adopts later revisions. It intentionally rejects reruns once the approved reconciliation has happened.

## Verification and limits

`Docs/BossHall/MechanicalArms_006.txt`, `Navigation_006.txt`, `SceneScope_006.txt`, `Reconciliation_006.txt` and `ReconciliationBindings_006.txt` hold evidence. Review captures are regenerated under `ReviewCaptures/BossHall/006/`: saved lighting, temporary white lamps, detail views from tier 2, both tier-1 galleries, entry/incline and lower-arena views, plus arm-hidden pairs and neighbour diff masks. White review lights and hidden-renderer states are never saved.

Acceptance requires contract keep-outs, source/material ownership, joint/attachment review, sight lines, unchanged outside-section states/shared assets, negative guards, isolated clear/Undo, saved reload, deterministic prefab/mesh bytes, traversal with carving active and visual inspection at the specified viewing distances. These do not establish live combat, containment, win ordering, player builds, final lighting or performance. Final architectural lighting belongs to env_055.

# Robotic arm authoring source

`Assets/FPS/Scripts/Environment/Editor/RoboticArmBuilder.cs` is the reproducible source for the first-principles yellow industrial robot arm (corebuster_art_002). Its geometry comes from `ProceduralMeshBuilder.cs`, the metre-scale primitive generator shared with the Heavy Industrial Kit. In Unity 6000.5.7f1, run `Tools/CoreBuster/Build Robotic Arm`. The batch equivalent, which also writes the review captures, is:

```text
Unity.exe -batchmode -projectPath <project> -executeMethod RoboticArmBuilder.BuildBatch -quit
```

Omit `-nographics` so the captures render. Build the Heavy Industrial Kit first: the arm reuses its finishes and fails with a message naming the kit if they are missing.

The builder deterministically creates the versioned assets in `Assets/FPS/Art/StartWorkshop/RoboticArm/`:

- `Prefabs/P_RoboticArm.prefab`, the posable hierarchy described below, saved in the display pose;
- seven mesh assets in `Meshes/`, one per rigid part (base, turret, lower arm, upper arm, wrist, gripper and a finger shared by both sides). Each mesh is authored in its own joint frame and uses up to three submeshes: worn yellow, dark steel and hose rubber;
- two materials in `Materials/`:
  - `M_RoboticArm_WornYellow` copies the kit's `M_HeavyPipe_HazardYellow` and tiles it 2.5x, because the kit's broad wear pattern reads as camouflage across the arm's flat booms;
  - `M_RoboticArm_HoseRubber` tints the kit's bare-steel grain near-black and non-metallic.

  The dark steel parts use the kit's `M_HeavyPipe_BareSteel` directly. No new textures are needed;
- `RoboticArm_ReviewTest.unity`, which shows three arms in the display, zero and reach poses beside a 1.8 m scale capsule.

## Pivot hierarchy

Unity units are metres. The prefab origin is the floor centre of the base plate, and local +Z is the arm's front (the direction it reaches). In the zero pose every joint rotation is identity and the chain stands straight up (+Y).

```text
P_RoboticArm                      root, floor origin
├─ Base                           static: bevelled plate, 8 plate bolts, flange + 12 bolts, pedestal, junction box, 2 hoses
├─ Collision_Base                 box
└─ J1_Turret      (0, 0.42, 0)    yaw about local Y: slewing ring, housing, rounded shoulder cheeks, drive motors, 2 hoses
   ├─ Collision_Turret            box
   └─ J2_Shoulder (0, 0.55, 0)    pitch about local X: tapered 1.25 m boom, axle hubs, elbow drive, wear strips, 2 hoses
      ├─ Collision_LowerArm       box
      └─ J3_Elbow (0, 1.25, 0)    pitch about local X: motor block, forearm tube, side motor, wrist fork, 2 hoses
         ├─ Collision_UpperArm    box
         └─ J4_Wrist (0, 1.05, 0) pitch about local X: axle hub and wrist body
            └─ J5_WristRoll (0, 0.20, 0)   roll about local Y: bolted tool flange and gripper palm
               ├─ Collision_Gripper        box (covers the wrist, palm and fingers)
               ├─ J6_Finger_L (-0.075, 0.11, 0), yawed 180 degrees
               ├─ J6_Finger_R (0.075, 0.11, 0)
               └─ Tool_Point  (0, 0.30, 0)  tool centre point between the fingertips
```

- Each `Jn` transform is its part's pivot: its mesh is on that GameObject and its collider on a uniquely named `Collision_*` child (unique names keep the prefab's file IDs stable across rebuilds), so rotating the joint moves everything downstream.
- Positive pitch tips the chain toward +Z. Fingers open by sliding along their own local +X (each finger's tip points along local -X, toward the tool axis). Their closed position is ±0.075 m.
- Hoses never cross a joint: each one starts and ends inside the same rigid part and bows outward like a slack service loop, so no pose tears or stretches them.
- Near the elbow, the upper arm stays inside the lower arm's coaxial hub shell, so bending the elbow never shows a seam.
- The prefab ships in the display pose: shoulder 15 degrees, elbow 95, wrist 50, turret and roll 0, fingers 0.02 m open. The lower arm leans forward, the upper arm reaches out and slightly down, and the gripper points at a bench-height target about 1.4 m up and 1.5 m in front. The top of the elbow is about 2.3 m above the floor. `RoboticArmBuilder.ApplyPose` is the reference implementation for posing through the joints.
- The prefab carries no animation, Animator or static flags. `StartWorkshopWorkstation` (corebuster_env_010) places it in `MainScene` at `Level/Start_Workshop/Workstation_010/Robot_Arm_Cell/Robot_Arm`, in this display pose on a 60 mm plinth and yawed so `Tool_Point` is over the workbench, and sets `Level` static flags. An idle servo sweep would rotate J1-J5, so it cannot be static; it is optional scope under corebuster_env_017, which owns the validator's moving-part exemption.

## Validation and budgets

Run `Tools/CoreBuster/Validate Robotic Arm`. It checks:

- the prefab, every joint path and `Tool_Point` exist;
- each joint pivot lies inside the geometry of both parts it connects, so rotating it cannot detach a part;
- the base, turret, lower arm, upper arm and wrist-roll each carry a primitive collider that moves with them;
- every renderer uses textured URP/Lit materials with one material per submesh;
- the review scene exists;
- the prefab stays within the triangle budget.

Triangle budget: 5,000 for the whole prefab, 2.5x the kit's 2,000-triangle per-prefab ceiling, for a single hero prop. Current count: 4,320 in total (base 1,208, turret 996, lower arm 856, upper arm 848, wrist 144, gripper 220, fingers 24 each).

`Tools/CoreBuster/Capture Robotic Arm Review` writes three 1280x720 images to the git-ignored `ReviewCaptures/RoboticArm/`:

- `Arm_10m.png`: the acceptance view, the display-pose arm from exactly 10 m at a 60-degree FOV, three-quarter side-on;
- `Arm_Detail.png`: a 4.2 m view of the joints and hoses;
- `Arm_Poses.png`: all three poses.

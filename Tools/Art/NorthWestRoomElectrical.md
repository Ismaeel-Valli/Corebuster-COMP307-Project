# North-West Room electrical geometry

`corebuster_env_032` generates the transformer and distribution-bank meshes in `Assets/FPS/Art/NorthWestRoom/Electrical_Systems/Meshes/` from `NorthWestRoomElectrical.cs`. The source uses `ProceduralMeshBuilder` blocks and radial cylinders; it does not copy a third-party model. The plinth, oil tank, cabinet, service door, busbars, insulator stems and skirts, and conduits are separate metre-scale assets so their shape and materials can be revised independently. Re-running the builder updates each asset at its existing path and retains its Unity `.meta` identity.

The two banks occupy the surveyed north-west and north-east corner pockets. One Modular Pack battery breaks the east bank's outline, avoiding the Centre Room's paired capacitor/battery arrangement. It is decorative and has disabled source colliders. Each bank has one consolidated collider and a matching carving obstacle.

Two short LineRenderers above the transformer contacts use a Room 3-owned copy of the project's `M_Lightning` material and its custom `ElectricArc.shader`. They have no scripts, colliders, damage or interaction. The subdued cyan is deliberately separate from keycard status colours.

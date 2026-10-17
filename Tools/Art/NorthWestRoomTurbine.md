# North-West Room turbine geometry

`NorthWestRoomTurbine.cs` is the reproducible first-principles source for the Room 3 horizontal machinery. The builder creates the project-owned mesh assets in `Assets/FPS/Art/NorthWestRoom/Turbine_Machinery/Meshes/` and room-owned interim URP/Lit material copies in `Materials/`.

The drum, end bells, drive flanges and conduits are metre-scale 32-sided cylinders along local X. Bearing blocks, base rails, feet and cooling ribs use metre-scale box geometry. Two wrappers combine those meshes with the existing Heavy Industrial Kit flange, reducer, elbow, tee, straight pipe and wall bracket prefabs. Kit colliders are disabled per instance; each wrapper owns one box collision hull and one matching carving obstacle. The source kit meshes and materials are not edited.

Run `Tools/CoreBuster/Build North-West Room Turbines` with a clean saved `MainScene` open alone. The builder measures the current room before placing two assemblies in the south-wall heavy zones. The generated mesh assets and their `.meta` files, material assets and their `.meta` files, and `MainScene.unity` are the deliverables. Keep this note and the builder as provenance.

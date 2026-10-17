# Heavy Industrial Kit authoring source

`Assets/FPS/Scripts/Environment/Editor/HeavyIndustrialKitBuilder.cs` is the reproducible source for the first-principles heavy-pipe and P-3 tank kit. In Unity 6000.5.7f1, run `Tools/CoreBuster/Build Heavy Industrial Kit`. The batch equivalent is:

```text
Unity.exe -batchmode -projectPath <project> -executeMethod HeavyIndustrialKitBuilder.BuildBatch -quit
```

The builder deterministically creates the versioned assets in `Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit/`:

- nine prefabs: 1 m and 2 m straights, 90-degree elbow, tee, reducer, flange, wall bracket, valve-wheel mount, and P-3 tank;
- eleven mesh assets (the tank separates its body, bands/feet, and geometry-built ID label);
- red-oxide paint, yellow hazard paint, and bare-steel URP/Lit materials;
- repeatable 512 px albedo and normal textures for each material;
- `HeavyIndustrialKit_AssemblyTest.unity`, which connects the pipe modules through their ports and presents every asset under neutral/warm workshop lighting.

## Port and scale convention

- Unity units are metres. Main pipes are 0.48 m in diameter; the tank is approximately 1.2 m in diameter and 3 m tall.
- A pipe prefab's root is `Port_A`. Its local +Z axis is the incoming assembly direction.
- `Port_B` is at the outgoing centreline and its local +Z points in the continuation direction. The elbow rotates that direction +90 degrees around Y.
- The tee's `Port_C` is the side outlet and follows the same forward-axis rule.
- To connect a new piece, align its `Port_A` position and rotation to the upstream `Port_B` or `Port_C`. `HeavyIndustrialKit_AssemblyTest.unity` is the canonical example.
- Texture V coordinates advance approximately once per metre along straight and curved pipe centre lines, making the worn finishes tile consistently.

## Validation and budgets

Run `Tools/CoreBuster/Validate Heavy Industrial Kit`. It checks that each prefab exists, every pipe has `Port_A` and `Port_B`, every prefab has primitive colliders, all renderers use URP/Lit, all material texture pairs are assigned, the assembly scene exists, and every prefab stays at or below 2,000 triangles.

Current triangle counts: straights 64 each, elbow 288, tee 320, reducer 64, flange 128, wall bracket 228, valve mount 424, and P-3 tank 1,528.

`Tools/CoreBuster/Capture Heavy Industrial Kit Review` writes a 1280x720 review image to the git-ignored `ReviewCaptures/HeavyIndustrialKit/Assembly.png`.

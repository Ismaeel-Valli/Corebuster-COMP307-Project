# Final polish for submission (2026-10-14)

This pass prepared `MainScene` for the presentation. It covers six areas: map cleanup, room-by-room rendering, corridor lockdown lighting, the escape ending, the loading screen, and the Normal/Ultra graphics options with ray tracing.

## 1. Map cleanup

68 objects outside the played map (X -336..-147, Z -46..153) were deleted from `MainScene`:

- the FPS Microgame's `Level/Room_01`, `Room_02` and `Room_03`;
- `Room_Altar`, the loose cubes, arches, poly shapes, cylinders, walls and stairs around X -90..70;
- two health pickups, the `MainGun`, `Drone`, `Rocket` and `model` props, and one HoverBot;
- the old `Patrol` path, which nothing referenced, and the lava and dust VFX;
- 12 stray `SideRail` pieces of `Overhead_Structure_007` at X = 33.5.

The `Directional Light` stays. It only looked off-map because of its gizmo mesh. No project asset files were deleted.

## 2. Room-by-room rendering (`ZoneStreamer`)

`Gameplay/ZoneStreamer.cs` sits under `===== GENERAL =====/Zone Streamer`.

**Zones.** The level is split into the tutorial wing, the Workshop, the Centre Room, the North-West Room and the Boss Hall, each an XZ area. Everything else is the corridors.

**What is drawn.**

- Only the zone the camera is in, plus any zone behind an open door the camera has a line of sight to, within 70 m. A zone seen this way stays drawn for 1.25 s after the door leaves sight.
- Hidden renderers set `forceRenderingOff`, so their colliders, AI and scripts keep running.
- Plain lights in a hidden zone switch off, which also drops their shadow maps. Lights that carry their own scripts are left alone.
- Geometry within 4 m of a door is always drawn, so door frames never hang in a void.
- Characters, weapons, pickups, doors, terminals, UI and anything spawned later are never hidden.
- The corridor kit (`CorridorRootName`, default `Plane (1)`) always belongs to the corridors, wherever its pieces fall.

**Fixed 2026-10-15: see-through corridor wall.** The corridor along X -248.8, Z 77..91 runs against the Boss Hall's zone edge (X -251.5). Its west-wall windows (`window_big_complete` (1), (2) and (4) in `Corridor_L (1)` and `Corridor_I (5)`, 9 renderers) had their bounds centres inside the Boss Hall's area plus `WallMargin`. They were therefore hidden with the hall, which left a hole looking out to space and the hall's floating enemies. Verified in Play mode with before and after captures from that corridor. Every other corridor-kit renderer was already in the corridors zone.

**Fixed 2026-10-15 (part 2): corridors ending on a room's wall.** Three corridors dead-end directly on a room's outer wall:
- the Workshop's west wall at Z 9;
- the Centre Room's east wall at Z 73;
- the North-West Room's south wall at X -224.8.

Those walls belong to their room's zone, so with the room hidden (its door locked, or out of sight) the corridor opened onto empty ground. `ZoneStreamer.CollectSharedWalls` now marks every room renderer within 1 m of its room's edge that has corridor floor just outside it (45 renderers). These are drawn whenever either the room or the corridors are. Verified in Play mode from all three dead-ends with every room hidden.

**Processing credentials.** Access machines (`KeycardAccessPoint`) and the Master terminal's vault doors pause before opening, with "PROCESSING CREDENTIALS..." or "PROCESSING OVERRIDE...". During the pause (`ZoneStreamer.OpenWhenReady`, at least 1.4 s and 4 frames), the zones on both sides of the doors are drawn behind the closed door. That compiles their shaders and uploads their textures before they are revealed. The reader's lamp pulses in the card's colour meanwhile.

**Load warm-up.** While the loading screen is up, each zone is rendered once from its centre (four directions at 320x180), with only that zone drawn.

**Measured.** Editor Play mode, Ryzen 7 5700G iGPU, at the gun locker looking slightly left (the reported drop):

| | Frame time | Triangles | SetPass calls | Shadow casters |
|---|---|---|---|---|
| Streaming off | 20.0 ms | 1.59 M | 1,108 | 598 |
| Streaming on | 12.3 ms | 0.51 M | 411 | 367 |

`ZoneStreamer.Streaming` turns streaming off for comparisons.

## 3. Corridor lockdown lighting (`Tools/CoreBuster/Build Corridor Lockdown`)

**Why the corridors were bright.** The corridor kit (`Plane (1)`, 1,358 renderers) used URP's `2D/Mesh2D-Lit-Default` shader. The 3D renderer draws that full-bright and unlit, so the corridors ignored every light.

`Environment/Editor/CorridorLockdownBuilder.cs` fixes this. It can be re-run, and it rebuilds from scratch each time.

**Materials.**

- The five pack materials are copied to `Assets/FPS/Art/Corridors/Lockdown/`, converted to URP/Lit through `WorkshopMaterialUpgrade.Upgrade`, and assigned to the corridor renderers only. The pack materials themselves are untouched.
- Each copy carries `T_Lockdown_Occlusion`, a uniform occlusion map at 15%. In URP/Lit, occlusion scales only indirect light (ambient and reflections), so the corridors lose the scene's bright flat ambient while lamp light reaches them in full. Scene-wide lighting is unchanged.
- The white light panels are dark grey with a faint residual glow.
- The octagonal window panes look out onto space (2026-10-15, below). Plain see-through glass would show the ground plane and the rooms' outer shells, so they are not see-through.
- **Fixed 2026-10-15: lit surfaces riddled with dots.** The Sci-Fi Styled Modular Pack saves `Textures/base-color-normal.png` as its normal map. That texture is a grayscale field of random white specks, not a normal map. The pack's 2D shader never read it, but on the URP/Lit copies each speck tilted the normal and glinted in lamp light. `WorkshopMaterialUpgrade.DropSpeckleNormalMap` now clears it during the upgrade, and it was cleared from the 9 copies that had it:
  - `M_Lockdown_Base`;
  - the Workshop and tutorial wing palettes;
  - the Centre Room and Boss Hall floor copies.

  Verified with before and after Play-mode captures of a red-lit corridor and the spawn shelves.

**Routes.** NavMesh paths run between doors in progression order:

| Route | Card | Lamp colour |
|---|---|---|
| Tutorial exit to the Workshop | black | silver (the card's tint) |
| Workshop to the Centre Room | blue | blue |
| Centre Room (N or W door) to the North-West Room | yellow | yellow |

Lamps within 3.6 m of a route take its colour. The earlier route wins where routes share a corridor. Stretches of a route longer than 9 m with no lamp get new lamps on the nearest wall (11 were added, under `===== CORRIDOR LOCKDOWN =====`).

**Beacons.** Each lamp gets a `Game/CorridorBeacon`:

- The globe and bulb shimmer in the route colour. Off-route lamps only glow faintly.
- The light is a separate, mostly grey source with only 28% of the card colour mixed in. It sits 0.7 m out from the wall, is shadowless, and reaches 7.5 m on a route or 6 m off it. The tint reads as a subtle shift, not a wash.

**The caged lamp (2026-10-15).** The corridors' lamps are the Workshop's first-principles caged lamp (`Lamp_South_1` and its siblings).

- `StartWorkshopLighting.SaveFixturePrefab` saves the Workshop's fixture, with its pool and wash spots, as `Assets/FPS/Art/Corridors/CageLamp/P_CageLamp_Wall.prefab`. The plate, arm, caps and cage are merged into one two-material body (`SM_P_CageLamp_Wall_Fixture`), so each lamp is two draws instead of eleven with the same geometry. The globe stays separate because the beacon colours it.
- One lamp stands on every pillar between the window bays, on both sides of every corridor: 216 lamps.
  - Each pillar face is found by casting at the window pieces' own meshes, given temporary colliders on layer 31, since the kit's colliders are boxes. The face is flat and upright from 1 m to 3 m, and the lamp sits at 2.6 m.
  - Of 230 faces, 14 were skipped because they were inside or facing a room, or blocked.
  - The builder owns every corridor lamp: earlier ones are removed on each run, and the gap lamps are gone.
- Each lamp lights the corridor with the Workshop lamp's pool spot, in the route colours or emergency red.
  - The pool is a 120° cone tilted 45° down with an 8 m range. It burns at 30 on a route and 18 off it (the Workshop's is 60), because there is a lamp every 4 m on both sides.
  - The wall-wash spot is switched off in the corridors, because it threw a hot circle onto the wall behind each lamp.
- With 216 lamps, `ZoneStreamer.CorridorLightReach` (40 m) keeps the lights in view under URP's limit of 256. A sweep found at most 86, with no limit warning in loading or play.

**Iron room shells and access pads (2026-10-15).**

- **Iron shells.** Seen from the corridors, the rooms' shells read as pale "concrete": the SciFi Warehouse Kit's walls, door frames and vents in the Centre Room, the North-West Room, the corridor kit and the Workshop's four kept boundary pieces. They still used the pack's URP 2D materials, which draw unlit. The tutorial wing's walls are the same kit on lit copies, which reads as dark iron.
  - `CorridorLockdownBuilder.IronShells` (part of Build, and alone as **Tools/CoreBuster/Apply Iron Room Shells**) moves those renderers onto the tutorial wing's lit `Walls` and `Structure Mat`. The vents' fan and guard get lit copies tinted to the walls' steel (`M_Lockdown_VentFan`, `M_Lockdown_VentGuard`, regenerated from the pack each run).
  - 70 renderers changed. The Boss Hall's shell is left to its own rendering pass.
  - A/B captures inside the Centre Room, North-West Room and Workshop show their interiors unchanged, except one North-West Room wall face that had still shown the pale concrete and now matches.
- **Access pads.** The blue and yellow keycard pads sat on the corridor's side walls, which are now window bays.
  - `KeycardProgressionBuilder.DoorWallMount` puts a pad on its gate's own wall, beside the gate on the corridor side. The gates are off-centre, so it uses the wider side: just clear of the frame, or centred in the gap when that is narrow.
  - New progressions are built this way, and **Tools/CoreBuster/Remount Access Machines** moved the four existing pads.
  - The remount also covers the tutorial wing's Workshop pad (`Access Machine (BLACK KEYCARD) - Workshop`), which Build Tutorial Wing places with the same `BuildAccessPoint`. It had stood on the corridor's window opposite the Spawn Room Door (N), seeming to float. It is now on the door's own wall, to the right of the door as you face it from the corridor (the wider side). The tutorial exit's pad is placed by hand on its own wall and is left alone.
- **Dead-end corridors.** Two corridor stubs that join no route ended in leftover kit boxes, each wider than the corridor and floored with the pack's unlit, concrete-pale tiles.
  - The west stub (`Corridor_I (1)`, x ≈ -249, z ≈ 61) also ended in a glass panel. Through it you could see the box's open far side, onto the sky.
  - The north stub (`Corridor_I (48)`, x ≈ -181, z ≈ 129) opened straight into its box.
  - Each now ends in an iron wall at the end of the last window bay, in `===== CORRIDORS KIT =====`.
    - The west cap is a vent wall, `Fans/Wall Fan End Cap`: the kit's Wall Fan in the iron `Walls` material with the steel-tinted vent. It is 6.6 m wide, like the other caps, and sunk 1.35 m so the vent sits mid-corridor, about 0.1 to 3.7 m up.
    - The north cap is `Walls/Wall Plain (62) End Cap`, the box's own back wall.
  - The glass panel, the other box walls, and four floor tiles were deleted. Three of the tiles formed the west box's floor, the fourth was a stray hidden below the corridor at (-255, 10).
  - **Tools/CoreBuster/Trim Corridor End Caps** (`CorridorLockdownBuilder.TrimCorridorEndCaps`) cuts every `... End Cap` piece of the corridors kit down to the corridor's height band, -0.05 to 4 m. The kit's 10 m room walls therefore no longer stand out above the roof or below the floor.
    - Each mesh is cut from the kit's original, clipping triangles against both planes. UVs are interpolated, so the texture is not stretched.
    - The cut meshes are saved to `Assets/FPS/Art/Corridors/Lockdown/EndCaps/` and used by the caps' colliders too. Re-running regenerates them in place.

**Sky windows (2026-10-15).** The octagonal panes (`M_Lockdown_SkyWindow`, new HLSL shader `CoreBuster/Sky Window`) draw the nebula sky along each pixel's view ray, as if they opened onto space.

- The skybox's own function now lives in `Assets/FPS/Art/Shaders/NebulaSky.hlsl`, shared by the skybox and the windows, so their clouds, core glow, drift and twinkling stars match. The refactored skybox renders pixel-identical to the original (A/B with drift frozen).
- Over the sky the pane adds a faint tint, a fresnel rim of reflected corridor light, and procedural dust and scratches. It takes no light specular, so lamps no longer leave glare dots on the glass.
- So the pane reads as glass in front of space rather than an opening, it also draws three things (all tunable on `M_Lockdown_SkyWindow`; the defaults are in the shader):
  - A faint blue-grey haze, `_Haze` 0.09. It thickens at grazing angles.
  - Soft diagonal glare streaks, `_Sheen` 0.14, one broad and one thin, every 2.6 m. They are pinned partly to the camera (`_SheenParallax` 0.45), so they slide across the panes as you walk past.
  - Faint smudges that break the streaks up.
- The builder copies the sky settings from the scene's skybox material, so re-run Build Corridor Lockdown after changing the sky. The old dark-glass copy, `M_Lockdown_Glass`, was retired.
- Cost: corridor frames went up by about 1.2 to 1.5 ms in the editor (to 10.5 to 14.7 ms), mostly the sky shader filling large panes.

**Black hole (2026-10-15).** The sky now holds a black hole with an accretion disk, at direction (-0.55, 0.3, 0.78): north-west, 17° above the horizon, framed by the north and west corridor windows. It is drawn by `BlackHoleSkyColor` in `NebulaSky.hlsl`, so the skybox and the sky windows both show it, with no textures.

- **The hole.** It is modelled one unit from the camera, so angles and distances there are interchangeable. Rays passing inside the capture radius (`_BlackHoleSize`, 0.085 rad) fall in, which makes the black shadow, and a thin photon ring rims it.
- **Lensing.** Rays are bent towards the hole by 2rs/b, steepening sharply near the capture radius, and faded out by 1.5 disk radii so the sky beyond is untouched. The background sky takes a gentler bend away from the shadow: at full strength the nebula smeared into a broad band of rings (an Einstein ring).
- **The disk.** It spans 1.25 to 5 shadow radii, tipped 9° towards the camera and rolled -12°.
  - The straight ray draws its near side, in front of the hole. The bent ray draws its far side, which arcs over and under the shadow.
  - Its colour runs from hot white at the inner edge to orange-red outside. Its streaks shear Keplerian-fashion, the inner gas lapping the outer.
  - Doppler beaming makes the side turning towards the camera brighter and bluer. A soft knee above 1 keeps that side from burning to flat white, so its streaks stay visible.
- **Live disk.** The disk visibly turns: the inner edge laps in about 6 s (`_DiskSpin` 1.1 rad/s, previously 0.5).
  - Shear that runs forever winds the streaks past the pixel into static, so the gas is two generations. Each lives 16 s and is seeded afresh, and they are cross-faded half a period apart. The blend is renormalised so the streaks keep their contrast.
  - Four hot spots flare near the inner edge, ride their orbit for 7 s while the shear stretches them into arcs, and fade. Each then respawns at a new radius and angle, and beaming makes each one brighten as it swings towards the camera.
  - Radial bands of the disk brighten and dim with a slow turbulent flicker.
  - Verified in Play mode with frame sequences 0.75 to 1 s apart, rendered through the game camera's post-processing.
- **Settings.** All of them are on the skybox material (`NebulaSkybox`), under *Black Hole*. Build Corridor Lockdown copies them to the window material, so re-run it after a change.
- **Cost.** Only pixels within reach of the hole do the extra work. Everything else is the plain sky.

## 4. Escape ending is now a win

In `FinaleDirector`, surviving the lockdown's 120 s (`SurviveDuration`) now launches the escape pod. A white flash follows, then the win screen with `EscapeWinLine`. Dying during the lockdown is unchanged: the core meltdown, then the lose screen.

`FinaleOverlay` builds a countdown plate at the top centre of the screen at runtime: "ESCAPE POD PRIMING" over the remaining time. It turns red and pulses in the last 15 s.

## 5. Loading screen

`UI/Menus/LoadingScreen.cs` is used by `SceneFader` for the scenes in `LoadingScreenScenes` (default: `MainScene`). That covers Play from the main menu and Play Again on the end screens.

- It survives the scene change and loads the scene asynchronously behind a progress bar, status line and spinner.
- After activation, it stays up through 8 warm-up frames and until `Game/LoadingGate` has no holders. `ZoneStreamer` and the ray-tracing build both hold it. The time limit is 20 s.
- Verified 2026-10-14: the screen came up 0.4 s after Play and lifted 16.9 s later in the editor, with the zones ready.

## 6. Graphics: Normal and Ultra, with ray tracing

`Tools/CoreBuster/Setup Render Quality (Normal + Ultra)` (`Rendering/Editor/RenderQualitySetup.cs`) sets up both levels. It can be re-run.

| | Normal (default) | Ultra |
|---|---|---|
| Pipeline asset | `Default_PipelineAsset`, unchanged | `Ultra_PipelineAsset` |
| MSAA | as before | 4x |
| Sun shadows | as before | 2048, two cascades, 70 m |
| Spot-shadow atlas | as before | 4096 |
| Soft shadows | as before | high |
| Renderers | default (SSAO) | `Ultra_ForwardRenderer` (SSAO) and `Ultra_RayTracedRenderer` (ray-traced AO) |
| Ray tracing | off | on by default |

- Both levels keep the 4 per-object light limit, so the rooms light as designed.
- Options lists Quality as Normal/Ultra, plus a Ray Tracing On/Off row built at runtime under Quality. Choosing Ultra turns ray tracing on and Normal turns it off; either can then be toggled. The settings are saved in PlayerPrefs.

**Ray tracing (`fps.Rendering`).**

- `RayTracingWorld` builds a BVH of the level's static geometry (4,199 mesh instances in `MainScene`) with Unity's Unified Ray Tracing API:
  - hardware DXR on a DX12 GPU with ray-tracing support;
  - otherwise a compute-shader BVH traversal that runs on any GPU.
- Characters, doors, weapons, the giant core, animated and physics objects, and transparent surfaces are left out.
- Cameras use the ray-traced renderer only once the BVH is ready.
- `RayTracedAmbientOcclusion` is a URP render-graph feature, using the custom shaders in `Assets/FPS/Art/Shaders/CoreBuster/RayTracing/`:
  - `RayTracedAO.urtshader` (HLSL, imported as both a compute and a ray-tracing shader) traces 2 cosine-distributed rays per pixel, at half resolution, 1.6 m long, against the real geometry.
  - `RayTracedAODenoise.compute` reprojects and accumulates the result over frames, then upsamples it with a depth-aware filter.
  - The result is published as URP's `_ScreenSpaceOcclusionTexture`, so every URP Lit material applies it.

**Verified on D3D11 (compute backend):**

- An asymmetric capture confirmed the depth-to-world reconstruction's orientation. The rejected orientation mirrored the wall's occlusion onto open floor.
- Contact occlusion appears in the lockers and under the desk and wall bases.

**Limits.**

- The compute path is slow on integrated GPUs, which is why Normal keeps it off.
- The hardware path is untested here, since no available machine has hardware ray tracing. The project's Windows graphics API list is `Direct3D11, Direct3D12`, so a build runs DX11 (compute backend) unless launched with `-force-d3d12`.
- The compute backend needs vertex and index buffers with the `Raw` target. In builds, a mesh that cannot provide them is skipped, with a warning, rather than failing the build. No build has been tested yet.

## 7. Boss Hall: reactor, consoles and the core fight (corebuster_game_002)

`Tools/CoreBuster/Build Finale` (`UI/Editor/FinaleBuilder.cs`) now also builds:

- **An opening under the core.** Each armour layer stops short of the bottom pole, so its lowest band is gone, and the old base fitting is replaced by an intake ring. From below, the glowing heart shows through the opening and nowhere else.
- **The reactor.** A copy of the Centre Room's containment reactor sits on the hall floor directly under the core and carves the NavMesh. It fires a beam up into the opening. The beam is two open tubes drawn with the new HLSL shader `CoreBuster/Reactor Beam` (`ReactorBeam.cs`): a white-hot centre line, warped-noise filaments and climbing pulses. Its `_Overcharge` input thickens, speeds up and whitens the beam.
- **Three consoles** at 120° on an 8.5 m ring around the reactor (`ReactorConsole.cs`), made from first principles: a plinth, a body block with a raised gun port, a status screen and lamps, and two electrode posts.
  - Each console has a heavy cable into the reactor's base, drawn with the new lit HLSL shader `CoreBuster/Power Cable` (`PowerCable.cs`). Its conductor lines drift with idle energy and brighten when the console is ready.
  - When a console is overcharged, a bright slug of energy (`_Flow`) runs down the cable into the reactor.
- **The turret** moves from under the core to the west end, at (-311.5, 115.3).

**The fight.** Shooting the armour no longer breaks it: `GiantCore.ShotsBreakArmour` is off, so shots only ring off. Each layer is removed this way:

1. Execute three enemies to fill the Disassembler (3/3 cores).
2. Use a console (E). The prompt says what is missing.
3. The execution plays in reverse (`CoreExecutioner.TryInject`): the player steps in, winds up, and jabs the gun into the port. The three cores then run down the barrel into the console one at a time, each with a shove, and the Disassembler drains to 0/3.
4. The console overloads for 5 s and shows "CORE OVERLOAD DETECTED - CLEAR THE CONSOLE". It arcs with electricity (the Disassembler's electrocution effect) and, after 0.8 s of warning, deals 22 HP/s within 3.2 m.
5. The surge travels down the cable (1.6 s). The beam overcharges (0.45 s rise, 2.2 s hold, 1.2 s fall), and 0.5 s into the peak the whole outermost layer is blown off the core (`GiantCore.BlowOffActiveLayer`, pieces thrown 3.2x harder).
6. One console works per layer, and only one runs at a time. After the third, the core is exposed and the final bust from the platform is unchanged.

The objective reads "Overcharge a reactor console to blow off the core's <layer>", with a counter out of 3.

Also fixed: the fight now starts when the player's chest, not their feet, is inside the hall. The hall bounds' floor sat exactly at the floor's height, so a player on the floor could fail the check.

`FinaleDirector.BeginForTesting` (editor and development builds only) skips the keycards and the choice, for tests.

**Verified 2026-10-14 in Play mode.** A scripted run filled the Disassembler and injected all three consoles in turn:

- each injection drained 3/3 to 0/3;
- each overload led to a surge, an overcharged beam and one layer blown off (layers 0 to 3);
- the core then reported exposed;
- the overload damage measured about 22 HP/s at the console.

Captures show the gun in the port, the arcing console, the white overcharged beam with steel plates flying, and the bare heart above the ring.

## 8. Boss Hall floor and the pink flash (corebuster_env_058)

`Tools/CoreBuster/Build Boss Hall Floor` (`Environment/Editor/BossFloorBuilder.cs`) runs after Build Finale and rebuilds `===== BOSS HALL FLOOR =====` each time.

**The pink flash.** The hall's collision floor `Plane (3)` (71 x 67 m at y 6.0) and the entry incline's `Plane (2)` still used ProBuilder's default material. Its shader does not exist in URP, so they drew pink wherever they showed through gaps in the floor tiles, notably from the stairs. They now use `M_BossFloor_Subfloor`, and their colliders are untouched.

**Cover.** 24 clusters copied from the Centre and North-West rooms' own dressing:

- low barriers and cover blocks;
- generators, capacitors, rotor cradles, battery and container units, crate, pallet and barrel stacks;
- 3.4 m machinery bays and 6 m pressure tanks.

They break the long sightlines across the floor while keeping clear of:

- the 12 m reactor ring;
- the east entry lane;
- the turret, the starting enemies and the consoles;
- the low galleries.

Each cluster keeps its colliders, carves the NavMesh (its own obstacle, or one added) and loses its room scripts. Its materials are copied to `Assets/FPS/Art/BossFloor/Materials/`, so the North-West Room's materials stay in that room. Starting enemies standing inside the reactor ring are moved to its edge.

**Ownership note.** These additions live under `===== FINALE =====` and `===== BOSS HALL FLOOR =====`, not under `Level/Boss_Hall_Dressing`. They change the saved scene the Boss Hall framework fingerprints, and move the turret it measured, so the framework's builders will report a changed baseline until it is reconciled.

## 9. Floor guide arrows (corebuster_game_003)

`Tools/CoreBuster/Build Floor Guide` (`Environment/Editor/FloorGuideBuilder.cs`) lays glowing chevrons on the floor along each leg of the progression, in the colour of that leg's keycard. `FloorGuide.cs` lights only the current leg; it fades in when the leg starts and out when its goal is reached.

| Route | Colour | Shown when | Hidden when |
|---|---|---|---|
| Tutorial exit to the Workshop | silver (black card) | the tutorial exit door has opened | the Workshop door opens |
| Workshop terminal to the Centre Room | blue | the player holds the blue card | a Centre Room door opens |
| Centre Room terminal to the North-West Room | yellow | the player holds the yellow card | the North-West Room door opens |
| North-West terminal to the Master terminal | red | the player holds the red card | the Master terminal unlocks |
| Master terminal into the Boss Hall | orange | the player chose to fight | the core fight starts |

**How the routes are built.** The rooms are separate NavMesh islands, and the NavMesh also covers the ground outside the ship, so a plain shortest path can leave the building.

- **Corridor legs** follow the corridor modules themselves: Dijkstra over touching modules, running from one shared edge to the next.
- **In-room legs** use the NavMesh, and the two are joined through the doorway.
- **Smoothing.** Paths are pushed toward the middle of the floor and smoothed.
- **Floor height.** Arrows sit on the visible floor, the higher of the collision floor and the top of the floor meshes, because some tiles have no collider.

**The shader.** `CoreBuster/Floor Guide` is a new HLSL shader: additive, with soft-edged strokes, pulses flowing toward the goal and a faint flicker. The arrows are always drawn (`ZoneStreamer` skips them), as a route crosses zones.

**Editor fix.** `EditorUtility.CopySerialized` onto an existing mesh asset left the editor drawing the old shape until it restarted, while builds were unaffected. This is why the reactor console's port looked buried earlier. `FloorGuideBuilder` and `FinaleBuilder` now rewrite mesh assets in place (`Overwrite`).

**Verified 2026-10-14.**

- Top-down and in-corridor captures show the arrows only inside corridors and rooms, on the visible floor.
- In Play mode, all routes are hidden at the start, and "To the Centre Room" faded in about 2.5 s after the blue card was added.

## 10. Scene hierarchy (2026-10-15)

`MainScene` had 984 top-level objects, mostly loose kit pieces. `Tools/CoreBuster/Organize Main Scene Hierarchy` (`Environment/Editor/SceneHierarchyOrganizer.cs`) files them into folders. It leaves 17 top-level objects, in this order:

| Top level | What it holds |
|---|---|
| `===== GENERAL =====` | GameManager, EventSystem, Directional Light, NavMeshSurface, ObjectiveKillEnemies, Zone Streamer |
| `===== PLAYER =====` | Player |
| `Plane (1)` | *builder root*: ground plane and corridor kit |
| `===== CORRIDORS KIT =====` | Walls, Fans, Floors (the corridor lamps belong to `===== CORRIDOR LOCKDOWN =====`) |
| `===== CORRIDOR LOCKDOWN =====`, `===== FLOOR GUIDE =====`, `===== KEYCARD PROGRESSION =====` | *builder roots* |
| `===== TUTORIAL WING =====` | *builder root* |
| `===== TUTORIAL WING KIT =====` | Doorways, Walls, Floors, Ceilings |
| `Level` | *builder root*: Start_Workshop, Centre_Room, NorthWest_Room, Boss_Hall_Dressing |
| `===== WORKSHOP KIT =====` | Doorways, Walls, Floors, Props |
| `===== CENTRE ROOM KIT =====`, `===== NORTH-WEST ROOM KIT =====` | Doorways, Walls, Floors |
| `===== BOSS HALL KIT =====` | Doorways, Walls, Fans, Floors, Ceilings, Catwalks & Stairs |
| `===== BOSS HALL FLOOR =====`, `===== FINALE =====` | *builder roots* |
| `===== ENEMIES =====` | Boss Hall, Centre Room, Workshop (placed enemies by room) |

**How pieces are filed.**

- A piece's room comes from `ZoneStreamer.Rooms`, widened by 1 m so walls on the edge count. Anything outside the rooms belongs to the corridors.
- Its type comes from the start of its name.
- Folders are empty objects at the origin carrying `Game/HierarchyFolder`, so no world transform changes. The tool checks this and reported 0 drifted transforms.
- The empty template separators `===== LEVEL/PATHS/PICKUPS =====` were removed.
- The tool can be rerun after a builder or teammate adds loose pieces. A second run filed nothing.

**Builder roots stay at the top level.** Their builders look them up there by name and rebuild them, so they are only reordered next to their room.

**Tools updated to look through folders.**

- `HierarchyFolder.PieceRoot(t)` replaces `t.root`, and `HierarchyFolder.TopLevel(scene)` replaces root scans:
  - `TutorialWingBuilder` (sky openings and lit copies);
  - `BossHallSurvey`, `BossHallRendering`, `BossHallRenderingReview` (kit pieces classified by name).
- `StartWorkshopFrame.Named` and `StartWorkshopSignage` stop their name-token walk at folders, so a folder called "Walls" does not make its pieces read as walls.
- Review snapshots that hash hierarchy paths (`BossHallSnapshot` and the room snapshots) will report the new layout once as a change.

**Verified.** In Play mode before and after, `ZoneStreamer` collected the same 8,263 renderers, with identical per-zone renderer, light and door counts. There were 18 actors and 2 objectives, the tutorial started, and no errors were logged.

## 11. Ship map in the officer quarters (2026-10-15)

On the quarters' north wall, left of the sidearm rack's shelves, a wall screen shows a top-down schematic of the whole level. It sits where the player wakes, so they see the layout first.

- **Building it.** `TutorialWingShipMap.cs` adds it to Build Tutorial Wing, which draws it last so the survey sees the finished wing. **Tools/CoreBuster/Rebuild Ship Map** redraws only the map after the level changes.
- **The floor plan is surveyed, not drawn by hand.** The builder probes the physics scene on a half-metre grid over the level.
  - A cell is floor if it has a floor below it and nothing 2 m or taller at body height.
  - The ground plane counts only inside the rooms, whose kit tiles have no colliders.
  - Doors count as open.
- **The texture.** `T_TW_ShipMap.png` is 1600 × 1200, drawn in code: rooms in their own colours, corridors in steel, a bright edge where floor meets wall, and a 4 m grid.
  - Each door is a bar in its keycard's colour. The Boss Hall door is red, because the master terminal takes all three cards.
  - Each security terminal is a diamond in the colour of the card it gives.
  - An amber ring marks where the player stands.
- **The text.** Labels are world text over the screen: the title, the room names, the map key and the six-step route. The screen material (`M_TW_ShipMap`) is emissive and matte, so the hanging light leaves no glare on it.

## 12. Rocket launcher, the armour buster (corebuster_game_004, 2026-10-15)

A second weapon, unlocked in the Boss Hall and built by **Tools/CoreBuster/Build Rocket Launcher** (`RocketLauncherBuilder`).

- **Where it is.** It hovers over the Final Assembly cradle's deck, between two emitter posts, held by two jittering electric beams: one to its barrel and one to its stock.
- **How to unlock it.** It works exactly like a reactor console overload.
  - The power panel on the cradle's front face, left of the deck, takes a full Disassembler (three cores) injected through the gun port.
  - While the panel overloads (3.5 s), the beams surge, and standing at the panel hurts.
  - When the panel is spent, the beams stutter out and the launcher drops onto the deck. Hold Interact on it to take it, as with the sidearm.
- **The weapon.** It goes into the next free slot: slot 2, after the blaster.
  - It is styled and sized to match the blaster: rust-red plating, a black muzzle shroud and venturi, dark ribbed grilles, glowing yellow hoops and studs, and a length of about 0.95 m. It is held where the blaster is.
  - Each rocket is built from the Elite Rifleman's projectile (its steel bolt, hot tip and trail), larger, with a smoke wake and motor flame.
- **One rocket every 15 s.** The launcher holds a single rocket, which the weapon's own automatic reload brings back over 15 s.
  - Each shot puts the launcher away and draws the main gun again (`LauncherCooldown`, after 0.55 s).
  - A `ROCKET LAUNCHER` gauge appears above the Disassembler's once the launcher is carried. It is a copy of the Disassembler's gauge in the `GameHUD` prefab, driven by `RocketLauncherChargeHUD`.
  - The gauge fills orange with the seconds left. When the rocket is back it flares, plays the Disassembler's charged sound, shows "ROCKET LAUNCHER READY - PRESS 2" and flashes yellow with `READY [2]` until fired.
- **The blast.** It deals no damage. `ArmourBreakBlast` tears the armour off at most 3 armoured enemies within 5.5 m that the blast can reach (not through walls), nearest first. The exposed body and core are then finished with the main gun or a core execution.
  - The Boss Hall's giant core is unaffected.
  - The explosion layers fire, sparks, embers and smoke over the template's `VFX_Explosion`: about 100 particles and a light that is off within 0.3 s. It adds a short view shake (`BlastShake`).
- **Switching.** Scroll, or press 2 / 1. `PlayerWeaponsManager` now tips the weapon down and rolls it as it is put away, and back as the next weapon is drawn (`WeaponSwitchTilt`).
- **Shared interface.** `IArmourBreakable` (in `fps.Game`) is implemented by `ArmouredEnemy`, so gameplay code can break armour without depending on the AI assembly.
- **Verified in Play mode.**
  - The full Disassembler injected and overloaded the panel, spending its charge (3 to 0).
  - The cradle released the launcher, and taking it put it in slot 2 and drew it.
  - One rocket at an armoured elite left it at full health (280/280) with its armour broken.
  - Switching back to the blaster showed the tilt.
  - After the restyle, one rocket into a cluster of five armoured soldiers broke exactly 3 of them, and the blaster came back up on its own.
  - A shot attempted mid-cooldown did not fire.
  - The gauge counted down and showed `READY [2]` with the ready message about 15 s after the shot.

## 13. Slow walk and room-skip hotkeys (2026-10-15)

- **Slow walk (V).** V toggles a slow walk at 40% of ground speed (`PlayerCharacterController.WalkSpeedRatio`); sprinting ends it.
  - It is the new input action `Player/Walk`, bound to V in `InputSystem_Actions`.
  - Play-mode check: 13.0 m/s normal, 5.2 m/s walking, and a second V returned to normal.
- **Room skips (`RoomSkipHotkeys`, on the Player prefab).** Each hotkey jumps to a keycard room. The player lands just inside its entrance holding every card earned before that room, with every door those cards open already open. The room's zone is drawn for a few frames first, so it arrives loaded.

  | Hotkey | Room | Cards on arrival |
  | --- | --- | --- |
  | Ctrl+P (blue) | Workshop | black |
  | Ctrl+I (yellow) | Centre Room | black, blue |
  | Ctrl+O (red) | North-West Room | black, blue, yellow |

  - A skip also ends the tutorial if it is still running (`TutorialDirector.Skip`). It hands over the sidearm, unseals the quarters and the wing's exit, and clears the tutorial card.
  - Skips are refused during a Core Bust, while using a panel, during the finale, while paused or when dead.
  - Ctrl+P no longer also opens the pause menu: `InGameMenuManager` ignores P while Ctrl is held.
  - Play-mode check (simulated key presses): each skip reached its room, visible, with the right cards. The keycard objective and tray updated, and the tutorial ended with the blaster drawn.
  - **In the Unity editor, Ctrl+P is also Unity's own Play shortcut.** Pressed for real in the Game view, it stops Play mode. Test that skip in a build, or rebind Unity's Play shortcut under *Edit > Shortcuts*. Ctrl+I and Ctrl+O are unaffected.

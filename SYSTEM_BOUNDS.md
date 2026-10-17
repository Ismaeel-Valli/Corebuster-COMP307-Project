# CoreBuster System Bounds

## Purpose

This file owns the invariants that make the configured build, Unity asset production path, and Core Buster execution mechanic coherent. Update it with focused verification when intentionally changing a bound.

## Build and scene bounds

1. `ProjectSettings/ProjectVersion.txt` pins the project to Unity `6000.5.7f1`.
2. `ProjectSettings/EditorBuildSettings.asset` is the source of truth for configured build scenes: `IntroMenu`, `WinScene`, `LoseScene`, then `MainScene`.
3. `GameFlowManager` defaults to the scene names `WinScene` and `LoseScene`; a scene rename or removal requires updating serialized references, the build list, and Play-mode validation together.

## Boss Hall lighting boundary (corebuster_env_055)

- The owner delegated the lighting tradeoff and conclusion after the initial overlap report;
  the later instruction supersedes the temporary strict-candidate hold. Keep the existing
  four-slot forward pipeline and gameplay lights. Excess light candidates in crowded combat
  are an explicit composition tradeoff, not a claim that the shader renders more than four.
  The encounter-prerequisite waiver applies only to env_055. Ambient, skybox, fog, navigation,
  shared assets and other rooms remain protected.
- Authoring belongs only to `Boss_Hall_Dressing/Lighting`. Exact light-only recipe objects may
  occupy firing reservations because they have no renderer or collider. No geometry or descendants
  inherit that exception. Only the reserved ring/feeder lamps may use the existing scaled-time
  `ReactorInstabilityLight`/`WorkshopAmbientMotion` bindings, validated against their exact recipes.
- The 24-light ceiling, exact binding recipe, physical containment and neighbour-image checks
  remain authoring gates. Cone coverage and conservative candidate lists remain reported in full;
  worst-flicker-state visual review and controlled Play-mode checks govern readability acceptance.

## Core execution invariants

1. An enemy becomes executable only after `ArmouredEnemy` exposes its `CoreExecutionTarget`; `SetVulnerable()` ignites the `UnstableCore` and registers the target.
2. `CoreExecutioner` must execute only a target whose `CanBeExecuted` state is true. It performs targeting; `CoreExecutionTarget` owns target eligibility and stage.
3. `BeginExecution()` removes the target from `CoreExecutionTarget.Vulnerable` and makes its `Health` invincible. No second execution may target it during the sequence.
4. The sequence must reach `Impale()` before absorption progress; `FinishExecution()` consumes the core, restores target damageability, and kills the target.
5. The vulnerable-target static list must reset when entering a play session without domain reload. `CoreExecutionTarget` already uses `SubsystemRegistration` for this.
6. `CoreGasCloud` is a damaging, lingering post-execution hazard and constrains concurrent full-strength clouds with `MaxActiveClouds`. Raising the cap needs visual-performance and gameplay-balance validation.

## Asset and source boundaries

- Use Unity to edit scenes, prefabs, materials, and their metadata. Preserve asset/meta pairing.
- `Assets/FPS/Scripts/AI/Editor/` builders can overwrite versioned assets. Inspect their output paths before running them and review the full diff after running them.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/`, and ordinary build folders are generated local state. They are not source and must not be edited or committed.
- The project renders with URP's 3D forward renderer (`Assets/Rendering/Default_PipelineAsset_ForwardRenderer.asset`).
  - Inside the Start Workshop, mesh materials must use a shader that renders lit: URP/Lit, another URP 3D shader, or a project shader.
  - URP/Unlit is allowed only where unlit is intended, such as graphic overlays.
  - URP `2D/*` shaders and Built-in pipeline shaders (`Standard`, `Legacy Shaders/*`, `Particles/*`) are not allowed on the room's meshes.
  - Areas outside the Start Workshop keep their existing materials.
- Third-party pack materials and prefabs (SciFi Warehouse Kit, Sci-Fi Styled Modular Pack, HorrorPuzzleItems) are never edited.
  - The Start Workshop uses project-owned copies under `Assets/FPS/Art/Materials/Workshop/`, assigned only to renderers inside the room (corebuster_render_005).
  - A boundary shell piece whose outer face is visible from a reachable neighbouring space keeps its shared material (`StartWorkshopLitMaterials.KeepShared`). Its room side is lit by adding dressing in front of it, not by overriding it: a collider-less room-side liner and a shadows-only caster (corebuster_render_007, `StartWorkshopLiners`).
  - corebuster_render_001's in-place conversion (local commit `e5f38e2`) predates this bound and was withdrawn on 2026-09-29 because it darkened every kit-built area of `MainScene`. corebuster_render_004 restores those materials.
- The North-West Room (Room 3, corebuster_env_036) follows the same rule with its own folder: every renderer under `Level/NorthWest_Room` uses a flattened URP/Lit (or project shader) material under `Assets/FPS/Art/NorthWestRoom/`, and no renderer outside the room uses one. Its shell finish is `Rendering/Room_Surfaces`, collider-free inward skins on the room boundary; the original shell, the pack materials and the kit albedo textures are never edited. Effective albedo of any large non-emissive surface is at least 0.22 luminance and metallic is at most 0.4.
- Project-specific shader work is in `Assets/FPS/Art/Shaders/CoreBuster/`. The final deliverable must identify the actual custom shader technique and its visual evidence; the course requirement calls for ShaderLab/HLSL or GLSL.

## Start Workshop scope bounds (owner decision, 2026-09-29)

1. **The room shell is fixed.** The walls, ceiling, floor tiles and doorway of the Start Workshop in `MainScene` are never moved, resized, removed, replaced or re-parented, and neither are their colliders.
   - Workshop work only adds dressing under `Level/Start_Workshop`: props, fixtures, lights, overlays, decals, probes and volumes.
   - The one exception is material overrides on shell renderers inside the room (corebuster_render_005).
   - The measured interior (X [-186.43, -154.51], Z [2.43, 34.20], Y [0.18, 11.08]) and the exit gap stay the reference frame.
   - Work that would shrink the room or lower its shell ceiling is out of scope. corebuster_env_006 was cancelled. The owner chose to keep the existing `Overhead_Structure_007` dressing below the fixed ceiling; it does not change the shell.
2. **Nothing built for the Start Workshop may change how any other area looks**, whether in `MainScene` or any other scene.
   - No edit to shared third-party materials or prefabs (see the asset boundaries above).
   - Lights, reflection probes and volumes live under `Level/Start_Workshop`, and their influence stops at the room's walls.
     - For lights (corebuster_render_006, `StartWorkshopLighting`), each light's lit volume stays inside the walls' measured outer faces, and no renderer outside the room is on its per-object light list.
     - The room's floor tiles, ceiling tiles, wall panels and liners each take at most the pipeline's 4 additional lights from the room.
   - The shell pieces' edges show from outside, and the floor tiles run to the walls' outer faces. A change to a shell copy's material must leave those edges' look unchanged (`BUGS.md`).
   - No change to scene-wide lighting (ambient, skybox, environment reflections, fog, the `Directional Light`, lightmapping) or to the URP pipeline or renderer assets, unless captures prove that the areas outside the room are unchanged.
   - **Decal exception (corebuster_render_002).** `Assets/Rendering/Default_PipelineAsset_ForwardRenderer.asset` carries one URP `DecalRendererFeature` (DBuffer, albedo and normal, decal layers), added by `UrpDecalFeatureSetup` and nothing else. It is a global change that paints nothing by itself.
     - Grime decals paint only renderers on rendering layer 7. Only the room's own shell, liners and cladding carry it; the validator fails if any other renderer in the room's region, any enemy or player renderer, or any character or weapon prefab renderer does.
     - Every grime projector lies inside the room. Any later change to the feature's settings, or to what carries layer 7, needs `Prove Grime Feature Leaves Neighbours Unchanged` (`TESTING.md`) to show the neighbouring views unchanged, and a frame-cost check.
     - The feature's cost is fixed per frame, in every scene, not only where decals show.

## Boss Hall environment framework bounds (corebuster_env_048)

- Environment additions belong only under `Level/Boss_Hall_Dressing`, at world identity with task-owned, local-identity sections. Original shell, decks/stairs, all eleven actors, door/incline, progression wiring and baked navigation retain their original parents, transforms and components.
- The tracked `BossHallLayout.json` embeds the accepted env_047 reservations and approved protected scene/asset fingerprints. A changed scene or asset baseline requires explicit reconciliation after separately authorized work; builders never adopt it automatically or change the measured contract in memory.
- Section rebuilds require the exact owning task ID, preserve every other section and support Undo rollback. Callbacks must use Undo for scene edits; asset disk writes have separate ownership and cannot be restored by scene Undo.
- Dressing uses flattened materials below `Assets/FPS/Art/BossHall/`. The framework rejects their assignment outside its root, foreign room/shared materials inside it and gameplay scripts on decoration. render_010's exact boundary-skin exception is defined below; original-renderer overrides remain forbidden. Later visual components require scoped validator extensions.
- Empty framework work grants no shell/rail/stair repair, NavMesh rebake, encounter/controller change or decorative executable target. Survey findings and the separate containment/win-ordering gate remain open. The provisional budget shares in `BOSS_HALL.md` do not relax the four-additional-light per-object limit.

## Boss Hall rendering exception (corebuster_render_010)

- Only exact derived skins in `Assets/FPS/Art/BossHall/Rendering/SurfaceBindings.json`, bound to the accepted protected baseline/contract, may cross ordinary dressing keep-outs. They are direct identity-transform children of `Rendering`, collider-free, enabled, receive-only for shadows, with matching saved mesh hashes/bounds/materials. This grants no collision or gameplay authority.
- Original renderer references remain unchanged. Source-derived inward clipping protects exterior tile edges and wall/roof faces; covered sunken tiles get no visible skin. Ground-plane error shaders and low wall collision gaps remain separate repairs.
- Isolated section rebuilds reapply known owned equivalents only within their target. Boss Hall materials remain flattened in the room art root, never assigned to original shell, actors, weapons, neighbours or shared prefabs. The twenty prepared finishes count toward the existing twenty-material ceiling.
- Grating alpha/UVs and asset-backed tiling survive reload. Large opaque copies have effective linear luminance at least 0.22 and metallic at most 0.4. Emissive accents need `_EMISSION` and `AnyEmissive` GI flags after import; this pass adds no global setting, bake, volume or architectural light.
- The owner's 2026-10-04 gameplay-gate waiver applies only to render_010. Later gates and survey defects remain open.

## Boss Hall assembly backdrop bounds (corebuster_env_049)

- `Assembly_Backdrop` content stays inside its contract-derived envelopes:
  - the cradle in west deep pocket 2, below the inter-tier firing volume;
  - the 1.2 m west wall strip below the tier-2 deck;
  - the overhead gantry above upper-deck headroom, outside the west bay-door approach.
- Below floor +1 m, nothing touches the west wall (0.35 m stand-off). The under-wall collision gap is a separate shell repair, and dressing must not be relied on to close it.
- Collision is one consolidated cradle box plus one matching carving obstacle, below Y 8.80. Backdrop collision must not newly block any turret line to a floor or tier-1 gallery eye that was clear before it was added. No baked NavMesh, agent, actor, turret or encounter change is permitted.
- The owner's 2026-10-04 gameplay-gate waiver covers env_049 only. Containment, win ordering and the official turret role still gate later dressing and final sign-off.

## Boss Hall mechanical arm bounds (corebuster_art_006)

- Four parked 1.55x visual arms belong to two owned cells in `Mechanical_Arms`. Source Workshop meshes/prefab/materials stay byte-identical; their colliders and gameplay affordances are not imported. Gunmetal/HazardYellow are reused from render_010; no new finish, lighting, emission, audio or animation is added.
- The two contract-derived arm envelopes flank the overhead hoist and the west bay-door approach. Source arm parts stay inside them. Only the six owned supporting meshes may extend upward to ceiling ?1.30 m +0.18 m to connect to the existing runway. Ordinary framework keep-outs still apply to every renderer; no skin-style placement exemption is added.
- There are no arm colliders or carving obstacles. Preserve the cradle collision/carve, original stairs/rails, all actors, firing/reveal lines, navigation and other dressing sections. Validate all three levels' turret firing rays and the entry silhouette; whole-arm triangle ceiling is 36,000, alongside the existing root/prefab/material budgets.
- The owner's 2026-10-04 gameplay waiver applies to art_006 only. Containment, win ordering and later dressing/final-sign-off gates remain open.
- The env_046 baseline acceptance was explicit, restricted to the audited scene SHA and unchanged protected assets, with corresponding skin-manifest binding updates only. Future changed baselines still require separately reviewed reconciliation; builders fail closed.


## Boss Hall spawn-room merge reconciliation (corebuster_env_051 prerequisite)

The user-requested merge/synchronization accepts remote spawn-room and Player-start changes only alongside every retained local change. `AcceptSpawnRoomMergeBatch` requires the exact independently audited merged-scene and prior contract/manifest hashes, preserves protected shared assets and all measured survey/reservation/skin data, and changes only the protected scene fingerprint/count and corresponding manifest bindings. MainScene remains byte-identical; future baseline changes still fail closed. This synchronization grants no gameplay-gate waiver, route/rail acceptance, shell repair or new placement exception. Catwalk preparation stays outside walking/headroom/firing reservations, with upper-tier placement deferred.

## Boss Hall catwalk scope disposition (corebuster_env_051)

On 2026-10-04 the owner approved the six prepared first-tier maintenance racks, deferred all upper-tier/stair/rail dressing, and waived the separate gameplay prerequisite for env_051 only. All ordinary placement, asset, gameplay, collision and navigation bounds remain binding. No upper-route acceptance or repair, containment/win-order verification or later-task waiver is granted.

## Boss Hall decorative energy bounds (corebuster_env_053)

- Energy ownership is limited to exact `Energy_053` children of the saved Assembly_Ring, Assembly_Arms_South/North, Power_Feed_South/North and Overhead_Feeder_South/North roots. Their parents, original machinery, prefabs and other sections remain protected. Rebuilds restore installed energy inside the parent transaction; failed rebuilds roll back through Undo.
- Energy contains only active Default/Untagged Transform, MeshFilter and MeshRenderer objects with exact owned mesh/material/transform bindings. No MonoBehaviour, gameplay core, health/damage, collider, obstacle, light, particle or audio component is permitted. The ordinary measured keep-outs still apply.
- Three materials under `Assets/FPS/Art/BossHall/Energy/` reuse the existing Reactor Containment/Core Energy Additive shaders. Source shaders, shared and other-room materials, actor/core/weapon assets and global rendering stay unchanged. Energy renderers neither cast nor receive shadows and use no probes.
- The owner's 2026-10-05 encounter-prerequisite waiver covers env_053 only. Controlled runtime regression does not clear containment/win-order verification or later-task/final gameplay gates.

## Boss Hall meltdown VFX bounds (corebuster_env_054)

- Meltdown effects live only as direct children of `Level/Boss_Hall_Dressing/Meltdown_VFX`. They are active, Default, Untagged and non-static, with exact recipe transforms derived from the contract and the saved ring/feeder anchors.
- Allowed components are Transform, ParticleSystem, ParticleSystemRenderer, LineRenderer, MeshFilter and MeshRenderer only. There is no MonoBehaviour, collider, carve, light, audio, health/damage, objective or encounter state.
- Particle modules for collision, trigger, lights, sub-emitters, external forces, noise and trails stay off, and emitters use scaled time.
- Initial target: at most 6 emitters, with hard live caps of 4/20/20/24/24 (92 total). Raising a cap or adding an emitter is a recipe change that fails validation until reviewed.
- Every effect's conservative recipe envelope must pass the ordinary measured keep-outs and the entry-to-turret sight line. Effects stay out of actor envelopes, the turret sweep, walking/firing reservations and the entry approach.
- Two owned flattened materials (hall total 19/20) reuse the unchanged Electric Arc and URP particle shaders. Workshop/Room 3 source textures and materials, shader sources, weapon/enemy VFX and global rendering stay unchanged.
- Effects cast no shadows and use no probes. Strict neighbour views must stay pixel-identical.
- Flashes stay comfortable: discharge bolt events at most about 0.53/s, arc strobe within 0.81–1.20×, no full-screen flash. Ambient sparks stay amber and fixed overhead, distinct from red/green/blue radial weapon-hit bursts.
- The owner's 2026-10-05 encounter-prerequisite waiver covers env_054 only. Warning-flicker light bindings belong to env_055 under its own Lighting checks.

## Final polish bounds (2026-10-14, `Docs/FinalPolish/README.md`)

1. **Zone streaming.** `ZoneStreamer` hides renderers only through `Renderer.forceRenderingOff` and switches off only lights that carry no scripts of their own.
   - Scripts that need to hide geometry must use `enabled` or `SetActive`, never `forceRenderingOff`.
   - A new room needs its XZ area added to `ZoneStreamer.Rooms`. Otherwise its contents count as corridor geometry and are drawn from every corridor.
   - Doors that reveal a zone must be `SecurityDoor`s, and readers must open them through `ZoneStreamer.OpenWhenReady`, so the zone behind them is drawn before it is revealed.
2. **Corridor materials.** Corridor renderers (`Plane (1)`) use only the URP/Lit copies in `Assets/FPS/Art/Corridors/Lockdown/`. Their darkness comes from each copy's occlusion map, not from scene-wide lighting. `Build Corridor Lockdown` owns these materials, the corridor lamps' `CorridorBeacon` settings and `===== CORRIDOR LOCKDOWN =====`.
3. **Graphics qualities.** `ProjectSettings/QualitySettings.asset` has exactly two levels: `Normal` (index 0, the default on every platform, `Default_PipelineAsset`) and `Ultra` (`Ultra_PipelineAsset`).
   - Ray tracing requires Ultra's renderer list to keep `Ultra_RayTracedRenderer` at index `RayTracingWorld.RayTracedRendererIndex` (1).
   - Normal must stay playable with ray tracing off on the presentation machine.
   - Both levels keep the 4 per-object additional-light limit.
4. **Loading.** `MainScene` is loaded through `LoadingScreen`. Systems that prepare the scene hold `LoadingGate` and must release it, including on failure.

5. **Boss Hall core fight (corebuster_game_002, env_058).** `Build Finale` owns `===== FINALE =====`: the reactor copy, beam, consoles, cables and the opened core. It also owns the turret's west position. `Build Boss Hall Floor` owns `===== BOSS HALL FLOOR =====` and the two ProBuilder ground planes' material.
   - Each armour layer is removed only by a console overload (`GiantCore.BlowOffActiveLayer`). Re-enabling `ShotsBreakArmour` brings back the older fight.
   - The 12 m ring around the reactor stays free of cover and spawn points.
   - Run Build Boss Hall Floor after Build Finale.
6. **Rocket launcher (corebuster_game_004).** `Build Rocket Launcher` owns `===== ROCKET LAUNCHER =====` (the cradle over the Final Assembly deck, its emitters, beams, power panel and conduit), the three prefabs `Weapon_RocketLauncher`, `Projectile_Rocket` and `VFX_RocketExplosion`, and the art under `Assets/FPS/Art/RocketLauncher/`.
   - Rockets deal no damage. Their blast (`ArmourBreakBlast`) only calls `IArmourBreakable.BreakArmour()`, so the core execution invariants above are unchanged: armour break still exposes the target through `ArmouredEnemy`.
   - One blast strips at most 3 enemies (nearest first). The launcher holds one rocket, back after 15 s (`LauncherCooldown`), and each shot returns the player to the main gun.
   - The giant core is not `IArmourBreakable`. Its layers stay removable only by console overloads, so the launcher cannot shortcut the core fight.
   - The power panel spends the Disassembler's charge through `CoreExecutioner.TryInject`, as the consoles do.
   - The explosion stays a single burst of about 100 particles plus one light that is off within 0.3 s. Raising those counts needs a frame-cost check in a crowded fight.

## Change protocol

1. Update this file before or with an intentional change to a bound.
2. Update `ARCHITECTURE.md` if ownership or flow changes.
3. Run the narrowest relevant Unity Edit-mode/Play-mode verification and the manual scenario in `TESTING.md`.
4. Record a reusable defect lesson in `BUGS.md` only after its root cause is understood.

## Boss Hall wayfinding bounds (corebuster_env_057)

- Graphics occupy only Wayfinding: the exact collision-resolved `Sign_Final_Assembly` reservation and two measured first-tier gallery wall patches with corner backing, empty front envelopes and ordinary keep-out checks. Three 4 x 1 m plates face actual floor/gallery approaches with readable full-range Quad UVs. Only Transform, MeshFilter and MeshRenderer are allowed; no added child, script, collision, navigation, light, particles, audio or terminal affordance.
- One flattened room-owned opaque URP/Unlit material and deterministic first-principles texture use the twentieth material slot. Shadows and probes are off; property blocks are forbidden. Wording is FINAL ASSEMBLY, with a printed safety border and no directional or interaction claim.
- Owner waived the encounter prerequisite for env_057 only on 2026-10-07. Finale progression, containment, combat and all-quality/player-build validation remain env_058. Changed baseline fingerprints need explicit reviewed reconciliation before authoring; the graphics builder never adopts them.

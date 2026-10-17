# Boss Hall focal and emergency lighting — corebuster_env_055

Authored and reviewed 2026-10-07 in Unity 6000.5.7f1.

The final choice is a cool turret/assembly focal point, warm separated arena pools and route
washes, red upper warnings, and small violet/amber flicker accents. It is saved in MainScene.
The owner waived the encounter prerequisite for this task only, then superseded the initial
strict-candidate hold with full permission to choose the lighting tradeoff and conclude the task.
The chosen solution retains the existing four-slot forward pipeline and every gameplay light.

## Saved implementation

`Level/Boss_Hall_Dressing/Lighting` contains 24 realtime spotlights: four arena pools, two focal
keys, eight stair throws, four gallery washes, one entry key, two upper warnings and three
reserved flicker lamps. Only the turret key casts shadows, at the lowest URP resolution tier.
The ring uses the existing scaled-time `ReactorInstabilityLight`; the two feeders use only Lamp
mode of `WorkshopAmbientMotion`. The bindings affect their own lights and use the env_054 anchors.

No geometry, collider, probe, material, audio or gameplay object was added. Hall materials remain
19/20 and emitters 5/6. Generator, validation, capture and disposable runtime-probe sources have
matching Unity metadata. The framework's physical keep-out exception permits only exact recipe
light-only objects, with no descendants or fixtures inheriting the exception.

## Evidence

| Check | Result and evidence |
| --- | --- |
| Final transient preflight | Exit 0; `Logs/env055_final_preview.log`, `LightingPreflight_055.txt` |
| Unity build/save/reload | Exit 0; five negative mutations and clear/Undo pass; 58,367 outside states and 3,358 shared entries unchanged; `Logs/env055_build.log`, `Lighting_055.txt` |
| Fresh saved capture | Exit 0; six strict neighbours unchanged at delta >4; `Logs/env055_capture.log`, `LightingCapture_055.txt` |
| Fresh post-Play validation | Exit 0, zero hard errors, protected shared baseline intact; `Logs/env055_validate.log`, `LightingReload_055.txt` |
| Controlled Play mode | Success path, scene/shared digest unchanged; three lamps pause/resume; real floor/gallery armour-break and consume-before-death execution; `Logs/env055_runtime.log`, `LightingRuntime_055.txt` |
| Scene scope | 99 new YAML blocks, none removed; only existing Lighting child list changed; `SceneScope_055.txt` |

Final MainScene SHA-256:
`f7197d7c4b5445fd570cf7e2ead21e8bd43898d2587e0d47492f25b8ad7ad4ee`.

Task lifecycle is closed in `DONE_TASKS.md` on 2026-10-07; the full env_055 block is removed from
`PLAN.md`. Source diff review found only 27 Unity-authored empty `m_Name: ` trailing-space
warnings in scene YAML; these were left under Unity ownership. Source/docs have no whitespace errors.

Agent visual review covered entry, arena, assembly, both galleries, upper views and all eight
stairs at minimum flicker, with steady/maximum comparison and both impaled execution captures.
The floor pools separate routes; warm treads and yellow rails remain visible against the darker
structure. The cool stator/cradle and turret are the primary focus. The existing bridge still
partly screens the assembly from entry, and large areas intentionally retain ambient/emissive
illumination rather than direct pools. This is agent review, not human-input sign-off.

Local evidence images (generated/ignored):

- [Saved minimum entry](../../ReviewCaptures/BossHall/055/Saved/Minimum/Entry.png)
- [Saved minimum assembly](../../ReviewCaptures/BossHall/055/Saved/Minimum/Assembly.png)
- [Saved minimum north gallery](../../ReviewCaptures/BossHall/055/Saved/Minimum/Gallery_North.png)
- [Saved minimum upper reverse](../../ReviewCaptures/BossHall/055/Saved/Minimum/Upper_Reverse.png)
- [Floor execution](../../ReviewCaptures/BossHall/055/Runtime/Execution_Floor.png)
- [Gallery execution](../../ReviewCaptures/BossHall/055/Runtime/Execution_Gallery.png)

## Rendering cost and accepted limits

Containment and outside-renderer checks have zero errors. Conservative square-frustum candidate
lists peak at seven for four forward slots on hall renderers (baseline four); 1,260 actor/core/
weapon bounds peak at five. All eight saved hall cores at maximum surge produce six candidates
(baseline four). Architecture plus two hypothetical combat lights peaks at four. These bounds
are conservative candidates, not the actual shader-selected lists. Crowded combat may omit a
light; no gameplay lamp or renderer has been suppressed to hide that tradeoff.

Steady direct cones cover 114/248 stair/deck/entry/actor eye samples. The remainder require the
ambient/emissive silhouette review above. Minimum and maximum flicker images retain the steady
route lights. These observations do not guarantee every moving combat configuration.

The Play probe observed 22/33/39 NPC weapon shots during entry/arena/gallery samples; the upper
reverse sample had zero shots with AI still enabled. Its measurements include normal Editor
rendering plus a temporary 960x540 review-camera render. Each off/on sample has ten warmup and
40 measured advancing frames; AI and VFX are not replayed identically between the two samples.

| View | GPU mean off/on (ms) | Main thread mean off/on (ms) |
| --- | ---: | ---: |
| Entry | 13.26 / 14.01 | 16.72 / 17.75 |
| Arena | 12.59 / 15.46 | 16.38 / 19.86 |
| Gallery | 39.84 / 40.05 | 46.69 / 48.16 |
| Upper reverse | 32.14 / 29.55 | 38.66 / 35.14 |

These engine counters establish observed Editor cost, not target-hardware player FPS. The
gallery is costly before lighting is enabled. URP reports reducing additional shadow resolution
by two to fit 15 slices into the unchanged 1024 atlas. The new pass contributes one low-tier
slice and no alpha geometry; no overdraw heatmap was captured. The runtime report includes
triangles, SetPass and shadow-caster counters plus nonzero FrameTimingManager GPU readings.

Existing negative-scale catwalk collider warnings and the headless menu's null-mouse exception
remain outside this visual task. The runtime probe disables that menu only in Play mode and
discards all temporary invincibility, positioning and execution changes on exit.

`corebuster_env_058` owns independent human moving combat/upper traversal, clustered core/cloud/
projectile selection review, overdraw/shadow/player-build profiling, and the full keycard-to-win
progression. The task-specific encounter waiver does not clear those final verification gates.

# CoreBuster

**COMP307 Computer Graphics, Group Project, 2026 (University of KwaZulu-Natal)**

CoreBuster is a first-person sci-fi shooter built in Unity 6 (URP). You wake from hibernation on a ship overrun by hostile robots, fight your way through a keycard-gated sequence of rooms, and finish the ship's unstable core in one of two endings. The signature mechanic is the **Core Bust**: break an enemy's armour (or freeze it with the Disassembler), step in, and tear the glowing Unstable Core out of its chest.

---

## Contents

1. [Group members](#group-members)
2. [How to run](#how-to-run)
3. [Controls](#controls)
4. [What we built](#what-we-built)
5. [Mechanics and implementation](#mechanics-and-implementation)
6. [Custom shaders](#custom-shaders)
7. [Assets created from first principles](#assets-created-from-first-principles)
8. [Third-party assets and credits](#third-party-assets-and-credits)
9. [Win and loss conditions](#win-and-loss-conditions)
10. [Repository layout and documentation](#repository-layout-and-documentation)
11. [AI disclosure](#ai-disclosure)

---

## Group members

| Name | Student number |
|---|---|
| Jawad Kachbal | 223037045 |
| Joshua Blom | 224039813 |
| Mahomed Valli Mahomed | 224005878 |
| Andiswa Hlengwa | 221002430 |
| Abiel Govender | 224058851 |

---

## How to run

**Requirements**

- Unity **6000.5.7f1** (pinned in `ProjectSettings/ProjectVersion.txt`). Install it through Unity Hub.
- Windows with a DirectX 11 or 12 GPU. The Windows graphics API list is `Direct3D11, Direct3D12`.
- Python 3 with Pillow (and numpy for one script) is only needed if you want to regenerate the procedural textures and audio in `Tools/Art/`. It is not needed to play.

**Steps**

1. Clone the repository.
2. In Unity Hub choose **Add project from disk** and select the repository root (the folder that contains `Assets/`, `Packages/` and `ProjectSettings/`). Open it with 6000.5.7f1. The first import takes a while because the project is large.
3. Open `Assets/FPS/Scenes/BootScreen.unity` and press **Play**. The game flows `BootScreen`, `IntroMenu`, `MainScene`, then `WinScene` or `LoseScene`. These are the scenes listed in the build settings.
4. `MainScene` loads behind a loading screen. The first load takes roughly 15 to 20 seconds in the Editor while each room is warmed up.

**Graphics quality.** *Options* offers **Normal** (default) and **Ultra**. Ultra raises shadow quality and MSAA and switches on a ray-traced ambient occlusion pass (hardware DXR where available, otherwise a compute-shader BVH). The compute path is slow on integrated GPUs, so use Normal there.

**Note.** We ran and tested the game in the Unity Editor. A standalone player build has not been tested.

---

## Controls

| Action | Keyboard and mouse |
|---|---|
| Move | `W` `A` `S` `D` |
| Look / aim | Mouse |
| Fire | Left mouse button |
| Aim down sights | Right mouse button |
| Reload | `R` |
| Jump | `Space` |
| Sprint | `Left Shift` |
| Crouch | `Left Ctrl` (the tutorial teaches this key; `C` is also bound) |
| Slide | Crouch while moving fast |
| Interact (hold on terminals, take items, Core Bust) | `E` |
| **Fire the Disassembler** | `F` |
| Switch weapon | `1` / `2`, `Q`, or mouse wheel |

A gamepad is also supported through the Input System bindings in `Assets/FPS/InputSystem_Actions.inputactions`. Keys can be rebound in the Options menu.

---

## What we built

CoreBuster started as a wave-based arena shooter with modular enemies and grew into a level-based game with a tutorial, keycard progression and a two-ending finale.

**The level.** One continuous ship, `MainScene`, made of five areas joined by corridors:

| Area | Role |
|---|---|
| Tutorial wing | Wake-up sequence, controls drill, and a gallery that showcases the game's NPCs, shaders and weapons |
| Start Workshop | First combat space, dressed with hand-built industrial props |
| Centre Room | Unstable blue reactor and the second keycard gate |
| North-West Room | Turbine hall, third keycard gate, and the escape lockdown |
| Boss Hall | The giant core, its reactor, three consoles and the final fight |

**The loop.** You collect three coloured keycards (black, blue, yellow), tap them on access machines to open security doors, and finally insert all three into the Master terminal, which lets you choose how to end the game.

**Enemies.** Armoured Cyber Soldiers, Robot1 brawlers, HoverBots, Elite warriors and turrets, armed with piston fists, rivet cannons, scrap scattergun and an assault rifle that fires rivet bursts.

**Weapons.** A main blaster, a shotgun, the Disassembler sidearm, and a rocket launcher that you free from a containment cradle by overloading its power panel.

---

## Mechanics and implementation

All gameplay code is under `Assets/FPS/Scripts/` and split into assemblies (`fps.Game` for shared primitives, `fps.Gameplay`, `fps.AI`, `fps.UI`, `fps.Rendering`).

### Player and movement
The player is built on Unity's FPS Microgame character controller and extended. `PlayerCharacterController` provides walking, sprinting, jumping, crouching and a speed-boosted **slide** with its own friction and duration limits. A jetpack component and the weapons manager (`PlayerWeaponsManager`, `WeaponController`, `ProjectileStandard`) handle gunplay, aiming and weapon switching. Input goes through the Input System actions asset.

### Core Bust execution
This is the centrepiece mechanic and is implemented as a small state machine across four scripts:

- `ArmouredEnemy` soaks part of incoming damage while armour holds. Once health drops below a threshold the armour is cut from the **current skinned pose** into physical debris chunks, the body switches to its exposed material, and the enemy's core is exposed.
- `CoreExecutionTarget` marks an enemy as executable, owns its eligibility and execution stage (`None`, `Grabbed`, `Impaled`, `Finished`), and plays the victim's half of the animation (doubling over the gun, slumping onto the player's shoulder, convulsing) on top of the Animator pose.
- `CoreExecutioner` does the targeting (distance, aim angle, line-of-sight check) and plays the player's half: the gun tucks in at the hip, jabs into the core, holds while the core is sucked in, then wrenches free. The player is invincible for the whole sequence and healed by the absorbed core.
- `UnstableCore` is the orb itself: a wobbling, surging energy sphere with embers, a light, a hum and an absorb stream. Executing it leaves a lingering damaging `CoreGasCloud` (capped by `MaxActiveClouds` to keep overdraw in check).

The order is fixed on purpose: armour break exposes a target, the target is removed from the vulnerable list while it is being executed (so nothing else can target it), and the core is consumed before the enemy dies. These invariants are written down in `SYSTEM_BOUNDS.md`.

### The Disassembler
A charge-based sidearm. Each execution charges it, and at full charge (three executions by default) one press quick-draws it from the left hip and fires a cluster of blue laser bolts (`DisassemblerBolt`). Every enemy hit is electrocuted by `DisassemblerShock`: its AI, weapons, navigation and animation freeze, the body convulses while lightning arcs to the ground (`ElectrocutionVfx`), and the chest hatch (`CoreWindow`) opens onto the core, so even an armoured enemy can be Core Busted. If nobody executes it, the shock wears off and the enemy resumes.

### Enemy AI
- **Navigation and chasing.** Enemies use the NavMesh. `Robot1ChaseController` is room-aware: doors split the NavMesh into rooms, so an enemy that cannot walk a complete path to the player holds its room instead of pressing against a doorway.
- **Perception.** `DetectionModule` finds the closest visible hostile actor and checks for obstructions.
- **Attacks.** `PistonFist` is a melee attack that alternates arms and locks onto the target's position at wind-up, giving a readable telegraph. `RivetCannon` fires slow rivet bolts in bursts: the barrel tracks the target, a warning lamp flashes, and the cannon only fires once the arm is raised and aimed.
- **Animation.** `Robot1ProceduralAnimator` layers procedural attack poses (hook punches, raised arms) over the locomotion clips, scaled to world speed.
- **Reactions.** Behaviour reacts to the player: freeze and thaw from the Disassembler, armour state, and the player's position relative to doors and rooms.

### Keycard progression
`KeycardInventory`, `KeycardTerminal`, `KeycardAccessPoint`, `SecurityDoor` and `MasterTerminal` implement the gating loop. `KeycardInteractor` plays first-person floppy-disk moves (inspect, tap on a reader, slot into the Master terminal) with the player locked in place. Readers pause with "PROCESSING CREDENTIALS..." before opening, which is also used to pre-render the room behind the door.

### Tutorial
`TutorialDirector` runs the wake-up: eyelids opening, a voiced ship-AI briefing with subtitles, stretching, taking the sidearm from a locker, drilling on dormant targets, and collecting the first keycard. Steps advance on what the player has actually done, so out-of-order play never stalls it.

### Finale
`FinaleDirector` offers two endings once all three keycards are in the Master terminal:

- **Escape.** The North-West Room locks down, Elites warp in, and a 120 second countdown runs. Surviving launches the escape pod.
- **Fight.** The Boss Hall opens and enemies warp in endlessly. Executing three enemies charges the Disassembler, and the cores are injected into one of three reactor consoles (`ReactorConsole`, via `CoreExecutioner.TryInject`, the execution played in reverse). Each overloaded console surges down its cable, overcharges the reactor beam and blows one armour layer off the `GiantCore` (steel, ceramic, lead). With the heart exposed, the player Core Busts it for the win.

### Rocket launcher
A weapon held in a `WeaponCradle` by two containment beams until an `OverloadPanel` is overloaded. Rockets deal no damage. Their blast only breaks armour on up to three nearby enemies, exposing them for execution, so the core-execution rules stay intact.

### Performance
- **Zone streaming (`ZoneStreamer`).** The level is divided into zones. Only the zone the camera is in, plus zones seen through open doors, are drawn. Hidden renderers use `forceRenderingOff` so colliders and AI keep running, and plain lights in hidden zones switch off. Measured in the Editor on an integrated GPU at the gun locker: frame time fell from 20.0 ms to 12.3 ms, triangles from 1.59 M to 0.51 M, and SetPass calls from 1,108 to 411.
- **Loading.** A `LoadingScreen` with a `LoadingGate` holds the scene until zones are warmed and, on Ultra, the ray-tracing BVH is built.
- **Quality levels.** Normal and Ultra pipeline assets, with ray tracing off by default on Normal.

### Rendering extras
Per-room lighting is bounded so one room's lights never change another's. A custom URP decal feature paints grime inside the Start Workshop only. Corridor lighting uses a per-route colour coding with caged lamps and `CorridorBeacon` components.

---

## Custom shaders

All custom shaders are hand-written ShaderLab/HLSL for URP in `Assets/FPS/Art/Shaders/`. None are Shader Graph.

| Shader | Technique shown |
|---|---|
| **Unstable Orb** (`UnstableOrb.shader`, `CoreOrbCommon.hlsl`) | The core itself. Vertex displacement from moving 3D noise makes the surface wobble and spike; surges, instability and collapse are driven per frame by `UnstableCore.cs`. |
| **Core Window** | A brass iris hatch drawn on a single quad. The view ray is traced in object space through a cylindrical cavity to the orb, so the core has real **parallax depth** without any geometry behind the quad. |
| **Galaxy Husk** (`Assets/Shaders/GalaxyHuskShaderURP_Textured(2).shader`) | The enemy "galaxy" body: two nebula textures **triplanar-projected**, scrolled and cross-faded over time, plus a procedural twinkling star layer and a **fresnel** colour shift. |
| **Core Armour** | One shader with three styles (steel, ceramic, lead). Damage is drawn procedurally: vertex displacement crumples or dents plates, and a 3D **Voronoi** crack network spreads cell by cell and glows with the core's light. `ArmourPiece` feeds per-plate damage and seed through a `MaterialPropertyBlock`. |
| **Galaxy Gas** | Particle shader for the gas an executed enemy bursts into. Drifting noise layers shape and fold each puff, stars twinkle inside, and colour is premultiplied so smoke alpha-blends while hot folds add light. |
| **Electric Arc** | Additive shader for the Disassembler's bolts and the lightning off electrocuted enemies. A wandering white-hot core in a glow, with travelling pulses and a strobe. |
| **Reactor Containment / Core Energy Additive** | GPU-animated reactor field with an unstable surge (irregular swells, dropouts, flashes) that the room lights mirror, and procedural soft-dot and ring particle shapes. |
| **Reactor Beam** | Domain-warped value-noise filaments stretched along the beam, climbing pulses, flicker, and an overcharge parameter driven by the console overloads. |
| **Power Cable** | Lit PBR sheath with steel rib collars, idle flow dashes, and a bright energy slug that travels down the cable when a console overloads. |
| **Floor Guide** | Additive guide chevrons with soft edges and pulses that travel toward the objective. |
| **Procedural Nebula Skybox** (`ProceduralNebulaSkybox_URP.shader`, `NebulaSky.hlsl`) | A fully GPU-generated nebula with no textures, so it never blurs. It also draws a **black hole**: rays inside a capture radius fall in, nearby rays are **gravitationally lensed**, and an accretion disk shows Keplerian shear, **Doppler beaming** and orbiting hot spots. |
| **Sky Window** | The corridor window panes draw the same nebula sky along each pixel's view ray, so they look onto space, with haze, glare streaks and dust over the top. |
| **UI Galaxy / UI Core Glow / UI Core Meltdown / UI Toxic Gas Overlay / Core Halo** | Menu and HUD effects, a screen-edge gas cue that thickens with exposure, and a camera-facing halo billboarded in view space. |
| **Ray-traced AO** (`RayTracing/RayTracedAO.urtshader`, `RayTracedAODenoise.compute`) | Two cosine-distributed rays per pixel against the level BVH, reprojected and accumulated over frames, then upsampled with a depth-aware filter and published to URP as the screen-space occlusion texture. |

---

## Assets created from first principles

The course requires assets modelled from first principles. We built these with code rather than importing them:

- **Procedural meshes.** A metre-scale primitive builder (`ProceduralMeshBuilder.cs`) is used by editor tools to generate the Heavy Industrial Kit (pipes, elbows, tees, valves and the P-3 tank), the yellow robotic arm (seven meshes, 4,320 triangles), the Centre Room reactor, the North-West Room turbines, the caged corridor lamps, and the Boss Hall arms, catwalks and backdrop.
- **Procedural textures.** Python scripts in `Tools/Art/` draw signage, floor markings, grime, ambient particle sprites and the exit board with Pillow, from seeded randomness, so every run produces identical output. Lettering uses a hand-built stencil alphabet (`stencil_font.py`), so no font file is shipped for it.
- **Procedural audio.** The reactor ambience loop is synthesised in Python. The ship AI's voice is Windows text-to-speech passed through our own DSP chain (band-pass, comb resonance, ring modulation, saturation and reverb).
- **Procedural shaders** listed above.
- **Reproducibility.** Every generator is a menu item under `Tools/CoreBuster/` and has an authoring note in `Tools/Art/`.

<!-- TODO (team): confirm which character and weapon models (for example the Disassembler gun, enemy armour shells, Robot1 and Cyber Soldier bodies) were modelled by hand, and list them here. -->

---

## Third-party assets and credits

Third-party packs live in their own folders under `Assets/` and are not edited. Where the game needed a different look, we made project-owned copies of the materials under `Assets/FPS/Art/`.

| Asset | Author / publisher | Used for |
|---|---|---|
| **FPS Microgame** (`Assets/FPS`) | Unity Technologies | Base player controller, weapon, health and enemy frameworks, HoverBot, HUD and audio that we extended |
| **SciFi Warehouse Kit** | Sickhead Games | Walls, floors, door frames, vents and the corridor kit |
| **Sci-Fi Styled Modular Pack** | Karboosx (to verify) | Modular floors, walls and palettes |
| **Sci-Fi Warrior PBR HP Polyart** | Polyart / Dungeon Mason (to verify) | Elite warrior enemies |
| **Human Basic Motions (FREE)** | Kevin Iglesias | Humanoid animation clips |
| **Steampunk UI** | Gentleland (Jacky Martin) | UI art and fonts (Bebas Neue, Roboto Slab, Arvo, each under its own font licence in the pack) |
| **Sci Fi Doors and Sci fi Access Machine** | MASH Virtual | Security doors and keycard access machines |
| **3D Scifi Kit Starter Kit HD** | Creepy Cat | Sci-fi environment props |
| **Horror Puzzle Items** | TODO: author | Floppy-disk keycards and small puzzle props |
| **LP Sci-Fi Weapons Lite** | JustCreate3D (to verify) | Low-poly weapon models |
| **Lunar Landscape 3D** | TODO: author | Rock prefabs |
| **"energy gun prototype (project c0r3)"** | average c-pen enjoyer, on [Sketchfab](https://sketchfab.com/3d-models/energy-gun-prototype-project-c0r3-60699e40cdc94060aa219e7b4e0bfa80), licensed [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | Main weapon model (`Assets/Models/Main Weapon`) |
| **Cyber Soldier model and textures** (`Assets/MyAssets/CyberSoldier`) | TODO: author | Armoured Cyber Soldier enemies |
| **Robot1** (`Assets/Models/Robot1.fbx`) | TODO: author | Robot1 enemies |
| **Nebula images** (`Assets/Custom Textures`) | TODO: source | Nebula and skybox textures |
| **TextMesh Pro, ProBuilder, Cinemachine, Input System, URP, AI Navigation, NavMeshComponents** | Unity Technologies | Engine packages |
| **Roboto, LiberationSans, EmojiOne** | See `Assets/FPS/Third-PartyNotice.txt` | Fonts bundled with FPS Microgame |
| **Windows text-to-speech** (Microsoft Zira) | Microsoft | Raw voice for the ship AI, before our DSP processing |

The Sketchfab model requires this attribution: this work is based on "energy gun prototype (project c0r3)" by average c-pen enjoyer, licensed under CC-BY-4.0.

---

## Win and loss conditions

- **Win (Escape).** Survive the 120 second lockdown in the North-West Room. The escape pod launches, a white flash plays, and the win screen appears.
- **Win (Fight).** Strip all three armour layers from the giant core using the reactor consoles, then Core Bust the exposed heart.
- **Loss.** Player health reaches zero. During the escape lockdown, death plays the player's own core expanding and bursting before the lose screen.

---

## Repository layout and documentation

| Path | Contents |
|---|---|
| `Assets/FPS/` | Game scripts, scenes, prefabs, art, audio and shaders |
| `Assets/FPS/Art/Shaders/` | Custom shaders (`CoreBuster/` subfolder for the project-specific ones) |
| `Assets/FPS/Scripts/` | Runtime code, with editor generators under `*/Editor/` |
| `Tools/Art/` | Python generators and authoring notes for procedural art and audio |
| `Docs/` | Room-by-room survey evidence and the final polish notes (`Docs/FinalPolish/README.md`) |
| `SYSTEM_BOUNDS.md` | Hard invariants for the build, assets, lighting and core execution |
| `AGENTS.md` | Contribution rules and document ownership |
| `Project_Specification.pdf`, `Project_Assessment.pdf` | The course brief and marking memo |
| `CoreBuster_Project_Overview.pdf`, `COREBUSTERS CONCEPTS.pdf`, `CoreBuster.pptx` | Original design brief, concept art and presentation |

---

## AI disclosure

AI tools, including large language model assistants, were used during this project to help with development, debugging and documentation. The game's concept, design direction and final decisions were the team's own, and the team is responsible for everything submitted.

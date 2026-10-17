# Tutorial wing (corebuster_tutorial_001)

The wing south-west of the corridors (X -261..-231, Z -42..0 in `MainScene`) is where the game now starts. It works as both a tutorial and the asset showcase for the presentation. `Tools/CoreBuster/Build Tutorial Wing` (`Assets/FPS/Scripts/AI/Editor/TutorialWingBuilder.cs`) builds everything under the scene root `===== TUTORIAL WING =====` and rebuilds it from scratch on each run. Generated art goes to `Assets/FPS/Art/TutorialWing/`.

## Player flow

1. **Officer quarters (X -249..-231, Z -42..-24).** The player wakes beside the hibernation pod on the east wall, facing north. Their eyelids open, the pod's canopy swings up and ship systems speak.
   - The ship AI speaks a voiced status report as they come round, with subtitles: revival complete, hostile units detected aboard, life support stable, main power offline with the backup generators running, then the prompt to stretch. Voiced lines are never cut off. Lines from a later step, or interjections, queue behind them.
   - The player starts unarmed, with 25 health (100 minus the two drill executions' rewards, 35 + 40) and 2/3 cores in the Disassembler.
   - The stretch checklist is look, walk, crouch (Ctrl) and jump.
   - The blaster hangs in the locker on the north wall. Holding E takes it, and the quarters door then unseals.
2. **Fabrication gallery (west strip).** Nine podiums, 2.75 m apart (z -36 to -14, ending south of the combat drill's hazard tape), run along the west wall behind stanchions and velvet ropes, south to north:
   - caged wall lamps (the red jelly-jar fitting beside the amber cage lamp that now lights the corridors; `Tools/CoreBuster/Rebuild Tutorial Lamp Exhibit` rebuilds just this podium), Unstable Orb, Galaxy Husk, nebula skybox globe, Galaxy Gas, then the four weapons side by side: piston fist, Disassembler (with Electric Arc), Scrap Scattergun and the Cyber Soldiers' Rivet Cannon (`Weapon_RivetCannon`, built from primitives by `RivetCannonBuilder`);
   - `Tools/CoreBuster/Rebuild Gallery Exhibit Line` rebuilds just the podiums, exhibits, plaques, ropes, carpet and showcase spots from `GalleryExhibits()`, loading the existing materials instead of rewriting them, so an exhibit can be added without a full wing rebuild;
   - the piston-fist sentry NPC (`Enemy_PistonFist`, a visual-only copy) stands on a plinth at the south end;
   - the menu shaders (UI Galaxy and UI Core Glow) play on a wall screen opposite.
   - Every plaque opens with its category, which also colours its glow bar: **NPC** (red), **ENVIRONMENT** (green), **SHADER** (violet) or **WEAPON** (amber). The hibernation pod's plaque in the quarters is ENVIRONMENT.
   - Each showcase has its own spot straight above it on the light track, with a faint beam cone down to the exhibit.
3. **Combat drill (north end).** Three dormant targets stand 4.5 m out from the north wall, so an executed target doubles over without clipping into it: the piston-fist drone (shielded, display only), an armoured Cyber Soldier and an Elite. They carry `ShowcaseDummy`, so they never notice, move or attack.
   - Coming within 12.5 m starts the drill card: fire, then aim down the sights.
   - The armour is taught passively, by the holograms over the targets and a ship systems line on the first hit.
   - Breaking the armour leads to a Core Bust prompt, then the healing line.
   - Next the player fires the Disassembler (F) at the Elite. Its hatch is held open until it is executed, and a missed shot is refunded.
4. **Exit (east wall).** The clearance terminal right of the exit door issues the black keycard. Tapping it on the reader opens the exit. The same card opens the Workshop's door (`Spawn Room Door (N)`, now sealed at start).

`TutorialDirector` advances on what the player has actually done, so doing things early or out of order does not stall it. `TutorialHUD` draws the eyelids, the task card (upper right) and the subtitles (bottom centre).

## Sky, dressing and lighting (corebuster_tutorial_002)

- **Sky openings.** Five `Ceiling Closed` tiles become the kit's `Ceiling Skylight` and seven `Wall Plain` panels become `Wall Window`. Five windows are on the west wall behind the gallery and the drill, one is in the quarters' south wall and one is above the pod. The nebula sky shows through them, and the low western sun throws patches across the floor. `k_Skylights` and `k_Windows` list them by centre.
- **Dressing.** Kit props fill bare walls and floors, sorted under `Dressing`:
  - Quarters: a bed, a rug with a glass table and two chairs, a skirting trim and caged wall lights.
  - Gallery: a notice board, a bench facing the exhibits, a corner crate and high ducting.
  - North hall: braced panels behind the drill, ducting, pallets, crates, barrels and a cart, a terminal wall panel and notice board, a computer station, a cargo platform under the skylight and a security camera.
- **Lighting.** The pipeline gives each object four additional lights (`SYSTEM_BOUNDS.md`), so the wing uses a few strong lights rather than many weak ones:
  - the showcase spots are *Important*, so each exhibit keeps its own spot;
  - two high gallery fills and a drill fill;
  - sconces at the wall lights;
  - brighter hanging lamps.
- **Post volume.** A local `Volume` covering the wing (`TW_PostVolume.asset`: post exposure +0.75, contrast +8) lifts the dim kit shell, as the Workshop's volume does. It has no effect outside the wing.
- **Verified 2026-10-07** with edit-mode captures of every area, plus a scripted Play-mode execution of the armoured target and the Elite at their new positions:
  - both sequences completed;
  - the victims' meshes reached at most z = -2.07, about 1.8 m short of the wall panels (z = -0.25);
  - no errors.

## What the builder changes outside its root

- **Sky openings.** The 12 replaced shell pieces are only deactivated. The builder finds them by their pivot frame, so a rebuild still works once they are inactive. Re-activate them to undo the openings.
- **Shell materials.** The wing's shell pieces (`Wall Plain`, `Floor Tile 01`, `Ceiling Closed`, `Wall Door 02` whose bounds lie inside the wing) and the pack props it places get lit URP copies under `Assets/FPS/Art/TutorialWing/Materials/Pack/`. It uses the same conversion as the Workshop (`WorkshopMaterialUpgrade.Upgrade`) and the same metallic calibration. Pack materials themselves are never edited.
- **Doorway collision.** The two `Wall Door 02` pieces have no colliders, so `Shell Collision` adds jamb and lintel boxes around their frames.
- **Workshop door.** `Spawn Room Door (N)` gets `StartLocked = true`, plus an access machine for the black card under `Workshop Access`. `KeycardProgressionBuilder` now also starts that door locked. Re-run this builder after `Install Keycard Progression`, which recreates its doors. That re-run also breaks the Finale's references, so `Build Finale` must be re-run after it too.
- **Player.** The Player is moved to (-234.67, 0, -31.9), facing north.

## Runtime pieces

- `Gameplay/Tutorial/`: `TutorialDirector`, `WeaponLocker` (a hold-to-use `KeycardInteractable`), `KeycardDispenser` and `HibernationPod`.
- `AI/ShowcaseDummy`.
- `UI/TutorialHUD`.

Shared changes:

- `KeycardInteractable.HoldDuration`, with hold tracking in `KeycardInteractor` and a hold bar in `KeycardPrompt`.
- `CoreExecutionTarget.HoldForcedExposure`, which the tutorial sets on its Elite only.
- `ObjectiveKeycards` now counts only the Master terminal's cards, so the black card doesn't read as "1/3".

The tutorial only runs if the Player starts inside the wing. Placed elsewhere, the director disables itself and the starting weapons are left alone.

## First-principles assets added

`TutorialWingArt.cs` generates these from code:

- Meshes:
  - the hibernation pod: base, tilted tub, end rings, light strips, glass canopy and console;
  - the octagonal podium, stanchion, velvet rope, weapon rack, plaque lectern, light rail and spot heads, the showcase beam cone and the tesla electrodes.
- Textures:
  - the pod's vitals screen, the hazard stripe and the beam falloff;
  - the black keycard, a recolour of the blue floppy texture and icon.
- Sounds: tutorial sounds synthesised through `CoreExecutionBuilder.SynthClip`.
- Ship AI voice (`Assets/FPS/Audio/Voice/ShipAI/VO_ShipAI_WakeUp_01..05.wav`), made by `Tools/Art/generate_ship_ai_voice.py`:
  - Windows' built-in text-to-speech (`Microsoft Zira Desktop`) speaks each line.
  - The script's own DSP chain makes it the ship AI: an intercom band-pass with a presence lift, a metallic comb resonance, a faint ring modulation, soft saturation and a synthetic corridor reverb.
  - Rerun it on Windows from the project root after changing a line. Keep its `LINES` in step with `TutorialDirector.WakeUpLines`, which holds the subtitles. `Build Tutorial Wing` attaches the clips by file name, in order.

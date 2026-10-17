# Boss Hall lower arena cover review (corebuster_env_052)

Agent review of `ReviewCaptures/BossHall/052/` (2026-10-05, Unity 6000.5.7f1). `Saved/` uses the saved scene lighting; `Neutral/` adds unsaved white review lamps; `*_Without.png` hides only the two housings.

## What reads well

- **Housing detail (`Neutral/Housing_North_Detail`, `Housing_North_Front`).** Two riveted blast-plate cabinets with armour caps, lifting lugs and vent ribs sit either side of a recessed red-oxide coolant block, which carries a louvred front, a cross header with a yellow handwheel valve and a flanged riser stub. Hazard-yellow corner guards and the kick strip mark the hall-facing edges. It reads as heavy service plant, not a crate or a terminal.
- **Placement (`Neutral/Housing_South_Oblique`).** Each housing stands against its wall under the gallery, centred between the env_050 coolant bank and switchgear. The under-gallery lane stays wide, and the gallery posts remain the nearest objects in front, 5.4 m away on the north side.
- **Cover read (`Saved/Crouch_At_North_End`).** From a crouch beside a housing end, its side face screens the lane toward the far end, and the central arena stays fully visible.

## Limits

- **Saved lighting is dark.** Under the galleries the housings are close to black until env_055 lights the hall.
- **Little visible from the entry or across the arena.** The housings sit under the galleries, so the gallery decks and posts screen them from the entry, `Arena_*` and gallery views (`Gallery_To_North_Housing`). They add edge structure along the under-gallery lanes, not a skyline from the entry. This follows the limited, edge-oriented scope; it is not a large-composition gain.
- **Overexposed neutral views.** The neutral lamps overexpose some close views; judge material colour from the detail view only.
- **Incline view.** `Incline_Reveal` changes 43,107 pixels between captures, all on the foreground step grating (animated sky) plus a few skylight specks, as in env_050 and art_006 (`Neighbours/Diff/Incline_Reveal.png`). The housings are not visible from the incline.

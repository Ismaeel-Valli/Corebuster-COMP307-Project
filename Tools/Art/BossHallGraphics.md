# Boss Hall graphics — corebuster_env_057

`generate_boss_hall_graphics.py` synthesizes a single 2048 × 512 RGB texture under
`Assets/FPS/Art/BossHall/Wayfinding/`. Its lettering comes from `stencil_font.py`:
hand-authored strokes and stencil bridges, without an external font. Flat colours,
rectangles and diagonal cuts supply the printed safety border. No stock image,
third-party artwork or AI image is used. Python with Pillow is required only to
regenerate the committed PNG; fresh Unity clones use the saved asset directly.

Run `python Tools/Art/generate_boss_hall_graphics.py`. The approved PNG SHA-256 is
`33328addf006ef85f546d49135ce33d011303f0c4df296a7619d41829cffa8c2`.
Regeneration is deterministic. Preserve its Unity-generated `.meta` file.

The exact wording is **FINAL ASSEMBLY**. It identifies the existing assembly
machinery as the facility endpoint. Yellow edge stripes frame equipment safety;
they do not define a damaging area. There are no arrows, interaction prompts,
live status assertions, named boss labels, access credentials or escape claims.
This static label does not invite interaction with decorative consoles.

`BossHallGraphics` owns only `Level/Boss_Hall_Dressing/Wayfinding`. It resolves
`BossHallReveal.SignReservations` against current backing collision, using
`Sign_Final_Assembly`: 4 × 1 m on the cradle front, facing the entrance (+X).
Unity's built-in Quad faces local −Z; `LookRotation(-normal)` preserves readable
left-to-right UVs. A single flattened opaque URP/Unlit material deliberately keeps
printed contrast under the saved lighting. It emits no light. UVs are the built-in
Quad's full 0–1 mapping; material scale/offset persist as (1,1)/(0,0). No property
block, collision, script, carve, shadow, reflection or light probe is added.

One material uses the last available slot (whole hall 20/20), shared by three
identity plates. The two gallery plates use the same wording and UVs. Their
wall patches are measured from real collision above first-tier walking headroom,
with flat backing at all corners, an empty front envelope and ordinary keep-out
checks. No upper-platform or floor-route graphics are added. The initial
cradle-only review failed gallery readability; the gallery-facing copies resolve
that independently of the entrance frame. Nearby floor, direct gallery and
along-gallery views evaluate readability. At the distant threshold the cradle
plate establishes hierarchy; its words are not promised readable across the arena.

Authoring: **Tools/CoreBuster/Build Boss Hall Graphics** is undoable and unsaved.
`BossHallGraphics.BuildBatch` uses an exclusive Editor with graphics, rebuilds
only Wayfinding, verifies guards/Undo/persistence/isolation and saves MainScene.
`ValidateBatch` and `BossHallGraphicsReview.CaptureBatch` read saved state.
The framework checks exact installed graphic bindings and material isolation.
Only Wayfinding renderers are excluded from the reveal's occupancy search so
the installed plate can occupy its own reservation; all other scenery remains
protected and the installed Wayfinding validator rejects extra objects.

Evidence and outstanding review are recorded in `Docs/BossHall/Review_057.md`.

# Centre Room survey evidence

`Measurement_022.json` is the accepted Unity Editor measurement snapshot for `corebuster_env_022`. `Layout_022.svg` is a plan drawn from it, with north (+Z) up. The full room specification and remaining manual review are in the local `CENTRE_ROOM.md` and `PLAN.md` (both excluded by the current repository ignore rules).

The JSON records the source scene's SHA-256, Unity version, world-space shell bounds, fixed-collider and visible gate openings, terminal access, all eight spawn references, existing occupants, wave composition, navigation samples and path corners. Object IDs refer to the saved `MainScene`; a subsequent scene save may require a fresh survey. `onNavMesh = false` means `navPosition` is only a finite placeholder copied from `position`, not a successful projection.

Gate `clearMin` / `clearMax` and `visibleOpenMin` / `visibleOpenMax` are offsets in metres along that gate's `along` vector from its `position`. The visible aperture includes fixed shell/frame triangles and the leaves translated to their configured fully-open positions. The live scene is not moved. `lintelY` and `visibleLintelY` are absolute world heights. The wider collision opening defines the conservative approach box.

Reservations are layout targets, not new scene objects. `ringBodyBlocked` counts static capsule overlaps; `ringActorOverlaps` counts overlaps with mobile agents separately. Partial across-gate paths describe the saved locked gates. This snapshot does not certify unlocked runtime connectivity, a player walk, or wave AI behaviour.

To regenerate:

1. Open a clean saved `Assets/FPS/Scenes/MainScene.unity` alone in Unity 6000.5.7f1.
2. Run `Tools/CoreBuster/Measure Centre Room` (`CentreRoomSurvey.MeasureBatch` for command-line use).
3. Review `ReviewCaptures/CentreRoom/022/Measurement.json`, then copy the accepted snapshot here as `Measurement_022.json`.
4. Run `python Tools/Art/render_centre_room_survey.py Docs/CentreRoom/Measurement_022.json Docs/CentreRoom/Layout_022.svg`.

`Tools/CoreBuster/Capture Centre Room Survey` creates five review images in the ignored capture directory. Neither command saves scene assets or rebakes navigation.

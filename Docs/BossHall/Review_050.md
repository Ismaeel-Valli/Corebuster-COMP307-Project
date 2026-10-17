# Services visual review - 2026-10-04

The agent inspected the approved `target_design/boss_room.png` alongside the corebuster_env_050 captures. All captures use the saved spatial layout. `Neutral/` adds unsaved white lamps under both galleries, at the upper trays and in the hall centre; `Saved/` keeps the scene's actual lighting. No review light, material or renderer change persists.

## Under the galleries (floor)

On each side, a switchgear group and a booster generator stand against the wall under the gallery. The switchgear is a hazard-yellow Room 3 plinth carrying three control cabinets with doors and a transformer with bushings. The generator reuses the Room 3 turbine drum, end bells and bearing pedestals on a base frame. Conduits rise from the cabinet tops, the generator terminal box and the bushings, then bend into chase plates on the wall.

A red-oxide coolant main with brass flanges, clamp brackets and an inline brass valve runs from an east-wall collar along the wall above the equipment. It ends in a bend into the wall chase. Three P-3 accumulators with brass bands stand on a skid and feed the main through formed nozzles.

Everything stays in the measured strip against the wall. The lower centre and the under-gallery lane remain open.

## Gallery band (tier 1)

- **Power.** Above the walking headroom, each power chase shows wall plates, three conduits and a wall-hung Room 3 junction cabinet.
- **Cooling.** The coolant line re-emerges from the wall with a brass collar and rises to Y 16.35. It runs to the corner on knee brackets, then turns along the west wall to a red-oxide header with a brass seat on the backdrop lintel.

## Upper band and overhead

On each side wall, a cable tray on wall brackets and knee braces runs from a Room 3 intake head on the east wall to the north-west or south-west corner. It continues along the west wall to a junction box bolted to the gantry corbel's outer face.

Two three-cable feeders droop from east-wall junction boxes across the hall. They hang from the ceiling at third points and end in conductor boxes on the gantry runway ends.

From the entry, the feeders, trays and both lower networks frame the turret and backdrop without crossing the focal axis.

## Story read

Power and coolant visibly arrive through the east wall, the Room 3 and Reactor side. They are then regulated and stored along the side walls, and terminate on the final assembly's lintel, corbels and crane runway. This is "everything you passed was feeding this" at service scale. It reuses the Room 3 turbine and electrical language without copying Room 3's machine rows.

## Known limits

- **Wall chases.** The contract reserves Y 16.68 to 19.30 across the whole hall, and the galleries meet the walls. Vertical continuity between the floor, gallery and upper bands is therefore shown by aligned wall chases (plates and collars at matching stations), not by exposed risers crossing the decks.
- **Saved lighting is dark.** The machinery reads dark under the scene's own lighting; env_055 owns final practical and focal lighting. The neutral lamps verify composition and connections, not final brightness.
- **Feeder shadows.** The feeders and upper-band parts cast no shadows, because the shell kit's 2D-shader walls and roof do not block shadow maps (`BUGS.md`).

## Boundary images

All six strict neighbour and backward views are pixel-identical, including Room 3 BossDoor. Forward views into the hall change as permitted. On the incline, the animated sky shows through the foreground grating, so no pixel-identical foreground claim is made there. `ReviewCaptures/BossHall/050/Neighbours/Report.txt` and the diff masks keep the raw counts.

## Evidence

- `ReviewCaptures/BossHall/050/Neutral/` and `Saved/`: `Entry`, `Landing_Forward`, `Arena_SouthEast`, `Arena_NorthEast`, `Under_North_Gallery`, `Under_South_Gallery`, `Switchgear_North`, `Coolant_Bank_South`, `Gallery_Power_Junction`, `Gallery_Cooling_Riser`, `West_Lintel_And_Corbel`, `Upper_North_Trunk`, `Upper_East_Intakes` and `Feeder_To_Runway`.
- `*_Without.png` pairs hide only the service renderers.
- The automated results are separate, in `Services_050.txt`, `SceneScope_050.txt` and `Navigation_050.txt`.

No live combat, human-input run, player build, target-hardware profiling, containment or win-ordering verification is claimed. The existing shell, stair and rail findings remain open.

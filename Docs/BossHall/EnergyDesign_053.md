# Contained final-assembly energy — corebuster_env_053

The approved language is worn machinery outside, impossible cosmos inside. MainScene and the accepted BossHallLayout contract own placement; `target_design/boss_room.png` supplies art direction only. The owner's 2026-10-05 encounter-prerequisite waiver applies solely to this task. Containment, win ordering, final lighting and later-task gates remain open.

`BossHallEnergy` owns `Energy_053` scene children of these machinery roots:

| Parent section/root | Energy | Parent owner |
| --- | --- | --- |
| Assembly_Backdrop/Assembly_Ring | Recessed violet/blue stator field | corebuster_env_049 |
| Mechanical_Arms/Assembly_Arms_South and North | Four small teal supply-box interfaces | corebuster_art_006 |
| Power_Services/Power_Feed_South and North | Two small blue junction-door interfaces | corebuster_env_050 |
| Overhead/Overhead_Feeder_South and North | Teal strips on two east intake faces | corebuster_env_050 |

Seven groups contain nine renderers and 504 instance triangles. The field stays within the ring's reserved aperture. Six angular gaps preserve the spokes, the inner hole preserves the hub and a radial gap preserves the steel torus. The service interfaces sit 8 mm off existing machinery faces. Arm children are scene overrides; no arm prefab or source material is edited.

Three flattened materials in `Assets/FPS/Art/BossHall/Energy/Materials/` reuse two unchanged custom shaders:

- Reactor Containment: violet body, blue filaments and restrained electric blue-white interior. Intensity 1.35, instability 0.3; live GPU time is retained in the saved material.
- Core Energy Additive: blue ring-shaped switchgear interfaces (intensity 1.4) and teal supply/intake indicators (intensity 1.15).

Core Window, Unstable Orb and Core Halo are intentionally unused: their iris/orb cue is reserved for executable enemies. Electric Arc and particles belong to the later visual meltdown pass. This energy adds no ambient emitter, light, audio or dynamic control.

Only Transform/MeshFilter/MeshRenderer components are allowed. Renderers have no shadows or probes. Exact mesh recipes, materials, palette/intensity, transforms, ownership and ordinary keep-outs are independently validated. There is no executable core, enemy/actor registration, health/damage, collider or carving obstacle.

The framework restores installed energy during populated parent rebuilds inside the parent's Undo transaction. Energy rebuilds snapshot all state outside the exact child groups, including machinery components and all other sections. Regeneration preserves assets and metadata. Build verification rebuilds the actual backdrop, arm and service parents, checks restoration, and reverses each test before saving the isolated energy additions.

Use the Build/Validate/Capture Boss Hall Contained Energy menus. Batch methods are `BossHallEnergy.PreflightBatch`, `BuildBatch`, `ValidateBatch`, `BossHallEnergyReview.CaptureBatch`, and `BossHallEnergyRuntime.RunBatch` (omit `-quit` for the Play-mode diagnostic). Reports alongside this file record actual outcomes; source presence alone is not verification. Neutral captures use unsaved review lamps. The runtime diagnostic is a controlled armour-break/execution regression, not a full encounter or human combat sign-off.

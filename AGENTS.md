# CoreBuster Agent Instructions

## Repository scope and conventions

- This Unity project root is the directory containing `Assets/`, `Packages/`, and `ProjectSettings/`.
- Use UTF-8 Markdown and repository-relative paths in project documentation.
- Preserve Unity `.meta` files with their assets. Do not rename, move, or recreate assets outside Unity unless the matching `.meta` file is handled correctly.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/`, and build output are generated local state and are ignored by Git. Do not hand-edit them or commit them.
- Treat prefabs, scenes, materials, and ScriptableObjects as Unity-authored YAML. Prefer the Unity Editor for structural changes and review their diffs carefully.

## Required reading

1. Read `CONTEXT.md` before meaningful work.
2. For structural or cross-system work, read `ARCHITECTURE.md` and `SYSTEM_BOUNDS.md`.
3. For a feature or bug task, read `PLAN.md`; for prior completions, read `DONE_TASKS.md`.
4. Before changing test coverage or validation, read `TESTING.md`.
5. Before debugging, read `BUGS.md`.
6. Before changing the custom core/execution flow, also read the Core Buster section of `ARCHITECTURE.md` and the execution invariants in `SYSTEM_BOUNDS.md`.

## Source-of-truth ownership

- `README.md` owns setup, running, build guidance, feature availability, and this document index.
- `ARCHITECTURE.md` owns durable topology, module ownership, scene flow, editor-generated asset boundaries, and runtime contracts.
- `CONTEXT.md` owns the current handoff and confirmed implementation snapshot.
- `PLAN.md` owns unfinished actionable work only; `DONE_TASKS.md` owns compressed completed history.
- `TESTING.md` owns test discovery and manual verification.
- `BUGS.md` owns reusable, understood bug lessons.
- `SYSTEM_BOUNDS.md` owns hard build, asset, and core-execution invariants.

## Planning and completion lifecycle

Use this lifecycle for substantial work:

`parking_lot -> planned -> active -> verified -> done_tasks`

- Add a substantial task to `PLAN.md` before implementation. Task IDs use `corebuster_<track>_<nnn>` (for example, `corebuster_ai_001`); never reuse or renumber published IDs.
- Give substantial tasks an outcome, scope, non-goals, dependencies, acceptance criteria, verification, and required documentation updates.
- When a task can be routed among multiple people or agents, name one primary owner/capability and reasoning level in its plan entry. Split separate design, implementation, and review ownership into linked task IDs.
- Completion gate: before reporting a planned task as complete, or beginning unrelated work after it, remove its full task block from `PLAN.md` and add a concise dated record to `DONE_TASKS.md`. The record must preserve the exact task ID, completion date, outcome, strongest verification, and any material limitation or follow-up task.
- A task that still needs verification is not complete. Keep it in `PLAN.md` with its remaining verification, or split a separately owned verification task before moving the implemented scope to `DONE_TASKS.md`.
- Before handoff, confirm that `PLAN.md` contains no completed-task history and that every completion record has a matching task ID and date in `DONE_TASKS.md`.
- Put intentionally deferred ideas in `PARKING_LOT.md` only when that file exists, including why they are deferred and what reopens them.

## Editing and verification discipline

- Check `git status --short` before and after work. Preserve unrelated changes. At this bootstrap, `.gitignore` and several `ProjectSettings/Packages/*.json` files already have user changes.
- Keep code changes scoped by assembly: `fps.Game` provides shared runtime primitives; `fps.Gameplay`, `fps.AI`, and `fps.UI` consume them. See `ARCHITECTURE.md` before adding cross-assembly dependencies.
- The `Assets/FPS/Scripts/AI/Editor/` tools create and overwrite committed prefabs, materials, VFX, and audio. Read their output paths and review all resulting diffs before running them. Do not run broad builder actions merely to refresh assets.
- Update the nearest source-of-truth document when behavior, scene configuration, build workflow, testing, or a durable boundary changes.
- Do not claim a feature works because source exists. Record Unity Play-mode/build verification in `TESTING.md` or a completion record.

## Generator tooling and version control

- The room/asset generator sources (`StartWorkshop*.cs`, `CentreRoom*.cs`, `NorthWestRoom*.cs`, `BossHall*.cs`, `WorkshopMaterialUpgrade.cs`, and `Tools/Art/*.py`) and the room reports in `Docs/` are shared source. Commit them with their Unity `.meta` files, together with the scenes and assets they generate, so every clone compiles and receives the same tools and playable game.
- Never add a `.gitignore` rule for a `.cs` file under `Assets/` or for a script another script references. An ignored script compiles on its author's machine but breaks or disappears on every fresh clone (commit `cfb3d35` fixed one such CS0103 break).
- Commit a script together with every new script it calls. Before pushing, check `git status --short --ignored` for `!!` entries under `Assets/`, and confirm that no tracked `.cs` file depends on an untracked one.
- The generators' `Tools/CoreBuster/Build ...` menu items rebuild and overwrite their room's committed scene content. Run only the builder for the room you own, and review the resulting scene/asset diffs before committing.

## Project-specific hard rules

- Preserve the Core Buster execution sequence: armour break exposes a target; the target is removed from the vulnerable pool while executing; consuming the core precedes killing the target. Details are in `SYSTEM_BOUNDS.md`.
- The configured build includes `IntroMenu`, `WinScene`, `LoseScene`, and `MainScene`. Update `ProjectSettings/EditorBuildSettings.asset`, scene-loading configuration, and documentation together when changing this contract.
- The course specification requires at least one custom ShaderLab/HLSL or GLSL shader and evidence of first-principles assets. Do not replace the custom core shader or custom asset evidence with third-party-only content without an explicit project decision.

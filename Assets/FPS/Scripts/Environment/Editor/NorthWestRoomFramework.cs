using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Owns the empty North-West Power Regulation Chamber dressing hierarchy and its measured placement contract.
/// It deliberately does not adopt the shell, doors, terminals, spawn markers, access machine, or navigation.
/// All layout data comes from NorthWestRoomSurvey (corebuster_env_029), before the scene is made dirty.
/// </summary>
public static class NorthWestRoomFramework
{
    public const string LevelName = "Level";
    public const string RootName = "NorthWest_Room";
    public const string RootPath = LevelName + "/" + RootName;
    public const string AssetRoot = "Assets/FPS/Art/NorthWestRoom";
    // Owner-approved corebuster_env_040 ceilings (2026-10-02); per-task shares are in NorthWestRoomDensityPlan.json.
    public const int PrefabBudget = 400, LightBudget = 24, CarvingObstacleBudget = 24, MaterialBudget = 26;

    public enum Section
    {
        TurbineMachinery, ElectricalSystems, PerimeterMachinery, FloorDetail, PressureSystems, WallServices, Overhead,
        Cover, MaintenanceStructures, Rendering, Lighting, Wayfinding,
    }

    public sealed class SectionSpec
    {
        public readonly Section section;
        public readonly string name, owner, assetFolder;
        public SectionSpec(Section section, string name, string owner)
        {
            this.section = section; this.name = name; this.owner = owner;
            assetFolder = AssetRoot + "/" + name;
        }
    }

    static readonly SectionSpec[] Specs =
    {
        new SectionSpec(Section.TurbineMachinery, "Turbine_Machinery", "corebuster_env_031"),
        new SectionSpec(Section.ElectricalSystems, "Electrical_Systems", "corebuster_env_032"),
        new SectionSpec(Section.PerimeterMachinery, "Perimeter_Machinery", "corebuster_env_041"),
        new SectionSpec(Section.FloorDetail, "Floor_Detail", "corebuster_env_042"),
        new SectionSpec(Section.PressureSystems, "Pressure_Systems", "corebuster_env_033"),
        new SectionSpec(Section.WallServices, "Wall_Services", "corebuster_env_033"),
        new SectionSpec(Section.Overhead, "Overhead", "corebuster_env_033"),
        new SectionSpec(Section.Cover, "Cover", "corebuster_env_034"),
        new SectionSpec(Section.MaintenanceStructures, "Maintenance_Structures", "corebuster_env_035"),
        new SectionSpec(Section.Rendering, "Rendering", "corebuster_env_036"),
        new SectionSpec(Section.Lighting, "Lighting", "corebuster_env_037"),
        new SectionSpec(Section.Wayfinding, "Wayfinding", "corebuster_env_038"),
    };

    public sealed class Layout
    {
        public readonly NorthWestRoomSurvey.Report report;
        public readonly Bounds interior;
        internal Layout(NorthWestRoomSurvey.Report report)
        {
            this.report = report ?? throw new ArgumentNullException(nameof(report));
            interior = ToBounds(report.interior);
        }
        public float Floor => report.floorTopY;
        public float MovementTop => report.movementBandTopY;

        /// <summary>Returns the first measured restriction a body-height item violates, or null.</summary>
        public string KeepOutViolation(Bounds bounds)
        {
            const float tolerance = .03f;
            if (bounds.min.x < interior.min.x - tolerance || bounds.max.x > interior.max.x + tolerance ||
                bounds.min.y < interior.min.y - tolerance || bounds.max.y > interior.max.y + tolerance ||
                bounds.min.z < interior.min.z - tolerance || bounds.max.z > interior.max.z + tolerance)
                return $"extends outside measured room volume {interior.min:F2} to {interior.max:F2}";

            // Services and overhead are only constrained by the measured volume. The survey's body-height
            // reservations are intentionally applied only in the movement band.
            if (bounds.min.y >= MovementTop) return null;
            foreach (var gate in report.gates)
                if (OverlapsXZ(bounds, ToBounds(gate.approach))) return $"overlaps {gate.name} approach reservation";
            if (OverlapsXZ(bounds, ToBounds(report.redTerminal.keepOut))) return "overlaps Red Terminal access or sightline reservation";
            if (OverlapsXZ(bounds, ToBounds(report.masterTerminal.keepOut))) return "overlaps Master Terminal access, slots, or camera reservation";
            foreach (var spawn in report.spawns)
                if (FlatDistance(spawn.position, bounds) < report.spawnRadius) return $"enters 2 m spawn reservation at {spawn.position:F2}";
            float near = FlatDistance(report.centre, bounds), far = FarthestCornerDistanceXZ(report.centre, bounds);
            if (near < report.ringOuterRadius && far > report.ringInnerRadius)
                return $"enters protected radius-{report.ringInnerRadius:F0}-to-{report.ringOuterRadius:F0} combat annulus";
            foreach (var route in report.routes.Where(r => r.connector && r.corners != null && r.corners.Length > 1))
                for (int i = 1; i < route.corners.Length; i++)
                    if (SegmentDistanceXZ(route.corners[i - 1], route.corners[i], bounds) < report.connectorHalfWidth)
                        return $"enters {report.connectorHalfWidth:F1} m connector '{route.name}'";
            return null;
        }

        public void ValidateBounds(Bounds bounds, string label, ICollection<string> errors)
        {
            var violation = KeepOutViolation(bounds);
            if (violation != null) errors.Add(label + " " + violation + ".");
        }
    }

    [MenuItem("Tools/CoreBuster/Build North-West Room Framework")]
    public static void BuildMenu()
    {
        try
        {
            var layout = MeasureLayout();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build North-West Room Framework");
            Build(layout);
            Undo.CollapseUndoOperations(undo);
            Selection.activeTransform = FindRoot(EditorSceneManager.GetActiveScene());
        }
        catch (Exception ex) { Debug.LogException(ex); throw; }
    }

    [MenuItem("Tools/CoreBuster/Validate North-West Room Framework")]
    public static bool ValidateMenu()
    {
        try { return Validate(EditorSceneManager.GetActiveScene(), MeasureLayout(), true); }
        catch (Exception ex) { Debug.LogError("NORTH-WEST ROOM FRAMEWORK CHECKS FAILED: " + ex.Message); return false; }
    }

    /// <summary>Batch authoring entry point: build, isolated Undo check, save/reload, and ownership snapshots.</summary>
    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = MeasureLayout();
        var before = CaptureCriticalTransforms(EditorSceneManager.GetActiveScene(), layout.report);
        var protectedMaterials = CaptureProtectedMaterialHashes();
        Build(layout);
        VerifyLayoutContract(layout);
        VerifyIsolatedRebuildAndUndo(layout);
        if (!Validate(EditorSceneManager.GetActiveScene(), layout, true))
            throw new InvalidOperationException("North-West Room framework validation failed before save.");
        VerifyProtectedMaterialHashes(protectedMaterials);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = MeasureLayout();
        if (!Validate(EditorSceneManager.GetActiveScene(), layout, true))
            throw new InvalidOperationException("North-West Room framework validation failed after reload.");
        CompareCriticalTransforms(before, CaptureCriticalTransforms(EditorSceneManager.GetActiveScene(), layout.report));
        Debug.Log($"NORTH-WEST ROOM FRAMEWORK BUILD VERIFIED: {RootPath}; {Specs.Length} persistent sections, isolated rebuild/Undo, measured layout checks, material ownership, save/reload, and shell/gameplay snapshots passed.");
    }

    /// <summary>Call while MainScene is clean, then pass the returned layout through a section builder.</summary>
    public static Layout MeasureLayout()
    {
        try { return new Layout(NorthWestRoomSurvey.Measure()); }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Cannot establish Room 3's contract from corebuster_env_029. Open a clean saved MainScene alone in Edit mode before measuring. " + ex.Message, ex);
        }
    }

    /// <summary>Creates only missing identity containers. Existing children and all gameplay ownership remain untouched.</summary>
    public static Transform Build(Layout layout)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        var scene = EditorSceneManager.GetActiveScene(); RequireMainScene(scene);
        var level = UniqueChild(scene.GetRootGameObjects().Select(g => g.transform), LevelName, "scene-root Level container");
        var root = UniqueDirectChild(level, RootName, false);
        if (!root)
        {
            var go = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(go, "Create North-West Room dressing root");
            Undo.SetTransformParent(go.transform, level, "Parent North-West Room dressing root");
            root = go.transform;
        }
        if (root.position != Vector3.zero || root.rotation != Quaternion.identity || root.lossyScale != Vector3.one)
        {
            if (root.GetComponentsInChildren<Renderer>(true).Length > 0 || root.GetComponentsInChildren<Collider>(true).Length > 0)
                throw new InvalidOperationException("The North-West Room root is not at world identity and already has dressing content; inspect it before moving the root.");
            Undo.RecordObject(root, "Place North-West Room dressing root at world identity");
            SetRootWorldIdentity(root, level);
        }
        foreach (var spec in Specs)
            if (!UniqueDirectChild(root, spec.name, false))
            {
                var go = new GameObject(spec.name);
                Undo.RegisterCreatedObjectUndo(go, "Create North-West Room " + spec.name + " section");
                Undo.SetTransformParent(go.transform, root, "Parent North-West Room " + spec.name + " section");
                ResetLocal(go.transform);
            }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!Validate(scene, layout, true)) throw new InvalidOperationException("Created framework did not validate; Undo and inspect the errors.");
        return root;
    }

    /// <summary>Clears exactly one section in the caller's Undo group; wrong task ownership is rejected.</summary>
    public static Transform BeginSectionRebuild(Layout layout, Section section, string ownerTaskId)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        var spec = Spec(section);
        if (!string.Equals(spec.owner, ownerTaskId, StringComparison.Ordinal))
            throw new InvalidOperationException($"{spec.name} is owned by {spec.owner}, not '{ownerTaskId}'.");
        var scene = EditorSceneManager.GetActiveScene(); RequireMainScene(scene);
        var root = FindRoot(scene) ?? throw new InvalidOperationException($"Missing {RootPath}; build the framework first.");
        var target = UniqueDirectChild(root, spec.name, true);
        foreach (Transform child in target.Cast<Transform>().ToArray()) Undo.DestroyObjectImmediate(child.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        return target;
    }

    /// <summary>True for anything under Level/NorthWest_Room/Rendering (the shell skins).</summary>
    public static bool IsRenderingSkin(Transform t)
    {
        string name = Spec(Section.Rendering).name;
        for (var p = t.parent; p; p = p.parent) if (p.name == name && p.parent && p.parent.name == RootName) return true;
        return false;
    }

    /// <summary>
    /// Only asset-backed, collider-free skins on the room boundary may cross floor gameplay reservations: every vertex is
    /// inside the measured volume and within half a metre of a wall, the floor or the ceiling.
    /// </summary>
    public static bool IsBoundarySkin(Renderer r, Layout layout)
    {
        if (!r || r.GetComponent<Collider>() || !IsRenderingSkin(r.transform)) return false;
        var mf = r.GetComponent<MeshFilter>();
        if (!mf || !mf.sharedMesh || !AssetDatabase.GetAssetPath(mf.sharedMesh).Replace('\\', '/').StartsWith(AssetFolder(Section.Rendering) + "/Shell/", StringComparison.Ordinal)) return false;
        Bounds b = layout.interior;
        b.Expand(.01f);
        foreach (var vertex in mf.sharedMesh.vertices)
        {
            Vector3 p = r.transform.TransformPoint(vertex);
            if (!b.Contains(p)) return false;
            float edge = Mathf.Min(p.x - b.min.x, b.max.x - p.x, p.y - b.min.y, b.max.y - p.y, p.z - b.min.z, b.max.z - p.z);
            if (edge > .5f) return false;
        }
        return true;
    }

    public static Transform FindSection(Scene scene, Section section)
    {
        var root = FindRoot(scene); return root ? UniqueDirectChild(root, Spec(section).name, true) : null;
    }
    public static SectionSpec Spec(Section section) => Specs.SingleOrDefault(s => s.section == section) ??
        throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown North-West Room section.");
    public static string AssetFolder(Section section) => Spec(section).assetFolder;

    /// <summary>Generated room assets must be isolated under their section's Room 3 folder.</summary>
    public static void RequireOwnedAsset(Object asset, Section section)
    {
        if (!asset) throw new ArgumentNullException(nameof(asset));
        string path = AssetDatabase.GetAssetPath(asset).Replace('\\', '/');
        if (!path.StartsWith(AssetFolder(section) + "/", StringComparison.Ordinal))
            throw new InvalidOperationException($"Generated {asset.GetType().Name} '{asset.name}' must be below {AssetFolder(section)}/; actual path '{path}'.");
    }

    /// <summary>
    /// Snapshot third-party material files before a builder runs and verify immediately after it saves. This catches
    /// the harmful in-place shared-pack material edits that scene material assignment alone cannot reveal.
    /// </summary>
    public static Dictionary<string, string> CaptureProtectedMaterialHashes()
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var guid in AssetDatabase.FindAssets("t:Material"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (IsThirdPartyMaterial(path) && File.Exists(path)) hashes[path] = HashFile(path);
        }
        return hashes;
    }

    public static void VerifyProtectedMaterialHashes(IReadOnlyDictionary<string, string> before)
    {
        var changed = before.Where(p => !File.Exists(p.Key) || HashFile(p.Key) != p.Value).Select(p => p.Key).ToArray();
        if (changed.Length > 0) throw new InvalidOperationException("Shared third-party material asset(s) changed: " + string.Join(", ", changed));
    }

    public static bool Validate(Scene scene, Layout layout, bool log)
    {
        RequireMainScene(scene);
        var errors = new List<string>();
        var level = TryUnique(scene.GetRootGameObjects().Select(g => g.transform), LevelName, "scene-root Level container", errors);
        var root = level ? TryUnique(level.Cast<Transform>(), RootName, RootPath, errors) : null;
        if (root)
        {
            ValidateRoot(root, errors);
            var expected = new HashSet<string>(Specs.Select(s => s.name), StringComparer.Ordinal);
            foreach (Transform child in root) if (!expected.Contains(child.name)) errors.Add($"Unexpected direct child {PathOf(child)}; every pass needs an owned section.");
            foreach (var spec in Specs)
            {
                var section = TryUnique(root.Cast<Transform>(), spec.name, RootPath + "/" + spec.name, errors);
                if (!section) continue;
                ValidateContainer(section, PathOf(section), errors);
                ValidateSectionContent(section, layout, errors);
            }
            ValidateGameplayOwnership(scene, root, layout.report, errors);
            ValidateMaterialOwnership(scene, root, errors);
            ValidateBudget(root, errors);
        }
        if (errors.Count == 0)
        {
            if (log) Debug.Log($"NORTH-WEST ROOM FRAMEWORK CHECKS PASSED: {Specs.Length} owned sections; measured Room 3 reservations and material ownership are valid. {BudgetSummary(root)}");
            return true;
        }
        if (log) Debug.LogError("NORTH-WEST ROOM FRAMEWORK CHECKS FAILED (" + errors.Count + "):\n  " + string.Join("\n  ", errors));
        return false;
    }

    /// <summary>Lets a later builder check its own section before saving, while retaining room-wide material guards.</summary>
    public static bool ValidateSection(Scene scene, Layout layout, Section section, bool log)
    {
        RequireMainScene(scene);
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        var root = FindRoot(scene) ?? throw new InvalidOperationException("Missing " + RootPath);
        var target = UniqueDirectChild(root, Spec(section).name, true);
        var errors = new List<string>();
        ValidateContainer(target, PathOf(target), errors);
        ValidateSectionContent(target, layout, errors);
        ValidateMaterialOwnership(scene, root, errors);
        ValidateBudget(root, errors);
        if (log) Debug.Log((errors.Count == 0 ? "NORTH-WEST ROOM SECTION CHECKS PASSED: " : "NORTH-WEST ROOM SECTION CHECKS FAILED: ") +
            Spec(section).name + ". " + BudgetSummary(root) + (errors.Count == 0 ? "" : "\n  " + string.Join("\n  ", errors)));
        return errors.Count == 0;
    }

    static void ValidateSectionContent(Transform section, Layout layout, List<string> errors)
    {
        // Floor_Detail holds collider-free flat overlays (corebuster_env_042). Body-height keep-outs do not apply to them
        // (the progression-lane chevrons must lie in a connector); NorthWestRoomFloor validates them against its own rules.
        bool flat = section.name == Spec(Section.FloorDetail).name;
        // Rendering holds collider-free inward shell skins (corebuster_env_036). They lie on the room boundary, so only the
        // boundary-skin rule applies; body-height keep-outs describe movement-band content, not a finish on the shell.
        bool skins = section.name == Spec(Section.Rendering).name;
        foreach (var renderer in section.GetComponentsInChildren<Renderer>(true))
            if (!renderer.enabled) continue;
            else if (skins) { if (!IsBoundarySkin(renderer, layout)) errors.Add(PathOf(renderer.transform) + " is not a collider-free asset-backed boundary skin."); }
            else if (!flat) layout.ValidateBounds(renderer.bounds, PathOf(renderer.transform), errors);
            else if (renderer.bounds.size.y > .1f || !layout.interior.Contains(renderer.bounds.min) || !layout.interior.Contains(renderer.bounds.max))
                errors.Add(PathOf(renderer.transform) + " is not a flat overlay inside the measured room volume.");
        foreach (var collider in section.GetComponentsInChildren<Collider>(true))
            if (flat) errors.Add(PathOf(collider.transform) + " has a collider; Floor_Detail overlays must have none.");
            else if (skins) errors.Add(PathOf(collider.transform) + " has a collider; Rendering skins must have none.");
            else if (collider.enabled && !collider.isTrigger) layout.ValidateBounds(collider.bounds, PathOf(collider.transform), errors);
        foreach (var light in section.GetComponentsInChildren<Light>(true))
            if (light.enabled && !Contains(layout.interior, light.transform.position)) errors.Add(PathOf(light.transform) + " light origin is outside measured room volume.");
        foreach (var target in section.GetComponentsInChildren<CoreExecutionTarget>(true)) errors.Add(PathOf(target.transform) + " has CoreExecutionTarget; decorative room content cannot be executable.");
        foreach (var obstacle in section.GetComponentsInChildren<NavMeshObstacle>(true))
            if (obstacle.enabled && !obstacle.carving) errors.Add(PathOf(obstacle.transform) + " has enabled non-carving NavMeshObstacle.");
    }

    static void ValidateGameplayOwnership(Scene scene, Transform root, NorthWestRoomSurvey.Report report, List<string> errors)
    {
        var red = All<KeycardTerminal>(scene).Where(t => t.name == NorthWestRoomSurvey.RedTerminalName).ToArray();
        var master = All<MasterTerminal>(scene).Where(t => t.name == NorthWestRoomSurvey.MasterTerminalName).ToArray();
        var doors = new[] { NorthWestRoomSurvey.EastDoorName, NorthWestRoomSurvey.BossDoorName };
        if (red.Length != 1) errors.Add("Expected exactly one North-West Room Red Terminal.");
        else { CheckOutside(root, red[0].transform, "Red Terminal", errors); foreach (var s in red[0].SpawnPoints.Where(s => s)) CheckOutside(root, s, "spawn marker", errors); }
        if (master.Length != 1) errors.Add("Expected exactly one Master Terminal."); else CheckOutside(root, master[0].transform, "Master Terminal", errors);
        foreach (var door in All<SecurityDoor>(scene).Where(d => doors.Contains(d.name))) CheckOutside(root, door.transform, "security door", errors);
        if (GlobalObjectId.TryParse(report.accessMachine.id, out var accessId) &&
            GlobalObjectId.GlobalObjectIdentifierToObjectSlow(accessId) is Component access)
            CheckOutside(root, access.transform, "access machine", errors);
        foreach (var shell in report.shell)
            if (GlobalObjectId.TryParse(shell.id, out var id) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is Component c) CheckOutside(root, c.transform, "original shell", errors);
    }

    static void ValidateMaterialOwnership(Scene scene, Transform root, List<string> errors)
    {
        // Catch an in-memory pack edit before saving; BuildBatch's hash comparison catches persisted edits.
        foreach (var guid in AssetDatabase.FindAssets("t:Material"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (IsThirdPartyMaterial(path) && EditorUtility.IsDirty(AssetDatabase.LoadAssetAtPath<Material>(path)))
                errors.Add("Shared third-party material has unsaved edits: '" + path + "'.");
        }
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials.Where(m => m))
            {
                string path = AssetDatabase.GetAssetPath(material).Replace('\\', '/');
                if (path.StartsWith("Assets/FPS/Art/Workshop/", StringComparison.Ordinal) ||
                    path.StartsWith("Assets/FPS/Art/Materials/Workshop/", StringComparison.Ordinal) ||
                    path.StartsWith("Assets/FPS/Art/CentreRoom/", StringComparison.Ordinal))
                    errors.Add($"{PathOf(renderer.transform)} uses another room's owned material '{path}'.");
            }
        foreach (var renderer in All<Renderer>(scene).Where(r => r && !r.transform.IsChildOf(root)))
            foreach (var material in renderer.sharedMaterials.Where(m => m))
                if (AssetDatabase.GetAssetPath(material).Replace('\\', '/').StartsWith(AssetRoot + "/", StringComparison.Ordinal))
                    errors.Add($"{PathOf(renderer.transform)} outside Room 3 uses room-owned material '{material.name}'.");
    }

    static void ValidateBudget(Transform root, List<string> errors)
    {
        var counts = CountBudget(root);
        if (counts.prefabs > PrefabBudget) errors.Add($"Room 3 uses {counts.prefabs}/{PrefabBudget} prefab instances.");
        if (counts.lights > LightBudget) errors.Add($"Room 3 uses {counts.lights}/{LightBudget} enabled lights.");
        if (counts.obstacles > CarvingObstacleBudget) errors.Add($"Room 3 uses {counts.obstacles}/{CarvingObstacleBudget} enabled carving obstacles.");
        if (counts.materials > MaterialBudget) errors.Add($"Room 3 uses {counts.materials}/{MaterialBudget} materials.");
    }

    static (int prefabs, int lights, int obstacles, int materials) CountBudget(Transform root) =>
        (root.GetComponentsInChildren<Transform>(true).Count(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)),
         root.GetComponentsInChildren<Light>(true).Count(l => l.enabled),
         root.GetComponentsInChildren<NavMeshObstacle>(true).Count(o => o.enabled && o.carving),
         root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().Count());

    static string BudgetSummary(Transform root)
    {
        var counts = CountBudget(root);
        return $"Budgets: prefabs {counts.prefabs}/{PrefabBudget}, lights {counts.lights}/{LightBudget}, carving obstacles {counts.obstacles}/{CarvingObstacleBudget}, materials {counts.materials}/{MaterialBudget}.";
    }

    static void VerifyIsolatedRebuildAndUndo(Layout layout)
    {
        var scene = EditorSceneManager.GetActiveScene();
        var turbine = FindSection(scene, Section.TurbineMachinery); var cover = FindSection(scene, Section.Cover);
        GameObject a = new GameObject("__Turbine_Rebuild_Verification"), b = new GameObject("__Cover_Isolation_Verification");
        string aName = a.name, bName = b.name;
        a.transform.SetParent(turbine, false); b.transform.SetParent(cover, false);
        try
        {
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Verify isolated North-West Room rebuild");
            BeginSectionRebuild(layout, Section.TurbineMachinery, Spec(Section.TurbineMachinery).owner); Undo.CollapseUndoOperations(undo);
            if (turbine.Find(aName) || !cover.Find(bName)) throw new InvalidOperationException("Isolated turbine rebuild changed another Room 3 section.");
            Undo.PerformUndo();
            if (!turbine.Find(aName) || !cover.Find(bName)) throw new InvalidOperationException("Undo did not restore isolated Room 3 section rebuild.");
        }
        finally
        {
            var restored = turbine.Find(aName);
            if (restored) Object.DestroyImmediate(restored.gameObject);
            if (b) Object.DestroyImmediate(b);
        }
    }

    static void VerifyLayoutContract(Layout layout)
    {
        var allowed = new Bounds(new Vector3(layout.interior.min.x + 1.5f, layout.Floor + .5f, layout.interior.min.z + 1.5f), Vector3.one);
        if (layout.KeepOutViolation(allowed) != null) throw new InvalidOperationException("Measured Room 3 layout rejected a representative south-west perimeter item.");
        var outside = new Bounds(new Vector3(layout.interior.max.x + 1, layout.Floor + .5f, layout.report.centre.z), Vector3.one);
        AssertViolationContains(layout, outside, "outside measured room volume");
        foreach (var gate in layout.report.gates)
            AssertViolationContains(layout, BodyAt(ToBounds(gate.approach).center, layout.Floor), gate.name + " approach");
        AssertViolationContains(layout, BodyAt(ToBounds(layout.report.redTerminal.keepOut).center, layout.Floor), "Red Terminal");
        AssertViolationContains(layout, BodyAt(ToBounds(layout.report.masterTerminal.keepOut).center, layout.Floor), "Master Terminal");
        var annulus = new Bounds(layout.report.centre + Vector3.right * 6 + Vector3.up * .5f, Vector3.one);
        if (layout.KeepOutViolation(annulus) == null) throw new InvalidOperationException("Measured Room 3 layout accepted body-height content in combat annulus.");
        var spawn = new Bounds(layout.report.spawns[0].position + Vector3.up * .5f, Vector3.one);
        if (layout.KeepOutViolation(spawn) == null) throw new InvalidOperationException("Measured Room 3 layout accepted body-height content in spawn reservation.");
        bool wrongOwner = false; try { BeginSectionRebuild(layout, Section.Cover, Spec(Section.TurbineMachinery).owner); } catch (InvalidOperationException) { wrongOwner = true; }
        if (!wrongOwner) throw new InvalidOperationException("Framework accepted a wrong section owner.");
    }

    static Bounds BodyAt(Vector3 point, float floor) => new Bounds(new Vector3(point.x, floor + .5f, point.z), Vector3.one * .5f);
    static void AssertViolationContains(Layout layout, Bounds bounds, string expected)
    {
        string violation = layout.KeepOutViolation(bounds);
        if (violation == null || !violation.Contains(expected))
            throw new InvalidOperationException($"Measured Room 3 layout did not reject '{expected}' as expected; result was '{violation}'.");
    }

    sealed class TransformState { public string label, parent; public Vector3 position, scale; public Quaternion rotation; }
    static Dictionary<string, TransformState> CaptureCriticalTransforms(Scene scene, NorthWestRoomSurvey.Report report)
    {
        var values = new HashSet<Transform>();
        foreach (var terminal in All<KeycardTerminal>(scene).Where(t => t.name == NorthWestRoomSurvey.RedTerminalName)) { AddHierarchy(terminal.transform, values); foreach (var s in terminal.SpawnPoints) if (s) values.Add(s); }
        foreach (var terminal in All<MasterTerminal>(scene).Where(t => t.name == NorthWestRoomSurvey.MasterTerminalName)) AddHierarchy(terminal.transform, values);
        foreach (var door in All<SecurityDoor>(scene).Where(d => d.name == NorthWestRoomSurvey.EastDoorName || d.name == NorthWestRoomSurvey.BossDoorName)) AddHierarchy(door.transform, values);
        if (GlobalObjectId.TryParse(report.accessMachine.id, out var accessId) &&
            GlobalObjectId.GlobalObjectIdentifierToObjectSlow(accessId) is Component access) AddHierarchy(access.transform, values);
        foreach (var shell in report.shell) if (GlobalObjectId.TryParse(shell.id, out var id) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is Component c) values.Add(c.transform);
        return values.ToDictionary(t => GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), t => new TransformState { label = PathOf(t), parent = t.parent ? GlobalObjectId.GetGlobalObjectIdSlow(t.parent).ToString() : "", position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
    }
    static void CompareCriticalTransforms(Dictionary<string, TransformState> before, Dictionary<string, TransformState> after)
    {
        var errors = new List<string>();
        foreach (var pair in before)
            if (!after.TryGetValue(pair.Key, out var value)) errors.Add(pair.Value.label + " is missing after reload");
            else if (pair.Value.parent != value.parent || pair.Value.position != value.position || pair.Value.rotation != value.rotation || pair.Value.scale != value.scale) errors.Add(pair.Value.label + " transform or parent changed");
        foreach (var pair in after) if (!before.ContainsKey(pair.Key)) errors.Add(pair.Value.label + " unexpectedly entered critical ownership set");
        if (errors.Count > 0) throw new InvalidOperationException("Room 3 shell/gameplay ownership changed:\n  " + string.Join("\n  ", errors));
    }

    static bool IsThirdPartyMaterial(string path) => path.StartsWith("Assets/SciFi Warehouse Kit/", StringComparison.Ordinal) || path.StartsWith("Assets/Sci-Fi Styled Modular Pack/", StringComparison.Ordinal) || path.StartsWith("Assets/HorrorPuzzleItems/", StringComparison.Ordinal);
    static string HashFile(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static Transform FindRoot(Scene scene) { var level = scene.GetRootGameObjects().Select(g => g.transform).SingleOrDefault(t => t.name == LevelName); return level ? level.Cast<Transform>().SingleOrDefault(t => t.name == RootName) : null; }
    static Transform UniqueChild(IEnumerable<Transform> items, string name, string label) { var found = items.Where(t => t.name == name).ToArray(); if (found.Length != 1) throw new InvalidOperationException($"Expected exactly one {label}, found {found.Length}."); return found[0]; }
    static Transform UniqueDirectChild(Transform parent, string name, bool required) { var found = parent.Cast<Transform>().Where(t => t.name == name).ToArray(); if (found.Length > 1 || required && found.Length != 1) throw new InvalidOperationException($"Expected {(required ? "exactly one" : "at most one")} {PathOf(parent)}/{name}, found {found.Length}."); return found.SingleOrDefault(); }
    static Transform TryUnique(IEnumerable<Transform> items, string name, string label, List<string> errors) { var found = items.Where(t => t.name == name).ToArray(); if (found.Length == 1) return found[0]; errors.Add($"Expected exactly one {label}, found {found.Length}."); return null; }
    static void ValidateContainer(Transform t, string label, List<string> errors) { if (t.GetComponents<Component>().Any(c => !(c is Transform))) errors.Add(label + " may contain only Transform."); if (t.localPosition != Vector3.zero || t.localRotation != Quaternion.identity || t.localScale != Vector3.one) errors.Add(label + " must retain identity local transform."); }
    static void ValidateRoot(Transform root, List<string> errors)
    {
        if (root.GetComponents<Component>().Any(c => !(c is Transform))) errors.Add(RootPath + " may contain only Transform.");
        if (root.position != Vector3.zero || root.rotation != Quaternion.identity || root.lossyScale != Vector3.one)
            errors.Add(RootPath + " must have identity world position, rotation, and scale despite Level's offset.");
    }
    static void CheckOutside(Transform root, Transform item, string label, List<string> errors) { if (item && item.IsChildOf(root)) errors.Add(PathOf(item) + " (" + label + ") was reparented into Room 3 dressing."); }
    static void RequireMainScene(Scene scene) { if (scene.path != NorthWestRoomSurvey.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1) throw new InvalidOperationException("Open MainScene alone in Edit mode before using the North-West Room framework."); }
    static void SetRootWorldIdentity(Transform root, Transform level)
    {
        Vector3 scale = level.lossyScale;
        if (Mathf.Approximately(scale.x, 0) || Mathf.Approximately(scale.y, 0) || Mathf.Approximately(scale.z, 0))
            throw new InvalidOperationException("Level has a zero scale component; Room 3 cannot compensate its transform.");
        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
    }
    static void ResetLocal(Transform t) { t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one; }
    static Bounds ToBounds(NorthWestRoomSurvey.Box box) { var b = new Bounds(); b.SetMinMax(box.min, box.max); return b; }
    static bool Contains(Bounds b, Vector3 p) => p.x >= b.min.x && p.x <= b.max.x && p.y >= b.min.y && p.y <= b.max.y && p.z >= b.min.z && p.z <= b.max.z;
    static bool OverlapsXZ(Bounds a, Bounds b) => a.min.x < b.max.x && a.max.x > b.min.x && a.min.z < b.max.z && a.max.z > b.min.z;
    static float FlatDistance(Vector3 point, Bounds bounds) { float x = Mathf.Max(bounds.min.x - point.x, 0, point.x - bounds.max.x), z = Mathf.Max(bounds.min.z - point.z, 0, point.z - bounds.max.z); return Mathf.Sqrt(x * x + z * z); }
    static float FarthestCornerDistanceXZ(Vector3 point, Bounds bounds) { float x = Mathf.Max(Mathf.Abs(bounds.min.x - point.x), Mathf.Abs(bounds.max.x - point.x)), z = Mathf.Max(Mathf.Abs(bounds.min.z - point.z), Mathf.Abs(bounds.max.z - point.z)); return Mathf.Sqrt(x * x + z * z); }
    static float SegmentDistanceXZ(Vector3 a, Vector3 b, Bounds bounds) { float length = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)); int samples = Mathf.Max(1, Mathf.CeilToInt(length / .1f)); float best = float.MaxValue; for (int i = 0; i <= samples; i++) best = Mathf.Min(best, FlatDistance(Vector3.Lerp(a, b, i / (float)samples), bounds)); return best; }
    static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    static void AddHierarchy(Transform root, HashSet<Transform> values) { foreach (var t in root.GetComponentsInChildren<Transform>(true)) values.Add(t); }
    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
}

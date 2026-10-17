using System;
using System.Collections.Generic;
using System.Linq;
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
/// Owns the empty Centre Room dressing hierarchy and the measured placement contract used by later builders.
/// It never adopts or reparents the room shell, gates, terminal, spawn markers, enemies, or navigation.
/// </summary>
public static class CentreRoomFramework
{
    public const string LevelName = "Level";
    public const string RootName = "Centre_Room";
    public const string RootPath = LevelName + "/" + RootName;
    public const string AssetRoot = "Assets/FPS/Art/CentreRoom";
    public const float MovementHeight = 2.2f;
    public const float RouteHalfWidth = 1.5f;
    public const float ReactorRadius = 3f;

    public enum Section
    {
        Reactor,
        Cover,
        PerimeterMachinery,
        WallServices,
        Overhead,
        Lighting,
        Wayfinding,
    }

    public sealed class SectionSpec
    {
        public readonly Section section;
        public readonly string name;
        public readonly string owner;
        public readonly string assetFolder;

        public SectionSpec(Section section, string name, string owner)
        {
            this.section = section;
            this.name = name;
            this.owner = owner;
            assetFolder = AssetRoot + "/" + name;
        }
    }

    static readonly SectionSpec[] Specs =
    {
        new SectionSpec(Section.Reactor, "Reactor", "corebuster_env_020"),
        new SectionSpec(Section.Cover, "Cover", "corebuster_env_024"),
        new SectionSpec(Section.PerimeterMachinery, "Perimeter_Machinery", "corebuster_env_025"),
        new SectionSpec(Section.WallServices, "Wall_Services", "corebuster_env_025"),
        new SectionSpec(Section.Overhead, "Overhead", "corebuster_env_025"),
        new SectionSpec(Section.Lighting, "Lighting", "corebuster_env_026"),
        new SectionSpec(Section.Wayfinding, "Wayfinding", "corebuster_env_027"),
    };

    public sealed class Layout
    {
        public readonly CentreRoomSurvey.Report report;
        public readonly Bounds interior;

        internal Layout(CentreRoomSurvey.Report report)
        {
            this.report = report ?? throw new ArgumentNullException(nameof(report));
            interior = ToBounds(report.interior);
        }

        public float Floor => interior.min.y;
        public float Ceiling => interior.max.y;
        public Vector3 RingCenter => report.ringCenter;

        /// <summary>
        /// Returns the first measured placement violation for a renderer/collider bounds, or null when it is clear.
        /// The central Reactor section may occupy only the radius-3 study envelope; all other body-height dressing
        /// preserves the complete radius-4-to-8 circulation annulus and the measured connectors to it.
        /// </summary>
        public string KeepOutViolation(Section section, Bounds bounds)
        {
            const float tolerance = .03f;
            if (bounds.min.x < interior.min.x - tolerance || bounds.max.x > interior.max.x + tolerance ||
                bounds.min.z < interior.min.z - tolerance || bounds.max.z > interior.max.z + tolerance ||
                bounds.min.y < interior.min.y - tolerance || bounds.max.y > interior.max.y + tolerance)
                return $"extends outside the measured interior {interior.min:F2} to {interior.max:F2}";

            if (bounds.min.y >= Floor + MovementHeight)
                return null;

            foreach (var gate in report.gates)
                if (OverlapsXZ(bounds, ToBounds(gate.approach)))
                    return $"overlaps the measured {gate.name} approach";

            if (OverlapsXZ(bounds, ToBounds(report.terminalKeepOut)))
                return "overlaps the yellow-terminal access or sightline reservation";

            foreach (var spawn in report.spawns)
                if (FlatDistance(spawn.position, bounds) < report.spawnRadius)
                    return $"enters the 2 m spawn reservation at {spawn.position:F2}";

            foreach (var occupant in report.occupants)
                if (FlatDistance(occupant.position, bounds) < report.spawnRadius)
                    return $"enters the 2 m occupant reservation at {occupant.position:F2}";

            if (section == Section.Reactor)
            {
                if (FarthestCornerDistanceXZ(report.ringCenter, bounds) > ReactorRadius + tolerance)
                    return $"extends outside the radius-{ReactorRadius:F0} reactor study envelope";
                return null;
            }

            float near = FlatDistance(report.ringCenter, bounds);
            float far = FarthestCornerDistanceXZ(report.ringCenter, bounds);
            if (near < report.ringOuterRadius && far > report.ringInnerRadius)
                return $"enters the continuous radius-{report.ringInnerRadius:F0}-to-{report.ringOuterRadius:F0} combat annulus";

            foreach (var route in ConnectorRoutes(report))
                for (int i = 1; i < route.corners.Length; i++)
                    if (SegmentDistanceXZ(route.corners[i - 1], route.corners[i], bounds) < RouteHalfWidth)
                        return $"enters the {RouteHalfWidth:F1} m half-width connector '{route.name}'";

            return null;
        }

        public void ValidateBounds(Section section, Bounds bounds, string label, ICollection<string> errors)
        {
            string violation = KeepOutViolation(section, bounds);
            if (violation != null) errors.Add(label + " " + violation + ".");
        }
    }

    [MenuItem("Tools/CoreBuster/Build Centre Room Framework")]
    public static void BuildMenu()
    {
        try
        {
            var layout = MeasureLayout();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Centre Room Framework");
            Build(layout);
            Undo.CollapseUndoOperations(undo);
            Selection.activeTransform = FindRoot(EditorSceneManager.GetActiveScene());
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            throw;
        }
    }

    [MenuItem("Tools/CoreBuster/Validate Centre Room Framework")]
    public static bool ValidateMenu()
    {
        try { return Validate(EditorSceneManager.GetActiveScene(), MeasureLayout(), true); }
        catch (Exception ex) { Debug.LogError("CENTRE ROOM FRAMEWORK CHECKS FAILED: " + ex.Message); return false; }
    }

    /// <summary>Batch build used for verified scene authoring. It saves, reloads, and checks critical transforms.</summary>
    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = MeasureLayout();
        var before = CaptureCriticalTransforms(EditorSceneManager.GetActiveScene());
        VerifyLayoutContract(layout);
        Build(layout);
        VerifyIsolatedRebuildAndUndo(layout);
        if (!Validate(EditorSceneManager.GetActiveScene(), layout, true))
            throw new InvalidOperationException("Centre Room framework validation failed before save.");
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = MeasureLayout();
        if (!Validate(EditorSceneManager.GetActiveScene(), layout, true))
            throw new InvalidOperationException("Centre Room framework validation failed after saved-scene reload.");
        CompareCriticalTransforms(before, CaptureCriticalTransforms(EditorSceneManager.GetActiveScene()));
        Debug.Log($"CENTRE ROOM FRAMEWORK BUILD VERIFIED: {RootPath} with {Specs.Length} independently owned sections; " +
            "negative keep-out/ownership checks passed, isolated rebuild/Undo passed, save/reload passed, and " +
            "shell/gameplay transform snapshots are unchanged.");
    }

    /// <summary>
    /// Measures the authoritative saved scene. Builders must call this before making the scene dirty, then pass the
    /// returned layout to their placement and validation code.
    /// </summary>
    public static Layout MeasureLayout()
    {
        try { return new Layout(CentreRoomSurvey.Measure()); }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Cannot establish the Centre Room dressing contract from MainScene. " +
                "Open the clean saved scene and restore its required shell, terminal, three gates, eight spawn markers, " +
                "HoverBot, and NavMesh anchors. " + ex.Message, ex);
        }
    }

    /// <summary>Creates missing empty containers only. Existing section content and gameplay objects are untouched.</summary>
    public static Transform Build(Layout layout)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        Scene scene = EditorSceneManager.GetActiveScene();
        RequireMainScene(scene);
        Transform level = UniqueChild(scene.GetRootGameObjects().Select(g => g.transform), LevelName,
            "scene-root Level container");
        Transform root = UniqueDirectChild(level, RootName, false);
        if (!root)
        {
            var go = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(go, "Create Centre Room dressing root");
            Undo.SetTransformParent(go.transform, level, "Parent Centre Room dressing root");
            ResetLocal(go.transform);
            root = go.transform;
        }

        foreach (var spec in Specs)
        {
            Transform section = UniqueDirectChild(root, spec.name, false);
            if (section) continue;
            var go = new GameObject(spec.name);
            Undo.RegisterCreatedObjectUndo(go, "Create Centre Room " + spec.name + " section");
            Undo.SetTransformParent(go.transform, root, "Parent Centre Room " + spec.name + " section");
            ResetLocal(go.transform);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!Validate(scene, layout, true))
            throw new InvalidOperationException("Centre Room framework was created but did not pass validation; Undo the build and inspect the reported errors.");
        return root;
    }

    /// <summary>
    /// Clears exactly one owned section in the current Undo group and returns its persistent container. A future
    /// builder calls MeasureLayout first, then this method, then creates its own children beneath the returned root.
    /// </summary>
    public static Transform BeginSectionRebuild(Layout layout, Section section, string ownerTaskId)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        SectionSpec spec = Spec(section);
        if (!string.Equals(ownerTaskId, spec.owner, StringComparison.Ordinal))
            throw new InvalidOperationException($"{spec.name} is owned by {spec.owner}, not '{ownerTaskId}'. " +
                "A builder may clear only its assigned Centre Room section.");
        Scene scene = EditorSceneManager.GetActiveScene();
        RequireMainScene(scene);
        Transform root = FindRoot(scene);
        if (!root) throw new InvalidOperationException($"Missing {RootPath}. Run Tools/CoreBuster/Build Centre Room Framework first.");
        Transform target = UniqueDirectChild(root, spec.name, true);
        foreach (Transform child in target.Cast<Transform>().ToArray())
            Undo.DestroyObjectImmediate(child.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        return target;
    }

    public static Transform FindSection(Scene scene, Section section)
    {
        Transform root = FindRoot(scene);
        return root ? UniqueDirectChild(root, Spec(section).name, true) : null;
    }

    public static SectionSpec Spec(Section section)
    {
        SectionSpec spec = Specs.SingleOrDefault(s => s.section == section);
        return spec ?? throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown Centre Room section.");
    }

    public static string AssetFolder(Section section) => Spec(section).assetFolder;

    /// <summary>Fails if a newly generated asset is not stored in its owning section's project folder.</summary>
    public static void RequireOwnedAsset(Object asset, Section section)
    {
        if (!asset) throw new ArgumentNullException(nameof(asset));
        string path = AssetDatabase.GetAssetPath(asset).Replace('\\', '/');
        string folder = AssetFolder(section) + "/";
        if (!path.StartsWith(folder, StringComparison.Ordinal))
            throw new InvalidOperationException($"Generated {asset.GetType().Name} '{asset.name}' must be written below {folder}; actual path is '{path}'.");
    }

    public static bool Validate(Scene scene, Layout layout, bool log)
    {
        RequireMainScene(scene);
        var errors = new List<string>();
        Transform level = TryUnique(scene.GetRootGameObjects().Select(g => g.transform), LevelName, "scene-root Level container", errors);
        Transform root = level ? TryUnique(level.Cast<Transform>(), RootName, RootPath, errors) : null;
        if (root)
        {
            ValidateContainer(root, RootPath, errors);
            var expected = new HashSet<string>(Specs.Select(s => s.name), StringComparer.Ordinal);
            foreach (Transform child in root)
                if (!expected.Contains(child.name)) errors.Add($"Unexpected direct child {RootPath}/{child.name}; every pass belongs in one owned section.");

            foreach (var spec in Specs)
            {
                Transform section = TryUnique(root.Cast<Transform>(), spec.name, RootPath + "/" + spec.name, errors);
                if (!section) continue;
                ValidateContainer(section, RootPath + "/" + spec.name, errors);
                ValidateSectionContent(section, spec, layout, errors);
            }
            ValidateGameplayOwnership(scene, root, layout.report, errors);
        }

        var report = new StringBuilder();
        report.AppendLine($"Measured interior {layout.interior.min:F2} to {layout.interior.max:F2}; ring centre {layout.RingCenter:F2}.");
        foreach (var spec in Specs) report.AppendLine($"  {spec.name}: owner {spec.owner}; generated assets {spec.assetFolder}/");
        foreach (string error in errors) report.AppendLine("  ERROR: " + error);
        if (errors.Count == 0)
        {
            if (log) Debug.Log($"CENTRE ROOM FRAMEWORK CHECKS PASSED: {Specs.Length} sections have unique ownership, identity transforms, measured keep-outs, and no adopted gameplay or shell objects.\n{report}");
            return true;
        }
        if (log) Debug.LogError($"CENTRE ROOM FRAMEWORK CHECKS FAILED ({errors.Count} error(s)).\n{report}");
        return false;
    }

    static void ValidateSectionContent(Transform section, SectionSpec spec, Layout layout, List<string> errors)
    {
        foreach (var renderer in section.GetComponentsInChildren<Renderer>(true))
            if (renderer.enabled && !(spec.section == Section.Lighting && CentreRoomLighting.IsBoundarySurface(renderer, layout)))
                layout.ValidateBounds(spec.section, renderer.bounds, PathOf(renderer.transform), errors);
        foreach (var collider in section.GetComponentsInChildren<Collider>(true))
            if (collider.enabled && !collider.isTrigger) layout.ValidateBounds(spec.section, collider.bounds, PathOf(collider.transform), errors);
        foreach (var light in section.GetComponentsInChildren<Light>(true))
            if (light.enabled && !Contains(layout.interior, light.transform.position))
                errors.Add($"{PathOf(light.transform)} light origin lies outside the measured interior.");
        foreach (var target in section.GetComponentsInChildren<CoreExecutionTarget>(true))
            errors.Add($"{PathOf(target.transform)} has CoreExecutionTarget; Centre Room dressing must never register as executable gameplay.");
        foreach (var obstacle in section.GetComponentsInChildren<NavMeshObstacle>(true))
            if (obstacle.enabled && !obstacle.carving)
                errors.Add($"{PathOf(obstacle.transform)} has a non-carving NavMeshObstacle; dressing obstacles must carve only where measured behavior requires it.");
    }

    static void ValidateGameplayOwnership(Scene scene, Transform root, CentreRoomSurvey.Report report, List<string> errors)
    {
        var terminals = All<KeycardTerminal>(scene).Where(t => t.name == "Centre Room Terminal (Yellow)").ToArray();
        if (terminals.Length != 1) errors.Add($"Expected one Centre Room Terminal (Yellow), found {terminals.Length}.");
        else
        {
            var terminal = terminals[0];
            CheckOutside(root, terminal.transform, "yellow terminal", errors);
            if (terminal.Doors != null) foreach (var door in terminal.Doors.Where(d => d)) CheckOutside(root, door.transform, "security gate", errors);
            if (terminal.SpawnPoints != null) foreach (var spawn in terminal.SpawnPoints.Where(s => s)) CheckOutside(root, spawn, "spawn marker", errors);
        }
        foreach (var occupant in report.occupants)
        {
            Object obj;
            if (GlobalObjectId.TryParse(occupant.id, out var id) && (obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id)))
                CheckOutside(root, obj is Component c ? c.transform : null, "room enemy", errors);
        }
        foreach (var shell in report.shell)
        {
            Object obj;
            if (GlobalObjectId.TryParse(shell.id, out var id) && (obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id)))
                CheckOutside(root, obj is Component c ? c.transform : null, "original shell", errors);
        }
    }

    static void VerifyIsolatedRebuildAndUndo(Layout layout)
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        Transform reactor = FindSection(scene, Section.Reactor);
        Transform cover = FindSection(scene, Section.Cover);
        var reactorSentinel = new GameObject("__Reactor_Rebuild_Verification");
        var coverSentinel = new GameObject("__Cover_Isolation_Verification");
        string reactorSentinelName = reactorSentinel.name;
        string coverSentinelName = coverSentinel.name;
        reactorSentinel.transform.SetParent(reactor, false);
        coverSentinel.transform.SetParent(cover, false);
        try
        {
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Verify isolated Centre Room section rebuild");
            BeginSectionRebuild(layout, Section.Reactor, Spec(Section.Reactor).owner);
            Undo.CollapseUndoOperations(undo);
            if (reactor.Find(reactorSentinelName) || !cover.Find(coverSentinelName))
                throw new InvalidOperationException("Isolated Reactor rebuild changed the wrong Centre Room section.");
            Undo.PerformUndo();
            if (!reactor.Find(reactorSentinelName) || !cover.Find(coverSentinelName))
                throw new InvalidOperationException("Undo did not restore the isolated Reactor section rebuild.");
        }
        finally
        {
            if (reactorSentinel) Object.DestroyImmediate(reactorSentinel);
            if (coverSentinel) Object.DestroyImmediate(coverSentinel);
        }
    }

    static void VerifyLayoutContract(Layout layout)
    {
        Bounds allowedReactor = new Bounds(layout.RingCenter + Vector3.up * .5f, new Vector3(4f, 1f, 4f));
        if (layout.KeepOutViolation(Section.Reactor, allowedReactor) != null)
            throw new InvalidOperationException("The measured layout rejected a representative reactor inside its radius-3 envelope.");

        Bounds oversizedReactor = new Bounds(layout.RingCenter + Vector3.up * .5f, new Vector3(6.2f, 1f, 6.2f));
        if (layout.KeepOutViolation(Section.Reactor, oversizedReactor) == null)
            throw new InvalidOperationException("The measured layout accepted a reactor outside its radius-3 envelope.");

        Bounds annulus = new Bounds(layout.RingCenter + Vector3.right * 6f + Vector3.up * .5f, Vector3.one);
        if (layout.KeepOutViolation(Section.Cover, annulus) == null)
            throw new InvalidOperationException("The measured layout accepted body-height cover in the protected combat annulus.");

        Bounds spawn = new Bounds(layout.report.spawns[0].position + Vector3.up * .5f, Vector3.one);
        if (layout.KeepOutViolation(Section.PerimeterMachinery, spawn) == null)
            throw new InvalidOperationException("The measured layout accepted body-height machinery in a spawn reservation.");

        bool rejectedWrongOwner = false;
        try { BeginSectionRebuild(layout, Section.Cover, Spec(Section.Reactor).owner); }
        catch (InvalidOperationException) { rejectedWrongOwner = true; }
        if (!rejectedWrongOwner)
            throw new InvalidOperationException("The framework accepted a section rebuild from the wrong task owner.");
    }

    sealed class TransformState
    {
        public string id, label, parent;
        public Vector3 localPosition, localScale;
        public Quaternion localRotation;
    }

    static Dictionary<string, TransformState> CaptureCriticalTransforms(Scene scene)
    {
        var transforms = new HashSet<Transform>();
        var terminal = All<KeycardTerminal>(scene).Single(t => t.name == "Centre Room Terminal (Yellow)");
        AddHierarchy(terminal.transform, transforms);
        foreach (var door in terminal.Doors) AddHierarchy(door.transform, transforms);
        foreach (var spawn in terminal.SpawnPoints) transforms.Add(spawn);
        foreach (var agent in All<NavMeshAgent>(scene))
            if (agent.name == "Enemy_HoverBot") AddHierarchy(agent.transform, transforms);
        var measured = CentreRoomSurvey.Measure();
        foreach (var shell in measured.shell)
            if (GlobalObjectId.TryParse(shell.id, out var id) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is Component c)
                transforms.Add(c.transform);

        return transforms.ToDictionary(t => GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), t => new TransformState
        {
            id = GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), label = PathOf(t),
            parent = t.parent ? GlobalObjectId.GetGlobalObjectIdSlow(t.parent).ToString() : "",
            localPosition = t.localPosition, localRotation = t.localRotation, localScale = t.localScale,
        });
    }

    static void CompareCriticalTransforms(Dictionary<string, TransformState> before, Dictionary<string, TransformState> after)
    {
        var errors = new List<string>();
        foreach (var pair in before)
        {
            if (!after.TryGetValue(pair.Key, out var value)) { errors.Add(pair.Value.label + " is missing after reload"); continue; }
            TransformState old = pair.Value;
            if (old.parent != value.parent || old.localPosition != value.localPosition || old.localRotation != value.localRotation || old.localScale != value.localScale)
                errors.Add(old.label + " transform or parent changed");
        }
        foreach (var pair in after) if (!before.ContainsKey(pair.Key)) errors.Add(pair.Value.label + " unexpectedly entered the critical-object set");
        if (errors.Count > 0) throw new InvalidOperationException("Centre Room shell/gameplay objects changed during framework build:\n  " + string.Join("\n  ", errors));
    }

    static Transform FindRoot(Scene scene)
    {
        Transform level = scene.GetRootGameObjects().Select(g => g.transform).SingleOrDefault(t => t.name == LevelName);
        return level ? level.Cast<Transform>().SingleOrDefault(t => t.name == RootName) : null;
    }

    static Transform UniqueChild(IEnumerable<Transform> candidates, string name, string label)
    {
        Transform[] matches = candidates.Where(t => t.name == name).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"Expected exactly one {label}, found {matches.Length}.");
        return matches[0];
    }

    static Transform UniqueDirectChild(Transform parent, string name, bool required)
    {
        Transform[] matches = parent.Cast<Transform>().Where(t => t.name == name).ToArray();
        if (matches.Length > 1 || (required && matches.Length != 1))
            throw new InvalidOperationException($"Expected {(required ? "exactly one" : "at most one")} {PathOf(parent)}/{name}, found {matches.Length}.");
        return matches.SingleOrDefault();
    }

    static Transform TryUnique(IEnumerable<Transform> candidates, string name, string label, List<string> errors)
    {
        Transform[] matches = candidates.Where(t => t.name == name).ToArray();
        if (matches.Length == 1) return matches[0];
        errors.Add($"Expected exactly one {label}, found {matches.Length}.");
        return null;
    }

    static void ValidateContainer(Transform transform, string label, List<string> errors)
    {
        if (transform.GetComponents<Component>().Any(c => !(c is Transform)))
            errors.Add(label + " is an ownership container and may have only a Transform component.");
        if (transform.localPosition != Vector3.zero || transform.localRotation != Quaternion.identity || transform.localScale != Vector3.one)
            errors.Add(label + " must keep identity local position, rotation, and scale.");
    }

    static void CheckOutside(Transform root, Transform candidate, string label, List<string> errors)
    {
        if (candidate && candidate.IsChildOf(root)) errors.Add($"{PathOf(candidate)} ({label}) was reparented into dressing; restore original ownership.");
    }

    static void RequireMainScene(Scene scene)
    {
        if (scene.path != CentreRoomSurvey.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Open MainScene alone in Edit mode before building or validating the Centre Room framework.");
    }

    static void ResetLocal(Transform transform)
    {
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    static IEnumerable<CentreRoomSurvey.Route> ConnectorRoutes(CentreRoomSurvey.Report report) => report.routes.Where(route =>
        route.corners != null && route.corners.Length > 1 && !route.name.StartsWith("Ring segment", StringComparison.Ordinal) &&
        !route.name.Contains("across saved gate") && !route.name.StartsWith("Workshop exit corridor", StringComparison.Ordinal));

    static Bounds ToBounds(CentreRoomSurvey.Box box)
    {
        var bounds = new Bounds();
        bounds.SetMinMax(box.min, box.max);
        return bounds;
    }

    static bool OverlapsXZ(Bounds a, Bounds b) =>
        a.min.x < b.max.x && a.max.x > b.min.x && a.min.z < b.max.z && a.max.z > b.min.z;

    static bool Contains(Bounds bounds, Vector3 point) => point.x >= bounds.min.x && point.x <= bounds.max.x &&
        point.y >= bounds.min.y && point.y <= bounds.max.y && point.z >= bounds.min.z && point.z <= bounds.max.z;

    static float FlatDistance(Vector3 point, Bounds bounds)
    {
        float dx = Mathf.Max(bounds.min.x - point.x, 0, point.x - bounds.max.x);
        float dz = Mathf.Max(bounds.min.z - point.z, 0, point.z - bounds.max.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static float FarthestCornerDistanceXZ(Vector3 point, Bounds bounds)
    {
        float dx = Mathf.Max(Mathf.Abs(bounds.min.x - point.x), Mathf.Abs(bounds.max.x - point.x));
        float dz = Mathf.Max(Mathf.Abs(bounds.min.z - point.z), Mathf.Abs(bounds.max.z - point.z));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static float SegmentDistanceXZ(Vector3 a, Vector3 b, Bounds bounds)
    {
        float length = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        int samples = Mathf.Max(1, Mathf.CeilToInt(length / .1f));
        float best = float.MaxValue;
        for (int i = 0; i <= samples; i++)
            best = Mathf.Min(best, FlatDistance(Vector3.Lerp(a, b, i / (float)samples), bounds));
        return best;
    }

    static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    static void AddHierarchy(Transform root, HashSet<Transform> values)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true)) values.Add(transform);
    }

    static string PathOf(Transform transform) => transform.parent ? PathOf(transform.parent) + "/" + transform.name : transform.name;
}

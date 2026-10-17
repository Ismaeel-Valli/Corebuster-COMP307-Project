using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Static acceptance checks for the Start Workshop dressing (corebuster_env_002 and its corebuster_env_004 detail
/// layer, the corebuster_env_007 overhead layer, the corebuster_env_009 pipework, the corebuster_env_010 workstation and the
/// corebuster_env_019 centre islands, with their 3 m combat lanes): props stay inside the room, clear of spawn, route, doorway and NPCs, are supported, do not interpenetrate,
/// keep asset materials on forward-renderer shaders (corebuster_render_001), keep the room's lit material copies inside the
/// room (corebuster_render_005), line the kept boundary pieces without closing the exit gap (corebuster_render_007), keep
/// the room's lights inside it and within URP's per-object light limit (corebuster_render_006), and follow the Level
/// static convention.
/// It does not replace the Play-mode walk/sprint walkthrough in TESTING.md.
/// </summary>
public static class StartWorkshopChecks
{
    // Player CharacterController radius (Player.prefab) plus margin for a comfortable sprint past props.
    const float RouteSweepRadius = .35f + .5f;
    const float Contact = .04f;
    const float MaxOverlap = .03f;
    // The measured wall faces are the pilasters; wall-mounted props sit on the panels recessed up to ~0.2 m behind them.
    const float WallRelief = .25f;
    // Overhead props (e.g. the exit sign) may sit in a recessed door lintel slightly beyond the measured wall face.
    const float OverheadRecess = .6f;
    const int MinProps = 15, MaxProps = 22;
    // The detail layer adds layered clutter, not a second room's worth; more than this reads as excessive.
    const int MaxDetailProps = 30;

    [MenuItem("Tools/CoreBuster/Validate Start Workshop Dressing")]
    public static bool Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var level = StartWorkshopDresser.FindLevel(scene);
        var root = level != null ? level.Find(StartWorkshopDresser.ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"START WORKSHOP CHECKS FAILED: no {StartWorkshopDresser.LevelName}/{StartWorkshopDresser.ContainerName} in the active scene.");
            return false;
        }

        var f = StartWorkshopFrame.Measure(scene, root);
        Physics.SyncTransforms();
        var errors = new List<string>();
        var warnings = new List<string>();

        // A prop is each direct child of a group (e.g. Storage_WestWall/Shelf_A); the workbench counts as one prop.
        var props = new List<(Transform t, Bounds b)>();
        int detail = 0;
        int overhead = 0;
        int cladding = 0;
        int pipework = 0;
        int workstation = 0;
        int lighting = 0;
        int centre = 0;
        foreach (Transform group in root)
        {
            // Overlays, boundary liners, the grime decal projectors (no renderer, no collider) and the local rendering controls are not
            // physical props; each has its own rules below.
            if (group.name == StartWorkshopGraphics.GroupName || group.name == StartWorkshopPortal.GroupName || StartWorkshopLiners.IsGroup(group) || StartWorkshopRendering.IsGroup(group) ||
                StartWorkshopSignage.IsGroup(group) || StartWorkshopScreens.IsGroup(group) || StartWorkshopGrime.IsGroup(group) ||
                StartWorkshopAmbient.IsGroup(group) || StartWorkshopAmbience.IsGroup(group) || StartWorkshopFloor.IsGroup(group)) continue;
            foreach (Transform prop in group)
            {
                // The lighting pass's lamps and strip are props; its light-only children (task light, fill, probe) are not.
                if (StartWorkshopLighting.IsGroup(group) && !StartWorkshopLighting.IsFixture(prop)) continue;
                props.Add((prop, StartWorkshopDresser.B(prop.gameObject)));
                if (group.name.EndsWith(StartWorkshopDetailer.DetailSuffix)) detail++;
                else if (StartWorkshopOverhead.IsGroup(group)) overhead++;
                else if (StartWorkshopCladding.IsGroup(group)) cladding++;
                else if (StartWorkshopPipework.IsGroup(group)) pipework++;
                else if (StartWorkshopWorkstation.IsGroup(group)) workstation++;
                else if (StartWorkshopLighting.IsGroup(group)) lighting++;
                else if (StartWorkshopCentre.IsGroup(group)) centre++;
            }
        }
        int first = props.Count - detail - overhead - cladding - pipework - workstation - lighting - centre;
        if (first < MinProps || first > MaxProps)
            warnings.Add($"{first} first-pass props placed; the plan targets roughly {MinProps}-20.");
        if (detail > MaxDetailProps)
            warnings.Add($"{detail} detail-layer props placed; more than {MaxDetailProps} risks excessive clutter.");

        foreach (var (t, b) in props)
        {
            string name = Path(t, root);
            float slack = f.IsReachable(b) ? WallRelief : OverheadRecess;
            if (b.min.x < f.West - slack || b.max.x > f.East + slack || b.min.z < f.South - slack ||
                b.max.z > f.North + slack || b.min.y < f.Floor - Contact || b.max.y > f.Ceiling + Contact)
                errors.Add($"{name} extends outside the room interior ({b.min:F2} to {b.max:F2}).");
            string keepOut = f.KeepOutViolation(b);
            if (keepOut != null) errors.Add($"{name} is {keepOut}.");
            if (!Supported(b, t, props, f)) errors.Add($"{name} is not resting on the floor, a wall, the ceiling or another prop.");
            if (f.IsReachable(b) && t.GetComponentsInChildren<Collider>().Length == 0)
                errors.Add($"{name} is at body height but has no collider.");
        }

        for (int i = 0; i < props.Count; i++)
            for (int j = i + 1; j < props.Count; j++)
                if (Overlap(props[i].b, props[j].b) && PartsOverlap(props[i].t, props[j].t))
                    errors.Add($"{Path(props[i].t, root)} intersects {Path(props[j].t, root)}.");

        CheckRouteSweep(f, root, errors);
        CheckAssetsAndStatics(root, level, errors);
        StartWorkshopGraphics.Validate(root, f, errors);
        StartWorkshopOverhead.Validate(root, f, overhead, errors);
        StartWorkshopCladding.Validate(root, f, errors);
        StartWorkshopPipework.Validate(root, f, errors);
        StartWorkshopWorkstation.Validate(root, f, errors);
        StartWorkshopLitMaterials.Validate(root, f, errors);
        StartWorkshopLiners.Validate(root, f, errors);
        StartWorkshopLighting.Validate(root, f, errors);
        StartWorkshopRendering.Validate(root, f, errors);
        StartWorkshopPortal.Validate(root, f, errors);
        StartWorkshopCentre.Validate(root, f, errors);
        StartWorkshopSignage.Validate(root, f, errors);
        StartWorkshopScreens.Validate(root, f, errors);
        StartWorkshopGrime.Validate(root, f, errors);
        StartWorkshopFloor.Validate(root, f, errors);
        StartWorkshopAmbient.Validate(root, errors);
        StartWorkshopAmbience.Validate(root, f, errors);

        Unity.FPS.Gameplay.PlayerCharacterController player = null;
        foreach (var go in scene.GetRootGameObjects())
            if (player == null) player = go.GetComponentInChildren<Unity.FPS.Gameplay.PlayerCharacterController>(true);
        if (player != null && (player.transform.position - new Vector3(-167.98f, 3.5f, 17.96f)).sqrMagnitude > .0001f)
            warnings.Add($"Player spawn moved from the documented (-167.98, 3.5, 17.96) to {player.transform.position:F2}.");

        var report = new StringBuilder();
        report.AppendLine(f.ToString());
        foreach (var (t, b) in props) report.AppendLine($"  {Path(t, root)}: {b.min:F2} to {b.max:F2}");
        foreach (var w in warnings) report.AppendLine("  WARNING: " + w);
        foreach (var e in errors) report.AppendLine("  ERROR: " + e);
        if (errors.Count == 0)
        {
            Debug.Log($"START WORKSHOP CHECKS PASSED: {props.Count} props ({first} first pass, {detail} detail, {overhead} overhead, {cladding} cladding, {pipework} pipework, {workstation} workstation, {lighting} light fixtures, {centre} centre-island) clear of spawn, route, doorway and NPCs; supported; " +
                      $"no overlaps; centre islands keep {StartWorkshopCentre.LaneWidth} m combat lanes; asset materials on forward-renderer shaders; lit copies only inside the room; boundary liners lit and clear of the exit gap; lights contained and within the per-object budget; Level static flags.\n{report}");
            return true;
        }
        Debug.LogError($"START WORKSHOP CHECKS FAILED ({errors.Count} error(s)).\n{report}");
        return false;
    }

    /// <summary>Touches the (collider-less) floor tiles, the ceiling, any other collider such as a wall or lintel, or rests on a prop.</summary>
    static bool Supported(Bounds b, Transform self, List<(Transform t, Bounds b)> props, StartWorkshopFrame f)
    {
        if (Mathf.Abs(b.min.y - f.Floor) < Contact || Mathf.Abs(f.Ceiling - b.max.y) < Contact) return true;
        foreach (var c in Physics.OverlapBox(b.center, b.extents + Vector3.one * Contact, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (!c.transform.IsChildOf(self)) return true;
        foreach (var (t, other) in props)
            if (t != self && Mathf.Abs(b.min.y - other.max.y) < Contact &&
                b.min.x < other.max.x && b.max.x > other.min.x && b.min.z < other.max.z && b.max.z > other.min.z)
                return true;
        return false;
    }

    internal static bool Overlap(Bounds a, Bounds b) =>
        Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) > MaxOverlap &&
        Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y) > MaxOverlap &&
        Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z) > MaxOverlap;

    /// <summary>
    /// Compares the props' individual mesh bounds, so an item resting on a bench top inside the bench's overall box
    /// (raised by the toolbox on it) is not mistaken for an intersection.
    /// </summary>
    internal static bool PartsOverlap(Transform a, Transform b)
    {
        foreach (var ra in a.GetComponentsInChildren<Renderer>())
            if (ra.enabled && (ra is MeshRenderer || ra is SkinnedMeshRenderer))
                foreach (var rb in b.GetComponentsInChildren<Renderer>())
                    if (rb.enabled && (rb is MeshRenderer || rb is SkinnedMeshRenderer) && Overlap(ra.bounds, rb.bounds))
                        return true;
        return false;
    }

    /// <summary>Sweeps a padded player capsule from spawn to the doorway; no workshop collider may be in the way.</summary>
    static void CheckRouteSweep(StartWorkshopFrame f, Transform root, List<string> errors)
    {
        Physics.SyncTransforms();
        Vector3 bottom = Vector3.up * (f.Floor + RouteSweepRadius), top = Vector3.up * (f.Floor + 1.8f - RouteSweepRadius);
        var blockers = new HashSet<string>();
        Vector3 end = f.RouteEnd;
        for (float t = 0; t <= 1f; t += .02f)
        {
            Vector3 p = Vector3.Lerp(f.Spawn, end, t);
            p.y = 0;
            foreach (var c in Physics.OverlapCapsule(p + bottom, p + top, RouteSweepRadius, ~0, QueryTriggerInteraction.Ignore))
                if (c.transform.IsChildOf(root)) blockers.Add(Path(c.transform, root));
        }
        foreach (var name in blockers) errors.Add($"{name} blocks the padded spawn-to-exit route.");
    }

    static void CheckAssetsAndStatics(Transform root, Transform level, List<string> errors)
    {
        var flags = GameObjectUtility.GetStaticEditorFlags(level.gameObject);
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != (StartWorkshopAmbient.ExemptsStatic(t, root) ? 0 : flags))
                errors.Add($"{Path(t, root)} static flags differ from {level.name}.");
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m == null || !AssetDatabase.Contains(m))
                    errors.Add($"{Path(r.transform, root)} uses a missing or non-asset material instance.");
                else if (WorkshopMaterialUpgrade.RendersFlat(m.shader))
                    errors.Add($"{Path(r.transform, root)} material {m.name} uses {m.shader.name}, which the forward renderer draws " +
                               "unlit or not at all; run Tools/CoreBuster/Apply Start Workshop Lit Materials.");
        foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
            if (!body.isKinematic) errors.Add($"{Path(body.transform, root)} has a simulated Rigidbody.");
    }

    static string Path(Transform t, Transform root)
    {
        string path = t.name;
        for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
        return path;
    }
}

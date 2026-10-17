using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = BossHallFramework.Section;

/// <summary>
/// corebuster_env_051: six sealed maintenance racks in first-tier wall-service pockets, above walking headroom.
/// Uses the accepted contract and measured original wall faces; preserves all deck, stair, rail and bay-door geometry.
/// Upper-tier placements are deliberately deferred pending the owner's route/rail disposition. Record the separate
/// gameplay prerequisite disposition before using the build commands. Preflight is read-only and needs no waiver.
/// </summary>
public static class BossHallCatwalk
{
    public const string Owner = "corebuster_env_051", Output = "ReviewCaptures/BossHall/051";
    public const string Folder = BossHallFramework.AssetRoot + "/Catwalk_Dressing";
    const Section Owned = Section.CatwalkDressing;
    const float Width = 2.2f, Height = 1.9f, Depth = .64f;
    static string measuredContract;
    static BossHallSurvey.Report measured;

    static BossHallSurvey.Report Measurements(BossHallFramework.Layout layout)
    {
        // The trimmed layout omits deck-width and rail-test arrays. Measure the current original geometry,
        // rather than treating absent values as zero. Protected-state validation makes this cache safe across
        // this owner's isolated transactions; a different accepted baseline invalidates it.
        if (measured == null || measuredContract != layout.contractSha)
        {
            measured = BossHallSurvey.Measure(); measuredContract = layout.contractSha;
        }
        return measured;
    }

    public sealed class Placement
    {
        public string name, zone;
        public int variant;
        public Vector3 position;
        public Quaternion rotation;
        public Bounds bounds;
    }

    public static MeshRenderer[] Renderers() => BossHallFramework.FindSection(Owned).GetComponentsInChildren<MeshRenderer>(true);

    public static List<Placement> Plan(BossHallFramework.Layout layout)
    {
        var own = BossHallFramework.FindSection(Owned);
        var other = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
            .Where(r => r.enabled && r.gameObject.activeInHierarchy && !r.transform.IsChildOf(own)).ToArray();
        var result = new List<Placement>();
        foreach (string side in new[] { "South", "North" })
            for (int index = 1; index <= 3; index++)
            {
                var zone = layout.Report.zones.Single(z => z.name == side + " gallery wall services " + index);
                var envelope = new Bounds(); envelope.SetMinMax(zone.min, zone.max);
                float y = zone.min.y + .08f;
                var outward = side == "South" ? Vector3.back : Vector3.forward;
                var rotation = Quaternion.LookRotation(-outward);
                var candidates = new List<float>();
                for (float x = zone.min.x + Width * .5f + .15f; x <= zone.max.x - Width * .5f - .15f; x += .25f) candidates.Add(x);
                Placement chosen = null;
                foreach (float x in candidates.OrderBy(x => Mathf.Abs(x - envelope.center.x)))
                {
                    var rayStart = new Vector3(x, y + Height * .5f, side == "South" ? zone.max.z : zone.min.z);
                    var hit = Physics.RaycastAll(rayStart, outward, 1.5f, ~0, QueryTriggerInteraction.Ignore)
                        .Where(h => !h.collider.transform.IsChildOf(BossHallFramework.FindRoot())).OrderBy(h => h.distance).FirstOrDefault();
                    if (!hit.collider || Vector3.Dot(hit.normal, -outward) < .9f || Mathf.Abs(hit.point.z - (side == "South" ? layout.interior.min.z : layout.interior.max.z)) > .1f) continue;
                    var origin = new Vector3(x, y, hit.point.z) - outward * .008f;
                    var bounds = new Bounds(origin + rotation * new Vector3(0, Height * .5f, Depth * .5f), new Vector3(Width, Height, Depth));
                    if (!Inside(envelope, bounds) || layout.KeepOutViolation(bounds) != null) continue;
                    // A small inward shrink admits flush bearing faces, but not overlap with another service or structure.
                    var test = bounds; test.Expand(-.02f);
                    if (other.Any(r => test.Intersects(r.bounds)) || result.Any(p => test.Intersects(p.bounds))) continue;
                    chosen = new Placement { name = "Maintenance_" + side + "_" + index, zone = zone.name,
                        variant = index - 1, position = origin, rotation = rotation, bounds = bounds }; break;
                }
                if (chosen == null) throw new InvalidOperationException("No measured free mounting pocket in " + zone.name + "; defer this placement rather than narrowing a route.");
                result.Add(chosen);
            }
        return result;
    }

    internal static Mesh Compose(int variant)
    {
        var b = new ProceduralMeshBuilder();
        void Block(float x, float y, float z, float w, float h, float d) => b.AddBlock(new Vector3(x, y, z), new Vector3(w, h, d));
        // Wall-bearing spine, four bolted feet and a restrained open cable ladder.
        Block(0, .95f, .045f, Width, .10f, .09f);
        foreach (float x in new[] { -.88f, .88f })
        {
            Block(x, .95f, .04f, .10f, Height, .08f);
            foreach (float y in new[] { .14f, 1.76f })
            {
                Block(x, y, .016f, .24f, .24f, .032f);
                b.AddCylinder(new Vector3(x, y, .032f), new Vector3(x, y, .09f), .045f, .045f, 8, true);
            }
        }
        foreach (float y in new[] { 1.54f, 1.79f }) Block(0, y, .40f, Width, .06f, .08f);
        for (float x = -1.04f; x < 1.09f; x += .13f) Block(x, 1.665f, .40f, .035f, .25f, .08f);
        foreach (float x in new[] { -.64f, 0f, .64f })
            b.AddCylinder(new Vector3(x, .42f, .11f), new Vector3(x, 1.58f, .11f), .027f, .027f, 8, true);
        if (variant != 1)
        {
            // Closed service covers: no screen, button, status lamp or terminal component.
            foreach (float x in new[] { -.53f, .53f })
            {
                Block(x, .73f, .28f, .94f, 1.05f, .40f);
                Block(x, .73f, .495f, .82f, .94f, .03f);
                foreach (float dx in new[] { -.35f, .35f })
                    foreach (float y in new[] { .36f, 1.10f })
                        b.AddCylinder(new Vector3(x + dx, y, .51f), new Vector3(x + dx, y, .545f), .035f, .035f, 8, true);
                if (variant == 2)
                    for (float y = .49f; y < .99f; y += .10f) Block(x, y, .53f, .55f, .035f, .03f);
            }
        }
        else
        {
            // Parked hose drums, closed by flanges, mounted to the same service rack.
            foreach (float x in new[] { -.52f, .52f })
            {
                var a = new Vector3(x, .76f, .12f); var c = new Vector3(x, .76f, .52f);
                b.AddCylinder(a, c, .29f, .29f, 20, true);
                b.AddCylinder(a, a + Vector3.forward * .05f, .40f, .40f, 20, true);
                b.AddCylinder(c, c + Vector3.forward * .06f, .40f, .40f, 20, true);
                b.AddCylinder(c + Vector3.forward * .06f, c + Vector3.forward * .12f, .10f, .10f, 12, true);
            }
        }
        var mesh = b.ToMesh("SM_Catwalk_Maintenance_" + variant);
        // Persist UVs in the mesh; no unsaved MaterialPropertyBlock overrides.
        return mesh;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    static string MeshPath(int variant) => Folder + "/Meshes/SM_Catwalk_Maintenance_" + variant + ".asset";
    static void BuildAssets()
    {
        EnsureFolder(Folder + "/Meshes");
        for (int variant = 0; variant < 3; variant++)
        {
            var mesh = Compose(variant); var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(variant));
            if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); }
            else { AssetDatabase.CreateAsset(mesh, MeshPath(variant)); existing = mesh; }
            BossHallFramework.RequireOwnedAsset(existing, Owned);
        }
        AssetDatabase.SaveAssets();
    }

    static void Rebuild(BossHallFramework.Layout layout, List<Placement> plan) => BossHallFramework.RebuildSection(layout, Owned, Owner, section =>
    {
        foreach (var p in plan)
        {
            var go = new GameObject(p.name); Undo.RegisterCreatedObjectUndo(go, "Build Boss Hall maintenance rack");
            go.transform.SetParent(section, false); go.transform.SetPositionAndRotation(p.position, p.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(p.variant));
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = BossHallRendering.GetFinish("Gunmetal");
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
        }
    });

    public static string Validate(BossHallFramework.Layout layout)
    {
        BossHallFramework.Validate(layout); var plan = Plan(layout); var section = BossHallFramework.FindSection(Owned);
        if (section.childCount != plan.Count || Renderers().Length != plan.Count) throw new InvalidOperationException("Expected exactly six maintenance racks.");
        foreach (var p in plan)
        {
            var t = section.Find(p.name); var renderer = t ? t.GetComponent<MeshRenderer>() : null; var filter = t ? t.GetComponent<MeshFilter>() : null;
            if (!renderer || !filter || t.childCount != 0 || t.GetComponents<Component>().Length != 3 ||
                Vector3.Distance(t.position, p.position) > .001f || Quaternion.Angle(t.rotation, p.rotation) > .001f || t.localScale != Vector3.one ||
                !t.gameObject.activeSelf || t.gameObject.layer != 0 || t.gameObject.tag != "Untagged" || !renderer.enabled || renderer.renderingLayerMask != 1 ||
                AssetDatabase.GetAssetPath(filter.sharedMesh) != MeshPath(p.variant) || renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial != BossHallRendering.GetFinish("Gunmetal") ||
                renderer.shadowCastingMode != ShadowCastingMode.Off || !renderer.receiveShadows || !Inside(p.bounds, renderer.bounds))
                throw new InvalidOperationException("Altered maintenance rack " + p.name);
            var expected = Compose(p.variant);
            try
            {
                if (filter.sharedMesh.subMeshCount != 1 || !filter.sharedMesh.vertices.SequenceEqual(expected.vertices) || !filter.sharedMesh.triangles.SequenceEqual(expected.triangles) ||
                    !filter.sharedMesh.uv.SequenceEqual(expected.uv) || !filter.sharedMesh.normals.SequenceEqual(expected.normals) ||
                    !filter.sharedMesh.tangents.SequenceEqual(expected.tangents)) throw new InvalidOperationException("Altered maintenance mesh " + p.name);
            }
            finally { Object.DestroyImmediate(expected); }
        }
        return PlanReport(layout, plan) + "\nPASS: exact saved recipes, world transforms, material/component ownership and first-tier envelopes.";
    }

    public static string PlanReport(BossHallFramework.Layout layout, List<Placement> plan)
    {
        var lines = new List<string> { "corebuster_env_051: FIRST-TIER-ONLY maintenance plan; upper stairs/rails/tier-2 placements deferred.",
            "Six wall-mounted sealed racks; 3 first-principles meshes, Gunmetal reuse, 0 prefabs/colliders/carves/lights/emitters/audio/terminals.",
            "Every rack is above the reserved 2.6 m gallery headroom and inside a surveyed wall-service pocket; no reserved-volume exception." };
        foreach (var p in plan) lines.Add(p.name + ": " + p.zone + "; " + BossHallServices.B(p.bounds));
        var baseline = Measurements(layout);
        foreach (var deck in baseline.decks) lines.Add(FormattableString.Invariant($"{deck.name}: baseline clear width median {deck.clearWidthMedian:F2} m, minimum {deck.clearWidthMin:F2} m; protected original geometry retained."));
        var rays = new List<(Vector3 a, Vector3 b)> { (layout.Report.entryEye, (layout.Report.turret.head.min + layout.Report.turret.head.max) * .5f) };
        float eye = BossHallRenderingReview.EyeHeight();
        foreach (var deck in layout.Report.deckFootprints)
            for (float x = deck.min.x + .5f; x < deck.max.x; x += 1)
                for (float z = deck.min.z + .5f; z < deck.max.z; z += 1)
                    rays.Add((layout.Report.turret.firingOrigin, new Vector3(x, deck.max.y + eye, z)));
        foreach (var station in baseline.railTests)
        {
            rays.Add((station.eye, station.eye + station.outward * 1.5f));
            for (float x = layout.interior.min.x + 2; x < layout.interior.max.x; x += 4)
                for (float z = layout.interior.min.z + 2; z < layout.interior.max.z; z += 4)
                {
                    var target = new Vector3(x, layout.Report.floorTopY + 1, z);
                    if (Vector3.Distance(target, station.eye) <= 30 && Vector3.Dot(target - station.eye, station.outward) > 0) rays.Add((station.eye, target));
                }
        }
        int blocked = rays.Count(q => plan.Any(p => p.bounds.IntersectRay(new Ray(q.a, (q.b - q.a).normalized), out float d) && d >= 0 && d < Vector3.Distance(q.a, q.b)));
        if (blocked != 0) throw new InvalidOperationException("Planned maintenance bounds obstruct " + blocked + " firing rays.");
        lines.Add("PASS: " + rays.Count + " entry, turret/deck and rail-opening firing rays have zero new bound obstructions.");
        return string.Join("\n", lines);
    }

    static bool Inside(Bounds a, Bounds b) => a.min.x <= b.min.x + .001f && a.min.y <= b.min.y + .001f && a.min.z <= b.min.z + .001f &&
        a.max.x >= b.max.x - .001f && a.max.y >= b.max.y - .001f && a.max.z >= b.max.z - .001f;

    public static void PreflightBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); string sha = BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        var fresh = Measurements(layout); var plan = Plan(layout);
        var lines = new List<string> { "READ-ONLY ENV051 PREFLIGHT; no dressing assets or scene content created.", "Scene: " + sha, PlanReport(layout, plan) };
        foreach (int tier in new[] { 1, 2 })
        {
            var rails = fresh.rails.Where(r => r.tier == tier).ToArray(); var tests = fresh.railTests.Where(r => r.tier == tier).ToArray();
            lines.Add($"Tier {tier}: {rails.Length} original rail colliders, {rails.Count(r => r.crossesEye)} cross standing eye; " +
                $"{tests.Count(r => r.levelBlocked)}/{tests.Length} level stations blocked; {tests.Sum(r => r.railBlocked)}/{tests.Sum(r => r.shots)} downward shots blocked by original rails.");
        }
        lines.Add("Upper stairs and rail collision are preserved limitations, not repaired or accepted by this preflight.");
        if (SceneManager.GetActiveScene().isDirty || BossHallSurvey.Hash(BossHallSurvey.ScenePath) != sha) throw new InvalidOperationException("Preflight changed MainScene.");
        File.WriteAllText("Docs/BossHall/CatwalkPreflight_051.txt", string.Join("\n", lines) + "\n"); Debug.Log(string.Join("\n", lines));
    }

    /// <summary>Mesh-only preparation. Does not populate or save the scene, or resolve any dressing prerequisite.</summary>
    public static void AssetsBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); PlanReport(layout, Plan(layout));
        string sha = BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        var scene = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null); var shared = BossHallSnapshot.CaptureAssets();
        BuildAssets();
        var hashes = Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, BossHallSurvey.Hash);
        BuildAssets();
        BossHallSnapshot.Compare(hashes, Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, BossHallSurvey.Hash), "Deterministic maintenance assets");
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Shared assets/settings");
        BossHallSnapshot.Compare(scene, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null), "Mesh-only preparation scene");
        BossHallFramework.Validate(layout);
        if (SceneManager.GetActiveScene().isDirty || sha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Asset preparation changed MainScene.");
        var meshes = Enumerable.Range(0, 3).Select(i => AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(i))).ToArray();
        File.WriteAllText("Docs/BossHall/CatwalkAssets_051.txt", "PASS: three first-principles maintenance meshes regenerated byte-identically, with unchanged metadata.\n" +
            "Unique mesh triangles: " + meshes.Sum(m => m.triangles.Length / 3) + "; six planned instances: " + meshes.Sum(m => m.triangles.Length / 3) * 2 + " triangles.\n" +
            "Full scene state and " + shared.Count + " shared asset/settings entries unchanged; no material edit or saved scene content.\nScene: " + sha + "\n");
    }

    /// <summary>Verify the authoring transaction using transient objects, then Undo/discard. Never saves MainScene.</summary>
    public static void DryRunBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); var plan = Plan(layout); PlanReport(layout, plan);
        if (Enumerable.Range(0, 3).Any(i => !AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(i))))
            throw new InvalidOperationException("Prepare maintenance meshes with AssetsBatch first.");
        string sha = BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        var states = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null); var shared = BossHallSnapshot.CaptureAssets();
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        try
        {
            Rebuild(layout, plan); Validate(layout); NegativeGuards(layout);
            BossHallFramework.RebuildSection(layout, Owned, Owner, _ => { }); Undo.PerformUndo(); Validate(layout);
        }
        finally
        {
            Undo.RevertAllDownToGroup(group);
            // Reload discards the transient dirty flag as well as any failed-test objects; no scene save occurs.
            EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        }
        BossHallSnapshot.Compare(states, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null), "Dry-run scene state");
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Dry-run shared assets/settings");
        BossHallFramework.Validate(BossHallFramework.MeasureLayout());
        if (SceneManager.GetActiveScene().isDirty || sha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Dry run changed MainScene.");
        File.WriteAllText("Docs/BossHall/CatwalkDryRun_051.txt", "PASS: transient isolated rebuild, exact mesh/transform/material/component validation, five negative guards and clear/Undo.\n" +
            "All test objects discarded; full scene state and shared assets unchanged.\nScene: " + sha + "\n" +
            "Saved authoring, populated save/reload, gameplay prerequisite and route disposition remain pending.\n");
    }

    [MenuItem("Tools/CoreBuster/Build Boss Hall First-Tier Maintenance")]
    public static void BuildMenu()
    {
        var layout = BossHallFramework.MeasureLayout(); var plan = Plan(layout);
        PlanReport(layout, plan); BuildAssets(); Rebuild(layout, plan); Debug.Log(Validate(layout));
    }

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); var section = BossHallFramework.FindSection(Owned);
        var outside = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), section, BossHallFramework.FindRoot());
        var assets = BossHallSnapshot.CaptureAssets(); var plan = Plan(layout); PlanReport(layout, plan);
        BuildAssets(); Rebuild(layout, plan); Validate(layout); NegativeGuards(layout);
        BossHallFramework.RebuildSection(layout, Owned, Owner, t => { }); Undo.PerformUndo(); Validate(layout);
        layout = BossHallFramework.SaveAndReload(layout);
        BossHallSnapshot.Compare(outside, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindSection(Owned), BossHallFramework.FindRoot()), "Unowned hall sections");
        BossHallSnapshot.Compare(assets, BossHallSnapshot.CaptureAssets(), "Shared assets/settings");
        var hashes = Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, BossHallSurvey.Hash);
        BuildAssets(); BossHallSnapshot.Compare(hashes, Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, BossHallSurvey.Hash), "Deterministic maintenance assets");
        BossHallRenderingReview.ValidateNeighbours();
        File.WriteAllText("Docs/BossHall/Catwalk_051.txt", Validate(layout) + "\nPASS: negative guards, isolated clear/Undo, save/reload, shared assets, other sections and deterministic meshes.\nScene SHA-256: " + BossHallSurvey.Hash(BossHallSurvey.ScenePath) + "\n");
    }

    static void NegativeGuards(BossHallFramework.Layout layout)
    {
        void Reject(Action action, string label)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Missing negative guard: " + label);
        }
        var renderer = Renderers().First(); var t = renderer.transform; var position = t.position;
        try { t.position -= Vector3.up * .2f; Physics.SyncTransforms(); Reject(() => Validate(layout), "gallery headroom"); }
        finally { t.position = position; Physics.SyncTransforms(); }
        var material = renderer.sharedMaterial;
        try { renderer.sharedMaterial = BossHallRendering.GetFinish("WarningRed"); Reject(() => Validate(layout), "unplanned finish"); }
        finally { renderer.sharedMaterial = material; }
        var collider = t.gameObject.AddComponent<BoxCollider>();
        try { Reject(() => Validate(layout), "unreachable fixture collision"); }
        finally { Object.DestroyImmediate(collider); }
        var light = t.gameObject.AddComponent<Light>();
        try { Reject(() => BossHallFramework.Validate(layout), "light outside Lighting"); }
        finally { Object.DestroyImmediate(light); }
        Reject(() => BossHallFramework.RebuildSection(layout, Owned, "corebuster_env_050", _ => { }), "wrong section owner");
        Validate(layout);
    }

    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        string sha = BossHallSurvey.Hash(BossHallSurvey.ScenePath); Debug.Log(Validate(BossHallFramework.MeasureLayout()));
        if (SceneManager.GetActiveScene().isDirty || sha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Validation changed MainScene.");
    }
}

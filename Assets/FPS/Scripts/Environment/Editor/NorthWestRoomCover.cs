using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = NorthWestRoomFramework.Section;

/// <summary>
/// Low loop-anchor cover for corebuster_env_034. The three anchors (A1 to A3) come from NorthWestRoomDensityPlan.json
/// (corebuster_env_040); each is one low piece with a single box collider (at most 1.2 m above the visible floor) and one
/// matching carving obstacle. Only Level/NorthWest_Room/Cover is rebuilt. Geometry is axis-aligned and in world coordinates.
/// </summary>
public static class NorthWestRoomCover
{
    const string Owner = "corebuster_env_034";
    const string Folder = "Assets/FPS/Art/NorthWestRoom/Cover";
    const string PlanPath = "Assets/FPS/Art/NorthWestRoom/NorthWestRoomDensityPlan.json";
    const string Output = "ReviewCaptures/NorthWestRoom/034";
    const string Mat = "Assets/FPS/Art/NorthWestRoom/";
    const float Pad = .03f, MaxVisibleHeight = 1.12f, ColliderLimit = 1.2f;
    static readonly string[] Expected = { "A1", "A2", "A3" };

    [Serializable] class P2 { public float x, z; }
    [Serializable] class R2 { public P2 min, max; }
    [Serializable] class PlanAnchor { public string id, owner; public R2 footprint; public float maxColliderHeight; }
    [Serializable] class Plan { public PlanAnchor[] anchors; }

    struct Parts { public Mesh cube, cylinder; public Material steel, graphite, hazard, tan, rust, brass, enamel; }
    static Parts P;
    static float F;

    // ---- entry points ------------------------------------------------------------------------------------------

    [MenuItem("Tools/CoreBuster/Build North-West Room Cover")]
    public static void BuildMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        NorthWestRoomFramework.Build(layout);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build North-West Room Cover");
        try { Build(layout); RequireValid(layout); Undo.CollapseUndoOperations(group); }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = NorthWestRoomFramework.MeasureLayout();
        NorthWestRoomFramework.Build(layout);
        var before = SnapshotOutside();
        var protectedMaterials = NorthWestRoomFramework.CaptureProtectedMaterialHashes();
        Build(layout);
        VerifyIsolatedUndo(layout);
        RequireValid(layout);
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Could not save Room 3 cover.");
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        var after = SnapshotOutside();
        if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out string value) || value != p.Value))
            throw new InvalidOperationException("A scene object outside Cover changed.");
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Build.txt", $"corebuster_env_034: {Expected.Length} loop-anchor cover pieces; isolation, keep-outs, plan footprints, collider height, material hashes and save/reload passed ({before.Count} outside transforms unchanged).\n");
        Debug.Log($"NORTH-WEST COVER VERIFIED: {before.Count} outside transforms unchanged.");
    }

    [MenuItem("Tools/CoreBuster/Validate North-West Room Cover")]
    public static void ValidateMenu() => RequireValid(NorthWestRoomFramework.MeasureLayout());

    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        ValidateMenu();
        var layout = NorthWestRoomFramework.MeasureLayout();
        var report = NorthWestRoomDensity.Measure(SceneManager.GetActiveScene(), layout);
        Debug.Log($"NORTH-WEST DENSITY AFTER COVER: loops clear {report.clearLoops}; clear floor {report.clearFloorFraction:P1}; errors {report.errors.Count}; targets not met: {string.Join(" | ", report.targetsNotMet)}");
        foreach (var e in report.errors) Debug.LogError(e);
    }

    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        CaptureMenu();
    }

    [MenuItem("Tools/CoreBuster/Capture North-West Room Cover")]
    public static void CaptureMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        float floor = layout.Floor, eye = floor + layout.report.playerEyeHeight;
        var views = new[]
        {
            ("A1_Power_Housing", new Vector3(-238.2f, eye, 118.5f), new Vector3(-238.2f, floor + .6f, 124.6f)),
            ("A2_Transformer_Base", new Vector3(-230.6f, eye, 113f), new Vector3(-230.6f, floor + .6f, 106.6f)),
            ("A3_Equipment_Case", new Vector3(-231f, eye, 120f), new Vector3(-225.2f, floor + .6f, 120f)),
            ("A1_Loop_Aisle", new Vector3(-243.5f, eye, 127.1f), new Vector3(-237f, floor + .6f, 124.6f)),
            ("A3_Loop_Aisle", new Vector3(-223f, eye, 124.5f), new Vector3(-225.2f, floor + .6f, 119.5f)),
        };
        Directory.CreateDirectory(Output);
        var go = new GameObject("Temporary Room 3 cover review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 70; camera.nearClipPlane = .05f; camera.farClipPlane = 200;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        // Unsaved review lamp: the room has no lighting until corebuster_env_037.
        var lamp = new GameObject("Temporary cover review lamp") { hideFlags = HideFlags.HideAndDontSave };
        var light = lamp.AddComponent<Light>();
        light.type = LightType.Point; light.range = 22; light.intensity = 9; light.color = new Color(1f, .78f, .5f); light.shadows = LightShadows.None;
        try
        {
            foreach (var view in views)
            {
                lamp.transform.position = Vector3.Lerp(view.Item2, view.Item3, .5f) + Vector3.up * 2.4f;
                camera.transform.SetPositionAndRotation(view.Item2, Quaternion.LookRotation(view.Item3 - view.Item2));
                var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                try
                {
                    camera.targetTexture = target;
                    camera.Render(); camera.Render();
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    texture.Apply();
                    File.WriteAllBytes(Output + "/" + view.Item1 + ".png", texture.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null;
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                    Object.DestroyImmediate(texture);
                }
            }
        }
        finally { Object.DestroyImmediate(lamp); Object.DestroyImmediate(go); }
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Cover capture dirtied MainScene.");
    }

    // ---- build -------------------------------------------------------------------------------------------------

    static List<PlanAnchor> OwnedAnchors()
    {
        var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(PlanPath));
        var anchors = plan.anchors.Where(a => a.owner == Owner).ToList();
        if (anchors.Count != Expected.Length || Expected.Any(id => anchors.All(a => a.id != id)))
            throw new InvalidOperationException($"Density plan assigns {anchors.Count} anchors to {Owner}; this builder expects {string.Join(",", Expected)}.");
        return anchors;
    }

    static void Build(NorthWestRoomFramework.Layout layout)
    {
        var anchors = OwnedAnchors();
        LoadParts();
        F = layout.Floor;
        Transform section = NorthWestRoomFramework.BeginSectionRebuild(layout, Section.Cover, Owner);
        foreach (var anchor in anchors) BuildPiece(section, anchor);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static void LoadParts()
    {
        EnsureFolder(Folder + "/Meshes");
        var cube = new ProceduralMeshBuilder(); cube.AddBlock(Vector3.zero, Vector3.one);
        var cyl = new ProceduralMeshBuilder(); cyl.AddCylinder(Vector3.down * .5f, Vector3.up * .5f, .5f, .5f, 24, true);
        P = new Parts
        {
            cube = Save(cube.ToMesh("SM_Cover_Unit_Cube"), "Meshes/SM_Cover_Unit_Cube.asset"),
            cylinder = Save(cyl.ToMesh("SM_Cover_Unit_Cylinder"), "Meshes/SM_Cover_Unit_Cylinder.asset"),
            steel = Load<Material>(Mat + "Electrical_Systems/Materials/M_Electrical_Steel.mat"),
            graphite = Load<Material>(Mat + "Electrical_Systems/Materials/M_Electrical_Graphite.mat"),
            hazard = Load<Material>(Mat + "Turbine_Machinery/Materials/M_Turbine_Hazard.mat"),
            tan = Load<Material>(Mat + "Perimeter_Machinery/Materials/M_Perimeter_Tan.mat"),
            rust = Load<Material>(Mat + "Pressure_Systems/Materials/M_Pressure_Rust.mat"),
            brass = Load<Material>(Mat + "Pressure_Systems/Materials/M_Pressure_Brass.mat"),
            enamel = Load<Material>(Mat + "Pressure_Systems/Materials/M_Pressure_Enamel.mat"),
        };
    }

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build North-West Room cover");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void Visual(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
    {
        var t = Group(parent, name);
        t.position = position;
        t.localScale = scale;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    // Box from its floor-relative bottom height and height.
    static void Box(Transform p, string name, float cx, float cz, float sx, float sz, float y0, float h, Material m) =>
        Visual(p, name, P.cube, m, new Vector3(cx, F + y0 + h * .5f, cz), new Vector3(sx, h, sz));

    static void Drum(Transform p, string name, float cx, float cz, float r, float y0, float h, Material m) =>
        Visual(p, name, P.cylinder, m, new Vector3(cx, F + y0 + h * .5f, cz), new Vector3(2 * r, h, 2 * r));

    // Cylinder along X or Z (rotation by 90 degrees is exact, so renderer bounds stay tight).
    static void Run(Transform p, string name, bool alongX, float cx, float cz, float length, float r, float y, Material m)
    {
        var t = Group(p, name);
        t.position = new Vector3(cx, F + y, cz);
        t.rotation = Quaternion.Euler(alongX ? new Vector3(0, 0, 90) : new Vector3(90, 0, 0));
        t.localScale = new Vector3(2 * r, length, 2 * r);
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = P.cylinder;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = m;
    }

    static void BuildPiece(Transform section, PlanAnchor anchor)
    {
        var f = anchor.footprint;
        float x0 = f.min.x + Pad, x1 = f.max.x - Pad, z0 = f.min.z + Pad, z1 = f.max.z - Pad;
        float cx = (x0 + x1) * .5f, cz = (z0 + z1) * .5f, sx = x1 - x0, sz = z1 - z0;
        Transform root = Group(section, "Cover_" + anchor.id);
        root.position = new Vector3(cx, F, cz);
        switch (anchor.id)
        {
            case "A1": PowerHousing(root, cx, cz, sx, sz); break;
            case "A2": TransformerBase(root, cx, cz, sx, sz); break;
            default: EquipmentCase(root, cx, cz, sx, sz); break;
        }
        Bounds union = default; bool first = true;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (first) { union = r.bounds; first = false; } else union.Encapsulate(r.bounds);
        // One consolidated box per piece, matched by one carving obstacle.
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = union.center - root.position;
        box.size = union.size;
        var obstacle = root.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = box.center;
        obstacle.size = box.size;
        obstacle.carving = true;
    }

    // A1: long power-routing housing with a hazard-edged plinth, a routing cabinet and conduit runs along its top.
    static void PowerHousing(Transform p, float cx, float cz, float sx, float sz)
    {
        Box(p, "Plinth", cx, cz, sx, sz, 0, .12f, P.tan);
        Box(p, "Hazard_Edge_S", cx, cz - sz * .5f + .03f, sx - .1f, .04f, .12f, .02f, P.hazard);
        Box(p, "Hazard_Edge_N", cx, cz + sz * .5f - .03f, sx - .1f, .04f, .12f, .02f, P.hazard);
        Box(p, "Body", cx, cz, sx - .3f, sz - .4f, .14f, .70f, P.graphite);
        Box(p, "Lid", cx, cz, sx - .22f, sz - .32f, .84f, .06f, P.steel);
        for (int i = 0; i < 3; i++)
        {
            float pitch = (sx - .6f) / 3f, u = cx + (i - 1) * pitch;
            Box(p, $"Door_{i}", u, cz - (sz - .4f) * .5f - .01f, pitch - .2f, .02f, .24f, .5f, i == 1 ? P.tan : P.steel);
            Box(p, $"Handle_{i}", u + .22f, cz - (sz - .4f) * .5f - .03f, .04f, .03f, .46f, .16f, P.graphite);
            Box(p, $"Indicator_{i}", u - .25f, cz - (sz - .4f) * .5f - .02f, .06f, .015f, .64f, .06f, P.hazard);
        }
        Run(p, "Conduit_Rust", true, cx, cz - .15f, sx - .7f, .045f, .96f, P.rust);
        Run(p, "Conduit_Brass", true, cx, cz + .15f, sx - .7f, .045f, .96f, P.brass);
        for (int i = 0; i < 3; i++)
            Box(p, $"Conduit_Clip_{i}", cx + (i - 1) * (sx - 1.2f) * .5f, cz, .06f, .5f, .90f, .12f, P.graphite);
        Box(p, "Junction_E", cx + sx * .5f - .24f, cz, .22f, .5f, .90f, .2f, P.steel);
        Box(p, "Junction_W", cx - sx * .5f + .24f, cz, .22f, .5f, .90f, .2f, P.steel);
    }

    // A2: transformer base with two squat coil drums, a steel bus box between them and brass terminal posts.
    static void TransformerBase(Transform p, float cx, float cz, float sx, float sz)
    {
        Box(p, "Plinth", cx, cz, sx, sz, 0, .14f, P.tan);
        Box(p, "Hazard_Edge_E", cx + sx * .5f - .03f, cz, .04f, sz - .1f, .14f, .02f, P.hazard);
        Box(p, "Hazard_Edge_W", cx - sx * .5f + .03f, cz, .04f, sz - .1f, .14f, .02f, P.hazard);
        float r = Mathf.Min(sz * .5f - .12f, .72f), dx = sx * .5f - r - .12f;
        foreach (float s in new[] { -1f, 1f })
        {
            float x = cx + s * dx;
            Drum(p, "Coil", x, cz, r, .16f, .70f, P.enamel);
            Drum(p, "Coil_Band_Low", x, cz, r + .02f, .30f, .06f, P.graphite);
            Drum(p, "Coil_Band_High", x, cz, r + .02f, .62f, .06f, P.graphite);
            Drum(p, "Coil_Cap", x, cz, r - .06f, .86f, .06f, P.steel);
            Drum(p, "Terminal_Post", x - s * .2f, cz - .2f, .04f, .92f, .18f, P.brass);
            Drum(p, "Terminal_Post_B", x + s * .2f, cz + .2f, .04f, .92f, .18f, P.brass);
        }
        Box(p, "Bus_Box", cx, cz, sx - 2 * (dx + r) + .5f, sz * .5f, .16f, .52f, P.graphite);
        Box(p, "Bus_Lid", cx, cz, sx - 2 * (dx + r) + .6f, sz * .5f + .08f, .68f, .05f, P.steel);
        Run(p, "Bus_Cable", true, cx, cz, 2 * dx, .05f, .86f, P.rust);
    }

    // A3: reinforced equipment case, long axis north-south, with a tan lid, steel latches and hazard bands.
    static void EquipmentCase(Transform p, float cx, float cz, float sx, float sz)
    {
        Box(p, "Skids", cx, cz, sx, sz, 0, .08f, P.graphite);
        Box(p, "Case", cx, cz, sx - .1f, sz - .1f, .08f, .86f, P.steel);
        Box(p, "Lid", cx, cz, sx - .04f, sz - .04f, .94f, .08f, P.tan);
        for (int i = 0; i < 3; i++)
        {
            float z = cz + (i - 1) * (sz - .7f) * .5f;
            Box(p, $"Band_{i}", cx, z, sx, .2f, .08f, .94f, P.graphite);
            Box(p, $"Hazard_W_{i}", cx - sx * .5f - 0.0f + .02f, z, .02f, .12f, .45f, .2f, P.hazard);
            Box(p, $"Latch_E_{i}", cx + sx * .5f - .04f, z, .05f, .12f, .55f, .22f, P.brass);
        }
        Box(p, "Handle_N", cx, cz + sz * .5f - .06f, .5f, .05f, .55f, .06f, P.graphite);
        Box(p, "Handle_S", cx, cz - sz * .5f + .06f, .5f, .05f, .55f, .06f, P.graphite);
        Box(p, "Stencil_Plate", cx - sx * .5f + .06f, cz, .02f, .6f, .6f, .3f, P.tan);
        Run(p, "Lid_Cable", false, cx, cz, sz - .8f, .035f, 1.06f, P.rust);
    }

    // ---- validation --------------------------------------------------------------------------------------------

    static void RequireValid(NorthWestRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        if (!NorthWestRoomFramework.Validate(scene, layout, true) || !NorthWestRoomFramework.ValidateSection(scene, layout, Section.Cover, true))
            throw new InvalidOperationException("North-West Room framework or cover section validation failed.");
        var cover = NorthWestRoomFramework.FindSection(scene, Section.Cover);
        var anchors = OwnedAnchors().ToDictionary(a => "Cover_" + a.id);
        var errors = new List<string>();
        float floor = layout.Floor;
        var roots = cover.Cast<Transform>().ToArray();
        if (roots.Length != Expected.Length || roots.Any(r => !anchors.ContainsKey(r.name))) errors.Add("Expected three cover pieces named from the density plan.");
        foreach (var root in roots)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger).ToArray();
            if (colliders.Length != 1) errors.Add($"{root.name} has {colliders.Length} colliders; expected one box.");
            else if (colliders[0].bounds.max.y > floor + ColliderLimit + .001f) errors.Add($"{root.name} collider is {colliders[0].bounds.max.y - floor:F2} m tall (limit {ColliderLimit} m).");
            var obstacles = root.GetComponentsInChildren<NavMeshObstacle>(true).Where(o => o.enabled && o.carving).ToArray();
            if (obstacles.Length != 1) errors.Add($"{root.name} has {obstacles.Length} carving obstacles; expected one.");
            if (!anchors.TryGetValue(root.name, out var plan)) continue;
            var foot = plan.footprint;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var b = renderer.bounds;
                if (b.min.x < foot.min.x - .001f || b.max.x > foot.max.x + .001f || b.min.z < foot.min.z - .001f || b.max.z > foot.max.z + .001f)
                    errors.Add($"{root.name}/{renderer.name} leaves its density-plan footprint.");
                if (b.max.y > floor + MaxVisibleHeight) errors.Add($"{root.name}/{renderer.name} rises {b.max.y - floor:F2} m above the floor (limit {MaxVisibleHeight} m).");
                if (b.min.y < floor - .01f) errors.Add($"{root.name}/{renderer.name} is below the floor.");
            }
        }
        if (cover.GetComponentsInChildren<Light>(true).Length != 0) errors.Add("Cover must carry no lights (corebuster_env_037).");
        if (cover.GetComponentsInChildren<MonoBehaviour>(true).Length != 0) errors.Add("Cover must be script-free.");
        int newMaterials = cover.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m && AssetDatabase.GetAssetPath(m).StartsWith(Folder + "/")).Distinct().Count();
        if (newMaterials != 0) errors.Add($"Cover owns {newMaterials} new materials; it should reuse the room's.");
        if (errors.Count > 0) throw new InvalidOperationException("North-West cover checks failed (" + errors.Count + "):\n  " + string.Join("\n  ", errors.Take(40)));
        Debug.Log($"NORTH-WEST COVER CHECKS PASSED: {roots.Length} pieces, one box collider (at most {ColliderLimit} m) and one carving obstacle each, inside plan footprints, no lights or scripts, no new materials.");
    }

    static void VerifyIsolatedUndo(NorthWestRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        var cover = NorthWestRoomFramework.FindSection(scene, Section.Cover);
        var pressure = NorthWestRoomFramework.FindSection(scene, Section.PressureSystems);
        int c = cover.childCount, p = pressure.childCount;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Verify isolated cover section Undo");
        NorthWestRoomFramework.BeginSectionRebuild(layout, Section.Cover, Owner);
        Undo.CollapseUndoOperations(group);
        if (cover.childCount != 0 || pressure.childCount != p) throw new InvalidOperationException("Cover section clear changed another section.");
        Undo.PerformUndo();
        if (cover.childCount != c || pressure.childCount != p) throw new InvalidOperationException("Cover section Undo did not restore exactly the cleared section.");
    }

    // ---- assets and snapshots ----------------------------------------------------------------------------------

    static T Save<T>(T generated, string relative) where T : Object
    {
        string path = Folder + "/" + relative;
        generated.name = Path.GetFileNameWithoutExtension(relative);
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing) { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
        else { AssetDatabase.CreateAsset(generated, path); existing = generated; }
        NorthWestRoomFramework.RequireOwnedAsset(existing, Section.Cover);
        return existing;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
    }

    static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ??
        throw new InvalidOperationException("Missing cover dependency: " + path);

    static Dictionary<string, string> SnapshotOutside()
    {
        var scene = SceneManager.GetActiveScene();
        var owned = NorthWestRoomFramework.FindSection(scene, Section.Cover);
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => !t.IsChildOf(owned))
            .ToDictionary(t => GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), t =>
            {
                var renderer = t.GetComponent<Renderer>();
                string mats = renderer ? string.Join(",", renderer.sharedMaterials.Select(m => m ? AssetDatabase.GetAssetPath(m) : "null")) : "";
                return FormattableString.Invariant($"{t.name}|{(t.parent ? GlobalObjectId.GetGlobalObjectIdSlow(t.parent).ToString() : "root")}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}|{t.gameObject.activeSelf}|{mats}");
            });
    }
}

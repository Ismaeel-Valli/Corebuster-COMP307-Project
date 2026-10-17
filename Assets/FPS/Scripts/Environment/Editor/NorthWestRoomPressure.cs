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
/// Pressure systems, wall services and overhead for corebuster_env_033. Bay footprints come from
/// NorthWestRoomDensityPlan.json (corebuster_env_040); this builder owns the ten bays that JSON assigns to
/// corebuster_env_033 plus the Wall_Services and Overhead sections. All geometry is authored in world coordinates
/// under identity-transform containers, so every part is axis-aligned and its renderer bounds are exact.
/// </summary>
public static class NorthWestRoomPressure
{
    const string Owner = "corebuster_env_033";
    const string Folder = "Assets/FPS/Art/NorthWestRoom/Pressure_Systems";
    const string PlanPath = "Assets/FPS/Art/NorthWestRoom/NorthWestRoomDensityPlan.json";
    const string Output = "ReviewCaptures/NorthWestRoom/033";
    const string KitRoot = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit/";
    const string GraphitePath = "Assets/FPS/Art/NorthWestRoom/Electrical_Systems/Materials/M_Electrical_Graphite.mat";
    const string SteelPath = "Assets/FPS/Art/NorthWestRoom/Electrical_Systems/Materials/M_Electrical_Steel.mat";
    const string HazardPath = "Assets/FPS/Art/NorthWestRoom/Turbine_Machinery/Materials/M_Turbine_Hazard.mat";
    const string TanPath = "Assets/FPS/Art/NorthWestRoom/Perimeter_Machinery/Materials/M_Perimeter_Tan.mat";
    const int ExpectedBays = 10, ExpectedObstacles = 8, PrefabCeiling = 145, StraightCeiling = 20, NewMaterialCeiling = 3;
    // Heights are absolute world Y (visible floor 0.18). Bays stop at the wall-service band; overhead starts above it.
    const float BayTop = 6.0f, ServiceMinAbsY = 3.59f, ServiceMaxAbsY = 6.18f, OverheadMinAbsY = 6.17f, OverheadMaxAbsY = 10.78f, Inset = .3f;

    [Serializable] class P2 { public float x, z; }
    [Serializable] class R2 { public P2 min, max; }
    [Serializable] class PlanBay { public string id, owner; public R2 footprint; }
    [Serializable] class Plan { public PlanBay[] bays; }

    struct Parts
    {
        public Mesh cube, cylinder;
        public Material rust, brass, enamel, steel, graphite, hazard, tan;
        public GameObject tank, valve, reducer;
    }

    struct Riser { public float x, z, r; }

    static Parts P;
    static float F;
    // Risers register their world position so the overhead builder can join them to the matching main.
    static readonly Dictionary<string, List<Riser>> Risers = new Dictionary<string, List<Riser>>();

    static float Y(float h) => F + h;

    // ---- entry points ------------------------------------------------------------------------------------------

    [MenuItem("Tools/CoreBuster/Build North-West Room Pressure Systems")]
    public static void BuildMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        NorthWestRoomFramework.Build(layout);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build North-West Room Pressure Systems");
        try { Build(layout); RequireValid(layout); Undo.CollapseUndoOperations(group); }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = NorthWestRoomFramework.MeasureLayout();
        NorthWestRoomFramework.Build(layout); // creates only missing containers
        var before = SnapshotOutside();
        var protectedMaterials = NorthWestRoomFramework.CaptureProtectedMaterialHashes();
        Build(layout);
        VerifyIsolatedUndo(layout);
        RequireValid(layout);
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Could not save Room 3 pressure systems.");
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        var after = SnapshotOutside();
        if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out string value) || value != p.Value))
            throw new InvalidOperationException("A scene object outside Pressure_Systems, Wall_Services and Overhead changed.");
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Build.txt", $"corebuster_env_033: {ExpectedBays} pressure bays, wall services and overhead; isolation, keep-outs, plan footprints, overhead pairwise intersections, material hashes and save/reload passed.\n");
        Debug.Log($"NORTH-WEST PRESSURE SYSTEMS VERIFIED: {before.Count} outside transforms unchanged.");
    }

    /// <summary>env_046: rebuild only Pressure_Systems with existing saved meshes/finishes.
    /// No asset regeneration, wall services, overhead or surface partition rebuild; aligns two existing lights.</summary>
    public static void BuildDropBaySlotsBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = NorthWestRoomFramework.MeasureLayout();
        var before = SnapshotOutside(new[] { Section.PressureSystems }, NorthWestRoomLighting.DropBayFixtures());
        var protectedMaterials = NorthWestRoomFramework.CaptureProtectedMaterialHashes();
        LoadParts(false);
        F = layout.Floor;
        Risers.Clear();
        var pressure = NorthWestRoomFramework.BeginSectionRebuild(layout, Section.PressureSystems, Owner);
        foreach (var bay in OwnedBays()) BuildBay(layout, pressure, bay);
        VerifyIsolatedUndo(layout);
        NorthWestRoomLighting.RefreshDropBayFixtures(layout);
        RequireValid(layout);
        NorthWestRoomRendering.RequireValid(layout);
        NorthWestRoomLighting.Validate(layout);
        if (!NorthWestRoomDensity.Check(SceneManager.GetActiveScene(), layout, true, true))
            throw new InvalidOperationException("Drop-bay fix failed the density targets.");
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
            throw new InvalidOperationException("Could not save drop-bay slots.");
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        NorthWestRoomRendering.RequireValid(layout);
        NorthWestRoomLighting.Validate(layout);
        var after = SnapshotOutside(new[] { Section.PressureSystems }, NorthWestRoomLighting.DropBayFixtures());
        if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out string value) || value != p.Value))
            throw new InvalidOperationException("Drop-bay fix changed an object outside Pressure_Systems and its two adjusted lighting fixtures.");
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        Directory.CreateDirectory("ReviewCaptures/NorthWestRoom/046");
        File.WriteAllText("ReviewCaptures/NorthWestRoom/046/Build.txt",
            $"corebuster_env_046: Pressure_Systems rebuild and two existing drop-bay fixture alignments; four visible drop skids, eight pressure carving hulls; isolated Undo, plan footprints, density targets, rendering, lighting, shared materials and save/reload passed. {before.Count} outside transforms/material assignments unchanged.\nOwner live-wave sprint remains required.\n");
        Debug.Log("NORTH-WEST DROP-BAY BUILD VERIFIED: " + before.Count + " outside transforms unchanged.");
    }

    [MenuItem("Tools/CoreBuster/Validate North-West Room Pressure Systems")]
    public static void ValidateMenu() => RequireValid(NorthWestRoomFramework.MeasureLayout());

    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        ValidateMenu();
    }

    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        CaptureMenu();
    }

    [MenuItem("Tools/CoreBuster/Capture North-West Room Pressure Systems")]
    public static void CaptureMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        float floor = layout.Floor, eye = floor + layout.report.playerEyeHeight;
        var views = new[]
        {
            ("SW_Corner_Tanks", new Vector3(-243.5f, eye, 108f), new Vector3(-249.3f, floor + 2f, 101f)),
            ("South_Manifolds", new Vector3(-234.5f, eye, 109f), new Vector3(-234.5f, floor + 2f, 100.9f)),
            ("SE_Corner", new Vector3(-226f, eye, 109f), new Vector3(-219.6f, floor + 2f, 101.2f)),
            ("East_Drops_And_Services", new Vector3(-230f, eye, 117f), new Vector3(-219.2f, floor + 3.6f, 124f)),
            ("North_Drops", new Vector3(-234.5f, eye, 122f), new Vector3(-234f, floor + 3.4f, 130.6f)),
            ("West_Corner", new Vector3(-241f, eye, 123f), new Vector3(-249.9f, floor + 3.4f, 129.6f)),
            ("Wall_Services_West", new Vector3(-240f, eye, 110f), new Vector3(-250f, floor + 4.9f, 110f)),
            ("Overhead_Arena", new Vector3(-234.7f, eye, 107f), new Vector3(-236f, 9.0f, 124f)),
            ("Overhead_North", new Vector3(-234.7f, eye, 118f), new Vector3(-236f, 9.0f, 130.2f)),
        };
        Directory.CreateDirectory(Output);
        var go = new GameObject("Temporary Room 3 pressure review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 70;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 200;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        // Unsaved review lamp: the room has no lighting until corebuster_env_037.
        var lamp = new GameObject("Temporary pressure review lamp") { hideFlags = HideFlags.HideAndDontSave };
        var light = lamp.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 22;
        light.intensity = 9;
        light.color = new Color(1f, .78f, .5f);
        light.shadows = LightShadows.None;
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
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Pressure capture dirtied MainScene.");
    }

    // ---- build -------------------------------------------------------------------------------------------------

    static List<PlanBay> OwnedBays()
    {
        var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(PlanPath));
        var bays = plan.bays.Where(b => b.owner == Owner).ToList();
        string[] expected = { "S1", "S3", "S5", "S7", "E5", "E7", "N3", "N6", "N9", "W2" };
        if (bays.Count != ExpectedBays || expected.Any(id => bays.All(b => b.id != id)))
            throw new InvalidOperationException($"Density plan assigns {bays.Count} bays to {Owner}; this builder expects {string.Join(",", expected)}.");
        return bays;
    }

    static void Build(NorthWestRoomFramework.Layout layout)
    {
        var bays = OwnedBays();
        LoadParts();
        F = layout.Floor;
        Risers.Clear();
        Transform pressure = NorthWestRoomFramework.BeginSectionRebuild(layout, Section.PressureSystems, Owner);
        Transform services = NorthWestRoomFramework.BeginSectionRebuild(layout, Section.WallServices, Owner);
        Transform overhead = NorthWestRoomFramework.BeginSectionRebuild(layout, Section.Overhead, Owner);
        foreach (var bay in bays) BuildBay(layout, pressure, bay);
        BuildServices(layout, services);
        BuildOverhead(layout, overhead);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static void LoadParts(bool regenerate = true)
    {
        EnsureFolder(Folder + "/Meshes");
        EnsureFolder(Folder + "/Materials");
        var cube = new ProceduralMeshBuilder(); cube.AddBlock(Vector3.zero, Vector3.one);
        var cyl = new ProceduralMeshBuilder(); cyl.AddCylinder(Vector3.down * .5f, Vector3.up * .5f, .5f, .5f, 20, true);
        P = new Parts
        {
            cube = regenerate ? Save(cube.ToMesh("SM_Pressure_Unit_Cube"), "Meshes/SM_Pressure_Unit_Cube.asset") : Load<Mesh>(Folder + "/Meshes/SM_Pressure_Unit_Cube.asset"),
            cylinder = regenerate ? Save(cyl.ToMesh("SM_Pressure_Unit_Cylinder"), "Meshes/SM_Pressure_Unit_Cylinder.asset") : Load<Mesh>(Folder + "/Meshes/SM_Pressure_Unit_Cylinder.asset"),
            // Palette-lock swatches (NorthWestRoomPalette.json): rust-red pipework and tan/ochre plating.
            rust = regenerate ? Finish("M_Pressure_Rust", new Color(.48f, .16f, .08f), .22f, .24f) : Load<Material>(Folder + "/Materials/M_Pressure_Rust.mat"),
            brass = regenerate ? Finish("M_Pressure_Brass", new Color(.62f, .42f, .16f), .38f, .40f) : Load<Material>(Folder + "/Materials/M_Pressure_Brass.mat"),
            enamel = regenerate ? Finish("M_Pressure_Enamel", new Color(.72f, .54f, .14f), .18f, .28f) : Load<Material>(Folder + "/Materials/M_Pressure_Enamel.mat"),
            steel = Load<Material>(SteelPath),
            graphite = Load<Material>(GraphitePath),
            hazard = Load<Material>(HazardPath),
            tan = Load<Material>(TanPath),
            tank = Load<GameObject>(KitRoot + "Prefabs/P_PressureTank_P3.prefab"),
            valve = Load<GameObject>(KitRoot + "Prefabs/P_HeavyPipe_ValveMount.prefab"),
            reducer = Load<GameObject>(KitRoot + "Prefabs/P_HeavyPipe_Reducer.prefab"),
        };
    }

    // ---- primitives (world coordinates) -----------------------------------------------------------------------

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build North-West Room pressure systems");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void Visual(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var t = Group(parent, name);
        t.position = position;
        t.rotation = rotation;
        t.localScale = scale;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    static void Box(Transform parent, string name, Vector3 centre, Vector3 size, Material m) => Visual(parent, name, P.cube, m, centre, Quaternion.identity, size);

    static void Cyl(Transform parent, string name, Vector3 a, Vector3 b, float r, Material m)
    {
        Vector3 d = b - a;
        float length = d.magnitude;
        if (length < .005f) throw new InvalidOperationException($"{name} has zero length.");
        Visual(parent, name, P.cylinder, m, (a + b) * .5f, Quaternion.FromToRotation(Vector3.up, d / length), new Vector3(2 * r, length, 2 * r));
    }

    static void Vertical(Transform parent, string name, float x, float z, float y0, float y1, float r, Material m) =>
        Cyl(parent, name, new Vector3(x, y0, z), new Vector3(x, y1, z), r, m);

    static void Collar(Transform parent, string name, float x, float z, float y, float r, Material m = null) =>
        Vertical(parent, name, x, z, y - .06f, y + .06f, r + .045f, m ?? P.steel);

    /// <summary>A vertical pipe from y0 to the bay top, registered so the overhead joins it to a main.</summary>
    static void RiserPipe(Transform bay, string main, string name, float x, float z, float y0, float r)
    {
        Vertical(bay, name, x, z, y0, Y(BayTop), r, P.rust);
        Collar(bay, name + "_Top_Collar", x, z, Y(BayTop) - .1f, r);
        if (!Risers.TryGetValue(main, out var list)) Risers[main] = list = new List<Riser>();
        list.Add(new Riser { x = x, z = z, r = r });
    }

    /// <summary>Hand valve on a vertical pipe: fat body, a stem toward the room, and a flat hazard wheel with spokes.</summary>
    static void WheelValve(Transform parent, string name, Vector3 pipeAt, float pipeR, Vector3 face)
    {
        Vector3 stemEnd = pipeAt + face * pipeR * 2.4f;
        Vertical(parent, name + "_Body", pipeAt.x, pipeAt.z, pipeAt.y - pipeR * 1.5f, pipeAt.y + pipeR * 1.5f, pipeR * 1.5f, P.brass);
        Cyl(parent, name + "_Stem", pipeAt, stemEnd, pipeR * .22f, P.steel);
        float wr = pipeR * 1.7f;
        Cyl(parent, name + "_Wheel", stemEnd, stemEnd + face * .02f, wr, P.hazard);
        Vector3 side = Vector3.Cross(Vector3.up, face).normalized;
        Cyl(parent, name + "_Spoke_A", stemEnd + side * wr, stemEnd - side * wr, .012f, P.graphite);
        Cyl(parent, name + "_Spoke_B", stemEnd + Vector3.up * wr, stemEnd - Vector3.up * wr, .012f, P.graphite);
    }

    static void Gauge(Transform parent, string name, Vector3 at, Vector3 face)
    {
        Cyl(parent, name + "_Case", at, at + face * .06f, .09f, P.graphite);
        Cyl(parent, name + "_Face", at + face * .055f, at + face * .067f, .068f, P.brass);
    }

    static void Kit(Transform parent, GameObject prefab, string name, Vector3 pos, Quaternion rot, float scale, Func<string, Material> pick)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(go, "Build North-West Room pressure kit part");
        go.name = name;
        go.transform.position = pos;
        go.transform.rotation = rot;
        go.transform.localScale = Vector3.one * scale;
        foreach (var collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = pick(renderer.name);
    }

    static void KitTank(Transform parent, string name, float x, float z) => Kit(parent, P.tank, name, new Vector3(x, F, z), Quaternion.identity, 1f,
        part => part == "Tank_Body" ? P.enamel : part == "Tank_Bands_And_Feet" ? P.steel : P.rust);

    // The wheel sits on the part's local +X; wallNormal turns it toward the room.
    static void KitValve(Transform parent, string name, Vector3 pos, Vector3 along, float scale, Vector3? wallNormal = null) =>
        Kit(parent, P.valve, name, pos, wallNormal.HasValue ? Quaternion.LookRotation(along, Vector3.Cross(along, wallNormal.Value)) : Quaternion.LookRotation(along), scale, part => P.rust);

    static void KitReducer(Transform parent, string name, Vector3 pos, Vector3 along, float scale) =>
        Kit(parent, P.reducer, name, pos, Quaternion.LookRotation(along), scale, part => P.brass);

    // ---- pressure bays -----------------------------------------------------------------------------------------

    static void BuildBay(NorthWestRoomFramework.Layout layout, Transform section, PlanBay bay)
    {
        var f = bay.footprint;
        Transform root = Group(section, "Bay_" + bay.id);
        root.position = new Vector3((f.min.x + f.max.x) * .5f, F, (f.min.z + f.max.z) * .5f);
        bool carve;
        switch (bay.id)
        {
            case "S1": BayS1(root); carve = true; break;
            case "S3": BayS3(root); carve = true; break;
            case "S5": BayS5(root); carve = true; break;
            case "S7": BayS7(root); carve = true; break;
            case "E5": BayE5(root); DropSkid(root, bay); carve = true; break;
            case "E7": BayE7(root); carve = false; break;
            case "N3": BayN3(root); DropSkid(root, bay); carve = true; break;
            case "N6": BayN6(root); DropSkid(root, bay); carve = true; break;
            case "N9": BayN9(root); carve = false; break;
            case "W2": BayW2(root); DropSkid(root, bay); carve = true; break;
            default: throw new InvalidOperationException("Unknown pressure bay " + bay.id);
        }
        Bounds union = default;
        bool first = true;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (first) { union = r.bounds; first = false; } else union.Encapsulate(r.bounds);
        }
        // One consolidated box per bay; footprints (not the box) were reserved by corebuster_env_040.
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = union.center - root.position;
        box.size = union.size;
        if (carve)
        {
            var obstacle = root.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = box.size;
            obstacle.carving = true;
        }
    }

    static bool IsDropSlot(string id) => id == "N3" || id == "N6" || id == "E5" || id == "W2";

    // env_046: a solid visible skid fills the approved footprint to the adjacent cabinet faces.
    // Its bay's existing consolidated collision hull and a carving obstacle close the whole slot;
    // a low step alone would leave the baked NavMesh navigable behind the row fronts.
    static void DropSkid(Transform root, PlanBay bay)
    {
        var f = bay.footprint;
        Vector3 centre = new Vector3((f.min.x + f.max.x) * .5f, F + .3f, (f.min.z + f.max.z) * .5f);
        Vector3 size = new Vector3(f.max.x - f.min.x - .02f, .6f, f.max.z - f.min.z - .02f);
        Box(root, "Drop_Skid_046", centre, size, P.tan);
        Vector3 edge = centre; edge.y = F + .48f;
        Vector3 stripe = size; stripe.y = .08f;
        if (bay.id.StartsWith("N", StringComparison.Ordinal)) { edge.z = f.min.z + .025f; stripe.z = .03f; }
        else if (bay.id == "E5") { edge.x = f.min.x + .025f; stripe.x = .03f; }
        else { edge.x = f.max.x - .025f; stripe.x = .03f; }
        Box(root, "Drop_Skid_Hazard_046", edge, stripe, P.hazard);
    }

    static void Plinth(Transform bay, float cx, float cz, float sx, float sz, float h = .1f)
    {
        Box(bay, "Plinth", new Vector3(cx, F + h * .5f, cz), new Vector3(sx, h, sz), P.tan);
    }

    // South-west corner landmark: three P-3 tanks, a valve tree over them, and the riser to the west main.
    static void BayS1(Transform bay)
    {
        Plinth(bay, -249.4f, 101.225f, 2.5f, 3.29f);
        Box(bay, "Hazard_Edge", new Vector3(-248.14f, F + .06f, 101.225f), new Vector3(.02f, .04f, 3.2f), P.hazard);
        KitTank(bay, "Tank_A", -249.95f, 100.35f);
        KitTank(bay, "Tank_B", -249.95f, 101.65f);
        KitTank(bay, "Tank_C", -248.85f, 100.95f);
        float header = Y(3.5f);
        Cyl(bay, "Valve_Tree_Header", new Vector3(-249.95f, header, 100.35f), new Vector3(-249.95f, header, 101.65f), .12f, P.rust);
        Cyl(bay, "Feed_To_C", new Vector3(-249.95f, header, 100.95f), new Vector3(-248.85f, header, 100.95f), .09f, P.rust);
        Vertical(bay, "Drop_A", -249.95f, 100.35f, Y(2.9f), header, .08f, P.brass);
        Vertical(bay, "Drop_B", -249.95f, 101.65f, Y(2.9f), header, .08f, P.brass);
        Vertical(bay, "Drop_C", -248.85f, 100.95f, Y(2.9f), header, .08f, P.brass);
        Vertical(bay, "Header_End_Cap", -249.95f, 101.65f, header - .1f, header + .1f, .15f, P.steel);
        Collar(bay, "Collar_Drop_A", -249.95f, 100.35f, Y(3.2f), .08f);
        Collar(bay, "Collar_Drop_B", -249.95f, 101.65f, Y(3.2f), .08f);
        Collar(bay, "Collar_Drop_C", -248.85f, 100.95f, Y(3.2f), .08f);
        RiserPipe(bay, "West", "Riser_A", -249.95f, 100.35f, header, .11f);
        KitReducer(bay, "Riser_Reducer", new Vector3(-249.95f, Y(4.4f), 100.35f), Vector3.up, .45f);
        KitValve(bay, "Feed_Valve", new Vector3(-249.5f, header, 100.95f), Vector3.right, .38f);
        Gauge(bay, "Gauge_Header", new Vector3(-249.95f, header + .05f, 101.1f) + Vector3.right * .14f, Vector3.right);
        Gauge(bay, "Gauge_C", new Vector3(-248.85f, Y(3.2f), 100.95f) + Vector3.right * .1f, Vector3.right);
    }

    // South manifold and pump skid: two horizontal filter housings, a motor-driven pump and a surge vessel.
    static void BayS3(Transform bay)
    {
        Plinth(bay, -237.6f, 100.9f, 2.7f, 2.6f, .14f);
        float xa = -238.75f, xb = -237.05f;
        foreach (var (name, z) in new[] { ("Filter_A", 100.2f), ("Filter_B", 101.05f) })
        {
            Cyl(bay, name + "_Housing", new Vector3(xa, Y(1.0f), z), new Vector3(xb, Y(1.0f), z), .36f, P.enamel);
            Cyl(bay, name + "_Cap_West", new Vector3(xa - .05f, Y(1.0f), z), new Vector3(xa + .05f, Y(1.0f), z), .40f, P.steel);
            Cyl(bay, name + "_Cap_East", new Vector3(xb - .05f, Y(1.0f), z), new Vector3(xb + .05f, Y(1.0f), z), .40f, P.steel);
            foreach (float sx in new[] { -238.3f, -237.45f })
                Box(bay, name + "_Saddle", new Vector3(sx, F + .14f + .25f, z), new Vector3(.12f, .5f, .7f), P.graphite);
            Gauge(bay, name + "_Gauge", new Vector3(xa - .1f, Y(1.0f), z), Vector3.left);
        }
        Cyl(bay, "Pump_Motor", new Vector3(-238.6f, Y(.55f), 101.95f), new Vector3(-237.7f, Y(.55f), 101.95f), .24f, P.graphite);
        Cyl(bay, "Pump_Coupling", new Vector3(-237.72f, Y(.55f), 101.95f), new Vector3(-237.4f, Y(.55f), 101.95f), .1f, P.steel);
        Cyl(bay, "Pump_Volute", new Vector3(-237.45f, Y(.55f), 101.95f), new Vector3(-237.05f, Y(.55f), 101.95f), .28f, P.brass);
        Box(bay, "Motor_Foot", new Vector3(-238.15f, F + .14f + .1f, 101.95f), new Vector3(.9f, .2f, .5f), P.graphite);
        Vertical(bay, "Surge_Vessel", -236.55f, 99.95f, F + .14f, Y(2.4f), .27f, P.enamel);
        Vertical(bay, "Surge_Vessel_Cap", -236.55f, 99.95f, Y(2.4f), Y(2.52f), .18f, P.steel);
        Collar(bay, "Surge_Band_Low", -236.55f, 99.95f, Y(.7f), .27f, P.graphite);
        Collar(bay, "Surge_Band_High", -236.55f, 99.95f, Y(1.8f), .27f, P.graphite);
        // Discharge goes up from the pump, along the manifold, and drops onto both housings.
        float m = -237.2f, top = Y(2.2f);
        Vertical(bay, "Discharge_Riser", m, 101.95f, Y(.83f), top, .09f, P.rust);
        Cyl(bay, "Manifold", new Vector3(m, top, 101.95f), new Vector3(m, top, 100.2f), .1f, P.rust);
        Vertical(bay, "Drop_A", m, 100.2f, Y(1.36f), top, .07f, P.brass);
        Vertical(bay, "Drop_B", m, 101.05f, Y(1.36f), top, .07f, P.brass);
        Collar(bay, "Manifold_Elbow", m, 101.95f, top, .09f, P.brass);
        RiserPipe(bay, "South", "Riser", m, 100.55f, top, .11f);
        KitValve(bay, "Discharge_Valve", new Vector3(m, Y(1.35f), 101.95f), Vector3.up, .38f);
        KitReducer(bay, "Riser_Reducer", new Vector3(m, Y(4.2f), 100.55f), Vector3.up, .45f);
        WheelValve(bay, "Valve_Manifold", new Vector3(m, Y(2.9f), 100.55f), .11f, Vector3.forward);
    }

    // South manifold, filter columns: three vertical columns under a header with an expansion tank behind them.
    static void BayS5(Transform bay)
    {
        Plinth(bay, -231.2f, 100.85f, 3.2f, 2.45f, .12f);
        float[] xs = { -232.2f, -231.2f, -230.2f };
        float header = Y(2.9f);
        for (int i = 0; i < xs.Length; i++)
        {
            Vertical(bay, $"Column_{i}", xs[i], 100.55f, F + .12f, Y(2.1f), .30f, P.enamel);
            Vertical(bay, $"Column_{i}_Dome", xs[i], 100.55f, Y(2.1f), Y(2.22f), .20f, P.steel);
            Collar(bay, $"Column_{i}_Band_Low", xs[i], 100.55f, Y(.5f), .30f, P.graphite);
            Collar(bay, $"Column_{i}_Band_High", xs[i], 100.55f, Y(1.5f), .30f, P.graphite);
            Vertical(bay, $"Feeder_{i}", xs[i], 100.55f, Y(2.22f), header, .07f, P.brass);
            Collar(bay, $"Feeder_{i}_Collar", xs[i], 100.55f, Y(2.55f), .07f);
        }
        Cyl(bay, "Top_Manifold", new Vector3(xs[0], header, 100.55f), new Vector3(xs[2], header, 100.55f), .1f, P.rust);
        Cyl(bay, "Expansion_Tank", new Vector3(-232.3f, Y(.95f), 101.55f), new Vector3(-230.1f, Y(.95f), 101.55f), .36f, P.rust);
        Cyl(bay, "Expansion_Cap_West", new Vector3(-232.35f, Y(.95f), 101.55f), new Vector3(-232.25f, Y(.95f), 101.55f), .40f, P.steel);
        Cyl(bay, "Expansion_Cap_East", new Vector3(-230.15f, Y(.95f), 101.55f), new Vector3(-230.05f, Y(.95f), 101.55f), .40f, P.steel);
        foreach (float sx in new[] { -231.9f, -230.5f })
            Box(bay, "Expansion_Saddle", new Vector3(sx, F + .12f + .24f, 101.55f), new Vector3(.12f, .48f, .72f), P.graphite);
        Vertical(bay, "Balance_Pipe", -231.2f, 101.55f, Y(1.31f), Y(2.5f), .07f, P.brass);
        Cyl(bay, "Balance_Crossover", new Vector3(-231.2f, Y(2.5f), 101.55f), new Vector3(-231.2f, Y(2.5f), 100.55f), .07f, P.brass);
        Collar(bay, "Balance_Elbow", -231.2f, 101.55f, Y(2.5f), .07f, P.brass);
        RiserPipe(bay, "South", "Riser", -231.2f, 100.55f, header, .11f);
        KitValve(bay, "Riser_Valve", new Vector3(-231.2f, Y(3.6f), 100.55f), Vector3.up, .45f);
        KitReducer(bay, "Manifold_Reducer", new Vector3(xs[0] - .2f, header, 100.55f), Vector3.right, .38f);
        WheelValve(bay, "Valve_Feeder_0", new Vector3(xs[0], Y(2.4f), 100.55f), .07f, Vector3.forward);
        WheelValve(bay, "Valve_Feeder_2", new Vector3(xs[2], Y(2.4f), 100.55f), .07f, Vector3.forward);
        Gauge(bay, "Gauge_Manifold", new Vector3(-230.7f, header, 100.55f) + Vector3.forward * .13f, Vector3.forward);
    }

    // South-east corner: an expansion tank and a tall vessel with a second riser.
    static void BayS7(Transform bay)
    {
        Plinth(bay, -219.6f, 101.0f, 1.5f, 2.9f);
        KitTank(bay, "Tank", -219.6f, 100.55f);
        Vertical(bay, "Vessel", -219.6f, 101.85f, F + .1f, Y(1.9f), .30f, P.enamel);
        Vertical(bay, "Vessel_Cap", -219.6f, 101.85f, Y(1.9f), Y(2.0f), .20f, P.steel);
        Collar(bay, "Vessel_Band", -219.6f, 101.85f, Y(1.0f), .30f, P.graphite);
        Cyl(bay, "Transfer_Pipe", new Vector3(-219.6f, Y(.5f), 101.17f), new Vector3(-219.6f, Y(.5f), 101.55f), .07f, P.brass);
        RiserPipe(bay, "South", "Riser_Tank", -219.6f, 100.55f, Y(2.9f), .11f);
        RiserPipe(bay, "South", "Riser_Vessel", -219.6f, 101.85f, Y(2.1f), .09f);
        KitReducer(bay, "Tank_Reducer", new Vector3(-219.6f, Y(4.0f), 100.55f), Vector3.up, .45f);
        KitValve(bay, "Vessel_Valve", new Vector3(-219.6f, Y(3.4f), 101.85f), Vector3.up, .35f);
        WheelValve(bay, "Valve_Transfer", new Vector3(-219.6f, Y(.5f), 101.36f), .07f, Vector3.left);
        Gauge(bay, "Gauge_Vessel", new Vector3(-219.9f, Y(1.45f), 101.85f), Vector3.left);
    }

    // East drop bay: two vertical pipes with a crossover and valve station between the cabinet rows.
    static void BayE5(Transform bay)
    {
        RiserPipe(bay, "East", "Pipe_A", -219.55f, 122.62f, F + .1f, .11f);
        RiserPipe(bay, "East", "Pipe_B", -219.55f, 123.02f, F + .1f, .09f);
        Cyl(bay, "Crossover", new Vector3(-219.55f, Y(1.6f), 122.62f), new Vector3(-219.55f, Y(1.6f), 123.02f), .06f, P.brass);
        WheelValve(bay, "Valve_A", new Vector3(-219.55f, Y(1.1f), 122.62f), .11f, Vector3.left);
        KitValve(bay, "Valve_B", new Vector3(-219.55f, Y(2.4f), 123.02f), Vector3.up, .38f);
        foreach (float h in new[] { .4f, 3.4f, 4.8f })
        {
            Collar(bay, $"Collar_A_{h}", -219.55f, 122.62f, Y(h), .11f);
            Collar(bay, $"Collar_B_{h}", -219.55f, 123.02f, Y(h), .09f);
        }
        Gauge(bay, "Gauge", new Vector3(-219.8f, Y(2.9f), 123.02f), Vector3.left);
    }

    // North-east corner riser: three drops behind the east distribution bank.
    static void BayE7(Transform bay)
    {
        Box(bay, "Kerb", new Vector3(-219.27f, F + .05f, 130.15f), new Vector3(.9f, .1f, 2.2f), P.tan);
        RiserPipe(bay, "North", "Pipe_A", -219.3f, 129.35f, F + .1f, .08f);
        RiserPipe(bay, "North", "Pipe_B", -219.3f, 129.85f, F + .1f, .10f);
        RiserPipe(bay, "North", "Pipe_Main", -219.3f, 130.6f, F + .1f, .12f);
        Box(bay, "Valve_Block", new Vector3(-219.3f, Y(1.2f), 129.85f), new Vector3(.22f, .18f, 1.4f), P.steel);
        KitReducer(bay, "Reducer", new Vector3(-219.3f, Y(2.2f), 130.6f), Vector3.up, .5f);
        WheelValve(bay, "Valve_B", new Vector3(-219.3f, Y(1.9f), 129.85f), .10f, Vector3.left);
        Collar(bay, "Collar_A", -219.3f, 129.35f, Y(3.0f), .08f);
        Collar(bay, "Collar_Main", -219.3f, 130.6f, Y(3.6f), .12f);
        Gauge(bay, "Gauge", new Vector3(-219.5f, Y(2.6f), 129.35f), Vector3.left);
    }

    // North drop bay: a fat and a thin pipe with a crossover.
    static void BayN3(Transform bay)
    {
        RiserPipe(bay, "North", "Pipe_A", -227.4f, 130.6f, F + .1f, .12f);
        RiserPipe(bay, "North", "Pipe_B", -226.8f, 130.6f, F + .1f, .08f);
        Cyl(bay, "Crossover", new Vector3(-227.4f, Y(2.2f), 130.6f), new Vector3(-226.8f, Y(2.2f), 130.6f), .06f, P.brass);
        WheelValve(bay, "Valve_A", new Vector3(-227.4f, Y(.9f), 130.6f), .12f, Vector3.back);
        KitReducer(bay, "Reducer_B", new Vector3(-226.8f, Y(1.4f), 130.6f), Vector3.up, .38f);
        Collar(bay, "Collar_A", -227.4f, 130.6f, Y(3.5f), .12f);
        Collar(bay, "Collar_B", -226.8f, 130.6f, Y(4.4f), .08f);
        Gauge(bay, "Gauge_A", new Vector3(-227.4f, Y(1.7f), 130.4f), Vector3.back);
        Gauge(bay, "Gauge_B", new Vector3(-226.8f, Y(2.8f), 130.46f), Vector3.back);
    }

    // North drop bay: three thin pipes tied by a valve block.
    static void BayN6(Transform bay)
    {
        float[] xs = { -241.95f, -241.55f, -241.15f };
        for (int i = 0; i < xs.Length; i++)
        {
            RiserPipe(bay, "North", $"Pipe_{i}", xs[i], 130.6f, F + .1f, .07f);
            WheelValve(bay, $"Valve_{i}", new Vector3(xs[i], Y(1.0f + i * .5f), 130.6f), .07f, Vector3.back);
            Collar(bay, $"Collar_{i}", xs[i], 130.6f, Y(3.2f + i * .5f), .07f);
        }
        Box(bay, "Tie_Block", new Vector3(-241.55f, Y(2.9f), 130.6f), new Vector3(1.0f, .18f, .28f), P.steel);
        KitValve(bay, "Kit_Valve", new Vector3(-241.55f, Y(4.6f), 130.6f), Vector3.up, .3f);
    }

    // North-west corner riser: three drops of different sizes behind the west transformer bank.
    static void BayN9(Transform bay)
    {
        Box(bay, "Kerb", new Vector3(-250.2f, F + .05f, 130.15f), new Vector3(.9f, .1f, 2.2f), P.tan);
        RiserPipe(bay, "West", "Pipe_A", -249.95f, 129.4f, F + .1f, .09f);
        RiserPipe(bay, "West", "Pipe_B", -249.95f, 129.95f, F + .1f, .13f);
        RiserPipe(bay, "West", "Pipe_C", -249.95f, 130.5f, F + .1f, .11f);
        Box(bay, "Valve_Block", new Vector3(-250.3f, Y(1.0f), 129.95f), new Vector3(.18f, .2f, 1.5f), P.steel);
        KitValve(bay, "Valve_B", new Vector3(-249.95f, Y(2.0f), 129.95f), Vector3.up, .5f, Vector3.left);
        WheelValve(bay, "Valve_C", new Vector3(-249.95f, Y(1.5f), 130.5f), .11f, Vector3.back);
        Collar(bay, "Collar_A", -249.95f, 129.4f, Y(3.3f), .09f);
        Collar(bay, "Collar_B", -249.95f, 129.95f, Y(4.2f), .13f);
        Gauge(bay, "Gauge", new Vector3(-249.78f, Y(2.6f), 129.4f), Vector3.right);
    }

    // West drop bay: one large and one small pipe with a kit valve and a bracket.
    static void BayW2(Transform bay)
    {
        RiserPipe(bay, "West", "Pipe_Large", -249.95f, 122.5f, F + .1f, .16f);
        RiserPipe(bay, "West", "Pipe_Small", -249.95f, 123.05f, F + .1f, .07f);
        KitValve(bay, "Valve_Large", new Vector3(-249.95f, Y(1.5f), 122.5f), Vector3.up, .65f);
        WheelValve(bay, "Valve_Small", new Vector3(-249.95f, Y(2.3f), 123.05f), .07f, Vector3.right);
        Cyl(bay, "Bracket_Arm", new Vector3(-250.6f, Y(3.2f), 122.8f), new Vector3(-249.95f, Y(3.2f), 122.8f), .03f, P.graphite);
        Collar(bay, "Collar_Large_Low", -249.95f, 122.5f, Y(.5f), .16f);
        Collar(bay, "Collar_Large_High", -249.95f, 122.5f, Y(4.0f), .16f);
        Gauge(bay, "Gauge", new Vector3(-249.78f, Y(2.9f), 122.5f), Vector3.right);
    }

    // ---- wall services -----------------------------------------------------------------------------------------

    /// <summary>A room wall seen from inside: u runs along it in world units, d is the distance out from it.</summary>
    struct Wall
    {
        public string name;
        public Vector3 origin, along, normal;
        public Vector3 At(float u, float d, float y) => origin + along * u + normal * d + Vector3.up * y;
        public Vector3 Size(float a, float b, float h) => new Vector3(Mathf.Abs(along.x) * a + Mathf.Abs(normal.x) * b, h, Mathf.Abs(along.z) * a + Mathf.Abs(normal.z) * b);
    }

    // kind: 0 cable tray over conduits, 1 duct with vents, 2 cabinets and fuseboxes, 3 pipe pair; -1 none.
    struct Stretch
    {
        public string wall; public float u0, u1, y1; public int kind1; public float y2; public int kind2;
        public Stretch(string wall, float u0, float u1, float y1, int kind1, float y2, int kind2)
        { this.wall = wall; this.u0 = u0; this.u1 = u1; this.y1 = y1; this.kind1 = kind1; this.y2 = y2; this.kind2 = kind2; }
    }

    // Stretches stay clear of the 033 pressure bays and above the 041 rows (top 3.61). The east-door and west reserved
    // stretches sit above the door, Master Terminal and Boss Hall Door columns (3.49, 3.96, 4.48).
    static readonly Stretch[] Stretches =
    {
        new Stretch("South", -247.8f, -239.4f, 3.95f, 0, 5.10f, 1),
        new Stretch("South", -235.9f, -233.1f, 3.95f, 2, 5.20f, 3),
        new Stretch("South", -229.2f, -220.7f, 3.95f, 1, 5.15f, 0),
        new Stretch("East", 102.7f, 106.5f, 3.95f, 3, 5.15f, 0),
        new Stretch("East", 106.85f, 110.75f, 3.95f, 0, 5.10f, 3),
        new Stretch("East", 111.05f, 113.7f, 3.95f, 2, 5.20f, 3),
        new Stretch("East", 113.95f, 117.05f, 3.95f, 1, 5.10f, 3),
        new Stretch("East", 117.3f, 122.1f, 3.95f, 0, 5.00f, 2),
        new Stretch("East", 123.5f, 128.8f, 3.95f, 3, 5.00f, 1),
        new Stretch("North", -223.1f, -220.0f, 3.95f, 0, 5.10f, 3),
        new Stretch("North", -226.4f, -223.4f, 3.95f, 1, 5.10f, 3),
        new Stretch("North", -231.5f, -227.8f, 3.95f, 0, 5.00f, 2),
        new Stretch("North", -237.1f, -231.8f, 3.95f, 3, 5.00f, 1),
        new Stretch("North", -240.85f, -237.45f, 3.95f, 0, 5.10f, 3),
        new Stretch("North", -246.0f, -242.25f, 3.95f, 2, 5.10f, 1),
        new Stretch("North", -249.5f, -246.35f, 3.95f, 3, 5.15f, 0),
        new Stretch("West", 123.4f, 128.8f, 3.95f, 1, 5.10f, 0),
        new Stretch("West", 117.45f, 122.0f, 3.95f, 0, 5.00f, 2),
        new Stretch("West", 103.3f, 108.0f, 4.85f, 0, 5.85f, 0),
        new Stretch("West", 108.4f, 113.0f, 4.85f, 1, 5.85f, 0),
        new Stretch("West", 113.4f, 117.1f, 4.85f, 3, 5.85f, 0),
    };

    static Wall MakeWall(NorthWestRoomFramework.Layout layout, string name)
    {
        var r = layout.interior;
        switch (name)
        {
            case "South": return new Wall { name = name, origin = new Vector3(0, 0, r.min.z), along = Vector3.right, normal = Vector3.forward };
            case "North": return new Wall { name = name, origin = new Vector3(0, 0, r.max.z), along = Vector3.right, normal = Vector3.back };
            case "East": return new Wall { name = name, origin = new Vector3(r.max.x, 0, 0), along = Vector3.forward, normal = Vector3.left };
            default: return new Wall { name = name, origin = new Vector3(r.min.x, 0, 0), along = Vector3.forward, normal = Vector3.right };
        }
    }

    static void BuildServices(NorthWestRoomFramework.Layout layout, Transform section)
    {
        var walls = new Dictionary<string, Wall>();
        foreach (var s in Stretches)
        {
            if (!walls.TryGetValue(s.wall, out var wall)) walls[s.wall] = wall = MakeWall(layout, s.wall);
            string id = $"{s.wall}_{s.u0:F1}_{s.u1:F1}".Replace('-', 'm');
            Transform g = Group(section, id);
            ServiceKind(wall, Group(g, "Lower"), s.kind1, s.u0, s.u1, s.y1);
            if (s.kind2 >= 0) ServiceKind(wall, Group(g, "Upper"), s.kind2, s.u0, s.u1, s.y2);
        }
    }

    static void ServiceKind(Wall w, Transform g, int kind, float u0, float u1, float y)
    {
        switch (kind)
        {
            case 0: Tray(w, g, u0, u1, y); break;
            case 1: Duct(w, g, u0, u1, y); break;
            case 2: Cabinets(w, g, u0, u1, y); break;
            default: PipePair(w, g, u0, u1, y); break;
        }
    }

    static void WBox(Wall w, Transform g, string name, float u, float d, float y, float a, float b, float h, Material m) =>
        Box(g, name, w.At(u, d, y), w.Size(a, b, h), m);

    static void WPipe(Wall w, Transform g, string name, float u0, float u1, float d, float y, float r, Material m) =>
        Cyl(g, name, w.At(u0, d, y), w.At(u1, d, y), r, m);

    static void Tray(Wall w, Transform g, float u0, float u1, float y)
    {
        float len = u1 - u0, uc = (u0 + u1) * .5f;
        WBox(w, g, "Tray_Base", uc, .25f, y, len, .40f, .03f, P.steel);
        WBox(w, g, "Tray_Rail_Wall", uc, .06f, y + .045f, len, .03f, .10f, P.graphite);
        WBox(w, g, "Tray_Rail_Room", uc, .44f, y + .045f, len, .03f, .10f, P.graphite);
        WPipe(w, g, "Cable_A", u0 + .05f, u1 - .05f, .15f, y + .06f, .035f, P.graphite);
        WPipe(w, g, "Cable_B", u0 + .05f, u1 - .05f, .25f, y + .06f, .035f, P.rust);
        WPipe(w, g, "Cable_C", u0 + .05f, u1 - .05f, .35f, y + .06f, .035f, P.brass);
        WPipe(w, g, "Conduit_Rust", u0 + .1f, u1 - .1f, .12f, y - .20f, .035f, P.rust);
        WPipe(w, g, "Conduit_Brass", u0 + .1f, u1 - .1f, .28f, y - .20f, .035f, P.brass);
        for (float u = u0 + .6f; u < u1 - .3f; u += 1.3f)
        {
            WBox(w, g, "Support_Arm", u, .27f, y - .06f, .05f, .46f, .04f, P.graphite);
            WBox(w, g, "Support_Plate", u, .05f, y - .10f, .06f, .04f, .26f, P.graphite);
            WBox(w, g, "Conduit_Clamp", u, .20f, y - .20f, .05f, .26f, .10f, P.graphite);
        }
    }

    static void Duct(Wall w, Transform g, float u0, float u1, float y)
    {
        float len = u1 - u0, uc = (u0 + u1) * .5f;
        WBox(w, g, "Duct_Body", uc, .30f, y + .19f, len, .50f, .38f, P.steel);
        WBox(w, g, "Hazard_Strip", uc, .56f, y + .06f, len - .2f, .02f, .04f, P.hazard);
        for (float u = u0 + .7f; u < u1 - .2f; u += 1.4f)
            WBox(w, g, "Duct_Flange", u, .30f, y + .19f, .08f, .56f, .44f, P.graphite);
        foreach (float f in new[] { .3f, .7f })
        {
            float u = u0 + len * f;
            WBox(w, g, "Grille_Frame", u, .56f, y + .19f, .62f, .03f, .28f, P.graphite);
            for (int i = 0; i < 4; i++)
                WBox(w, g, "Grille_Slat_" + i, u, .585f, y + .09f + i * .067f, .54f, .02f, .025f, P.tan);
        }
        for (float u = u0 + .35f; u < u1 - .2f; u += 2.1f)
            WBox(w, g, "Hanger_Plate", u, .03f, y + .19f, .06f, .06f, .5f, P.graphite);
    }

    static void Cabinets(Wall w, Transform g, float u0, float u1, float y)
    {
        float len = u1 - u0;
        int n = len >= 4f ? 3 : 2;
        float pitch = (len - .5f) / n;
        float width = Mathf.Min(.85f, pitch - .15f);
        for (int i = 0; i < n; i++)
        {
            float u = u0 + .25f + (i + .5f) * pitch;
            Material body = i % 2 == 0 ? P.tan : P.steel, door = i % 2 == 0 ? P.steel : P.tan;
            WBox(w, g, $"Cabinet_{i}_Rail", u, .04f, y + .1f, width + .1f, .06f, .08f, P.graphite);
            WBox(w, g, $"Cabinet_{i}_Body", u, .22f, y + .52f, width, .38f, .95f, body);
            WBox(w, g, $"Cabinet_{i}_Door", u, .415f, y + .50f, width * .86f, .015f, .80f, door);
            WBox(w, g, $"Cabinet_{i}_Handle", u + width * .3f, .44f, y + .50f, .035f, .03f, .24f, P.graphite);
            WBox(w, g, $"Cabinet_{i}_Indicator", u - width * .25f, .42f, y + .82f, .05f, .02f, .05f, P.hazard);
            WBox(w, g, $"Cabinet_{i}_Cap", u, .22f, y + 1.0f, width + .05f, .42f, .04f, P.graphite);
            Cyl(g, $"Cabinet_{i}_Stub", w.At(u, .12f, y + .99f), w.At(u, .12f, y + 1.08f), .03f, P.rust);
        }
        float first = u0 + .25f + pitch * .5f, last = u0 + .25f + (n - .5f) * pitch;
        WPipe(w, g, "Cabinet_Conduit", first, last, .12f, y + 1.08f, .03f, P.rust);
        WBox(w, g, "Fusebox_Body", u1 - .22f, .12f, y + .55f, .30f, .20f, .42f, P.graphite);
        WBox(w, g, "Fusebox_Door", u1 - .22f, .225f, y + .55f, .24f, .012f, .34f, P.steel);
        WBox(w, g, "Fusebox_Strip", u1 - .22f, .235f, y + .72f, .20f, .012f, .035f, P.hazard);
    }

    static void PipePair(Wall w, Transform g, float u0, float u1, float y)
    {
        float len = u1 - u0;
        WPipe(w, g, "Pipe_Lower", u0, u1, .12f, y, .07f, P.rust);
        WPipe(w, g, "Pipe_Upper", u0, u1, .12f, y + .34f, .055f, P.brass);
        foreach (float u in new[] { u0 + .03f, u1 - .03f })
        {
            Cyl(g, "Flange_Lower", w.At(u - .03f, .12f, y), w.At(u + .03f, .12f, y), .11f, P.steel);
            Cyl(g, "Flange_Upper", w.At(u - .03f, .12f, y + .34f), w.At(u + .03f, .12f, y + .34f), .09f, P.steel);
        }
        for (float u = u0 + .5f; u < u1 - .3f; u += 1.5f)
            WBox(w, g, "Bracket", u, .07f, y + .17f, .06f, .14f, .50f, P.graphite);
        KitValve(g, "Kit_Valve", w.At((u0 + u1) * .5f, .12f, y), w.along, .27f, w.normal);
        if (len > 2.5f) KitReducer(g, "Kit_Reducer", w.At(u1 - .5f, .12f, y + .34f), w.along, .27f);
    }

    // ---- overhead ----------------------------------------------------------------------------------------------

    struct Main
    {
        public string id; public bool alongX; public float fixedAxis, y, r, start, end;
    }

    static void BuildOverhead(NorthWestRoomFramework.Layout layout, Transform section)
    {
        // Mains are separate runs at different heights and do not close into a ring.
        var mains = new[]
        {
            new Main { id = "South", alongX = true, fixedAxis = 100.55f, y = 8.0f, r = .28f, start = -246.5f, end = -219.6f },
            new Main { id = "West", alongX = false, fixedAxis = -249.95f, y = 7.7f, r = .26f, start = 100.35f, end = 130.5f },
            new Main { id = "North", alongX = true, fixedAxis = 130.6f, y = 8.9f, r = .30f, start = -242.2f, end = -219.3f },
            new Main { id = "East", alongX = false, fixedAxis = -219.55f, y = 7.3f, r = .24f, start = 106.0f, end = 123.02f },
        };
        foreach (var main in mains) BuildMain(section, main);
        BuildCrossDuct(section);
        BuildCrossTray(section);
        BuildCrossPipes(section);
    }

    static Vector3 OnMain(Main m, float along, float y) => m.alongX ? new Vector3(along, y, m.fixedAxis) : new Vector3(m.fixedAxis, y, along);

    static void BuildMain(Transform section, Main m)
    {
        Transform run = Group(section, "Run_" + m.id + "_Main");
        Cyl(run, "Main", OnMain(m, m.start, m.y), OnMain(m, m.end, m.y), m.r, P.rust);
        foreach (float end in new[] { m.start, m.end })
            Cyl(run, "End_Cap", OnMain(m, end + (end == m.start ? 0 : -.08f), m.y), OnMain(m, end + (end == m.start ? .08f : 0), m.y), m.r + .06f, P.steel);
        if (!Risers.TryGetValue(m.id, out var risers)) risers = new List<Riser>();
        // Flanges and straps between the riser joints.
        for (float a = m.start + 1.6f; a < m.end - .8f; a += 3.2f)
            if (risers.All(rs => Mathf.Abs((m.alongX ? rs.x : rs.z) - a) > .6f))
                Cyl(run, "Flange_Ring", OnMain(m, a - .05f, m.y), OnMain(m, a + .05f, m.y), m.r + .07f, P.graphite);
        for (float a = m.start + 1.0f; a < m.end - .5f; a += 3.4f)
        {
            Vector3 top = OnMain(m, a, m.y + m.r);
            Vertical(run, "Hanger", top.x, top.z, top.y, OverheadMaxAbsY - .08f, .03f, P.graphite);
            Box(run, "Hanger_Plate", new Vector3(top.x, OverheadMaxAbsY - .04f, top.z), new Vector3(.2f, .08f, .2f), P.graphite);
            Box(run, "Hanger_Saddle", top + Vector3.down * .02f, new Vector3(.18f, .05f, .18f), P.steel);
        }
        foreach (var rs in risers)
        {
            float onAxis = m.alongX ? rs.z : rs.x, along = m.alongX ? rs.x : rs.z;
            Vertical(run, "Riser_Stub", rs.x, rs.z, OverheadMinAbsY, m.y, rs.r, P.rust);
            Vertical(run, "Riser_Joint", rs.x, rs.z, m.y - rs.r - .15f, m.y + rs.r + .15f, rs.r + .08f, P.steel);
            if (Mathf.Abs(onAxis - m.fixedAxis) > .01f)
                Cyl(run, "Riser_Jog", new Vector3(rs.x, m.y, rs.z), OnMain(m, along, m.y), rs.r, P.rust);
        }
        Gauge(run, "Gauge", OnMain(m, (m.start + m.end) * .5f, m.y) + Vector3.down * (m.r + .05f), Vector3.down);
    }

    static void BuildCrossDuct(Transform section)
    {
        Transform run = Group(section, "Run_Cross_Duct");
        const float x = -241.2f, y = 9.2f, z0 = 101.8f, z1 = 129.4f;
        Box(run, "Duct", new Vector3(x, y, (z0 + z1) * .5f), new Vector3(.9f, .5f, z1 - z0), P.steel);
        for (float z = z0 + 1.6f; z < z1 - .5f; z += 3.2f)
            Box(run, "Duct_Flange", new Vector3(x, y, z), new Vector3(.98f, .58f, .12f), P.graphite);
        Box(run, "Hazard_Strip", new Vector3(x, y - .26f, (z0 + z1) * .5f), new Vector3(.5f, .02f, z1 - z0 - .4f), P.hazard);
        foreach (float z in new[] { 103.2f, 106.6f, 110.0f, 113.4f, 118.4f, 121.8f, 125.2f, 128.4f })
            foreach (float dx in new[] { -.4f, .4f })
            {
                Vertical(run, "Hanger", x + dx, z, y + .25f, OverheadMaxAbsY - .08f, .03f, P.graphite);
                Box(run, "Hanger_Plate", new Vector3(x + dx, OverheadMaxAbsY - .04f, z), new Vector3(.18f, .08f, .18f), P.graphite);
            }
    }

    static void BuildCrossTray(Transform section)
    {
        Transform run = Group(section, "Run_Cross_Tray");
        const float x0 = -249.0f, x1 = -221.5f, y = 10.2f, z = 115.8f;
        float len = x1 - x0, xc = (x0 + x1) * .5f;
        Box(run, "Tray_Base", new Vector3(xc, y, z), new Vector3(len, .04f, .6f), P.steel);
        Box(run, "Tray_Rail_A", new Vector3(xc, y + .07f, z - .3f), new Vector3(len, .1f, .04f), P.graphite);
        Box(run, "Tray_Rail_B", new Vector3(xc, y + .07f, z + .3f), new Vector3(len, .1f, .04f), P.graphite);
        Cyl(run, "Cable_A", new Vector3(x0 + .1f, y + .06f, z - .15f), new Vector3(x1 - .1f, y + .06f, z - .15f), .04f, P.graphite);
        Cyl(run, "Cable_B", new Vector3(x0 + .1f, y + .06f, z), new Vector3(x1 - .1f, y + .06f, z), .04f, P.rust);
        Cyl(run, "Cable_C", new Vector3(x0 + .1f, y + .06f, z + .15f), new Vector3(x1 - .1f, y + .06f, z + .15f), .04f, P.brass);
        for (float x = x0 + .6f; x < x1 - .3f; x += 1.2f)
            Box(run, "Rung", new Vector3(x, y - .015f, z), new Vector3(.05f, .02f, .66f), P.graphite);
        foreach (float x in new[] { -248.2f, -244.7f, -238.0f, -234.5f, -231.0f, -226.4f, -222.8f })
        {
            Vertical(run, "Hanger", x, z - .3f, y + .12f, OverheadMaxAbsY - .08f, .025f, P.graphite);
            Vertical(run, "Hanger", x, z + .3f, y + .12f, OverheadMaxAbsY - .08f, .025f, P.graphite);
            Box(run, "Hanger_Plate", new Vector3(x, OverheadMaxAbsY - .04f, z), new Vector3(.18f, .08f, .8f), P.graphite);
        }
    }

    static void BuildCrossPipes(Transform section)
    {
        Transform run = Group(section, "Run_Cross_Pipes");
        const float y = 8.4f, z0 = 104.0f, z1 = 126.8f;
        Cyl(run, "Pipe_Rust", new Vector3(-229.5f, y, z0), new Vector3(-229.5f, y, z1), .09f, P.rust);
        Cyl(run, "Pipe_Brass", new Vector3(-228.9f, y, z0), new Vector3(-228.9f, y, z1), .07f, P.brass);
        foreach (float z in new[] { z0, z1 })
        {
            Cyl(run, "End_Flange_Rust", new Vector3(-229.5f, y, z - .04f), new Vector3(-229.5f, y, z + .04f), .15f, P.steel);
            Cyl(run, "End_Flange_Brass", new Vector3(-228.9f, y, z - .04f), new Vector3(-228.9f, y, z + .04f), .12f, P.steel);
        }
        for (float z = z0 + 4.5f; z < z1 - 2f; z += 4.5f)
        {
            Cyl(run, "Flange_Rust", new Vector3(-229.5f, y, z - .05f), new Vector3(-229.5f, y, z + .05f), .14f, P.graphite);
            Cyl(run, "Flange_Brass", new Vector3(-228.9f, y, z - .05f), new Vector3(-228.9f, y, z + .05f), .11f, P.graphite);
        }
        foreach (float z in new[] { 107.0f, 111.5f, 121.0f, 125.0f })
        {
            Box(run, "Cross_Bar", new Vector3(-229.2f, y + .13f, z), new Vector3(.9f, .05f, .05f), P.graphite);
            Vertical(run, "Hanger", -229.2f, z, y + .13f, OverheadMaxAbsY - .08f, .03f, P.graphite);
            Box(run, "Hanger_Plate", new Vector3(-229.2f, OverheadMaxAbsY - .04f, z), new Vector3(.2f, .08f, .2f), P.graphite);
        }
    }

    // ---- validation --------------------------------------------------------------------------------------------

    static void RequireValid(NorthWestRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        if (!NorthWestRoomFramework.Validate(scene, layout, true) ||
            !NorthWestRoomFramework.ValidateSection(scene, layout, Section.PressureSystems, true) ||
            !NorthWestRoomFramework.ValidateSection(scene, layout, Section.WallServices, true) ||
            !NorthWestRoomFramework.ValidateSection(scene, layout, Section.Overhead, true))
            throw new InvalidOperationException("North-West Room framework or pressure section validation failed.");
        var pressure = NorthWestRoomFramework.FindSection(scene, Section.PressureSystems);
        var services = NorthWestRoomFramework.FindSection(scene, Section.WallServices);
        var overhead = NorthWestRoomFramework.FindSection(scene, Section.Overhead);
        var errors = new List<string>();
        float floor = layout.Floor;

        // Pressure bays: named from the plan, one box collider, inside the footprint, below the service band.
        var bays = OwnedBays().ToDictionary(b => "Bay_" + b.id);
        var roots = pressure.Cast<Transform>().ToArray();
        if (roots.Length != ExpectedBays || roots.Any(r => !bays.ContainsKey(r.name)))
            errors.Add($"Expected {ExpectedBays} pressure bays named from the density plan.");
        var bayBounds = new List<Bounds>();
        foreach (var root in roots)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger).ToArray();
            if (colliders.Length != 1) errors.Add($"{root.name} has {colliders.Length} colliders; expected one consolidated box.");
            if (!bays.TryGetValue(root.name, out var plan)) continue;
            var foot = plan.footprint;
            if (IsDropSlot(plan.id))
            {
                var skid = root.Find("Drop_Skid_046");
                var hull = root.GetComponent<BoxCollider>();
                var obstacle = root.GetComponent<NavMeshObstacle>();
                if (!skid || !hull || !obstacle || !obstacle.enabled || !obstacle.carving)
                    errors.Add($"{root.name} lacks its visible slot closure or carving hull.");
                else
                {
                    var b = skid.GetComponent<Renderer>().bounds;
                    if (Mathf.Abs(b.min.x - foot.min.x) > .02f || Mathf.Abs(b.max.x - foot.max.x) > .02f ||
                        Mathf.Abs(b.min.z - foot.min.z) > .02f || Mathf.Abs(b.max.z - foot.max.z) > .02f ||
                        Mathf.Abs(b.min.y - floor) > .01f || b.size.y < .59f)
                        errors.Add($"{root.name} skid does not fill its approved slot footprint.");
                    if (obstacle.center != hull.center || obstacle.size != hull.size)
                        errors.Add($"{root.name} carve does not match its consolidated hull.");
                }
            }
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var b = renderer.bounds;
                bayBounds.Add(b);
                if (b.min.x < foot.min.x - .03f || b.max.x > foot.max.x + .03f || b.min.z < foot.min.z - .03f || b.max.z > foot.max.z + .03f)
                    errors.Add($"{root.name}/{renderer.name} leaves its density-plan footprint.");
                if (b.max.y > floor + BayTop + .03f) errors.Add($"{root.name}/{renderer.name} rises above the {BayTop} m bay top.");
                if (b.min.y < floor - .01f) errors.Add($"{root.name}/{renderer.name} is below the floor.");
            }
        }
        int obstacles = pressure.GetComponentsInChildren<NavMeshObstacle>(true).Count(o => o.enabled && o.carving);
        if (obstacles != ExpectedObstacles) errors.Add($"Expected {ExpectedObstacles} carving obstacles, found {obstacles}.");

        // Wall services: collider-free, in the service band, near a wall, clear of reserved columns and the bays.
        if (services.GetComponentsInChildren<Collider>(true).Any(c => c.enabled)) errors.Add("Wall_Services must be collider-free.");
        var interior = layout.interior;
        var columns = new[]
        {
            (name: "east door", x0: -222f, x1: -218.7f, z0: 106.72f, z1: 110.82f, top: 3.49f),
            (name: "Boss Hall Door / Master Terminal / progression lane", x0: -250.8f, x1: -248.5f, z0: 103.22f, z1: 117.22f, top: 4.48f),
            (name: "Red Terminal", x0: -235.48f, x1: -233.52f, z0: 129.5f, z1: 131.4f, top: 2.64f),
        };
        var otherRows = new[] { Section.PerimeterMachinery, Section.TurbineMachinery, Section.ElectricalSystems }
            .SelectMany(s => NorthWestRoomFramework.FindSection(scene, s).GetComponentsInChildren<Renderer>(true)).Select(r => r.bounds).ToArray();
        foreach (var renderer in services.GetComponentsInChildren<Renderer>(true))
        {
            var b = renderer.bounds;
            string label = ModuleOf(renderer.transform, services).parent.name + "/" + renderer.name;
            if (b.min.y < ServiceMinAbsY || b.max.y > ServiceMaxAbsY) errors.Add($"{label} leaves the {ServiceMinAbsY}-{ServiceMaxAbsY} service band.");
            // The wall the item hangs on: the smallest far-edge distance across the four walls.
            float farEdge = Mathf.Min(Mathf.Min(interior.max.x - b.min.x, b.max.x - interior.min.x), Mathf.Min(interior.max.z - b.min.z, b.max.z - interior.min.z));
            if (farEdge > 1.2f + .001f) errors.Add($"{label} is more than 1.2 m from every wall.");
            foreach (var c in columns)
                if (b.min.x < c.x1 && b.max.x > c.x0 && b.min.z < c.z1 && b.max.z > c.z0 && b.min.y < c.top + .1f)
                    errors.Add($"{label} enters the {c.name} column (needs Y {c.top + .1f:F2}).");
            var padded = b; padded.Expand(.02f);
            if (bayBounds.Any(o => o.Intersects(padded))) errors.Add($"{label} intersects a pressure bay.");
            if (otherRows.Any(o => o.Intersects(padded))) errors.Add($"{label} intersects another Room 3 machine section.");
        }
        // Services must not overlap each other except within the same lower/upper module.
        var serviceRenderers = services.GetComponentsInChildren<Renderer>(true);
        var groupOf = serviceRenderers.ToDictionary(r => r, r => ModuleOf(r.transform, services));
        for (int i = 0; i < serviceRenderers.Length; i++)
            for (int j = i + 1; j < serviceRenderers.Length; j++)
            {
                if (groupOf[serviceRenderers[i]] == groupOf[serviceRenderers[j]]) continue;
                var a = serviceRenderers[i].bounds; a.Expand(-.002f);
                if (a.Intersects(serviceRenderers[j].bounds))
                    errors.Add($"Wall service {groupOf[serviceRenderers[i]].parent.name}/{serviceRenderers[i].name} intersects {groupOf[serviceRenderers[j]].parent.name}/{serviceRenderers[j].name}.");
            }

        // Overhead: collider-free, in the overhead band, inset from the walls, and no two runs intersect.
        if (overhead.GetComponentsInChildren<Collider>(true).Any(c => c.enabled)) errors.Add("Overhead must be collider-free.");
        var runs = overhead.Cast<Transform>().ToArray();
        if (runs.Length != 7) errors.Add($"Expected seven overhead runs, found {runs.Length}.");
        var inner = interior; inner.Expand(new Vector3(-2 * Inset, 0, -2 * Inset));
        foreach (var renderer in overhead.GetComponentsInChildren<Renderer>(true))
        {
            var b = renderer.bounds;
            string label = RunOf(renderer.transform, overhead).name + "/" + renderer.name;
            if (b.min.y < OverheadMinAbsY || b.max.y > OverheadMaxAbsY + .001f) errors.Add($"{label} leaves the overhead band {OverheadMinAbsY}-{OverheadMaxAbsY}.");
            if (b.min.x < inner.min.x - .001f || b.max.x > inner.max.x + .001f || b.min.z < inner.min.z - .001f || b.max.z > inner.max.z + .001f)
                errors.Add($"{label} is inside the {Inset} m wall inset.");
            var padded = b; padded.Expand(.02f);
            if (!renderer.name.StartsWith("Riser_Stub", StringComparison.Ordinal) && bayBounds.Any(o => o.Intersects(padded))) errors.Add($"{label} intersects a pressure bay.");
        }
        var overheadRenderers = overhead.GetComponentsInChildren<Renderer>(true);
        int pairs = 0;
        for (int i = 0; i < overheadRenderers.Length; i++)
            for (int j = i + 1; j < overheadRenderers.Length; j++)
            {
                if (RunOf(overheadRenderers[i].transform, overhead) == RunOf(overheadRenderers[j].transform, overhead)) continue;
                pairs++;
                var a = overheadRenderers[i].bounds; a.Expand(.05f);
                if (a.Intersects(overheadRenderers[j].bounds))
                    errors.Add($"Overhead {RunOf(overheadRenderers[i].transform, overhead).name}/{overheadRenderers[i].name} comes within 5 cm of {RunOf(overheadRenderers[j].transform, overhead).name}/{overheadRenderers[j].name}.");
            }

        // Section-wide rules.
        var all = new[] { pressure, services, overhead };
        foreach (var s in all)
        {
            if (s.GetComponentsInChildren<Light>(true).Length != 0) errors.Add(s.name + " must carry no lights (corebuster_env_037).");
            if (s.GetComponentsInChildren<MonoBehaviour>(true).Length != 0) errors.Add(s.name + " must be script-free.");
        }
        int prefabs = all.Sum(s => s.GetComponentsInChildren<Transform>(true).Count(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)));
        if (prefabs > PrefabCeiling) errors.Add($"Pressure sections use {prefabs}/{PrefabCeiling} prefab instances.");
        int straight2m = all.Sum(s => s.GetComponentsInChildren<Transform>(true).Count(t =>
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) return false;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            return source && AssetDatabase.GetAssetPath(source).EndsWith("P_HeavyPipe_Straight_2m.prefab", StringComparison.Ordinal);
        }));
        if (straight2m > StraightCeiling) errors.Add($"Heavy-pipe straight 2 m count {straight2m} exceeds {StraightCeiling}.");
        int newMaterials = all.SelectMany(s => s.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m && AssetDatabase.GetAssetPath(m).StartsWith(Folder + "/")).Distinct().Count();
        if (newMaterials > NewMaterialCeiling) errors.Add($"Pressure sections own {newMaterials}/{NewMaterialCeiling} new materials.");
        if (errors.Count > 0) throw new InvalidOperationException("North-West pressure checks failed (" + errors.Count + "):\n  " + string.Join("\n  ", errors.Take(60)));
        Debug.Log($"NORTH-WEST PRESSURE CHECKS PASSED: {ExpectedBays} bays, {obstacles}/{ExpectedObstacles} carving obstacles, {services.childCount} service stretches, {runs.Length} overhead runs ({pairs} cross-run renderer pairs checked), {prefabs}/{PrefabCeiling} prefabs, {straight2m}/{StraightCeiling} straight 2 m pipes, {newMaterials}/{NewMaterialCeiling} new materials.");
    }

    static Transform RunOf(Transform t, Transform section)
    {
        while (t.parent != section) t = t.parent;
        return t;
    }

    /// <summary>The Lower or Upper module group of a wall-service stretch that holds this renderer.</summary>
    static Transform ModuleOf(Transform t, Transform section)
    {
        while (t.parent.parent != section) t = t.parent;
        return t;
    }

    static void VerifyIsolatedUndo(NorthWestRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        var pressure = NorthWestRoomFramework.FindSection(scene, Section.PressureSystems);
        var services = NorthWestRoomFramework.FindSection(scene, Section.WallServices);
        var overhead = NorthWestRoomFramework.FindSection(scene, Section.Overhead);
        int p = pressure.childCount, s = services.childCount, o = overhead.childCount;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Verify isolated pressure section Undo");
        NorthWestRoomFramework.BeginSectionRebuild(layout, Section.PressureSystems, Owner);
        Undo.CollapseUndoOperations(group);
        if (pressure.childCount != 0 || services.childCount != s || overhead.childCount != o)
            throw new InvalidOperationException("Pressure section clear changed another section.");
        Undo.PerformUndo();
        if (pressure.childCount != p || services.childCount != s || overhead.childCount != o)
            throw new InvalidOperationException("Pressure section Undo did not restore exactly the cleared section.");
    }

    // ---- assets and snapshots ----------------------------------------------------------------------------------

    static Material Finish(string name, Color color, float metallic, float smoothness)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", color); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness);
        return Save(m, "Materials/" + name + ".mat");
    }

    static T Save<T>(T generated, string relative) where T : Object
    {
        string path = Folder + "/" + relative;
        generated.name = Path.GetFileNameWithoutExtension(relative);
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing) { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
        else { AssetDatabase.CreateAsset(generated, path); existing = generated; }
        NorthWestRoomFramework.RequireOwnedAsset(existing, Section.PressureSystems);
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
        throw new InvalidOperationException("Missing pressure dependency: " + path);

    static Dictionary<string, string> SnapshotOutside(Section[] excluded = null, Transform[] extraExcluded = null)
    {
        Canvas.ForceUpdateCanvases();
        var scene = SceneManager.GetActiveScene();
        var owned = (excluded != null && excluded.Length > 0 ? excluded : new[] { Section.PressureSystems, Section.WallServices, Section.Overhead }).Select(s => NorthWestRoomFramework.FindSection(scene, s)).ToArray();
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => owned.All(s => !t.IsChildOf(s)) && (extraExcluded == null || extraExcluded.All(s => !s || !t.IsChildOf(s))))
            .ToDictionary(t => GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), t =>
            {
                var renderer = t.GetComponent<Renderer>();
                string mats = renderer ? string.Join(",", renderer.sharedMaterials.Select(m => m ? AssetDatabase.GetAssetPath(m) : "null")) : "";
                return FormattableString.Invariant($"{t.name}|{(t.parent ? GlobalObjectId.GetGlobalObjectIdSlow(t.parent).ToString() : "root")}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}|{t.gameObject.activeSelf}|{mats}");
            });
    }
}

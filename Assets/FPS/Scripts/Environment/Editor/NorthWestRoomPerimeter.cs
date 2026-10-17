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
/// Perimeter machine rows and flush bays for corebuster_env_041. Footprints come from NorthWestRoomDensityPlan.json
/// (corebuster_env_040); this builder owns only the bays that JSON assigns to corebuster_env_041.
/// </summary>
public static class NorthWestRoomPerimeter
{
    const string Owner = "corebuster_env_041";
    const string Folder = "Assets/FPS/Art/NorthWestRoom/Perimeter_Machinery";
    const string PlanPath = "Assets/FPS/Art/NorthWestRoom/NorthWestRoomDensityPlan.json";
    const string Output = "ReviewCaptures/NorthWestRoom/041";
    const string GraphitePath = "Assets/FPS/Art/NorthWestRoom/Electrical_Systems/Materials/M_Electrical_Graphite.mat";
    const string SteelPath = "Assets/FPS/Art/NorthWestRoom/Electrical_Systems/Materials/M_Electrical_Steel.mat";
    const string HazardPath = "Assets/FPS/Art/NorthWestRoom/Turbine_Machinery/Materials/M_Turbine_Hazard.mat";
    const int ExpectedBays = 12, ExpectedObstacles = 8, PrefabCeiling = 130;
    const float WallInset = .02f, EndInset = .03f, Kerb = .12f, MaxTop = 3.4f;

    [Serializable] class P2 { public float x, z; }
    [Serializable] class R2 { public P2 min, max; }
    [Serializable] class PlanBay { public string id, owner; public R2 footprint; }
    [Serializable] class Plan { public PlanBay[] bays; }

    /// <summary>Hand-authored variety per bay: widths, heights, depth fractions and the obstacle choice.</summary>
    struct Profile
    {
        public float[] widths, heights, depth;
        public bool carve;
        public Profile(bool carve, float[] widths, float[] heights, float[] depth)
        { this.carve = carve; this.widths = widths; this.heights = heights; this.depth = depth; }
    }

    // Eight rows carve (the 041 budget); the two corner-adjacent rows N2 and W1 and the 1.1 m flush bays S4 and E3
    // keep one consolidated collider but no obstacle, because they sit in the NavMesh margin beside the walls.
    static readonly Dictionary<string, Profile> Profiles = new Dictionary<string, Profile>
    {
        ["E1"] = new Profile(true, new[] { .9f, 1.1f, .75f, 1.0f }, new[] { 2.0f, 2.6f, 1.9f, 2.3f }, new[] { .85f, 1f, .8f, .95f }),
        ["E2"] = new Profile(true, new[] { 1.7f, .9f }, new[] { 3.4f, 2.4f }, new[] { 1f, .75f }),
        ["E3"] = new Profile(false, new[] { .8f, .6f, .9f, .75f }, new[] { 2.2f, 3.0f, 2.0f, 2.8f }, new[] { .8f, .85f, .7f, .9f }),
        ["S4"] = new Profile(false, new[] { .7f, 1.0f, .55f, .75f }, new[] { 2.6f, 2.1f, 3.4f, 2.3f }, new[] { .75f, .9f, .8f, .7f }),
        ["E4"] = new Profile(true, new[] { 1.2f, .8f, .8f, 1.1f, .9f }, new[] { 2.2f, 2.2f, 3.0f, 1.9f, 2.4f }, new[] { .9f, .7f, 1f, .8f, .95f }),
        ["E6"] = new Profile(true, new[] { 1.0f, 1.0f, .7f, 1.3f, .8f, .9f }, new[] { 1.9f, 2.1f, 2.6f, 3.0f, 3.4f, 3.4f }, new[] { .8f, 1f, .75f, .9f, 1f, .85f }),
        ["N2"] = new Profile(false, new[] { 1.0f, .8f, 1.2f }, new[] { 2.4f, 3.0f, 2.0f }, new[] { .8f, 1f, .9f }),
        ["N4"] = new Profile(true, new[] { .7f, .7f, .7f, .9f, .8f }, new[] { 2.0f, 2.0f, 2.0f, 2.8f, 2.2f }, new[] { .7f, .7f, .7f, 1f, .85f }),
        ["N5"] = new Profile(true, new[] { .9f, .9f, .9f, .9f }, new[] { 3.0f, 3.4f, 3.2f, 3.4f }, new[] { 1f, .9f, 1f, .85f }),
        ["N7"] = new Profile(true, new[] { 1.3f, .85f, 1.0f, .8f }, new[] { 2.2f, 1.8f, 2.7f, 2.0f }, new[] { .75f, 1f, .85f, .95f }),
        ["W1"] = new Profile(false, new[] { 1.0f, .8f, 1.1f, .7f, .9f, .9f }, new[] { 2.0f, 2.4f, 3.2f, 2.0f, 2.6f, 2.2f }, new[] { .9f, .8f, 1f, .7f, .95f, .85f }),
        ["W3"] = new Profile(true, new[] { 1.1f, .9f, .9f, 1.0f, .8f }, new[] { 2.4f, 2.0f, 2.8f, 2.2f, 3.0f }, new[] { .85f, 1f, .75f, .9f, .8f }),
    };

    struct Parts
    {
        public Mesh cube, cylinder;
        public Material warmGrey, tan, ochre, graphite, steel, hazard;
    }

    [MenuItem("Tools/CoreBuster/Build North-West Room Perimeter Machinery")]
    public static void BuildMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        NorthWestRoomFramework.Build(layout);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build North-West Room Perimeter Machinery");
        try { Build(layout); RequireValid(layout); Undo.CollapseUndoOperations(group); }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = NorthWestRoomFramework.MeasureLayout();
        NorthWestRoomFramework.Build(layout); // creates only the missing Perimeter_Machinery container
        var before = SnapshotOutside();
        var protectedMaterials = NorthWestRoomFramework.CaptureProtectedMaterialHashes();
        Build(layout);
        VerifyIsolatedUndo(layout);
        RequireValid(layout);
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Could not save Room 3 perimeter machinery.");
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        var after = SnapshotOutside();
        if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out string value) || value != p.Value))
            throw new InvalidOperationException("A scene object outside Perimeter_Machinery changed.");
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Build.txt", $"corebuster_env_041: {ExpectedBays} perimeter bays; isolation, measured keep-outs, bay footprints, material hashes, and save/reload passed.\n");
        Debug.Log($"NORTH-WEST PERIMETER VERIFIED: {before.Count} outside transforms unchanged.");
    }

    [MenuItem("Tools/CoreBuster/Validate North-West Room Perimeter Machinery")]
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

    [MenuItem("Tools/CoreBuster/Capture North-West Room Perimeter Machinery")]
    public static void CaptureMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        var room = layout.interior;
        var c = layout.report.centre;
        float eye = layout.Floor + layout.report.playerEyeHeight;
        float fy = layout.Floor + 1.8f;
        var views = new[]
        {
            ("East_Wall", new Vector3(c.x - 3f, eye, c.z), new Vector3(room.max.x - 1.5f, fy, c.z + 4f)),
            ("East_Wall_South", new Vector3(c.x - 3f, eye, c.z), new Vector3(room.max.x - 1.5f, fy, room.min.z + 6f)),
            ("North_Wall_East", new Vector3(c.x + 2f, eye, c.z), new Vector3(c.x + 7f, fy, room.max.z - 1.5f)),
            ("North_Wall_West", new Vector3(c.x - 2f, eye, c.z), new Vector3(room.min.x + 7f, fy, room.max.z - 1.5f)),
            ("West_Wall", new Vector3(c.x + 3f, eye, c.z), new Vector3(room.min.x + 1.5f, fy, c.z + 6f)),
            ("South_Flush_Bay", new Vector3(c.x, eye, c.z - 3f), new Vector3(c.x - 1f, fy, room.min.z + 1f)),
        };
        Directory.CreateDirectory(Output);
        var go = new GameObject("Temporary Room 3 perimeter review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 70;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 200;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        // Unsaved review lamp: the room has no lighting until corebuster_env_037.
        var lamp = new GameObject("Temporary perimeter review lamp") { hideFlags = HideFlags.HideAndDontSave };
        var light = lamp.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 16;
        light.intensity = 6;
        light.color = new Color(1f, .78f, .5f);
        light.shadows = LightShadows.None;
        try
        {
            foreach (var view in views)
            {
                lamp.transform.position = Vector3.Lerp(view.Item2, view.Item3, .55f) + Vector3.up * 2f;
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
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Perimeter capture dirtied MainScene.");
    }

    static List<PlanBay> OwnedBays()
    {
        var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(PlanPath));
        var bays = plan.bays.Where(b => b.owner == Owner).ToList();
        if (bays.Count != ExpectedBays || bays.Any(b => !Profiles.ContainsKey(b.id)))
            throw new InvalidOperationException($"Density plan assigns {bays.Count} bays to {Owner}; this builder has {Profiles.Count} profiles for {ExpectedBays}.");
        return bays;
    }

    static void Build(NorthWestRoomFramework.Layout layout)
    {
        var bays = OwnedBays();
        Parts parts = LoadParts();
        Transform section = NorthWestRoomFramework.BeginSectionRebuild(layout, Section.PerimeterMachinery, Owner);
        foreach (var bay in bays) BuildBay(layout, section, bay, parts);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static Parts LoadParts()
    {
        EnsureFolder(Folder + "/Meshes");
        EnsureFolder(Folder + "/Materials");
        var cube = new ProceduralMeshBuilder(); cube.AddBlock(Vector3.zero, Vector3.one);
        var cyl = new ProceduralMeshBuilder(); cyl.AddCylinder(Vector3.down * .5f, Vector3.up * .5f, .5f, .5f, 16, true);
        return new Parts
        {
            cube = Save(cube.ToMesh("SM_Perimeter_Unit_Cube"), "Meshes/SM_Perimeter_Unit_Cube.asset"),
            cylinder = Save(cyl.ToMesh("SM_Perimeter_Unit_Cylinder"), "Meshes/SM_Perimeter_Unit_Cylinder.asset"),
            warmGrey = Finish("M_Perimeter_WarmGrey", new Color(.50f, .48f, .42f), .32f, .34f),
            tan = Finish("M_Perimeter_Tan", new Color(.58f, .46f, .28f), .25f, .30f),
            ochre = Finish("M_Perimeter_Ochre", new Color(.70f, .46f, .10f), .20f, .28f),
            graphite = Load<Material>(GraphitePath),
            steel = Load<Material>(SteelPath),
            hazard = Load<Material>(HazardPath),
        };
    }

    static void BuildBay(NorthWestRoomFramework.Layout layout, Transform section, PlanBay bay, Parts p)
    {
        var profile = Profiles[bay.id];
        var min = new Vector3(bay.footprint.min.x, 0, bay.footprint.min.z);
        var max = new Vector3(bay.footprint.max.x, 0, bay.footprint.max.z);
        var room = layout.interior;
        // Wall normal points into the room; the row grows along "right" = up x normal.
        Vector3 normal;
        const float touch = .05f;
        if (max.x > room.max.x - touch) normal = Vector3.left;
        else if (min.x < room.min.x + touch) normal = Vector3.right;
        else if (max.z > room.max.z - touch) normal = Vector3.back;
        else if (min.z < room.min.z + touch) normal = Vector3.forward;
        else throw new InvalidOperationException($"Bay {bay.id} does not touch a room wall.");
        Vector3 right = Vector3.Cross(Vector3.up, normal);
        Vector3 size = max - min;
        bool alongX = Mathf.Abs(normal.x) < .5f;
        float depth = alongX ? size.z : size.x, length = alongX ? size.x : size.z;
        Vector3 centre = new Vector3((min.x + max.x) * .5f, layout.Floor, (min.z + max.z) * .5f);

        Transform root = Group(section, $"Bay_{bay.id}");
        root.SetPositionAndRotation(centre - normal * (depth * .5f) - right * (length * .5f), Quaternion.LookRotation(normal));

        int n = profile.widths.Length;
        float gap = .08f + (bay.id[1] % 3) * .02f;
        float usable = length - 2 * EndInset - (n - 1) * gap;
        float scale = usable / profile.widths.Sum();
        float maxDepth = depth - WallInset - Kerb;
        float u = EndInset, tallest = 0, deepest = 0;
        int seed = bay.id[0] + bay.id[1] * 3;

        for (int i = 0; i < n; i++)
        {
            float w = profile.widths[i] * scale;
            float h = Mathf.Min(profile.heights[i], MaxTop - Kerb - .04f);
            float d = Mathf.Max(.5f, maxDepth * profile.depth[i]);
            tallest = Mathf.Max(tallest, h);
            deepest = Mathf.Max(deepest, d);
            int style = (i + seed) % 3;
            bool tanBody = (i + seed) % 4 == 3;
            Unit(root, $"Unit_{i}", u, w, h, d, style, tanBody, p);
            if (i < n - 1 && ((i + seed) & 1) == 0)
            {
                float next = Mathf.Min(h, Mathf.Min(profile.heights[i + 1], MaxTop - Kerb - .04f));
                Part(root, $"Side_Conduit_{i}", p.cylinder, p.steel, new Vector3(u + w + gap * .5f, Kerb + next * .5f, WallInset + Mathf.Min(d, maxDepth * profile.depth[i + 1]) + .03f),
                    Quaternion.identity, new Vector3(.07f, next, .07f));
            }
            u += w + gap;
        }

        // Housekeeping kerb with a hazard-yellow trim along its front edge.
        float kerbDepth = deepest + .1f;
        Part(root, "Kerb", p.cube, p.tan, new Vector3(length * .5f, Kerb * .5f, WallInset + kerbDepth * .5f), Quaternion.identity,
            new Vector3(length - 2 * EndInset, Kerb, kerbDepth));
        Part(root, "Kerb_Hazard_Trim", p.cube, p.hazard, new Vector3(length * .5f, Kerb * .5f, WallInset + kerbDepth + .004f), Quaternion.identity,
            new Vector3(length - 2 * EndInset, .05f, .01f));

        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(length * .5f, (Kerb + tallest) * .5f, WallInset + kerbDepth * .5f);
        box.size = new Vector3(length - 2 * EndInset, Kerb + tallest, kerbDepth);
        if (profile.carve)
        {
            var obstacle = root.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = box.size;
            obstacle.carving = true;
        }
    }

    /// <summary>One cabinet: body, door, ochre band, top cap and style-dependent louvres or indicators.</summary>
    static void Unit(Transform row, string name, float u, float w, float h, float d, int style, bool tanBody, Parts p)
    {
        Transform t = Group(row, name);
        t.localPosition = new Vector3(u + w * .5f, Kerb, WallInset);
        Material body = tanBody ? p.tan : p.warmGrey, door = tanBody ? p.warmGrey : p.tan;
        Part(t, "Body", p.cube, body, new Vector3(0, h * .5f, d * .5f), Quaternion.identity, new Vector3(w, h, d));
        Part(t, "Door", p.cube, door, new Vector3(0, h * .46f, d + .012f), Quaternion.identity, new Vector3(w * .84f, h * .78f, .024f));
        Part(t, "Ochre_Band", p.cube, p.ochre, new Vector3(0, h * .90f, d * .5f), Quaternion.identity, new Vector3(w + .012f, .12f, d + .012f));
        Part(t, "Top_Cap", p.cube, p.graphite, new Vector3(0, h + .02f, d * .5f), Quaternion.identity, new Vector3(w + .05f, .04f, d + .05f));
        Part(t, "Handle", p.cube, p.graphite, new Vector3(w * .32f, h * .46f, d + .035f), Quaternion.identity, new Vector3(.035f, .24f, .03f));
        if (style == 1)
            for (int i = 0; i < 4; i++)
                Part(t, "Louvre_" + i, p.cube, p.graphite, new Vector3(0, h * .26f + i * .09f, d + .03f), Quaternion.identity, new Vector3(w * .6f, .035f, .025f));
        else if (style == 2)
        {
            Part(t, "Indicator_Panel", p.cube, p.graphite, new Vector3(0, h * .70f, d + .03f), Quaternion.identity, new Vector3(w * .5f, .16f, .025f));
            Part(t, "Indicator_Strip", p.cube, p.hazard, new Vector3(0, h * .70f, d + .045f), Quaternion.identity, new Vector3(w * .38f, .035f, .012f));
        }
        else
            Part(t, "Door_Seam", p.cube, p.graphite, new Vector3(0, h * .46f, d + .028f), Quaternion.identity, new Vector3(.02f, h * .74f, .012f));
        if (h >= 2.5f && h + Kerb + .34f <= MaxTop && style != 2)
            Part(t, "Vent_Stack", p.cylinder, p.graphite, new Vector3(0, h + .2f, d * .5f), Quaternion.identity, new Vector3(.22f, .3f, .22f));
    }

    static void RequireValid(NorthWestRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        if (!NorthWestRoomFramework.Validate(scene, layout, true) ||
            !NorthWestRoomFramework.ValidateSection(scene, layout, Section.PerimeterMachinery, true))
            throw new InvalidOperationException("North-West Room framework or perimeter section validation failed.");
        var section = NorthWestRoomFramework.FindSection(scene, Section.PerimeterMachinery);
        var bays = OwnedBays().ToDictionary(b => "Bay_" + b.id);
        var roots = section.Cast<Transform>().ToArray();
        if (roots.Length != ExpectedBays || roots.Any(r => !bays.ContainsKey(r.name)))
            throw new InvalidOperationException($"Expected {ExpectedBays} perimeter bays named from the density plan.");
        var errors = new List<string>();
        foreach (var root in roots)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger).ToArray();
            if (colliders.Length != 1) errors.Add($"{root.name} has {colliders.Length} colliders; expected one consolidated box.");
            var foot = bays[root.name].footprint;
            var plan = new Bounds(); plan.SetMinMax(new Vector3(foot.min.x, -1000, foot.min.z), new Vector3(foot.max.x, 1000, foot.max.z));
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var b = renderer.bounds;
                if (b.min.x < plan.min.x - .03f || b.max.x > plan.max.x + .03f || b.min.z < plan.min.z - .03f || b.max.z > plan.max.z + .03f)
                    errors.Add($"{root.name}/{renderer.name} leaves its density-plan footprint.");
                if (b.max.y > layout.Floor + MaxTop + .03f) errors.Add($"{root.name}/{renderer.name} rises above {MaxTop} m.");
            }
        }
        int obstacles = section.GetComponentsInChildren<NavMeshObstacle>(true).Count(o => o.enabled && o.carving);
        if (obstacles != ExpectedObstacles) errors.Add($"Expected {ExpectedObstacles} carving obstacles, found {obstacles}.");
        int prefabs = section.GetComponentsInChildren<Transform>(true).Count(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject));
        if (prefabs > PrefabCeiling) errors.Add($"Perimeter section uses {prefabs}/{PrefabCeiling} prefab instances.");
        if (section.GetComponentsInChildren<Light>(true).Length != 0) errors.Add("Perimeter section must carry no lights (corebuster_env_037).");
        if (section.GetComponentsInChildren<MonoBehaviour>(true).Length != 0) errors.Add("Perimeter section must be script-free.");
        var newMaterials = section.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m && AssetDatabase.GetAssetPath(m).StartsWith(Folder + "/")).Distinct().Count();
        if (newMaterials > 3) errors.Add($"Perimeter section owns {newMaterials}/3 new materials.");
        if (errors.Count > 0) throw new InvalidOperationException("North-West perimeter checks failed:\n  " + string.Join("\n  ", errors));
        int renderers = section.GetComponentsInChildren<Renderer>(true).Length;
        Debug.Log($"NORTH-WEST PERIMETER CHECKS PASSED: {ExpectedBays} bays, {renderers} renderers, {obstacles}/{ExpectedObstacles} carving obstacles, {prefabs}/{PrefabCeiling} prefabs, {newMaterials}/3 new materials.");
    }

    static void VerifyIsolatedUndo(NorthWestRoomFramework.Layout layout)
    {
        var section = NorthWestRoomFramework.FindSection(SceneManager.GetActiveScene(), Section.PerimeterMachinery);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        NorthWestRoomFramework.BeginSectionRebuild(layout, Section.PerimeterMachinery, Owner);
        Undo.CollapseUndoOperations(group);
        if (section.childCount != 0) throw new InvalidOperationException("Perimeter section clear left content.");
        Undo.PerformUndo();
        if (section.childCount != ExpectedBays) throw new InvalidOperationException("Perimeter section Undo did not restore all bays.");
    }

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build North-West Room perimeter machinery");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void Part(Transform parent, string name, Mesh mesh, Material material, Vector3 pos, Quaternion rotation, Vector3 scale)
    {
        var t = Group(parent, name);
        t.localPosition = pos;
        t.localRotation = rotation;
        t.localScale = scale;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

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
        NorthWestRoomFramework.RequireOwnedAsset(existing, Section.PerimeterMachinery);
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
        throw new InvalidOperationException("Missing perimeter dependency: " + path);

    static Dictionary<string, string> SnapshotOutside()
    {
        var scene = SceneManager.GetActiveScene();
        var section = NorthWestRoomFramework.FindSection(scene, Section.PerimeterMachinery);
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => !t.IsChildOf(section))
            .ToDictionary(t => GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), t =>
            {
                var renderer = t.GetComponent<Renderer>();
                string mats = renderer ? string.Join(",", renderer.sharedMaterials.Select(m => m ? AssetDatabase.GetAssetPath(m) : "null")) : "";
                return FormattableString.Invariant($"{t.name}|{(t.parent ? GlobalObjectId.GetGlobalObjectIdSlow(t.parent).ToString() : "root")}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}|{t.gameObject.activeSelf}|{mats}");
            });
    }
}

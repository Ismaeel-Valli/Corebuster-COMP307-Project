using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = NorthWestRoomFramework.Section;

/// <summary>
/// corebuster_env_037. Owns Lighting, derived surface partitions and the room-owned lens emission repair.
/// Leaves the original shell, shared pack assets, gameplay and pipeline settings unchanged.
/// </summary>
public static partial class NorthWestRoomLighting
{
    const string Owner = "corebuster_env_037", UndoName = "Build North-West Room Lighting";
    const string Folder = NorthWestRoomFramework.AssetRoot + "/Lighting";
    const string Output = "ReviewCaptures/NorthWestRoom/037";
    static readonly Color Amber = new Color(1f, .76f, .48f), Red = new Color(1f, .14f, .055f), Blue = new Color(.58f, .76f, 1f);
    static Mesh box, cylinder;
    static Material housing, amber, red, blue;

    [MenuItem("Tools/CoreBuster/Build North-West Room Lighting")]
    public static void BuildMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(UndoName);
        try { Build(layout); SplitSurfaces(layout); Validate(layout); Undo.CollapseUndoOperations(group); }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = NorthWestRoomFramework.MeasureLayout();
        Directory.CreateDirectory(Output);
        var before = Snapshot();
        var materials = NorthWestRoomFramework.CaptureProtectedMaterialHashes();
        if (!Directory.Exists(Output + "/Neighbours/Before")) CaptureNeighbours("Before", layout);
        Build(layout);
        SplitSurfaces(layout);
        VerifyUndo(layout);
        Validate(layout);
        CompareSnapshot(before);
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(materials);
        if (!NorthWestRoomDensity.Check(SceneManager.GetActiveScene(), layout, true, true))
            throw new InvalidOperationException("Density regression.");
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Scene save failed.");
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = NorthWestRoomFramework.MeasureLayout();
        Validate(layout); CompareSnapshot(before);
        NorthWestRoomRendering.ValidateMenu();
        NorthWestRoomFloor.ValidateMenu();
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(materials);
        Capture(layout); CompareNeighbours();
        File.WriteAllText(Output + "/Build.txt", $"corebuster_env_037: isolated Undo, validation, density, save/reload and {before.Count} protected object states passed.\nLive two-wave visibility and owner review remain required.\n");
        Debug.Log("NORTH-WEST LIGHTING BUILD VERIFIED; live two-wave visibility and owner review remain required.");
    }

    [MenuItem("Tools/CoreBuster/Validate North-West Room Lighting")]
    public static void ValidateMenu() => Validate(NorthWestRoomFramework.MeasureLayout());
    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        ValidateMenu();
    }
    [MenuItem("Tools/CoreBuster/Capture North-West Room Lighting")]
    public static void CaptureMenu() => Capture(NorthWestRoomFramework.MeasureLayout());
    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        ValidateMenu(); CaptureMenu(); CompareNeighbours();
    }

    static void Build(NorthWestRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var s in new[] { Section.Rendering, Section.TurbineMachinery, Section.ElectricalSystems,
            Section.PerimeterMachinery, Section.PressureSystems, Section.FloorDetail, Section.Cover })
            if (NorthWestRoomFramework.FindSection(scene, s).childCount == 0) throw new InvalidOperationException("Build " + s + " first.");
        EnsureFolder(Folder + "/Meshes"); EnsureFolder(Folder + "/Materials");
        box = Save(BoxMesh(), Folder + "/Meshes/SM_Fixture_Box.asset");
        cylinder = Save(CylinderMesh(), Folder + "/Meshes/SM_Fixture_Cylinder.asset");
        housing = Load("Perimeter_Machinery/Materials/M_Perimeter_WarmGrey.mat");
        blue = Load("Rendering/Materials/M_Room3_Electric_Emissive.mat");
        // URP derives _EMISSION from AnyEmissive on import; None silently removes the keyword.
        // Fixtures do not contribute GI and this pass never bakes lightmaps.
        blue.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        blue.EnableKeyword("_EMISSION"); EditorUtility.SetDirty(blue);
        amber = Finish("M_Practical_Amber", Amber, new Color(2f, .65f, .16f, 1));
        red = Finish("M_Beacon_Red", Red, new Color(2f, .025f, .008f, 1));
        var root = NorthWestRoomFramework.BeginSectionRebuild(layout, Section.Lighting, Owner);
        var min = layout.interior.min; var max = layout.interior.max; var c = layout.interior.center;
        // Bay-front coverage sites, measured from the approved density map. A shared fixture lights
        // contiguous bays; its full forward-culling frustum still fits inside the measured shell.
        var sites = new (string bays, float x, float z, float angle)[] {
            ("S1", 1.250f, 3.250f, 17.571f),
            ("W2", 2.500f, 23.250f, 13.920f),
            ("N9", 1.000f, 29.500f, 13.920f),
            ("W3", 2.500f, 20.250f, 35.165f),
            ("N7_N8_W1", 4.500f, 28.000f, 52.195f),
            ("S2", 7.000f, 2.750f, 38.514f),
            ("N5_N6", 10.000f, 29.500f, 32.876f),
            ("S3", 13.000f, 2.750f, 38.514f),
            ("S4", 16.250f, 1.000f, 13.920f),
            ("S5", 19.500f, 2.500f, 35.165f),
            ("N3_N4", 22.750f, 29.250f, 36.268f),
            ("S6_E1", 28.000f, 4.250f, 53.295f),
            ("E2", 29.250f, 12.750f, 37.501f),
            ("E4", 29.500f, 20.250f, 34.133f),
            ("E6_N1_N2", 28.000f, 28.250f, 49.156f),
            ("E5", 29.400f, 23.250f, 16.463f),
            ("E3", 31.000f, 16.000f, 12.803f),
            ("S7", 31.000f, 3.000f, 12.803f),
            ("E7_N1", 30.000f, 30.000f, 25.917f),
        };
        foreach (var site in sites)
        {
            Vector3 p = new Vector3(min.x + site.x, 7.5f, min.z + site.z);
            bool electric = site.bays == "N7_N8_W1" || site.bays == "E6_N1_N2";
            bool warning = site.bays == "W3";
            int kind = electric ? 3 : warning ? 2 : site.angle > 30 ? 0 : 1;
            var f = Fixture(root, "Bay_" + site.bays, p, site.bays.StartsWith("E") || site.bays.StartsWith("W"), kind, layout);
            Spot(f, "Bay_Key", p - Vector3.up * .2f, Vector3.down, 7.7f, site.angle,
                electric ? 500f : warning ? 420f : 750f, electric ? Blue : warning ? Red : Amber, false);
        }
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 p = new Vector3(c.x + x * 4.5f, 9.2f, c.z + z * 4.5f);
                bool entry = x == 1 && z == -1;
                if (entry) p += new Vector3(1f, 0, -1f);
                var f = Fixture(root, "Aisle_Strip_" + x + "_" + z, p, true, 0, layout);
                Spot(f, "Arena_Key", p - Vector3.up * .2f, Vector3.down, 11f, entry ? 86f : 88f, 1700f, Amber, true);
            }
        var gate = layout.report.gates.Single(g => g.name == NorthWestRoomSurvey.BossDoorName);
        Vector3 beacon = new Vector3(min.x + 1.4f, 5.2f, gate.position.z);
        var boss = Fixture(root, "Boss_Warning_Beacon", beacon, false, 2, layout);
        Spot(boss, "Warning_Key", beacon - Vector3.up * .2f, Vector3.down, 5.2f, 28f, 500f, Red, false);
        AssetDatabase.SaveAssets(); Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(scene);
    }

    internal static Transform[] DropBayFixtures()
    {
        var lighting = NorthWestRoomFramework.FindSection(SceneManager.GetActiveScene(), Section.Lighting);
        return new[] { lighting.Find("Bay_W2"), lighting.Find("Bay_E5") };
    }

    // env_046: maintain front coverage after the approved footprint extension, without
    // rebuilding lighting, rewriting assets or discarding existing surface partitions.
    internal static void RefreshDropBayFixtures(NorthWestRoomFramework.Layout layout)
    {
        var fixtures = DropBayFixtures();
        for (int i = 0; i < fixtures.Length; i++)
        {
            var f = fixtures[i];
            if (!f) throw new InvalidOperationException("Missing saved drop-bay light fixture.");
            Undo.RecordObject(f, "Align drop-bay light with extended skid");
            Vector3 p = f.position;
            p.x = layout.interior.min.x + (i == 0 ? 2.500f : 29.400f);
            f.position = p;
            // Recompute the suspension against its new overhead anchor, as Fixture does.
            var rod = f.Find("Suspension_0");
            if (!rod) throw new InvalidOperationException("Missing drop-bay suspension.");
            var world = f.TransformPoint(new Vector3(0, .34f, 0));
            float top = f.parent.parent.GetComponentsInChildren<Renderer>().Where(r => !r.transform.IsChildOf(f.parent))
                .Select(r => r.bounds).Where(b => b.min.y > world.y && world.x >= b.min.x && world.x <= b.max.x && world.z >= b.min.z && world.z <= b.max.z)
                .Select(b => b.min.y).DefaultIfEmpty(layout.interior.max.y - .025f).Min();
            float length = top - world.y;
            Undo.RecordObject(rod, "Align drop-bay suspension");
            rod.localPosition = new Vector3(0, .34f + length * .5f, 0);
            rod.localScale = new Vector3(.035f, length, .035f);
        }
        Physics.SyncTransforms();
    }

    static Transform Fixture(Transform root, string name, Vector3 p, bool alongZ, int kind, NorthWestRoomFramework.Layout l)
    {
        var f = Group(root, name); f.position = p;
        if (alongZ) f.rotation = Quaternion.Euler(0, 90, 0);
        Material lens = kind == 2 ? red : kind == 3 ? blue : amber;
        if (kind == 0)
        {
            Part(f, "Steel_Tray", new Vector3(0, .12f, 0), new Vector3(3.2f, .18f, .48f), housing);
            Part(f, "Reflector", Vector3.zero, new Vector3(3f, .055f, .39f), housing);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                var tube = Part(f, "Tube_" + sign, new Vector3(0, -.055f, sign * .115f), new Vector3(.085f, 2.95f, .085f), lens, true);
                tube.localRotation = Quaternion.Euler(0, 0, 90);
                Part(f, "End_Cap_" + sign, new Vector3(sign * 1.53f, -.02f, 0), new Vector3(.13f, .19f, .46f), housing);
            }
            for (int i = -2; i <= 2; i++) Part(f, "Guard_" + i, new Vector3(i * .6f, -.12f, 0), new Vector3(.027f, .025f, .43f), housing);
        }
        else
        {
            Part(f, "Base", new Vector3(0, .28f, 0), new Vector3(.48f, .1f, .48f), housing, true);
            Part(f, "Lens", Vector3.zero, new Vector3(.27f, .48f, .27f), lens, true);
            Part(f, "Guard_Ring", new Vector3(0, -.27f, 0), new Vector3(.42f, .065f, .42f), housing, true);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2;
                Part(f, "Cage_" + i, new Vector3(Mathf.Cos(a) * .18f, 0, Mathf.Sin(a) * .18f), new Vector3(.025f, .52f, .025f), housing);
            }
        }
        // Suspensions stop at the first overhead surface, so no rods pass through an existing duct.
        foreach (float x in kind == 0 ? new[] { -1.1f, 1.1f } : new[] { 0f })
        {
            var world = f.TransformPoint(new Vector3(x, .34f, 0));
            float top = root.parent.GetComponentsInChildren<Renderer>().Where(r => !r.transform.IsChildOf(root))
                .Select(r => r.bounds).Where(b => b.min.y > world.y && world.x >= b.min.x && world.x <= b.max.x && world.z >= b.min.z && world.z <= b.max.z)
                .Select(b => b.min.y).DefaultIfEmpty(l.interior.max.y - .025f).Min();
            float length = top - world.y;
            Part(f, "Suspension_" + x, new Vector3(x, .34f + length * .5f, 0), new Vector3(.035f, length, .035f), housing);
        }
        return f;
    }

    static Light Spot(Transform parent, string name, Vector3 p, Vector3 direction, float range, float angle, float intensity, Color color, bool shadows)
    {
        var t = Group(parent, name); t.SetPositionAndRotation(p, Quaternion.LookRotation(direction));
        var light = t.gameObject.AddComponent<Light>(); light.type = LightType.Spot;
        light.range = range; light.spotAngle = angle; light.innerSpotAngle = angle * .75f;
        light.color = color; light.intensity = intensity; light.lightmapBakeType = LightmapBakeType.Realtime;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        light.shadowBias = .025f; light.shadowNormalBias = .12f; light.renderMode = LightRenderMode.ForcePixel;
        var data = t.gameObject.AddComponent<UniversalAdditionalLightData>();
        using var so = new SerializedObject(data);
        so.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        return light;
    }

    static Material Load(string relative) => AssetDatabase.LoadAssetAtPath<Material>(NorthWestRoomFramework.AssetRoot + "/" + relative)
        ?? throw new InvalidOperationException("Missing room material " + relative);
    static Material Finish(string name, Color color, Color emission)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", color); m.SetFloat("_Metallic", .15f); m.SetFloat("_Smoothness", .35f);
        m.SetColor("_EmissionColor", emission); m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        return Save(m, Folder + "/Materials/" + name + ".mat");
    }
    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, UndoName);
        go.transform.SetParent(parent, false); return go.transform;
    }
    static Transform Part(Transform parent, string name, Vector3 p, Vector3 size, Material m, bool round = false)
    {
        var t = Group(parent, name); t.localPosition = p; t.localScale = size;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = round ? cylinder : box;
        var r = t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(t.gameObject, GameObjectUtility.GetStaticEditorFlags(parent.parent.gameObject));
        return t;
    }
    static T Save<T>(T value, string path, Section owner = Section.Lighting) where T : Object
    {
        value.name = Path.GetFileNameWithoutExtension(path);
        var old = AssetDatabase.LoadAssetAtPath<T>(path);
        if (old) { EditorUtility.CopySerialized(value, old); Object.DestroyImmediate(value); EditorUtility.SetDirty(old); }
        else { AssetDatabase.CreateAsset(value, path); old = value; }
        NorthWestRoomFramework.RequireOwnedAsset(old, owner); return old;
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, split));
        AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
    static Mesh BoxMesh()
    {
        var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
        foreach (Vector3 normal in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
        {
            Vector3 u = Mathf.Abs(normal.y) > .5f ? Vector3.right : Vector3.up, v = Vector3.Cross(normal, u);
            int start = vertices.Count;
            foreach (var p in new[] { new Vector2(-1,-1), new Vector2(1,-1), new Vector2(1,1), new Vector2(-1,1) })
            { vertices.Add((normal + u * p.x + v * p.y) * .5f); uv.Add((p + Vector2.one) * .5f); }
            triangles.AddRange(new[] { start, start+1, start+2, start, start+2, start+3 });
        }
        var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
    static Mesh CylinderMesh()
    {
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI / 8, b = (i + 1) * Mathf.PI / 8;
            Vector3 p = new Vector3(Mathf.Cos(a) * .5f, 0, Mathf.Sin(a) * .5f), q = new Vector3(Mathf.Cos(b) * .5f, 0, Mathf.Sin(b) * .5f);
            int s = vertices.Count;
            vertices.AddRange(new[] { p-Vector3.up*.5f, p+Vector3.up*.5f, q+Vector3.up*.5f, q-Vector3.up*.5f,
                Vector3.up*.5f, p+Vector3.up*.5f, q+Vector3.up*.5f, -Vector3.up*.5f, q-Vector3.up*.5f, p-Vector3.up*.5f });
            triangles.AddRange(new[] { s,s+1,s+2,s,s+2,s+3,s+4,s+6,s+5,s+7,s+9,s+8 });
        }
        var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
}

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

/// <summary>Builds the two horizontal, section-owned power machines for corebuster_env_031.</summary>
public static class NorthWestRoomTurbine
{
    const string Owner = "corebuster_env_031";
    const string Folder = "Assets/FPS/Art/NorthWestRoom/Turbine_Machinery";
    const string Kit = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit/";
    const string Output = "ReviewCaptures/NorthWestRoom/031";

    struct Parts
    {
        public Mesh drum, endBell, flange, bearing, foot, rail, fin, conduit;
        public Material graphite, steel, oxide, yellow;
        public GameObject pipe, reducer, elbow, tee, kitFlange, bracket;
    }

    [MenuItem("Tools/CoreBuster/Build North-West Room Turbines")]
    public static void BuildMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build North-West Room Turbines");
        try { Build(layout); RequireValid(layout); Undo.CollapseUndoOperations(group); }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = NorthWestRoomFramework.MeasureLayout();
        var before = SnapshotOutside();
        var materials = NorthWestRoomFramework.CaptureProtectedMaterialHashes();
        Build(layout);
        VerifyIsolatedUndo(layout);
        RequireValid(layout);
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(materials);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
            throw new InvalidOperationException("Could not save North-West Room turbines.");
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        var after = SnapshotOutside();
        if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out string value) || value != p.Value))
            throw new InvalidOperationException("A scene object outside Turbine_Machinery changed.");
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(materials);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Build.txt", "corebuster_env_031: two south-wall horizontal assemblies; section isolation, measured keep-outs, asset ownership, shared material hashes, and save/reload passed.\n");
        Debug.Log($"NORTH-WEST TURBINES VERIFIED: {before.Count} outside transforms unchanged; two assemblies, save/reload and measured reservations passed.");
    }

    [MenuItem("Tools/CoreBuster/Validate North-West Room Turbines")]
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

    [MenuItem("Tools/CoreBuster/Capture North-West Room Turbines")]
    public static void CaptureMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        var report = layout.report;
        var east = report.gates.Single(g => g.name == NorthWestRoomSurvey.EastDoorName);
        var views = new[]
        {
            ("East_Entry", east.position - east.outward * 4f + Vector3.up * report.playerEyeHeight,
                report.centre + Vector3.up * 1.6f),
            ("Red_Terminal", new Vector3(report.redTerminal.standing.position.x, report.redTerminal.eyeY, report.redTerminal.standing.position.z),
                report.redTerminal.focus.position),
            ("Master_Terminal", new Vector3(report.masterTerminal.standing.position.x, report.masterTerminal.eyeY, report.masterTerminal.standing.position.z),
                report.masterTerminal.lookAt.position),
            ("South_Machinery", new Vector3(report.centre.x, report.floorTopY + report.playerEyeHeight, report.centre.z - 6f),
                new Vector3(report.centre.x, report.floorTopY + 1.5f, report.interior.min.z + 1.7f)),
            ("West_Turbine_Detail", new Vector3(report.interior.min.x + 7.1f, report.floorTopY + report.playerEyeHeight, report.interior.min.z + 9f),
                new Vector3(report.interior.min.x + 7.1f, report.floorTopY + 1.5f, report.interior.min.z + 1.72f)),
            ("East_Generator_Detail", new Vector3(report.interior.max.x - 6.2f, report.floorTopY + report.playerEyeHeight, report.interior.min.z + 9f),
                new Vector3(report.interior.max.x - 6.2f, report.floorTopY + 1.5f, report.interior.min.z + 1.72f)),
        };
        Directory.CreateDirectory(Output);
        var go = new GameObject("Temporary Room 3 turbine camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 65;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 200;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var lightObject = new GameObject("Temporary turbine review light") { hideFlags = HideFlags.HideAndDontSave };
        var reviewLight = lightObject.AddComponent<Light>();
        reviewLight.type = LightType.Point;
        reviewLight.range = 12;
        reviewLight.intensity = 5;
        reviewLight.shadows = LightShadows.None;
        reviewLight.enabled = false;
        try
        {
            foreach (var view in views)
            {
                reviewLight.enabled = view.Item1.EndsWith("_Detail", StringComparison.Ordinal);
                if (reviewLight.enabled) lightObject.transform.position = view.Item3 + Vector3.up * 2.7f + Vector3.forward * 3f;
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
        finally { Object.DestroyImmediate(lightObject); Object.DestroyImmediate(go); }
        if (SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Turbine capture dirtied MainScene.");
    }

    static void Build(NorthWestRoomFramework.Layout layout)
    {
        if (layout.report.spawns.Count != 8 || layout.report.spawns.Any(s => !s.onNavMesh))
            throw new InvalidOperationException("The eight measured spawn points must remain on the NavMesh.");
        // Resolve and author assets before clearing the section. Existing IDs survive a rebuild.
        Parts p = LoadParts();
        Transform section = NorthWestRoomFramework.BeginSectionRebuild(layout, Section.TurbineMachinery, Owner);
        float y = layout.Floor;
        float z = layout.interior.min.z + 1.72f;
        // The survey reserves separate 14 m south-wall heavy zones, split by spawn 2.
        BuildAssembly(section, "West_Turbine", new Vector3(layout.interior.min.x + 7.1f, y, z), 8.8f, p, false);
        BuildAssembly(section, "East_Generator", new Vector3(layout.interior.max.x - 6.2f, y, z), 8.8f, p, true);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static Parts LoadParts()
    {
        EnsureFolder(Folder + "/Meshes");
        EnsureFolder(Folder + "/Materials");
        Material bare = Load<Material>(Kit + "Materials/M_HeavyPipe_BareSteel.mat");
        Material red = Load<Material>(Kit + "Materials/M_HeavyPipe_RedOxide.mat");
        Material yellow = Load<Material>(Kit + "Materials/M_HeavyPipe_HazardYellow.mat");
        return new Parts
        {
            drum = Cylinder("SM_Turbine_Drum", 6.0f, .83f),
            endBell = Cylinder("SM_End_Bell", .55f, 1.04f),
            flange = Cylinder("SM_Drive_Flange", .20f, 1.12f),
            bearing = Block("SM_Bearing_Block", new Vector3(.58f, 1.42f, 1.62f)),
            foot = Block("SM_Anchor_Foot", new Vector3(.72f, .32f, 2.20f)),
            rail = Block("SM_Base_Rail", new Vector3(8.30f, .18f, .19f)),
            fin = Block("SM_Cooling_Fin", new Vector3(.09f, 1.9f, 1.9f)),
            conduit = Cylinder("SM_Power_Conduit", 6.5f, .095f),
            graphite = CopyFinish("M_Turbine_Graphite", bare, new Color(.18f, .21f, .23f), .36f, .39f),
            steel = CopyFinish("M_Turbine_Steel", bare, new Color(.42f, .46f, .47f), .48f, .43f),
            oxide = CopyFinish("M_Turbine_Oxide", red, new Color(.42f, .12f, .075f), .23f, .32f),
            yellow = CopyFinish("M_Turbine_Hazard", yellow, new Color(.70f, .46f, .09f), .24f, .36f),
            pipe = Load<GameObject>(Kit + "Prefabs/P_HeavyPipe_Straight_1m.prefab"),
            reducer = Load<GameObject>(Kit + "Prefabs/P_HeavyPipe_Reducer.prefab"),
            elbow = Load<GameObject>(Kit + "Prefabs/P_HeavyPipe_Elbow_90.prefab"),
            tee = Load<GameObject>(Kit + "Prefabs/P_HeavyPipe_Tee.prefab"),
            kitFlange = Load<GameObject>(Kit + "Prefabs/P_HeavyPipe_Flange.prefab"),
            bracket = Load<GameObject>(Kit + "Prefabs/P_HeavyPipe_WallBracket.prefab"),
        };
    }

    static void BuildAssembly(Transform section, string name, Vector3 world, float length, Parts p, bool generator)
    {
        Transform root = Group(section, name);
        root.position = world;
        Transform machine = Group(root, "Machine");
        float shaftY = 1.48f;
        Part(machine, "Horizontal_Armoured_Drum", p.drum, generator ? p.graphite : p.oxide, new Vector3(0, shaftY, 0));
        for (int sign = -1; sign <= 1; sign += 2)
        {
            Part(machine, sign < 0 ? "Inlet_Bell" : "Output_Bell", p.endBell, p.graphite, new Vector3(sign * 3.26f, shaftY, 0));
            Part(machine, sign < 0 ? "Drive_Flange_A" : "Drive_Flange_B", p.flange, p.steel, new Vector3(sign * 3.62f, shaftY, 0));
            Part(machine, sign < 0 ? "Bearing_A" : "Bearing_B", p.bearing, p.steel, new Vector3(sign * 3.83f, .84f, 0));
            Part(machine, sign < 0 ? "Foot_A" : "Foot_B", p.foot, p.oxide, new Vector3(sign * 3.72f, .16f, 0));
        }
        for (int i = 0; i < 7; i++)
            Part(machine, $"Rib_{i:00}", p.fin, i % 3 == 0 ? p.yellow : p.steel,
                new Vector3(-2.70f + i * .90f, shaftY, 0));
        for (int i = 0; i < 2; i++)
            Part(machine, "Longitudinal_Base_Rail_" + i, p.rail, p.graphite,
                new Vector3(0, .17f, i == 0 ? -.88f : .88f));
        // Non-walkable maintenance framing and a pair of thin electrical runs.
        for (int i = 0; i < 2; i++)
            Part(machine, "Power_Conduit_" + i, p.conduit, i == 0 ? p.yellow : p.oxide,
                new Vector3(0, 2.28f, i == 0 ? -.42f : .42f));

        Transform services = Group(root, "Kit_Service_Couplings");
        for (int sign = -1; sign <= 1; sign += 2)
        {
            float x = sign * 3.20f;
            KitPart(services, p.kitFlange, $"Kit_Flange_{sign}", new Vector3(x, 1.48f, -.30f), Quaternion.Euler(0, 90, 0), p.steel);
            KitPart(services, p.reducer, $"Reducer_{sign}", new Vector3(x, 1.49f, -.30f), Quaternion.Euler(0, 90, 0), p.oxide);
            KitPart(services, p.bracket, $"Wall_Bracket_{sign}", new Vector3(x, 2.25f, -.55f), Quaternion.identity, p.steel);
        }
        KitPart(services, p.pipe, "Kit_Pipe", new Vector3(0, 2.25f, -.40f), Quaternion.Euler(0, 90, 0), p.steel);
        KitPart(services, p.tee, "Kit_Tee", new Vector3(generator ? 1.65f : -1.65f, 2.25f, -.25f), Quaternion.Euler(0, 90, 0), p.oxide);
        KitPart(services, p.elbow, "Kit_Elbow", new Vector3(generator ? 2.25f : -2.25f, 2.25f, 0), Quaternion.Euler(0, 90, 0), p.steel);

        // One collision hull and one carve per machine: decorative ribs and kit parts have no colliders.
        Transform hull = Group(root, "Consolidated_Collision_Hull");
        var box = hull.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0, 1.28f, 0);
        box.size = new Vector3(length, 2.56f, 2.48f);
        var obstacle = root.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = box.center;
        obstacle.size = box.size;
        obstacle.carving = true;
    }

    static void RequireValid(NorthWestRoomFramework.Layout layout)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!NorthWestRoomFramework.Validate(scene, layout, true) ||
            !NorthWestRoomFramework.ValidateSection(scene, layout, Section.TurbineMachinery, true))
            throw new InvalidOperationException("North-West Room framework or turbine section validation failed.");
        var section = NorthWestRoomFramework.FindSection(scene, Section.TurbineMachinery);
        var roots = section.Cast<Transform>().ToArray();
        if (roots.Length != 2 || roots.Any(r => !r.GetComponent<NavMeshObstacle>() ||
            !r.GetComponent<NavMeshObstacle>().carving || r.GetComponentsInChildren<Collider>(true).Count(c => c.enabled) != 1))
            throw new InvalidOperationException("Expected two assemblies, each with one collision hull and one carving obstacle.");
        int prefabs = section.GetComponentsInChildren<Transform>(true).Count(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject));
        if (prefabs > 50) throw new InvalidOperationException($"Turbine section uses {prefabs} prefab instances; ceiling is 50.");
        Debug.Log($"NORTH-WEST TURBINE CHECKS PASSED: two horizontal assemblies; {prefabs}/50 prefab instances; measured keep-outs and consolidated collision clear.");
    }

    static void VerifyIsolatedUndo(NorthWestRoomFramework.Layout layout)
    {
        var section = NorthWestRoomFramework.FindSection(SceneManager.GetActiveScene(), Section.TurbineMachinery);
        if (section.childCount != 2) throw new InvalidOperationException("Cannot test Undo without both turbine assemblies.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Verify isolated turbine section Undo");
        NorthWestRoomFramework.BeginSectionRebuild(layout, Section.TurbineMachinery, Owner);
        Undo.CollapseUndoOperations(group);
        if (section.childCount != 0) throw new InvalidOperationException("Turbine section clear left old content.");
        Undo.PerformUndo();
        if (section.childCount != 2 || !section.Find("West_Turbine") || !section.Find("East_Generator"))
            throw new InvalidOperationException("Undo did not restore only the two turbine assemblies.");
    }

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build North-West Room turbine");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position)
    {
        Transform t = Group(parent, name);
        t.localPosition = position;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    static void KitPart(Transform parent, GameObject prefab, string name, Vector3 position, Quaternion rotation, Material material)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(go, "Build North-West Room kit coupling");
        go.name = name;
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        foreach (var collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = material;
    }

    static Mesh Cylinder(string name, float length, float radius)
    {
        var b = new ProceduralMeshBuilder();
        b.AddCylinder(Vector3.left * length * .5f, Vector3.right * length * .5f, radius, radius, 32, true);
        return Save(b.ToMesh(name), "Meshes/" + name + ".asset");
    }

    static Mesh Block(string name, Vector3 size)
    {
        var b = new ProceduralMeshBuilder();
        b.AddBlock(Vector3.zero, size);
        return Save(b.ToMesh(name), "Meshes/" + name + ".asset");
    }

    static Material CopyFinish(string name, Material source, Color color, float metallic, float smoothness)
    {
        var m = new Material(source) { name = name };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        return Save(m, "Materials/" + name + ".mat");
    }

    static T Save<T>(T generated, string relative) where T : Object
    {
        string path = Folder + "/" + relative;
        generated.name = Path.GetFileNameWithoutExtension(relative);
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing) { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
        else { AssetDatabase.CreateAsset(generated, path); existing = generated; }
        NorthWestRoomFramework.RequireOwnedAsset(existing, Section.TurbineMachinery);
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
        throw new InvalidOperationException("Missing turbine dependency: " + path);

    static Dictionary<string, string> SnapshotOutside()
    {
        Scene scene = SceneManager.GetActiveScene();
        Transform section = NorthWestRoomFramework.FindSection(scene, Section.TurbineMachinery);
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => t == section || !t.IsChildOf(section))
            .ToDictionary(t => GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), t =>
            {
                var renderer = t.GetComponent<Renderer>();
                string mats = renderer ? string.Join(",", renderer.sharedMaterials.Select(m => m ? AssetDatabase.GetAssetPath(m) : "null")) : "";
                return FormattableString.Invariant($"{t.name}|{(t.parent ? GlobalObjectId.GetGlobalObjectIdSlow(t.parent).ToString() : "root")}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}|{t.gameObject.activeSelf}|{mats}");
            });
    }
}

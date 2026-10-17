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

/// <summary>Section-owned power distribution dressing for corebuster_env_032.</summary>
public static class NorthWestRoomElectrical
{
    const string Owner = "corebuster_env_032";
    const string Folder = "Assets/FPS/Art/NorthWestRoom/Electrical_Systems";
    const string Modular = "Assets/Sci-Fi Styled Modular Pack/Prefabs/Machines/";
    const string Kit = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit/Materials/";
    const string Output = "ReviewCaptures/NorthWestRoom/032";

    struct Parts
    {
        public Mesh plinth, tank, cabinet, door, busbar, rod, disc, conduit;
        public Material graphite, steel, oxide, ceramic, arc;
        public GameObject battery;
    }

    [MenuItem("Tools/CoreBuster/Build North-West Room Electrical Systems")]
    public static void BuildMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build North-West Room Electrical Systems");
        try { Build(layout); RequireValid(layout); Undo.CollapseUndoOperations(group); }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = NorthWestRoomFramework.MeasureLayout();
        var before = SnapshotOutside();
        var protectedMaterials = NorthWestRoomFramework.CaptureProtectedMaterialHashes();
        Build(layout);
        VerifyIsolatedUndo(layout);
        RequireValid(layout);
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Could not save Room 3 electrical systems.");
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        var after = SnapshotOutside();
        if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out string value) || value != p.Value))
            throw new InvalidOperationException("A scene object outside Electrical_Systems changed.");
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Build.txt", "corebuster_env_032: two north-corner electrical banks; isolation, measured keep-outs, material hashes, and save/reload passed.\n");
        Debug.Log($"NORTH-WEST ELECTRICAL VERIFIED: {before.Count} outside transforms unchanged.");
    }

    [MenuItem("Tools/CoreBuster/Validate North-West Room Electrical Systems")]
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

    [MenuItem("Tools/CoreBuster/Capture North-West Room Electrical Systems")]
    public static void CaptureMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        var room = layout.interior;
        float eye = layout.Floor + layout.report.playerEyeHeight;
        var views = new[]
        {
            ("East_Entry", new Vector3(room.max.x - 4f, eye, layout.report.centre.z - 5f),
                new Vector3(room.max.x - 2.75f, layout.Floor + 1.8f, room.max.z - 1.35f)),
            ("North_East_Bank", new Vector3(room.max.x - 5.8f, eye, room.max.z - 5.5f),
                new Vector3(room.max.x - 2.75f, layout.Floor + 1.55f, room.max.z - 1.35f)),
            ("North_West_Bank", new Vector3(room.min.x + 6.2f, eye, room.max.z - 5.5f),
                new Vector3(room.min.x + 2.75f, layout.Floor + 1.55f, room.max.z - 1.35f)),
            ("Red_Terminal_Context", new Vector3(layout.report.redTerminal.standing.position.x, layout.report.redTerminal.eyeY,
                layout.report.redTerminal.standing.position.z), layout.report.redTerminal.focus.position),
            ("Master_Terminal_Context", new Vector3(layout.report.masterTerminal.standing.position.x, layout.report.masterTerminal.eyeY,
                layout.report.masterTerminal.standing.position.z), layout.report.masterTerminal.lookAt.position),
        };
        Directory.CreateDirectory(Output);
        var go = new GameObject("Temporary Room 3 electrical review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 65;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 200;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var lamp = new GameObject("Temporary electrical review lamp") { hideFlags = HideFlags.HideAndDontSave };
        var light = lamp.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 10;
        light.intensity = 4;
        light.shadows = LightShadows.None;
        try
        {
            foreach (var view in views)
            {
                light.enabled = view.Item1.Contains("Bank");
                if (light.enabled) lamp.transform.position = view.Item3 + Vector3.up * 2.5f + Vector3.back * 2f;
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
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Electrical capture dirtied MainScene.");
    }

    static void Build(NorthWestRoomFramework.Layout layout)
    {
        Parts parts = LoadParts();
        Transform section = NorthWestRoomFramework.BeginSectionRebuild(layout, Section.ElectricalSystems, Owner);
        float y = layout.Floor;
        // Both banks sit in the surveyed north corner pockets, away from the red terminal's north-wall column.
        BuildBank(section, "West_Transformer_Bank", new Vector3(layout.interior.min.x + 2.75f, y, layout.interior.max.z - 1.35f), parts, false);
        BuildBank(section, "East_Distribution_Bank", new Vector3(layout.interior.max.x - 2.75f, y, layout.interior.max.z - 1.35f), parts, true);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static Parts LoadParts()
    {
        EnsureFolder(Folder + "/Meshes");
        EnsureFolder(Folder + "/Materials");
        var bare = Load<Material>(Kit + "M_HeavyPipe_BareSteel.mat");
        var red = Load<Material>(Kit + "M_HeavyPipe_RedOxide.mat");
        var lightning = Load<Material>("Assets/FPS/Art/Materials/CoreBuster/M_Lightning.mat");
        var arc = Save(new Material(lightning) { name = "M_Room3_Electric_Arc" }, "Materials/M_Room3_Electric_Arc.mat");
        arc.SetColor("_Color", new Color(.28f, .78f, .80f, 1));
        arc.SetFloat("_Intensity", 7f);
        arc.SetFloat("_Flicker", .38f);
        EditorUtility.SetDirty(arc);
        return new Parts
        {
            plinth = Block("SM_Electrical_Plinth", new Vector3(3.30f, .22f, 1.65f)),
            tank = Block("SM_Transformer_Tank", new Vector3(1.16f, 1.65f, 1.05f)),
            cabinet = Block("SM_Control_Cabinet", new Vector3(.80f, 1.78f, .60f)),
            door = Block("SM_Cabinet_Door", new Vector3(.68f, 1.53f, .035f)),
            busbar = Block("SM_Copper_Busbar", new Vector3(2.72f, .10f, .12f)),
            rod = Cylinder("SM_Insulator_Stem", .62f, .065f),
            disc = Cylinder("SM_Insulator_Skirt", .065f, .20f),
            conduit = Cylinder("SM_Power_Conduit", 1.68f, .07f),
            graphite = CopyFinish("M_Electrical_Graphite", bare, new Color(.18f, .23f, .24f), .31f),
            steel = CopyFinish("M_Electrical_Steel", bare, new Color(.38f, .43f, .42f), .42f),
            oxide = CopyFinish("M_Electrical_Oxide", red, new Color(.38f, .16f, .09f), .26f),
            ceramic = CopyFinish("M_Electrical_Ceramic", bare, new Color(.57f, .59f, .47f), .04f),
            arc = arc,
            battery = Load<GameObject>(Modular + "Battery.prefab"),
        };
    }

    static void BuildBank(Transform section, string name, Vector3 world, Parts p, bool east)
    {
        Transform root = Group(section, name);
        root.position = world;
        Part(root, "Raised_Steel_Plinth", p.plinth, p.graphite, new Vector3(0, .11f, 0));
        float tankX = east ? .42f : -.42f;
        Part(root, "Transformer_Oil_Tank", p.tank, p.oxide, new Vector3(tankX, 1.08f, -.18f));
        float cabinetX = east ? -1.12f : 1.12f;
        Part(root, "Distribution_Cabinet", p.cabinet, p.graphite, new Vector3(cabinetX, 1.11f, -.25f));
        Part(root, "Cabinet_Service_Door", p.door, p.steel, new Vector3(cabinetX, 1.11f, .072f));
        for (int i = 0; i < 3; i++)
        {
            float x = tankX + (i - 1) * .35f;
            Part(root, "Insulator_Stem_" + i, p.rod, p.ceramic, new Vector3(x, 2.20f, -.18f), Quaternion.Euler(0, 0, 90));
            for (int ring = 0; ring < 3; ring++)
                Part(root, $"Insulator_Skirt_{i}_{ring}", p.disc, p.ceramic,
                    new Vector3(x, 2.04f + ring * .17f, -.18f), Quaternion.Euler(0, 0, 90));
        }
        // Elevated copper bars and conduits stay over the bank, with no player-height projection.
        for (int i = 0; i < 2; i++)
            Part(root, "Busbar_" + i, p.busbar, p.oxide, new Vector3(0, 2.62f + i * .15f, -.43f + i * .39f));
        for (int i = 0; i < 2; i++)
            Part(root, "Conduit_" + i, p.conduit, p.steel,
                new Vector3(cabinetX, 2.17f, -.35f + i * .40f), Quaternion.Euler(0, 90, 0));

        // One reused component breaks the east bank's outline without copying Centre Room pairs.
        if (east) Prefab(root, p.battery, "Battery_Service_Module", new Vector3(-1.10f, .28f, .26f), .55f);

        // Decorative contacts are deliberately above reach, script-free and collider-free.
        Arc(root, "Insulator_Arc", p.arc,
            new Vector3(tankX - .46f, 2.48f, .53f),
            new Vector3(tankX - .28f, 2.66f, .61f),
            new Vector3(tankX - .08f, 2.49f, .54f),
            new Vector3(tankX + .13f, 2.64f, .61f),
            new Vector3(tankX + .38f, 2.50f, .53f));

        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0, 1.25f, 0);
        box.size = new Vector3(3.38f, 2.50f, 1.78f);
        var obstacle = root.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = box.center;
        obstacle.size = box.size;
        obstacle.carving = true;
    }

    static void Arc(Transform parent, string name, Material material, params Vector3[] points)
    {
        var t = Group(parent, name);
        var line = t.gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.widthMultiplier = .08f;
        line.numCapVertices = 0;
        line.textureMode = LineTextureMode.Stretch;
        line.sharedMaterial = material;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
    }

    static void RequireValid(NorthWestRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        if (!NorthWestRoomFramework.Validate(scene, layout, true) ||
            !NorthWestRoomFramework.ValidateSection(scene, layout, Section.ElectricalSystems, true))
            throw new InvalidOperationException("North-West Room framework or electrical section validation failed.");
        var section = NorthWestRoomFramework.FindSection(scene, Section.ElectricalSystems);
        var roots = section.Cast<Transform>().ToArray();
        if (roots.Length != 2 || roots.Any(r => !r.GetComponent<NavMeshObstacle>() || !r.GetComponent<NavMeshObstacle>().carving ||
            r.GetComponentsInChildren<Collider>(true).Count(c => c.enabled && !c.isTrigger) != 1))
            throw new InvalidOperationException("Expected two electrical banks with one consolidated collider and carving obstacle each.");
        int prefabs = section.GetComponentsInChildren<Transform>(true).Count(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject));
        if (prefabs > 40) throw new InvalidOperationException($"Electrical section uses {prefabs}/40 prefab instances.");
        if (section.GetComponentsInChildren<LineRenderer>(true).Length != 2 ||
            section.GetComponentsInChildren<LineRenderer>(true).Any(a => a.GetComponent<Collider>() || a.GetComponents<MonoBehaviour>().Length != 0))
            throw new InvalidOperationException("Electrical arcs must be two visual-only lines.");
        Debug.Log($"NORTH-WEST ELECTRICAL CHECKS PASSED: two banks, {prefabs}/40 prefabs, two visual-only arcs.");
    }

    static void VerifyIsolatedUndo(NorthWestRoomFramework.Layout layout)
    {
        var section = NorthWestRoomFramework.FindSection(SceneManager.GetActiveScene(), Section.ElectricalSystems);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        NorthWestRoomFramework.BeginSectionRebuild(layout, Section.ElectricalSystems, Owner);
        Undo.CollapseUndoOperations(group);
        if (section.childCount != 0) throw new InvalidOperationException("Electrical section clear left content.");
        Undo.PerformUndo();
        if (section.childCount != 2) throw new InvalidOperationException("Electrical section Undo did not restore both banks.");
    }

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build North-West Room electrical systems");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void Part(Transform parent, string name, Mesh mesh, Material material, Vector3 pos, Quaternion rotation = default)
    {
        var t = Group(parent, name);
        t.localPosition = pos;
        t.localRotation = rotation == default ? Quaternion.identity : rotation;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    static void Prefab(Transform parent, GameObject prefab, string name, Vector3 pos, float scale)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(go, "Add Room 3 modular electrical component");
        go.name = name;
        go.transform.localPosition = pos;
        go.transform.localScale *= scale;
        foreach (var collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    static Mesh Block(string name, Vector3 size)
    {
        var b = new ProceduralMeshBuilder(); b.AddBlock(Vector3.zero, size);
        return Save(b.ToMesh(name), "Meshes/" + name + ".asset");
    }

    static Mesh Cylinder(string name, float length, float radius)
    {
        var b = new ProceduralMeshBuilder();
        b.AddCylinder(Vector3.left * length * .5f, Vector3.right * length * .5f, radius, radius, 16, true);
        return Save(b.ToMesh(name), "Meshes/" + name + ".asset");
    }

    static Material CopyFinish(string name, Material source, Color color, float metallic)
    {
        var m = new Material(source) { name = name };
        m.SetColor("_BaseColor", color); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", .38f);
        return Save(m, "Materials/" + name + ".mat");
    }

    static T Save<T>(T generated, string relative) where T : Object
    {
        string path = Folder + "/" + relative;
        generated.name = Path.GetFileNameWithoutExtension(relative);
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing) { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
        else { AssetDatabase.CreateAsset(generated, path); existing = generated; }
        NorthWestRoomFramework.RequireOwnedAsset(existing, Section.ElectricalSystems);
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
        throw new InvalidOperationException("Missing electrical dependency: " + path);

    static Dictionary<string, string> SnapshotOutside()
    {
        var scene = SceneManager.GetActiveScene();
        var section = NorthWestRoomFramework.FindSection(scene, Section.ElectricalSystems);
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

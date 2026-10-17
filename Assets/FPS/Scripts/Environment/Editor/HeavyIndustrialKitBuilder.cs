using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Rebuilds the first-principles heavy-pipe and P-3 pressure-tank kit used by the Start Workshop.
/// Meshes use metres, repeat their UVs approximately once per metre, and keep pipe input ports at
/// the prefab origin. Generated assets are committed; this builder is their reproducible source.
/// </summary>
public static class HeavyIndustrialKitBuilder
{
    const string Root = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit";
    const string MeshFolder = Root + "/Meshes";
    const string MaterialFolder = Root + "/Materials";
    const string TextureFolder = Root + "/Textures";
    const string PrefabFolder = Root + "/Prefabs";
    const string TestScenePath = Root + "/HeavyIndustrialKit_AssemblyTest.unity";
    const string ReviewPath = "ReviewCaptures/HeavyIndustrialKit/Assembly.png";

    const int RadialSegments = 16;
    const float PipeRadius = .24f;
    const float ElbowRadius = .65f;

    static readonly string[] k_PrefabNames =
    {
        "P_HeavyPipe_Straight_1m", "P_HeavyPipe_Straight_2m", "P_HeavyPipe_Elbow_90",
        "P_HeavyPipe_Tee", "P_HeavyPipe_Reducer", "P_HeavyPipe_Flange",
        "P_HeavyPipe_WallBracket", "P_HeavyPipe_ValveMount", "P_PressureTank_P3",
    };

    struct PortSpec
    {
        public string Name;
        public Vector3 Position;
        public Quaternion Rotation;

        public PortSpec(string name, Vector3 position, Quaternion rotation)
        {
            Name = name;
            Position = position;
            Rotation = rotation;
        }
    }

    [MenuItem("Tools/CoreBuster/Build Heavy Industrial Kit")]
    public static void BuildAll()
    {
        EnsureFolder(MeshFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(TextureFolder);
        EnsureFolder(PrefabFolder);

        BuildTextures();
        Material red = BuildMaterial("RedOxide", .22f, .24f);
        Material yellow = BuildMaterial("HazardYellow", .18f, .20f);
        Material steel = BuildMaterial("BareSteel", .9f, .42f);

        var meshes = BuildMeshes();
        BuildPrefabs(meshes, red, yellow, steel);
        AssetDatabase.SaveAssets();
        BuildAssemblyScene(steel);
        AssetDatabase.SaveAssets();

        if (!Validate(false))
            throw new InvalidOperationException("Heavy Industrial Kit validation failed. See the Console for details.");

        Debug.Log("HEAVY INDUSTRIAL KIT BUILT: 9 prefabs, 11 mesh assets, 3 URP/Lit materials, " +
                  "6 procedural textures and one port-snapped assembly-test scene under " + Root);
    }

    public static void BuildBatch()
    {
        try
        {
            BuildAll();
            CaptureAssemblyReview();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/CoreBuster/Validate Heavy Industrial Kit")]
    public static void ValidateMenu()
    {
        Validate(true);
    }

    [MenuItem("Tools/CoreBuster/Capture Heavy Industrial Kit Review")]
    public static void CaptureAssemblyReview()
    {
        if (!File.Exists(TestScenePath))
            throw new FileNotFoundException("Build the Heavy Industrial Kit first.", TestScenePath);

        Scene scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
        Camera camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true))
            .FirstOrDefault();
        if (!camera) throw new InvalidOperationException("Assembly test scene has no camera.");

        const int width = 1280;
        const int height = 720;
        RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        target.antiAliasing = 4;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            string absolute = Path.GetFullPath(ReviewPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllBytes(absolute, texture.EncodeToPNG());
            Debug.Log("HEAVY INDUSTRIAL KIT REVIEW CAPTURED: " + absolute);
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    static Dictionary<string, Mesh> BuildMeshes()
    {
        var meshes = new Dictionary<string, Mesh>();

        meshes["Straight1m"] = SaveMesh(CylinderMesh("SM_HeavyPipe_Straight_1m", 1f, PipeRadius),
            MeshFolder + "/SM_HeavyPipe_Straight_1m.asset");
        meshes["Straight2m"] = SaveMesh(CylinderMesh("SM_HeavyPipe_Straight_2m", 2f, PipeRadius),
            MeshFolder + "/SM_HeavyPipe_Straight_2m.asset");
        meshes["Elbow"] = SaveMesh(ElbowMesh(), MeshFolder + "/SM_HeavyPipe_Elbow_90.asset");
        meshes["Tee"] = SaveMesh(TeeMesh(), MeshFolder + "/SM_HeavyPipe_Tee.asset");
        meshes["Reducer"] = SaveMesh(ReducerMesh(), MeshFolder + "/SM_HeavyPipe_Reducer.asset");
        meshes["Flange"] = SaveMesh(FlangeMesh(), MeshFolder + "/SM_HeavyPipe_Flange.asset");
        meshes["Bracket"] = SaveMesh(BracketMesh(), MeshFolder + "/SM_HeavyPipe_WallBracket.asset");
        meshes["Valve"] = SaveMesh(ValveMountMesh(), MeshFolder + "/SM_HeavyPipe_ValveMount.asset");
        meshes["TankBody"] = SaveMesh(TankBodyMesh(), MeshFolder + "/SM_PressureTank_P3_Body.asset");
        meshes["TankBands"] = SaveMesh(TankBandsMesh(), MeshFolder + "/SM_PressureTank_P3_Bands.asset");
        meshes["TankLabel"] = SaveMesh(TankLabelMesh(), MeshFolder + "/SM_PressureTank_P3_Label.asset");
        return meshes;
    }

    static Mesh CylinderMesh(string name, float length, float radius)
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddCylinder(Vector3.zero, Vector3.forward * length, radius, radius, RadialSegments, true);
        return builder.ToMesh(name);
    }

    static Mesh ElbowMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddElbow(ElbowRadius, PipeRadius, RadialSegments, 8);
        return builder.ToMesh("SM_HeavyPipe_Elbow_90");
    }

    static Mesh TeeMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddCylinder(Vector3.zero, Vector3.forward * 1.2f, PipeRadius, PipeRadius, RadialSegments, true);
        builder.AddCylinder(new Vector3(0f, 0f, .6f), new Vector3(.72f, 0f, .6f),
            PipeRadius, PipeRadius, RadialSegments, true);
        builder.AddTorus(new Vector3(0f, 0f, .6f), Vector3.forward, .29f, .045f, 16, 6);
        return builder.ToMesh("SM_HeavyPipe_Tee");
    }

    static Mesh ReducerMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddCylinder(Vector3.zero, Vector3.forward * .65f, PipeRadius, .14f, RadialSegments, true);
        return builder.ToMesh("SM_HeavyPipe_Reducer");
    }

    static Mesh FlangeMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddAnnularCylinderZ(.14f, .36f, PipeRadius * .98f, RadialSegments);
        return builder.ToMesh("SM_HeavyPipe_Flange");
    }

    static Mesh BracketMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddBox(new Vector3(0f, 0f, .04f), new Vector3(.82f, .82f, .08f));
        builder.AddBox(new Vector3(-.31f, 0f, .22f), new Vector3(.10f, .56f, .36f));
        builder.AddBox(new Vector3(.31f, 0f, .22f), new Vector3(.10f, .56f, .36f));
        builder.AddTorus(new Vector3(0f, 0f, .38f), Vector3.forward, .29f, .045f, 16, 6);
        return builder.ToMesh("SM_HeavyPipe_WallBracket");
    }

    static Mesh ValveMountMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddCylinder(Vector3.zero, Vector3.forward * .7f, PipeRadius, PipeRadius, RadialSegments, true);
        builder.AddCylinder(new Vector3(0f, 0f, .35f), new Vector3(.46f, 0f, .35f),
            .09f, .09f, 12, true);
        builder.AddCylinder(new Vector3(.42f, 0f, .35f), new Vector3(.52f, 0f, .35f),
            .15f, .15f, 12, true);
        Vector3 wheelCenter = new Vector3(.54f, 0f, .35f);
        builder.AddTorus(wheelCenter, Vector3.right, .34f, .045f, 20, 6);
        builder.AddBox(wheelCenter, new Vector3(.08f, .06f, .63f));
        builder.AddBox(wheelCenter, new Vector3(.08f, .63f, .06f));
        return builder.ToMesh("SM_HeavyPipe_ValveMount");
    }

    static Mesh TankBodyMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddLatheY(new[]
        {
            new Vector2(.28f, .12f), new Vector2(.44f, .20f), new Vector2(.56f, .34f),
            new Vector2(.60f, .52f), new Vector2(.60f, 2.48f), new Vector2(.56f, 2.66f),
            new Vector2(.44f, 2.80f), new Vector2(.28f, 2.88f),
        }, 24, true);
        return builder.ToMesh("SM_PressureTank_P3_Body");
    }

    static Mesh TankBandsMesh()
    {
        var builder = new ProceduralMeshBuilder();
        foreach (float y in new[] { .58f, 1.5f, 2.42f })
            builder.AddTorus(new Vector3(0f, y, 0f), Vector3.up, .61f, .055f, 24, 6);
        builder.AddCylinder(new Vector3(-.34f, 0f, -.18f), new Vector3(-.34f, .25f, -.18f),
            .09f, .09f, 10, true);
        builder.AddCylinder(new Vector3(.34f, 0f, -.18f), new Vector3(.34f, .25f, -.18f),
            .09f, .09f, 10, true);
        builder.AddCylinder(new Vector3(-.34f, 0f, .18f), new Vector3(-.34f, .25f, .18f),
            .09f, .09f, 10, true);
        builder.AddCylinder(new Vector3(.34f, 0f, .18f), new Vector3(.34f, .25f, .18f),
            .09f, .09f, 10, true);
        return builder.ToMesh("SM_PressureTank_P3_Bands");
    }

    static Mesh TankLabelMesh()
    {
        var builder = new ProceduralMeshBuilder();
        const float z = .606f;
        const float depth = .035f;
        const float stroke = .075f;
        const float height = .50f;
        float y = 1.55f;

        // Block-built P-3 lettering remains geometry, so the identifier needs no font dependency.
        AddGlyphBox(builder, -.34f, y, z, stroke, height, depth);
        AddGlyphBox(builder, -.20f, y + .21f, z, .25f, stroke, depth);
        AddGlyphBox(builder, -.20f, y, z, .25f, stroke, depth);
        AddGlyphBox(builder, -.07f, y + .105f, z, stroke, .25f, depth);
        AddGlyphBox(builder, .09f, y, z, .16f, stroke, depth);
        foreach (float offset in new[] { .21f, 0f, -.21f })
            AddGlyphBox(builder, .35f, y + offset, z, .28f, stroke, depth);
        AddGlyphBox(builder, .49f, y + .105f, z, stroke, .25f, depth);
        AddGlyphBox(builder, .49f, y - .105f, z, stroke, .25f, depth);
        return builder.ToMesh("SM_PressureTank_P3_Label");
    }

    static void AddGlyphBox(ProceduralMeshBuilder builder, float x, float y, float z,
        float width, float height, float depth)
    {
        builder.AddBox(new Vector3(x, y, z), new Vector3(width, height, depth));
    }

    static void BuildPrefabs(Dictionary<string, Mesh> meshes, Material red, Material yellow, Material steel)
    {
        SavePipePrefab("P_HeavyPipe_Straight_1m", meshes["Straight1m"], red,
            new[] { Port("Port_A", Vector3.zero, 0f), Port("Port_B", Vector3.forward, 0f) },
            root => AddBoxCollider(root, "Collision", new Vector3(0f, 0f, .5f),
                new Vector3(PipeRadius * 2f, PipeRadius * 2f, 1f), Quaternion.identity));

        SavePipePrefab("P_HeavyPipe_Straight_2m", meshes["Straight2m"], yellow,
            new[] { Port("Port_A", Vector3.zero, 0f), Port("Port_B", Vector3.forward * 2f, 0f) },
            root => AddBoxCollider(root, "Collision", new Vector3(0f, 0f, 1f),
                new Vector3(PipeRadius * 2f, PipeRadius * 2f, 2f), Quaternion.identity));

        SavePipePrefab("P_HeavyPipe_Elbow_90", meshes["Elbow"], red,
            new[]
            {
                Port("Port_A", Vector3.zero, 0f),
                Port("Port_B", new Vector3(ElbowRadius, 0f, ElbowRadius), 90f),
            }, root =>
            {
                AddBoxCollider(root, "Collision_In", new Vector3(0f, 0f, ElbowRadius * .5f),
                    new Vector3(PipeRadius * 2f, PipeRadius * 2f, ElbowRadius), Quaternion.identity);
                AddBoxCollider(root, "Collision_Out", new Vector3(ElbowRadius * .5f, 0f, ElbowRadius),
                    new Vector3(PipeRadius * 2f, PipeRadius * 2f, ElbowRadius), Quaternion.Euler(0f, 90f, 0f));
            });

        SavePipePrefab("P_HeavyPipe_Tee", meshes["Tee"], red,
            new[]
            {
                Port("Port_A", Vector3.zero, 0f), Port("Port_B", Vector3.forward * 1.2f, 0f),
                Port("Port_C", new Vector3(.72f, 0f, .6f), 90f),
            }, root =>
            {
                AddBoxCollider(root, "Collision_Main", new Vector3(0f, 0f, .6f),
                    new Vector3(PipeRadius * 2f, PipeRadius * 2f, 1.2f), Quaternion.identity);
                AddBoxCollider(root, "Collision_Branch", new Vector3(.36f, 0f, .6f),
                    new Vector3(PipeRadius * 2f, PipeRadius * 2f, .72f), Quaternion.Euler(0f, 90f, 0f));
            });

        SavePipePrefab("P_HeavyPipe_Reducer", meshes["Reducer"], yellow,
            new[] { Port("Port_A", Vector3.zero, 0f), Port("Port_B", Vector3.forward * .65f, 0f) },
            root => AddBoxCollider(root, "Collision", new Vector3(0f, 0f, .325f),
                new Vector3(PipeRadius * 2f, PipeRadius * 2f, .65f), Quaternion.identity));

        SavePipePrefab("P_HeavyPipe_Flange", meshes["Flange"], steel,
            new[] { Port("Port_A", Vector3.zero, 0f), Port("Port_B", Vector3.forward * .14f, 0f) },
            root => AddBoxCollider(root, "Collision", new Vector3(0f, 0f, .07f),
                new Vector3(.72f, .72f, .14f), Quaternion.identity));

        SavePipePrefab("P_HeavyPipe_WallBracket", meshes["Bracket"], yellow,
            Array.Empty<PortSpec>(),
            root => AddBoxCollider(root, "Collision", new Vector3(0f, 0f, .2f),
                new Vector3(.82f, .82f, .4f), Quaternion.identity));

        SavePipePrefab("P_HeavyPipe_ValveMount", meshes["Valve"], red,
            new[] { Port("Port_A", Vector3.zero, 0f), Port("Port_B", Vector3.forward * .7f, 0f) },
            root =>
            {
                AddBoxCollider(root, "Collision_Pipe", new Vector3(0f, 0f, .35f),
                    new Vector3(PipeRadius * 2f, PipeRadius * 2f, .7f), Quaternion.identity);
                AddBoxCollider(root, "Collision_Wheel", new Vector3(.52f, 0f, .35f),
                    new Vector3(.16f, .76f, .76f), Quaternion.identity);
            });

        BuildTankPrefab(meshes, yellow, steel, red);
    }

    static PortSpec Port(string name, Vector3 position, float yaw)
    {
        return new PortSpec(name, position, Quaternion.Euler(0f, yaw, 0f));
    }

    static void SavePipePrefab(string name, Mesh mesh, Material material, PortSpec[] ports,
        Action<GameObject> addColliders)
    {
        var root = new GameObject(name);
        try
        {
            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            foreach (PortSpec port in ports) AddPort(root.transform, port);
            addColliders(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + name + ".prefab");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void BuildTankPrefab(Dictionary<string, Mesh> meshes, Material yellow, Material steel, Material red)
    {
        const string name = "P_PressureTank_P3";
        var root = new GameObject(name);
        try
        {
            AddVisual(root.transform, "Tank_Body", meshes["TankBody"], yellow);
            AddVisual(root.transform, "Tank_Bands_And_Feet", meshes["TankBands"], steel);
            AddVisual(root.transform, "P3_ID", meshes["TankLabel"], red);
            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.center = new Vector3(0f, 1.5f, 0f);
            collider.radius = .62f;
            collider.height = 3f;
            AddPort(root.transform, new PortSpec("Service_Port", new Vector3(0f, 2.25f, .6f),
                Quaternion.identity));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + name + ".prefab");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void AddVisual(Transform parent, string name, Mesh mesh, Material material)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        child.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    static void AddPort(Transform root, PortSpec port)
    {
        var child = new GameObject(port.Name).transform;
        child.SetParent(root, false);
        child.localPosition = port.Position;
        child.localRotation = port.Rotation;
    }

    static void AddBoxCollider(GameObject root, string name, Vector3 center, Vector3 size, Quaternion rotation)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        child.transform.localPosition = center;
        child.transform.localRotation = rotation;
        var collider = child.AddComponent<BoxCollider>();
        collider.size = size;
    }

    static void BuildAssemblyScene(Material floorMaterial)
    {
        Scene previous = SceneManager.GetActiveScene();
        bool useEmptyStartupScene = string.IsNullOrEmpty(previous.path);
        if (useEmptyStartupScene && previous.isDirty)
            throw new InvalidOperationException(
                "Save or discard the modified untitled scene before building the Heavy Industrial Kit.");

        // Unity refuses additive Editor scenes beside its unsaved, untitled batch-mode startup scene.
        // Reuse that scene only when it is clean; with a real project scene open, keep the generated
        // test scene additive so the artist's active scene is never closed or modified.
        Scene scene = useEmptyStartupScene
            ? previous
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            if (useEmptyStartupScene)
            {
                foreach (GameObject existingRoot in scene.GetRootGameObjects())
                    UnityEngine.Object.DestroyImmediate(existingRoot);
            }
            var assembly = new GameObject("Port_Snapped_Assembly");
            GameObject straight2 = AddPrefab("P_HeavyPipe_Straight_2m", assembly.transform);
            straight2.transform.SetPositionAndRotation(new Vector3(-2.2f, 1.15f, -1f), Quaternion.identity);
            GameObject elbow = AddSnapped("P_HeavyPipe_Elbow_90", straight2.transform.Find("Port_B"), assembly.transform);
            GameObject straight1 = AddSnapped("P_HeavyPipe_Straight_1m", elbow.transform.Find("Port_B"), assembly.transform);
            GameObject tee = AddSnapped("P_HeavyPipe_Tee", straight1.transform.Find("Port_B"), assembly.transform);
            GameObject reducer = AddSnapped("P_HeavyPipe_Reducer", tee.transform.Find("Port_B"), assembly.transform);
            GameObject valve = AddSnapped("P_HeavyPipe_ValveMount", reducer.transform.Find("Port_B"), assembly.transform);
            AddSnapped("P_HeavyPipe_Flange", valve.transform.Find("Port_B"), assembly.transform);

            GameObject branch = AddSnapped("P_HeavyPipe_Straight_1m", tee.transform.Find("Port_C"), assembly.transform);
            branch.GetComponent<MeshRenderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_HeavyPipe_HazardYellow.mat");

            GameObject bracket = AddPrefab("P_HeavyPipe_WallBracket", assembly.transform);
            bracket.transform.SetPositionAndRotation(new Vector3(-2.2f, 1.15f, .15f), Quaternion.identity);

            GameObject tank = AddPrefab("P_PressureTank_P3", null);
            tank.transform.position = new Vector3(2.3f, 0f, 1.3f);
            tank.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Review_Floor";
            floor.transform.position = new Vector3(0f, -.08f, 1.2f);
            floor.transform.localScale = new Vector3(8f, .15f, 7f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;

            var key = new GameObject("Key_Light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.6f;
            key.color = Color.white;
            key.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            key.shadows = LightShadows.Soft;

            var fill = new GameObject("Fill_Light").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 10f;
            fill.intensity = 3f;
            fill.color = Color.white;
            fill.transform.position = new Vector3(-2f, 4f, -2f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.24f, .24f, .24f);
            RenderSettings.reflectionIntensity = 1f;

            var camera = new GameObject("Review_Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(7.4f, 4.8f, -7.2f);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(.1f, 1.35f, 1.4f) - camera.transform.position);
            camera.fieldOfView = 42f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .035f, .045f);
            camera.allowHDR = true;
            camera.allowMSAA = true;
            var cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.renderShadows = true;
            cameraData.volumeLayerMask = ~0;

            EditorSceneManager.SaveScene(scene, TestScenePath);
        }
        finally
        {
            if (!useEmptyStartupScene)
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    static GameObject AddPrefab(string name, Transform parent)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + name + ".prefab");
        if (!asset) throw new FileNotFoundException("Missing generated prefab " + name);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        if (parent) instance.transform.SetParent(parent, true);
        return instance;
    }

    static GameObject AddSnapped(string name, Transform upstreamPort, Transform parent)
    {
        if (!upstreamPort) throw new InvalidOperationException("Cannot snap " + name + ": upstream port is missing.");
        GameObject instance = AddPrefab(name, parent);
        Transform input = instance.transform.Find("Port_A");
        if (!input) throw new InvalidOperationException(name + " has no Port_A.");
        Quaternion rotation = upstreamPort.rotation * Quaternion.Inverse(input.localRotation);
        instance.transform.SetPositionAndRotation(upstreamPort.position - rotation * input.localPosition, rotation);
        return instance;
    }

    static bool Validate(bool logSuccess)
    {
        int errors = 0;
        foreach (string name in k_PrefabNames)
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab)
            {
                Debug.LogError("Heavy Industrial Kit: missing prefab " + path);
                errors++;
                continue;
            }

            int triangles = prefab.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh)
                .Sum(filter => filter.sharedMesh.triangles.Length / 3);
            if (triangles > 2000)
            {
                Debug.LogError($"Heavy Industrial Kit: {name} has {triangles} triangles (limit 2000).");
                errors++;
            }
            if (prefab.GetComponentsInChildren<Collider>(true).Length == 0)
            {
                Debug.LogError("Heavy Industrial Kit: " + name + " has no collider.");
                errors++;
            }
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.sharedMaterial || !renderer.sharedMaterial.shader ||
                    renderer.sharedMaterial.shader.name != "Universal Render Pipeline/Lit")
                {
                    Debug.LogError("Heavy Industrial Kit: " + name + " does not use URP/Lit throughout.");
                    errors++;
                    break;
                }
            }

            bool isPipe = name != "P_HeavyPipe_WallBracket" && name != "P_PressureTank_P3";
            if (isPipe && (!prefab.transform.Find("Port_A") || !prefab.transform.Find("Port_B")))
            {
                Debug.LogError("Heavy Industrial Kit: " + name + " is missing Port_A or Port_B.");
                errors++;
            }

            Debug.Log($"Heavy Industrial Kit: {name} = {triangles} triangles.");
        }

        foreach (string materialName in new[] { "RedOxide", "HazardYellow", "BareSteel" })
        {
            string materialPath = MaterialFolder + "/M_HeavyPipe_" + materialName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material || !material.GetTexture("_BaseMap") || !material.GetTexture("_BumpMap"))
            {
                Debug.LogError("Heavy Industrial Kit: incomplete material " + materialPath);
                errors++;
            }
        }

        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath))
        {
            Debug.LogError("Heavy Industrial Kit: missing assembly-test scene " + TestScenePath);
            errors++;
        }

        if (errors == 0 && logSuccess)
            Debug.Log("HEAVY INDUSTRIAL KIT VALIDATION PASSED: all prefabs have snap ports, simple colliders, " +
                      "URP/Lit materials and no more than 2,000 triangles.");
        return errors == 0;
    }

    static void BuildTextures()
    {
        WriteTextureSet("RedOxide", new Color32(118, 27, 17, 255), new Color32(64, 69, 70, 255), 17);
        WriteTextureSet("HazardYellow", new Color32(226, 132, 18, 255), new Color32(74, 75, 66, 255), 43);
        WriteTextureSet("BareSteel", new Color32(82, 88, 90, 255), new Color32(38, 42, 44, 255), 79);
    }

    static void WriteTextureSet(string name, Color32 baseColor, Color32 exposedColor, int seed)
    {
        const int size = 512;
        var albedoPixels = new Color32[size * size];
        var height = new float[size * size];
        bool painted = name != "BareSteel";
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = x + y * size;
                float broad = Mathf.PerlinNoise((x + seed * 11) * .018f, (y - seed * 7) * .018f);
                float fine = Hash01(x, y, seed);
                float scratch = Mathf.Abs(Mathf.Sin((x * .43f + y * .09f + seed) * .37f));
                bool chipped = painted && ((broad < .32f && fine > .73f) ||
                    (scratch < .035f && Hash01(x / 3, y / 5, seed + 13) > .74f));
                Color32 source = chipped ? exposedColor : baseColor;
                float value = .72f + broad * .34f + (fine - .5f) * .08f;
                if (chipped) value *= .72f + fine * .18f;
                albedoPixels[index] = Multiply(source, value);
                height[index] = broad * .20f + fine * .035f + (chipped ? -.18f : 0f);
            }
        }

        var normalPixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float left = height[((x - 1 + size) % size) + y * size];
                float right = height[((x + 1) % size) + y * size];
                float down = height[x + ((y - 1 + size) % size) * size];
                float up = height[x + ((y + 1) % size) * size];
                Vector3 normal = new Vector3((left - right) * 5f, (down - up) * 5f, 1f).normalized;
                normalPixels[x + y * size] = new Color32(
                    (byte)Mathf.RoundToInt((normal.x * .5f + .5f) * 255f),
                    (byte)Mathf.RoundToInt((normal.y * .5f + .5f) * 255f),
                    (byte)Mathf.RoundToInt((normal.z * .5f + .5f) * 255f), 255);
            }
        }

        string prefix = TextureFolder + "/T_HeavyPipe_" + name;
        WritePng(prefix + "_Albedo.png", albedoPixels, size, false);
        WritePng(prefix + "_Normal.png", normalPixels, size, true);
    }

    static Color32 Multiply(Color32 color, float multiplier)
    {
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * multiplier), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * multiplier), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * multiplier), 0, 255), color.a);
    }

    static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            uint value = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            value = (value ^ (value >> 13)) * 1274126177u;
            return (value ^ (value >> 16)) / (float)uint.MaxValue;
        }
    }

    static void WritePng(string assetPath, Color32[] pixels, int size, bool normalMap)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, normalMap);
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        File.WriteAllBytes(Path.GetFullPath(assetPath), texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.sRGBTexture = !normalMap;
        importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.SaveAndReimport();
    }

    static Material BuildMaterial(string name, float metallic, float smoothness)
    {
        string path = MaterialFolder + "/M_HeavyPipe_" + name + ".mat";
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new InvalidOperationException("Universal Render Pipeline/Lit shader is unavailable.");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader) { name = "M_HeavyPipe_" + name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/T_HeavyPipe_" + name + "_Albedo.png");
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/T_HeavyPipe_" + name + "_Normal.png");
        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_MainTex", albedo);
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_BumpScale", 1f);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(material);
        return material;
    }

    static Mesh SaveMesh(Mesh generated, string path)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing)
        {
            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(generated, path);
        return generated;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}

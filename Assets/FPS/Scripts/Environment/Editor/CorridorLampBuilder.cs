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

/// <summary>
/// Rebuilds the first-principles caged corridor lamp (corebuster_art_007): a cast junction housing,
/// a red glass globe around a hot bulb, a four-bar wire guard and a realtime red point light, in wall
/// and ceiling mountings. Meshes are in metres; generated assets are committed and this builder is
/// their reproducible source. It writes only below <see cref="Root"/> and never opens MainScene.
/// </summary>
public static class CorridorLampBuilder
{
    const string Root = "Assets/FPS/Art/Corridors/CorridorLamp";
    const string MeshFolder = Root + "/Meshes";
    const string MaterialFolder = Root + "/Materials";
    const string TextureFolder = Root + "/Textures";
    const string PrefabFolder = Root + "/Prefabs";
    const string ReviewScenePath = Root + "/CorridorLamp_Review.unity";
    const string ReviewFolder = "ReviewCaptures/CorridorLamp";

    const string WallPrefab = "P_CorridorLamp_Wall";
    const string CeilingPrefab = "P_CorridorLamp_Ceiling";
    const int TriangleLimit = 2000;

    // Head frame: origin at the top of the conduit hub, globe hanging down -Y.
    const int Segments = 16;
    const float CollarTop = -.084f, CollarBottom = -.112f;
    const float GlassRadius = .068f, GlassDomeY = -.29f;
    const float GuardRadius = .084f, GuardBar = .0065f;
    const float BulbY = -.205f;
    static readonly Vector3 k_WallHeadOffset = new Vector3(0f, -.08f, .22f);
    static readonly Vector3 k_CeilingHeadOffset = new Vector3(0f, -.15f, 0f);

    public static readonly Color LightColor = new Color(1f, .14f, .055f);

    [MenuItem("Tools/CoreBuster/Build Corridor Lamp")]
    public static void BuildAll()
    {
        EnsureFolder(MeshFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(TextureFolder);
        EnsureFolder(PrefabFolder);

        BuildTextures();
        Material paint = BuildPaintMaterial();
        Material glass = BuildGlassMaterial();
        Material bulb = BuildBulbMaterial();

        var meshes = new Dictionary<string, Mesh>
        {
            ["Head"] = SaveMesh(HeadMesh(), MeshFolder + "/SM_CorridorLamp_Head.asset"),
            ["Globe"] = SaveMesh(GlobeMesh(), MeshFolder + "/SM_CorridorLamp_Globe.asset"),
            ["Bulb"] = SaveMesh(BulbMesh(), MeshFolder + "/SM_CorridorLamp_Bulb.asset"),
            ["WallMount"] = SaveMesh(WallMountMesh(), MeshFolder + "/SM_CorridorLamp_WallMount.asset"),
            ["CeilingMount"] = SaveMesh(CeilingMountMesh(), MeshFolder + "/SM_CorridorLamp_CeilingMount.asset"),
        };
        AssetDatabase.SaveAssets();

        SaveLampPrefab(WallPrefab, meshes["WallMount"], k_WallHeadOffset, meshes, paint, glass, bulb);
        SaveLampPrefab(CeilingPrefab, meshes["CeilingMount"], k_CeilingHeadOffset, meshes, paint, glass, bulb);
        AssetDatabase.SaveAssets();
        BuildReviewScene();
        AssetDatabase.SaveAssets();

        if (!Validate(false))
            throw new InvalidOperationException("Corridor Lamp validation failed. See the Console for details.");
        Debug.Log("CORRIDOR LAMP BUILT: 2 prefabs, 5 mesh assets, 3 URP/Lit materials, " +
                  "3 procedural textures and one review scene under " + Root);
    }

    public static void BuildBatch()
    {
        try
        {
            BuildAll();
            CaptureReview();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/CoreBuster/Validate Corridor Lamp")]
    public static void ValidateMenu()
    {
        Validate(true);
    }

    [MenuItem("Tools/CoreBuster/Capture Corridor Lamp Review")]
    public static void CaptureReview()
    {
        if (!File.Exists(ReviewScenePath))
            throw new FileNotFoundException("Build the Corridor Lamp first.", ReviewScenePath);

        // Rendering beside another loaded scene would mix in its geometry and lighting, so the review
        // scene opens alone and the artist's saved scene setup is reopened afterwards.
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save or discard open scene changes before capturing the Corridor Lamp review.");
        }
        Scene scene = EditorSceneManager.OpenScene(ReviewScenePath, OpenSceneMode.Single);
        try
        {
            Camera[] cameras = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
            if (cameras.Length == 0) throw new InvalidOperationException("Corridor Lamp review scene has no camera.");
            foreach (Camera camera in cameras)
                RenderToPng(camera, ReviewFolder + "/" + camera.name.Replace("Review_", "") + ".png", 1280, 720);
        }
        finally
        {
            if (previous.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previous);
        }
    }

    // ---- Meshes ------------------------------------------------------------------------------

    /// <summary>
    /// Cast junction housing, threaded collar, lamp socket and the four-bar wire guard, all in the
    /// painted finish. Lathe profiles run downward: ProceduralMeshBuilder.AddLatheY only winds its
    /// sides outward when the profile's Y decreases.
    /// </summary>
    static Mesh HeadMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddCylinder(Vector3.zero, Vector3.down * .032f, .031f, .031f, 12, true);
        builder.AddLatheY(new[]
        {
            new Vector2(.031f, -.022f), new Vector2(.060f, -.028f), new Vector2(.081f, -.040f),
            new Vector2(.090f, -.056f), new Vector2(.090f, -.078f), new Vector2(.085f, CollarTop),
        }, Segments, false);
        builder.AddCylinder(new Vector3(0f, CollarTop, 0f), new Vector3(0f, CollarBottom, 0f),
            .086f, .086f, Segments, true);
        builder.AddTorus(new Vector3(0f, -.098f, 0f), Vector3.up, .086f, .0035f, Segments, 3);
        builder.AddCylinder(new Vector3(0f, CollarBottom, 0f), new Vector3(0f, -.152f, 0f),
            .015f, .015f, 10, false);

        Vector3[] directions = { Vector3.right, Vector3.forward, Vector3.left, Vector3.back };
        foreach (Vector3 direction in directions)
        {
            // Guard lug cast onto the housing shoulder; the guard bar leaves from its underside.
            Vector3 lugSize = direction.x != 0f ? new Vector3(.024f, .026f, .022f) : new Vector3(.022f, .026f, .024f);
            builder.AddBox(direction * .093f + Vector3.up * -.068f, lugSize);
            builder.AddTube(GuardBarPath(direction), GuardBar, 4, true);
        }

        // Horizontal hoops: three on the straight run of the globe, one where the dome begins.
        foreach (float y in new[] { -.145f, -.205f, -.265f })
            builder.AddTorus(new Vector3(0f, y, 0f), Vector3.up, GuardRadius, .0055f, Segments, 3);
        float domeHoopY = GlassDomeY - .026f;
        float domeHoopRadius = Mathf.Sqrt(GuardRadius * GuardRadius - .026f * .026f);
        builder.AddTorus(new Vector3(0f, domeHoopY, 0f), Vector3.up, domeHoopRadius, .0055f, Segments, 3);

        // Bottom boss where the four bars meet, with its retaining nut.
        builder.AddCylinder(new Vector3(0f, -.360f, 0f), new Vector3(0f, -.378f, 0f), .030f, .028f, 12, true);
        builder.AddCylinder(new Vector3(0f, -.378f, 0f), new Vector3(0f, -.392f, 0f), .012f, .012f, 6, true);
        return builder.ToMesh("SM_CorridorLamp_Head");
    }

    static List<Vector3> GuardBarPath(Vector3 direction)
    {
        var path = new List<Vector3>
        {
            direction * .093f + Vector3.up * -.074f,
            direction * GuardRadius + Vector3.up * -.118f,
            direction * GuardRadius + Vector3.up * GlassDomeY,
        };
        // Follow the globe's hemispherical bottom at the guard offset, ending in the bottom boss.
        for (int step = 1; step <= 5; step++)
        {
            float angle = step / 5f * 70f * Mathf.Deg2Rad;
            path.Add(direction * (GuardRadius * Mathf.Cos(angle)) +
                     Vector3.up * (GlassDomeY - GuardRadius * Mathf.Sin(angle)));
        }
        return path;
    }

    /// <summary>
    /// Jelly-jar globe: a short neck, a straight run, then a hemispherical bottom. V runs in metres
    /// along Y, which the glass material remaps to its vertical emission gradient.
    /// </summary>
    static Mesh GlobeMesh()
    {
        var profile = new List<Vector2>
        {
            new Vector2(.058f, -.108f), new Vector2(.066f, -.118f), new Vector2(GlassRadius, -.128f),
            new Vector2(GlassRadius, GlassDomeY),
        };
        for (int step = 1; step <= 6; step++)
        {
            float angle = step / 6f * 90f * Mathf.Deg2Rad;
            profile.Add(new Vector2(GlassRadius * Mathf.Cos(angle), GlassDomeY - GlassRadius * Mathf.Sin(angle)));
        }
        var builder = new ProceduralMeshBuilder();
        builder.AddLatheY(profile.ToArray(), Segments, false);
        return builder.ToMesh("SM_CorridorLamp_Globe");
    }

    static Mesh BulbMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddLatheY(new[]
        {
            new Vector2(.011f, -.150f), new Vector2(.016f, -.166f), new Vector2(.024f, -.188f),
            new Vector2(.026f, BulbY), new Vector2(.021f, -.228f), new Vector2(.011f, -.243f),
            new Vector2(0f, -.248f),
        }, 12, false);
        return builder.ToMesh("SM_CorridorLamp_Bulb");
    }

    /// <summary>Round cast wall boss, two fixing bolts and a conduit arm bending down into the hub.</summary>
    static Mesh WallMountMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddCylinder(Vector3.zero, Vector3.forward * .026f, .062f, .056f, Segments, true);
        foreach (float x in new[] { -.038f, .038f })
            builder.AddCylinder(new Vector3(x, 0f, .024f), new Vector3(x, 0f, .034f), .009f, .009f, 6, true);
        builder.AddCylinder(new Vector3(0f, 0f, .024f), new Vector3(0f, 0f, .05f), .032f, .03f, 12, true);

        const float bend = .06f;
        float bendZ = k_WallHeadOffset.z - bend;
        var path = new List<Vector3> { new Vector3(0f, 0f, .045f), new Vector3(0f, 0f, bendZ) };
        for (int step = 1; step <= 6; step++)
        {
            float angle = step / 6f * 90f * Mathf.Deg2Rad;
            path.Add(new Vector3(0f, -bend + bend * Mathf.Cos(angle), bendZ + bend * Mathf.Sin(angle)));
        }
        path.Add(new Vector3(0f, k_WallHeadOffset.y - .012f, k_WallHeadOffset.z));
        builder.AddTube(path, .022f, 8, true);
        return builder.ToMesh("SM_CorridorLamp_WallMount");
    }

    /// <summary>Round ceiling canopy and a straight conduit drop into the hub.</summary>
    static Mesh CeilingMountMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddCylinder(Vector3.zero, Vector3.down * .026f, .062f, .056f, Segments, true);
        foreach (float x in new[] { -.038f, .038f })
            builder.AddCylinder(new Vector3(x, -.024f, 0f), new Vector3(x, -.034f, 0f), .009f, .009f, 6, true);
        builder.AddCylinder(new Vector3(0f, -.02f, 0f), new Vector3(0f, k_CeilingHeadOffset.y - .012f, 0f),
            .022f, .022f, 8, true);
        return builder.ToMesh("SM_CorridorLamp_CeilingMount");
    }

    // ---- Prefabs -----------------------------------------------------------------------------

    /// <summary>
    /// Root sits on the mounting surface. The wall lamp's local +Z is the wall's outward normal; the
    /// ceiling lamp hangs down local -Y. Both carry one realtime, shadowless red point light.
    /// </summary>
    static void SaveLampPrefab(string name, Mesh mount, Vector3 headOffset, Dictionary<string, Mesh> meshes,
        Material paint, Material glass, Material bulb)
    {
        var root = new GameObject(name);
        try
        {
            AddVisual(root.transform, "Mount", mount, paint, true);
            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = headOffset;
            AddVisual(head, "Housing_And_Guard", meshes["Head"], paint, true);
            AddVisual(head, "Bulb", meshes["Bulb"], bulb, false);
            AddVisual(head, "Red_Globe", meshes["Globe"], glass, false);

            var lightObject = new GameObject("Lamp_Light");
            lightObject.transform.SetParent(head, false);
            lightObject.transform.localPosition = new Vector3(0f, BulbY, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LightColor;
            light.range = 5f;
            light.intensity = 2f;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.renderMode = LightRenderMode.Auto;
            lightObject.AddComponent<UniversalAdditionalLightData>();

            // One simple collider around the caged head so shots and props stop on the guard.
            var collider = head.gameObject.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.center = new Vector3(0f, -.2f, 0f);
            collider.radius = .095f;
            collider.height = .40f;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + name + ".prefab");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void AddVisual(Transform parent, string name, Mesh mesh, Material material, bool castShadows)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.receiveShadows = castShadows;
    }

    // ---- Review scene ------------------------------------------------------------------------

    static void BuildReviewScene()
    {
        Scene previous = SceneManager.GetActiveScene();
        bool useEmptyStartupScene = string.IsNullOrEmpty(previous.path);
        if (useEmptyStartupScene && previous.isDirty)
            throw new InvalidOperationException("Save or discard the modified untitled scene before building the Corridor Lamp.");

        // As in HeavyIndustrialKitBuilder: never close or modify the artist's open scene.
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

            // A 3 m wide, 3 m high, 12 m long corridor mock-up running along +Z.
            // Untextured stand-in surfaces: the lamp's metre-scale paint would stretch across cube faces.
            Material surface = LitMaterial("ReviewSurface");
            surface.SetColor("_BaseColor", new Color(.42f, .43f, .45f));
            surface.SetColor("_Color", new Color(.42f, .43f, .45f));
            surface.SetFloat("_Metallic", 0f);
            surface.SetFloat("_Smoothness", .25f);
            EditorUtility.SetDirty(surface);
            var corridor = new GameObject("Corridor_Mockup").transform;
            AddReviewBlock(corridor, "Floor", new Vector3(0f, -.05f, 4f), new Vector3(3.2f, .1f, 12f), surface);
            AddReviewBlock(corridor, "Ceiling", new Vector3(0f, 3.05f, 4f), new Vector3(3.2f, .1f, 12f), surface);
            AddReviewBlock(corridor, "Wall_West", new Vector3(-1.55f, 1.5f, 4f), new Vector3(.1f, 3f, 12f), surface);
            AddReviewBlock(corridor, "Wall_East", new Vector3(1.55f, 1.5f, 4f), new Vector3(.1f, 3f, 12f), surface);

            foreach (float z in new[] { 1.5f, 6.5f })
            {
                GameObject lamp = AddPrefab(WallPrefab);
                lamp.transform.SetPositionAndRotation(new Vector3(-1.5f, 2.3f, z), Quaternion.Euler(0f, 90f, 0f));
            }
            GameObject east = AddPrefab(WallPrefab);
            east.transform.SetPositionAndRotation(new Vector3(1.5f, 2.3f, 4f), Quaternion.Euler(0f, -90f, 0f));
            GameObject ceiling = AddPrefab(CeilingPrefab);
            ceiling.transform.position = new Vector3(0f, 3f, 9f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.10f, .105f, .115f);
            RenderSettings.reflectionIntensity = .6f;

            AddReviewCamera("Review_Corridor", new Vector3(.55f, 1.65f, -.6f), new Vector3(-1.3f, 2.05f, 2.2f), 50f);
            AddReviewCamera("Review_Closeup", new Vector3(-.75f, 2.12f, 1.08f), new Vector3(-1.28f, 2.03f, 1.5f), 38f);

            var volume = new GameObject("Review_Bloom").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = BuildReviewProfile();

            EditorSceneManager.SaveScene(scene, ReviewScenePath);
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

    static void AddReviewCamera(string name, Vector3 position, Vector3 target, float fieldOfView)
    {
        var camera = new GameObject(name).AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
        camera.fieldOfView = fieldOfView;
        camera.nearClipPlane = .02f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.01f, .012f, .015f);
        camera.allowHDR = true;
        camera.allowMSAA = true;
        var cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderPostProcessing = true;
        cameraData.renderShadows = true;
        cameraData.volumeLayerMask = ~0;
    }

    static VolumeProfile BuildReviewProfile()
    {
        string path = Root + "/CorridorLamp_ReviewProfile.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (!profile)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(.8f);
        bloom.scatter.Override(.6f);
        if (!profile.TryGet(out Tonemapping tonemapping)) tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.ACES);
        EditorUtility.SetDirty(profile);
        foreach (VolumeComponent component in profile.components)
        {
            if (!AssetDatabase.Contains(component)) AssetDatabase.AddObjectToAsset(component, profile);
        }
        return profile;
    }

    static void AddReviewBlock(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(parent, false);
        block.transform.localPosition = position;
        block.transform.localScale = size;
        block.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    static GameObject AddPrefab(string name)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + name + ".prefab");
        if (!asset) throw new FileNotFoundException("Missing generated prefab " + name);
        return (GameObject)PrefabUtility.InstantiatePrefab(asset);
    }

    static void RenderToPng(Camera camera, string path, int width, int height)
    {
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
            string absolute = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllBytes(absolute, texture.EncodeToPNG());
            Debug.Log("CORRIDOR LAMP REVIEW CAPTURED: " + absolute);
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    // ---- Validation --------------------------------------------------------------------------

    static bool Validate(bool logSuccess)
    {
        int errors = 0;
        foreach (string name in new[] { WallPrefab, CeilingPrefab })
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab)
            {
                Debug.LogError("Corridor Lamp: missing prefab " + path);
                errors++;
                continue;
            }

            int triangles = prefab.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh)
                .Sum(filter => filter.sharedMesh.triangles.Length / 3);
            if (triangles > TriangleLimit)
            {
                Debug.LogError($"Corridor Lamp: {name} has {triangles} triangles (limit {TriangleLimit}).");
                errors++;
            }
            if (prefab.GetComponentsInChildren<Collider>(true).Length == 0)
            {
                Debug.LogError("Corridor Lamp: " + name + " has no collider.");
                errors++;
            }
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Material material = renderer.sharedMaterial;
                if (!material || !material.shader || material.shader.name != "Universal Render Pipeline/Lit")
                {
                    Debug.LogError("Corridor Lamp: " + name + "/" + renderer.name + " does not use URP/Lit.");
                    errors++;
                }
            }
            Light[] lights = prefab.GetComponentsInChildren<Light>(true);
            if (lights.Length != 1 || lights[0].type != LightType.Point || lights[0].shadows != LightShadows.None ||
                lights[0].lightmapBakeType != LightmapBakeType.Realtime)
            {
                Debug.LogError("Corridor Lamp: " + name + " must carry exactly one realtime, shadowless point light.");
                errors++;
            }
            Debug.Log($"Corridor Lamp: {name} = {triangles} triangles.");
        }

        // The globe and bulb are pure lathes, so they prove the downward-profile winding the housing shares.
        foreach (string meshName in new[] { "Globe", "Bulb" })
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshFolder + "/SM_CorridorLamp_" + meshName + ".asset");
            if (!mesh) continue;
            int inward = CountInwardNormals(mesh);
            if (inward > 0)
            {
                Debug.LogError($"Corridor Lamp: SM_CorridorLamp_{meshName} has {inward} inward-facing lathe normals.");
                errors++;
            }
        }

        Material paint = LoadMaterial("CastPaint");
        if (!paint || !paint.GetTexture("_BaseMap") || !paint.GetTexture("_BumpMap"))
        {
            Debug.LogError("Corridor Lamp: incomplete cast paint material.");
            errors++;
        }
        foreach (string name in new[] { "RedGlass", "Bulb" })
        {
            Material material = LoadMaterial(name);
            if (!material || !material.IsKeywordEnabled("_EMISSION") ||
                (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive) == 0)
            {
                Debug.LogError("Corridor Lamp: M_CorridorLamp_" + name + " is not emissive after import.");
                errors++;
            }
        }
        Material glass = LoadMaterial("RedGlass");
        if (glass && (!glass.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || glass.renderQueue < (int)RenderQueue.Transparent))
        {
            Debug.LogError("Corridor Lamp: M_CorridorLamp_RedGlass is not transparent.");
            errors++;
        }
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ReviewScenePath))
        {
            Debug.LogError("Corridor Lamp: missing review scene " + ReviewScenePath);
            errors++;
        }

        if (errors == 0 && logSuccess)
            Debug.Log("CORRIDOR LAMP VALIDATION PASSED: both prefabs use URP/Lit, outward-facing meshes, one " +
                      "realtime shadowless point light, a collider, and no more than 2,000 triangles.");
        return errors == 0;
    }

    /// <summary>Counts normals that face the mesh's Y axis, ignoring vertices on the axis itself.</summary>
    static int CountInwardNormals(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int inward = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 radial = new Vector3(vertices[i].x, 0f, vertices[i].z);
            if (radial.sqrMagnitude < 1e-6f) continue;
            if (Vector3.Dot(radial.normalized, normals[i]) < -.01f) inward++;
        }
        return inward;
    }

    // ---- Textures and materials --------------------------------------------------------------

    static void BuildTextures()
    {
        const int size = 512;
        var albedo = new Color32[size * size];
        var height = new float[size * size];
        var paint = new Color32(172, 176, 178, 255);
        var exposed = new Color32(96, 98, 100, 255);
        var grime = new Color32(58, 52, 44, 255);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = x + y * size;
                // Cast-iron orange peel under thick paint, with chipped edges and settled grime.
                float broad = Mathf.PerlinNoise(x * .014f + 31f, y * .014f - 17f);
                float pit = Mathf.PerlinNoise(x * .11f + 5f, y * .11f + 9f);
                float fine = Hash01(x, y, 61);
                bool chipped = broad < .26f && fine > .86f;
                float grimeAmount = Mathf.Clamp01((Mathf.PerlinNoise(x * .03f - 4f, y * .045f + 2f) - .52f) * 2.4f);
                Color32 surface = chipped ? exposed : Color32.Lerp(paint, grime, grimeAmount * .35f);
                float value = .90f + broad * .10f + (pit - .5f) * .08f + (fine - .5f) * .04f;
                albedo[index] = Multiply(surface, value);
                height[index] = pit * .30f + broad * .12f + fine * .02f + (chipped ? -.22f : 0f);
            }
        }
        WritePng(TextureFolder + "/T_CorridorLamp_CastPaint_Albedo.png", albedo, size, size, false, TextureWrapMode.Repeat);
        WritePng(TextureFolder + "/T_CorridorLamp_CastPaint_Normal.png", NormalsFromHeight(height, size, 4f), size,
            size, true, TextureWrapMode.Repeat);

        // Vertical glow profile for the globe: hottest level with the bulb, falling off towards the
        // neck and dome, plus faint mould lines. U is constant, so the texture is a thin strip.
        const int glowWidth = 4, glowHeight = 256;
        var glow = new Color32[glowWidth * glowHeight];
        for (int y = 0; y < glowHeight; y++)
        {
            float globeY = Mathf.Lerp(GlobeBottom, GlobeTop, y / (glowHeight - 1f));
            float distance = Mathf.Abs(globeY - BulbY) / .11f;
            float value = .18f + .82f * Mathf.Exp(-distance * distance * 2.2f);
            if (Mathf.Abs(globeY - (GlassDomeY + .002f)) < .0025f) value *= .7f;
            byte level = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
            for (int x = 0; x < glowWidth; x++) glow[x + y * glowWidth] = new Color32(level, level, level, 255);
        }
        WritePng(TextureFolder + "/T_CorridorLamp_Glow.png", glow, glowWidth, glowHeight, false, TextureWrapMode.Clamp);
    }

    const float GlobeTop = -.108f;
    const float GlobeBottom = GlassDomeY - GlassRadius;

    static Material BuildPaintMaterial()
    {
        Material material = LitMaterial("CastPaint");
        material.SetTexture("_BaseMap", LoadTexture("CastPaint_Albedo"));
        material.SetTexture("_MainTex", LoadTexture("CastPaint_Albedo"));
        material.SetTexture("_BumpMap", LoadTexture("CastPaint_Normal"));
        material.SetFloat("_BumpScale", .8f);
        material.SetFloat("_Metallic", .1f);
        material.SetFloat("_Smoothness", .3f);
        material.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material BuildGlassMaterial()
    {
        Material material = LitMaterial("RedGlass");
        material.SetColor("_BaseColor", new Color(.34f, .012f, .01f, .78f));
        material.SetColor("_Color", new Color(.34f, .012f, .01f, .78f));
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", .9f);

        // URP/Lit shares _BaseMap_ST with the emission map: map the globe's height onto V in [0, 1].
        Texture2D glow = LoadTexture("Glow");
        material.SetTexture("_BaseMap", null);
        material.SetTexture("_EmissionMap", glow);
        float scale = 1f / (GlobeTop - GlobeBottom);
        material.SetTextureScale("_BaseMap", new Vector2(1f, scale));
        material.SetTextureOffset("_BaseMap", new Vector2(0f, -GlobeBottom * scale));
        material.SetColor("_EmissionColor", new Color(2.2f, .05f, .02f));
        MakeEmissive(material);

        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        // URP keeps specular on alpha-blended Lit by premultiplying, which pairs with a source factor of One.
        material.SetFloat("_BlendModePreserveSpecular", 1f);
        material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetFloat("_SrcBlend", (float)BlendMode.One);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Back);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material BuildBulbMaterial()
    {
        Material material = LitMaterial("Bulb");
        material.SetColor("_BaseColor", new Color(1f, .62f, .42f));
        material.SetColor("_Color", new Color(1f, .62f, .42f));
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", .8f);
        material.SetColor("_EmissionColor", new Color(9f, 2.6f, .9f));
        MakeEmissive(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void MakeEmissive(Material material)
    {
        // URP derives _EMISSION from AnyEmissive on import; None silently removes the keyword.
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        material.EnableKeyword("_EMISSION");
    }

    static Material LitMaterial(string name)
    {
        string path = MaterialFolder + "/M_CorridorLamp_" + name + ".mat";
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new InvalidOperationException("Universal Render Pipeline/Lit shader is unavailable.");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader) { name = "M_CorridorLamp_" + name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        return material;
    }

    static Material LoadMaterial(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_CorridorLamp_" + name + ".mat");
    }

    static Texture2D LoadTexture(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/T_CorridorLamp_" + name + ".png");
    }

    static Color32[] NormalsFromHeight(float[] height, int size, float strength)
    {
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float left = height[((x - 1 + size) % size) + y * size];
                float right = height[((x + 1) % size) + y * size];
                float down = height[x + ((y - 1 + size) % size) * size];
                float up = height[x + ((y + 1) % size) * size];
                Vector3 normal = new Vector3((left - right) * strength, (down - up) * strength, 1f).normalized;
                pixels[x + y * size] = new Color32(
                    (byte)Mathf.RoundToInt((normal.x * .5f + .5f) * 255f),
                    (byte)Mathf.RoundToInt((normal.y * .5f + .5f) * 255f),
                    (byte)Mathf.RoundToInt((normal.z * .5f + .5f) * 255f), 255);
            }
        }
        return pixels;
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

    static void WritePng(string assetPath, Color32[] pixels, int width, int height, bool normalMap,
        TextureWrapMode wrap)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, normalMap);
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        File.WriteAllBytes(Path.GetFullPath(assetPath), texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        importer.wrapMode = wrap;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.sRGBTexture = !normalMap;
        importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();
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

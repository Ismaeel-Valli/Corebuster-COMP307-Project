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
/// Rebuilds the first-principles yellow industrial robot arm (corebuster_art_002). Every rigid part is
/// authored in its own joint frame, so each joint transform is the part's pivot. In the zero pose the
/// chain stands straight up (+Y) and +Z is the arm's front; the prefab ships in <see cref="k_DisplayPose"/>.
/// Generated assets are committed; this builder is their reproducible source.
/// </summary>
public static class RoboticArmBuilder
{
    const string Root = "Assets/FPS/Art/StartWorkshop/RoboticArm";
    const string MeshFolder = Root + "/Meshes";
    const string MaterialFolder = Root + "/Materials";
    const string PrefabPath = Root + "/Prefabs/P_RoboticArm.prefab";
    const string TestScenePath = Root + "/RoboticArm_ReviewTest.unity";
    const string KitRoot = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit";
    const string ReviewFolder = "ReviewCaptures/RoboticArm";

    // Hero-prop budget: 2.5x corebuster_art_001's 2,000-triangle per-prefab ceiling.
    const int TriangleBudget = 5000;

    // Joint offsets from the parent joint, in metres (zero pose).
    static readonly Vector3 k_TurretPivot = new Vector3(0f, .42f, 0f);
    static readonly Vector3 k_ShoulderPivot = new Vector3(0f, .55f, 0f);
    static readonly Vector3 k_ElbowPivot = new Vector3(0f, 1.25f, 0f);
    static readonly Vector3 k_WristPivot = new Vector3(0f, 1.05f, 0f);
    static readonly Vector3 k_RollPivot = new Vector3(0f, .20f, 0f);
    static readonly Vector3 k_FingerPivot = new Vector3(.075f, .11f, 0f);
    static readonly Vector3 k_ToolPoint = new Vector3(0f, .30f, 0f);

    const int Yellow = 0;
    const int Steel = 1;
    const int Rubber = 2;

    /// <summary>A named set of joint angles in degrees; fingers are the opening per side in metres.</summary>
    struct Pose
    {
        public float Turret, Shoulder, Elbow, Wrist, Roll, FingerOpening;

        public Pose(float turret, float shoulder, float elbow, float wrist, float roll, float fingerOpening)
        {
            Turret = turret;
            Shoulder = shoulder;
            Elbow = elbow;
            Wrist = wrist;
            Roll = roll;
            FingerOpening = fingerOpening;
        }
    }

    // Lower arm leaning 15 degrees forward, upper arm reaching out and slightly down, tool pointing down.
    static readonly Pose k_DisplayPose = new Pose(0f, 15f, 95f, 50f, 0f, .02f);
    static readonly Pose k_ZeroPose = new Pose(0f, 0f, 0f, 0f, 0f, 0f);
    static readonly Pose k_ReachPose = new Pose(-35f, -12f, 118f, -35f, 90f, 0f);

    static readonly string[] k_JointPaths =
    {
        "J1_Turret",
        "J1_Turret/J2_Shoulder",
        "J1_Turret/J2_Shoulder/J3_Elbow",
        "J1_Turret/J2_Shoulder/J3_Elbow/J4_Wrist",
        "J1_Turret/J2_Shoulder/J3_Elbow/J4_Wrist/J5_WristRoll",
        "J1_Turret/J2_Shoulder/J3_Elbow/J4_Wrist/J5_WristRoll/J6_Finger_L",
        "J1_Turret/J2_Shoulder/J3_Elbow/J4_Wrist/J5_WristRoll/J6_Finger_R",
    };

    [MenuItem("Tools/CoreBuster/Build Robotic Arm")]
    public static void BuildAll()
    {
        EnsureFolder(MeshFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));

        Material yellow = BuildPaintMaterial(LoadKitMaterial("M_HeavyPipe_HazardYellow"));
        Material steel = LoadKitMaterial("M_HeavyPipe_BareSteel");
        Material rubber = BuildHoseMaterial();
        var slots = new[] { yellow, steel, rubber };

        var meshes = new Dictionary<string, Mesh>
        {
            ["Base"] = SaveMesh(BaseMesh(), "SM_RoboticArm_Base"),
            ["Turret"] = SaveMesh(TurretMesh(), "SM_RoboticArm_Turret"),
            ["LowerArm"] = SaveMesh(LowerArmMesh(), "SM_RoboticArm_LowerArm"),
            ["UpperArm"] = SaveMesh(UpperArmMesh(), "SM_RoboticArm_UpperArm"),
            ["Wrist"] = SaveMesh(WristMesh(), "SM_RoboticArm_Wrist"),
            ["Gripper"] = SaveMesh(GripperMesh(), "SM_RoboticArm_Gripper"),
            ["Finger"] = SaveMesh(FingerMesh(), "SM_RoboticArm_Finger"),
        };

        BuildPrefab(meshes, slots);
        AssetDatabase.SaveAssets();
        BuildReviewScene(steel);
        AssetDatabase.SaveAssets();

        if (!Validate(false))
            throw new InvalidOperationException("Robotic arm validation failed. See the Console for details.");

        Debug.Log("ROBOTIC ARM BUILT: P_RoboticArm with a six-joint pivot hierarchy, 7 mesh assets and a " +
                  "three-pose review scene under " + Root);
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

    [MenuItem("Tools/CoreBuster/Validate Robotic Arm")]
    public static void ValidateMenu()
    {
        Validate(true);
    }

    [MenuItem("Tools/CoreBuster/Capture Robotic Arm Review")]
    public static void CaptureReview()
    {
        if (!File.Exists(TestScenePath))
            throw new FileNotFoundException("Build the robotic arm first.", TestScenePath);

        Scene scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
        Camera[] cameras = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
        if (cameras.Length == 0) throw new InvalidOperationException("Robotic arm review scene has no camera.");
        // In batch mode the first frame after opening the scene renders before its lights are set up;
        // warm every camera once so each capture is lit the same way.
        foreach (Camera camera in cameras) camera.Render();
        foreach (Camera camera in cameras)
            Render(camera, Path.Combine(ReviewFolder, camera.name.Replace("Review_Camera_", "Arm_") + ".png"));
    }

    static void Render(Camera camera, string relativePath)
    {
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
            string absolute = Path.GetFullPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllBytes(absolute, texture.EncodeToPNG());
            Debug.Log("ROBOTIC ARM REVIEW CAPTURED: " + absolute);
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    // ---- Meshes. Submesh 0 = worn yellow, 1 = dark steel, 2 = hose rubber. ----

    /// <summary>Static base, floor origin: bevelled bolted plate, flange, yellow pedestal, hose feed.</summary>
    static Mesh BaseMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.UseSubmesh(Yellow);
        builder.AddLatheY(new[]
        {
            new Vector2(.30f, .11f), new Vector2(.30f, .36f), new Vector2(.28f, .40f), new Vector2(.24f, .42f),
        }, 24, true);

        builder.UseSubmesh(Steel);
        builder.AddFrustumY(0f, .06f, new Vector2(1f, 1f), new Vector2(.96f, .96f));
        foreach (Vector2 bolt in new[]
                 {
                     new Vector2(-.42f, -.42f), new Vector2(0f, -.42f), new Vector2(.42f, -.42f),
                     new Vector2(-.42f, 0f), new Vector2(.42f, 0f),
                     new Vector2(-.42f, .42f), new Vector2(0f, .42f), new Vector2(.42f, .42f),
                 })
            AddHexBolt(builder, new Vector3(bolt.x, .06f, bolt.y), .035f, .035f);
        builder.AddCylinder(new Vector3(0f, .06f, 0f), new Vector3(0f, .11f, 0f), .40f, .40f, 24, true);
        for (int i = 0; i < 12; i++)
        {
            float angle = (i + .5f) * Mathf.PI * 2f / 12f;
            AddHexBolt(builder, new Vector3(Mathf.Cos(angle) * .355f, .11f, Mathf.Sin(angle) * .355f), .022f, .025f);
        }
        builder.AddBlock(new Vector3(.26f, .13f, -.38f), new Vector3(.18f, .14f, .12f));

        // Two supply hoses leave the junction box and enter the pedestal's rear flank.
        builder.UseSubmesh(Rubber);
        AddHose(builder, new Vector3(.23f, .19f, -.38f), new Vector3(.23f, .34f, -.38f),
            new Vector3(.19f, .30f, -.30f), new Vector3(.15f, .28f, -.22f), .028f);
        AddHose(builder, new Vector3(.29f, .19f, -.38f), new Vector3(.29f, .30f, -.38f),
            new Vector3(.25f, .26f, -.28f), new Vector3(.20f, .24f, -.19f), .028f);
        return builder.ToMesh("SM_RoboticArm_Base");
    }

    /// <summary>J1 frame (pedestal top): slewing ring, housing, rounded shoulder cheeks and drive motors.</summary>
    static Mesh TurretMesh()
    {
        float shoulder = k_ShoulderPivot.y;
        var builder = new ProceduralMeshBuilder();
        builder.UseSubmesh(Yellow);
        builder.AddFrustumY(.07f, .30f, new Vector2(.64f, .60f), new Vector2(.54f, .50f));
        foreach (float side in new[] { -1f, 1f })
        {
            builder.AddBlock(new Vector3(.23f * side, .395f, 0f), new Vector3(.10f, .31f, .44f));
            builder.AddCylinder(new Vector3(.18f * side, shoulder, 0f), new Vector3(.28f * side, shoulder, 0f),
                .22f, .22f, 20, true);
        }
        builder.AddCylinder(new Vector3(.28f, shoulder, 0f), new Vector3(.44f, shoulder, 0f), .15f, .15f, 16, true);

        builder.UseSubmesh(Steel);
        builder.AddCylinder(Vector3.zero, new Vector3(0f, .07f, 0f), .33f, .33f, 24, true);
        builder.AddCylinder(new Vector3(.44f, shoulder, 0f), new Vector3(.48f, shoulder, 0f), .12f, .12f, 16, true);
        builder.AddCylinder(new Vector3(-.28f, shoulder, 0f), new Vector3(-.31f, shoulder, 0f), .12f, .12f, 16, true);
        builder.AddCylinder(new Vector3(0f, .12f, -.30f), new Vector3(0f, .42f, -.30f), .10f, .10f, 12, true);
        builder.AddCylinder(new Vector3(0f, .42f, -.30f), new Vector3(0f, .46f, -.30f), .07f, .07f, 12, true);

        // Rear motor feeds loop up to the shoulder drive and the opposite cheek.
        builder.UseSubmesh(Rubber);
        AddHose(builder, new Vector3(.05f, .38f, -.33f), new Vector3(.10f, .64f, -.42f),
            new Vector3(.36f, .72f, -.30f), new Vector3(.36f, .60f, -.10f), .03f);
        AddHose(builder, new Vector3(-.05f, .38f, -.33f), new Vector3(-.10f, .62f, -.42f),
            new Vector3(-.28f, .58f, -.34f), new Vector3(-.23f, .50f, -.16f), .03f);
        return builder.ToMesh("SM_RoboticArm_Turret");
    }

    /// <summary>J2 frame (shoulder axis): tapered main boom with axle hubs and the elbow drive.</summary>
    static Mesh LowerArmMesh()
    {
        float elbow = k_ElbowPivot.y;
        var builder = new ProceduralMeshBuilder();
        builder.UseSubmesh(Yellow);
        builder.AddFrustumY(.12f, elbow - .15f, new Vector2(.30f, .40f), new Vector2(.24f, .30f));
        builder.AddCylinder(new Vector3(-.15f, 0f, 0f), new Vector3(.15f, 0f, 0f), .21f, .21f, 20, true);
        builder.AddCylinder(new Vector3(-.13f, elbow, 0f), new Vector3(.13f, elbow, 0f), .17f, .17f, 20, true);
        builder.AddCylinder(new Vector3(-.15f, elbow, 0f), new Vector3(-.34f, elbow, 0f), .13f, .13f, 16, true);

        builder.UseSubmesh(Steel);
        builder.AddCylinder(new Vector3(-.175f, 0f, 0f), new Vector3(.175f, 0f, 0f), .12f, .12f, 16, true);
        builder.AddCylinder(new Vector3(-.15f, elbow, 0f), new Vector3(.15f, elbow, 0f), .14f, .14f, 16, true);
        builder.AddCylinder(new Vector3(-.34f, elbow, 0f), new Vector3(-.37f, elbow, 0f), .10f, .10f, 16, true);
        // Steel wear strips on the front and back faces of the boom.
        builder.AddFrustumY(.35f, .85f, new Vector2(.12f, .41f), new Vector2(.12f, .366f));

        // Two hydraulic lines bow off the back of the boom into the elbow drive.
        builder.UseSubmesh(Rubber);
        AddHose(builder, new Vector3(-.06f, .25f, -.16f), new Vector3(-.06f, .45f, -.36f),
            new Vector3(-.20f, 1.0f, -.34f), new Vector3(-.26f, 1.22f, -.08f), .03f);
        AddHose(builder, new Vector3(.06f, .25f, -.16f), new Vector3(.06f, .50f, -.31f),
            new Vector3(-.14f, 1.02f, -.28f), new Vector3(-.22f, 1.25f, -.02f), .03f);
        return builder.ToMesh("SM_RoboticArm_LowerArm");
    }

    /// <summary>J3 frame (elbow axis): motor block, forearm tube and the wrist fork.</summary>
    static Mesh UpperArmMesh()
    {
        float wrist = k_WristPivot.y;
        var builder = new ProceduralMeshBuilder();
        builder.UseSubmesh(Yellow);
        builder.AddBlock(new Vector3(0f, .30f, 0f), new Vector3(.24f, .40f, .22f));
        builder.AddCylinder(new Vector3(0f, .50f, 0f), new Vector3(0f, .95f, 0f), .095f, .08f, 16, true);
        foreach (float side in new[] { -1f, 1f })
        {
            builder.AddBlock(new Vector3(.095f * side, wrist - .07f, 0f), new Vector3(.03f, .14f, .14f));
            builder.AddCylinder(new Vector3(.08f * side, wrist, 0f), new Vector3(.11f * side, wrist, 0f),
                .07f, .07f, 12, true);
        }
        builder.AddBlock(new Vector3(0f, .955f, 0f), new Vector3(.22f, .03f, .14f));

        builder.UseSubmesh(Steel);
        builder.AddCylinder(new Vector3(0f, .49f, 0f), new Vector3(0f, .53f, 0f), .11f, .11f, 16, true);
        builder.AddCylinder(new Vector3(.12f, .30f, 0f), new Vector3(.22f, .30f, 0f), .08f, .08f, 12, true);
        builder.AddCylinder(new Vector3(.22f, .30f, 0f), new Vector3(.24f, .30f, 0f), .06f, .06f, 12, true);
        builder.AddCylinder(new Vector3(.15f, 0f, 0f), new Vector3(.19f, 0f, 0f), .10f, .10f, 16, true);

        builder.UseSubmesh(Rubber);
        AddHose(builder, new Vector3(-.04f, .45f, -.09f), new Vector3(-.04f, .60f, -.24f),
            new Vector3(-.02f, .82f, -.20f), new Vector3(-.01f, .90f, -.04f), .025f);
        AddHose(builder, new Vector3(.18f, .36f, -.02f), new Vector3(.20f, .50f, -.10f),
            new Vector3(.10f, .70f, -.14f), new Vector3(.03f, .78f, -.03f), .025f);
        return builder.ToMesh("SM_RoboticArm_UpperArm");
    }

    /// <summary>J4 frame (wrist pitch axis): axle hub and the short wrist body.</summary>
    static Mesh WristMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.UseSubmesh(Yellow);
        builder.AddLatheY(new[] { new Vector2(.065f, 0f), new Vector2(.07f, .10f), new Vector2(.06f, .20f) }, 16, true);
        builder.UseSubmesh(Steel);
        builder.AddCylinder(new Vector3(-.075f, 0f, 0f), new Vector3(.075f, 0f, 0f), .065f, .065f, 12, true);
        return builder.ToMesh("SM_RoboticArm_Wrist");
    }

    /// <summary>J5 frame (tool roll axis): bolted tool flange and gripper palm.</summary>
    static Mesh GripperMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddCylinder(new Vector3(0f, -.02f, 0f), new Vector3(0f, .02f, 0f), .09f, .09f, 16, true);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.PI * 2f / 6f;
            AddHexBolt(builder, new Vector3(Mathf.Cos(angle) * .07f, .02f, Mathf.Sin(angle) * .07f), .012f, .015f);
        }
        builder.AddBlock(new Vector3(0f, .065f, 0f), new Vector3(.26f, .09f, .10f));
        return builder.ToMesh("SM_RoboticArm_Gripper");
    }

    /// <summary>J6 frame: one finger whose tip points local -X, toward the tool axis.</summary>
    static Mesh FingerMesh()
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddBlock(new Vector3(0f, .08f, 0f), new Vector3(.035f, .16f, .07f));
        builder.AddBlock(new Vector3(-.025f, .145f, 0f), new Vector3(.05f, .03f, .07f));
        return builder.ToMesh("SM_RoboticArm_Finger");
    }

    static void AddHexBolt(ProceduralMeshBuilder builder, Vector3 seat, float radius, float height)
    {
        builder.AddCylinder(seat, seat + Vector3.up * height, radius, radius, 6, true);
    }

    static void AddHose(ProceduralMeshBuilder builder, Vector3 start, Vector3 startHandle,
        Vector3 endHandle, Vector3 end, float radius)
    {
        builder.AddTube(ProceduralMeshBuilder.Bezier(start, startHandle, endHandle, end, 12), radius, 8, true);
    }

    // ---- Prefab ----

    static void BuildPrefab(Dictionary<string, Mesh> meshes, Material[] slots)
    {
        var root = new GameObject("P_RoboticArm");
        try
        {
            Transform rootTransform = root.transform;
            Part("Base", rootTransform, Vector3.zero, meshes["Base"], slots);
            AddBoxCollider(rootTransform, "Collision_Base", new Vector3(0f, .21f, 0f), new Vector3(1f, .42f, 1f));

            Transform turret = Part("J1_Turret", rootTransform, k_TurretPivot, meshes["Turret"], slots);
            AddBoxCollider(turret, "Collision_Turret", new Vector3(.02f, .38f, -.05f), new Vector3(.92f, .76f, .74f));

            Transform shoulder = Part("J2_Shoulder", turret, k_ShoulderPivot, meshes["LowerArm"], slots);
            AddBoxCollider(shoulder, "Collision_LowerArm", new Vector3(-.08f, .62f, -.09f), new Vector3(.60f, 1.66f, .62f));

            Transform elbow = Part("J3_Elbow", shoulder, k_ElbowPivot, meshes["UpperArm"], slots);
            AddBoxCollider(elbow, "Collision_UpperArm", new Vector3(.06f, .51f, -.07f), new Vector3(.38f, 1.22f, .38f));

            Transform wrist = Part("J4_Wrist", elbow, k_WristPivot, meshes["Wrist"], slots);
            Transform roll = Part("J5_WristRoll", wrist, k_RollPivot, meshes["Gripper"], new[] { slots[Steel] });
            AddBoxCollider(roll, "Collision_Gripper", new Vector3(0f, .05f, 0f), new Vector3(.30f, .40f, .16f));

            Part("J6_Finger_L", roll, new Vector3(-k_FingerPivot.x, k_FingerPivot.y, 0f), meshes["Finger"],
                new[] { slots[Steel] }).localRotation = Quaternion.Euler(0f, 180f, 0f);
            Part("J6_Finger_R", roll, k_FingerPivot, meshes["Finger"], new[] { slots[Steel] });

            var tool = new GameObject("Tool_Point").transform;
            tool.SetParent(roll, false);
            tool.localPosition = k_ToolPoint;

            ApplyPose(root, k_DisplayPose);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static Transform Part(string name, Transform parent, Vector3 localPosition, Mesh mesh, Material[] slots)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = slots.Take(mesh.subMeshCount).ToArray();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return part.transform;
    }

    static void AddBoxCollider(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = center;
        child.AddComponent<BoxCollider>().size = size;
    }

    /// <summary>
    /// Poses an arm instance through its joint transforms only: yaw about J1 local Y, pitch about
    /// J2-J4 local X, roll about J5 local Y, and each finger slides outward along its local X.
    /// </summary>
    static void ApplyPose(GameObject arm, Pose pose)
    {
        Transform Joint(int index) => arm.transform.Find(k_JointPaths[index]);
        Joint(0).localRotation = Quaternion.Euler(0f, pose.Turret, 0f);
        Joint(1).localRotation = Quaternion.Euler(pose.Shoulder, 0f, 0f);
        Joint(2).localRotation = Quaternion.Euler(pose.Elbow, 0f, 0f);
        Joint(3).localRotation = Quaternion.Euler(pose.Wrist, 0f, 0f);
        Joint(4).localRotation = Quaternion.Euler(0f, pose.Roll, 0f);
        float fingerX = k_FingerPivot.x + pose.FingerOpening;
        Joint(5).localPosition = new Vector3(-fingerX, k_FingerPivot.y, 0f);
        Joint(6).localPosition = new Vector3(fingerX, k_FingerPivot.y, 0f);
    }

    // ---- Materials ----

    static Material LoadKitMaterial(string name)
    {
        string path = KitRoot + "/Materials/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
            throw new FileNotFoundException(
                "The robotic arm shares corebuster_art_001's finishes; run Tools/CoreBuster/Build Heavy Industrial Kit first.",
                path);
        return material;
    }

    /// <summary>
    /// The kit's worn hazard-yellow paint, tiled 2.5x: at pipe scale its broad wear reads as chipping,
    /// but across the arm's flat booms it would read as camouflage blotches.
    /// </summary>
    static Material BuildPaintMaterial(Material kitPaint)
    {
        Material material = LoadOrCreateMaterial("M_RoboticArm_WornYellow", kitPaint.shader);
        material.CopyPropertiesFromMaterial(kitPaint);
        material.SetTextureScale("_BaseMap", new Vector2(2.5f, 2.5f));
        material.SetTextureScale("_MainTex", new Vector2(2.5f, 2.5f));
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material LoadOrCreateMaterial(string name, Shader shader)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        return material;
    }

    /// <summary>Dark hydraulic-hose rubber reusing the kit's steel grain under a near-black, non-metal tint.</summary>
    static Material BuildHoseMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new InvalidOperationException("Universal Render Pipeline/Lit shader is unavailable.");
        Material material = LoadOrCreateMaterial("M_RoboticArm_HoseRubber", shader);
        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(KitRoot + "/Textures/T_HeavyPipe_BareSteel_Albedo.png");
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(KitRoot + "/Textures/T_HeavyPipe_BareSteel_Normal.png");
        if (!albedo || !normal)
            throw new FileNotFoundException("Missing corebuster_art_001 bare-steel textures under " + KitRoot);
        var tint = new Color(.2f, .2f, .21f);
        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_MainTex", albedo);
        material.SetColor("_BaseColor", tint);
        material.SetColor("_Color", tint);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_BumpScale", .6f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", .32f);
        material.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---- Review scene ----

    static void BuildReviewScene(Material floorMaterial)
    {
        Scene previous = SceneManager.GetActiveScene();
        bool useEmptyStartupScene = string.IsNullOrEmpty(previous.path);
        if (useEmptyStartupScene && previous.isDirty)
            throw new InvalidOperationException(
                "Save or discard the modified untitled scene before building the robotic arm.");

        // Same scene-handling rule as HeavyIndustrialKitBuilder: never close or modify the artist's scene.
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

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            AddArm(prefab, "Arm_DisplayPose", Vector3.zero, k_DisplayPose);
            AddArm(prefab, "Arm_ZeroPose", new Vector3(-3.2f, 0f, .4f), k_ZeroPose);
            AddArm(prefab, "Arm_ReachPose", new Vector3(3.2f, 0f, .4f), k_ReachPose);

            // 1.8 m person-height reference for the scale review.
            var person = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            person.name = "Scale_Reference_1_8m";
            person.transform.position = new Vector3(-1.4f, .9f, 1.6f);
            person.transform.localScale = new Vector3(.5f, .9f, .5f);
            person.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Review_Floor";
            floor.transform.position = new Vector3(0f, -.08f, 1f);
            floor.transform.localScale = new Vector3(12f, .15f, 9f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;

            var key = new GameObject("Key_Light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.6f;
            key.color = new Color(1f, .93f, .82f);
            key.transform.rotation = Quaternion.Euler(40f, -145f, 0f);
            key.shadows = LightShadows.Soft;

            var fill = new GameObject("Fill_Light").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 12f;
            fill.intensity = 3f;
            fill.color = Color.white;
            fill.transform.position = new Vector3(2.5f, 4f, 4f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.24f, .24f, .24f);
            RenderSettings.reflectionIntensity = 1f;

            // The acceptance view: the display-pose arm from exactly 10 m at a player-like 60-degree FOV,
            // three-quarter side-on as it will stand beside the workbench.
            Vector3 heroTarget = new Vector3(0f, 1.2f, .5f);
            AddCamera("Review_Camera_10m", heroTarget + new Vector3(.85f, .2f, .45f).normalized * 10f,
                heroTarget, 60f);
            AddCamera("Review_Camera_Detail", heroTarget + new Vector3(.8f, .3f, .5f).normalized * 4.2f,
                heroTarget + new Vector3(0f, .1f, .1f), 50f);
            AddCamera("Review_Camera_Poses", new Vector3(1.8f, 3.6f, 8.6f), new Vector3(0f, 1.1f, .4f), 50f);

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

    static void AddArm(GameObject prefab, string name, Vector3 position, Pose pose)
    {
        var arm = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        arm.name = name;
        arm.transform.position = position;
        ApplyPose(arm, pose);
    }

    static void AddCamera(string name, Vector3 position, Vector3 target, float fieldOfView)
    {
        var camera = new GameObject(name).AddComponent<Camera>();
        camera.transform.position = position;
        camera.transform.rotation = Quaternion.LookRotation(target - position);
        camera.fieldOfView = fieldOfView;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.025f, .035f, .045f);
        camera.allowHDR = true;
        camera.allowMSAA = true;
        var cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderPostProcessing = true;
        cameraData.renderShadows = true;
        cameraData.volumeLayerMask = ~0;
    }

    // ---- Validation ----

    static bool Validate(bool logSuccess)
    {
        int errors = 0;
        void Fail(string message)
        {
            Debug.LogError("Robotic arm: " + message);
            errors++;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!prefab)
        {
            Fail("missing prefab " + PrefabPath);
            return false;
        }

        int triangles = 0;
        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.sharedMesh)
            {
                Fail(filter.name + " has no mesh.");
                continue;
            }
            int partTriangles = filter.sharedMesh.triangles.Length / 3;
            triangles += partTriangles;
            Debug.Log($"Robotic arm: {filter.name} = {partTriangles} triangles.");
        }
        if (triangles > TriangleBudget) Fail($"{triangles} triangles exceeds the {TriangleBudget} budget.");

        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter && filter.sharedMesh && renderer.sharedMaterials.Length != filter.sharedMesh.subMeshCount)
                Fail(renderer.name + " has a material count that does not match its submeshes.");
            if (renderer.sharedMaterials.Any(material => !material || !material.shader ||
                                                         material.shader.name != "Universal Render Pipeline/Lit" ||
                                                         !material.GetTexture("_BaseMap")))
                Fail(renderer.name + " does not use textured URP/Lit materials throughout.");
        }

        Collider[] colliders = prefab.GetComponentsInChildren<Collider>(true);
        if (colliders.Any(collider => !(collider is BoxCollider || collider is CapsuleCollider || collider is SphereCollider)))
            Fail("only primitive colliders are allowed.");
        foreach (string part in new[] { "", "J1_Turret", k_JointPaths[1], k_JointPaths[2], k_JointPaths[4] })
        {
            Transform owner = part.Length == 0 ? prefab.transform : prefab.transform.Find(part);
            if (owner && !owner.Cast<Transform>().Any(child => child.GetComponent<Collider>()))
                Fail((part.Length == 0 ? "the base" : part) + " has no collider moving with it.");
        }

        // Each pivot must sit inside the geometry of both parts it joins, so rotating it cannot
        // detach a part. Bounds are checked in the zero pose, which is how the meshes are authored.
        Transform previousPart = prefab.transform.Find("Base");
        if (!previousPart) Fail("missing Base.");
        foreach (string path in k_JointPaths)
        {
            Transform joint = prefab.transform.Find(path);
            if (!joint)
            {
                Fail("missing joint " + path);
                continue;
            }
            Transform parentPart = path.Contains("J6_") ? prefab.transform.Find(k_JointPaths[4]) : previousPart;
            Mesh parentMesh = MeshOf(parentPart);
            Mesh childMesh = MeshOf(joint);
            if (!parentMesh || !Expanded(parentMesh.bounds).Contains(joint.localPosition))
                Fail(path + " pivot is outside its parent part's geometry.");
            if (!childMesh || !Expanded(childMesh.bounds).Contains(Vector3.zero))
                Fail(path + " pivot is outside its own part's geometry.");
            if (!path.Contains("J6_")) previousPart = joint;
        }
        if (!prefab.transform.Find(k_JointPaths[4] + "/Tool_Point")) Fail("missing Tool_Point.");

        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath)) Fail("missing review scene " + TestScenePath);

        Debug.Log($"Robotic arm: P_RoboticArm = {triangles} triangles (budget {TriangleBudget}).");
        if (errors == 0 && logSuccess)
            Debug.Log("ROBOTIC ARM VALIDATION PASSED: pivot hierarchy, primitive colliders on every moving part, " +
                      $"URP/Lit materials and {triangles}/{TriangleBudget} triangles.");
        return errors == 0;
    }

    static Mesh MeshOf(Transform part)
    {
        MeshFilter filter = part ? part.GetComponent<MeshFilter>() : null;
        return filter ? filter.sharedMesh : null;
    }

    static Bounds Expanded(Bounds bounds)
    {
        bounds.Expand(.02f);
        return bounds;
    }

    static Mesh SaveMesh(Mesh generated, string name)
    {
        string path = MeshFolder + "/" + name + ".asset";
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

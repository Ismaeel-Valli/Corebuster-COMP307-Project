using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>corebuster_art_006: four parked assembly arms in two supported overhead cells. Never edits the Workshop arm.</summary>
public static class BossHallMechanicalArms
{
    public const string Owner = "corebuster_art_006";
    public const string Folder = BossHallFramework.AssetRoot + "/Mechanical_Arms";
    public const string Output = "ReviewCaptures/BossHall/006";
    public const string SourcePrefab = "Assets/FPS/Art/StartWorkshop/RoboticArm/Prefabs/P_RoboticArm.prefab";
    const string SourceMeshes = "Assets/FPS/Art/StartWorkshop/RoboticArm/Meshes/";
    const float ArmScale = 1.55f;
    public static readonly string[] Roots = { "Assembly_Arms_South", "Assembly_Arms_North" };
    public static readonly string[] Finishes = { "Gunmetal", "HazardYellow" };
    static readonly string[] JointPaths = {
        "J1_Turret", "J1_Turret/J2_Shoulder", "J1_Turret/J2_Shoulder/J3_Elbow",
        "J1_Turret/J2_Shoulder/J3_Elbow/J4_Wrist", "J1_Turret/J2_Shoulder/J3_Elbow/J4_Wrist/J5_WristRoll"
    };

    public sealed class Frame
    {
        public BossHallBackdrop.Frame backdrop;
        public float runwayX, baseY, runwayBottom;
        public Bounds[] bays;
        public Vector3 Pivot(int side) => new Vector3(runwayX, baseY, bays[side].center.z);
    }

    public static Frame Measure(BossHallFramework.Layout layout)
    {
        var b = BossHallBackdrop.Measure(layout);
        var f = new Frame { backdrop = b, runwayX = b.wallX + 6.6f, baseY = b.ceiling - 2.35f, runwayBottom = b.ceiling - 1.3f };
        f.bays = new[] {
            Box(new Vector3(b.wallX + 4.3f, b.upperTop + .3f, b.bedSouth - 3.5f), new Vector3(b.wallX + 8f, b.ceiling - 2.2f, b.axisZ - 3.2f)),
            Box(new Vector3(b.wallX + 4.3f, b.upperTop + .3f, b.axisZ + 3.2f), new Vector3(b.wallX + 8f, b.ceiling - 2.2f, b.bedNorth + 3.5f))
        };
        if (f.bays.Any(x => x.size.z < 10 || x.size.y < 4)) throw new InvalidOperationException("Measured arm bays are too small.");
        return f;
    }

    static Bounds Box(Vector3 a, Vector3 b) { var result = new Bounds(); result.SetMinMax(a, b); return result; }
    static bool Inside(Bounds a, Bounds b) => a.Contains(b.min - Vector3.one * .001f) && a.Contains(b.max + Vector3.one * .001f);
    static string PrefabPath(int side) => Folder + "/Prefabs/P_" + Roots[side] + ".prefab";

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    // Copies visual transforms and mesh references only. Collision, interaction and scripts never enter an overhead cell.
    static Transform CopyVisual(Transform source, Transform parent)
    {
        if (source.name.StartsWith("Collision_", StringComparison.Ordinal)) return null;
        var t = new GameObject(source.name).transform; t.SetParent(parent, false);
        t.localPosition = source.localPosition; t.localRotation = source.localRotation; t.localScale = source.localScale;
        var filter = source.GetComponent<MeshFilter>();
        if (filter)
        {
            if (!filter.sharedMesh || !AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith(SourceMeshes, StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected source arm mesh at " + source.name);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterials = Enumerable.Range(0, filter.sharedMesh.subMeshCount)
                .Select(i => BossHallRendering.GetFinish(i == 0 && source.name != "J5_WristRoll" && !source.name.StartsWith("J6_") ? "HazardYellow" : "Gunmetal")).ToArray();
        }
        foreach (Transform child in source) CopyVisual(child, t);
        return t;
    }

    static void Part(Transform parent, string name, Mesh mesh, Material[] materials)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false);
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials.Take(mesh.subMeshCount).ToArray();
    }

    static void Cylinder(ProceduralMeshBuilder b, Vector3 a, Vector3 c, float radius, int segments = 16)
        => b.AddCylinder(a, c, radius, radius, segments, true);
    static void Hose(ProceduralMeshBuilder b, Vector3 a, Vector3 c, Vector3 bow, float radius)
        => b.AddTube(ProceduralMeshBuilder.Bezier(a, a + bow, c + bow, c, 12), radius, 8, true);

    static Mesh SaveMesh(ProceduralMeshBuilder builder, string name)
    {
        var mesh = builder.ToMesh(name); string path = Folder + "/Meshes/" + name + ".asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved) { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
        else { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
        BossHallFramework.RequireOwnedAsset(saved, BossHallFramework.Section.MechanicalArms);
        return saved;
    }

    /// <summary>Asset-only stage, safe before the scene-placement gate. Uses the accepted survey, never refreshes a baseline.</summary>
    public static void PrepareBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use exclusive batch Editor access.");
        var contract = JsonUtility.FromJson<BossHallFramework.Contract>(File.ReadAllText(BossHallFramework.ContractPath));
        BuildAssets(Measure(new BossHallFramework.Layout(contract)));
        var before = AssetHashes(); BuildAssets(Measure(new BossHallFramework.Layout(contract)));
        BossHallSnapshot.Compare(before, AssetHashes(), "Deterministic arm composition assets");
        Debug.Log("BOSS HALL ARM ASSETS VERIFIED: two cells, four moderate-scale arms, source prefab untouched; no MainScene authoring.");
    }

    static Dictionary<string, string> AssetHashes() => Directory.GetFiles(Folder, "*", SearchOption.AllDirectories)
        .ToDictionary(p => p, BossHallSurvey.Hash, StringComparer.Ordinal);

    static void BuildAssets(Frame f)
    {
        EnsureFolder(Folder + "/Meshes"); EnsureFolder(Folder + "/Prefabs");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab) ?? throw new InvalidOperationException("Missing source Workshop arm.");
        var sourceHashes = Directory.GetFiles("Assets/FPS/Art/StartWorkshop/RoboticArm", "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, BossHallSurvey.Hash, StringComparer.Ordinal);
        var materials = Finishes.Select(BossHallRendering.GetFinish).ToArray();
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            for (int side = 0; side < 2; side++)
            {
                var root = new GameObject(Roots[side]); SceneManager.MoveGameObjectToScene(root, preview);
                try
                {
                    var structure = new ProceduralMeshBuilder(); var services = new ProceduralMeshBuilder(); var fixtures = new ProceduralMeshBuilder();
                    float drop = f.runwayBottom - f.baseY, direction = side == 0 ? 1 : -1;
                    // Two separate bolted rail clamps, not a new free-standing gantry spanning the bay-door approach.
                    for (int armIndex = 0; armIndex < 2; armIndex++)
                    {
                        string prefix = "Arm_" + (armIndex == 0 ? "A_" : "B_");
                        float mountZ = armIndex == 0 ? -2.5f : 2.5f;
                        var arm = CopyVisual(source.transform, root.transform);
                        arm.localPosition = new Vector3(0, 0, mountZ);
                        arm.localRotation = Quaternion.Euler(0, side == 0 ? 0 : 180, 0) * Quaternion.Euler(0, 0, 180);
                        arm.localScale = Vector3.one * ArmScale;
                        Transform Joint(int i) => arm.Find(JointPaths[i]) ?? throw new InvalidOperationException("Source arm joint missing: " + JointPaths[i]);
                        Joint(0).localRotation = Quaternion.identity;
                        Joint(1).localRotation = Quaternion.Euler(armIndex == 0 ? 15 : 32, 0, 0);
                        Joint(2).localRotation = Quaternion.Euler(armIndex == 0 ? 95 : 74, 0, 0);
                        Joint(3).localRotation = Quaternion.Euler(armIndex == 0 ? 50 : 54, 0, 0);
                        Joint(4).localRotation = Quaternion.identity;
                        var tool = Joint(4).Find("Tool_Point");
                        if (!tool) throw new InvalidOperationException("Source tool point missing.");
                        Vector3 Local(Vector3 p) => root.transform.InverseTransformPoint(p);
                        Vector3 basePoint = new Vector3(0, 0, mountZ);
                        structure.UseSubmesh(0);
                        structure.AddBlock(basePoint + Vector3.up * .08f, new Vector3(1.8f, .16f, 1.8f));
                        structure.AddBlock(basePoint + Vector3.up * (drop * .5f), new Vector3(.55f, drop, .7f));
                        // The flange grasps the existing runway's lower flange (a deliberate support contact).
                        structure.AddBlock(basePoint + Vector3.up * (drop + .06f), new Vector3(1.2f, .18f, 1.5f));
                        foreach (float dx in new[] { -.66f, .66f })
                        {
                            Cylinder(structure, basePoint + new Vector3(dx, .16f, -.55f), basePoint + new Vector3(dx * .55f, drop, -.55f), .075f);
                            Cylinder(structure, basePoint + new Vector3(dx, .16f, .55f), basePoint + new Vector3(dx * .55f, drop, .55f), .075f);
                        }
                        structure.UseSubmesh(1);
                        structure.AddBlock(basePoint + new Vector3(.91f, .08f, 0), new Vector3(.06f, .16f, 1.5f));
                        structure.AddBlock(basePoint + new Vector3(-.91f, .08f, 0), new Vector3(.06f, .16f, 1.5f));
                        structure.UseSubmesh(0);
                        foreach (float dx in new[] { -.68f, .68f }) foreach (float dz in new[] { -.68f, .68f })
                            Cylinder(structure, basePoint + new Vector3(dx, .16f, dz), basePoint + new Vector3(dx, .23f, dz), .065f, 6);

                        services.UseSubmesh(0);
                        Vector3 feed = basePoint + new Vector3(-.48f, drop - .12f, -.5f * direction);
                        services.AddBlock(feed, new Vector3(.28f, .22f, .38f));
                        for (int h = 0; h < 2; h++)
                        {
                            Vector3 port = feed + new Vector3(0, -.1f, (h == 0 ? -.09f : .09f));
                            Vector3 pedestal = Local(arm.TransformPoint(new Vector3(.26f + h * .05f, .19f, -.38f)));
                            Hose(services, port, pedestal, new Vector3(-.22f, .05f, -.25f * direction), .035f);
                            Cylinder(services, port - Vector3.up * .04f, port + Vector3.up * .04f, .065f, 12);
                            Cylinder(services, pedestal - arm.forward * .05f, pedestal + arm.forward * .05f, .06f, 12);
                        }
                        // A cylinder and exposed rod with clevises follow each boom; they share the boom's rigid frame.
                        var shoulder = Joint(1);
                        Vector3 a = Local(shoulder.TransformPoint(new Vector3(.32f, .15f, -.22f)));
                        Vector3 c = Local(shoulder.TransformPoint(new Vector3(.32f, 1.05f, -.22f)));
                        Vector3 split = Vector3.Lerp(a, c, .62f);
                        Cylinder(services, a, split, .095f); Cylinder(services, split, c, .045f);
                        foreach (var p in new[] { a, c }) Cylinder(services, p - shoulder.right * .1f, p + shoulder.right * .1f, .11f);

                        // Each parked gripper holds a small tooling mandrel on an attached fixture, never an interactive core.
                        var tip = Local(tool.position - tool.up * (.04f * ArmScale));
                        var post = new Vector3(1.05f, tip.y - .25f, tip.z);
                        fixtures.UseSubmesh(0);
                        Cylinder(fixtures, tip - tool.up * .16f, tip + tool.up * .16f, .045f * ArmScale);
                        Cylinder(fixtures, tip, post + Vector3.up * .25f, .07f);
                        Cylinder(fixtures, post, new Vector3(post.x, drop + .06f, post.z), .09f);
                        fixtures.AddBlock(new Vector3(post.x, drop + .06f, post.z), new Vector3(.35f, .18f, .4f));
                        fixtures.UseSubmesh(1);
                        fixtures.AddBlock(post, new Vector3(.28f, .14f, .36f));
                        // Prefab identities are stable because every generated GameObject name is globally unique.
                        foreach (var t in arm.GetComponentsInChildren<Transform>()) t.name = prefix + t.name;
                    }
                    Part(root.transform, "Bolted_Gantry_Hangers", SaveMesh(structure, "SM_Arms_Hangers_" + side), materials);
                    Part(root.transform, "Hydraulics_And_Supply_Hoses", SaveMesh(services, "SM_Arms_Services_" + side), materials);
                    Part(root.transform, "Parked_Clamp_Fixtures", SaveMesh(fixtures, "SM_Arms_Fixtures_" + side), materials);
                    if (root.GetComponentsInChildren<Transform>().GroupBy(t => t.name).Any(g => g.Count() != 1))
                        throw new InvalidOperationException("Repeated arm-composition child name.");
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(side));
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        BossHallSnapshot.Compare(sourceHashes, Directory.GetFiles("Assets/FPS/Art/StartWorkshop/RoboticArm", "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, BossHallSurvey.Hash, StringComparer.Ordinal), "Complete source arm folder and metadata");
    }

    static void Populate(Transform section, Frame f)
    {
        for (int side = 0; side < 2; side++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(side)) ?? throw new InvalidOperationException("Prepare arm assets first.");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, section);
            Undo.RegisterCreatedObjectUndo(root, "Build Boss Hall mechanical arms");
            root.name = Roots[side];
            root.transform.position = f.Pivot(side); root.transform.rotation = Quaternion.identity; root.transform.localScale = Vector3.one;
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
        }
    }

    public static string Validate(BossHallFramework.Layout layout)
    {
        BossHallBackdrop.Validate(layout); var f = Measure(layout);
        var section = BossHallFramework.FindSection(BossHallFramework.Section.MechanicalArms);
        if (!section.Cast<Transform>().Select(t => t.name).SequenceEqual(Roots))
            throw new InvalidOperationException("Expected two arm cells; got " + string.Join(", ", section.Cast<Transform>().Select(t => t.name)));
        var renderers = section.GetComponentsInChildren<MeshRenderer>(true).Where(r => !BossHallEnergy.IsEnergyRenderer(r)).ToArray();
        if (renderers.Length != 38) throw new InvalidOperationException("Expected 32 source part renderers and six supporting meshes; got " + renderers.Length);
        int triangles = 0;
        for (int side = 0; side < 2; side++)
        {
            var root = section.Find(Roots[side]);
            if (Vector3.Distance(root.position, f.Pivot(side)) > .001f || root.rotation != Quaternion.identity || root.localScale != Vector3.one ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root.gameObject) != PrefabPath(side)) throw new InvalidOperationException("Altered arm-cell placement or prefab binding.");
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (BossHallEnergy.IsEnergyRenderer(r)) continue;
                var allowed = f.bays[side];
                // Only the three owned supporting meshes can extend upward to connect to the existing runway.
                var mesh = r.GetComponent<MeshFilter>().sharedMesh; string path = AssetDatabase.GetAssetPath(mesh);
                bool owned = path.StartsWith(Folder + "/Meshes/", StringComparison.Ordinal);
                if (owned) allowed.SetMinMax(allowed.min, new Vector3(allowed.max.x, f.runwayBottom + .18f, allowed.max.z));
                allowed.Expand(.006f);
                if (!Inside(allowed, r.bounds) || !r.enabled || !r.gameObject.activeInHierarchy || !mesh ||
                    (!owned && !path.StartsWith(SourceMeshes, StringComparison.Ordinal))) throw new InvalidOperationException("Invalid arm bounds/source: " + r.name + " " + r.bounds);
                if (r.sharedMaterials.Length != mesh.subMeshCount || r.sharedMaterials.Any(m => !Finishes.Any(n => m == BossHallRendering.GetFinish(n))))
                    throw new InvalidOperationException("Unexpected arm finish: " + r.name);
                triangles += (int)mesh.GetIndexCount(0) / 3;
                for (int i = 1; i < mesh.subMeshCount; i++) triangles += (int)mesh.GetIndexCount(i) / 3;
            }
            foreach (string prefix in new[] { "Arm_A_", "Arm_B_" })
            {
                var arm = root.Find(prefix + "P_RoboticArm");
                if (!arm || Vector3.Distance(arm.localScale, Vector3.one * ArmScale) > .001f) throw new InvalidOperationException("Unexpected arm scale.");
                var pedestal = arm.Find(prefix + "Base").GetComponent<MeshRenderer>();
                var turret = arm.Find(prefix + "J1_Turret");
                if (!ContainsExpanded(pedestal.bounds, turret.position) || !ContainsExpanded(turret.GetComponent<MeshRenderer>().bounds, turret.position))
                    throw new InvalidOperationException("Turret disconnected from its bolted pedestal.");
                foreach (var joint in arm.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith(prefix + "J") && !t.name.Contains("J6_")))
                {
                    var parentMesh = joint.parent.GetComponent<MeshRenderer>(); var childMesh = joint.GetComponent<MeshRenderer>();
                    // J1 connects the pedestal; all downstream joints remain inside both connected part bounds.
                    if (parentMesh && childMesh && (!ContainsExpanded(parentMesh.bounds, joint.position) || !ContainsExpanded(childMesh.bounds, joint.position)))
                        throw new InvalidOperationException("Detached arm joint: " + joint.name);
                }
            }
        }
        if (triangles > 36000) throw new InvalidOperationException("Arm composition exceeds the explicit 36,000 triangle ceiling.");
        if (section.GetComponentsInChildren<Collider>(true).Length != 0 || section.GetComponentsInChildren<NavMeshObstacle>(true).Length != 0 ||
            section.GetComponentsInChildren<Light>(true).Length != 0 || section.GetComponentsInChildren<AudioSource>(true).Length != 0)
            throw new InvalidOperationException("Overhead arm cells must be visual-only with no collision, carving, light or audio.");
        string lines = SightLines(layout, renderers);
        string report = $"BOSS HALL ARMS: 2 assembly roots, 2 owned prefab instances (no nested source prefabs), 4 arms at {ArmScale:F2}x, " +
            $"{renderers.Length} renderers, {triangles} triangles, 6 owned first-principles meshes, 2 reused finishes; no collision/carving/lights/emitters.\n" + lines;
        Debug.Log(report); return report;
    }

    static bool ContainsExpanded(Bounds b, Vector3 p) { b.Expand(.08f); return b.Contains(p); }
    static string SightLines(BossHallFramework.Layout layout, MeshRenderer[] renderers)
    {
        var r = layout.Report; var head = (r.turret.head.min + r.turret.head.max) * .5f;
        bool Blocked(Vector3 a, Vector3 b) { float d = Vector3.Distance(a, b); var ray = new Ray(a, (b - a) / d);
            return renderers.Any(x => x.bounds.IntersectRay(ray, out float hit) && hit >= 0 && hit < d); }
        if (Blocked(r.entryEye, head)) throw new InvalidOperationException("Arms obscure the entry-to-turret head sight line.");
        int eyes = 0, blocked = 0; float eye = BossHallRenderingReview.EyeHeight();
        for (float x = layout.interior.min.x + 1; x < layout.interior.max.x; x += 2)
            for (float z = layout.interior.min.z + 1; z < layout.interior.max.z; z += 2)
            {
                var p = new Vector3(x, r.floorTopY + eye, z); if (Vector3.Distance(p, r.turret.firingOrigin) > r.turret.detectionRange) continue;
                eyes++; if (Blocked(r.turret.firingOrigin, p)) blocked++;
            }
        foreach (var deck in r.deckFootprints.Where(d => d.name.Contains(" gallery:") || d.name.StartsWith("Upper deck network:", StringComparison.Ordinal)))
            for (float x = deck.min.x + .5f; x < deck.max.x; x += 1)
                for (float z = deck.min.z + .5f; z < deck.max.z; z += 1)
                {
                    var p = new Vector3(x, deck.max.y + eye, z); if (Vector3.Distance(p, r.turret.firingOrigin) > r.turret.detectionRange) continue;
                    eyes++; if (Blocked(r.turret.firingOrigin, p)) blocked++;
                }
        if (blocked != 0) throw new InvalidOperationException("Arm renderer bounds cross " + blocked + " turret lines.");
        return "Turret: entry silhouette clear; " + eyes + " lower-floor/tier-1/tier-2 firing rays clear of every arm renderer bound.";
    }

    [MenuItem("Tools/CoreBuster/Build Boss Hall Mechanical Arms")]
    public static void BuildMenu()
    {
        var layout = BossHallFramework.MeasureLayout(); var f = Measure(layout); BuildAssets(f);
        BossHallFramework.RebuildSection(layout, BossHallFramework.Section.MechanicalArms, Owner, t => Populate(t, f)); Validate(layout);
        Debug.Log("Mechanical arms built with Undo. Review and save in Unity; the separate gameplay prerequisite must be satisfied or explicitly waived.");
    }
    [MenuItem("Tools/CoreBuster/Validate Boss Hall Mechanical Arms")]
    public static void ValidateMenu() => Validate(BossHallFramework.MeasureLayout());

    internal static void RebuildForEnergyVerification(BossHallFramework.Layout layout)
    {
        var f = Measure(layout);
        BossHallFramework.RebuildSection(layout, BossHallFramework.Section.MechanicalArms, Owner, t => Populate(t, f));
        Validate(layout);
    }

    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use exclusive batch Editor access.");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); var f = Measure(layout);
        var protectedState = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindSection(BossHallFramework.Section.MechanicalArms));
        var assets = BossHallSnapshot.CaptureAssets();
        BossHallMechanicalArmsReview.NeighbourProof(layout, false);
        BuildAssets(f);
        BossHallFramework.RebuildSection(layout, BossHallFramework.Section.MechanicalArms, Owner, t => Populate(t, f)); Validate(layout);
        VerifyGuards(layout);
        var section = BossHallFramework.FindSection(BossHallFramework.Section.MechanicalArms);
        BossHallFramework.RebuildSection(layout, BossHallFramework.Section.MechanicalArms, Owner, _ => { }); Undo.PerformUndo(); Validate(layout);
        layout = BossHallFramework.SaveAndReload(layout); string summary = Validate(layout);
        BossHallSnapshot.Compare(protectedState, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindSection(BossHallFramework.Section.MechanicalArms)), "Outside Mechanical_Arms after reload");
        BossHallSnapshot.Compare(assets, BossHallSnapshot.CaptureAssets(), "Shared assets after arms");
        var hashes = AssetHashes(); BuildAssets(Measure(layout)); BossHallSnapshot.Compare(hashes, AssetHashes(), "Repeated arm asset hashes/GUIDs");
        string boundary = BossHallMechanicalArmsReview.NeighbourProof(layout, true); BossHallMechanicalArmsReview.Capture(layout);
        BossHallRenderingReview.ValidateNeighbours();
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Arm verification dirtied the saved scene.");
        Directory.CreateDirectory("Docs/BossHall");
        File.WriteAllText("Docs/BossHall/MechanicalArms_006.txt", "corebuster_art_006 | Unity " + Application.unityVersion + " | 2026-10-04\nScene SHA-256: " + BossHallSurvey.Hash(BossHallSurvey.ScenePath) +
            "\nPASS: contract keep-outs, arm/support bounds, source mesh provenance, joint connections, moderate scale, negative guards, isolated clear/Undo, save/reload, repeated asset hashes, " + protectedState.Count +
            " outside-section states, " + assets.Count + " shared asset/meta/settings entries, Centre and Room 3 validators.\n" + summary + "\n" + boundary + "\n" + BossHallFramework.BudgetReport() + "\n");
        Debug.Log("BOSS HALL MECHANICAL ARMS BUILD VERIFIED");
    }

    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); string sha = BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        ValidateMenu(); BossHallRenderingReview.ValidateNeighbours();
        if (SceneManager.GetActiveScene().isDirty || sha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Read-only arm check changed the scene.");
        Debug.Log("BOSS HALL MECHANICAL ARMS RELOAD VERIFIED");
    }

    static void VerifyGuards(BossHallFramework.Layout layout)
    {
        void Reject(Action action, string name) { try { action(); } catch (InvalidOperationException) { Debug.Log("PASS arm guard: " + name); return; } throw new InvalidOperationException("Accepted " + name); }
        var section = BossHallFramework.FindSection(BossHallFramework.Section.MechanicalArms); var root = section.GetChild(0); var p = root.position;
        try { root.position += Vector3.down * 5; Physics.SyncTransforms(); Reject(() => Validate(layout), "arm in upper walking volume"); } finally { root.position = p; }
        var renderer = root.GetComponentInChildren<MeshRenderer>(); var materials = renderer.sharedMaterials;
        try { renderer.sharedMaterial = BossHallRendering.GetFinish("RedOxide"); Reject(() => Validate(layout), "third finish"); } finally { renderer.sharedMaterials = materials; }
        var collider = root.gameObject.AddComponent<BoxCollider>();
        try { Reject(() => Validate(layout), "unreachable collision"); } finally { Object.DestroyImmediate(collider); }
        var arm = root.Find("Arm_A_P_RoboticArm"); var s = arm.localScale;
        try { arm.localScale *= 2; Reject(() => Validate(layout), "enormously scaled source arm"); } finally { arm.localScale = s; }
        Reject(() => BossHallFramework.RebuildSection(layout, BossHallFramework.Section.MechanicalArms, "corebuster_env_049", _ => { }), "wrong section owner");
        Physics.SyncTransforms(); Validate(layout);
    }
}

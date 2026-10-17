using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FPS.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = CentreRoomFramework.Section;

/// <summary>First-principles decorative landmark, owned only by corebuster_env_020.</summary>
public static class CentreRoomReactor
{
    const string Owner = "corebuster_env_020";
    const string Folder = "Assets/FPS/Art/CentreRoom/Reactor";
    const string Heavy = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit/";
    public const string Output = "ReviewCaptures/CentreRoom/020";
    const float Radius = 2.7f;
    const float Height = 8.6f;
    const int Sectors = 32;

    [MenuItem("Tools/CoreBuster/Build Centre Room Reactor")]
    public static void BuildMenu()
    {
        var layout = CentreRoomFramework.MeasureLayout();
        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build Centre Room Reactor");
        try
        {
            Build(layout);
            RequireValid(layout);
            Undo.CollapseUndoOperations(undo);
        }
        catch
        {
            Undo.RevertAllDownToGroup(undo);
            throw;
        }
    }

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        var before = SnapshotOutsideReactor();
        var layout = CentreRoomFramework.MeasureLayout();
        Build(layout);
        RequireValid(layout);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
            throw new InvalidOperationException("Could not save the reactor scene.");
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = CentreRoomFramework.MeasureLayout();
        RequireValid(layout);
        var after = SnapshotOutsideReactor();
        if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out string state) || state != p.Value))
            throw new InvalidOperationException("An object outside the Reactor section changed during the build.");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Measurement.json", JsonUtility.ToJson(layout.report, true));
        Debug.Log($"CENTRE ROOM REACTOR BUILD VERIFIED: saved/reloaded; {before.Count} outside transforms unchanged; " +
            "5.4 m diameter, 8.6 m height, isolated decoration. Player and both-wave Play-mode review still required.");
    }

    [MenuItem("Tools/CoreBuster/Validate Centre Room Reactor")]
    public static void ValidateMenu() => RequireValid(CentreRoomFramework.MeasureLayout());

    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        ValidateMenu();
    }

    public static void BuildAndCaptureBatch()
    {
        BuildBatch();
        CaptureMenu();
    }

    static void Build(CentreRoomFramework.Layout layout)
    {
        if (layout.report.spawns.Count != 8 || layout.report.spawns.Any(s => !s.onNavMesh))
            throw new InvalidOperationException("All eight authoritative spawn markers must be on the loaded NavMesh.");
        // Resolve every dependency before replacing the existing section.
        Material steel = Load<Material>(Heavy + "Materials/M_HeavyPipe_BareSteel.mat");
        Material yellow = Load<Material>(Heavy + "Materials/M_HeavyPipe_HazardYellow.mat");
        GameObject pipe = Load<GameObject>(Heavy + "Prefabs/P_HeavyPipe_Straight_2m.prefab");
        GameObject flange = Load<GameObject>(Heavy + "Prefabs/P_HeavyPipe_Flange.prefab");
        Shader fieldShader = Shader.Find("CoreBuster/Reactor Containment");
        if (!fieldShader) throw new InvalidOperationException("Reactor containment shader is missing.");
        EnsureFolder(Folder);
        EnsureFolder(Folder + "/Meshes");
        EnsureFolder(Folder + "/Materials");
        Material shell = CopyFinish("M_Reactor_Graphite", steel, new Color(.17f, .20f, .23f), .28f, .42f);
        Material armour = CopyFinish("M_Reactor_Ochre", yellow, new Color(.62f, .36f, .08f), .22f, .36f);
        Material trim = CopyFinish("M_Reactor_Steel", steel, new Color(.39f, .44f, .47f), .4f, .5f);
        var (teal, violet, field) = EnergyMaterials(fieldShader);

        Transform section = CentreRoomFramework.BeginSectionRebuild(layout, Section.Reactor, Owner);
        Transform root = Group(section, "Containment_Assembly");
        root.position = layout.RingCenter;
        Transform pedestal = Group(root, "Mechanical_Base");
        // Narrow radial sectors keep the framework's conservative axis-aligned bounds inside its
        // radius-3 envelope without weakening the common keep-out rule for circular meshes.
        // Their convex colliders form a closed 5.4 m polygon; no small trim collider can snag a player.
        for (int i = 0; i < Sectors; i++)
        {
            Mesh mesh = SaveAsset(BaseSector(i), $"Meshes/SM_Base_{i:00}.asset");
            Transform part = MeshPart(pedestal, $"Base_Sector_{i:00}", mesh, i % 4 == 0 ? armour : shell);
            var collision = part.gameObject.AddComponent<MeshCollider>();
            collision.sharedMesh = mesh;
            collision.convex = true;
        }
        Cylinder(pedestal, "Lower_Drum", 1.9f, .95f, 1.85f, shell, true);
        Ring(pedestal, "Lower_Lock_Ring", 1.9f, .10f, 1.43f, trim);
        Ring(pedestal, "Lower_Field_Seat", 1.70f, .075f, 1.98f, violet);
        Cylinder(pedestal, "Emitter_Housing", 1.55f, 1.82f, 2.25f, shell, true);

        Transform frame = Group(root, "Containment_Frame");
        Mesh strutMesh = BlockMesh("SM_Strut", new Vector3(.28f, 4.85f, .34f));
        Mesh shoeMesh = BlockMesh("SM_Anchor", new Vector3(.42f, .62f, .56f));
        Mesh ribMesh = BlockMesh("SM_Base_Rib", new Vector3(.12f, .67f, .44f));
        Mesh ventMesh = BlockMesh("SM_Vent", new Vector3(.035f, .27f, .035f));
        for (int i = 0; i < 16; i++)
        {
            float angle = i * 22.5f;
            Transform rib = MeshPart(pedestal, $"Base_Rib_{i:00}", ribMesh, trim);
            PlaceRadial(rib, angle, 2.35f, .65f);
            for (int j = 0; j < 3; j++)
            {
                Transform vent = MeshPart(pedestal, $"Vent_{i:00}_{j}", ventMesh, shell);
                PlaceRadial(vent, angle + (j - 1) * 2.8f, 2.51f, .68f);
                vent.localRotation *= Quaternion.Euler(-21.4f, 0, 0);
            }
        }
        for (int i = 0; i < 6; i++)
        {
            float angle = i * 60;
            Transform strut = MeshPart(frame, $"Containment_Column_{i}", strutMesh, i % 2 == 0 ? armour : trim);
            PlaceRadial(strut, angle, 1.62f, 4.475f);
            strut.gameObject.AddComponent<BoxCollider>();
            foreach (float y in new[] { 1.92f, 6.95f })
            {
                Transform shoe = MeshPart(frame, $"Column_Anchor_{i}_{y:F2}", shoeMesh, shell);
                PlaceRadial(shoe, angle, 1.62f, y);
            }
        }
        Ring(frame, "Lower_Containment_Hoop", 1.64f, .13f, 2.3f, trim);
        Ring(frame, "Upper_Containment_Hoop", 1.64f, .13f, 6.7f, trim);

        Transform crown = Group(root, "Collector_Crown");
        Cylinder(crown, "Collector_Drum", 1.88f, 6.85f, 7.4f, shell, true);
        Ring(crown, "Collector_Flange", 2.02f, .12f, 7.42f, trim);
        Cylinder(crown, "Armoured_Cap", 1.68f, 7.48f, 7.91f, armour, true);
        Ring(crown, "Collector_Indicator", 1.69f, .035f, 7.7f, teal);
        Cylinder(crown, "Upper_Stack", 1.24f, 7.91f, 8.6f, shell, true);
        for (int i = 0; i < 4; i++) Ring(crown, $"Cooling_Fin_{i}", 1.38f, .065f, 8.02f + i * .16f, trim);

        Transform plumbing = Group(root, "Coolant_Pipes");
        for (int i = 0; i < 4; i++)
        {
            Transform riser = Group(plumbing, "Coolant_Riser_" + i);
            PlaceRadial(riser, 45 + i * 90, 1.96f, 1.35f);
            // The kit straight runs along +Z from Port_A to Port_B. Turn it up; keep its metre scale.
            PrefabPart(riser, pipe, "Pipe", Vector3.zero, Quaternion.Euler(-90, 0, 0), trim);
            PrefabPart(riser, flange, "Flange_Lower", Vector3.zero, Quaternion.Euler(-90, 0, 0), armour);
            PrefabPart(riser, flange, "Flange_Upper", Vector3.up * 2, Quaternion.Euler(-90, 0, 0), trim);
        }

        Transform energy = Group(root, "Decorative_Energy");
        var fieldMesh = new ProceduralMeshBuilder();
        fieldMesh.AddCylinder(Vector3.zero, Vector3.up * 4.4f, 1.12f, 1.12f, 64, false);
        MeshPart(energy, "Contained_Field", SaveAsset(fieldMesh.ToMesh("SM_Contained_Field"), "Meshes/SM_Contained_Field.asset"), field)
            .localPosition = Vector3.up * 2.3f;
        Cylinder(energy, "Magnetic_Spine", .17f, 2.3f, 6.7f, shell, false);
        for (int i = 0; i < 7; i++)
            Ring(energy, $"Induction_Ring_{i}", .64f, .025f, 2.52f + i * .65f, i % 2 == 0 ? violet : teal);

        var obstacle = root.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Capsule;
        // Set all extents together: in 6000.5.7f1 the height/radius setter sequence
        // read back half the requested height. Size matches the Inspector's stored extents.
        obstacle.size = new Vector3(Radius * 2, Height, Radius * 2);
        obstacle.center = Vector3.up * (Height * .5f);
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        var flags = GameObjectUtility.GetStaticEditorFlags(section.parent.parent.gameObject);
        foreach (Transform t in root.GetComponentsInChildren<Transform>())
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        // Animated emissive geometry is neither an occluder nor a source for baked GI.
        foreach (Transform t in energy.GetComponentsInChildren<Transform>())
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags & ~(StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic));
        foreach (Renderer r in energy.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        AssetDatabase.SaveAssets();
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // corebuster_render_008 palette. The asset names keep their original GUIDs and filenames:
    // "Teal" is now the ice-blue ring and "Violet" the deep-blue ring.
    static (Material teal, Material violet, Material field) EnergyMaterials(Shader fieldShader)
    {
        Material teal = Emission("M_Reactor_Teal", new Color(.18f, .62f, 1f), 2.6f);
        Material violet = Emission("M_Reactor_Violet", new Color(.08f, .28f, 1f), 2.6f);
        var fieldSource = new Material(fieldShader);
        fieldSource.SetColor("_Color", new Color(.08f, .32f, 1f));
        fieldSource.SetColor("_Accent", new Color(.3f, .75f, 1f));
        fieldSource.SetColor("_Core", new Color(.78f, .9f, 1f));
        fieldSource.SetFloat("_Intensity", 2.2f);
        fieldSource.SetFloat(ReactorInstabilityLight.InstabilityProperty, 1f);
        fieldSource.SetFloat("_ReviewTime", -1f);
        return (teal, violet, SaveAsset(fieldSource, "Materials/M_Reactor_Field.mat"));
    }

    /// <summary>
    /// Rewrites only the three energy material assets, leaving the saved Reactor section untouched.
    /// The lighting build must follow, because the rings render through its lit copies.
    /// </summary>
    public static Material RefreshEnergyMaterials()
    {
        Shader fieldShader = Shader.Find("CoreBuster/Reactor Containment");
        if (!fieldShader) throw new InvalidOperationException("Reactor containment shader is missing.");
        EnsureFolder(Folder + "/Materials");
        Material field = EnergyMaterials(fieldShader).field;
        AssetDatabase.SaveAssets();
        return field;
    }

    public static Material FieldMaterial => Load<Material>(Folder + "/Materials/M_Reactor_Field.mat");

    static Mesh BaseSector(int index)
    {
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        // Closed convex wedge, with a broad foot and tapered industrial skirt. Each face has its own normals.
        Vector2[] profile = { new Vector2(0, 0), new Vector2(Radius, 0), new Vector2(Radius, .18f),
            new Vector2(2.32f, 1.15f), new Vector2(0, 1.15f) };
        float a = index * Mathf.PI * 2 / Sectors, b = (index + 1) * Mathf.PI * 2 / Sectors;
        Vector3 Point(Vector2 p, float angle) => new Vector3(Mathf.Cos(angle) * p.x, p.y, Mathf.Sin(angle) * p.x);
        void Face(Vector3 p, Vector3 q, Vector3 r, Vector3 s)
        {
            int start = vertices.Count;
            vertices.AddRange(new[] { p, q, r, s });
            uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        for (int j = 0; j < profile.Length - 1; j++)
            Face(Point(profile[j], a), Point(profile[j + 1], a), Point(profile[j + 1], b), Point(profile[j], b));
        // End faces use a fan; reverse the far face so convex-collider cooking sees a closed solid.
        for (int j = 1; j < profile.Length - 1; j++)
        {
            Face(Point(profile[0], a), Point(profile[j + 1], a), Point(profile[j], a), Point(profile[j], a));
            Face(Point(profile[0], b), Point(profile[j], b), Point(profile[j + 1], b), Point(profile[j + 1], b));
        }
        var mesh = new Mesh { name = $"SM_Base_{index:00}" };
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        return mesh;
    }

    internal static void RequireValid(CentreRoomFramework.Layout layout)
    {
        var errors = new List<string>();
        Scene scene = SceneManager.GetActiveScene();
        Transform section = CentreRoomFramework.FindSection(scene, Section.Reactor);
        Transform root = section ? section.Find("Containment_Assembly") : null;
        if (!root || section.childCount != 1) errors.Add("Expected exactly one Containment_Assembly.");
        if (root)
        {
            if (Vector3.Distance(root.position, layout.RingCenter) > .01f || root.localScale != Vector3.one || root.rotation != Quaternion.identity)
                errors.Add("The assembly moved from the measured reactor centre or changed scale/rotation.");
            foreach (string group in new[] { "Mechanical_Base", "Containment_Frame", "Collector_Crown", "Coolant_Pipes", "Decorative_Energy" })
                if (!root.Find(group)) errors.Add("Missing " + group);
            var obstacle = root.GetComponent<NavMeshObstacle>();
            if (root.GetComponentsInChildren<NavMeshObstacle>(true).Length != 1 || !obstacle || !obstacle.enabled || !obstacle.carving ||
                obstacle.shape != NavMeshObstacleShape.Capsule || Mathf.Abs(obstacle.radius - Radius) > .001f ||
                Vector3.Distance(obstacle.size, new Vector3(Radius * 2, Height, Radius * 2)) > .001f || obstacle.center != Vector3.up * (Height * .5f))
                errors.Add("Expected one enabled radius-2.7 m cylindrical carving obstacle; actual " +
                    (obstacle ? $"shape={obstacle.shape}, radius={obstacle.radius}, size={obstacle.size}, center={obstacle.center}, enabled={obstacle.enabled}, carving={obstacle.carving}" : "missing"));
            // A strict decoration allowlist also excludes UnstableCore, Health, CoreExecutioner,
            // target-registration scripts, animation behaviours and missing scripts, without assembly coupling.
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
                if (!(c is Transform || c is MeshFilter || c is MeshRenderer || c is Collider || c is NavMeshObstacle))
                    errors.Add("Unexpected runtime component: " + (c ? c.GetType().Name : "missing script"));
            if (root.GetComponentsInChildren<MeshCollider>().Count(c => c.name.StartsWith("Base_Sector_")) != Sectors)
                errors.Add("The closed base needs all 32 collision sectors.");
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) errors.Add(r.name + " is disabled.");
                foreach (Material m in r.sharedMaterials)
                    if (!m || !m.shader || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m)) || ShaderUtil.ShaderHasError(m.shader))
                        errors.Add(r.name + " has a missing, transient or broken material/shader.");
                var filter = r.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) { errors.Add(r.name + " has no mesh."); continue; }
                foreach (Vector3 v in filter.sharedMesh.vertices)
                {
                    Vector3 p = root.InverseTransformPoint(filter.transform.TransformPoint(v));
                    if (new Vector2(p.x, p.z).magnitude > Radius + .025f || p.y < -.025f || p.y > Height + .025f)
                    { errors.Add(r.name + " exceeds the final cylindrical footprint/height."); break; }
                }
            }
            var energy = root.Find("Decorative_Energy");
            if (energy && energy.GetComponentsInChildren<Collider>(true).Length != 0) errors.Add("Energy must have no collision.");
            if (renderers.Length == 0) errors.Add("Reactor has no visible geometry.");
        }
        if (layout.report.spawns.Count != 8 || layout.report.spawns.Any(s => !s.onNavMesh)) errors.Add("A spawn left the NavMesh.");
        if (layout.report.ringBodyBlocked != 0 || layout.report.ringNavHits != layout.report.ringSamples)
            errors.Add("The surveyed circulation ring is obstructed or missing NavMesh.");
        if (!layout.report.terminalStandingClear || !layout.report.terminalVisible) errors.Add("Terminal access is blocked.");
        if (!CentreRoomFramework.Validate(scene, layout, true)) errors.Add("Framework checks failed.");
        if (!CentreRoomPerimeter.Validate(scene, layout, true)) errors.Add("Perimeter integration checks failed.");
        if (errors.Count > 0) throw new InvalidOperationException("CENTRE ROOM REACTOR CHECKS FAILED:\n" + string.Join("\n", errors));
        Debug.Log($"CENTRE ROOM REACTOR CHECKS PASSED: centre {layout.RingCenter:F3}; diameter {2 * Radius:F1}, height {Height:F1}; " +
            "owned geometry/materials, collision/carving, eight spawns, ring, terminal and perimeter; no gameplay components.");
    }

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create reactor");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static Transform MeshPart(Transform parent, string name, Mesh mesh, Material material)
    {
        Transform t = Group(parent, name);
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        return t;
    }

    static Mesh BlockMesh(string name, Vector3 size)
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddBlock(Vector3.zero, size);
        return SaveAsset(builder.ToMesh(name), "Meshes/" + name + ".asset");
    }

    static void Cylinder(Transform parent, string name, float radius, float bottom, float top, Material material, bool collider)
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddCylinder(Vector3.up * bottom, Vector3.up * top, radius, radius, 48, true);
        Transform t = MeshPart(parent, name, SaveAsset(builder.ToMesh("SM_" + name), "Meshes/SM_" + name + ".asset"), material);
        if (collider)
        {
            var c = t.gameObject.AddComponent<MeshCollider>();
            c.sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
            c.convex = true;
        }
    }

    static void Ring(Transform parent, string name, float radius, float thickness, float y, Material material)
    {
        var builder = new ProceduralMeshBuilder();
        builder.AddTorus(Vector3.up * y, Vector3.up, radius, thickness, 48, 8);
        MeshPart(parent, name, SaveAsset(builder.ToMesh("SM_" + name), "Meshes/SM_" + name + ".asset"), material);
    }

    static void PlaceRadial(Transform t, float degrees, float radius, float y)
    {
        Quaternion rotation = Quaternion.Euler(0, degrees, 0);
        t.localPosition = rotation * (Vector3.forward * radius) + Vector3.up * y;
        t.localRotation = rotation;
    }

    static void PrefabPart(Transform parent, GameObject prefab, string name, Vector3 position, Quaternion rotation, Material material)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(go, "Create reactor pipe");
        // Unpack only the instance: remove kit box collision and retain the exact original mesh geometry.
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = name; go.transform.localPosition = position; go.transform.localRotation = rotation;
        foreach (Collider c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = material;
    }

    static Material CopyFinish(string name, Material source, Color color, float metallic, float smoothness)
    {
        var m = new Material(source) { name = name };
        m.SetColor("_BaseColor", color); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness);
        return SaveAsset(m, "Materials/" + name + ".mat");
    }

    static Material Emission(string name, Color color, float strength)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", color * .35f); m.SetColor("_EmissionColor", color * strength);
        m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return SaveAsset(m, "Materials/" + name + ".mat");
    }

    static T SaveAsset<T>(T generated, string relative) where T : Object
    {
        string path = Folder + "/" + relative;
        generated.name = Path.GetFileNameWithoutExtension(relative);
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing) { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
        else { AssetDatabase.CreateAsset(generated, path); existing = generated; }
        CentreRoomFramework.RequireOwnedAsset(existing, Section.Reactor);
        return existing;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
    }

    static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!asset) throw new InvalidOperationException("Missing reactor dependency: " + path);
        return asset;
    }

    static Dictionary<string, string> SnapshotOutsideReactor()
    {
        Scene scene = SceneManager.GetActiveScene();
        Transform reactor = CentreRoomFramework.FindSection(scene, Section.Reactor);
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => t == reactor || !t.IsChildOf(reactor))
            .ToDictionary(t => GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), t =>
            {
                var renderer = t.GetComponent<Renderer>();
                string materials = renderer ? string.Join(",", renderer.sharedMaterials.Select(m => m ? AssetDatabase.GetAssetPath(m) : "null")) : "";
                return FormattableString.Invariant($"{t.name}|{(t.parent ? GlobalObjectId.GetGlobalObjectIdSlow(t.parent).ToString() : "root")}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}|{t.gameObject.activeSelf}|{materials}");
            });
    }

    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        CaptureMenu();
    }

    [MenuItem("Tools/CoreBuster/Capture Centre Room Reactor")]
    public static void CaptureMenu()
    {
        var layout = CentreRoomFramework.MeasureLayout();
        RequireValid(layout);
        Vector3 c = layout.RingCenter;
        var views = new[]
        {
            ("South_Entry", new Vector3(-191.955f, layout.Floor + 1.7f, layout.interior.min.z + 1.4f), c + Vector3.up * 3.4f),
            ("North_Approach", c + new Vector3(5, 1.7f, 12), c + Vector3.up * 3.4f),
            ("West_Approach", c + new Vector3(-12, 1.7f, -5), c + Vector3.up * 3.4f),
            ("Containment_Detail", c + new Vector3(5.5f, 2.6f, -6), c + Vector3.up * 4),
            ("Circulation_Overview", c + new Vector3(9, 9.8f, -11), c + Vector3.up * 1.7f),
        };
        Directory.CreateDirectory(Output);
        var go = new GameObject("Reactor review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 65; camera.nearClipPlane = .05f; camera.farClipPlane = 200;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        camera.GetUniversalAdditionalCameraData().volumeLayerMask = ~0;
        var previewLights = new List<GameObject>();
        try
        {
            for (int capture = 0; capture < views.Length + 1; capture++)
            {
                bool materialPreview = capture == views.Length;
                var view = materialPreview ? views[3] : views[capture];
                if (materialPreview)
                {
                    // Diagnostic-only illumination exposes workmanship before env_026 lights the room.
                    // This extra image is explicitly not evidence of saved-scene/Game-view lighting.
                    foreach (var lightSetup in new[] { (new Vector3(35, -35, 0), 1.8f), (new Vector3(35, 145, 0), .9f) })
                    {
                        var lightGo = new GameObject("Temporary material preview light") { hideFlags = HideFlags.HideAndDontSave };
                        previewLights.Add(lightGo);
                        lightGo.transform.rotation = Quaternion.Euler(lightSetup.Item1);
                        var light = lightGo.AddComponent<Light>();
                        light.type = LightType.Directional; light.intensity = lightSetup.Item2;
                        light.color = Color.white; light.shadows = LightShadows.None;
                    }
                }
                camera.transform.SetPositionAndRotation(view.Item2, Quaternion.LookRotation(view.Item3 - view.Item2));
                var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                try
                {
                    camera.targetTexture = rt; camera.Render(); camera.Render();
                    RenderTexture.active = rt;
                    texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                    File.WriteAllBytes(Output + "/" + (materialPreview ? "Material_Preview_Only" : view.Item1) + ".png", texture.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null; RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture);
                }
            }
        }
        finally
        {
            foreach (var light in previewLights) Object.DestroyImmediate(light);
            Object.DestroyImmediate(go);
        }
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Capture dirtied the scene; do not save.");
        Debug.Log("Centre Room reactor review captures: " + Output);
    }
}

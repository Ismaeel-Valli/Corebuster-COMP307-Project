using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Rebuildable corebuster_env_014 floor detail pass: flush overlays on the Start Workshop floor carrying yellow dashed
/// zone safety lines, floor grates and vent grilles, drain covers, oil stains, and a "START HERE / GO FURTHER" chevron
/// stencil at spawn. Built under Level/Start_Workshop in group "Floor_Detail_014".
/// All overlays are collider-free quads with project-owned textures and materials.
/// </summary>
public static class StartWorkshopFloor
{
    public const string GroupName = "Floor_Detail_014";
    const string UndoName = "Detail Start Workshop Floor";
    const string Art = "Assets/FPS/Art/";
    const string Textures = Art + "Textures/StartWorkshop/";
    const string Materials = Art + "Materials/Props/";

    public const string StencilName = "Spawn_Start_Here_Stencil";

    [MenuItem("Tools/CoreBuster/Detail Start Workshop Floor")]
    public static void Detail()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var level = scene.path == StartWorkshopDresser.ScenePath ? StartWorkshopDresser.FindLevel(scene) : null;
        var root = level != null ? level.Find(StartWorkshopDresser.ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"Open {StartWorkshopDresser.ScenePath} and run Tools/CoreBuster/Dress Start Workshop first.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        RebuildIn(scene, root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root.Find(GroupName);
        Debug.Log("Start Workshop floor details rebuilt.");
        StartWorkshopChecks.Run();
    }

    public static void BuildBatch()
    {
        var scene = EditorSceneManager.OpenScene(StartWorkshopDresser.ScenePath, OpenSceneMode.Single);
        var level = StartWorkshopDresser.FindLevel(scene);
        var root = level != null ? level.Find(StartWorkshopDresser.ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"No {StartWorkshopDresser.LevelName}/{StartWorkshopDresser.ContainerName} in {StartWorkshopDresser.ScenePath}; run Tools/CoreBuster/Dress Start Workshop first.");
            EditorApplication.Exit(1);
            return;
        }
        RebuildIn(scene, root);
        EditorSceneManager.SaveScene(scene);
        StartWorkshopChecks.Run();
    }

    static void RebuildIn(Scene scene, Transform root)
    {
        var f = StartWorkshopFrame.Measure(scene, root);
        Build(root, f);
        var level = root.parent;
        var flags = GameObjectUtility.GetStaticEditorFlags(level.gameObject);
        var group = root.Find(GroupName);
        if (group != null) StartWorkshopDresser.ApplyStaticConvention(group, flags);
        StartWorkshopLitMaterials.ApplyTo(scene, root);
    }

    public static void Build(Transform root, StartWorkshopFrame f)
    {
        var existing = root.Find(GroupName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        var group = new GameObject(GroupName).transform;
        group.SetParent(root, false);
        group.position = new Vector3(f.Center.x, f.Floor, f.Center.z);

        // Project-owned materials
        var matStartHere = MaterialFor("Floor_Start_Here", true);
        var matDashedLine = MaterialFor("Floor_Dashed_Line", true);
        var matDashedCorner = MaterialFor("Floor_Dashed_Corner", true);
        var matGrateLong = MaterialFor("Floor_Grate_Long", true);
        var matVentGrille = MaterialFor("Floor_Vent_Grille", true);
        var matDrainSquare = MaterialFor("Floor_Drain_Square", true);
        var matOilLarge = MaterialFor("Floor_Oil_Stain_Large", true);
        var matOilMed = MaterialFor("Floor_Oil_Stain_Medium", true);
        var matOilSmall = MaterialFor("Floor_Oil_Stain_Small", true);

        // Tiered Y offsets above floor to prevent z-fighting and give clean depth sorting
        float yLine = f.Floor + 0.020f;
        float yStencil = f.Floor + 0.021f;
        float yGrate = f.Floor + 0.022f;
        float yDrain = f.Floor + 0.022f;
        float yOil = f.Floor + 0.024f;

        // 1. Spawn Stencil: "START HERE / GO FURTHER" double chevron pointing along route to exit
        Vector3 route = f.RouteEnd - f.Spawn;
        route.y = 0;
        Vector3 routeDir = route.magnitude > 0.01f ? route.normalized : Vector3.forward;
        Vector3 stencilPos = f.Spawn + routeDir * 1.5f;
        stencilPos.y = yStencil;
        FloorQuad(group, StencilName, stencilPos,
            Quaternion.LookRotation(Vector3.down, routeDir),
            new Vector2(2.2f, 2.2f), matStartHere);

        // 2. Dashed perimeter safety lines around each work zone and along walls
        BuildSafetyLines(group, f, yLine, matDashedLine, matDashedCorner);

        // 3. Floor grates and vent grilles
        BuildGratesAndVents(group, f, yGrate, matGrateLong, matVentGrille);

        // 4. Drain covers
        BuildDrains(group, f, yDrain, matDrainSquare);

        // 5. Oil stains and machine drips
        BuildOilStains(group, f, yOil, matOilLarge, matOilMed, matOilSmall);

        Undo.RegisterCreatedObjectUndo(group.gameObject, UndoName);
    }

    static void BuildSafetyLines(Transform parent, StartWorkshopFrame f, float y, Material matLine, Material matCorner)
    {
        var linesGroup = new GameObject("Zone_Safety_Lines").transform;
        linesGroup.SetParent(parent, false);

        // West Storage Zone: North-South perimeter line at X = -182.8f from Z = 5.0f to Z = 30.5f
        float westX = -182.8f;
        for (float z = 7.0f; z <= 27.5f; z += 4.0f)
        {
            LongQuad(linesGroup, $"Line_West_{z:F0}", new Vector3(westX, y, z), Vector3.forward, 4.0f, 0.4f, matLine);
        }
        LongQuad(linesGroup, "Line_West_NorthEnd", new Vector3(westX, y, 29.5f), Vector3.forward, 2.0f, 0.4f, matLine);

        // Corner returns for West zone:
        FloorQuad(linesGroup, "Corner_West_North", new Vector3(westX, y, 30.5f),
            Quaternion.Euler(90f, 0f, 0f), new Vector2(0.4f, 0.4f), matCorner);
        LongQuad(linesGroup, "Line_West_NorthReturn", new Vector3(-184.0f, y, 30.5f), Vector3.right, 2.0f, 0.4f, matLine);

        FloorQuad(linesGroup, "Corner_West_South", new Vector3(westX, y, 5.0f),
            Quaternion.Euler(90f, 270f, 0f), new Vector2(0.4f, 0.4f), matCorner);
        LongQuad(linesGroup, "Line_West_SouthReturn", new Vector3(-184.0f, y, 5.0f), Vector3.right, 2.0f, 0.4f, matLine);

        // East Hero Workstation Zone: North-South line at X = -158.0f from Z = 3.5f to Z = 25.0f
        float eastX = -158.0f;
        for (float z = 5.5f; z <= 22.0f; z += 4.0f)
        {
            LongQuad(linesGroup, $"Line_East_{z:F0}", new Vector3(eastX, y, z), Vector3.forward, 4.0f, 0.4f, matLine);
        }
        LongQuad(linesGroup, "Line_East_NorthEnd", new Vector3(eastX, y, 24.0f), Vector3.forward, 2.0f, 0.4f, matLine);

        // Corner returns for East zone:
        FloorQuad(linesGroup, "Corner_East_North", new Vector3(eastX, y, 25.0f),
            Quaternion.Euler(90f, 90f, 0f), new Vector2(0.4f, 0.4f), matCorner);
        LongQuad(linesGroup, "Line_East_NorthReturn", new Vector3(-156.5f, y, 25.0f), Vector3.right, 2.4f, 0.4f, matLine);

        FloorQuad(linesGroup, "Corner_East_South", new Vector3(eastX, y, 3.5f),
            Quaternion.Euler(90f, 180f, 0f), new Vector2(0.4f, 0.4f), matCorner);
        LongQuad(linesGroup, "Line_East_SouthReturn", new Vector3(-156.5f, y, 3.5f), Vector3.right, 2.4f, 0.4f, matLine);

        // South Rear Wall Services perimeter (East-West at Z = 5.0f)
        LongQuad(linesGroup, "Line_South_WestSec", new Vector3(-179.0f, y, 5.0f), Vector3.right, 4.0f, 0.4f, matLine);
        LongQuad(linesGroup, "Line_South_EastSec", new Vector3(-162.0f, y, 5.0f), Vector3.right, 4.0f, 0.4f, matLine);

        // Walkway guides either side of the spawn-to-exit route, so zone lines read from the spawn itself. They sit just
        // outside the stencil and stop short of the exit flank guides below.
        Vector3 route = f.RouteEnd - f.Spawn;
        route.y = 0;
        Vector3 dir = route.normalized, side = new Vector3(dir.z, 0, -dir.x);
        for (float d = 5.0f; d <= 11.0f; d += 4.0f)
            foreach (var (tag, sign) in new[] { ("West", -1f), ("East", 1f) })
                LongQuad(linesGroup, $"Line_Route_{tag}_{d:F0}", f.Spawn + dir * d + side * (sign * 1.9f) + Vector3.up * (y - f.Floor),
                    dir, 4.0f, 0.4f, matLine);

        // North Exit Approach Flanking Guides (guiding towards exit doorway at X ~ -163.9f, Z = 34.4f)
        LongQuad(linesGroup, "Line_Exit_Flank_West", new Vector3(-167.2f, y, 31.8f), Vector3.forward, 3.8f, 0.4f, matLine);
        LongQuad(linesGroup, "Line_Exit_Flank_East", new Vector3(-160.6f, y, 31.8f), Vector3.forward, 3.8f, 0.4f, matLine);
    }

    static void BuildGratesAndVents(Transform parent, StartWorkshopFrame f, float y, Material matGrate, Material matVent)
    {
        var gratesGroup = new GameObject("Grates_And_Vents").transform;
        gratesGroup.SetParent(parent, false);

        // Long Grates
        LongQuad(gratesGroup, "Grate_West_Bay", new Vector3(-181.8f, y, 17.5f), Vector3.forward, 3.0f, 0.75f, matGrate);

        LongQuad(gratesGroup, "Grate_East_Workstation", new Vector3(-158.8f, y, 6.8f), Vector3.forward, 3.0f, 0.75f, matGrate);

        LongQuad(gratesGroup, "Grate_South_Services", new Vector3(-174.0f, y, 4.6f), Vector3.right, 2.8f, 0.7f, matGrate);

        LongQuad(gratesGroup, "Grate_Maintenance_Bay", new Vector3(-175.5f, y, 21.8f), Vector3.right, 2.4f, 0.65f, matGrate);

        // Square Vent Grilles (1.2m x 1.2m)
        FloorQuad(gratesGroup, "Vent_North_Exit", new Vector3(-168.0f, y, 29.5f),
            Quaternion.Euler(90f, 0f, 0f), new Vector2(1.2f, 1.2f), matVent);

        FloorQuad(gratesGroup, "Vent_West_Storage", new Vector3(-181.5f, y, 27.5f),
            Quaternion.Euler(90f, 0f, 0f), new Vector2(1.2f, 1.2f), matVent);

        FloorQuad(gratesGroup, "Vent_East_Maintenance", new Vector3(-159.2f, y, 16.5f),
            Quaternion.Euler(90f, 0f, 0f), new Vector2(1.2f, 1.2f), matVent);
    }

    static void BuildDrains(Transform parent, StartWorkshopFrame f, float y, Material matDrain)
    {
        var drainsGroup = new GameObject("Drain_Covers").transform;
        drainsGroup.SetParent(parent, false);

        // Square drain covers (0.8m x 0.8m)
        FloorQuad(drainsGroup, "Drain_South_Valves", new Vector3(-169.5f, y, 4.3f),
            Quaternion.Euler(90f, 0f, 0f), new Vector2(0.8f, 0.8f), matDrain);

        FloorQuad(drainsGroup, "Drain_East_Bench", new Vector3(-157.8f, y, 10.2f),
            Quaternion.Euler(90f, 0f, 0f), new Vector2(0.8f, 0.8f), matDrain);

        FloorQuad(drainsGroup, "Drain_West_Barrels", new Vector3(-183.2f, y, 11.5f),
            Quaternion.Euler(90f, 0f, 0f), new Vector2(0.8f, 0.8f), matDrain);

        FloorQuad(drainsGroup, "Drain_NorthWest_Bay", new Vector3(-179.8f, y, 28.5f),
            Quaternion.Euler(90f, 0f, 0f), new Vector2(0.8f, 0.8f), matDrain);

        FloorQuad(drainsGroup, "Drain_SouthEast_Stack", new Vector3(-159.0f, y, 4.5f),
            Quaternion.Euler(90f, 0f, 0f), new Vector2(0.8f, 0.8f), matDrain);
    }

    static void BuildOilStains(Transform parent, StartWorkshopFrame f, float y, Material matLarge, Material matMed, Material matSmall)
    {
        var stainsGroup = new GameObject("Oil_Stains").transform;
        stainsGroup.SetParent(parent, false);

        // Large puddles
        FloorQuad(stainsGroup, "Oil_Stain_Large_Maintenance", new Vector3(-175.2f, y, 24.8f),
            Quaternion.Euler(90f, 25f, 0f), new Vector2(2.4f, 2.4f), matLarge);

        FloorQuad(stainsGroup, "Oil_Stain_Large_SouthDrop", new Vector3(-168.8f, y, 4.5f),
            Quaternion.Euler(90f, -40f, 0f), new Vector2(2.0f, 2.0f), matLarge);

        // Medium stains
        FloorQuad(stainsGroup, "Oil_Stain_Med_RobotArm", new Vector3(-157.6f, y, 4.2f),
            Quaternion.Euler(90f, 15f, 0f), new Vector2(1.3f, 1.3f), matMed);

        FloorQuad(stainsGroup, "Oil_Stain_Med_EastCart", new Vector3(-158.2f, y, 12.2f),
            Quaternion.Euler(90f, 80f, 0f), new Vector2(1.1f, 1.1f), matMed);

        FloorQuad(stainsGroup, "Oil_Stain_Med_WestBarrels", new Vector3(-183.0f, y, 9.8f),
            Quaternion.Euler(90f, -15f, 0f), new Vector2(1.4f, 1.4f), matMed);

        // Small stains & drip clusters
        FloorQuad(stainsGroup, "Oil_Stain_Small_Trolley", new Vector3(-173.2f, y, 22.2f),
            Quaternion.Euler(90f, 45f, 0f), new Vector2(0.8f, 0.8f), matSmall);

        FloorQuad(stainsGroup, "Oil_Stain_Small_Sorting", new Vector3(-177.2f, y, 8.8f),
            Quaternion.Euler(90f, -60f, 0f), new Vector2(0.7f, 0.7f), matSmall);

        FloorQuad(stainsGroup, "Oil_Stain_Small_Drain", new Vector3(-170.2f, y, 4.8f),
            Quaternion.Euler(90f, 110f, 0f), new Vector2(0.75f, 0.75f), matSmall);
    }

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        var group = root.Find(GroupName);
        if (group == null)
        {
            errors.Add($"Missing {GroupName}; run Tools/CoreBuster/Detail Start Workshop Floor.");
            return;
        }

        var stencil = group.Find(StencilName);
        if (stencil == null)
        {
            errors.Add($"Missing {StencilName} in {GroupName}.");
        }
        else
        {
            Bounds sb = StartWorkshopDresser.B(stencil.gameObject);
            if (sb.min.y < f.Floor - 0.01f || sb.max.y > f.Floor + 0.05f)
                errors.Add($"{StencilName} is not flush with the workshop floor.");
            if (StartWorkshopFrame.FlatDistance(f.Spawn, sb) > 5.0f)
                errors.Add($"{StencilName} is too far from the spawn location.");
        }

        int lines = 0, grates = 0, vents = 0, drains = 0, stains = 0;
        foreach (var r in group.GetComponentsInChildren<MeshRenderer>())
        {
            var t = r.transform;
            if (t.GetComponent<Collider>() != null)
                errors.Add($"{t.name} in {GroupName} must be collider-free.");

            Bounds b = r.bounds;
            if (b.min.y < f.Floor - 0.01f || b.max.y > f.Floor + 0.06f)
                errors.Add($"{t.name} overlay is not flush with the floor ({b.min.y:F3} to {b.max.y:F3}).");

            if (b.min.x < f.West - 0.5f || b.max.x > f.East + 0.5f ||
                b.min.z < f.South - 0.5f || b.max.z > f.North + 0.5f)
                errors.Add($"{t.name} extends outside the room boundaries.");

            if (t.name.StartsWith("Line_") || t.name.StartsWith("Corner_")) lines++;
            else if (t.name.StartsWith("Grate_")) grates++;
            else if (t.name.StartsWith("Vent_")) vents++;
            else if (t.name.StartsWith("Drain_")) drains++;
            else if (t.name.StartsWith("Oil_")) stains++;
        }

        if (lines < 8) errors.Add($"Only {lines} safety line overlays found; expected at least 8.");
        if (grates < 3) errors.Add($"Only {grates} floor grates found; expected at least 3.");
        if (vents < 2) errors.Add($"Only {vents} vent grilles found; expected at least 2.");
        if (drains < 3) errors.Add($"Only {drains} drain covers found; expected at least 3.");
        if (stains < 5) errors.Add($"Only {stains} oil stains found; expected at least 5.");
    }

    /// <summary>A flush strip whose texture's long (U) axis runs along <paramref name="along"/> for <paramref name="length"/> metres.</summary>
    static GameObject LongQuad(Transform parent, string name, Vector3 position, Vector3 along, float length, float width, Material material)
    {
        float yaw = Mathf.Atan2(-along.z, along.x) * Mathf.Rad2Deg;
        return FloorQuad(parent, name, position, Quaternion.Euler(90f, yaw, 0f), new Vector2(length, width), material);
    }

    static GameObject FloorQuad(Transform parent, string name, Vector3 position, Quaternion rotation, Vector2 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var collider = go.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        Undo.RegisterCreatedObjectUndo(go, UndoName);
        return go;
    }

    static Material MaterialFor(string name, bool transparent)
    {
        string texturePath = Textures + name + ".png";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null) throw new InvalidOperationException("Missing project-owned texture: " + texturePath);
        string materialPath = Materials + "M_" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader is unavailable.");
            material = new Material(shader) { name = "M_" + name };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        if (transparent)
        {
            material.SetFloat("_Surface", 1);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
        EditorUtility.SetDirty(material);
        return material;
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// corebuster_render_011. Puts the corridors into lockdown: dark and grey, lit only by their wall lamps, with the lamps
/// on each route glowing in the colour of the keycard that opens the door at its end.
/// <list type="bullet">
/// <item>The corridor kit ("Plane (1)") used URP's 2D Mesh2D-Lit shader, which the 3D renderer draws full-bright and
/// unlit. Its five pack materials are copied to <c>Assets/FPS/Art/Corridors/Lockdown/</c>, converted to URP/Lit and
/// assigned to the corridor renderers only. Each copy carries a uniform dark occlusion map, which in URP/Lit scales
/// only indirect light (ambient and reflections), so the corridors lose the scene's bright flat ambient while lamp
/// light reaches them in full. The pack materials and scene-wide lighting are untouched.</item>
/// <item>Routes are NavMesh paths between the doors in progression order: the tutorial exit to the Workshop (black
/// card), the Workshop to the Centre Room (blue) and the Centre Room to the North-West Room (yellow). Lamps within
/// reach of a route take its card's colour (an earlier route wins where they share a corridor); every other lamp
/// keeps its red emergency glow and red light. Long unlit stretches of a route get extra lamps on the nearest wall.</item>
/// <item>Each lamp gets a <see cref="CorridorBeacon"/>: the coloured globe shimmers, while its light stays grey with
/// a subtle tint and is moved out from the wall so it lights the corridor rather than a hotspot behind it.</item>
/// <item>The corridors' lamps are the Workshop's first-principles caged lamp (StartWorkshopLighting's fixture, saved as
/// <see cref="CageLampPath"/>), one on every pillar between the window bays on both sides, lit by the Workshop lamp's
/// pool spot down the corridor in the route or emergency colours above. Its wall wash stays off here: it threw a hot
/// circle onto the wall behind the lamp. The jelly-jar lamp stays on show in the tutorial wing.</item>
/// <item>The rooms' shells as the corridors see them (the SciFi Warehouse Kit's walls, door frames and vents) still
/// used the pack's URP 2D materials, which draw unlit: flat, pale "concrete". They take the tutorial wing's lit copies
/// (its dark iron), and the vents lit copies tinted to the same steel. The Boss Hall's shell is left to its own pass.</item>
/// <item>The octagonal window panes show the nebula sky (CoreBuster/Sky Window, sharing the skybox's function), as if
/// they opened onto space; what really stands behind them is the ground plane and the rooms' outer shells.</item>
/// </list>
/// Rebuilds from scratch each run: the lamps live under "===== CORRIDOR LOCKDOWN =====".
/// </summary>
public static class CorridorLockdownBuilder
{
    const string ArtRoot = "Assets/FPS/Art/Corridors/Lockdown";
    const string OldLampName = "P_CorridorLamp_Wall";
    const string CageLampPrefab = "Assets/FPS/Art/Corridors/CageLamp/P_CageLamp_Wall.prefab";
    public const string CageLampName = "P_CageLamp_Wall";
    // The wash looks back at its wall from 1.3 m out; this lets it reach the wall with some strength. What it reaches
    // behind the wall faces away from it and stays unlit.
    const float CageWashRange = 2.2f;
    const string CorridorRootName = "Plane (1)";
    const string RootName = "===== CORRIDOR LOCKDOWN =====";

    // The fraction of indirect light the corridors keep.
    const float AmbientKept = .15f;
    const float RouteReach = 3.6f;
    // The pillars' faces are flat and upright from 1 m to 3 m; the lamp's plate is 0.58 m tall.
    const float LampHeight = 2.6f;
    // Pillars are found on the window pieces' own meshes, given colliders on this otherwise unused layer while it runs.
    const int ProbeLayer = 31;
    const string SkyWindowShader = "CoreBuster/Sky Window";
    const string GlassPack = "Assets/Sci-Fi Styled Modular Pack/Materials/glass.mat";
    const string PackKit = "Assets/SciFi Warehouse Kit/Art/Materials/";
    // The tutorial wing's lit copies of the kit (Build Tutorial Wing): the iron of its walls.
    const string IronKit = "Assets/FPS/Art/TutorialWing/Materials/Pack/SciFi Warehouse Kit/Art/Materials/";
    // The vents' fan and guard, darkened from the kit's pale grey to the iron walls' steel.
    static readonly Color k_VentTint = new Color(.68f, .72f, .78f);
    // The caged lamp in the corridors (the Workshop's pool is a 50 degree cone tilted 58 degrees down, range 5, at 60).
    // A lamp on every pillar, both sides, so each burns low. Chosen from Play-mode captures against the original
    // corridor lighting.
    const float CagePoolRoute = 30f, CagePoolIdle = 18f;
    const float CagePoolAngle = 120f, CagePoolInner = 50f, CagePoolRange = 8f, CagePoolTilt = 45f;

    // Pack material -> lockdown copy name.
    static readonly (string pack, string copy)[] k_Materials =
    {
        ("Assets/Sci-Fi Styled Modular Pack/Materials/Palletes/base color.mat", "M_Lockdown_Base"),
        ("Assets/Sci-Fi Styled Modular Pack/Materials/white emision.mat", "M_Lockdown_Panel"),
        ("Assets/Sci-Fi Styled Modular Pack/Materials/lattice.mat", "M_Lockdown_Lattice"),
        ("Assets/Sci-Fi Styled Modular Pack/Materials/lattice floor.mat", "M_Lockdown_LatticeFloor"),
    };

    // Room interiors (XZ), used to tell which side of a door is the corridor.
    static readonly Rect[] k_Rooms =
    {
        Rect.MinMaxRect(-262f, -43f, -231.3f, 0.5f),  // tutorial wing
        Rect.MinMaxRect(-187f, 1f, -154f, 34.7f),     // Workshop
        Rect.MinMaxRect(-215f, 50.6f, -182f, 83.4f),  // Centre Room
        Rect.MinMaxRect(-252f, 99f, -218.3f, 132f),   // North-West Room
        Rect.MinMaxRect(-335f, 80f, -251.2f, 150f),   // Boss Hall
    };

    class Route
    {
        public string Name;
        public string Card;
        public Color Color;
        public Vector3[] Points;
    }

    static readonly Color k_GreyLight = new Color(.74f, .78f, .84f);
    // Lamps off every route keep the fitting's red emergency glow (its glass and light colours).
    static readonly Color k_IdleGlow = new Color(1f, .1f, .04f);
    static readonly Color k_IdleGlass = new Color(.34f, .012f, .01f, .78f);
    static readonly Color k_IdleLight = new Color(1f, .14f, .055f);

    [MenuItem("Tools/CoreBuster/Build Corridor Lockdown")]
    public static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        var corridors = GameObject.Find(CorridorRootName);
        if (!corridors)
        {
            Debug.LogError($"Corridor lockdown: no '{CorridorRootName}' in the open scene (open MainScene).");
            return;
        }
        var report = new StringBuilder();

        var materials = BuildMaterials(report);
        // The octagonal panes look out onto the nebula sky (the earlier dark-glass copy is retired).
        var sky = SkyWindowMaterial(report);
        materials[GlassPack] = sky;
        materials[$"{ArtRoot}/M_Lockdown_Glass.mat"] = sky;
        int swapped = 0;
        foreach (var renderer in corridors.GetComponentsInChildren<Renderer>(true))
        {
            var shared = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < shared.Length; i++)
                if (shared[i] && materials.TryGetValue(AssetDatabase.GetAssetPath(shared[i]), out var copy))
                {
                    shared[i] = copy;
                    changed = true;
                }
            if (!changed) continue;
            Undo.RecordObject(renderer, "Corridor Lockdown");
            renderer.sharedMaterials = shared;
            swapped++;
        }
        report.AppendLine($"  {swapped} corridor renderers now use the lockdown materials.");
        int iron = IronShells(report);
        report.AppendLine($"  {iron} room-shell renderers (walls, door frames, vents) moved off the unlit pack materials.");
        if (AssetDatabase.LoadAssetAtPath<Material>($"{ArtRoot}/M_Lockdown_Glass.mat"))
            AssetDatabase.DeleteAsset($"{ArtRoot}/M_Lockdown_Glass.mat");

        var old = GameObject.Find(RootName);
        if (old) Undo.DestroyObjectImmediate(old);
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Corridor Lockdown");

        var routes = BuildRoutes(report);
        var cage = AssetDatabase.LoadAssetAtPath<GameObject>(CageLampPath());
        int removed = RemoveCorridorLamps();
        var lamps = PlacePillarLamps(cage, corridors.transform, root.transform, report);
        report.AppendLine($"  {removed} earlier corridor lamps removed, {lamps.Count} placed on the pillars.");

        var counts = new Dictionary<string, int>();
        foreach (var lamp in lamps)
        {
            var route = routes.FirstOrDefault(r => DistanceToPath(lamp.position, r.Points) < RouteReach);
            Configure(lamp.gameObject, route);
            string key = route != null ? route.Name : "idle";
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
        }
        foreach (var kv in counts) report.AppendLine($"  {kv.Value} lamps: {kv.Key}");

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"CORRIDOR LOCKDOWN BUILT: {lamps.Count} lamps.\n{report}");
    }

    // ------------------------------------------------------------------ materials

    static Dictionary<string, Material> BuildMaterials(StringBuilder report)
    {
        Directory.CreateDirectory(ArtRoot);
        var occlusion = OcclusionTexture();
        var result = new Dictionary<string, Material>();
        var toUpgrade = new List<(string path, Material material)>();
        foreach (var (pack, copyName) in k_Materials)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(pack);
            if (!source)
            {
                report.AppendLine($"  MISSING pack material {pack}");
                continue;
            }
            // Rewritten in place on a rebuild, so renderers already using the copy keep their reference.
            string path = $"{ArtRoot}/{copyName}.mat";
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy)
            {
                copy.shader = source.shader;
                copy.CopyPropertiesFromMaterial(source);
                copy.shaderKeywords = source.shaderKeywords;
            }
            else
            {
                copy = new Material(source) { name = copyName };
                AssetDatabase.CreateAsset(copy, path);
            }
            result[pack] = copy;
            result[path] = copy;
            toUpgrade.Add((path, copy));
        }
        WorkshopMaterialUpgrade.Upgrade(toUpgrade);

        foreach (var (path, material) in toUpgrade)
        {
            material.SetTexture("_OcclusionMap", occlusion);
            material.SetFloat("_OcclusionStrength", 1f);
            switch (material.name)
            {
                case "M_Lockdown_Panel":
                    // The white light panels are dark during the lockdown: pale paint with a faint residual glow.
                    material.SetColor("_BaseColor", new Color(.62f, .63f, .66f));
                    material.SetColor("_EmissionColor", new Color(.018f, .02f, .024f));
                    material.SetFloat("_Smoothness", .35f);
                    break;
                case "M_Lockdown_Glass":
                    // Opaque, so the room shells and ground plane outside never show through, but with full
                    // reflections (no occlusion map): the dark glass mirrors the nebula sky and the lamps.
                    material.SetTexture("_OcclusionMap", null);
                    material.SetFloat("_Surface", 0f);
                    material.SetFloat("_Blend", 0f);
                    material.SetFloat("_AlphaClip", 0f);
                    material.SetColor("_BaseColor", new Color(.02f, .025f, .035f, 1f));
                    material.SetColor("_EmissionColor", Color.black);
                    material.SetFloat("_Smoothness", .95f);
                    material.SetFloat("_Metallic", 0f);
                    break;
                default:
                    material.SetFloat("_Smoothness", Mathf.Min(material.GetFloat("_Smoothness"), .45f));
                    break;
            }
            BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
            bool emissive = material.GetColor("_EmissionColor").maxColorComponent > 0f;
            CoreUtilsSetKeyword(material, "_EMISSION", emissive);
            material.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.AnyEmissive
                : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(material);
            report.AppendLine($"  {path}: surface {material.GetFloat("_Surface")}, queue {material.renderQueue}");
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    static void CoreUtilsSetKeyword(Material material, string keyword, bool on)
    {
        if (on) material.EnableKeyword(keyword);
        else material.DisableKeyword(keyword);
    }

    static Texture2D OcclusionTexture()
    {
        string path = $"{ArtRoot}/T_Lockdown_Occlusion.png";
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
        byte v = (byte)Mathf.RoundToInt(AmbientKept * 255f);
        texture.SetPixels32(Enumerable.Repeat(new Color32(v, v, v, 255), 16).ToArray());
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = false;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Point;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ------------------------------------------------------------------ routes

    static List<Route> BuildRoutes(StringBuilder report)
    {
        var doors = Object.FindObjectsByType<SecurityDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .ToDictionary(d => d.name);
        var cards = AssetDatabase.FindAssets("t:KeycardData").Select(g =>
            AssetDatabase.LoadAssetAtPath<KeycardData>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c).ToList();
        Color Tint(string card) => cards.FirstOrDefault(c => c.name == card) is KeycardData k ? k.Tint : Color.white;

        var routes = new List<Route>();
        void Add(string name, string card, string from, params string[] to)
        {
            if (!doors.TryGetValue(from, out var start))
            {
                report.AppendLine($"  ROUTE {name}: no door '{from}'");
                return;
            }
            Vector3[] best = null;
            foreach (var target in to)
            {
                if (!doors.TryGetValue(target, out var end)) continue;
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(Outside(start), Outside(end), NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete) continue;
                if (best == null || Length(path.corners) < Length(best)) best = path.corners;
            }
            if (best == null)
            {
                report.AppendLine($"  ROUTE {name}: no NavMesh path from {from}");
                return;
            }
            routes.Add(new Route { Name = name, Card = card, Color = Tint(card), Points = best });
            report.AppendLine($"  ROUTE {name}: {Length(best):F0} m, {best.Length} corners");
        }

        // In progression order; a lamp shared by two routes keeps the earlier one's colour.
        Add("to the Workshop (black card)", "Keycard_Black", "Tutorial Wing Exit Door", "Spawn Room Door (N)");
        Add("to the Centre Room (blue card)", "Keycard_Blue", "Spawn Room Door (N)",
            "Centre Room Door (S)", "Centre Room Door (W)", "Centre Room Door (N)");
        Add("to the North-West Room (yellow card)", "Keycard_Yellow", "Centre Room Door (N)", "North-West Room Door (E)");
        Add("to the North-West Room (yellow card, west)", "Keycard_Yellow", "Centre Room Door (W)", "North-West Room Door (E)");
        return routes;
    }

    // The corridor side of a door: its leaves slide along its forward, so the doorway faces along its right.
    static Vector3 Outside(SecurityDoor door)
    {
        var t = door.transform;
        foreach (var dir in new[] { t.right, -t.right, t.forward, -t.forward })
        {
            var p = t.position + dir * 3f;
            if (InsideRoom(p)) continue;
            if (NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) return hit.position;
        }
        return t.position;
    }

    static bool InsideRoom(Vector3 p) => k_Rooms.Any(r => r.Contains(new Vector2(p.x, p.z)));

    static float Length(Vector3[] points)
    {
        float length = 0f;
        for (int i = 1; i < points.Length; i++) length += Vector3.Distance(points[i - 1], points[i]);
        return length;
    }

    static float DistanceToPath(Vector3 p, Vector3[] path)
    {
        var q = new Vector2(p.x, p.z);
        float best = float.MaxValue;
        for (int i = 1; i < path.Length; i++)
        {
            var a = new Vector2(path[i - 1].x, path[i - 1].z);
            var b = new Vector2(path[i].x, path[i].z);
            var ab = b - a;
            float k = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
            best = Mathf.Min(best, Vector2.Distance(q, a + ab * k));
        }
        return best;
    }

    // ------------------------------------------------------------------ lamps

    /// <summary>The corridors' caged lamp prefab, saved from the Workshop's fixture the first time it is needed.</summary>
    public static string CageLampPath()
    {
        // Saved again (in place, same GUID) when missing or from before the fixture was merged into one body.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CageLampPrefab);
        if (!prefab || !prefab.transform.Find("Fixture"))
            StartWorkshopLighting.SaveFixturePrefab(CageLampPrefab, CageLampName, CageWashRange);
        return CageLampPrefab;
    }

    // A corridor lamp: any wall lamp outside the rooms that does not face into one. (The tutorial wing's own jelly-jar
    // lamps stand just outside its area on its east wall, facing in.)
    static bool IsCorridorLamp(Transform t) =>
        (t.name.StartsWith(CageLampName) || t.name.StartsWith(OldLampName)) &&
        PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) && !InsideRoom(t.position) && !InsideRoom(t.position + t.forward);

    static int RemoveCorridorLamps()
    {
        var old = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(IsCorridorLamp).ToList();
        foreach (var lamp in old) Undo.DestroyObjectImmediate(lamp.gameObject);
        return old.Count;
    }

    // Every pillar between two window bays, on both sides of every corridor. A bay is a 4 m window piece whose mesh
    // stands out from its pivot (the corridor side) and whose pillars are its two edges. Each pillar's face is found by
    // casting at the bay's own visible mesh, given a collider for the purpose (the kit's colliders are boxes).
    static List<Transform> PlacePillarLamps(GameObject cage, Transform corridors, Transform root, StringBuilder report)
    {
        var lamps = new List<Transform>();
        var bays = corridors.GetComponentsInChildren<MeshRenderer>(true)
            .Where(r => r.name.EndsWith("LOD0") && r.sharedMaterials.Any(m => m && m.shader && m.shader.name == SkyWindowShader))
            .ToList();
        var probes = new List<(MeshCollider collider, int layer)>();
        foreach (var bay in bays)
        {
            var probe = bay.gameObject.AddComponent<MeshCollider>();
            probe.sharedMesh = bay.GetComponent<MeshFilter>().sharedMesh;
            probes.Add((probe, bay.gameObject.layer));
            bay.gameObject.layer = ProbeLayer;
        }
        Physics.SyncTransforms();
        try
        {
            var faces = new List<(Vector3 point, Vector3 normal)>();
            int skipped = 0;
            foreach (var bay in bays)
            {
                var piece = bay.transform.parent ? bay.transform.parent : bay.transform;
                var c = bay.bounds.center;
                var inward = new Vector3(piece.position.x - c.x, 0f, piece.position.z - c.z);
                if (inward.sqrMagnitude < 1e-4f) continue;
                inward.Normalize();
                var along = Vector3.Cross(Vector3.up, inward);
                float half = Mathf.Abs(Vector3.Dot(bay.bounds.size, new Vector3(Mathf.Abs(along.x), 0f, Mathf.Abs(along.z)))) * .5f;
                foreach (float side in new[] { -1f, 1f })
                {
                    var origin = new Vector3(piece.position.x, LampHeight, piece.position.z) + along * (side * half) + inward * 1.2f;
                    if (!Physics.Raycast(origin, -inward, out var hit, 2.5f, 1 << ProbeLayer, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(hit.normal.y) > .2f || faces.Any(f => (f.point - hit.point).sqrMagnitude < .36f)) continue;
                    faces.Add((hit.point, new Vector3(hit.normal.x, 0f, hit.normal.z).normalized));
                }
            }
            foreach (var probe in probes)
            {
                probe.collider.gameObject.layer = probe.layer;
                Object.DestroyImmediate(probe.collider);
            }
            probes.Clear();
            Physics.SyncTransforms();

            foreach (var (point, normal) in faces)
            {
                // Not in or facing a room, and nothing (a door frame, a junction's corner) in the lamp's way.
                if (InsideRoom(point) || InsideRoom(point + normal) ||
                    Physics.Raycast(point + normal * .05f, normal, .45f, ~0, QueryTriggerInteraction.Ignore))
                {
                    skipped++;
                    continue;
                }
                var lamp = (GameObject)PrefabUtility.InstantiatePrefab(cage, root);
                lamp.name = $"{CageLampName} ({lamps.Count + 1})";
                lamp.transform.SetPositionAndRotation(point + normal * .01f, Quaternion.LookRotation(normal, Vector3.up));
                Undo.RegisterCreatedObjectUndo(lamp, "Corridor Lockdown");
                lamps.Add(lamp.transform);
            }
            report.AppendLine($"  {bays.Count} window bays, {faces.Count} pillar faces, {skipped} skipped (in or facing a room, or blocked).");
        }
        finally
        {
            foreach (var probe in probes)
                if (probe.collider)
                {
                    probe.collider.gameObject.layer = probe.layer;
                    Object.DestroyImmediate(probe.collider);
                }
        }
        return lamps;
    }

    /// <summary>Only the rooms' iron shells (part of Build), without rebuilding the lamps.</summary>
    [MenuItem("Tools/CoreBuster/Apply Iron Room Shells")]
    public static void ApplyIronShells()
    {
        var report = new StringBuilder();
        int iron = IronShells(report);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"IRON ROOM SHELLS: {iron} renderers moved off the unlit pack materials.\n{report}");
    }

    // The dead-end corridors are capped with the kit's 10 m room walls; the caps keep only the band the corridor
    // encloses, from just under its floor to the top of its roof, so nothing stands out above or below the corridor.
    const string CorridorsKitRoot = "===== CORRIDORS KIT =====";
    const string EndCapSuffix = "End Cap";
    const float EndCapFloor = -.05f, EndCapRoof = 4f;

    /// <summary>
    /// Trims every "... End Cap" piece of the corridors kit to the corridor's height band. Each mesh is cut from the
    /// kit's original (so re-running never compounds), keeping its UVs, and saved beside the lockdown art; its collider
    /// takes the trimmed mesh too.
    /// </summary>
    [MenuItem("Tools/CoreBuster/Trim Corridor End Caps")]
    public static void TrimCorridorEndCaps()
    {
        var root = GameObject.Find(CorridorsKitRoot);
        if (!root)
        {
            Debug.LogError("Trim corridor end caps: no " + CorridorsKitRoot + " in the open scene.");
            return;
        }
        var report = new StringBuilder("TRIM CORRIDOR END CAPS:");
        string folder = $"{ArtRoot}/EndCaps";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(ArtRoot, "EndCaps");
        foreach (var cap in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith(EndCapSuffix)))
            foreach (var filter in cap.GetComponentsInChildren<MeshFilter>(true))
            {
                var original = PrefabUtility.GetCorrespondingObjectFromSource(filter);
                var source = original ? original.sharedMesh : filter.sharedMesh;
                if (!source) continue;
                var t = filter.transform;
                float low = t.InverseTransformPoint(new Vector3(t.position.x, EndCapFloor, t.position.z)).y;
                float high = t.InverseTransformPoint(new Vector3(t.position.x, EndCapRoof, t.position.z)).y;
                Mesh mesh = source;
                if (source.bounds.min.y < low || source.bounds.max.y > high)
                {
                    mesh = ClipMeshY(source, low, high);
                    string file = filter.transform == cap ? AssetName(cap.name) : $"{AssetName(cap.name)}_{AssetName(filter.name)}";
                    mesh.name = $"SM_{file}";
                    string path = $"{folder}/{mesh.name}.asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (existing)
                    {
                        EditorUtility.CopySerialized(mesh, existing);
                        mesh = existing;
                    }
                    else AssetDatabase.CreateAsset(mesh, path);
                }
                Undo.RecordObject(filter, "Trim Corridor End Caps");
                filter.sharedMesh = mesh;
                var collider = filter.GetComponent<MeshCollider>();
                if (collider)
                {
                    Undo.RecordObject(collider, "Trim Corridor End Caps");
                    collider.sharedMesh = mesh;
                }
                report.Append($"\n  {cap.name}/{filter.name}: {(mesh == source ? "already inside the corridor" : $"trimmed to local y {low:F2}..{high:F2} ({source.vertexCount} -> {mesh.vertexCount} vertices)")}");
            }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log(report.ToString());
    }

    static string AssetName(string name) =>
        new string(name.Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '_').ToArray()).Trim().Replace(' ', '_');

    // The part of a mesh between two heights (object space): each triangle clipped against both planes, its vertex
    // attributes interpolated along the cut edges so the texture is not stretched.
    static Mesh ClipMeshY(Mesh source, float low, float high)
    {
        var positions = source.vertices;
        var normals = source.normals;
        var tangents = source.tangents;
        var uv = source.uv;
        var uv2 = source.uv2;
        bool hasNormals = normals.Length == positions.Length, hasTangents = tangents.Length == positions.Length;
        bool hasUv = uv.Length == positions.Length, hasUv2 = uv2.Length == positions.Length;

        var outPositions = new List<Vector3>();
        var outNormals = new List<Vector3>();
        var outTangents = new List<Vector4>();
        var outUv = new List<Vector2>();
        var outUv2 = new List<Vector2>();
        var submeshes = new List<List<int>>();

        // A vertex as weights over the source's vertices (at most two: a cut lies on one edge).
        int Emit(int a, int b, float s)
        {
            outPositions.Add(Vector3.Lerp(positions[a], positions[b], s));
            if (hasNormals) outNormals.Add(Vector3.Lerp(normals[a], normals[b], s).normalized);
            if (hasTangents) outTangents.Add(Vector4.Lerp(tangents[a], tangents[b], s));
            if (hasUv) outUv.Add(Vector2.Lerp(uv[a], uv[b], s));
            if (hasUv2) outUv2.Add(Vector2.Lerp(uv2[a], uv2[b], s));
            return outPositions.Count - 1;
        }

        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            var triangles = source.GetTriangles(sub);
            var indices = new List<int>();
            var kept = new Dictionary<int, int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                // The polygon as (a, b, s): a point s of the way from source vertex a to b.
                var polygon = new List<(int a, int b, float s)>
                    { (triangles[i], triangles[i], 0f), (triangles[i + 1], triangles[i + 1], 0f), (triangles[i + 2], triangles[i + 2], 0f) };
                polygon = ClipPolygon(polygon, positions, low, true);
                polygon = ClipPolygon(polygon, positions, high, false);
                if (polygon.Count < 3) continue;
                var ids = new int[polygon.Count];
                for (int k = 0; k < polygon.Count; k++)
                {
                    var (a, b, s) = polygon[k];
                    if (a == b || s <= 0f)
                    {
                        if (!kept.TryGetValue(a, out ids[k])) kept[a] = ids[k] = Emit(a, a, 0f);
                    }
                    else ids[k] = Emit(a, b, s);
                }
                for (int k = 1; k + 1 < ids.Length; k++)
                {
                    indices.Add(ids[0]);
                    indices.Add(ids[k]);
                    indices.Add(ids[k + 1]);
                }
            }
            submeshes.Add(indices);
        }

        var mesh = new Mesh { indexFormat = outPositions.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.SetVertices(outPositions);
        if (hasNormals) mesh.SetNormals(outNormals);
        if (hasTangents) mesh.SetTangents(outTangents);
        if (hasUv) mesh.SetUVs(0, outUv);
        if (hasUv2) mesh.SetUVs(1, outUv2);
        mesh.subMeshCount = submeshes.Count;
        for (int sub = 0; sub < submeshes.Count; sub++) mesh.SetTriangles(submeshes[sub], sub);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Sutherland-Hodgman against one horizontal plane: keeps y >= plane (above) or y <= plane (below). Points are
    // positions along source edges; every cut lands on an original triangle edge, since the two planes are parallel.
    static List<(int a, int b, float s)> ClipPolygon(List<(int a, int b, float s)> polygon, Vector3[] positions, float plane, bool keepAbove)
    {
        var result = new List<(int a, int b, float s)>();
        float Height((int a, int b, float s) p) => Mathf.Lerp(positions[p.a].y, positions[p.b].y, p.s);
        bool Inside((int a, int b, float s) p) => keepAbove ? Height(p) >= plane : Height(p) <= plane;
        for (int i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];
            bool currentIn = Inside(current), nextIn = Inside(next);
            if (currentIn) result.Add(current);
            if (currentIn == nextIn) continue;
            // The crossing lies on the source edge between the two points' own source vertices.
            Vector3 from = Vector3.Lerp(positions[current.a], positions[current.b], current.s);
            Vector3 to = Vector3.Lerp(positions[next.a], positions[next.b], next.s);
            float s = (plane - from.y) / (to.y - from.y);
            result.Add(Crossing(current, next, s));
        }
        return result;
    }

    // A point s of the way between two polygon points, re-expressed on a single source edge. A point that is itself a
    // cut sits on the edge its neighbour shares with it, so its far end is the shared vertex.
    static (int a, int b, float s) Crossing((int a, int b, float s) p, (int a, int b, float s) q, float s)
    {
        int pa = p.s >= 1f ? p.b : p.a, qa = q.s >= 1f ? q.b : q.a;
        bool pVertex = p.a == p.b || p.s <= 0f || p.s >= 1f, qVertex = q.a == q.b || q.s <= 0f || q.s >= 1f;
        if (pVertex && qVertex) return (pa, qa, s);
        // One end is a cut on edge (a, b): the other end is one of a or b (both lie on the triangle's edge).
        if (!pVertex)
        {
            // From p (on edge p.a-p.b) towards vertex qa, which is p.a or p.b.
            return qa == p.b ? (p.a, p.b, Mathf.Lerp(p.s, 1f, s)) : (p.a, p.b, Mathf.Lerp(p.s, 0f, s));
        }
        return pa == q.a ? (q.a, q.b, Mathf.Lerp(0f, q.s, s)) : (q.a, q.b, Mathf.Lerp(1f, q.s, s));
    }

    static int IronShells(StringBuilder report)
    {
        var map = new Dictionary<Material, Material>();
        void Map(string pack, Material lit)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(PackKit + pack + ".mat");
            if (source && lit) map[source] = lit;
            else report.AppendLine($"  MISSING {(source ? "lit copy" : "pack material")} for {pack}");
        }
        Map("Walls", AssetDatabase.LoadAssetAtPath<Material>(IronKit + "Walls.mat"));
        Map("Structure Mat", AssetDatabase.LoadAssetAtPath<Material>(IronKit + "Structure Mat.mat"));
        Map("Fan Mat", VentCopy("Fan Mat", "M_Lockdown_VentFan"));
        Map("Vent Trans Mat", VentCopy("Vent Trans Mat", "M_Lockdown_VentGuard"));

        int swapped = 0;
        var bossHall = k_Rooms[k_Rooms.Length - 1];
        foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var c = renderer.bounds.center;
            if (bossHall.Contains(new Vector2(c.x, c.z))) continue;
            var shared = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < shared.Length; i++)
                if (shared[i] && map.TryGetValue(shared[i], out var lit))
                {
                    shared[i] = lit;
                    changed = true;
                }
            if (!changed) continue;
            Undo.RecordObject(renderer, "Corridor Lockdown");
            renderer.sharedMaterials = shared;
            swapped++;
        }
        return swapped;
    }

    // A lit copy of one of the kit's vent materials, regenerated from the pack each run (so the tint never compounds)
    // and darkened to the walls' steel.
    static Material VentCopy(string pack, string copyName)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(PackKit + pack + ".mat");
        if (!source) return null;
        string path = $"{ArtRoot}/{copyName}.mat";
        var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (copy) EditorUtility.CopySerialized(source, copy);
        else
        {
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path);
            copy = AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        copy.name = copyName;
        if (copy.parent) copy.parent = null;
        WorkshopMaterialUpgrade.Upgrade(new List<(string, Material)> { (path, copy) });
        copy.SetColor("_BaseColor", copy.GetColor("_BaseColor") * k_VentTint);
        // As the tutorial wing's copies: the kit's high metallic leaves almost no diffuse.
        float metallic = copy.GetFloat("_Metallic");
        if (metallic > .1f && !copy.GetTexture("_MetallicGlossMap"))
            copy.SetFloat("_Metallic", Mathf.Min(.4f, .1f + .3f * (metallic - .1f) / .8f));
        EditorUtility.SetDirty(copy);
        return copy;
    }

    // The sky window material, its sky settings copied from the scene's nebula skybox so the panes and the sky match.
    static Material SkyWindowMaterial(StringBuilder report)
    {
        var shader = Shader.Find(SkyWindowShader);
        string path = $"{ArtRoot}/M_Lockdown_SkyWindow.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader) { name = "M_Lockdown_SkyWindow" };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        var skybox = RenderSettings.skybox;
        int copied = 0;
        if (skybox)
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i);
                if (!skybox.HasProperty(name)) continue;
                switch (shader.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color: material.SetColor(name, skybox.GetColor(name)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector: material.SetVector(name, skybox.GetVector(name)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range: material.SetFloat(name, skybox.GetFloat(name)); break;
                    default: continue;
                }
                copied++;
            }
        EditorUtility.SetDirty(material);
        report.AppendLine($"  {path}: {copied} sky settings copied from {(skybox ? skybox.name : "no skybox")}.");
        return material;
    }

    static void Configure(GameObject lamp, Route route)
    {
        var beacon = lamp.GetComponent<CorridorBeacon>();
        if (!beacon) beacon = Undo.AddComponent<CorridorBeacon>(lamp);
        Undo.RecordObject(beacon, "Corridor Lockdown");
        var renderers = lamp.GetComponentsInChildren<Renderer>(true);
        var lights = lamp.GetComponentsInChildren<Light>(true);
        // The caged lamp lights like the Workshop's: a pool down the corridor and a wash onto its wall.
        bool caged = lamp.name.StartsWith(CageLampName);
        beacon.Globe = renderers.FirstOrDefault(r => r.name.Contains("Globe"));
        beacon.Bulb = renderers.FirstOrDefault(r => r.name == "Bulb");
        beacon.Light = caged ? lights.FirstOrDefault(l => l.name == StartWorkshopLighting.PoolName) : lights.FirstOrDefault();
        beacon.WashLight = caged ? lights.FirstOrDefault(l => l.name == StartWorkshopLighting.WashName) : null;

        Color glow = route != null ? route.Color : k_IdleGlow;
        beacon.GlowColor = glow;
        // Kept low enough that bloom does not wash the colour out to white; the bulb burns hotter.
        beacon.GlowIntensity = route != null ? 3.2f : 2.2f;
        beacon.BulbIntensity = route != null ? 7f : 6f;
        beacon.GlassColor = route != null ? new Color(glow.r * .6f, glow.g * .6f, glow.b * .6f, .55f) : k_IdleGlass;
        beacon.Shimmer = route != null ? .4f : .2f;
        beacon.LightIntensity = route != null ? 4.5f : 2.6f;

        if (caged)
        {
            ConfigureCaged(beacon, route);
            EditorUtility.SetDirty(beacon);
            return;
        }
        if (beacon.Light)
        {
            Undo.RecordObject(beacon.Light, "Corridor Lockdown");
            Undo.RecordObject(beacon.Light.transform, "Corridor Lockdown");
            // Grey, with only a hint of the card's colour.
            beacon.Light.color = route != null ? Color.Lerp(k_GreyLight, route.Color, .28f) : k_IdleLight;
            beacon.Light.intensity = beacon.LightIntensity;
            beacon.Light.range = route != null ? 7.5f : 6f;
            beacon.Light.shadows = LightShadows.None;
            // Out from the wall, so it lights the corridor rather than a hotspot on the wall behind it.
            var head = beacon.Light.transform.parent;
            beacon.Light.transform.position = (head ? head.position : lamp.transform.position) +
                                              lamp.transform.forward * .7f + Vector3.down * .2f;
        }
        EditorUtility.SetDirty(beacon);
    }

    // The Workshop's pool, in the route's grey-tinted light or the emergency red. It opens wider and flatter than in the
    // Workshop, so it reaches across the corridor and up its walls.
    static void ConfigureCaged(CorridorBeacon beacon, Route route)
    {
        Color color = route != null ? Color.Lerp(k_GreyLight, route.Color, .28f) : k_IdleLight;
        beacon.LightIntensity = route != null ? CagePoolRoute : CagePoolIdle;
        // The wash lit a hot circle on the wall behind the lamp; the corridor lamps go without it.
        if (beacon.WashLight)
        {
            Undo.RecordObject(beacon.WashLight.gameObject, "Corridor Lockdown");
            beacon.WashLight.gameObject.SetActive(false);
            beacon.WashLight = null;
        }
        if (beacon.Light)
        {
            Undo.RecordObject(beacon.Light, "Corridor Lockdown");
            beacon.Light.color = color;
            beacon.Light.intensity = beacon.LightIntensity;
            beacon.Light.shadows = LightShadows.None;
        }
        if (beacon.Light)
        {
            var pool = beacon.Light;
            Undo.RecordObject(pool.transform, "Corridor Lockdown");
            pool.spotAngle = CagePoolAngle;
            pool.innerSpotAngle = CagePoolInner;
            pool.range = CagePoolRange;
            Vector3 n = beacon.transform.forward;
            float tilt = CagePoolTilt * Mathf.Deg2Rad;
            pool.transform.rotation = Quaternion.LookRotation(n * Mathf.Cos(tilt) - Vector3.up * Mathf.Sin(tilt), Vector3.up);
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FPS.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// corebuster_env_058. Fills the Boss Hall's open floor so the core fight has cover and sightline breaks, and fixes the
/// pink flash on the stairs. Run after Tools/CoreBuster/Build Finale (it keeps clear of the reactor ring that adds).
/// <list type="bullet">
/// <item>Cover clusters are copies of the Centre and North-West rooms' own dressing (barriers, generators, capacitors,
/// rotor cradles, crate and barrel stacks, machinery bays, pressure tanks), mixing low cover with tall pieces that
/// block sight. Each copy keeps its colliders, carves the NavMesh, and uses material copies under
/// <c>Assets/FPS/Art/BossFloor/Materials/</c> (the North-West Room's materials stay in that room).</item>
/// <item>Placements keep clear of the reactor ring, the east entry lane, the turret, the starting enemies and the low
/// galleries along the walls; any that would overlap existing collision are nudged or skipped (and reported).</item>
/// <item>Starting enemies standing inside the reactor ring are moved out to its edge.</item>
/// <item>The pink: the hall's and the entry incline's ProBuilder ground planes ("Plane (3)", "Plane (2)") still used
/// ProBuilder's default material, whose shader does not exist in URP. Their renderers get a dark sub-floor material;
/// their collision is untouched.</item>
/// </list>
/// Rebuilds from scratch each run under "===== BOSS HALL FLOOR =====".
/// </summary>
public static class BossFloorBuilder
{
    const string RootName = "===== BOSS HALL FLOOR =====";
    const string ArtRoot = "Assets/FPS/Art/BossFloor";
    const string MaterialFolder = ArtRoot + "/Materials";

    static readonly Vector3 k_Reactor = new Vector3(-296f, 6.2f, 115.3f);
    static readonly Vector3 k_Turret = new Vector3(-311.5f, 6.2f, 115.3f);
    const float ReactorClear = 12f;
    // The open floor between the galleries, and the east entry lane kept free.
    static readonly Rect k_Floor = Rect.MinMaxRect(-321f, 96f, -267f, 134.5f);
    static readonly Rect k_EntryLane = Rect.MinMaxRect(-285f, 111.5f, -262f, 119f);

    struct Spot
    {
        public string Source;
        public float X, Z, Yaw;
        public Spot(string source, float x, float z, float yaw = 0f) { Source = source; X = x; Z = z; Yaw = yaw; }
    }

    const string Centre = "Level/Centre_Room/";
    const string NorthWest = "Level/NorthWest_Room/";

    static readonly Spot[] k_Spots =
    {
        // East: low cover either side of the entry lane, tall machinery at the corners.
        new Spot(Centre + "Cover/Low_Barrier_SW", -279f, 124.5f),
        new Spot(Centre + "Cover/Low_Barrier_SE", -279f, 106f),
        new Spot(NorthWest + "Perimeter_Machinery/Bay_N4", -272f, 129.5f),
        new Spot(NorthWest + "Perimeter_Machinery/Bay_N5", -272f, 100.5f),
        new Spot(Centre + "Perimeter_Machinery/Crate_Stack_NE", -285.5f, 131f, 20f),
        new Spot(Centre + "Perimeter_Machinery/Pallet_Row_S", -285.5f, 99.5f, -15f),
        // North and south bands.
        new Spot(Centre + "Perimeter_Machinery/Generator_W", -293f, 131f, 90f),
        new Spot(Centre + "Perimeter_Machinery/Capacitor_N", -304f, 130.5f),
        new Spot(Centre + "Perimeter_Machinery/Generator_E", -293f, 99.5f, 90f),
        new Spot(Centre + "Perimeter_Machinery/Capacitor_E", -304f, 100f, 90f),
        // Inner ring: breaks the long sightlines across the reactor.
        new Spot(Centre + "Perimeter_Machinery/Rotor_Cradle_E", -287.5f, 126f, 45f),
        new Spot(Centre + "Perimeter_Machinery/Rotor_Cradle_W", -287.5f, 104.5f, -45f),
        new Spot(Centre + "Perimeter_Machinery/Barrel_Pair_SE", -306f, 127.5f),
        new Spot(Centre + "Perimeter_Machinery/Barrel_Row_NW", -305f, 103f),
        new Spot(Centre + "Cover/Low_Barrier_NW", -297f, 129.5f),
        new Spot(Centre + "Cover/Low_Barrier_NE", -297f, 101f),
        new Spot(Centre + "Perimeter_Machinery/Pallet_Stack_N", -279.5f, 131f),
        new Spot(Centre + "Perimeter_Machinery/Crate_Stack_SW", -279.5f, 99.5f),
        // West: tall pressure tanks and low cover flanking the turret.
        new Spot(NorthWest + "Pressure_Systems/Bay_S3", -309.5f, 130f),
        new Spot(NorthWest + "Pressure_Systems/Bay_S5", -309.5f, 100.5f),
        new Spot(NorthWest + "Cover/Cover_A3", -317f, 120f),
        new Spot(NorthWest + "Cover/Cover_A1", -317f, 110.5f, 90f),
        new Spot(Centre + "Perimeter_Machinery/Container_N", -318f, 128f),
        new Spot(Centre + "Perimeter_Machinery/Battery_Bay_W", -318f, 102.5f),
    };

    [MenuItem("Tools/CoreBuster/Build Boss Hall Floor")]
    public static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        if (!GameObject.Find("Level/Boss_Hall_Dressing"))
        {
            Debug.LogError("Boss Hall floor: open MainScene first.");
            return;
        }
        var report = new StringBuilder();
        Directory.CreateDirectory(MaterialFolder);

        FixPinkPlanes(report);

        var old = GameObject.Find(RootName);
        if (old) Object.DestroyImmediate(old);
        var root = new GameObject(RootName).transform;
        Physics.SyncTransforms();

        ClearReactorRing(report);
        Physics.SyncTransforms();

        var actors = Object.FindObjectsByType<Health>(FindObjectsInactive.Exclude)
            .Where(h => h.GetComponent<NavMeshAgent>() || h.name.Contains("Turret")).Select(h => h.transform.position).ToList();
        var consoles = Object.FindObjectsByType<Unity.FPS.Gameplay.ReactorConsole>(FindObjectsInactive.Exclude)
            .Select(c => c.transform.position).ToList();
        var materials = new Dictionary<Material, Material>();

        int placed = 0;
        foreach (var spot in k_Spots)
        {
            var source = GameObject.Find(spot.Source);
            if (!source)
            {
                report.AppendLine($"  SKIPPED {spot.Source}: not in the scene");
                continue;
            }
            var copy = Object.Instantiate(source, root);
            copy.name = source.name + " (Boss Hall)";
            StripScripts(copy);
            RemapMaterials(copy, materials);

            if (TryPlace(copy, source, spot, actors, consoles, out string why))
            {
                AddCarving(copy);
                placed++;
            }
            else
            {
                report.AppendLine($"  SKIPPED {source.name} at ({spot.X}, {spot.Z}): {why}");
                Object.DestroyImmediate(copy);
            }
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"BOSS HALL FLOOR BUILT: {placed}/{k_Spots.Length} cover clusters, {materials.Count} material copies.\n{report}");
    }

    // ------------------------------------------------------------------ pink planes

    static void FixPinkPlanes(StringBuilder report)
    {
        var subfloor = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_BossFloor_Subfloor.mat");
        if (!subfloor)
        {
            subfloor = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_BossFloor_Subfloor" };
            AssetDatabase.CreateAsset(subfloor, MaterialFolder + "/M_BossFloor_Subfloor.mat");
        }
        subfloor.SetColor("_BaseColor", new Color(.09f, .095f, .1f));
        subfloor.SetFloat("_Metallic", .3f);
        subfloor.SetFloat("_Smoothness", .3f);
        EditorUtility.SetDirty(subfloor);

        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var m = materials[i];
                if (m && (m.name == "ProBuilderDefault" || m.shader == null || m.shader.name == "Hidden/InternalErrorShader") &&
                    renderer.bounds.center.x < -250f && renderer.bounds.center.z > 75f && renderer.bounds.center.z < 155f)
                {
                    materials[i] = subfloor;
                    changed = true;
                }
            }
            if (!changed) continue;
            Undo.RecordObject(renderer, "Fix pink planes");
            renderer.sharedMaterials = materials;
            report.AppendLine($"  Fixed missing shader on {renderer.name} ({renderer.bounds.size.x:F0} x {renderer.bounds.size.z:F0} m)");
        }
    }

    // ------------------------------------------------------------------ placement

    static void ClearReactorRing(StringBuilder report)
    {
        foreach (var agent in Object.FindObjectsByType<NavMeshAgent>(FindObjectsInactive.Exclude))
        {
            var root = agent.transform;
            var flat = new Vector2(root.position.x - k_Reactor.x, root.position.z - k_Reactor.z);
            if (flat.magnitude >= ReactorClear + .5f || Mathf.Abs(root.position.y - k_Reactor.y) > 2f) continue;
            if (flat.sqrMagnitude < .01f) flat = Vector2.right;
            var target = k_Reactor + new Vector3(flat.x, 0f, flat.y).normalized * (ReactorClear + 1.5f);
            if (NavMesh.SamplePosition(target, out var hit, 3f, NavMesh.AllAreas)) target = hit.position;
            Undo.RecordObject(root, "Clear reactor ring");
            report.AppendLine($"  Moved {root.name} out of the reactor ring: {root.position} -> {target}");
            root.position = target;
        }
    }

    static bool TryPlace(GameObject copy, GameObject source, Spot spot, List<Vector3> actors, List<Vector3> consoles,
        out string why)
    {
        // Keep the source's pose relative to its own floor; turn it about its footprint's centre.
        var sourceBounds = Bounds(source);
        float sourceFloor = sourceBounds.min.y;
        Vector3 pivotOffset = source.transform.position - new Vector3(sourceBounds.center.x, sourceFloor, sourceBounds.center.z);
        var rotation = Quaternion.Euler(0f, spot.Yaw, 0f);

        foreach (var nudge in new[] { Vector2.zero, new Vector2(1.5f, 0f), new Vector2(-1.5f, 0f), new Vector2(0f, 1.5f), new Vector2(0f, -1.5f) })
        {
            var centre = new Vector3(spot.X + nudge.x, 0f, spot.Z + nudge.y);
            centre.y = FloorAt(centre);
            copy.transform.SetPositionAndRotation(centre + rotation * pivotOffset, rotation * source.transform.rotation);
            Physics.SyncTransforms();
            var bounds = Bounds(copy);
            if (!Clear(bounds, copy, actors, consoles, out why)) continue;
            return true;
        }
        why = "no clear spot nearby (" + (string.IsNullOrEmpty(LastReason) ? "blocked" : LastReason) + ")";
        return false;
    }

    static string LastReason;

    static bool Clear(Bounds bounds, GameObject copy, List<Vector3> actors, List<Vector3> consoles, out string why)
    {
        why = null;
        var flat = new Rect(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z);
        bool Fail(string reason)
        {
            LastReason = reason;
            return false;
        }
        if (flat.xMin < k_Floor.xMin || flat.xMax > k_Floor.xMax || flat.yMin < k_Floor.yMin || flat.yMax > k_Floor.yMax)
            return Fail("off the open floor");
        if (flat.Overlaps(k_EntryLane)) return Fail("in the entry lane");
        if (DistanceToRect(flat, new Vector2(k_Reactor.x, k_Reactor.z)) < ReactorClear) return Fail("in the reactor ring");
        if (DistanceToRect(flat, new Vector2(k_Turret.x, k_Turret.z)) < 3f) return Fail("by the turret");
        foreach (var actor in actors)
            if (DistanceToRect(flat, new Vector2(actor.x, actor.z)) < 2f) return Fail("on an enemy");
        foreach (var console in consoles)
            if (DistanceToRect(flat, new Vector2(console.x, console.z)) < 3.5f) return Fail("by a console");

        // Existing collision, ignoring the floor itself and the copy.
        var probe = new Bounds(bounds.center + Vector3.up * .1f, bounds.size - new Vector3(.1f, .3f, .1f));
        foreach (var other in Physics.OverlapBox(probe.center, probe.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (other.transform.IsChildOf(copy.transform)) continue;
            if (other.bounds.max.y < bounds.min.y + .2f) continue;
            return Fail("overlaps " + other.name);
        }
        return true;
    }

    static float DistanceToRect(Rect rect, Vector2 point)
    {
        float dx = Mathf.Max(rect.xMin - point.x, 0f, point.x - rect.xMax);
        float dy = Mathf.Max(rect.yMin - point.y, 0f, point.y - rect.yMax);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    static float FloorAt(Vector3 point)
    {
        float best = float.NegativeInfinity;
        foreach (var hit in Physics.RaycastAll(new Vector3(point.x, 9.5f, point.z), Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.collider.GetComponentInParent<Health>() && !hit.collider.transform.IsChildOf(GameObject.Find(RootName).transform))
                best = Mathf.Max(best, hit.point.y);
        return float.IsNegativeInfinity(best) ? 6.2f : best;
    }

    static Bounds Bounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    static void AddCarving(GameObject copy)
    {
        // Several sources already carve their own footprint.
        if (copy.GetComponentInChildren<NavMeshObstacle>()) return;
        var bounds = Bounds(copy);
        var obstacle = copy.AddComponent<NavMeshObstacle>();
        obstacle.carving = true;
        obstacle.center = copy.transform.InverseTransformPoint(bounds.center);
        var size = copy.transform.InverseTransformVector(bounds.size);
        obstacle.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
    }

    // Dressing only: no room behaviour comes with the copies.
    static void StripScripts(GameObject copy)
    {
        foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour && (behaviour.GetType().Namespace ?? "").StartsWith("Unity.FPS"))
                Object.DestroyImmediate(behaviour);
    }

    static void RemapMaterials(GameObject copy, Dictionary<Material, Material> cache)
    {
        foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!source) continue;
                if (!cache.TryGetValue(source, out var copyMaterial))
                {
                    // Rooms reuse names (Gunmetal, ...), so the source's GUID keeps each copy distinct.
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                    string suffix = string.IsNullOrEmpty(guid) ? "" : "_" + guid.Substring(0, 6);
                    string path = $"{MaterialFolder}/M_BossFloor_{Sanitize(source.name)}{suffix}.mat";
                    copyMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!copyMaterial)
                    {
                        copyMaterial = new Material(source) { name = Path.GetFileNameWithoutExtension(path) };
                        AssetDatabase.CreateAsset(copyMaterial, path);
                    }
                    else
                    {
                        copyMaterial.shader = source.shader;
                        copyMaterial.CopyPropertiesFromMaterial(source);
                    }
                    cache[source] = copyMaterial;
                }
                materials[i] = copyMaterial;
            }
            renderer.sharedMaterials = materials;
        }
    }

    static string Sanitize(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        return new string(chars);
    }
}

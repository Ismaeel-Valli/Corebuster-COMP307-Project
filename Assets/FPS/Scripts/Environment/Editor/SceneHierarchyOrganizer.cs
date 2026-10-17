using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Files MainScene's loose top-level objects into folders (<see cref="HierarchyFolder"/>): one kit folder per room
/// (rooms are ZoneStreamer's areas; the rest is the corridors), split by type (Walls, Doorways, Floors, ...), enemies
/// under ===== ENEMIES ===== by room, and the game systems and the player in their own folders.
/// <para>Builder-owned roots stay at the top level (Level, Plane (1) and the other ===== roots): their builders find
/// them there and rebuild them. They are ordered beside their room's kit folder. Re-runnable: it only moves objects
/// that are not filed yet, and world transforms never change.</para>
/// </summary>
public static class SceneHierarchyOrganizer
{
    const string ScenePath = "Assets/FPS/Scenes/MainScene.unity";
    const string General = "===== GENERAL =====";
    const string PlayerFolder = "===== PLAYER =====";
    const string Enemies = "===== ENEMIES =====";
    const string CorridorsName = "Corridors";
    // How far outside a room's floor area its walls and doorways still count as the room's.
    const float RoomMargin = 1f;
    // Renderers this far from a piece's pivot (sun shafts, far-flung effects) do not decide its room.
    const float AnchorReach = 25f;

    // Type folders, by the start of a piece's name. Folder names avoid the tokens tools look for in paths ("Floor"
    // only appears as "Floors", which no path check reaches).
    static readonly (string folder, string[] prefixes)[] Types =
    {
        ("Doorways", new[] { "Wall Door", "Alternate Wall Door", "Wall BayDoor" }),
        ("Walls", new[] { "Wall Plain", "Wall Corner", "glass_panel" }),
        ("Fans", new[] { "Wall Fan" }),
        ("Floors", new[] { "Floor Tile", "Plane (" }),
        ("Ceilings", new[] { "Ceiling", "LP_ceiling_tile", "LP_Skylight" }),
        ("Catwalks & Stairs", new[] { "Catwalk", "CatWalk", "Corridor Incline" }),
        ("Lamps", new[] { "P_CorridorLamp_Wall", "P_CageLamp_Wall" }),
        ("Props", new[] { "PBRCharacter", "PolyartCharacter" }),
    };

    // Builder-owned roots: never moved, only ordered.
    static readonly string[] BuilderRoots =
    {
        "Level", "Plane (1)", "===== TUTORIAL WING =====", "===== KEYCARD PROGRESSION =====", "===== CORRIDOR LOCKDOWN =====",
        "===== FLOOR GUIDE =====", "===== BOSS HALL FLOOR =====", "===== FINALE =====",
    };

    // The FPS Microgame template's empty separators, superseded by the folders.
    static readonly string[] LegacySeparators = { "===== LEVEL =====", "===== PATHS =====", "===== PICKUPS =====" };

    [MenuItem("Tools/CoreBuster/Organize Main Scene Hierarchy")]
    public static void Organize()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath);
        }
        var streamer = UnityEngine.Object.FindAnyObjectByType<ZoneStreamer>(FindObjectsInactive.Include);
        if (!streamer)
        {
            Debug.LogError("Organize Main Scene Hierarchy: no ZoneStreamer in the scene, so no room areas.");
            return;
        }

        var before = WorldTransforms(scene);
        var moved = new Dictionary<string, int>();
        var unfiled = new List<string>();
        int removed = 0;

        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.GetComponent<HierarchyFolder>() || BuilderRoots.Contains(go.name)) continue;
            if (LegacySeparators.Contains(go.name) && go.transform.childCount == 0 && go.GetComponents<Component>().Length == 1)
            {
                Undo.DestroyObjectImmediate(go);
                removed++;
                continue;
            }
            // Separators, folders and builder roots are never filed themselves.
            if (go.name.StartsWith("=====", StringComparison.Ordinal)) continue;

            string path = Destination(go, streamer);
            if (path == null)
            {
                unfiled.Add(go.name);
                continue;
            }
            Undo.SetTransformParent(go.transform, Folder(scene, path), true, "Organize Main Scene Hierarchy");
            moved[path] = moved.TryGetValue(path, out int n) ? n + 1 : 1;
        }

        foreach (var folder in AllFolders(scene)) SortChildren(folder);
        OrderRoots(scene, streamer);

        int drifted = WorldTransforms(scene).Count(kv => before.TryGetValue(kv.Key, out var m) && !Same(m, kv.Value));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        var report = new StringBuilder($"Organize Main Scene Hierarchy: {moved.Values.Sum()} objects filed, {removed} empty " +
                                       $"separators removed, {scene.rootCount} top-level objects, {drifted} world transforms changed.");
        foreach (var kv in moved.OrderBy(kv => kv.Key)) report.Append($"\n  {kv.Key}: {kv.Value}");
        if (unfiled.Count > 0) report.Append("\n  Left at the top level (no rule): " + string.Join(", ", unfiled));
        if (drifted > 0) Debug.LogError(report.ToString());
        else Debug.Log(report.ToString());
    }

    // "Folder/Subfolder" for a top-level object, or null when no rule covers it.
    static string Destination(GameObject go, ZoneStreamer streamer)
    {
        if (go.GetComponent<PlayerCharacterController>()) return PlayerFolder;
        // Every character but the player is an enemy (they carry an Actor, like the player).
        if (go.GetComponent<Actor>()) return Enemies + "/" + RoomAt(go.transform.position, streamer);
        if (go.GetComponent<Objective>() || go.GetComponent<GameFlowManager>() || HasComponent(go, "EventSystem") ||
            HasComponent(go, "NavMeshSurface") || (go.TryGetComponent<Light>(out var sun) && sun.type == LightType.Directional))
            return General;

        foreach (var (folder, prefixes) in Types)
            if (prefixes.Any(p => go.name.StartsWith(p, StringComparison.Ordinal)))
                return KitFolder(RoomAt(Anchor(go), streamer)) + "/" + folder;
        return null;
    }

    // By type name, for components in assemblies this one does not reference (UI, AI Navigation).
    static bool HasComponent(GameObject go, string typeName) =>
        go.GetComponents<Component>().Any(c => c && c.GetType().Name == typeName);

    static string KitFolder(string room) => $"===== {room.ToUpperInvariant()} KIT =====";

    static string RoomAt(Vector3 p, ZoneStreamer streamer)
    {
        foreach (var room in streamer.Rooms)
        {
            var a = room.Area;
            if (p.x >= a.xMin - RoomMargin && p.x <= a.xMax + RoomMargin && p.z >= a.yMin - RoomMargin && p.z <= a.yMax + RoomMargin)
                return room.Name;
        }
        return CorridorsName;
    }

    // Where a piece stands: the middle of its renderers (kit pivots sit on a corner), or its pivot.
    static Vector3 Anchor(GameObject go)
    {
        Vector3 pivot = go.transform.position;
        Bounds? bounds = null;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if ((r.bounds.center - pivot).sqrMagnitude > AnchorReach * AnchorReach) continue;
            if (bounds is Bounds b)
            {
                b.Encapsulate(r.bounds);
                bounds = b;
            }
            else bounds = r.bounds;
        }
        return bounds?.center ?? pivot;
    }

    static Transform Folder(UnityEngine.SceneManagement.Scene scene, string path)
    {
        Transform parent = null;
        foreach (var name in path.Split('/'))
        {
            Transform found = parent
                ? parent.Cast<Transform>().FirstOrDefault(c => c.name == name)
                : scene.GetRootGameObjects().Select(g => g.transform).FirstOrDefault(t => t.name == name);
            if (!found)
            {
                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Organize Main Scene Hierarchy");
                if (parent) Undo.SetTransformParent(go.transform, parent, false, "Organize Main Scene Hierarchy");
                found = go.transform;
            }
            if (!found.GetComponent<HierarchyFolder>())
            {
                // A reused template separator: it must sit at the origin before anything is filed in it.
                if (found.childCount == 0)
                {
                    Undo.RecordObject(found, "Organize Main Scene Hierarchy");
                    found.localPosition = Vector3.zero;
                    found.localRotation = Quaternion.identity;
                    found.localScale = Vector3.one;
                }
                Undo.AddComponent<HierarchyFolder>(found.gameObject);
            }
            parent = found;
        }
        return parent;
    }

    static IEnumerable<Transform> AllFolders(UnityEngine.SceneManagement.Scene scene) =>
        scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<HierarchyFolder>(true)).Select(f => f.transform);

    // Pieces in natural order ("Wall Plain (2)" before "Wall Plain (10)"); folders in the order of the type rules.
    static void SortChildren(Transform folder)
    {
        var typeOrder = Types.Select(t => t.folder).ToList();
        var children = folder.Cast<Transform>()
            .OrderBy(c => c.GetComponent<HierarchyFolder>() ? 0 : 1)
            .ThenBy(c => typeOrder.IndexOf(c.name) is int i && i >= 0 ? i : int.MaxValue)
            .ThenBy(c => NaturalKey(c.name), StringComparer.Ordinal)
            .ToList();
        for (int i = 0; i < children.Count; i++)
            if (children[i].GetSiblingIndex() != i)
            {
                Undo.SetSiblingIndex(children[i], i, "Organize Main Scene Hierarchy");
            }
    }

    static string NaturalKey(string name) => Regex.Replace(name, @"\d+", m => m.Value.PadLeft(8, '0'));

    // Systems and the player first, then each room's builder roots beside its kit folder, then the enemies.
    static void OrderRoots(UnityEngine.SceneManagement.Scene scene, ZoneStreamer streamer)
    {
        var order = new List<string> { General, PlayerFolder, "Plane (1)", KitFolder(CorridorsName), "===== CORRIDOR LOCKDOWN =====",
            "===== FLOOR GUIDE =====", "===== KEYCARD PROGRESSION =====", "===== TUTORIAL WING =====" };
        order.Add("Level");
        foreach (var room in streamer.Rooms) order.Add(KitFolder(room.Name));
        order.AddRange(new[] { "===== BOSS HALL FLOOR =====", "===== FINALE =====", Enemies });
        // The tutorial wing's kit sits with its builder root.
        order.Remove(KitFolder(streamer.Rooms[0].Name));
        order.Insert(order.IndexOf("===== TUTORIAL WING =====") + 1, KitFolder(streamer.Rooms[0].Name));

        var roots = scene.GetRootGameObjects().Select(g => g.transform)
            .OrderBy(t => order.IndexOf(t.name) is int i && i >= 0 ? i : int.MaxValue)
            .ToList();
        for (int i = 0; i < roots.Count; i++)
            if (roots[i].GetSiblingIndex() != i) roots[i].SetSiblingIndex(i);
    }

    static Dictionary<Transform, Matrix4x4> WorldTransforms(UnityEngine.SceneManagement.Scene scene) =>
        scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => !t.GetComponent<HierarchyFolder>())
            .ToDictionary(t => t, t => t.localToWorldMatrix);

    static bool Same(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++)
            if (Mathf.Abs(a[i] - b[i]) > 1e-3f) return false;
        return true;
    }
}

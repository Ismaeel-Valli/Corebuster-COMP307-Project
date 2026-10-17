using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static StartWorkshopDresser;

/// <summary>
/// Rebuildable corebuster_env_007 structure pass. The 5.8–7.2 m grid deliberately sits below the existing duct
/// run, making the upper half of the workshop readable without introducing movement blockers or real lights.
/// </summary>
public static class StartWorkshopOverhead
{
    public const string GroupName = "Overhead_Structure_007";
    const string UndoName = "Build Start Workshop Overhead";
    const string Kit = "Assets/SciFi Warehouse Kit/Prefabs/";
    const string CeilingSupport = Kit + "Structures/Pillars and Beams/Ceiling Support.prefab";
    const string WallSupport = Kit + "Structures/Pillars and Beams/Wall Support.prefab";
    const string Pillar = Kit + "Structures/Pillars and Beams/Pillar.prefab";
    const string Pipe = Kit + "Structures/Structure Props/Pipes 01.prefab";
    const string HangingLight = Kit + "Props/Misc Props/Hanging Light.prefab";
    const string Sprinkler = Kit + "Structures/Structure Props/Sprinkler 03.prefab";
    const string SecurityCamera = Kit + "Props/Misc Props/Security Cam.prefab";

    const float BeamCenterY = 6.05f;
    const float SideBeamCenterY = 6.72f;
    const float SegmentGap = .05f;

    [MenuItem("Tools/CoreBuster/Build Start Workshop Overhead")]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first; the overhead pass belongs under {LevelName}/{ContainerName}.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        var old = root.Find(GroupName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var frame = StartWorkshopFrame.Measure(scene, root);
        int firstChild = root.childCount;
        try
        {
            Build(root, frame);
            ApplyStaticConvention(root.GetChild(firstChild), GameObjectUtility.GetStaticEditorFlags(level.gameObject));
        }
        catch
        {
            if (root.childCount > firstChild) UnityEngine.Object.DestroyImmediate(root.GetChild(firstChild).gameObject);
            throw;
        }
        Undo.RegisterCreatedObjectUndo(root.GetChild(firstChild).gameObject, UndoName);
        StartWorkshopLitMaterials.ApplyTo(scene, root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root.Find(GroupName);
        Debug.Log($"Start Workshop overhead structure rebuilt. {frame}");
        StartWorkshopChecks.Run();
    }

    /// <summary>Called by the full workshop rebuild after the duct/detail pass, and by the isolated menu action.</summary>
    public static void Build(Transform root, StartWorkshopFrame frame)
    {
        if (root.Find(GroupName) != null)
            throw new InvalidOperationException($"{GroupName} already exists. Remove it before rebuilding the overhead pass.");

        // This is deliberately an anchor rather than an assumed coordinate: the ceiling pass is only valid beside
        // the existing env_004 duct system, and a partial/hand-edited workshop fails with a useful message.
        Require(root, "Ducts_Ceiling/Duct_Run");

        var group = Group(root, GroupName);
        float west = frame.West + .45f, east = frame.East - .45f;
        float south = frame.South + .7f, north = frame.North - .7f;
        float[] crossZ = { Mathf.Lerp(south, north, .16f), Mathf.Lerp(south, north, .39f), Mathf.Lerp(south, north, .62f), Mathf.Lerp(south, north, .84f) };

        // Four transverse beams are visible from each spawn-facing direction. Two raised side rails create a real
        // grid while leaving the previous duct run visible above the new ceiling band.
        for (int i = 0; i < crossZ.Length; i++)
            BuildRun(group, $"Crossbeam_{i + 1}", CeilingSupport, frame, west, east, crossZ[i], BeamCenterY, alongX: true, count: 6);
        BuildRun(group, "SideRail_West", CeilingSupport, frame, west + 1.15f, south, north, SideBeamCenterY, alongX: false, count: 6);
        BuildRun(group, "SideRail_East", CeilingSupport, frame, east - 1.15f, south, north, SideBeamCenterY, alongX: false, count: 6);

        // Conduits sit on top of alternate transverse members. They are distinct props rather than lights, and are
        // kept in contact with the structure so the static support rule can prove each run is hung.
        for (int i = 0; i < crossZ.Length; i += 2)
            BuildRun(group, $"Conduit_{i / 2 + 1}", Pipe, frame, west + 2.2f, east - 2.2f, crossZ[i] + .32f,
                BeamCenterY + .31f, alongX: true, count: 5, bottomAligned: true);

        // Short wall-hugging brackets and cropped vertical posts make the grid read as supported, but all geometry
        // remains well above the 2.2 m movement-clearance band.
        for (int i = 0; i < crossZ.Length; i++)
        {
            var westBracket = Prop(group, WallSupport, $"West_Bracket_{i + 1}", frame, Wall.West, .18f);
            Top(westBracket, BeamCenterY - .32f);
            Face(westBracket, frame, Wall.West, 0);
            Along(westBracket, frame, Wall.West, crossZ[i], Anchor.Center);
            Mount(westBracket, root, Wall.West);

            var eastPost = Prop(group, Pillar, $"East_Post_{i + 1}", frame, Wall.East, .16f);
            Top(eastPost, BeamCenterY - .32f);
            Face(eastPost, frame, Wall.East, 0);
            Along(eastPost, frame, Wall.East, crossZ[i], Anchor.Center);
            Mount(eastPost, root, Wall.East);
        }

        // Fixtures are mesh-only here; component lights and their budgets remain owned by corebuster_env_015/003.
        for (int i = 0; i < crossZ.Length; i++)
        {
            var light = Prop(group, HangingLight, $"Hanging_Light_{i + 1}", frame, Wall.West);
            RemoveLights(light);
            Top(light, BeamCenterY - .32f);
            Center(light, frame.Center.x + (i % 2 == 0 ? -4.2f : 4.2f), crossZ[i]);
        }

        var sprinklerA = Prop(group, Sprinkler, "Sprinkler_SW", frame, Wall.South);
        Top(sprinklerA, BeamCenterY - .32f);
        Center(sprinklerA, west + 4.5f, crossZ[0]);
        var sprinklerB = Prop(group, Sprinkler, "Sprinkler_NE", frame, Wall.North);
        Top(sprinklerB, BeamCenterY - .32f);
        Center(sprinklerB, east - 4.5f, crossZ[crossZ.Length - 1]);

        var camera = Prop(group, SecurityCamera, "Security_Camera_SW", frame, Wall.South);
        Top(camera, BeamCenterY - .32f);
        Face(camera, frame, Wall.South, 0);
        Along(camera, frame, Wall.South, west + 1.1f, Anchor.Center);
        Mount(camera, root, Wall.South);
    }

    /// <summary>Explicit corebuster_env_007 acceptance rule; overhead is counted separately from floor dressing.</summary>
    public static void Validate(Transform root, StartWorkshopFrame frame, int overheadCount, List<string> errors)
    {
        var group = root.Find(GroupName);
        if (group == null)
        {
            errors.Add($"missing {GroupName}; run Tools/CoreBuster/Build Start Workshop Overhead.");
            return;
        }
        if (overheadCount < 20) errors.Add($"{GroupName} has only {overheadCount} props; the grid, conduit and fixtures are incomplete.");
        int visibleDirections = 0;
        foreach (var axis in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
        {
            bool found = false;
            foreach (Transform prop in group)
            {
                Bounds b = B(prop.gameObject);
                Vector3 delta = b.center - frame.Spawn;
                if (b.min.y >= frame.Floor + StartWorkshopFrame.HeadHeight && b.center.y >= 5.5f && b.center.y <= 7.3f && Vector3.Dot(delta, axis) > 1f)
                {
                    found = true;
                    break;
                }
            }
            if (found) visibleDirections++;
        }
        if (visibleDirections != 4) errors.Add($"{GroupName} is not represented in all four spawn-facing directions ({visibleDirections}/4).");
        foreach (Transform prop in group)
            if (B(prop.gameObject).min.y < frame.Floor + StartWorkshopFrame.HeadHeight)
                errors.Add($"{GroupName}/{prop.name} drops below the {StartWorkshopFrame.HeadHeight:F1} m movement-clearance band.");
    }

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    static void BuildRun(Transform parent, string prefix, string prefabPath, StartWorkshopFrame frame, float startA,
        float endA, float fixedB, float height, bool alongX, int count, bool bottomAligned = false)
    {
        float total = endA - startA;
        float length = (total - SegmentGap * (count - 1)) / count;
        var probe = Prop(parent, prefabPath, prefix + "_Probe", frame, Wall.West);
        Bounds authored = B(probe);
        bool authoredAlongX = authored.size.x >= authored.size.z;
        bool turn = alongX ? !authoredAlongX : authoredAlongX;
        if (turn) probe.transform.Rotate(0, 90, 0, Space.World);
        float authoredLength = alongX ? B(probe).size.x : B(probe).size.z;
        UnityEngine.Object.DestroyImmediate(probe);
        for (int i = 0; i < count; i++)
        {
            var part = Prop(parent, prefabPath, $"{prefix}_{i + 1}", frame, Wall.West);
            if (turn) part.transform.Rotate(0, 90, 0, Space.World);
            var scale = part.transform.localScale;
            if (authoredAlongX) scale.x *= length / authoredLength;
            else scale.z *= length / authoredLength;
            part.transform.localScale = scale;

            float centerA = startA + i * (length + SegmentGap) + length * .5f;
            Center(part, alongX ? centerA : fixedB, alongX ? fixedB : centerA);
            if (bottomAligned) Bottom(part, height); else CenterHeight(part, height);
        }
    }

    static void Center(GameObject go, float x, float z)
    {
        Bounds b = B(go);
        Shift(go, x - b.center.x, 0, z - b.center.z);
    }

    static void CenterHeight(GameObject go, float y)
    {
        Bounds b = B(go);
        Shift(go, 0, y - b.center.y, 0);
    }

    static Transform Require(Transform root, string path)
    {
        var anchor = root.Find(path);
        if (anchor == null)
            throw new InvalidOperationException($"Missing required Start Workshop anchor '{path}'. Run Tools/CoreBuster/Densify Start Workshop before building the overhead pass.");
        return anchor;
    }

    static void RemoveLights(GameObject go)
    {
        foreach (var light in go.GetComponentsInChildren<Light>(true))
            UnityEngine.Object.DestroyImmediate(light);
    }
}

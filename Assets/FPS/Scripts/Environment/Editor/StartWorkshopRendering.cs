using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using static StartWorkshopDresser;

/// <summary>
/// corebuster_render_003: the Start Workshop's bounded URP look-development layer.  It owns a local Volume and its
/// project-owned profile, plus a light-probe grid for the player and characters.  The profile is deliberately local:
/// its trigger bounds end just inside the measured shell, so no grade or bloom can be evaluated in the corridor.
/// Global RenderSettings fog and the URP renderer's SSAO feature are intentionally not changed here; both affect the
/// whole scene and are outside this room-owned pass.
/// </summary>
public static class StartWorkshopRendering
{
    public const string GroupName = "Rendering_003";
    public const string VolumeName = "Workshop_Volume";
    public const string ProbeGroupName = "Character_Light_Probes";
    public const string ProfilePath = "Assets/FPS/Art/Rendering/StartWorkshop_Render_003.asset";

    const string UndoName = "Build Start Workshop Rendering";
    const float ShellInset = .08f;
    const float BlendDistance = .35f;
    const int ProbeColumns = 5;
    const int ProbeRows = 5;
    static readonly float[] ProbeHeights = { 1.25f, 4.8f };

    [MenuItem("Tools/CoreBuster/Build Start Workshop Rendering")]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var root = FindRoom(scene);
        if (root == null) return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        RebuildIn(scene, root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root.Find(GroupName);
        StartWorkshopChecks.Run();
    }

    /// <summary>
    /// Batch entry point:
    /// Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod StartWorkshopRendering.BuildBatch -quit.
    /// The profile and light-probe grid do not need a graphics device; use the review capture tool separately for
    /// visual verification.
    /// </summary>
    public static void BuildBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = FindRoom(scene);
        if (root == null) { EditorApplication.Exit(1); return; }
        RebuildIn(scene, root);
        EditorSceneManager.SaveScene(scene);
        if (!StartWorkshopChecks.Run()) EditorApplication.Exit(1);
    }

    static Transform FindRoom(Scene scene)
    {
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
            Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first; rendering belongs under {LevelName}/{ContainerName}.");
        return root;
    }

    static void RebuildIn(Scene scene, Transform root)
    {
        var old = root.Find(GroupName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var f = StartWorkshopFrame.Measure(scene, root);
        var group = Build(root, f);
        ApplyStaticConvention(group, GameObjectUtility.GetStaticEditorFlags(root.parent.gameObject));
        Undo.RegisterCreatedObjectUndo(group.gameObject, UndoName);
    }

    /// <summary>Called by the full workshop rebuild after lighting has established the room's reflections.</summary>
    public static Transform Build(Transform root, StartWorkshopFrame f)
    {
        if (root.Find(GroupName) != null)
            throw new InvalidOperationException($"{GroupName} already exists. Remove it before rebuilding rendering.");

        var group = Group(root, GroupName);
        var profile = RegenerateProfile();
        BuildVolume(group, f, profile);
        BuildLightProbes(group, f);
        AssetDatabase.SaveAssets();
        Debug.Log($"Start Workshop rendering rebuilt: local URP grade and bloom in {GroupName}/{VolumeName}, " +
                  $"{ProbeColumns * ProbeRows * ProbeHeights.Length} character light probes. {f}");
        return group;
    }

    static void BuildVolume(Transform group, StartWorkshopFrame f, VolumeProfile profile)
    {
        var go = new GameObject(VolumeName);
        go.transform.SetParent(group, false);
        Bounds b = VolumeBounds(f);
        go.transform.position = b.center;

        var volume = go.AddComponent<Volume>();
        volume.isGlobal = false;
        volume.priority = 10f;
        volume.blendDistance = BlendDistance;
        volume.sharedProfile = profile;

        // Volume uses a collider for its local influence. It remains a trigger so neither the player nor the
        // workshop route validator can collide with it.
        var bounds = go.AddComponent<BoxCollider>();
        bounds.isTrigger = true;
        bounds.size = b.size;
    }

    static void BuildLightProbes(Transform group, StartWorkshopFrame f)
    {
        var go = new GameObject(ProbeGroupName);
        go.transform.SetParent(group, false);
        var probes = go.AddComponent<LightProbeGroup>();
        var positions = new List<Vector3>(ProbeColumns * ProbeRows * ProbeHeights.Length);
        for (int y = 0; y < ProbeHeights.Length; y++)
            for (int z = 0; z < ProbeRows; z++)
                for (int x = 0; x < ProbeColumns; x++)
                {
                    float px = Mathf.Lerp(f.West + 1f, f.East - 1f, x / (float)(ProbeColumns - 1));
                    float pz = Mathf.Lerp(f.South + 1f, f.North - 1f, z / (float)(ProbeRows - 1));
                    positions.Add(go.transform.InverseTransformPoint(new Vector3(px, f.Floor + ProbeHeights[y], pz)));
                }
        probes.probePositions = positions.ToArray();
    }

    static VolumeProfile RegenerateProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            EnsureFolders(ProfilePath);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "StartWorkshop_Render_003";
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        // This is an owned generated profile: remove stale effects so a hand Inspector change cannot make the local
        // grade nondeterministic or add a costly effect without a builder change.
        foreach (var component in profile.components.ToArray())
        {
            if (component == null) continue;
            profile.Remove(component.GetType());
            AssetDatabase.RemoveObjectFromAsset(component);
            UnityEngine.Object.DestroyImmediate(component, true);
        }

        var bloom = Add<Bloom>(profile);
        bloom.threshold.value = 1.05f;
        bloom.intensity.value = .35f;
        bloom.scatter.value = .62f;
        bloom.clamp.value = 4f;
        bloom.tint.value = new Color(1f, .78f, .52f);
        bloom.highQualityFiltering.value = false;
        bloom.downscale.value = BloomDownscaleMode.Quarter;
        bloom.maxIterations.value = 5;

        var grade = Add<ColorAdjustments>(profile);
        grade.postExposure.value = .12f;
        grade.contrast.value = 16f;
        grade.colorFilter.value = new Color(1f, .94f, .84f);
        grade.saturation.value = -5f;

        var vignette = Add<Vignette>(profile);
        vignette.color.value = Color.black;
        vignette.center.value = new Vector2(.5f, .5f);
        vignette.intensity.value = .16f;
        vignette.smoothness.value = .32f;
        vignette.rounded.value = false;

        var tonemapping = Add<Tonemapping>(profile);
        tonemapping.mode.value = TonemappingMode.Neutral;

        profile.Reset();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    static T Add<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var component = profile.Add<T>(true);
        component.name = typeof(T).Name;
        component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    static void EnsureFolders(string assetPath)
    {
        var parts = assetPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length - 1; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    static Bounds VolumeBounds(StartWorkshopFrame f)
    {
        var min = new Vector3(f.West + ShellInset, f.Floor + ShellInset, f.South + ShellInset);
        var max = new Vector3(f.East - ShellInset, f.Ceiling - ShellInset, f.North - ShellInset);
        var result = new Bounds();
        result.SetMinMax(min, max);
        return result;
    }

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    /// <summary>Static contracts for the local grade. Scene-wide fog and SSAO remain intentionally out of scope.</summary>
    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        var group = root.Find(GroupName);
        if (group == null)
        {
            errors.Add($"missing {GroupName}; run Tools/CoreBuster/Build Start Workshop Rendering.");
            return;
        }
        if (root.GetComponentsInChildren<Volume>(true).Length != 1)
            errors.Add($"The workshop needs exactly one local Volume in {GroupName}.");
        var volume = group.Find(VolumeName)?.GetComponent<Volume>();
        var collider = group.Find(VolumeName)?.GetComponent<BoxCollider>();
        if (volume == null || collider == null) errors.Add($"{GroupName}/{VolumeName} needs a Volume and BoxCollider.");
        else
        {
            if (volume.isGlobal) errors.Add($"{GroupName}/{VolumeName} must be local, never global.");
            if (volume.sharedProfile == null || AssetDatabase.GetAssetPath(volume.sharedProfile) != ProfilePath)
                errors.Add($"{GroupName}/{VolumeName} must use {ProfilePath}.");
            if (!collider.isTrigger) errors.Add($"{GroupName}/{VolumeName}'s bounds must be a trigger.");
            Bounds expected = VolumeBounds(f);
            if ((collider.bounds.min - expected.min).magnitude > .01f || (collider.bounds.max - expected.max).magnitude > .01f)
                errors.Add($"{GroupName}/{VolumeName}'s bounds must remain inside the workshop shell.");
            if (Mathf.Abs(volume.blendDistance - BlendDistance) > .001f)
                errors.Add($"{GroupName}/{VolumeName} must use a {BlendDistance:F2} m interior blend distance.");
            ValidateProfile(volume.sharedProfile, errors);
        }

        var probeGroups = root.GetComponentsInChildren<LightProbeGroup>(true);
        var probes = group.Find(ProbeGroupName)?.GetComponent<LightProbeGroup>();
        int expectedProbes = ProbeColumns * ProbeRows * ProbeHeights.Length;
        if (probeGroups.Length != 1 || probes == null) errors.Add($"The workshop needs exactly one {GroupName}/{ProbeGroupName}.");
        else if (probes.probePositions == null || probes.probePositions.Length != expectedProbes)
            errors.Add($"{GroupName}/{ProbeGroupName} needs {expectedProbes} probes for character lighting.");
        else
            foreach (var p in probes.probePositions.Select(probes.transform.TransformPoint))
                if (p.x <= f.West || p.x >= f.East || p.y <= f.Floor || p.y >= f.Ceiling || p.z <= f.South || p.z >= f.North)
                {
                    errors.Add($"{GroupName}/{ProbeGroupName} has a probe outside the room.");
                    break;
                }
    }

    static void ValidateProfile(VolumeProfile profile, List<string> errors)
    {
        if (profile == null) return;
        if (profile.components.Count != 4) errors.Add($"{ProfilePath} must contain only Bloom, Color Adjustments, Vignette and Tonemapping.");
        var bloom = profile.components.OfType<Bloom>().FirstOrDefault();
        var grade = profile.components.OfType<ColorAdjustments>().FirstOrDefault();
        var vignette = profile.components.OfType<Vignette>().FirstOrDefault();
        var tone = profile.components.OfType<Tonemapping>().FirstOrDefault();
        if (bloom == null || bloom.intensity.value <= 0 || bloom.downscale.value != BloomDownscaleMode.Quarter)
            errors.Add($"{ProfilePath} needs its low-cost amber Bloom override.");
        if (grade == null || grade.contrast.value < 10 || grade.colorFilter.value.b >= grade.colorFilter.value.r)
            errors.Add($"{ProfilePath} needs a warm, contrasty Color Adjustments override.");
        if (vignette == null || vignette.intensity.value <= 0)
            errors.Add($"{ProfilePath} needs its Vignette override.");
        if (tone == null || tone.mode.value != TonemappingMode.Neutral)
            errors.Add($"{ProfilePath} needs Neutral Tonemapping.");
    }
}

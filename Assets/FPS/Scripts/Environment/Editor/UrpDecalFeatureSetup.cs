using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// corebuster_render_002: owns the one global change the Start Workshop grime decals need, a URP
/// <see cref="DecalRendererFeature"/> on the project's forward renderer
/// (<c>Assets/Rendering/Default_PipelineAsset_ForwardRenderer.asset</c>). The renderer is shared by every scene in the build,
/// so the feature is added and configured only here, reproducibly, and the settings are checked by the workshop validator.
/// <list type="bullet">
/// <item><b>DBuffer.</b> Decals are drawn into a small buffer that the receiving materials read, so a decal takes exactly the
/// same lighting as the surface under it. A screen-space decal would be lit as a separate box mesh by only the box's own
/// four per-object lights, and the room has 22 lamps.</item>
/// <item><b>Albedo and normal only.</b> Two buffer targets instead of three; the grime decals never write metallic, occlusion
/// or smoothness.</item>
/// <item><b>Decal layers.</b> A decal draws only on renderers that share one of its rendering-layer bits, so characters and weapons,
/// which stay on the default layer, are never painted (<see cref="StartWorkshopGrime.LayerIndex"/>).</item>
/// </list>
/// The feature is enabled and configured through its serialized settings because the settings type is internal to URP.
/// </summary>
public static class UrpDecalFeatureSetup
{
    public const string RendererPath = "Assets/Rendering/Default_PipelineAsset_ForwardRenderer.asset";
    public const string DecalTemplateMaterial = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Decal.mat";

    // URP's DecalTechniqueOption (Automatic 0, DBuffer 1, ScreenSpace 2) and DecalSurfaceData (Albedo 0, AlbedoNormal 1,
    // AlbedoNormalMAOS 2). Both enums are internal, so their serialized values are used.
    const int TechniqueDBuffer = 1, SurfaceAlbedoNormal = 1;
    // The room is about 45 m across its diagonal; nothing farther than this needs its decals.
    const float MaxDrawDistance = 80f;

    public static UniversalRendererData LoadRenderer() => AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);

    public static DecalRendererFeature Find(UniversalRendererData data) =>
        data == null ? null : data.rendererFeatures.OfType<DecalRendererFeature>().FirstOrDefault();

    [MenuItem("Tools/CoreBuster/Enable URP Decal Renderer Feature")]
    public static void EnableFromMenu()
    {
        try { Debug.Log(Ensure()); }
        catch (Exception e) { Debug.LogError("Could not enable the URP decal feature: " + e.Message); }
    }

    /// <summary>Batch: -executeMethod UrpDecalFeatureSetup.EnableBatch. Adds and configures the feature, then exits nonzero on failure.</summary>
    public static void EnableBatch()
    {
        try { Debug.Log(Ensure()); }
        catch (Exception e)
        {
            Debug.LogError("Could not enable the URP decal feature: " + e.Message);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// Adds the decal feature to the forward renderer if it is missing (as the renderer Inspector's Add Renderer Feature does:
    /// a sub-asset of the renderer, listed in its feature array and local-id map) and writes the settings above. Rerunning
    /// changes nothing. Returns a one-line description of what happened.
    /// </summary>
    public static string Ensure()
    {
        var data = LoadRenderer() ?? throw new InvalidOperationException($"Missing forward renderer {RendererPath}.");
        var feature = Find(data);
        bool added = false;
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<DecalRendererFeature>();
            feature.name = nameof(DecalRendererFeature);
            AssetDatabase.AddObjectToAsset(feature, data);
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId))
                throw new InvalidOperationException("Unity gave the new decal feature no local file id.");
            var renderer = new SerializedObject(data);
            var features = renderer.FindProperty("m_RendererFeatures");
            var map = renderer.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            renderer.ApplyModifiedProperties();
            added = true;
        }

        bool configured = Configure(feature);
        if (added || configured)
        {
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }
        return added ? $"Added the URP decal feature (DBuffer, albedo + normal, decal layers) to {RendererPath}."
            : configured ? $"Reconfigured the existing URP decal feature in {RendererPath}."
            : $"The URP decal feature in {RendererPath} was already configured.";
    }

    static bool Configure(DecalRendererFeature feature)
    {
        var so = new SerializedObject(feature);
        bool changed = false;
        changed |= Set(so.FindProperty("m_Settings.technique"), TechniqueDBuffer);
        changed |= Set(so.FindProperty("m_Settings.decalLayers"), true);
        changed |= Set(so.FindProperty("m_Settings.dBufferSettings.surfaceData"), SurfaceAlbedoNormal);
        changed |= Set(so.FindProperty("m_Settings.maxDrawDistance"), MaxDrawDistance);
        if (changed) so.ApplyModifiedProperties();
        if (!feature.isActive)
        {
            feature.SetActive(true);
            changed = true;
        }
        return changed;
    }

    static bool Set(SerializedProperty p, int value)
    {
        if (p == null) throw new InvalidOperationException("URP's DecalRendererFeature no longer has the expected settings; update UrpDecalFeatureSetup.");
        if (p.intValue == value) return false;
        p.intValue = value;
        return true;
    }

    static bool Set(SerializedProperty p, bool value)
    {
        if (p == null) throw new InvalidOperationException("URP's DecalRendererFeature no longer has the expected settings; update UrpDecalFeatureSetup.");
        if (p.boolValue == value) return false;
        p.boolValue = value;
        return true;
    }

    static bool Set(SerializedProperty p, float value)
    {
        if (p == null) throw new InvalidOperationException("URP's DecalRendererFeature no longer has the expected settings; update UrpDecalFeatureSetup.");
        if (Mathf.Approximately(p.floatValue, value)) return false;
        p.floatValue = value;
        return true;
    }

    /// <summary>The validator rule: exactly one active decal feature on the forward renderer, configured as <see cref="Ensure"/> writes it.</summary>
    public static void Validate(List<string> errors)
    {
        var data = LoadRenderer();
        if (data == null)
        {
            errors.Add($"Missing forward renderer {RendererPath}.");
            return;
        }
        var features = data.rendererFeatures.OfType<DecalRendererFeature>().ToList();
        if (features.Count != 1)
        {
            errors.Add($"{RendererPath} needs exactly one URP Decal renderer feature (found {features.Count}); run Tools/CoreBuster/Enable URP Decal Renderer Feature.");
            return;
        }
        var feature = features[0];
        if (!feature.isActive) errors.Add("The URP decal renderer feature is disabled; grime decals will not draw.");
        var so = new SerializedObject(feature);
        if (so.FindProperty("m_Settings.technique")?.intValue != TechniqueDBuffer)
            errors.Add("The URP decal feature must use the DBuffer technique (decals take the surface's own lighting).");
        if (so.FindProperty("m_Settings.decalLayers")?.boolValue != true)
            errors.Add("The URP decal feature must enable decal layers, or grime would paint characters and weapons.");
    }
}

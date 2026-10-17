using System.Linq;
using Unity.FPS.Game;
using Unity.FPS.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;
using UnityEngine.Rendering.Universal;

/// <summary>
/// corebuster_render_012. Sets up the two graphics qualities offered in Options:
/// <list type="bullet">
/// <item><b>Normal</b>: the existing Default_PipelineAsset and its forward renderer (SSAO), unchanged. It is the
/// default, tuned for the presentation machine, and keeps ray tracing off.</item>
/// <item><b>Ultra</b>: Ultra_PipelineAsset, a copy with 4x MSAA, 2048 two-cascade sun shadows, a 4096 spot-shadow
/// atlas and high soft-shadow quality. It has two renderers: Ultra_ForwardRenderer (a copy of the default, SSAO) and
/// Ultra_RayTracedRenderer (SSAO swapped for <see cref="RayTracedAmbientOcclusion"/>), which cameras use while ray
/// tracing is on (<see cref="RayTracingWorld"/>).</item>
/// </list>
/// Per-object light limits stay at 4 in both, so the rooms' lighting looks as designed. Safe to re-run: existing
/// Ultra assets are updated in place.
/// </summary>
public static class RenderQualitySetup
{
    const string RenderingRoot = "Assets/Rendering";
    const string DefaultAsset = RenderingRoot + "/Default_PipelineAsset.asset";
    const string DefaultRenderer = RenderingRoot + "/Default_PipelineAsset_ForwardRenderer.asset";
    const string UltraAsset = RenderingRoot + "/Ultra_PipelineAsset.asset";
    const string UltraRenderer = RenderingRoot + "/Ultra_ForwardRenderer.asset";
    const string UltraRayTracedRenderer = RenderingRoot + "/Ultra_RayTracedRenderer.asset";
    const string TraceShader = "Assets/FPS/Art/Shaders/CoreBuster/RayTracing/RayTracedAO.urtshader";
    const string DenoiseShader = "Assets/FPS/Art/Shaders/CoreBuster/RayTracing/RayTracedAODenoise.compute";

    [MenuItem("Tools/CoreBuster/Setup Render Quality (Normal + Ultra)")]
    public static void Setup()
    {
        var normal = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(DefaultAsset);
        if (!normal || !AssetDatabase.LoadAssetAtPath<UniversalRendererData>(DefaultRenderer))
        {
            Debug.LogError("Render quality setup: the default pipeline asset or renderer is missing.");
            return;
        }

        var ultraRenderer = CopyOnce<UniversalRendererData>(DefaultRenderer, UltraRenderer);
        var rayTracedRenderer = CopyOnce<UniversalRendererData>(DefaultRenderer, UltraRayTracedRenderer);
        var ultra = CopyOnce<UniversalRenderPipelineAsset>(DefaultAsset, UltraAsset);
        ConfigureRayTracedRenderer(rayTracedRenderer);
        ConfigureUltra(ultra, ultraRenderer, rayTracedRenderer);
        ConfigureQualityLevels(normal, ultra);
        AssetDatabase.SaveAssets();
        Debug.Log("RENDER QUALITY SET UP: Normal (default, SSAO) and Ultra (MSAA, high shadows, ray-traced AO renderer).");
    }

    static T CopyOnce<T>(string source, string destination) where T : Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(destination);
        if (existing) return existing;
        AssetDatabase.CopyAsset(source, destination);
        return AssetDatabase.LoadAssetAtPath<T>(destination);
    }

    static void ConfigureRayTracedRenderer(UniversalRendererData data)
    {
        var so = new SerializedObject(data);
        var features = so.FindProperty("m_RendererFeatures");
        var map = so.FindProperty("m_RendererFeatureMap");

        // SSAO out: the ray-traced pass writes the same occlusion texture.
        for (int i = features.arraySize - 1; i >= 0; i--)
        {
            var feature = features.GetArrayElementAtIndex(i).objectReferenceValue;
            if (feature is not ScreenSpaceAmbientOcclusion) continue;
            features.DeleteArrayElementAtIndex(i);
            map.DeleteArrayElementAtIndex(i);
            Object.DestroyImmediate(feature, true);
        }

        var rtao = data.rendererFeatures.OfType<RayTracedAmbientOcclusion>().FirstOrDefault();
        if (!rtao)
        {
            rtao = ScriptableObject.CreateInstance<RayTracedAmbientOcclusion>();
            rtao.name = "RayTracedAmbientOcclusion";
            AssetDatabase.AddObjectToAsset(rtao, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(rtao, out string _, out long localId);
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = rtao;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        var all = AssetDatabase.LoadAllAssetsAtPath(TraceShader);
        var resources = new RayTracingResources();
        resources.Load();
        rtao.shaders = new RayTracingWorld.Shaders
        {
            TraceCompute = all.OfType<ComputeShader>().FirstOrDefault(),
            TraceHardware = all.OfType<RayTracingShader>().FirstOrDefault(),
            GeometryPoolKernels = resources.geometryPoolKernels,
            CopyBuffer = resources.copyBuffer,
            CopyPositions = resources.copyPositions,
            BitHistogram = resources.bitHistogram,
            BlockReducePart = resources.blockReducePart,
            BlockScan = resources.blockScan,
            BuildHlbvh = resources.buildHlbvh,
            RestructureBvh = resources.restructureBvh,
            Scatter = resources.scatter,
        };
        rtao.denoise = AssetDatabase.LoadAssetAtPath<ComputeShader>(DenoiseShader);
        rtao.SetActive(true);
        EditorUtility.SetDirty(rtao);
        EditorUtility.SetDirty(data);
        if (!rtao.shaders.IsComplete || !rtao.denoise)
            Debug.LogError("Render quality setup: ray-traced AO shaders are missing (" + TraceShader + ").");
    }

    static void ConfigureUltra(UniversalRenderPipelineAsset ultra, ScriptableRendererData forward, ScriptableRendererData rayTraced)
    {
        var so = new SerializedObject(ultra);
        var list = so.FindProperty("m_RendererDataList");
        list.arraySize = 2;
        list.GetArrayElementAtIndex(0).objectReferenceValue = forward;
        list.GetArrayElementAtIndex(RayTracingWorld.RayTracedRendererIndex).objectReferenceValue = rayTraced;
        so.FindProperty("m_DefaultRendererIndex").intValue = 0;
        Set(so, "m_MSAA", 4);
        Set(so, "m_RenderScale", 1f);
        Set(so, "m_MainLightShadowmapResolution", 2048);
        Set(so, "m_ShadowCascadeCount", 2);
        Set(so, "m_ShadowDistance", 70f);
        Set(so, "m_AdditionalLightsShadowmapResolution", 4096);
        Set(so, "m_SoftShadowQuality", 3);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ultra);
    }

    static void Set(SerializedObject so, string name, int value)
    {
        var property = so.FindProperty(name);
        if (property != null) property.intValue = value;
        else Debug.LogWarning($"Render quality setup: no {name} on the pipeline asset.");
    }

    static void Set(SerializedObject so, string name, float value)
    {
        var property = so.FindProperty(name);
        if (property != null) property.floatValue = value;
        else Debug.LogWarning($"Render quality setup: no {name} on the pipeline asset.");
    }

    static void ConfigureQualityLevels(RenderPipelineAsset normal, RenderPipelineAsset ultra)
    {
        var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
        if (!settings)
        {
            Debug.LogError("Render quality setup: ProjectSettings/QualitySettings.asset not found.");
            return;
        }
        var so = new SerializedObject(settings);
        var levels = so.FindProperty("m_QualitySettings");
        // Duplicate the existing level, so both keep its texture, LOD and shadow settings.
        while (levels.arraySize < 2) levels.InsertArrayElementAtIndex(0);
        while (levels.arraySize > 2) levels.DeleteArrayElementAtIndex(levels.arraySize - 1);

        var low = levels.GetArrayElementAtIndex(0);
        low.FindPropertyRelative("name").stringValue = "Normal";
        low.FindPropertyRelative("customRenderPipeline").objectReferenceValue = normal;

        var high = levels.GetArrayElementAtIndex(1);
        high.FindPropertyRelative("name").stringValue = GameSettings.UltraQualityName;
        high.FindPropertyRelative("customRenderPipeline").objectReferenceValue = ultra;
        high.FindPropertyRelative("anisotropicTextures").intValue = 2;

        so.FindProperty("m_CurrentQuality").intValue = 0;
        var defaults = so.FindProperty("m_PerPlatformDefaultQuality");
        for (int i = 0; defaults != null && i < defaults.arraySize; i++)
            defaults.GetArrayElementAtIndex(i).FindPropertyRelative("second").intValue = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        QualitySettings.SetQualityLevel(0, true);
    }
}

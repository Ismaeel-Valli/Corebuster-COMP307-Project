using System;
using System.Collections;
using System.Diagnostics;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Unity.FPS.Rendering
{
    /// <summary>
    /// The ray-tracing side of Ultra quality. It owns Unity's Unified Ray Tracing context and an acceleration
    /// structure (a BVH) of the loaded level's static geometry, and switches cameras to the ray-traced renderer while
    /// ray tracing is on.
    /// <list type="bullet">
    /// <item>Backend: hardware ray tracing (DXR) when the GPU and graphics API support it, otherwise a compute-shader
    /// BVH traversal that runs on any GPU with compute shaders. The compute path is slow on weak GPUs, which is why
    /// ray tracing is off on Normal quality.</item>
    /// <item>The structure is built after each scene load (the loading screen waits for it through
    /// <see cref="LoadingGate"/>) or when ray tracing is switched on. Only static level geometry goes in: anything
    /// under a CoreBuster script (characters, doors, weapons, the giant core), animated or physics-driven is left
    /// out, as are transparent surfaces.</item>
    /// <item>Cameras use the pipeline asset's renderer <see cref="RayTracedRendererIndex"/> (Ultra's ray-traced
    /// renderer, which swaps SSAO for <see cref="RayTracedAmbientOcclusion"/>) only once the structure is ready.</item>
    /// </list>
    /// </summary>
    public static class RayTracingWorld
    {
        public const int RayTracedRendererIndex = 1;

        [Serializable]
        public class Shaders
        {
            [Tooltip("RayTracedAO.urtshader (its main compute-shader asset).")]
            public ComputeShader TraceCompute;
            [Tooltip("RayTracedAO.urtshader's RayTracingShader sub-asset, for hardware ray tracing.")]
            public RayTracingShader TraceHardware;

            [Header("Unified Ray Tracing kernels (core package)")]
            public ComputeShader GeometryPoolKernels;
            public ComputeShader CopyBuffer;
            public ComputeShader CopyPositions;
            public ComputeShader BitHistogram;
            public ComputeShader BlockReducePart;
            public ComputeShader BlockScan;
            public ComputeShader BuildHlbvh;
            public ComputeShader RestructureBvh;
            public ComputeShader Scatter;

            public bool IsComplete => TraceCompute && GeometryPoolKernels && CopyBuffer && CopyPositions &&
                                      BitHistogram && BlockReducePart && BlockScan && BuildHlbvh && RestructureBvh &&
                                      Scatter;

            public RayTracingResources ToResources() => new RayTracingResources
            {
                geometryPoolKernels = GeometryPoolKernels,
                copyBuffer = CopyBuffer,
                copyPositions = CopyPositions,
                bitHistogram = BitHistogram,
                blockReducePart = BlockReducePart,
                blockScan = BlockScan,
                buildHlbvh = BuildHlbvh,
                restructureBvh = RestructureBvh,
                scatter = Scatter,
            };
        }

        static Shaders s_Shaders;
        static RayTracingContext s_Context;
        static IRayTracingShader s_Shader;
        static IRayTracingAccelStruct s_AccelStruct;
        static GraphicsBuffer s_BuildScratch;
        static Runner s_Runner;
        static bool s_Building;
        static bool s_Failed;
        static bool s_Hooked;
        static Scene s_BuiltForScene;

        public static bool Ready => s_AccelStruct != null && s_Shader != null && !s_Building;
        public static bool Wanted => GameSettings.RayTracing && GameSettings.IsUltra && s_Shaders != null && !s_Failed;
        public static IRayTracingShader Shader => s_Shader;
        public static IRayTracingAccelStruct AccelStruct => s_AccelStruct;
        public static RayTracingBackend Backend => s_Context != null ? s_Context.BackendType : RayTracingBackend.Compute;
        public static int InstanceCount { get; private set; }

        /// <summary>Called by the ray-traced AO renderer feature with its shader references.</summary>
        public static void Register(Shaders shaders)
        {
            if (shaders == null || !shaders.IsComplete) return;
            s_Shaders = shaders;
            Hook();
        }

        // The project enters play mode without a domain reload: drop last session's GPU objects and hooks.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            Unhook();
            DisposeStructure();
            s_Failed = false;
            s_Building = false;
            s_Runner = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartSession() => Hook();

        static void Hook()
        {
            if (s_Hooked || !Application.isPlaying) return;
            s_Hooked = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
            GameSettings.GraphicsChanged += OnGraphicsChanged;
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            Application.quitting += DisposeAll;
        }

        static void Unhook()
        {
            s_Hooked = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            GameSettings.GraphicsChanged -= OnGraphicsChanged;
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            Application.quitting -= DisposeAll;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            DisposeStructure();
            if (Wanted) RequestBuild();
        }

        static void OnGraphicsChanged()
        {
            if (Wanted && !Ready) RequestBuild();
        }

        // Ultra's ray-traced renderer for game cameras while ray tracing is on and ready; the default otherwise.
        static void OnBeginContextRendering(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
        {
            if (!Application.isPlaying) return;
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!asset) return;
            bool hasRayTracedRenderer = asset.rendererDataList.Length > RayTracedRendererIndex;
            // URP creates renderers on first use; creating the ray-traced one registers its shaders here.
            if (s_Shaders == null && hasRayTracedRenderer && GameSettings.RayTracing && GameSettings.IsUltra)
                asset.GetRenderer(RayTracedRendererIndex);
            bool wanted = Wanted;
            if (wanted && !Ready) RequestBuild();
            int index = wanted && Ready && hasRayTracedRenderer ? RayTracedRendererIndex : -1;
            foreach (var camera in cameras)
            {
                if (camera.cameraType != CameraType.Game) continue;
                if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) ||
                    data.renderType != CameraRenderType.Base) continue;
                data.SetRenderer(index);
            }
        }

        static void RequestBuild()
        {
            if (s_Building || s_Failed || s_Shaders == null) return;
            var scene = SceneManager.GetActiveScene();
            if (s_AccelStruct != null && s_BuiltForScene == scene) return;
            if (!s_Runner)
            {
                var go = new GameObject("Ray Tracing World") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(go);
                s_Runner = go.AddComponent<Runner>();
            }
            s_Building = true;
            s_Runner.StartCoroutine(Build(scene));
        }

        static IEnumerator Build(Scene scene)
        {
            var gateKey = new object();
            LoadingGate.Begin(gateKey);
            var clock = Stopwatch.StartNew();
            try
            {
                if (!CreateContext())
                {
                    s_Failed = true;
                    yield break;
                }
                DisposeStructure();
                s_AccelStruct = s_Context.CreateAccelerationStructure(new AccelerationStructureOptions
                    { buildFlags = BuildFlags.PreferFastTrace });

                int added = 0, failed = 0;
                var slice = Stopwatch.StartNew();
                foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
                {
                    if (!Qualifies(renderer) || !renderer.TryGetComponent<MeshFilter>(out var filter)) continue;
                    var mesh = filter.sharedMesh;
                    if (!mesh) continue;
                    var materials = renderer.sharedMaterials;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var material = s < materials.Length ? materials[s] : null;
                        if (material && material.renderQueue >= (int)RenderQueue.Transparent) continue;
                        if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                        var instance = new MeshInstanceDesc(mesh, s)
                        {
                            localToWorldMatrix = renderer.localToWorldMatrix,
                            // Both faces occlude: plenty of the kit is single-sided.
                            enableTriangleCulling = false,
                            opaqueGeometry = true,
                            mask = 1u,
                            instanceID = (uint)added,
                        };
                        try
                        {
                            s_AccelStruct.AddInstance(instance);
                            added++;
                        }
                        catch (Exception e)
                        {
                            if (failed++ < 3) Debug.LogWarning($"Ray tracing: skipped {renderer.name} ({e.Message})", renderer);
                        }
                    }
                    if (slice.ElapsedMilliseconds > 25)
                    {
                        yield return null;
                        slice.Restart();
                        // A newer scene replaced the one being built: start again for it.
                        if (SceneManager.GetActiveScene() != scene) yield break;
                    }
                }

                var cmd = new CommandBuffer { name = "Build ray tracing acceleration structure" };
                RayTracingHelper.ResizeScratchBufferForBuild(s_AccelStruct, ref s_BuildScratch);
                s_AccelStruct.Build(cmd, s_BuildScratch);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();
                InstanceCount = added;
                s_BuiltForScene = scene;
                yield return null;
                Debug.Log($"Ray tracing ready ({s_Context.BackendType} backend): {added} mesh instances" +
                          (failed > 0 ? $", {failed} skipped" : "") + $", built in {clock.ElapsedMilliseconds} ms.");
            }
            finally
            {
                s_Building = false;
                LoadingGate.End(gateKey);
            }
        }

        static bool CreateContext()
        {
            if (s_Context != null) return s_Shader != null;
            try
            {
                bool hardware = RayTracingContext.IsBackendSupported(RayTracingBackend.Hardware) && s_Shaders.TraceHardware;
                var backend = hardware ? RayTracingBackend.Hardware : RayTracingBackend.Compute;
                if (!RayTracingContext.IsBackendSupported(backend))
                {
                    Debug.LogWarning("Ray tracing is not supported on this GPU (no compute shaders).");
                    return false;
                }
                s_Context = new RayTracingContext(backend, s_Shaders.ToResources());
                s_Shader = s_Context.CreateRayTracingShader(hardware ? s_Shaders.TraceHardware : (UnityEngine.Object)s_Shaders.TraceCompute);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Ray tracing could not start: " + e.Message);
                s_Context?.Dispose();
                s_Context = null;
                s_Shader = null;
                return false;
            }
        }

        // Static level geometry only.
        static bool Qualifies(MeshRenderer renderer)
        {
            if (!renderer.enabled || renderer.gameObject.layer == 5) return false;
            for (var t = renderer.transform; t; t = t.parent)
            {
                if (t.TryGetComponent<Rigidbody>(out _) || t.TryGetComponent<Animator>(out _) ||
                    t.TryGetComponent<Canvas>(out _)) return false;
                foreach (var behaviour in t.GetComponents<MonoBehaviour>())
                    if (behaviour && (behaviour.GetType().Namespace ?? "").StartsWith("Unity.FPS", StringComparison.Ordinal))
                        return false;
            }
            return true;
        }

        static void DisposeStructure()
        {
            s_AccelStruct?.Dispose();
            s_AccelStruct = null;
            s_BuildScratch?.Dispose();
            s_BuildScratch = null;
            s_BuiltForScene = default;
            InstanceCount = 0;
        }

        static void DisposeAll()
        {
            DisposeStructure();
            s_Shader = null;
            s_Context?.Dispose();
            s_Context = null;
        }

        sealed class Runner : MonoBehaviour
        {
            void OnDestroy()
            {
                if (s_Runner == this) DisposeAll();
            }
        }
    }
}

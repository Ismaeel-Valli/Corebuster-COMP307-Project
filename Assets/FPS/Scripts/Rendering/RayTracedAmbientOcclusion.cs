using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Unity.FPS.Rendering
{
    /// <summary>
    /// Ray-traced ambient occlusion for Ultra quality's ray-traced renderer (in place of SSAO). After the depth and
    /// normals prepass it traces short rays from every visible surface against <see cref="RayTracingWorld"/>'s
    /// acceleration structure, so occlusion comes from the real geometry, including what is off screen or behind
    /// other surfaces, which screen-space AO cannot see. The noisy trace is accumulated over frames, upsampled with a
    /// depth-aware filter, and published as URP's _ScreenSpaceOcclusionTexture, which every URP Lit material already
    /// applies to its ambient (and, by DirectLightingStrength, direct) lighting.
    /// </summary>
    [DisallowMultipleRendererFeature("CoreBuster Ray Traced Ambient Occlusion")]
    public class RayTracedAmbientOcclusion : ScriptableRendererFeature
    {
        [Serializable]
        public class Settings
        {
            [Tooltip("How far the occlusion rays reach, in metres.")]
            [Range(.25f, 4f)] public float Radius = 1.6f;
            [Range(1, 8)] public int RaysPerPixel = 2;
            [Tooltip("Trace at half resolution and upsample (a quarter of the rays).")]
            public bool HalfResolution = true;
            [Range(0f, 1f)] public float Intensity = 1f;
            [Tooltip("How much the occlusion also darkens direct lighting (URP SSAO's setting of the same name).")]
            [Range(0f, 1f)] public float DirectLightingStrength = .25f;
            [Tooltip("Weight of each new frame in the running average (lower is smoother but slower to react).")]
            [Range(.02f, 1f)] public float TemporalBlend = .15f;
            [Tooltip("Flips the depth texture's rows relative to clip space (debug: off is correct with URP's intermediate textures).")]
            public bool InvertY;
        }

        public Settings settings = new Settings();
        public RayTracingWorld.Shaders shaders = new RayTracingWorld.Shaders();
        public ComputeShader denoise;

        Pass m_Pass;

        public override void Create()
        {
            RayTracingWorld.Register(shaders);
            m_Pass?.Dispose();
            m_Pass = new Pass(this) { renderPassEvent = RenderPassEvent.AfterRenderingPrePasses };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!denoise || !RayTracingWorld.Wanted || !RayTracingWorld.Ready) return;
            ref var cameraData = ref renderingData.cameraData;
            if (cameraData.camera.cameraType != CameraType.Game || cameraData.renderType != CameraRenderType.Base) return;
            m_Pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass?.Dispose();
            m_Pass = null;
        }

        sealed class Pass : ScriptableRenderPass, IDisposable
        {
            static readonly GlobalKeyword k_ScreenSpaceOcclusion = GlobalKeyword.Create("_SCREEN_SPACE_OCCLUSION");
            static readonly int k_OcclusionTexture = Shader.PropertyToID("_ScreenSpaceOcclusionTexture");
            static readonly int k_OcclusionParam = Shader.PropertyToID("_AmbientOcclusionParam");

            static readonly int k_RtDepth = Shader.PropertyToID("_RTAO_Depth");
            static readonly int k_RtNormals = Shader.PropertyToID("_RTAO_Normals");
            static readonly int k_RtOutput = Shader.PropertyToID("_RTAO_Output");
            static readonly int k_RtInvViewProj = Shader.PropertyToID("_RTAO_InvViewProj");
            static readonly int k_RtTraceSize = Shader.PropertyToID("_RTAO_TraceSize");
            static readonly int k_RtDepthSize = Shader.PropertyToID("_RTAO_DepthSize");
            static readonly int k_RtCameraPos = Shader.PropertyToID("_RTAO_CameraPos");
            static readonly int k_RtRadius = Shader.PropertyToID("_RTAO_Radius");
            static readonly int k_RtFlipY = Shader.PropertyToID("_RTAO_FlipY");
            static readonly int k_RtRayCount = Shader.PropertyToID("_RTAO_RayCount");
            static readonly int k_RtFrame = Shader.PropertyToID("_RTAO_Frame");

            static readonly int k_Depth = Shader.PropertyToID("_Depth");
            static readonly int k_Current = Shader.PropertyToID("_Current");
            static readonly int k_History = Shader.PropertyToID("_History");
            static readonly int k_HistoryOut = Shader.PropertyToID("_HistoryOut");
            static readonly int k_Accumulated = Shader.PropertyToID("_Accumulated");
            static readonly int k_Result = Shader.PropertyToID("_Result");
            static readonly int k_InvViewProj = Shader.PropertyToID("_InvViewProj");
            static readonly int k_PrevViewProj = Shader.PropertyToID("_PrevViewProj");
            static readonly int k_CameraPos = Shader.PropertyToID("_CameraPos");
            static readonly int k_PrevCameraPos = Shader.PropertyToID("_PrevCameraPos");
            static readonly int k_TraceSize = Shader.PropertyToID("_TraceSize");
            static readonly int k_DepthSize = Shader.PropertyToID("_DepthSize");
            static readonly int k_FullSize = Shader.PropertyToID("_FullSize");
            static readonly int k_FlipY = Shader.PropertyToID("_FlipY");
            static readonly int k_Blend = Shader.PropertyToID("_Blend");
            static readonly int k_Intensity = Shader.PropertyToID("_Intensity");
            static readonly int k_HistoryValid = Shader.PropertyToID("_HistoryValid");

            readonly RayTracedAmbientOcclusion m_Feature;
            RTHandle m_Trace;
            RTHandle m_Result;
            readonly RTHandle[] m_History = new RTHandle[2];
            int m_HistoryIndex;
            bool m_HistoryValid;
            GraphicsBuffer m_TraceScratch;
            Matrix4x4 m_PrevViewProj;
            Vector3 m_PrevCameraPos;
            int m_LastFrame = -1;

            class PassData
            {
                public TextureHandle Depth, Normals, Trace, HistoryIn, HistoryOut, Result;
                public IRayTracingShaderRef Tracer;
                public ComputeShader Denoise;
                public GraphicsBuffer Scratch;
                public Matrix4x4 InvViewProj, PrevViewProj;
                public Vector4 CameraPos, PrevCameraPos, TraceSize, FullSize;
                public float Radius, FlipY, Blend, Intensity, DirectStrength;
                public int Rays, Frame;
                public bool HistoryValid;
            }

            // A struct wrapper keeps the pass data free of the URT interface's lifetime rules.
            struct IRayTracingShaderRef
            {
                public UnityEngine.Rendering.UnifiedRayTracing.IRayTracingShader Shader;
                public UnityEngine.Rendering.UnifiedRayTracing.IRayTracingAccelStruct AccelStruct;
            }

            public Pass(RayTracedAmbientOcclusion feature)
            {
                m_Feature = feature;
                profilingSampler = new ProfilingSampler("CoreBuster Ray Traced AO");
            }

            public void Dispose()
            {
                m_Trace?.Release();
                m_Result?.Release();
                m_History[0]?.Release();
                m_History[1]?.Release();
                m_TraceScratch?.Dispose();
                m_Trace = m_Result = m_History[0] = m_History[1] = null;
                m_TraceScratch = null;
            }

            static RTHandle Ensure(RTHandle handle, int width, int height, GraphicsFormat format, string name)
            {
                if (handle != null && handle.rt && handle.rt.width == width && handle.rt.height == height) return handle;
                handle?.Release();
                var descriptor = new RenderTextureDescriptor(width, height, format, GraphicsFormat.None)
                {
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    useMipMap = false,
                };
                return RTHandles.Alloc(descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: name);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                TextureHandle depth = resources.cameraDepthTexture;
                TextureHandle normals = resources.cameraNormalsTexture;
                var tracer = RayTracingWorld.Shader;
                var accel = RayTracingWorld.AccelStruct;
                if (!depth.IsValid() || !normals.IsValid() || tracer == null || accel == null) return;

                var settings = m_Feature.settings;
                var target = cameraData.cameraTargetDescriptor;
                int fullWidth = Mathf.Max(1, target.width), fullHeight = Mathf.Max(1, target.height);
                int divider = settings.HalfResolution ? 2 : 1;
                int traceWidth = Mathf.Max(1, fullWidth / divider), traceHeight = Mathf.Max(1, fullHeight / divider);

                bool resized = m_Trace == null || m_Trace.rt.width != traceWidth || m_Trace.rt.height != traceHeight;
                m_Trace = Ensure(m_Trace, traceWidth, traceHeight, GraphicsFormat.R32G32_SFloat, "RTAO Trace");
                m_History[0] = Ensure(m_History[0], traceWidth, traceHeight, GraphicsFormat.R32G32_SFloat, "RTAO History A");
                m_History[1] = Ensure(m_History[1], traceWidth, traceHeight, GraphicsFormat.R32G32_SFloat, "RTAO History B");
                m_Result = Ensure(m_Result, fullWidth, fullHeight, GraphicsFormat.R16_SFloat, "RTAO Result");
                UnityEngine.Rendering.UnifiedRayTracing.RayTracingHelper.ResizeScratchBufferForTrace(tracer,
                    (uint)traceWidth, (uint)traceHeight, 1, ref m_TraceScratch);

                var view = cameraData.GetViewMatrix();
                var projection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), false);
                var viewProj = projection * view;
                var cameraPos = cameraData.worldSpaceCameraPos;
                // URP renders the depth/normals prepass into intermediate textures with the projection flipped on
                // top-left-origin APIs, so texture rows already run bottom-up like clip space on every API (verified
                // on D3D11 with an asymmetric capture: -1 mirrored the wall's occlusion onto the floor).
                float flip = settings.InvertY ? -1f : 1f;
                // Only one frame of history: a skipped frame (or a resize) restarts the average.
                bool historyValid = m_HistoryValid && !resized && m_LastFrame == Time.frameCount - 1;

                var traceHandle = renderGraph.ImportTexture(m_Trace);
                var historyIn = renderGraph.ImportTexture(m_History[m_HistoryIndex]);
                var historyOut = renderGraph.ImportTexture(m_History[1 - m_HistoryIndex]);
                var result = renderGraph.ImportTexture(m_Result);

                using (var builder = renderGraph.AddUnsafePass<PassData>("CoreBuster Ray Traced AO", out var data, profilingSampler))
                {
                    data.Depth = depth;
                    data.Normals = normals;
                    data.Trace = traceHandle;
                    data.HistoryIn = historyIn;
                    data.HistoryOut = historyOut;
                    data.Result = result;
                    data.Tracer = new IRayTracingShaderRef { Shader = tracer, AccelStruct = accel };
                    data.Denoise = m_Feature.denoise;
                    data.Scratch = m_TraceScratch;
                    data.InvViewProj = viewProj.inverse;
                    data.PrevViewProj = historyValid ? m_PrevViewProj : viewProj;
                    data.CameraPos = cameraPos;
                    data.PrevCameraPos = historyValid ? m_PrevCameraPos : cameraPos;
                    data.TraceSize = new Vector4(traceWidth, traceHeight, 1f / traceWidth, 1f / traceHeight);
                    data.FullSize = new Vector4(fullWidth, fullHeight, 1f / fullWidth, 1f / fullHeight);
                    data.Radius = settings.Radius;
                    data.FlipY = flip;
                    data.Blend = settings.TemporalBlend;
                    data.Intensity = settings.Intensity;
                    data.DirectStrength = settings.DirectLightingStrength;
                    data.Rays = settings.RaysPerPixel;
                    data.Frame = Time.frameCount;
                    data.HistoryValid = historyValid;

                    builder.UseTexture(depth);
                    builder.UseTexture(normals);
                    builder.UseTexture(traceHandle, AccessFlags.ReadWrite);
                    builder.UseTexture(historyIn);
                    builder.UseTexture(historyOut, AccessFlags.ReadWrite);
                    builder.UseTexture(result, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetGlobalTextureAfterPass(result, k_OcclusionTexture);
                    builder.SetRenderFunc(static (PassData d, UnsafeGraphContext context) => Execute(d, context));
                }

                m_HistoryIndex = 1 - m_HistoryIndex;
                m_HistoryValid = true;
                m_PrevViewProj = viewProj;
                m_PrevCameraPos = cameraPos;
                m_LastFrame = Time.frameCount;
            }

            static void Execute(PassData d, UnsafeGraphContext context)
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                var tracer = d.Tracer.Shader;

                // 1. Trace.
                tracer.SetAccelerationStructure(cmd, "_AccelStruct", d.Tracer.AccelStruct);
                tracer.SetTextureParam(cmd, k_RtDepth, d.Depth);
                tracer.SetTextureParam(cmd, k_RtNormals, d.Normals);
                tracer.SetTextureParam(cmd, k_RtOutput, d.Trace);
                tracer.SetMatrixParam(cmd, k_RtInvViewProj, d.InvViewProj);
                tracer.SetVectorParam(cmd, k_RtTraceSize, d.TraceSize);
                tracer.SetVectorParam(cmd, k_RtDepthSize, d.FullSize);
                tracer.SetVectorParam(cmd, k_RtCameraPos, d.CameraPos);
                tracer.SetFloatParam(cmd, k_RtRadius, d.Radius);
                tracer.SetFloatParam(cmd, k_RtFlipY, d.FlipY);
                tracer.SetIntParam(cmd, k_RtRayCount, d.Rays);
                tracer.SetIntParam(cmd, k_RtFrame, d.Frame);
                tracer.Dispatch(cmd, d.Scratch, (uint)d.TraceSize.x, (uint)d.TraceSize.y, 1);

                // 2. Accumulate over time.
                var denoise = d.Denoise;
                int temporal = denoise.FindKernel("Temporal");
                cmd.SetComputeTextureParam(denoise, temporal, k_Depth, d.Depth);
                cmd.SetComputeTextureParam(denoise, temporal, k_Current, d.Trace);
                cmd.SetComputeTextureParam(denoise, temporal, k_History, d.HistoryIn);
                cmd.SetComputeTextureParam(denoise, temporal, k_HistoryOut, d.HistoryOut);
                cmd.SetComputeMatrixParam(denoise, k_InvViewProj, d.InvViewProj);
                cmd.SetComputeMatrixParam(denoise, k_PrevViewProj, d.PrevViewProj);
                cmd.SetComputeVectorParam(denoise, k_CameraPos, d.CameraPos);
                cmd.SetComputeVectorParam(denoise, k_PrevCameraPos, d.PrevCameraPos);
                cmd.SetComputeVectorParam(denoise, k_TraceSize, d.TraceSize);
                cmd.SetComputeVectorParam(denoise, k_DepthSize, d.FullSize);
                cmd.SetComputeVectorParam(denoise, k_FullSize, d.FullSize);
                cmd.SetComputeFloatParam(denoise, k_FlipY, d.FlipY);
                cmd.SetComputeFloatParam(denoise, k_Blend, d.Blend);
                cmd.SetComputeFloatParam(denoise, k_Intensity, d.Intensity);
                cmd.SetComputeIntParam(denoise, k_HistoryValid, d.HistoryValid ? 1 : 0);
                cmd.DispatchCompute(denoise, temporal, Groups(d.TraceSize.x), Groups(d.TraceSize.y), 1);

                // 3. Upsample to the screen with a depth-aware filter.
                int upsample = denoise.FindKernel("Upsample");
                cmd.SetComputeTextureParam(denoise, upsample, k_Depth, d.Depth);
                cmd.SetComputeTextureParam(denoise, upsample, k_Accumulated, d.HistoryOut);
                cmd.SetComputeTextureParam(denoise, upsample, k_Result, d.Result);
                cmd.DispatchCompute(denoise, upsample, Groups(d.FullSize.x), Groups(d.FullSize.y), 1);

                // 4. URP's Lit shaders read it as screen-space ambient occlusion.
                cmd.SetKeyword(k_ScreenSpaceOcclusion, true);
                cmd.SetGlobalVector(k_OcclusionParam, new Vector4(1f, 0f, 0f, d.DirectStrength));
            }

            static int Groups(float size) => Mathf.CeilToInt(size / 8f);

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                cmd.SetKeyword(k_ScreenSpaceOcclusion, false);
            }
        }
    }
}

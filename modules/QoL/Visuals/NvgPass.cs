using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.QoL.Visuals
{
    /// <summary>
    /// The night-vision pass on the world camera, after transparents and before the cockpit and the game's
    /// post-processing: the frame becomes phosphor, the hot units are redrawn as a glow, and ten times a second
    /// a 16 x 16 average of the frame is read back for the tube's gain meter.
    /// </summary>
    internal sealed class NvgPass : ScriptableRenderPass, IDisposable
    {
        /// <summary>Size: the renderer's bounding radius in metres when it was first seen, for the too-small-to-matter cut.</summary>
        internal struct HotRenderer { public Renderer Renderer; public int Submeshes; public float Heat, Size; }

        private const int MeterSize = 16;
        private const float MeterInterval = 0.1f;
        // ponytail: a flat cap on thermal draws per frame; sort by screen size if a crowded apron ever starves the far units.
        private const int MaxDraws = 600;

        private static readonly int FrameId = Shader.PropertyToID("_BoscaliNvgFrame");
        private static readonly int TintId = Shader.PropertyToID("_NvgTint");
        private static readonly int NoiseId = Shader.PropertyToID("_NvgNoise");
        private static readonly int HeatId = Shader.PropertyToID("_NvgHeat");
        private static readonly int HeatColorId = Shader.PropertyToID("_NvgHeatColor");

        private Material phosphor, heat;
        private Camera camera;
        private RenderTexture meter;
        private bool hooked, pending;
        private float nextMeter;

        internal readonly List<HotRenderer> Hot = new List<HotRenderer>(256);
        internal bool Active, DrawHeat;
        internal Color Tint, HeatColor;
        /// <summary>x: grain on the signal, y: noise floor.</summary>
        internal Vector2 Noise;

        /// <summary>The last meter reading, in the frame's own linear units before exposure.</summary>
        internal float Mean { get; private set; }
        internal float Peak { get; private set; }
        internal bool Metered { get; private set; }
        internal int ExecutedFrame { get; private set; } = -1;
        internal int Draws { get; private set; }

        internal NvgPass() { renderPassEvent = RenderPassEvent.AfterRenderingTransparents; }

        internal void Bind(Camera target, Material phosphorMaterial, Material heatMaterial)
        {
            camera = target;
            phosphor = phosphorMaterial;
            heat = heatMaterial;
            if (!hooked)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                hooked = true;
            }
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera rendering)
        {
            if (!Active || rendering == null || rendering != camera || phosphor == null) return;
            UniversalAdditionalCameraData data = rendering.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType != CameraRenderType.Base || data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(this);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (phosphor == null || renderingData.cameraData.camera != camera) return;
            RenderTextureDescriptor frame = renderingData.cameraData.cameraTargetDescriptor;
            frame.depthBufferBits = 0;
            frame.msaaSamples = 1;
            frame.useMipMap = true;
            frame.autoGenerateMips = false;
            bool meterNow = !pending && Time.unscaledTime >= nextMeter && EnsureMeter();

            CommandBuffer cmd = CommandBufferPool.Get("Boscali Night Vision");
            try
            {
                ScriptableRenderer renderer = renderingData.cameraData.renderer;
                cmd.GetTemporaryRT(FrameId, frame, FilterMode.Bilinear);
                cmd.Blit(renderer.cameraColorTargetHandle, FrameId);
                cmd.SetGlobalColor(TintId, Tint);
                cmd.SetGlobalVector(NoiseId, new Vector4(Noise.x, Noise.y, (Time.frameCount & 63) * 7.31f,
                    Mathf.Max(0f, Mathf.Round(Mathf.Log(frame.width / (float)MeterSize, 2f)))));
                if (meterNow)
                {
                    cmd.GenerateMips(FrameId);
                    cmd.Blit(FrameId, meter, phosphor, 1);
                    cmd.RequestAsyncReadback(meter, OnMeter);
                    pending = true;
                    nextMeter = Time.unscaledTime + MeterInterval;
                }
                cmd.Blit(FrameId, renderer.cameraColorTargetHandle, phosphor, 0);
                // URP tracks its own attachments; give the camera its targets back before drawing into them.
                cmd.SetRenderTarget(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
                int draws = 0;
                if (DrawHeat && heat != null)
                {
                    cmd.SetGlobalColor(HeatColorId, HeatColor);
                    for (int i = 0; i < Hot.Count && draws < MaxDraws; i++)
                    {
                        HotRenderer hot = Hot[i];
                        if (hot.Renderer == null || !hot.Renderer.enabled || !hot.Renderer.isVisible) continue;
                        cmd.SetGlobalFloat(HeatId, hot.Heat);
                        for (int s = 0; s < hot.Submeshes; s++, draws++) cmd.DrawRenderer(hot.Renderer, heat, s, 0);
                    }
                }
                Draws = draws;
                cmd.ReleaseTemporaryRT(FrameId);
                context.ExecuteCommandBuffer(cmd);
                ExecutedFrame = Time.frameCount;
            }
            finally { CommandBufferPool.Release(cmd); }
        }

        private void OnMeter(AsyncGPUReadbackRequest request)
        {
            pending = false;
            if (request.hasError) return;
            NativeArray<float> data = request.GetData<float>();
            if (data.Length < MeterSize * MeterSize * 4) return;
            float sum = 0f, peak = 0f;
            for (int i = 0; i < MeterSize * MeterSize; i++)
            {
                float l = 0.2126f * data[i * 4] + 0.7152f * data[i * 4 + 1] + 0.0722f * data[i * 4 + 2];
                if (!(l >= 0f) || float.IsInfinity(l)) continue;
                sum += l;
                if (l > peak) peak = l;
            }
            Mean = sum / (MeterSize * MeterSize);
            Peak = peak;
            Metered = true;
        }

        private bool EnsureMeter()
        {
            if (meter != null) return true;
            meter = new RenderTexture(MeterSize, MeterSize, 0, RenderTextureFormat.ARGBFloat)
            {
                name = "BoscaliNvgMeter", filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave,
            };
            return true;
        }

        /// <summary>Forget the reading: the next switch-on starts from a cold tube.</summary>
        internal void Rest()
        {
            Active = false;
            Metered = false;
            Hot.Clear();
        }

        public void Dispose()
        {
            if (hooked) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            hooked = false;
            Rest();
            camera = null;
            phosphor = heat = null;
            if (meter != null) UnityEngine.Object.Destroy(meter);
            meter = null;
        }
    }
}

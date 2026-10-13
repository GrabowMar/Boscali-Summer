using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Math;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// Sun shafts on the world camera, after the clouds and before the cockpit and the game's post-processing:
    /// a glow around the sun, blocked by scene depth and by the cloud cover, is smeared away from the sun at
    /// quarter resolution and added over the frame. Skipped whole when the sun is behind the camera, set, or the
    /// goggles are on. Local presentation only; without the shader nothing is drawn.
    /// </summary>
    internal sealed class SunShaftsPass : ScriptableRenderPass, IDisposable
    {
        // Statics, not constants: the look is tuned live through the bridge.
        /// <summary>How far the light is smeared (share of the way to the sun) and how much of it is added.</summary>
        internal static float Reach = 0.9f, Strength = 0.35f;
        /// <summary>Radius of the glow around the sun, in screen heights.</summary>
        internal static float GlowRadius = 0.6f;

        private static readonly int A = Shader.PropertyToID("_BoscaliShaftsA"), B = Shader.PropertyToID("_BoscaliShaftsB");
        private static readonly int SunId = Shader.PropertyToID("_ShaftSun"), ParamsId = Shader.PropertyToID("_ShaftParams");
        private static readonly int TintId = Shader.PropertyToID("_ShaftTint");

        private Material material;
        private Camera camera;
        private Vector3 toSun;
        private Color tint;
        private bool active, hooked, shaderMissing;

        /// <summary>Automation: frame of the last draw.</summary>
        internal int ExecutedFrame { get; private set; } = -1;

        internal SunShaftsPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        internal void Tick(LevelInfo level, Camera view, bool enabled)
        {
            active = enabled && view != null && level != null && level.sun != null && level.sunObject != null &&
                level.sunObject.activeInHierarchy && !shaderMissing &&
                !(GameAccess.TryGetNightVisionState(out bool selected, out bool on) && (selected || on));
            if (!active) return;
            if (material == null)
            {
                Shader shader = CanopyShaderBundle.GetSunShaftsShader();
                if (shader == null) { shaderMissing = true; active = false; return; }
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            camera = view;
            toSun = -level.sun.transform.forward;
            // The same air as the disc: white overhead, orange and dim at the horizon, gone once it has set.
            float elevation = Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg;
            SunDiscMath.Transmission(elevation, view.transform.GlobalPosition().y, out float r, out float g, out float b);
            tint = new Color(r, g, b) * SunDiscMath.AboveHorizon(elevation);
            if (!hooked)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                hooked = true;
            }
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera rendering)
        {
            if (!active || rendering == null || rendering != camera || material == null) return;
            UniversalAdditionalCameraData data = rendering.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType != CameraRenderType.Base || data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(this);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null || renderingData.cameraData.camera != camera) return;
            Vector4 clip = renderingData.cameraData.GetProjectionMatrix() * renderingData.cameraData.GetViewMatrix() *
                new Vector4(toSun.x, toSun.y, toSun.z, 0f);
            if (clip.w <= 1e-4f) return; // the sun is behind the camera
            float u = clip.x / clip.w * 0.5f + 0.5f, v = clip.y / clip.w * 0.5f + 0.5f;
            float strength = Strength * SunDiscMath.ScreenFade(u, v);
            if (strength <= 0f) return;

            RenderTextureDescriptor frame = renderingData.cameraData.cameraTargetDescriptor;
            float aspect = frame.width / (float)frame.height;
            frame.width = Mathf.Max(1, frame.width / 4);
            frame.height = Mathf.Max(1, frame.height / 4);
            frame.depthBufferBits = 0;
            frame.msaaSamples = 1;
            float clouds = Shader.GetGlobalTexture(CloudLowRes.CoverId) != null ? 1f : 0f;

            CommandBuffer cmd = CommandBufferPool.Get("Boscali Sun Shafts");
            try
            {
                ScriptableRenderer renderer = renderingData.cameraData.renderer;
                cmd.GetTemporaryRT(A, frame, FilterMode.Bilinear);
                cmd.GetTemporaryRT(B, frame, FilterMode.Bilinear);
                cmd.SetGlobalVector(SunId, new Vector4(u, v, aspect, GlowRadius));
                cmd.SetGlobalColor(TintId, tint);
                cmd.SetGlobalVector(ParamsId, new Vector4(clouds, 0f, Reach, strength));
                cmd.Blit(Texture2D.blackTexture, A, material, 0);
                cmd.Blit(A, B, material, 1);
                // A second, shorter smear fills the gaps between the first one's sixteen steps.
                cmd.SetGlobalVector(ParamsId, new Vector4(clouds, 0f, Reach * 0.25f, strength));
                cmd.Blit(B, A, material, 1);
                cmd.Blit(A, renderer.cameraColorTargetHandle, material, 2);
                // URP tracks its own attachments; give the camera its targets back.
                cmd.SetRenderTarget(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
                cmd.ReleaseTemporaryRT(A);
                cmd.ReleaseTemporaryRT(B);
                context.ExecuteCommandBuffer(cmd);
                ExecutedFrame = Time.frameCount;
            }
            finally { CommandBufferPool.Release(cmd); }
        }

        public void Dispose()
        {
            if (hooked) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            hooked = active = false;
            camera = null;
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null;
        }
    }
}

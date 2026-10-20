using System;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// Clouds in the game's reflection probe, so water and airframes reflect them. The game re-renders its one probe every five seconds; this pass draws the cloud march
    /// onto each face as it is rendered (the shader's CloudProbe pass, at half the step count, one sample per texel). Nothing runs on the frames in between.
    /// It marches with a copy of the cloud material, so the main camera's march and its temporal state are untouched.
    /// Since 2026-10-20 it also draws into the video-feed lenses (<see cref="FeedCamera.IsLens"/>: the drone link and the missile feed), which
    /// otherwise showed a cloudless sky: the same one-pass march at half steps, only on the frames a feed renders (15-20 Hz, at most 480x270).
    /// </summary>
    internal sealed class CloudProbePass : ScriptableRenderPass, IDisposable
    {
        private static readonly int SizeId = Shader.PropertyToID("_CloudLowResSize");
        private static readonly int CheckerOnId = Shader.PropertyToID("_CloudCheckerOn");
        private static readonly int FrustumId = Shader.PropertyToID("_CloudFrustum");
        private static readonly int CameraPosId = Shader.PropertyToID("_CloudCameraPos");
        private static readonly int ForwardId = Shader.PropertyToID("_CloudCameraForward");
        private static readonly int StepsId = Shader.PropertyToID("_CloudSteps");
        private static readonly int FarStepsId = Shader.PropertyToID("_CloudFarSteps");

        private Material source, probe;
        private int passIndex = -1, copiedFrame = -1;
        private bool hooked;

        /// <summary>Faces drawn since the pass was made (sims read it to prove the probe got its clouds).</summary>
        internal static int FacesDrawn { get; private set; }
        /// <summary>Feed-lens frames drawn since the pass was made (sims read it to prove a feed got its clouds).</summary>
        internal static int FeedFramesDrawn { get; private set; }

        private static bool Wanted(Camera camera) => camera.cameraType == CameraType.Reflection || FeedCamera.IsLens(camera);

        internal CloudProbePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        /// <summary>The cloud material to march with, or null while the clouds are not drawn.</summary>
        internal void Bind(Material march)
        {
            source = march;
            if (!hooked && march != null)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                hooked = true;
            }
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera rendering)
        {
            if (source == null || rendering == null || !Wanted(rendering)) return;
            if (probe == null || probe.shader != source.shader)
            {
                if (probe != null) UnityEngine.Object.Destroy(probe);
                probe = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
                passIndex = probe.FindPass("CloudProbe");
            }
            if (passIndex < 0) return;   // an older shader bundle without the pass: no reflections, nothing else changes
            ScriptableRenderer renderer = rendering.TryGetComponent(out UniversalAdditionalCameraData data) ? data.scriptableRenderer : UniversalRenderPipeline.asset?.scriptableRenderer;
            renderer?.EnqueuePass(this);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (source == null || probe == null || passIndex < 0 || !Wanted(camera)) return;
            if (copiedFrame != Time.frameCount)
            {
                copiedFrame = Time.frameCount;
                probe.CopyPropertiesFromMaterial(source);
                probe.SetFloat(CheckerOnId, 0f);
                probe.SetFloat(StepsId, source.GetFloat(StepsId) * 0.5f);
                probe.SetFloat(FarStepsId, source.GetFloat(FarStepsId) * 0.5f);
            }
            RenderTextureDescriptor face = renderingData.cameraData.cameraTargetDescriptor;
            Matrix4x4 inverseView = camera.worldToCameraMatrix.inverse;
            probe.SetVector(SizeId, new Vector4(face.width, face.height, 1f / face.width, 1f / face.height));
            probe.SetVector(ForwardId, inverseView.MultiplyVector(Vector3.back).normalized);
            CommandBuffer cmd = CommandBufferPool.Get("Boscali Cloud Probe");
            try
            {
                // Set in the command buffer, so they apply to this face's draw only: the main camera sets its own before its march.
                cmd.SetGlobalMatrix(FrustumId, CloudVolumeUniforms.FrustumRays(inverseView, renderingData.cameraData.GetProjectionMatrix().inverse));
                cmd.SetGlobalVector(CameraPosId, inverseView.GetColumn(3));
                cmd.DrawProcedural(Matrix4x4.identity, probe, passIndex, MeshTopology.Triangles, 3);
                context.ExecuteCommandBuffer(cmd);
                if (camera.cameraType == CameraType.Reflection) FacesDrawn++;
                else FeedFramesDrawn++;
            }
            finally { CommandBufferPool.Release(cmd); }
        }

        public void Dispose()
        {
            if (hooked) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            hooked = false;
            source = null;
            if (probe != null) UnityEngine.Object.Destroy(probe);
            probe = null;
            passIndex = -1;
        }
    }
}

using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Features.Visuals.Runtime
{
    /// <summary>
    /// Phase-0 spike: a no-op renderer feature proving runtime injection works. It draws nothing;
    /// it only logs once each from Create, AddRenderPasses, Execute and Dispose. Removed or
    /// productized into FxPass in Phase 1.
    /// </summary>
    internal sealed class FxSpikeFeature : ScriptableRendererFeature
    {
        private sealed class SpikePass : ScriptableRenderPass
        {
            private readonly FxSpikeFeature owner;

            public SpikePass(FxSpikeFeature feature)
            {
                owner = feature;
                // Where a future consolidated FxPass would live; also proves the event fires.
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                owner.OnPassExecuted(renderingData.cameraData.camera);
            }
        }

        // Static: Unity calls Create() inside CreateInstance, before any instance logger exists.
        internal static ManualLogSource Logger { get; set; }

        private SpikePass pass;
        private bool createdLogged;
        private bool enqueuedLogged;
        private bool executedLogged;

        public override void Create()
        {
            pass = new SpikePass(this);
            if (!createdLogged)
            {
                createdLogged = true;
                Logger?.LogInfo("[Visuals] FxSpike: Create called.");
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null) return;
            renderer.EnqueuePass(pass);
            if (!enqueuedLogged)
            {
                enqueuedLogged = true;
                Camera camera = renderingData.cameraData.camera;
                Logger?.LogInfo("[Visuals] FxSpike: AddRenderPasses executing (camera " +
                    (camera != null ? camera.name : "?") + ").");
            }
        }

        protected override void Dispose(bool disposing)
        {
            Logger?.LogInfo("[Visuals] FxSpike: disposed.");
            base.Dispose(disposing);
        }

        private void OnPassExecuted(Camera camera)
        {
            if (executedLogged) return;
            executedLogged = true;
            Logger?.LogInfo("[Visuals] FxSpike: pass executed on camera " +
                (camera != null ? camera.name : "?") + ".");
        }
    }
}

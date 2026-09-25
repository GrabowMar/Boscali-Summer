using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Features.Visuals.Runtime
{
    /// <summary>
    /// AMD FidelityFX RCAS sharpening with no shader of our own. The game never touches render
    /// scale or upscaling, and URP treats the FSR filter as upscaling even at 100 % scale, so
    /// selecting it on the pipeline asset runs RCAS in the final blit (<c>FinalPost</c> keeps its
    /// <c>_RCAS</c> variants in the build). The asset's three fields are captured on first use and
    /// put back when sharpening is switched off or the module unloads.
    /// </summary>
    internal sealed class Sharpening
    {
        private UniversalRenderPipelineAsset asset;
        private UpscalingFilterSelection savedFilter;
        private bool savedOverride;
        private float savedSharpness;

        private static UniversalRenderPipelineAsset Current => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        public void Apply(bool on, float strength)
        {
            UniversalRenderPipelineAsset current = Current;
            // A quality-level change can swap the asset under us: hand the old one back first.
            if (asset != null && asset != current) Restore();
            if (!on || current == null)
            {
                Restore();
                return;
            }
            if (asset == null)
            {
                asset = current;
                savedFilter = current.upscalingFilter;
                savedOverride = current.fsrOverrideSharpness;
                savedSharpness = current.fsrSharpness;
            }
            current.upscalingFilter = UpscalingFilterSelection.FSR;
            current.fsrOverrideSharpness = true;
            current.fsrSharpness = strength < 0f ? 0f : (strength > 1f ? 1f : strength);
        }

        public void Restore()
        {
            if (asset == null) return;
            asset.upscalingFilter = savedFilter;
            asset.fsrOverrideSharpness = savedOverride;
            asset.fsrSharpness = savedSharpness;
            asset = null;
        }

        public void Describe(IDictionary<string, object> state)
        {
            UniversalRenderPipelineAsset current = Current;
            state["sharpen"] = current != null && current.upscalingFilter == UpscalingFilterSelection.FSR && current.fsrOverrideSharpness
                ? current.fsrSharpness : -1f;
        }
    }
}

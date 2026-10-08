using System;
using UnityEngine;
using UnityEngine.Rendering;
using BoscaliSummer.Core.Fx;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>The reduced-resolution cloud targets and the commands that fill them, shared by
    /// the URP pass and the offline bench. The sky is kept at half resolution. With temporal
    /// update on, each frame marches one texel of every 2x2 block (a quarter-resolution march)
    /// and the resolve reprojects the other three from last frame along the cloud's own
    /// distance, clamped to the fresh neighbours so nothing ghosts. Off, the whole half-size
    /// target is marched every frame. Either way the composite then upsamples the half-size
    /// colour against the half-size visible cloud or scene depth.
    /// The composite cube is a renderer draw, and URP binds its material before this pass
    /// executes: a texture swapped here reaches it one frame late, so the sky lagged the
    /// camera and the aircraft's hole trailed it. Its bindings therefore never change; the
    /// shown targets are fixed and history is a GPU copy of them.</summary>
    internal sealed class CloudLowRes : IDisposable
    {
        private const int MarchPass = 2;
        private const int ResolvePass = 1;
        private static readonly int ColourId = Shader.PropertyToID("_CloudLowResColour");
        private static readonly int DepthId = Shader.PropertyToID("_CloudLowResDepth");
        private static readonly int SizeId = Shader.PropertyToID("_CloudLowResSize");
        private static readonly int QuarterSizeId = Shader.PropertyToID("_CloudQuarterSize");
        private static readonly int QuarterColourId = Shader.PropertyToID("_CloudQuarterColour");
        private static readonly int QuarterDataId = Shader.PropertyToID("_CloudQuarterData");
        private static readonly int HistoryId = Shader.PropertyToID("_CloudHistoryTex");
        private static readonly int HistoryDepthId = Shader.PropertyToID("_CloudHistoryDepth");
        private static readonly int HistoryValidId = Shader.PropertyToID("_CloudHistoryValid");
        private static readonly int CheckerOnId = Shader.PropertyToID("_CloudCheckerOn");

        private readonly RenderTargetIdentifier[] targets = new RenderTargetIdentifier[2];
        private RenderTexture quarterColour, quarterData, colour, depth, history, historyDepth;
        private int requestedWidth, requestedHeight;
        private float requestedScale;
        private bool allocationRefused;
        private long refusedPoolBytes;
        private float retryAt;
        internal long TargetBytes => (long)Width * Height * 24L +
            (long)((Width + 1) / 2) * ((Height + 1) / 2) * 16L;

        internal int Width { get; private set; }
        internal int Height { get; private set; }
        /// <summary>False until a resolve has written history this target size can reuse.</summary>
        internal bool HistoryValid { get; private set; }

        internal bool Ensure(int fullWidth, int fullHeight)
        {
            float quality = Mathf.Clamp(FxBus.Scales.RenderTargets, 0.25f, 1f);
            bool sameRequest = requestedWidth == fullWidth && requestedHeight == fullHeight && requestedScale == quality;
            if (colour != null && sameRequest && colour.IsCreated() && history.IsCreated() &&
                depth.IsCreated() && historyDepth.IsCreated() && quarterColour.IsCreated() && quarterData.IsCreated()) return true;
            // A refused set would otherwise allocate/free fifteen targets every rendered frame.
            // Pool changes recover immediately; an unchanged refusal retries twice per second.
            if (allocationRefused && sameRequest && refusedPoolBytes == FxRtPool.UsedBytes && Time.unscaledTime < retryAt)
                return false;
            Release();
            requestedWidth = fullWidth; requestedHeight = fullHeight; requestedScale = quality;
            float scale = Math.Min(1f, Math.Min(1920f / Math.Max(1, fullWidth), 1080f / Math.Max(1, fullHeight))) * quality;
            for (int attempt = 0; attempt < 3; attempt++, scale *= 0.7f)
            {
                int w = Math.Max(2, (int)Math.Ceiling(fullWidth * scale / 2f));
                int h = Math.Max(2, (int)Math.Ceiling(fullHeight * scale / 2f));
                Width = w; Height = h;
                colour = Target(w, h, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, "Boscali Clouds");
                history = Target(w, h, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, "Boscali Clouds History");
                // Only the nearest relevant depth survives resolve. Two RFloat targets cost the same
                // as the old single RGFloat target and allow disocclusion rejection.
                depth = Target(w, h, RenderTextureFormat.RFloat, FilterMode.Point, "Boscali Clouds Depth");
                historyDepth = Target(w, h, RenderTextureFormat.RFloat, FilterMode.Point, "Boscali Clouds History Depth");
                quarterColour = Target((w + 1) / 2, (h + 1) / 2, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, "Boscali Clouds Quarter");
                quarterData = Target((w + 1) / 2, (h + 1) / 2, RenderTextureFormat.RGFloat, FilterMode.Point, "Boscali Clouds Quarter Depth");
                HistoryValid = false;
                if (TargetBytes <= 16L * 1024 * 1024 && Acquire(colour) && Acquire(history) && Acquire(depth) && Acquire(historyDepth) &&
                    Acquire(quarterColour) && Acquire(quarterData))
                { allocationRefused = false; return true; }
                Release();
            }
            allocationRefused = true; refusedPoolBytes = FxRtPool.UsedBytes;
            retryAt = Time.unscaledTime + 0.5f;
            return false;
        }

        /// <summary>Records the march (and the resolve) into <paramref name="cmd"/> and points the
        /// composite at the result. The caller restores its own render targets afterwards.</summary>
        internal void Record(CommandBuffer cmd, Material march, Material composite, bool temporal)
        {
            var half = new Vector4(Width, Height, 1f / Width, 1f / Height);
            var quarter = new Vector4(quarterColour.width, quarterColour.height, 1f / quarterColour.width, 1f / quarterColour.height);
            march.SetVector(SizeId, half);
            composite.SetVector(SizeId, half);
            composite.SetVector(QuarterSizeId, quarter);
            march.SetFloat(CheckerOnId, temporal ? 1f : 0f);
            composite.SetTexture(ColourId, colour);
            composite.SetTexture(DepthId, depth);
            targets[0] = colour;
            targets[1] = depth;
            if (temporal)
            {
                composite.SetFloat(HistoryValidId, HistoryValid ? 1f : 0f);
                targets[0] = quarterColour;
                targets[1] = quarterData;
                cmd.SetRenderTarget(targets, targets[0]);
                cmd.DrawProcedural(Matrix4x4.identity, march, MarchPass, MeshTopology.Triangles, 3);
                composite.SetTexture(QuarterColourId, quarterColour);
                composite.SetTexture(QuarterDataId, quarterData);
                composite.SetTexture(HistoryId, history);
                composite.SetTexture(HistoryDepthId, historyDepth);
                targets[0] = colour;
                targets[1] = depth;
                cmd.SetRenderTarget(targets, targets[0]);
                cmd.DrawProcedural(Matrix4x4.identity, composite, ResolvePass, MeshTopology.Triangles, 3);
                cmd.CopyTexture(colour, history);
                cmd.CopyTexture(depth, historyDepth);
                HistoryValid = true;
            }
            else
            {
                cmd.SetRenderTarget(targets, targets[0]);
                cmd.DrawProcedural(Matrix4x4.identity, march, MarchPass, MeshTopology.Triangles, 3);
                HistoryValid = false;
            }
        }

        internal void InvalidateHistory() => HistoryValid = false;

        private static bool Acquire(RenderTexture target) => target.Create() && FxRtPool.Own(target);

        private static RenderTexture Target(int w, int h, RenderTextureFormat format, FilterMode filter, string name) =>
            new RenderTexture(w, h, 0, format, RenderTextureReadWrite.Linear)
            { name = name, filterMode = filter, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };

        private void Release()
        {
            Free(ref quarterColour); Free(ref quarterData); Free(ref colour); Free(ref history); Free(ref depth); Free(ref historyDepth);
            Width = Height = 0;
            HistoryValid = false;
        }

        private static void Free(ref RenderTexture texture)
        {
            if (texture == null) return;
            FxRtPool.Disown(texture);
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        public void Dispose() { Release(); allocationRefused = false; retryAt = 0f; }
    }
}

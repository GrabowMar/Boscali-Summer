using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>The reduced-resolution cloud targets and the commands that fill them, shared by
    /// the URP pass and the offline bench. The sky is kept at half resolution. With temporal
    /// update on, each frame marches one texel of every 2x2 block (a quarter-resolution march)
    /// and the resolve reprojects the other three from last frame along the cloud's own
    /// distance, clamped to the fresh neighbours so nothing ghosts. Off, the whole half-size
    /// target is marched every frame. Either way the composite then upsamples the half-size
    /// colour against the half-size scene depth.</summary>
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
        private static readonly int HistoryValidId = Shader.PropertyToID("_CloudHistoryValid");
        private static readonly int CheckerOnId = Shader.PropertyToID("_CloudCheckerOn");

        private readonly RenderTargetIdentifier[] targets = new RenderTargetIdentifier[2];
        private RenderTexture quarterColour, quarterData, historyA, historyB, halfData;
        private bool writeA;

        internal int Width { get; private set; }
        internal int Height { get; private set; }
        /// <summary>False until a resolve has written history this target size can reuse.</summary>
        internal bool HistoryValid { get; private set; }

        internal bool Ensure(int fullWidth, int fullHeight)
        {
            int w = Math.Max(2, (fullWidth + 1) / 2), h = Math.Max(2, (fullHeight + 1) / 2);
            if (historyA != null && Width == w && Height == h && historyA.IsCreated() && historyB.IsCreated() &&
                halfData.IsCreated() && quarterColour.IsCreated() && quarterData.IsCreated()) return true;
            Release();
            Width = w;
            Height = h;
            historyA = Target(w, h, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, "Boscali Clouds A");
            historyB = Target(w, h, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, "Boscali Clouds B");
            halfData = Target(w, h, RenderTextureFormat.RGFloat, FilterMode.Point, "Boscali Clouds Depth");
            quarterColour = Target((w + 1) / 2, (h + 1) / 2, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, "Boscali Clouds Quarter");
            quarterData = Target((w + 1) / 2, (h + 1) / 2, RenderTextureFormat.RGFloat, FilterMode.Point, "Boscali Clouds Quarter Depth");
            HistoryValid = false;
            return historyA.Create() && historyB.Create() && halfData.Create() && quarterColour.Create() && quarterData.Create();
        }

        /// <summary>Records the march (and the resolve) into <paramref name="cmd"/> and points the
        /// composite at the result. The caller restores its own render targets afterwards.</summary>
        internal void Record(CommandBuffer cmd, Material march, Material composite, bool temporal)
        {
            RenderTexture write = writeA ? historyA : historyB, read = writeA ? historyB : historyA;
            var half = new Vector4(Width, Height, 1f / Width, 1f / Height);
            var quarter = new Vector4(quarterColour.width, quarterColour.height, 1f / quarterColour.width, 1f / quarterColour.height);
            march.SetVector(SizeId, half);
            composite.SetVector(SizeId, half);
            composite.SetVector(QuarterSizeId, quarter);
            march.SetFloat(CheckerOnId, temporal ? 1f : 0f);
            if (temporal)
            {
                composite.SetFloat(HistoryValidId, HistoryValid ? 1f : 0f);
                targets[0] = quarterColour;
                targets[1] = quarterData;
                cmd.SetRenderTarget(targets, targets[0]);
                cmd.DrawProcedural(Matrix4x4.identity, march, MarchPass, MeshTopology.Triangles, 3);
                composite.SetTexture(QuarterColourId, quarterColour);
                composite.SetTexture(QuarterDataId, quarterData);
                composite.SetTexture(HistoryId, read);
                targets[0] = write;
                targets[1] = halfData;
                cmd.SetRenderTarget(targets, targets[0]);
                cmd.DrawProcedural(Matrix4x4.identity, composite, ResolvePass, MeshTopology.Triangles, 3);
                HistoryValid = true;
            }
            else
            {
                targets[0] = write;
                targets[1] = halfData;
                cmd.SetRenderTarget(targets, targets[0]);
                cmd.DrawProcedural(Matrix4x4.identity, march, MarchPass, MeshTopology.Triangles, 3);
                HistoryValid = false;
            }
            composite.SetTexture(ColourId, write);
            composite.SetTexture(DepthId, halfData);
            writeA = !writeA;
        }

        internal void InvalidateHistory() => HistoryValid = false;

        private static RenderTexture Target(int w, int h, RenderTextureFormat format, FilterMode filter, string name) =>
            new RenderTexture(w, h, 0, format, RenderTextureReadWrite.Linear)
            { name = name, filterMode = filter, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };

        private void Release()
        {
            Free(ref quarterColour); Free(ref quarterData); Free(ref historyA); Free(ref historyB); Free(ref halfData);
            Width = Height = 0;
            HistoryValid = false;
        }

        private static void Free(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        public void Dispose() => Release();
    }
}

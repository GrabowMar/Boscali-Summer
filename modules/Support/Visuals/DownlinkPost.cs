using System;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Support.Domain.Space;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// The DOWNLINK's IR (white-hot) and LOW LIGHT looks without a new shader. The imager's picture is blitted to a 320x200 target and read
    /// back with <see cref="AsyncGPUReadback"/> at most four times a second (never ReadPixels); a CPU pass writes the look into one reused
    /// texture. IR: luminance stretched between its 1st and 99th percentile, inverted to white-hot, with soft glow discs at the given picture
    /// points (host-revealed contacts, an own impact). LOW LIGHT: the same gain as green phosphor with grain and a vignette. A fresh picture
    /// resolves from coarse blocks band by band, top down, to the link's final block. Owns its target (counted in <see cref="FxRtPool"/>) and
    /// texture until <see cref="Dispose"/>; a refused target or a failing readback is said in <see cref="Words"/>, never shown as black.
    /// </summary>
    internal sealed class DownlinkPost
    {
        public const int Width = 320, Height = 200, MaxGlows = 17;
        private const float Interval = 0.25f, HoldSeconds = 2f, LostSeconds = 1f, RetrySeconds = 5f;
        private const int Coarse = 16, Bands = 8, GlowRadius = 6, MinSpan = 32;

        private RenderTexture small;
        private Texture2D output;
        private readonly byte[] lum = new byte[Width * Height];
        private readonly int[] histogram = new int[256];
        private readonly Vector2[] glows = new Vector2[MaxGlows];
        private readonly Action<AsyncGPUReadbackRequest> onRead;
        private DownlinkMode look, shownLook;
        private int glowCount, block = 1, frame;
        private float resolved = 1f, nextAt, pendingAt, goodAt = -100f, failingSince = -1f, refusedUntil;
        private bool pending, hasFrame, disposed;

        public DownlinkPost() { onRead = OnRead; }

        /// <summary>Why there is no picture (empty while the first frame is still on its way).</summary>
        public string Words { get; private set; } = "";

        /// <summary>
        /// Feeds the imager's picture in <paramref name="mode"/> (IR or LOW LIGHT); returns the post picture to show, or null with <see cref="Words"/>.
        /// <paramref name="glowUv"/>: picture points (0..1, origin bottom-left) that glow white-hot in IR.
        /// </summary>
        public Texture Process(Texture source, DownlinkMode mode, int finalBlock, float resolve01, Vector2[] glowUv, int glowN)
        {
            float wall = Time.unscaledTime;
            Words = "";
            if (disposed) return null;
            if (!SystemInfo.supportsAsyncGPUReadback) { Words = "NEGATIVE: NO GPU READBACK HERE — USE EO"; return null; }
            if (!Ensure(wall)) { Words = "NEGATIVE: VIDEO MEMORY BUDGET FULL — USE EO"; return null; }
            if (pending && wall - pendingAt > LostSeconds) { pending = false; Fail(wall); } // a readback that never answers is a failed one
            if (mode != look) nextAt = 0f; // a new look asks at once
            if (!pending && wall >= nextAt && source != null)
            {
                nextAt = wall + Interval;
                look = mode;
                block = Mathf.Clamp(finalBlock, 1, Coarse);
                resolved = Mathf.Clamp01(resolve01);
                glowCount = Mathf.Clamp(glowN, 0, MaxGlows);
                if (glowUv != null) Array.Copy(glowUv, glows, Mathf.Min(glowCount, glowUv.Length));
                RenderTexture previous = RenderTexture.active;
                Graphics.Blit(source, small);
                RenderTexture.active = previous;
                pending = true;
                pendingAt = wall;
                AsyncGPUReadback.Request(small, 0, TextureFormat.RGBA32, onRead);
            }
            if (failingSince >= 0f && wall - goodAt > HoldSeconds) { Words = "NEGATIVE: DOWNLINK READBACK FAILED — RETRYING"; return null; }
            return hasFrame && shownLook == mode ? output : null;
        }

        private bool Ensure(float wall)
        {
            if (small != null && output != null) return true;
            if (wall < refusedUntil) return false;
            small = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32) { name = "BoscaliDownlinkPost" };
            small.Create();
            if (!FxRtPool.Own(small))
            {
                small.Release();
                UnityEngine.Object.Destroy(small);
                small = null;
                refusedUntil = wall + RetrySeconds;
                return false;
            }
            output = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "BoscaliDownlinkLook", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            return true;
        }

        private void Fail(float wall)
        {
            if (failingSince < 0f) failingSince = wall;
        }

        private void OnRead(AsyncGPUReadbackRequest request)
        {
            if (disposed) return;
            pending = false;
            float wall = Time.unscaledTime;
            if (request.hasError || output == null) { Fail(wall); return; }
            NativeArray<byte> source = request.GetData<byte>();
            if (source.Length < Width * Height * 4) { Fail(wall); return; }
            Paint(source, output.GetRawTextureData<byte>());
            output.Apply(false);
            hasFrame = true;
            shownLook = look;
            goodAt = wall;
            failingSince = -1f;
        }

        private int Percentile(int rank)
        {
            int sum = 0;
            for (int i = 0; i < histogram.Length; i++) { sum += histogram[i]; if (sum > rank) return i; }
            return histogram.Length - 1;
        }

        private void Paint(NativeArray<byte> src, NativeArray<byte> dst)
        {
            const int n = Width * Height;
            Array.Clear(histogram, 0, histogram.Length);
            for (int i = 0, p = 0; i < n; i++, p += 4)
            {
                int l = (src[p] * 77 + src[p + 1] * 150 + src[p + 2] * 29) >> 8;
                lum[i] = (byte)l;
                histogram[l]++;
            }
            // Auto-gain: the 1st..99th percentile spans the whole range, so a dark night render and a bright noon read alike. The gain stops at
            // 8x around the scene's middle, so a flat sea reads mid-grey instead of stretched noise.
            int lo = Percentile(n / 100), hi = Percentile(n - n / 100 - 1);
            if (hi - lo < MinSpan) { lo = (lo + hi - MinSpan) / 2; hi = lo + MinSpan; }
            int span = hi - lo;
            bool ir = look == DownlinkMode.Ir;
            frame++;
            int done = resolved >= 1f ? Bands : (int)(resolved * Bands);
            const int cx = Width / 2, cy = Height / 2, edge2 = cx * cx + cx * cx;
            for (int y = 0; y < Height; y++)
            {
                int band = (Height - 1 - y) * Bands / Height; // row 0 is the bottom; band 0 is the top
                int b = band < done ? block : Coarse;
                int rowSource = (y - y % b) * Width, row = y * Width;
                int dy = (y - cy) * Width / Height;
                for (int x = 0; x < Width; x++)
                {
                    int s = (lum[rowSource + x - x % b] - lo) * 255 / span;
                    s = s < 0 ? 0 : s > 255 ? 255 : s;
                    int p = (row + x) * 4;
                    if (ir)
                    {
                        byte v = (byte)(255 - s);
                        dst[p] = v; dst[p + 1] = v; dst[p + 2] = v;
                    }
                    else
                    {
                        uint h = unchecked((uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(frame * 83492791));
                        h ^= h >> 13; h = unchecked(h * 0x5bd1e995u); h ^= h >> 15;
                        int dx = x - cx;
                        int g = s + (int)(h & 31u) - 16;
                        g = g * (1024 - 600 * (dx * dx + dy * dy) / edge2) >> 10; // vignette
                        g = 14 + (g < 0 ? 0 : g > 255 ? 255 : g) * 241 / 255;   // phosphor never goes fully black
                        dst[p] = (byte)(g * 70 >> 8); dst[p + 1] = (byte)g; dst[p + 2] = (byte)(g * 96 >> 8);
                    }
                    dst[p + 3] = 255;
                }
            }
            if (ir) Glow(dst);
        }

        /// <summary>White-hot discs: a soft falloff that only ever brightens.</summary>
        private void Glow(NativeArray<byte> dst)
        {
            const int r2 = GlowRadius * GlowRadius;
            for (int k = 0; k < glowCount; k++)
            {
                int gx = Mathf.RoundToInt(glows[k].x * Width), gy = Mathf.RoundToInt(glows[k].y * Height);
                for (int y = Math.Max(0, gy - GlowRadius); y <= Math.Min(Height - 1, gy + GlowRadius); y++)
                    for (int x = Math.Max(0, gx - GlowRadius); x <= Math.Min(Width - 1, gx + GlowRadius); x++)
                    {
                        int d2 = (x - gx) * (x - gx) + (y - gy) * (y - gy);
                        if (d2 >= r2) continue;
                        int t = 255 - d2 * 255 / r2, add = t * t / 255;
                        int p = (y * Width + x) * 4;
                        if (add <= dst[p]) continue;
                        byte v = (byte)add;
                        dst[p] = v; dst[p + 1] = v; dst[p + 2] = v;
                    }
            }
        }

        /// <summary>Scene reset or feed teardown: the target leaves the budget, both GPU objects go, a late readback lands on nothing.</summary>
        public void Dispose()
        {
            disposed = true;
            pending = false;
            if (small != null)
            {
                FxRtPool.Disown(small);
                small.Release();
                UnityEngine.Object.Destroy(small);
                small = null;
            }
            if (output != null) UnityEngine.Object.Destroy(output);
            output = null;
        }
    }
}

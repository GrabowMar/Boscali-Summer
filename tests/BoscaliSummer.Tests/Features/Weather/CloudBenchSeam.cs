#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

// The deck seam metric of spec 7.3 (bench mode "seam", skies 8, 15 and 29 unless CLOUD_BENCH_SKIES names others): the opacity the march
// leaves (A, rows whose deck hit is 120 km out to just short of the hand-off band), the band where the deck takes over, and the opacity
// of the deck alone (B, 6 km past the band to 340 km), averaged per row over 1920 columns x 72 azimuths x 16 seeds. The march and the
// deck draw the same sky, so A and B must agree and the band must be a straight line between them. Pinned sky, eye 500 m, a 2 degree
// vertical field of view over the 1080 rows, ground off, full-resolution march (no temporal accumulation; its pixel dither averages out
// over two million samples a row). Each seed bakes its own tape, so its deck altitude H and depth hd differ a little: every seed places
// its own rows and band, and only the means are pooled. The eye drops under the slab for a deck it would sit inside (sky 29), or the
// march alone would cover the whole chord and no row would be deck-only. On a failure set SkyDerived.HorizonStack = HorizonStack *
// meanA / meanB (sky 29: the 0.9 floor of HorizonCover) and run again.
internal static class CloudBenchSeam
{
    private const int Width = 1920, Height = 1080, Azimuths = 72, Seeds = 16;
    private const float DefaultEye = 500f, FovDegrees = 2f, CentreElevation = 0.45f, FarLimit = 220000f;
    private const float Tolerance = 0.05f;

    /// <summary>The unclamped hand-off quantity of FlightCloud.shader's DeckWeight at a deck hit D metres out:
    /// 0.5 (t0 + t1) + hw - 220 km. wDeck leaves 0 where it is 0 and reaches 1 where it is 2 hw.</summary>
    private static float Hand(float distance, float height, float depth, float eye, out float hw)
    {
        float span = height - eye; // signed height of the deck plane above the eye
        float low = (height - 0.4f * depth - eye) / span, high = (height + 0.6f * depth - eye) / span;
        float t0 = Math.Max(0f, Math.Min(low, high) * distance), t1 = Math.Max(low, high) * distance;
        hw = 0.5f * Math.Max(t1 - t0, 0.25f * FarLimit);
        return 0.5f * (t0 + t1) + hw - FarLimit;
    }

    private static float Bisect(Func<float, float> f)
    {
        float lo = 1000f, hi = 3e6f;
        for (int i = 0; i < 60; i++) { float mid = 0.5f * (lo + hi); if (f(mid) < 0f) lo = mid; else hi = mid; }
        return 0.5f * (lo + hi);
    }

    public static void Run(StringBuilder log, IReadOnlyList<int> skies, Camera camera, RenderTexture target, CommandBuffer cmd, Mesh cube,
        Material march, Texture noise, float halfExtent)
    {
        var uniforms = new CloudVolumeUniforms();
        var readback = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        camera.fieldOfView = FovDegrees;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f); // the target's alpha is then the cloud opacity
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int renders = 0;
        foreach (int sky in skies)
        {
            var rowSum = new double[Seeds][];
            var heights = new float[Seeds];
            var depths = new float[Seeds];
            float eye = DefaultEye;
            for (int seedIndex = 0; seedIndex < Seeds; seedIndex++)
            {
                uint seed = 90210u + 7919u * (uint)seedIndex;
                var key = new WeatherKey(seed, 0f, false, (byte)SkyLibrary.ClassOf(sky), 0, 0, false, 0f, 25000f, skyPin: (byte)sky);
                const float time = 900f;
                var field = new WeatherField();
                field.Build(key, time, halfExtent, halfExtent, 13f);
                CloudMaps maps = CloudMaps.Build(key, time, halfExtent, halfExtent, 13f, 105000f, 315000f, null);
                heights[seedIndex] = field.Tape.Tape.DeckAltitude;
                depths[seedIndex] = Math.Max(1f, field.RowAt(0f, 0f).HorizonDepth);
                // The eye of the whole sky is set once, from the first seed: 300 m under the slab when 500 m would sit within 100 m of it.
                if (seedIndex == 0 && heights[0] - 0.4f * depths[0] - 100f < DefaultEye) eye = Math.Max(30f, heights[0] - 0.4f * depths[0] - 300f);
                Texture2D near = Map(maps.Near, CloudMaps.NearSize), nearProfiles = Map(maps.NearProfiles, CloudMaps.NearSize);
                Texture2D far = Map(maps.Far, CloudMaps.FarSize), farProfiles = Map(maps.FarProfiles, CloudMaps.FarSize);
                Texture2D envelope = Map(maps.Envelope, CloudMaps.EnvelopeSize);
                envelope.filterMode = FilterMode.Point;
                uniforms.Settle(field);
                camera.transform.position = new Vector3(0f, eye, 0f);
                float pitch = heights[seedIndex] - eye >= 0f ? CentreElevation : -CentreElevation;
                var frame = new CloudFrame
                {
                    WorldOffset = Vector3.zero, CameraPosition = camera.transform.position, CameraForward = camera.transform.forward,
                    FieldOfView = FovDegrees, PixelHeight = Height, Bottom = maps.Bottom, Top = maps.Top,
                    SunDirection = new Vector3(0.35f, 0.55f, -0.65f).normalized, SunColor = new Color(1.9f, 1.8f, 1.6f),
                    Ambient = new Color(0.55f, 0.65f, 0.85f) * 0.45f + new Color(0.72f, 0.8f, 0.9f) * 0.3f + new Color(1.9f, 1.8f, 1.6f) * 0.05f,
                    Ground = new Color(0.25f, 0.27f, 0.22f) * 0.25f + new Color(0.72f, 0.8f, 0.9f) * 0.1f,
                    Fog = new Color(0.72f, 0.8f, 0.9f), Extinction = 0.00004f, DeltaTime = 0f,
                    RainVisualsEnabled = maps.RainMaximum > .02f,
                };
                uniforms.Apply(march, field, frame, noise);
                CloudVolumeUniforms.ApplySpans(march, 105000f, 315000f);
                march.SetTexture("_WeatherMapTex", near); march.SetTexture("_WeatherProfileTex", nearProfiles);
                march.SetTexture("_WeatherFarMapTex", far); march.SetTexture("_WeatherFarProfileTex", farProfiles);
                march.SetTexture("_WeatherEnvelopeTex", envelope); march.SetFloat("_WeatherEnvelopeOn", 1f);
                var sums = new double[Height];
                for (int azimuth = 0; azimuth < Azimuths; azimuth++)
                {
                    camera.transform.rotation = Quaternion.Euler(-pitch, azimuth * 5f, 0f);
                    uniforms.ApplyView(camera, camera.transform.position);
                    Matrix4x4 box = Matrix4x4.TRS(camera.transform.position, Quaternion.identity, Vector3.one * 1000f);
                    cmd.Clear();
                    cmd.DrawMesh(cube, box, march, 0, 0);
                    camera.Render();
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    readback.Apply();
                    RenderTexture.active = null;
                    var raw = readback.GetRawTextureData<byte>();
                    for (int y = 0; y < Height; y++)
                    {
                        int at = y * Width * 4 + 3;
                        long total = 0;
                        for (int x = 0; x < Width; x++, at += 4) total += raw[at];
                        sums[y] += total;
                    }
                    renders++;
                }
                for (int y = 0; y < Height; y++) sums[y] /= 255.0 * Width * Azimuths;
                rowSum[seedIndex] = sums;
                UnityEngine.Object.Destroy(near); UnityEngine.Object.Destroy(nearProfiles);
                UnityEngine.Object.Destroy(far); UnityEngine.Object.Destroy(farProfiles); UnityEngine.Object.Destroy(envelope);
            }
            Report(log, sky, rowSum, heights, depths, eye);
        }
        log.AppendLine("seam: " + renders + " renders, " + (watch.Elapsed.TotalMilliseconds / Math.Max(1, renders)).ToString("0.0") + " ms/frame including readback");
    }

    private sealed class SeedRows
    {
        public double[] Mean;     // opacity per row (row 0 is the bottom of the frame)
        public float[] Distance;  // the deck-plane hit distance of each row's elevation, infinite for rows that do not meet the plane
        public float Start, End;  // where wDeck leaves 0 and reaches 1 for this seed's deck (H, hd)
        public readonly List<int> A = new List<int>(), Band = new List<int>(), B = new List<int>();
    }

    private static void Report(StringBuilder log, int sky, double[][] rowSum, float[] heights, float[] depths, float eye)
    {
        var seeds = new SeedRows[Seeds];
        float heightMin = float.MaxValue, heightMax = float.MinValue, depthMin = float.MaxValue, depthMax = float.MinValue;
        for (int k = 0; k < Seeds; k++)
        {
            float height = heights[k], depth = depths[k];
            heightMin = Math.Min(heightMin, height); heightMax = Math.Max(heightMax, height);
            depthMin = Math.Min(depthMin, depth); depthMax = Math.Max(depthMax, depth);
            float span = height - eye, sign = span >= 0f ? 1f : -1f, absSpan = Math.Abs(span);
            var s = new SeedRows { Mean = rowSum[k], Distance = new float[Height] };
            s.Start = Bisect(d => Hand(d, height, depth, eye, out _));
            s.End = Bisect(d => { float g = Hand(d, height, depth, eye, out float hw); return g - 2f * hw; });
            for (int y = 0; y < Height; y++)
            {
                // The elevation of this row at the centre column, and the distance at which that ray meets the deck plane.
                double elevation = sign * CentreElevation + Math.Atan(((y + 0.5) / (Height / 2.0) - 1.0) * Math.Tan(FovDegrees * 0.5 * Math.PI / 180.0)) * 180.0 / Math.PI;
                double up = sign * elevation * Math.PI / 180.0;
                float d = up > 1e-7 ? (float)(Math.Abs(span) / Math.Tan(up)) : float.PositiveInfinity;
                s.Distance[y] = d;
                if (d >= 120000f && d <= s.Start - 3000f) s.A.Add(y);
                else if (d >= s.Start && d <= s.End) s.Band.Add(y);
                else if (d >= s.End + 6000f && d <= 340000f) s.B.Add(y);
            }
            seeds[k] = s;
        }
        string tag = "seam sky " + (sky + 1).ToString("00") + ": ";
        string heightText = string.Format(CultureInfo.InvariantCulture, "eye {0:0} m, H {1:0}..{2:0} m, hd {3:0}..{4:0} m", eye, heightMin, heightMax, depthMin, depthMax);
        foreach (SeedRows s in seeds)
            if (s.A.Count == 0 || s.Band.Count == 0 || s.B.Count == 0)
            {
                log.AppendLine("SKIP " + tag + "a band has no rows (A " + s.A.Count + ", band " + s.Band.Count + ", B " + s.B.Count + "; " + heightText + ")");
                return;
            }
        double Mean(List<int> rows, double[] values) { double sum = 0; foreach (int y in rows) sum += values[y]; return sum / rows.Count; }
        var aSeed = new double[Seeds]; var bSeed = new double[Seeds];
        double meanA = 0, meanB = 0, rowsA = 0, rowsBand = 0, rowsB = 0;
        for (int k = 0; k < Seeds; k++)
        {
            aSeed[k] = Mean(seeds[k].A, seeds[k].Mean); bSeed[k] = Mean(seeds[k].B, seeds[k].Mean);
            meanA += aSeed[k] / Seeds; meanB += bSeed[k] / Seeds;
            rowsA += seeds[k].A.Count / (double)Seeds; rowsBand += seeds[k].Band.Count / (double)Seeds; rowsB += seeds[k].B.Count / (double)Seeds;
        }
        double SeOf(double[] values, double avg) { double v = 0; foreach (double x in values) v += (x - avg) * (x - avg) / (Seeds - 1); return Math.Sqrt(v / Seeds); }
        // The 9-row box mean in the band against the straight line from mean A to mean B, at 25 places across the band, averaged over seeds.
        const int places = 24;
        double worst = 0;
        for (int i = 0; i <= places; i++)
        {
            double u = i / (double)places, deviation = 0;
            foreach (SeedRows s in seeds)
            {
                float target = s.Start + (float)u * (s.End - s.Start);
                int best = s.Band[0];
                foreach (int y in s.Band) if (Math.Abs(s.Distance[y] - target) < Math.Abs(s.Distance[best] - target)) best = y;
                double box = 0; int n = 0;
                for (int j = Math.Max(0, best - 4); j <= Math.Min(Height - 1, best + 4); j++) { box += s.Mean[j]; n++; }
                deviation += (box / n - (meanA + u * (meanB - meanA))) / Seeds;
            }
            worst = Math.Max(worst, Math.Abs(deviation));
        }
        bool pass = Math.Abs(meanA - meanB) <= Tolerance && worst <= Tolerance;
        var csv = new StringBuilder("seed,row,distance_m,opacity" + Environment.NewLine);
        for (int k = 0; k < Seeds; k++)
            for (int y = 0; y < Height; y++)
                if (float.IsFinite(seeds[k].Distance[y]) && seeds[k].Distance[y] <= 400000f)
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F0},{3:F5}", k, y, seeds[k].Distance[y], seeds[k].Mean[y]));
        File.WriteAllText("seam-sky-" + (sky + 1).ToString("00") + ".csv", csv.ToString());
        log.AppendLine((pass ? "PASS " : "FAIL ") + tag + string.Format(CultureInfo.InvariantCulture,
            "{0}; rows A {1:0} band {2:0} B {3:0}; mean A {4:F3} (se {5:F4}) mean B {6:F3} (se {7:F4}) |A-B| {8:F3}; band box deviation {9:F3} (limit {10:F2}){11}",
            heightText, rowsA, rowsBand, rowsB, meanA, SeOf(aSeed, meanA), meanB, SeOf(bSeed, meanB), Math.Abs(meanA - meanB), worst, Tolerance,
            pass ? "" : "; HorizonStack x " + (meanB > 1e-4 ? (meanA / meanB).ToString("0.000", CultureInfo.InvariantCulture) : "n/a")));
    }

    private static Texture2D Map(byte[] data, int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        t.LoadRawTextureData(data);
        t.Apply(false, false);
        return t;
    }
}
#endif

#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Tests.Features.Weather;
using UnityEngine;

// The bench's sky contact-sheet mode (spec 10.3): one pose per library sky, a metrics row per sky and one sheet per class. Selected by
// CLOUD_BENCH_SKIES ("all", a class name such as "Fair", or sky numbers such as "8,17,29"). Not part of the default player run.
internal sealed class CloudBenchSkies
{
    public const float Pitch = 9.8f; // the horizon sits at 65 % of the frame height, like the reference thumbnails
    /// <summary>Two skies of one class whose thumbnails differ by less than this (mean absolute luminance, rows 7-77) look alike. An initial figure: change the skies, never the floor.</summary>
    public const float DistinctFloor = 0.02f;
    private const int Thumbnail = 480;

    private readonly int[] order;
    private readonly Dictionary<int, Color32[]> thumbs = new Dictionary<int, Color32[]>();
    private readonly Dictionary<int, Color32[]> cloudy = new Dictionary<int, Color32[]>();
    private readonly Dictionary<int, SkyMetrics.Result> results = new Dictionary<int, SkyMetrics.Result>();
    private readonly HashSet<WeatherRegimeType> flushed = new HashSet<WeatherRegimeType>();

    private CloudBenchSkies(int[] order) { this.order = order; }

    /// <summary>The selected skies, or null when the environment does not ask for the sky sheets.</summary>
    public static CloudBenchSkies FromEnvironment()
    {
        string filter = Environment.GetEnvironmentVariable("CLOUD_BENCH_SKIES");
        if (string.IsNullOrEmpty(filter)) return null;
        var picked = new List<int>();
        foreach (string part in filter.Split(','))
        {
            string name = part.Trim();
            if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
                for (int i = 0; i < SkyLibrary.Count; i++) picked.Add(i);
            else if (int.TryParse(name, out int number) && number >= 1 && number <= SkyLibrary.Count) picked.Add(number - 1);
            else if (!int.TryParse(name, out _) && Enum.TryParse(name, true, out WeatherRegimeType cls))
            { for (int i = 0; i < SkyLibrary.Count; i++) if (SkyLibrary.ClassOf(i) == cls) picked.Add(i); }
            else throw new Exception("CLOUD_BENCH_SKIES: unknown selector '" + name + "'");
        }
        File.WriteAllText("sky-metrics.csv", "sky,name,class,nominalCover,cloudFraction,openFraction,lumaStdDev,litDark,featureDegrees,verdict\n");
        File.WriteAllText("sky-distinct.csv", "class,skyA,skyB,meanAbsLumaDifference,verdict\n");
        return new CloudBenchSkies(picked.Distinct().ToArray());
    }

    public IReadOnlyList<int> Skies => order;

    public bool Includes(int sky) => Array.IndexOf(order, sky) >= 0;

    /// <summary>The scene name of a sky in the sheet run: sky-08-fair-weather-cumulus (so -Only matches by substring).</summary>
    public static string SceneName(int index)
    {
        var slug = new StringBuilder();
        foreach (char c in SkyLibrary.Get(index).Name.ToLowerInvariant()) slug.Append(char.IsLetterOrDigit(c) ? c : '-');
        return "sky-" + (index + 1).ToString("00") + "-" + slug;
    }

    /// <summary>Texture2D pixels run bottom row first; the metrics read the frame from the top (the reference thumbnails' rows 7-77).</summary>
    private static float[] Luma(Color32[] pixels, int width, int height)
    {
        var luma = new float[pixels.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Color32 p = pixels[(height - 1 - y) * width + x];
                luma[y * width + x] = (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) / 255f;
            }
        return luma;
    }

    /// <summary>Per pixel, the largest channel difference (0-1) between the frame with clouds and the same pose without, rows from the top.
    /// A grey deck under a blue sky differs in colour more than in luminance, so cover is judged on this.</summary>
    private static float[] Difference(Color32[] cloudyPixels, Color32[] clearPixels, int width, int height)
    {
        var difference = new float[cloudyPixels.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (height - 1 - y) * width + x;
                Color32 a = cloudyPixels[i], b = clearPixels[i];
                int d = Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));
                difference[y * width + x] = d / 255f;
            }
        return difference;
    }

    /// <summary>Whether the sky's cloud is a cumulus group or a storm cell, which the sheet frames from below at its edge (CloudBench place 10).</summary>
    public static bool IsFeature(int index)
    {
        SkyDef def = SkyLibrary.Get(index);
        return SkyMetrics.For(def.Nominal, def.HasLow, def.Genus.Genus).Feature;
    }

    /// <summary>The frame with clouds: remember its pixels and a thumbnail.</summary>
    public void Capture(int sky, Texture2D frame)
    {
        Color32[] pixels = frame.GetPixels32();
        cloudy[sky] = pixels;
        thumbs[sky] = Downscale(pixels, frame.width, frame.height);
    }

    /// <summary>The same pose without clouds: measure, write the row, and write the class sheet once every selected sky of it is in.</summary>
    public void Finish(int sky, Texture2D clearFrame, float degreesPerPixel, StringBuilder log)
    {
        int width = clearFrame.width, height = clearFrame.height;
        Color32[] clearPixels = clearFrame.GetPixels32();
        SkyDef def = SkyLibrary.Get(sky);
        SkyMetrics.Result r = SkyMetrics.Measure(Luma(cloudy[sky], width, height), Difference(cloudy[sky], clearPixels, width, height), width, height, degreesPerPixel);
        SkyMetrics.Expectation e = SkyMetrics.For(def.Nominal, def.HasLow, def.Genus.Genus);
        List<string> failed = SkyMetrics.Failures(r, e);
        results[sky] = r;
        string verdict = failed.Count == 0 ? "PASS" : "FAIL " + string.Join("; ", failed);
        File.AppendAllText("sky-metrics.csv", string.Join(",", (sky + 1).ToString(), def.Name.Replace(',', ' '), def.Class.ToString(), F(e.Cover),
            F(r.CloudFraction), F(r.OpenFraction), F(r.LumaStdDev), r.HasLitDark ? F(r.LitDarkRatio) : "", F(r.FeatureDegrees), "\"" + verdict + "\"") + "\n");
        log.AppendLine("sky " + (sky + 1).ToString("00") + " " + def.Name.PadRight(36) + " cover " + r.CloudFraction.ToString("0.00") + " (nominal " + e.Cover.ToString("0.00") +
            ") std " + r.LumaStdDev.ToString("0.000") + " lit/dark " + (r.HasLitDark ? r.LitDarkRatio.ToString("0.0") : "-") + " feature " + r.FeatureDegrees.ToString("0") + "deg  " + verdict);
        cloudy.Remove(sky);
        WriteSheetIfComplete(def.Class, log);
    }

    private void WriteSheetIfComplete(WeatherRegimeType cls, StringBuilder log)
    {
        var inClass = new List<int>();
        foreach (int i in order) if (SkyLibrary.ClassOf(i) == cls) inClass.Add(i);
        foreach (int i in inClass) if (!thumbs.ContainsKey(i)) return;
        if (!flushed.Add(cls)) return;
        // Skies of one class must not look alike: the mean absolute luminance difference of every pair of thumbnails over the metrics' rows.
        int tileHeight = Thumbnail * 9 / 16;
        for (int a = 0; a < inClass.Count; a++)
            for (int b = a + 1; b < inClass.Count; b++)
            {
                float difference = SkyMetrics.MeanAbsDifference(Luma(thumbs[inClass[a]], Thumbnail, tileHeight), Luma(thumbs[inClass[b]], Thumbnail, tileHeight), Thumbnail, tileHeight);
                string verdict = difference >= DistinctFloor ? "PASS" : "FAIL";
                File.AppendAllText("sky-distinct.csv", cls + "," + (inClass[a] + 1) + "," + (inClass[b] + 1) + "," + F(difference) + "," + verdict + "\n");
                if (verdict == "FAIL")
                    log.AppendLine("distinct " + cls + ": sky " + (inClass[a] + 1) + " and sky " + (inClass[b] + 1) + " look alike (mean luminance difference " +
                        difference.ToString("0.000") + ", floor " + DistinctFloor.ToString("0.000") + ")");
            }
        int columns = 4, rows = (inClass.Count + columns - 1) / columns, tileH = Thumbnail * 9 / 16;
        var sheet = new Texture2D(columns * Thumbnail, rows * tileH, TextureFormat.RGBA32, false);
        var pixels = new Color32[sheet.width * sheet.height];
        for (int k = 0; k < inClass.Count; k++)
        {
            Color32[] tile = thumbs[inClass[k]];
            int ox = (k % columns) * Thumbnail, oy = (rows - 1 - k / columns) * tileH;
            for (int y = 0; y < tileH; y++) Array.Copy(tile, y * Thumbnail, pixels, (oy + y) * sheet.width + ox, Thumbnail);
        }
        sheet.SetPixels32(pixels);
        File.WriteAllBytes("sheet-" + cls.ToString().ToLowerInvariant() + ".png", sheet.EncodeToPNG());
        UnityEngine.Object.Destroy(sheet);
        foreach (int i in inClass) thumbs.Remove(i);
    }

    /// <summary>A box-filter downscale to Thumbnail x (Thumbnail * 9 / 16).</summary>
    private static Color32[] Downscale(Color32[] source, int width, int height)
    {
        int tileH = Thumbnail * 9 / 16;
        var result = new Color32[Thumbnail * tileH];
        float sx = (float)width / Thumbnail, sy = (float)height / tileH;
        for (int y = 0; y < tileH; y++)
            for (int x = 0; x < Thumbnail; x++)
            {
                int x0 = (int)(x * sx), x1 = Math.Max(x0 + 1, (int)((x + 1) * sx)), y0 = (int)(y * sy), y1 = Math.Max(y0 + 1, (int)((y + 1) * sy));
                int r = 0, g = 0, b = 0, n = 0;
                for (int yy = y0; yy < y1 && yy < height; yy++)
                    for (int xx = x0; xx < x1 && xx < width; xx++) { Color32 c = source[yy * width + xx]; r += c.r; g += c.g; b += c.b; n++; }
                result[y * Thumbnail + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
            }
        return result;
    }

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
}
#endif

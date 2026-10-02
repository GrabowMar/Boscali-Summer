using System;
using System.Collections.Generic;
using NOAvionics;
using Unity.Profiling;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// Live kit v2 measurement hooks for the nomodkit sim runner ("call" ops): Start builds three demo consoles
    /// ticking in the real player loop plus one blur-behind window; Read returns per-frame averages of the kit's
    /// profiler markers, GC allocated per frame, the UGUI canvas update and the blur probe; Stop tears it down.
    /// Diagnostics only — nothing calls this outside automation.
    /// </summary>
    public static class AvDiagnostics
    {
        private static GameObject root;
        private static AvWindow window;
        private static readonly List<ProfilerRecorder> Recorders = new List<ProfilerRecorder>(8);
        private static readonly string[] Markers = { "NOA.UI.Tick", "NOA.UI.Relayout", "NOA.UI.Restyle" };

        public static Dictionary<string, object> Start(Dictionary<string, object> args)
        {
            Stop(null);
            AvFxDriver.EnsureRunning();
            root = new GameObject("AvDiagnostics", typeof(RectTransform), typeof(Canvas));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150;
            for (int i = 0; i < 3; i++)
            {
                var host = AvLay.Child((RectTransform)root.transform, "host" + i);
                AvLay.Place(host, 20f + i * 500f, 20f, AvTokens.PanelWidth, AvTokens.PanelHeight);
                BuildDemo(host);
            }
            bool blur = args != null && args.TryGetValue("blur", out object b) && b is bool on && on;
            AvBlurSource.Enabled = blur || AvBlurSource.Enabled;
            window = AvWindow.Build(root.transform, "DIAG", "BLUR PROBE", 420f, 260f, 160);
            window.Body.Section(AvIcon.Gauge, "DIAGNOSTICS", "LIVE");
            window.Show();

            Recorders.Clear();
            foreach (string m in Markers) Recorders.Add(ProfilerRecorder.StartNew(ProfilerCategory.Scripts, m, 240));
            Recorders.Add(ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 240));
            Recorders.Add(ProfilerRecorder.StartNew(ProfilerCategory.Gui, "UGUI.Rendering.UpdateBatches", 240));
            Recorders.Add(ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PostLateUpdate.PlayerUpdateCanvases", 240));
            return new Dictionary<string, object> { { "started", true }, { "bundle", AvBundle.Available }, { "blur", AvBlurSource.Enabled } };
        }

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            var result = new Dictionary<string, object>();
            string[] names = { "tickMs", "relayoutMs", "restyleMs", "gcBytesPerFrame", "uguiBatchesMs", "canvasUpdateMs" };
            for (int i = 0; i < Recorders.Count && i < names.Length; i++)
            {
                ProfilerRecorder r = Recorders[i];
                double avg = r.Valid ? Average(r) : -1;
                bool bytes = names[i] == "gcBytesPerFrame";
                result[names[i]] = avg < 0 ? -1.0 : bytes ? avg : avg / 1e6;
            }
            foreach (KeyValuePair<string, object> kv in AvBlurSource.Probe()) result["blur_" + kv.Key] = kv.Value;
            result["fxTier"] = AvFxDriver.Tier.ToString();
            result["theme"] = AvStyleHost.Theme.ToString();
            return result;
        }

        public static Dictionary<string, object> Stop(Dictionary<string, object> args)
        {
            foreach (ProfilerRecorder r in Recorders) r.Dispose();
            Recorders.Clear();
            if (window != null) { window.Hide(); window = null; }
            if (root != null) { UnityEngine.Object.Destroy(root); root = null; }
            return new Dictionary<string, object> { { "stopped", true } };
        }

        private static double Average(ProfilerRecorder r)
        {
            int n = r.Count;
            if (n == 0) return 0;
            double sum = 0;
            for (int i = 0; i < n; i++) sum += r.GetSample(i).Value;
            return sum / n;
        }

        private static void BuildDemo(RectTransform host)
        {
            AvConsole con = AvConsole.Build(host, "DIAG", "KIT V2 / LIVE", 1);
            AvChip[] chips = con.Chips(2);
            chips[0].Set("LIVE", AvState.Ready);
            chips[1].Set("PERF", AvState.Info);
            AvMetric[] m = con.Metrics("FUNDS", "TRACKS", "MORALE");
            con.Tabs((AvIcon.Gauge, "LIVE"));
            AvFlow p = con.Page(0);
            p.Section(AvIcon.Activity, "RANDOM WALK", "10 HZ");
            bool on = true;
            AvCellGrid g = p.Grid(2);
            for (int i = 0; i < 6; i++) g.Toggle("CHANNEL " + (i + 1), "Demo toggle", () => on, v => on = v);
            var chart = p.Add(new AvLineChart(p.Content));
            var values = new float[64];
            string[] money = new string[64];
            for (int i = 0; i < 64; i++) money[i] = AvNum.Money(1e9 + i * 1.3e7);
            int t = 0;
            con.Ticker.Add(-1, AvTickRate.Fast, () =>
            {
                t++;
                m[0].Set(money[t & 63], "AVAILABLE", (t & 63) / 64f);
                m[1].Set(money[(t * 7) & 63], "LIVE", ((t * 7) & 63) / 64f);
                m[2].Set(money[(t * 3) & 63], "OF 100", ((t * 3) & 63) / 64f, AvState.Caution);
            });
            con.Ticker.Add(-1, AvTickRate.Slow, () =>
            {
                for (int i = 0; i < values.Length - 1; i++) values[i] = values[i + 1];
                values[values.Length - 1] = Mathf.PerlinNoise(t * 0.05f, 0.3f);
                chart.SetSeries(values, values.Length, "MIN", "MAX", "NOW");
            });
            con.Footer.Set("Kit v2 live diagnostics.");
            con.Finish();
        }
    }
}

using NOAvionics;
using BoscaliSummer.Core.Ui;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal sealed partial class SettingsMfdPanel
    {
        private const int FrameSamples = 60;
        private const float FrameSampleSeconds = 0.25f;
        private readonly float[] frameMs = new float[FrameSamples];
        private int frameCount;
        private float frameSampleTime;
        private int frameSampleIndex;

        private void BuildPerformancePage(AvFlow flow, int page)
        {
            var rows = clientSettings?.Rows;
            int count = rows?.Count ?? 0;
            if (count == 0)
            {
                flow.Section(AvIcon.Activity, "PERFORMANCE", "UNAVAILABLE");
                flow.Add(new AvNote(flow.Content) { MinHeight = 18f }).Set("NONE INSTALLED");
            }
            else
            {
                // One click sets every switch a feature tagged with a preset, on any tab, plus PANELS' PANEL FX and blur.
                // Sound, comfort, master switches and the frame limit are never touched.
                int[] fxTier = { (int)AvFxTier.Full, (int)AvFxTier.Lite, (int)AvFxTier.Off };
                flow.Section(AvIcon.Gauge, "PRESET", "ONE CLICK · ALSO SETS WORLD, COCKPIT, PANELS");
                var preset = flow.Add(AvSegmented.Strip(flow.Content, ClientPreset.Names,
                    () =>
                    {
                        for (int i = 0; i < fxTier.Length; i++)
                            if ((int)settings.AvionicsFxTier.Value == fxTier[i] &&
                                settings.AvionicsBlurBehind.Value == (i == 0) &&
                                clientSettings.MatchesPreset(i)) return i;
                        return -1;
                    },
                    i =>
                    {
                        clientSettings.ApplyPreset(i);
                        settings.AvionicsFxTier.Value = (AvFxTier)fxTier[i];
                        settings.AvionicsBlurBehind.Value = i == 0;
                        Echo("PRESET — " + ClientPreset.Names[i]);
                        Changed();
                    }));
                preset.Options[0].Help = "QUALITY: every Boscali visual on, full panel effects, no savings applied.";
                preset.Options[1].Help = "BALANCED: drops the costliest extras (pilot reflection, terrain wet pass, panel blur) and turns ADAPTIVE FX and the invisible far-unit savings on.";
                preset.Options[2].Help = "FAST: all optional visuals off, panel effects off, LOD and shadow caps on, units past 15 km not drawn. Sound and motion are left alone.";
                flow.Ticker.Add(page, AvTickRate.Slow, preset.Refresh);
                BuildBoardRows(flow, page, CPerf);
            }

            // Command's own budget: how often the map's sector grid is rebuilt.
            flow.Section(AvIcon.Map2, "MAP GRID", "CPU");
            var grid = flow.Add(new SetRingRow(flow.Content));
            EntryRing(flow, grid, page, "SECTOR REFRESH", settings.GridRefreshInterval, .2f, 2f, .1f, v => AvNum.Seconds(v, 1),
                "How often the map's sector grid updates. Longer intervals reduce CPU work. Recommended: 0.5 s.");

            BuildMaintenance(flow, page);

            // Live frame time: three dials and a trace that grows to fill the page, so the effect of every
            // switch above can be watched here. Sampled while this page shows, four times a second.
            flow.Section(AvIcon.Activity, "FRAME TIME", "LIVE");
            var fps = new AvGauge(flow.Content, "FPS");
            var last = new AvGauge(flow.Content, "FRAME");
            var worst = new AvGauge(flow.Content, "WORST");
            fps.Help = "FPS: frames per second averaged over the last quarter second. 55 and up is smooth.";
            last.Help = "FRAME: the latest average frame time in milliseconds. 16.7 ms is 60 fps.";
            worst.Help = "WORST: the slowest frame time in the trace below. Spikes here are stutter.";
            flow.Row(fps, last, worst);
            var chart = flow.Add(new AvLineChart(flow.Content, 96f), 1f);
            void Sample()
            {
                float now = Time.unscaledTime;
                if (frameSampleTime <= 0f) { frameSampleTime = now; frameSampleIndex = Time.frameCount; return; }
                float dt = now - frameSampleTime;
                if (dt < FrameSampleSeconds) return;
                int frames = Mathf.Max(1, Time.frameCount - frameSampleIndex);
                frameSampleTime = now; frameSampleIndex = Time.frameCount;
                float ms = dt / frames * 1000f;
                if (frameCount < FrameSamples) frameMs[frameCount++] = ms;
                else
                {
                    System.Array.Copy(frameMs, 1, frameMs, 0, FrameSamples - 1);
                    frameMs[FrameSamples - 1] = ms;
                }
                float peak = 0f, low = float.MaxValue;
                for (int i = 0; i < frameCount; i++) { peak = Mathf.Max(peak, frameMs[i]); low = Mathf.Min(low, frameMs[i]); }
                float rate = ms > 0.01f ? 1000f / ms : 0f;
                fps.Set(Mathf.Clamp01(rate / 120f), AvNum.Fixed(rate, 0),
                    rate >= 55f ? AvState.Ready : rate >= 30f ? AvState.Caution : AvState.Danger);
                last.Set(Mathf.Clamp01(ms / 33.3f), AvNum.Fixed(ms, 1),
                    ms <= 18f ? AvState.Ready : ms <= 33.4f ? AvState.Caution : AvState.Danger);
                worst.Set(Mathf.Clamp01(peak / 66.6f), AvNum.Fixed(peak, 1),
                    peak <= 33.4f ? AvState.Ready : peak <= 66.7f ? AvState.Caution : AvState.Danger);
                chart.SetSeries(frameMs, frameCount, AvNum.Fixed(low, 1), AvNum.Fixed(peak, 1), AvNum.Fixed(ms, 1) + " MS");
            }
            flow.Ticker.Add(page, AvTickRate.Fast, Sample);
        }
    }
}

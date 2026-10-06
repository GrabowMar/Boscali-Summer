using NOAvionics;
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
                string previous = null;
                AvCellGrid grid = null;
                for (int i = 0; i < count; i++)
                {
                    var row = rows[i];
                    if (row.Section != previous)
                    {
                        previous = row.Section;
                        flow.Section(AvIcon.Activity, previous, "NO RESTART");
                        grid = flow.Grid(2);
                    }
                    ToggleCell(flow, grid, page, row.Label, row.Help, () => row.Value, row.Set);
                }
            }

            flow.Section(AvIcon.Bolt, "PANEL EFFECTS", "RENDER COST");
            var blurCell = AvCell.Toggle(flow.Content, "BLUR BEHIND", "",
                () => settings.AvionicsBlurBehind.Value,
                v => { settings.AvionicsBlurBehind.Value = v; Echo("BLUR BEHIND — " + (v ? "ON" : "OFF")); });
            blurCell.Help = "Blur the world behind floating windows (up to ~0.4 ms GPU; frosted glass when off).";
            var fxSeg = new AvSegmented(flow.Content, "FX TIER", new[] { "OFF", "LITE", "FULL" },
                () => (int)settings.AvionicsFxTier.Value,
                i => { settings.AvionicsFxTier.Value = (AvFxTier)i; Echo("FX TIER — " + settings.AvionicsFxTier.Value); });
            fxSeg.Options[0].Help = "OFF: no panel animation or glass effects at all. Cheapest.";
            fxSeg.Options[1].Help = "LITE: keep the scan and shine effects, drop the heavier passes.";
            fxSeg.Options[2].Help = "FULL: every panel effect the theme defines.";
            flow.Row(blurCell, fxSeg);
            flow.Ticker.Add(page, AvTickRate.Slow, () => { fxSeg.Refresh(); blurCell.Refresh(); });

            // Live frame time: three dials and a trace that grows to fill the page, so the effect of every
            // switch above can be watched here. Sampled while this page shows, four times a second.
            flow.Section(AvIcon.Activity, "FRAME TIME", "LIVE");
            var fps = new AvGauge(flow.Content, "FPS", AvGaugeShape.Ring, 64f);
            var last = new AvGauge(flow.Content, "FRAME", AvGaugeShape.Ring, 64f);
            var worst = new AvGauge(flow.Content, "WORST", AvGaugeShape.Ring, 64f);
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

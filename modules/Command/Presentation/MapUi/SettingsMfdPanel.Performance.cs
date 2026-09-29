using BoscaliSummer.Framework.Features;
using NOAvionics;
using NOAvionics.Ui;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal sealed partial class SettingsMfdPanel
    {
        private void BuildPerformancePage(AvFlow flow, int page)
        {
            var rows = clientSettings?.Rows;
            int count = rows?.Count ?? 0;
            if (count == 0)
            {
                flow.Section(AvIcon.Activity, "PERFORMANCE", "UNAVAILABLE");
                flow.Add(new NoteLine(flow.Content)).Set("No client performance controls are installed.");
            }
            else
            {
                string previous = null;
                for (int i = 0; i < count; i++)
                {
                    var row = rows[i];
                    if (row.Section != previous)
                    {
                        previous = row.Section;
                        flow.Section(AvIcon.Activity, previous, "NO RESTART");
                    }
                    Toggle(flow, page, row.Label, row.Help, () => row.Value, row.Set);
                }
            }

            flow.Section(AvIcon.Bolt, "PANEL EFFECTS", "AVIONICS");
            var fxSeg = flow.Add(new AvSegmented(flow.Content, "FX TIER", new[] { "OFF", "LITE", "FULL" },
                () => (int)settings.AvionicsFxTier.Value,
                i => { settings.AvionicsFxTier.Value = (AvFxTier)i; Echo("FX TIER — " + settings.AvionicsFxTier.Value); }));
            var blurCell = flow.Add(AvCell.Toggle(flow.Content, "BLUR BEHIND",
                "Blur the world behind floating windows (up to ~0.4 ms GPU; frosted glass when off).",
                () => settings.AvionicsBlurBehind.Value,
                v => { settings.AvionicsBlurBehind.Value = v; Echo("BLUR BEHIND — " + (v ? "ON" : "OFF")); }));
            flow.Ticker.Add(page, AvTickRate.Slow, () => { fxSeg.Refresh(); blurCell.Refresh(); });
        }
    }
}

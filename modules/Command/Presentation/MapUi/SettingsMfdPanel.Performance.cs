using NOAvionics;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>PERF: the presets, the cost dials the features publish, the map grid budget and maintenance. The switches a preset moves live on PANELS and VISUALS, not here.</summary>
    internal sealed partial class SettingsMfdPanel
    {
        private void BuildPerformancePage(AvFlow flow, int page)
        {
            if (clientSettings != null && clientSettings.Rows.Count > 0)
            {
                // One click sets every switch a feature tagged with a preset, on any tab, plus PANELS' PANEL FX and blur.
                // Sound, comfort, master switches and the frame limit are never touched.
                int[] fxTier = { (int)AvFxTier.Full, (int)AvFxTier.Lite, (int)AvFxTier.Off };
                flow.Section(AvIcon.Gauge, "PRESET", "ONE CLICK · ALSO SETS VISUALS, PANELS");
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
        }
    }
}

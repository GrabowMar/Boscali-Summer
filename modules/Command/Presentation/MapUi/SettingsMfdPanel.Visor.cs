using NOAvionics;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The visor's own layers on the COCKPIT tab, below the TAC BOARD: today the terrain ghost (the Command module draws it, a saved client-local
    /// level that applies at once, so a pilot sees it in flight and there is no preview here).
    /// </summary>
    internal sealed partial class SettingsMfdPanel
    {
        private void BuildVisorRows(AvFlow flow, int page)
        {
            flow.Section(AvIcon.Map2, "VISOR TERRAIN", "GROUND AROUND AND BELOW");
            var level = flow.Add(new SetRingRow(flow.Content));
            PercentRing(flow, level, page, "TERRAIN GHOST", settings.TerrainGhost, 0f, 1f, .05f,
                "A faint wire relief of the ground around and below you, over the view. 0% turns it off.");
        }
    }
}

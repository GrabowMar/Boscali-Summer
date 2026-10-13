using System;
using NOAvionics;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The visor's own layers on the COCKPIT tab, below the TAC BOARD: the terrain ghost (the Command module draws it, a saved client-local
    /// level that applies at once, so a pilot sees it in flight and there is no preview here) and the HOLO MAP the map key projects while it is held (a switch, a range strip and a north-up switch,
    /// all saved client-local entries that apply at once).
    /// </summary>
    internal sealed partial class SettingsMfdPanel
    {
        // Index = the strip's segment; the config entry holds the kilometres (10, 20 or 40).
        private static readonly int[] HoloRanges = { 10, 20, 40 };
        private static readonly string[] HoloRangeNames = { "10 KM", "20 KM", "40 KM" };
        private static readonly string[] HoloRangeBriefs =
        {
            "10 KM: the ground close around you, with the most detail.",
            "20 KM: the default picture.",
            "40 KM: a wide picture, the coast and the next valley over.",
        };

        private void BuildVisorRows(AvFlow flow, int page)
        {
            flow.Section(AvIcon.Map2, "VISOR TERRAIN", "GROUND AROUND AND BELOW");
            var level = flow.Add(new SetRingRow(flow.Content));
            PercentRing(flow, level, page, "TERRAIN GHOST", settings.TerrainGhost, 0f, 1f, .05f,
                "A faint wire relief of the ground around and below you, over the view. 0% turns it off.");

            // The map key's middle stop: the switch first, then the radius and the orientation, which wait on the switch as the TAC BOARD's options wait on the board.
            Func<bool> holoOn = () => settings.HoloMap.Value;
            Func<string> holoOff = () => "Turn on HOLO MAP first.";
            ToggleCell(flow, flow.Grid(1), page, "HOLO MAP",
                "Hold the map key to project a 3D hologram of the ground around you; tap it for the full map.",
                () => settings.HoloMap.Value, v => settings.HoloMap.Value = v);
            BoardStrip(flow, page, "HOLO RANGE", HoloRangeNames, HoloRangeBriefs,
                () => Array.IndexOf(HoloRanges, settings.HoloRangeKm.Value), i => settings.HoloRangeKm.Value = HoloRanges[i], holoOn, holoOff);
            ToggleCell(flow, flow.Grid(1), page, "NORTH UP",
                "ON keeps north at the top of the hologram. OFF turns the hologram so your heading is up.",
                () => settings.HoloNorthUp.Value, v => settings.HoloNorthUp.Value = v, holoOn, holoOff);
        }
    }
}

using System;
using BoscaliSummer.Modules.Command.Configuration;
using NOAvionics;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The visor's own layers on the COCKPIT tab, below the TAC BOARD: the terrain ghost (the Command module draws it, a saved client-local
    /// level that applies at once, so a pilot sees it in flight and there is no preview here), the TAC MAP (the corner map as a small hologram, or the game's own: a HOLO / CLASSIC strip and a 5 / 10 / 20 KM
    /// range strip) and the HOLO MAP the map key projects while it is held (a switch, a range strip and a north-up switch), all saved client-local entries that apply at once.
    /// </summary>
    internal sealed partial class SettingsMfdPanel
    {
        // Index = the strip's segment; the TAC MAP's entry holds the mode, the range entries hold the kilometres (5, 10 or 20 for the mini; 10, 20 or 40 for the held holo).
        private static readonly string[] TacMapNames = { "HOLO", "CLASSIC" };
        private static readonly string[] TacMapBriefs =
        {
            "HOLO: the corner map is a small hologram of the ground around you; hold the map key to grow it.",
            "CLASSIC: the game's own corner map, as before. Holding the map key still raises the hologram.",
        };
        private static readonly int[] MiniRanges = { 5, 10, 20 };
        private static readonly string[] MiniRangeNames = { "5 KM", "10 KM", "20 KM" };
        private static readonly string[] MiniRangeBriefs =
        {
            "5 KM: the ground right around you, in the most detail.",
            "10 KM: the default picture.",
            "20 KM: a wide picture of the coast and the next valley.",
        };
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

            // The corner map: a small hologram or the game's own, and how far the small one reaches (which waits on the mode).
            Func<bool> tacHolo = () => settings.TacMap.Value == TacMapMode.Holo;
            BoardStrip(flow, page, "TAC MAP", TacMapNames, TacMapBriefs,
                () => settings.TacMap.Value == TacMapMode.Holo ? 0 : 1, i => settings.TacMap.Value = i == 0 ? TacMapMode.Holo : TacMapMode.Classic, () => true, () => "");
            BoardStrip(flow, page, "MINI RANGE", MiniRangeNames, MiniRangeBriefs,
                () => Array.IndexOf(MiniRanges, settings.TacMapRangeKm.Value), i => settings.TacMapRangeKm.Value = MiniRanges[i], tacHolo, () => "Set TAC MAP to HOLO first.");

            // The map key's middle stop: the switch first, then the radius and the orientation, which wait on the switch as the TAC BOARD's options wait on the board. NORTH UP also turns the mini hologram, so it waits on neither being off.
            Func<bool> holoOn = () => settings.HoloMap.Value;
            Func<string> holoOff = () => "Turn on HOLO MAP first.";
            Func<bool> turns = () => holoOn() || tacHolo();
            Func<string> turnsOff = () => "Turn on HOLO MAP or set TAC MAP to HOLO first.";
            ToggleCell(flow, flow.Grid(1), page, "HOLO MAP",
                "Hold the map key to project a 3D hologram of the ground around you; tap it for the full map.",
                () => settings.HoloMap.Value, v => settings.HoloMap.Value = v);
            BoardStrip(flow, page, "HOLO RANGE", HoloRangeNames, HoloRangeBriefs,
                () => Array.IndexOf(HoloRanges, settings.HoloRangeKm.Value), i => settings.HoloRangeKm.Value = HoloRanges[i], holoOn, holoOff);
            ToggleCell(flow, flow.Grid(1), page, "NORTH UP",
                "ON keeps north at the top of the hologram. OFF turns it so your heading is up.",
                () => settings.HoloNorthUp.Value, v => settings.HoloNorthUp.Value = v, turns, turnsOff);
        }
    }
}

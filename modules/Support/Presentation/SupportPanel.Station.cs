using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;
using NOAvionics;
using NOAvionics.Ui;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPACE status: fixed station sector, construction, health and voice loop. Orders belong to
    /// the station room; this page is the glance plus one door into it.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private static readonly string[] TileKeys =
            { "POWER", "THERMAL", "FUEL", "LINK", "CREW", "DEBRIS", "ORBIT", "MODULES" };

        private AvFlow stationFlow;
        private AvSection stationSection;
        private AvReadout stationReadout;
        private AvChip[] stationTiles;
        private AvRow stationEnergyRow, stationFuelRow;
        private LogLines stationLog;
        private AvControl stationConsoleButton;
        private AvCard stationEmptyCard;
        private bool stationExists = true; // forces the first refresh to reconcile visibility

        private void ResetStationPage()
        {
            stationFlow = null;
            stationSection = null;
            stationReadout = null;
            stationTiles = null;
            stationEnergyRow = stationFuelRow = null;
            stationLog = null;
            stationConsoleButton = null;
            stationEmptyCard = null;
            stationExists = true;
        }

        // ---- Build -----------------------------------------------------------------------------

        private void BuildStationPage(AvFlow status)
        {
            stationFlow = status;
            stationSection = status.Section(AvIcon.Satellite, OrbitalPlatform.Callsign + " · STATION", "");
            stationReadout = status.Add(new AvReadout(status.Content));

            AvButtons buttons = status.Buttons(new AvControl.Spec("OPEN TASKING", OpenStationConsole, AvButtonStyle.Primary, AvIcon.Map2));
            stationConsoleButton = buttons.Controls[0];

            status.Section(AvIcon.Activity, "HEALTH · RESOURCES");
            stationTiles = BuildChipRow(status, TileKeys);
            stationEnergyRow = status.Add(new AvRow(status.Content));
            stationFuelRow = status.Add(new AvRow(status.Content));

            stationEmptyCard = status.Add(new AvCard(status.Content, status.Ticker, status.Inner, "NO STATION ON ORBIT"));
            stationEmptyCard.Flow.Add(new NoteText(stationEmptyCard.Flow.Content)).Set(
                "Launch " + OrbitalPlatform.Callsign + "'s core from Engineering in the task map; every other module " +
                "docks to it, providing persistent coverage from a fixed position.");
            stationEmptyCard.Flow.Buttons(new AvControl.Spec("OPEN TASKING", OpenStationConsole, AvButtonStyle.Primary, AvIcon.Map2));

            status.Section(AvIcon.ListDetails, "VOICE LOOP · FLIGHT");
            stationLog = status.Add(new LogLines(status.Content, LoopLines));
        }

        // ---- Refresh ---------------------------------------------------------------------------

        private void RefreshStationPage(OrbitalPlatform platform, double now)
        {
            if (stationReadout == null) return;
            bool station = platform != null && platform.Exists;
            if (station != stationExists)
            {
                stationExists = station;
                stationReadout.Rect.gameObject.SetActive(station);
                foreach (AvChip chip in stationTiles) chip.Rect.gameObject.SetActive(station);
                stationEnergyRow.Rect.gameObject.SetActive(station);
                stationFuelRow.Rect.gameObject.SetActive(station);
                stationConsoleButton.Rect.parent.gameObject.SetActive(station);
                stationEmptyCard.Rect.gameObject.SetActive(!station);
                stationFlow.RequestRelayout();
            }
            stationLog.Write(loop);
            if (!station) return;

            PlatformStats stats = platform.Stats(now);
            OrbitState state = platform.State(now);
            RefreshStationHero(platform, stats, state, now);
            RefreshStationTiles(platform, stats, state, now);
            RefreshStationResources(platform, stats);
        }

        private void RefreshStationHero(OrbitalPlatform platform, in PlatformStats stats, in OrbitState state, double now)
        {
            PlatformHold hold = platform.HoldAt(now);
            bool ready = hold == PlatformHold.None;
            AvState tone = ready ? platform.Brownout ? AvState.Danger : AvState.Ready : AvState.Info;
            string word = ready ? platform.Brownout ? "POWER LOW" : "ON STATION" : PlatformWords.Hold(hold);
            OrbitRegime orbit = platform.Orbit;
            string sector = StationKeeping.Name(platform.PositionIndex) + " · " + orbit.Name + " · " +
                             TheaterGrid.Km(orbit.Altitude) + " KM";
            PlatformFitStep step = PlatformMissions.Next(platform, plan.Mission, now);
            string mission = step.Complete
                ? PlatformMissions.Name(plan.Mission) + " LOADOUT FITTED"
                : PlatformMissions.Name(plan.Mission) + " " + step.Fitted + "/" + step.Total + " · NEXT " +
                  PlatformModules.Info(step.Module).Name;
            stationReadout.Set(word, sector, mission);
            stationSection.SetCaption(AvStates.Glyph(tone) + word);
        }

        private void RefreshStationTiles(OrbitalPlatform platform, in PlatformStats stats, in OrbitState state, double now)
        {
            float charge = stats.StorageKj > 0f ? platform.Energy / stats.StorageKj : 0f;
            SetChip(stationTiles[0], "POWER",
                platform.Brownout ? "BROWNOUT" : charge < 0.25f ? "LOW " + AvNum.Percent(charge) :
                    stats.NetEclipseKw < 0f && state.InPass ? "ON CELLS" : "NOMINAL",
                platform.Brownout ? AvState.Danger : charge < 0.25f ? AvState.Caution : AvState.Ready);

            string hot = platform.RunsHot(ModuleKind.Emp, now) && platform.Fitted(ModuleKind.Emp) ? "EMP"
                : platform.RunsHot(ModuleKind.Reactor, now) && platform.Fitted(ModuleKind.Reactor) ? "RTG" : null;
            SetChip(stationTiles[1], "THERMAL", hot != null ? "HOT · " + hot : "NOMINAL", hot != null ? AvState.Caution : AvState.Ready);

            if (stats.FuelCapacity <= 0f) SetChip(stationTiles[2], "FUEL", "NO TANKS", AvState.Inert);
            else
            {
                float fuel = platform.Fuel / stats.FuelCapacity;
                string drag = platform.Orbit.DragFuelPerSecond > 0f ? " · DRAG" : "";
                SetChip(stationTiles[2], "FUEL", platform.Fuel <= 0.5f ? "DRY" : AvNum.Percent(fuel) + drag,
                    platform.Fuel <= 0.5f ? AvState.Danger : fuel < 0.25f ? AvState.Caution : AvState.Ready);
            }

            PlatformHold hold = platform.HoldAt(now);
            SetChip(stationTiles[3], "LINK", hold != PlatformHold.None ? "HOLD" : "LINKED", hold != PlatformHold.None ? AvState.Info : AvState.Ready);
            SetChip(stationTiles[4], "CREW", stats.Crewed ? "3 ABOARD" : "UNCREWED", stats.Crewed ? AvState.Ready : AvState.Inert);

            int down = -1;
            for (int i = 0; i < OrbitalPlatform.CellCount && down < 0; i++)
                if (platform.Cell(i) != ModuleKind.None && !platform.IsOnline(i, now)) down = i;
            bool deflected = platform.Notice == PlatformNotice.DebrisDeflected;
            SetChip(stationTiles[5], "DEBRIS", down >= 0 ? "HIT · " + PlatformModules.Info(platform.Cell(down)).Code : deflected ? "DEFLECTED" : "CLEAR",
                down >= 0 ? AvState.Danger : deflected ? AvState.Info : AvState.Ready);
            SetChip(stationTiles[6], "ORBIT", platform.Orbit.Code + (hold == PlatformHold.None ? "" : " · BURN"),
                hold == PlatformHold.None ? AvState.Ready : AvState.Info);
            SetChip(stationTiles[7], "MODULES", stats.Online + "/" + stats.Modules + " ONLINE", stats.Online < stats.Modules ? AvState.Danger : AvState.Ready);
        }

        private void RefreshStationResources(OrbitalPlatform platform, in PlatformStats stats)
        {
            string energy = AvNum.Thousands(platform.Energy) + "/" + AvNum.Thousands(stats.StorageKj) + " KJ · SUN " +
                             PlatformWords.Kilowatts(stats.NetSunKw) + " · DARK " + PlatformWords.Kilowatts(stats.NetEclipseKw);
            stationEnergyRow.Set("ENERGY", energy, "", platform.Brownout ? AvState.Danger : AvState.Info);

            string fuel = (stats.FuelCapacity > 0f ? "FUEL " + AvNum.Thousands(platform.Fuel) : "NO TANKS") + " · " +
                          (stats.RodCapacity > 0 ? "RODS " + platform.Rods + "/" + stats.RodCapacity : "NO RODS") + " · " +
                          PlatformWords.Tonnes(stats.Mass) + "/" + PlatformWords.Tonnes(OrbitalPlatform.MassLimit);
            stationFuelRow.Set("MASS", fuel, "", AvState.Info);
        }
    }
}

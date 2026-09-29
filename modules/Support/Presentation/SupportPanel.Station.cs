using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Presentation.Viz;
using NOAvionics;
using NOAvionics.Ui;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPACE status: the orbit schematic as the hero (or, with no station, its one empty-state card
    /// with LAUNCH CORE), the door into the task map and engineering, health as labelled meters,
    /// the module roster and the voice loop. Orders belong to the rooms; this page is the glance.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private static readonly string[] TileKeys = { "THERMAL", "LINK", "CREW", "DEBRIS", "SUN", "DARK" };

        private AvFlow stationFlow;
        private OrbitHero stationHero;
        private AvButtons stationButtons;
        private AvSection healthSection, moduleSection;
        private MeterRow meterEnergy, meterFuel, meterRods, meterMass;
        private AvChip[] stationTiles;
        private RosterGrid roster;
        private LogTape stationLog;
        private AvControl stationConsoleButton;
        private readonly byte[] heroCells = new byte[OrbitHero.Cells];

        private void ResetStationPage()
        {
            stationFlow = null;
            stationHero = null;
            stationButtons = null;
            healthSection = moduleSection = null;
            meterEnergy = meterFuel = meterRods = meterMass = null;
            stationTiles = null;
            roster = null;
            stationLog = null;
            stationConsoleButton = null;
        }

        // ---- Build -----------------------------------------------------------------------------

        private void BuildStationPage(AvFlow status)
        {
            stationFlow = status;
            stationHero = status.Add(new OrbitHero(status.Content));
            stationHero.AddCta(new AvControl.Spec("LAUNCH CORE", OpenEngineering, AvButtonStyle.Primary, AvIcon.Satellite))
                .Help = "Open Engineering and launch " + OrbitalPlatform.Callsign + "'s core; every other module docks to it.";

            stationButtons = status.Buttons(
                new AvControl.Spec("OPEN TASKING", OpenStationConsole, AvButtonStyle.Primary, AvIcon.Map2),
                new AvControl.Spec("ENGINEERING", OpenEngineering, AvButtonStyle.Default, AvIcon.Settings));
            stationConsoleButton = stationButtons.Controls[0];
            stationConsoleButton.Help = "The tasking map: power focus, targeting solution and quick map actions.";
            stationButtons.Controls[1].Help = "The engineering wall: loadouts, the blueprint, the module rack and every launch.";

            healthSection = status.Section(AvIcon.Activity, "STATION HEALTH", "");
            meterEnergy = status.Add(new MeterRow(status.Content, "ENERGY"));
            meterFuel = status.Add(new MeterRow(status.Content, "FUEL"));
            meterRods = status.Add(new MeterRow(status.Content, "RODS"));
            meterMass = status.Add(new MeterRow(status.Content, "MASS"));
            stationTiles = BuildChipRow(status, TileKeys, 3);

            moduleSection = status.Section(AvIcon.Stack2, "MODULES", "");
            roster = status.Add(new RosterGrid(status.Content));

            status.Section(AvIcon.ListDetails, "VOICE LOOP · FLIGHT");
            stationLog = status.Add(new LogTape(status.Content, LoopLines));
        }

        private void SetStationParts(bool station)
        {
            stationButtons.SetShown(station);
            healthSection.SetShown(station);
            meterEnergy.SetShown(station);
            meterFuel.SetShown(station);
            meterRods.SetShown(station);
            meterMass.SetShown(station);
            foreach (AvChip chip in stationTiles) chip.SetShown(station);
            moduleSection.SetShown(station);
            roster.SetShown(station);
        }

        // ---- Refresh ---------------------------------------------------------------------------

        private void RefreshStationPage(OrbitalPlatform platform, double now)
        {
            if (stationHero == null) return;
            bool station = platform != null && platform.Exists;
            SetStationParts(station);
            stationLog.Write(loop);
            if (!station)
            {
                stationHero.ShowEmpty("NO STATION ON ORBIT",
                    "Launch " + OrbitalPlatform.Callsign + "'s core from Engineering; every other module docks to it, " +
                    "giving persistent coverage from a fixed sector.", "NO CONTACT");
                return;
            }

            PlatformStats stats = platform.Stats(now);
            OrbitState state = platform.State(now);
            RefreshStationHero(platform, stats, now);
            RefreshStationHealth(platform, stats, state, now);
            RefreshStationResources(platform, stats);
            RefreshRoster(platform, stats, now);
        }

        private void RefreshStationHero(OrbitalPlatform platform, in PlatformStats stats, double now)
        {
            PlatformHold hold = platform.HoldAt(now);
            bool ready = hold == PlatformHold.None;
            AvState tone = ready ? platform.Brownout ? AvState.Danger : AvState.Ready : AvState.Info;
            string word = ready ? platform.Brownout ? "POWER LOW" : "ON STATION" : PlatformWords.Hold(hold);
            OrbitRegime orbit = platform.Orbit;
            string place = StationKeeping.Name(platform.PositionIndex) + " · " + orbit.Name;
            PlatformFitStep step = PlatformMissions.Next(platform, plan.Mission, now);
            string mission = step.Complete
                ? PlatformMissions.Name(plan.Mission) + " LOADOUT FITTED"
                : PlatformMissions.Name(plan.Mission) + " " + step.Fitted + "/" + step.Total + " · NEXT " +
                  PlatformModules.Info(step.Module).Name;

            for (int i = 0; i < OrbitalPlatform.CellCount; i++)
            {
                ModuleKind kind = platform.Cell(i);
                byte cell = OrbitHero.CellNone;
                if (kind == ModuleKind.Core) cell = platform.IsOnline(i, now) ? OrbitHero.CellCore : OrbitHero.CellOffline;
                else if (kind != ModuleKind.None)
                    cell = !platform.IsOnline(i, now) ? OrbitHero.CellOffline
                        : platform.RunsHot(kind, now) ? OrbitHero.CellHot : OrbitHero.CellOnline;
                if (platform.Pending != ModuleKind.None && platform.PendingCell == i) cell = OrbitHero.CellPending;
                heroCells[i] = cell;
            }

            bool moving = hold == PlatformHold.Rephase;
            int held = moving ? StationKeeping.Origin(platform.Seed) : platform.PositionIndex;
            stationHero.ShowStation(word, tone, orbit.Code + " · " + TheaterGrid.Km(orbit.Altitude) + " KM", place, mission,
                OrbitalPlatform.Callsign, held, moving ? platform.PositionIndex : -1, heroCells,
                stats.Online + "/" + stats.Modules, stats.Online < stats.Modules ? "✕ MODULES ONLINE" : "MODULES ONLINE");
        }

        private void RefreshStationHealth(OrbitalPlatform platform, in PlatformStats stats, in OrbitState state, double now)
        {
            string hot = platform.RunsHot(ModuleKind.Emp, now) && platform.Fitted(ModuleKind.Emp) ? "EMP"
                : platform.RunsHot(ModuleKind.Reactor, now) && platform.Fitted(ModuleKind.Reactor) ? "RTG" : null;
            SetChip(stationTiles[0], "THERMAL", hot != null ? "HOT · " + hot : "NOMINAL", hot != null ? AvState.Caution : AvState.Ready);

            PlatformHold hold = platform.HoldAt(now);
            SetChip(stationTiles[1], "LINK", hold != PlatformHold.None ? "HOLD" : "LINKED", hold != PlatformHold.None ? AvState.Info : AvState.Ready);
            SetChip(stationTiles[2], "CREW", stats.Crewed ? "3 ABOARD" : "NONE", stats.Crewed ? AvState.Ready : AvState.Inert);

            int down = -1;
            for (int i = 0; i < OrbitalPlatform.CellCount && down < 0; i++)
                if (platform.Cell(i) != ModuleKind.None && !platform.IsOnline(i, now)) down = i;
            bool deflected = platform.Notice == PlatformNotice.DebrisDeflected;
            SetChip(stationTiles[3], "DEBRIS", down >= 0 ? "HIT · " + PlatformModules.Info(platform.Cell(down)).Code : deflected ? "DEFLECTED" : "CLEAR",
                down >= 0 ? AvState.Danger : deflected ? AvState.Info : AvState.Ready);

            SetChip(stationTiles[4], "SUN", PlatformWords.Kilowatts(stats.NetSunKw), stats.NetSunKw >= 0f ? AvState.Ready : AvState.Caution);
            SetChip(stationTiles[5], "DARK", PlatformWords.Kilowatts(stats.NetEclipseKw), stats.NetEclipseKw >= 0f ? AvState.Ready : AvState.Info);

            string caption = platform.Brownout ? "✕ BROWNOUT" : down >= 0 ? "✕ MODULE DOWN" : hot != null ? "▲ RUNNING HOT" : "NOMINAL";
            healthSection.SetCaption(caption);
        }

        private void RefreshStationResources(OrbitalPlatform platform, in PlatformStats stats)
        {
            float charge = stats.StorageKj > 0f ? platform.Energy / stats.StorageKj : 0f;
            meterEnergy.Set(charge, AvNum.Thousands(platform.Energy) + "/" + AvNum.Thousands(stats.StorageKj) + " kJ",
                platform.Brownout ? "BROWNOUT" : charge < 0.25f ? "LOW" : "NOMINAL",
                platform.Brownout ? AvState.Danger : charge < 0.25f ? AvState.Caution : AvState.Ready);

            if (stats.FuelCapacity <= 0f) meterFuel.Set(0f, "—", "NO TANKS", AvState.Inert);
            else
            {
                float fuel = platform.Fuel / stats.FuelCapacity;
                string drag = platform.Orbit.DragFuelPerSecond > 0f ? "DRAG" : platform.Fuel <= 0.5f ? "DRY" : fuel < 0.25f ? "LOW" : "NOMINAL";
                meterFuel.Set(fuel, AvNum.Thousands(platform.Fuel) + "/" + AvNum.Thousands(stats.FuelCapacity),
                    platform.Fuel <= 0.5f ? "DRY" : drag,
                    platform.Fuel <= 0.5f ? AvState.Danger : fuel < 0.25f || platform.Orbit.DragFuelPerSecond > 0f ? AvState.Caution : AvState.Ready);
            }

            if (stats.RodCapacity <= 0) meterRods.Set(0f, "—", "NO RODS", AvState.Inert);
            else
                meterRods.Set(platform.Rods / (float)stats.RodCapacity, platform.Rods + "/" + stats.RodCapacity,
                    platform.Rods == 0 ? "EMPTY" : "LOADED", platform.Rods == 0 ? AvState.Caution : AvState.Ready);

            float mass = stats.Mass / OrbitalPlatform.MassLimit;
            meterMass.Set(mass, PlatformWords.Tonnes(stats.Mass) + "/" + PlatformWords.Tonnes(OrbitalPlatform.MassLimit),
                mass >= 0.9f ? "NEAR LIMIT" : "IN LIMIT", mass >= 0.9f ? AvState.Caution : AvState.Info);
        }

        private void RefreshRoster(OrbitalPlatform platform, in PlatformStats stats, double now)
        {
            int n = 0;
            for (int i = 0; i < OrbitalPlatform.CellCount && n < RosterGrid.MaxCells; i++)
            {
                ModuleKind kind = platform.Cell(i);
                if (kind == ModuleKind.None) continue;
                ModuleInfo info = PlatformModules.Info(kind);
                bool online = platform.IsOnline(i, now);
                bool hot = online && platform.RunsHot(kind, now);
                string word = !online ? "DOWN " + AvNum.Fixed(platform.OfflineRemaining(i, now), 0) + "S" : hot ? "HOT" : "ONLINE";
                roster.Set(n++, info.Code, info.Name, word, !online ? AvState.Danger : hot ? AvState.Caution : AvState.Ready);
            }
            if (platform.Pending != ModuleKind.None && n < RosterGrid.MaxCells)
            {
                ModuleInfo info = PlatformModules.Info(platform.Pending);
                roster.Set(n++, info.Code, info.Name, "DOCK " + PlatformWords.Clock(platform.DockAt - now), AvState.Info);
            }
            roster.SetCount(n);
            moduleSection.SetCaption(stats.Modules + "/" + OrbitalPlatform.CellCount + " FITTED · " + stats.Online + " ONLINE");
        }
    }
}

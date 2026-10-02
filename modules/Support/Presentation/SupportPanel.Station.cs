using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Orbital;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// SPACE status: the orbit schematic as the hero (or, with no station, its one empty-state card
    /// with LAUNCH CORE), the door into the task map and engineering, health as labelled meters,
    /// the fitted-module roster, the voice loop and the battery history. No section headers:
    /// every block names itself. Orders belong to the rooms and the ACTIONS page.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private static readonly string[] TileKeys = { "THERMAL", "LINK", "CREW", "DEBRIS", "SUN", "DARK" };

        private OrbitHero stationHero;
        private AvSection healthSection;
        private AvButtons stationButtons;
        private AvGauge meterEnergy, meterFuel, meterRods, meterMass;
        private AvChip[] stationTiles;
        private RosterGrid roster;
        private AvLineChart energyChart;
        private LogTape stationLog;
        private BriefCard windowCard;
        private readonly byte[] heroCells = new byte[OrbitHero.Cells];

        private void ResetStationPage()
        {
            stationHero = null;
            healthSection = null;
            stationButtons = null;
            meterEnergy = meterFuel = meterRods = meterMass = null;
            stationTiles = null;
            roster = null;
            energyChart = null;
            stationLog = null;
            windowCard = null;
        }

        // ---- Build -----------------------------------------------------------------------------

        private void BuildStationPage(AvFlow status)
        {
            stationHero = status.Add(new OrbitHero(status.Content));
            stationHero.AddCta(new AvControl.Spec("LAUNCH CORE", () =>
            {
                support.RequestCoreLaunch();
                nextRefresh = 0f;
            }, AvButtonStyle.Primary, AvIcon.Satellite))
                .Help = "Launch " + OrbitalPlatform.Callsign + "'s core directly; every other module docks to it.";

            stationButtons = status.Buttons(
                new AvControl.Spec("OPEN TASKING", OpenStationConsole, AvButtonStyle.Primary, AvIcon.Map2),
                new AvControl.Spec("ENGINEERING", OpenEngineering, AvButtonStyle.Default, AvIcon.Settings));
            stationConsoleButton = stationButtons.Controls[0];
            stationConsoleButton.Help = "BASTION fire control: Reconnaissance, Kinetic and Electromagnetic branches. Track, charge, vent and commit optional weapon improvements.";
            stationButtons.Controls[1].Help = "The engineering wall: loadouts, the blueprint, the module rack and every launch.";
            status.Section(AvIcon.Clock, "TASKING WINDOW", "");
            windowCard = status.Add(new BriefCard(status.Content));

            meterEnergy = new AvGauge(status.Content, "ENERGY", AvGaugeShape.Segments, 60f);
            meterFuel = new AvGauge(status.Content, "FUEL", AvGaugeShape.Segments, 60f);
            meterRods = new AvGauge(status.Content, "RODS", AvGaugeShape.Segments, 60f);
            meterMass = new AvGauge(status.Content, "MASS", AvGaugeShape.Segments, 60f);
            status.Row(meterEnergy, meterFuel, meterRods, meterMass);
            stationTiles = BuildChipRow(status, TileKeys, 3);

            roster = status.Add(new RosterGrid(status.Content));
            stationLog = status.Add(new LogTape(status.Content, LoopLines));
            energyChart = AddTrend(status);
        }

        private void SetStationParts(bool station)
        {
            healthSection.SetShown(station);
            stationButtons.SetShown(station);
            meterEnergy.SetShown(station);
            meterFuel.SetShown(station);
            meterRods.SetShown(station);
            meterMass.SetShown(station);
            foreach (AvChip chip in stationTiles) chip.SetShown(station);
            roster.SetShown(station);
        }

        // ---- Refresh ---------------------------------------------------------------------------

        private void RefreshStationPage(OrbitalPlatform platform, double now)
        {
            if (stationHero == null) return;
            bool station = platform != null && platform.Exists;
            PaintWindowCard();
            SetStationParts(station);
            stationLog.Write(loop);
            if (!station)
            {
                stationHero.ShowEmpty("NO STATION ON ORBIT",
                    "Launch " + OrbitalPlatform.Callsign + "'s core with LAUNCH CORE; every other module docks to it, " +
                    "giving persistent coverage from a fixed sector.", "NO CONTACT");
                return;
            }

            PlatformStats stats = platform.Stats(now);
            OrbitState state = platform.State(now);
            RefreshStationHero(platform, stats, now);
            RefreshStationHealth(platform, stats, state, now);
            RefreshStationResources(platform, stats);
            RefreshRoster(platform, now);
            PaintTrend(energyChart, energyTrend, "%");
        }

        private void PaintWindowCard()
        {
            if (windowCard == null) return;
            if (support.LocalWindowOpen)
                windowCard.Set("WINDOW OPEN · CLOSES T-" + PlatformWords.Clock(support.LocalWindowChangeIn),
                    "Rod, EMP and the sweeps release.", AvState.Ready);
            else windowCard.Set("WINDOW CLOSED · OPENS T-" + PlatformWords.Clock(support.LocalWindowChangeIn),
                    "Rod, EMP and the sweeps hold. PRSM and cruise need no window.", AvState.Inert);
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

        private static string Pct(float v01, AvState st) => AvStates.Glyph(st) + Mathf.RoundToInt(Mathf.Clamp01(v01) * 100f) + "%";

        private void RefreshStationResources(OrbitalPlatform platform, in PlatformStats stats)
        {
            float charge = stats.StorageKj > 0f ? platform.Energy / stats.StorageKj : 0f;
            AvState energy = platform.Brownout ? AvState.Danger : charge < 0.25f ? AvState.Caution : AvState.Ready;
            meterEnergy.Set(charge, Pct(charge, energy), energy);
            SetMeterHelp(meterEnergy, "ENERGY: battery charge " + Mathf.RoundToInt(platform.Energy) + " / " +
                Mathf.RoundToInt(stats.StorageKj) + " kJ." + (platform.Brownout ? " BROWNOUT: demand exceeds supply." : ""));

            if (stats.FuelCapacity <= 0f)
            {
                meterFuel.Set(0f, "—", AvState.Inert);
                SetMeterHelp(meterFuel, "FUEL: no tank fitted.");
            }
            else
            {
                float fuel = platform.Fuel / stats.FuelCapacity;
                AvState st = platform.Fuel <= 0.5f ? AvState.Danger
                    : fuel < 0.25f || platform.Orbit.DragFuelPerSecond > 0f ? AvState.Caution : AvState.Ready;
                meterFuel.Set(fuel, Pct(fuel, st), st);
                SetMeterHelp(meterFuel, "FUEL: " + platform.Fuel.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " / " + stats.FuelCapacity.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " units." + (platform.Orbit.DragFuelPerSecond > 0f ? " Drag is burning fuel to hold orbit." : ""));
            }

            if (stats.RodCapacity <= 0)
            {
                meterRods.Set(0f, "—", AvState.Inert);
                SetMeterHelp(meterRods, "RODS: no launch rack fitted.");
            }
            else meterRods.Set(platform.Rods / (float)stats.RodCapacity, platform.Rods + "/" + stats.RodCapacity,
                platform.Rods == 0 ? AvState.Caution : AvState.Ready);

            if (stats.RodCapacity > 0)
                SetMeterHelp(meterRods, "RODS: " + platform.Rods + " of " + stats.RodCapacity + " kinetic rods loaded.");

            float mass = stats.Mass / OrbitalPlatform.MassLimit;
            AvState ms = mass >= 0.9f ? AvState.Caution : AvState.Info;
            meterMass.Set(mass, Pct(mass, ms), ms);
            SetMeterHelp(meterMass, "MASS: " + PlatformWords.Tonnes(stats.Mass) + " of " + PlatformWords.Tonnes(OrbitalPlatform.MassLimit) + " limit.");
        }

        private static void SetMeterHelp(AvGauge gauge, string text)
        {
            if (gauge != null && gauge.Help != text) gauge.Help = text;
        }

        /// <summary>The fitted modules by name and state, one hover tip each: what the module does.</summary>
        private void RefreshRoster(OrbitalPlatform platform, double now)
        {
            int listed = 0;
            for (int i = 0; i < OrbitalPlatform.CellCount && listed < RosterGrid.MaxCells; i++)
            {
                ModuleKind kind = platform.Cell(i);
                bool docking = kind == ModuleKind.None && platform.Pending != ModuleKind.None && platform.PendingCell == i;
                if (kind == ModuleKind.None && !docking) continue;
                ModuleInfo info = PlatformModules.Info(docking ? platform.Pending : kind);
                bool online = docking || platform.IsOnline(i, now);
                bool hot = online && !docking && platform.RunsHot(kind, now);
                AvState tone = docking ? AvState.Info : !online ? AvState.Danger : hot ? AvState.Caution : AvState.Ready;
                roster.Set(listed, info.Code, info.Name, docking ? "DOCKING" : !online ? "DOWN" : hot ? "HOT" : "ONLINE", tone);
                roster.SetHelp(listed, info.Name + " — " + info.Summary +
                    (docking ? " Docking in " + PlatformWords.Clock(platform.DockAt - now) + "."
                    : !online ? " Offline: check power and cooling." : hot ? " Running hot: add a radiator beside it." : ""));
                listed++;
            }
            roster.SetCount(listed);
        }
    }
}

using System;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPACE — the orbital platform, split the same way CYBER is.
    ///
    /// <para>STATUS is the glance: where the station is, what it is made of, what is wrong with
    /// it, and one button to the full-screen console. ACTIONS is the flying half — the map
    /// abilities the station has earned, armed in one click. Everything that <em>grows</em> the
    /// station (mission loadouts, the truss, the catalogue, launches, jettisons) lives in
    /// the task map (<see cref="Views.StationTaskingView"/>), with the station wall one step deeper because it has
    /// no business competing for attention with the aircraft.</para>
    ///
    /// <para>This file is the shell: sub-page wiring, the work that must keep running with the
    /// page closed (the radar product and the voice loop) and the state the two SPACE sub-pages
    /// share. Every figure comes from the station model; every control is a host request.</para>
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const int LoopLines = 6;
        private const string StationStatusHelp = "The station at a glance: fixed position, modules, health and the voice loop.";
        private const string StationActionsHelp = "The station's abilities and their module requirements.";

        private OpsSubPage spacePage;

        /// <summary>
        /// The voice loop, newest line first. <see cref="Views.StationView"/> and
        /// <see cref="Views.StationTaskingView"/> take this array by reference (Window.cs), so its
        /// identity and ring-buffer semantics must not change.
        /// </summary>
        private readonly string[] loop = new string[LoopLines];
        private readonly PlatformProducts products = new PlatformProducts();
        private readonly PlatformPlan plan = new PlatformPlan();
        private float nextBackground;

        private string loggedStatus;
        private bool loggedExists;
        private OrbitPhase loggedPhase;
        private PlatformHold loggedHold;
        private ModuleKind loggedPending;
        private byte loggedNotice;
        private bool loggedBrownout;
        private byte loggedRegime;

        private void ResetSpacePage()
        {
            spacePage = null;
            for (int i = 0; i < loop.Length; i++) loop[i] = null;
            loggedStatus = null;
            loggedExists = false;
            loggedPending = ModuleKind.None;
            loggedNotice = 0;
            loggedBrownout = false;
            nextBackground = 0f;
            products.Reset();
            plan.Reset();
            ResetStationPage();
            ResetSpaceOpsPage();
        }

        // ---- Build ---------------------------------------------------------------------------

        private void BuildSpacePage(AvFlow page)
        {
            spacePage = page.Add(new OpsSubPage(page.Content, page.Ticker, page.Inner, AvIcon.Satellite, "SPACE",
                sub => nextRefresh = 0f, StationStatusHelp, StationActionsHelp));
            BuildStationPage(spacePage.Status);
            BuildSpaceOpsPage(spacePage.Actions);
            Log("CONSOLE ONLINE · FLIGHT HAS THE ROOM");
        }

        private void RefreshSpace(bool bypass)
        {
            if (spacePage == null) return;
            OrbitalPlatform platform = support.LocalPlatform;
            double now = support.OrbitNow;
            if (spacePage.Sub == 0) RefreshStationPage(platform, now);
            else RefreshSpaceOpsPage(bypass, platform, now);
        }

        // ---- Full-screen instruments ---------------------------------------------------------

        /// <summary>Open the station task map; engineering remains one step deeper.</summary>
        private void OpenStationConsole()
        {
            if (FullscreenInput.AnyOpen && !Window.OpsWindow.IsOpen) return;
            OpenRoom(TaskingRoom(), null, stationConsoleButton != null ? stationConsoleButton.Rect : null);
            Log("FLIGHT · " + OrbitalPlatform.Callsign + " TASKING MAP OPEN");
        }

        /// <summary>Open the engineering wall, where the core launch and every module launch live.</summary>
        private void OpenEngineering()
        {
            if (FullscreenInput.AnyOpen && !Window.OpsWindow.IsOpen) return;
            OpenRoom(StationRoom(), null, null);
            Log("FLIGHT · " + OrbitalPlatform.Callsign + " ENGINEERING OPEN");
        }

        private void OpenUplink(GlobalPosition? aim)
        {
            if (FullscreenInput.AnyOpen && !Window.OpsWindow.IsOpen) return;
            OpenRoom(ImagerRoom(), aim.HasValue ? (object)aim.Value : null, null);
            Log("UPLINK · " + OrbitalPlatform.Callsign + " FEED ON THE MAIN SCREEN");
        }

        // ---- Background ----------------------------------------------------------------------

        /// <summary>Work that must not wait for the SPACE page to be on screen.</summary>
        private void TickSpaceBackground()
        {
            if (spacePage == null) return;
            // The SAR scene is expensive (rays, raster, texture upload), so it only forms
            // while the OPS screen or the uplink feed is actually on screen.
            if (viewOpen || ImagerOpen)
            {
                if (products.Tick(support, UnityEngine.Time.unscaledDeltaTime))
                    Log("RADAR SCAN · SCENE FORMING · " + support.RadarScanContacts + " STATIONARY CONTACT(S)");
            }

            if (UnityEngine.Time.unscaledTime < nextBackground) return;
            nextBackground = UnityEngine.Time.unscaledTime + 0.2f;
            OrbitalPlatform platform = support.LocalPlatform;
            TrackLoopEvents(platform, support.OrbitNow);
        }

        private void TrackLoopEvents(OrbitalPlatform platform, double now)
        {
            string status = support.Status;
            if (status != loggedStatus)
            {
                loggedStatus = status;
                if (status != null && (status.EndsWith("accepted.", StringComparison.Ordinal) ||
                                       status.IndexOf("denied", StringComparison.Ordinal) >= 0 ||
                                       status.IndexOf(" complete:", StringComparison.Ordinal) >= 0))
                    Log(status.ToUpperInvariant());
            }

            bool exists = platform != null && platform.Exists;
            if (exists != loggedExists)
            {
                loggedExists = exists;
                Log(exists ? "LIFTOFF CONFIRMED · " + OrbitalPlatform.Callsign + " CORE CLIMBING"
                           : OrbitalPlatform.Callsign + " OFF THE BOARD");
                if (exists)
                {
                    loggedNotice = platform.NoticeSerial;
                    loggedPending = platform.Pending;
                    loggedHold = platform.HoldAt(now);
                    loggedRegime = platform.Regime;
                    loggedPhase = platform.State(now).Phase;
                }
                return;
            }
            if (!exists) return;

            if (platform.NoticeSerial != loggedNotice)
            {
                loggedNotice = platform.NoticeSerial;
                string module = PlatformModules.Info(platform.Cell(platform.NoticeCell)).Code + " " +
                                OrbitalPlatform.CellName(platform.NoticeCell);
                switch (platform.Notice)
                {
                    case PlatformNotice.DebrisHit: Log("MMOD STRIKE · " + module + " OFFLINE"); break;
                    case PlatformNotice.DebrisDeflected: Log("MMOD STRIKE · " + module + " SHIELD HELD"); break;
                    case PlatformNotice.SafeMode: Log("OUT OF FUEL AT LOW · SAFE MODE CLIMB TO MID"); break;
                    case PlatformNotice.Docked:
                        Log(platform.NoticeCell == OrbitalPlatform.CoreCell && loggedPending == ModuleKind.Cargo
                            ? "CARGO DOCKED · TANKS AND MAGAZINES FULL"
                            : "HARD DOCK · " + module);
                        break;
                }
            }
            if (platform.Pending != loggedPending)
            {
                if (platform.Pending != ModuleKind.None)
                    Log("LIFTOFF CONFIRMED · " + PlatformModules.Info(platform.Pending).Code + " DOCKING IN " +
                        PlatformWords.Clock(platform.DockAt - now));
                loggedPending = platform.Pending;
            }
            if (platform.Brownout != loggedBrownout)
            {
                loggedBrownout = platform.Brownout;
                Log(platform.Brownout ? "BROWNOUT · LOADS SHED" : "POWER RESTORED · LOADS BACK ON");
            }

            PlatformHold hold = platform.HoldAt(now);
            OrbitPhase phase = platform.State(now).Phase;
            if (hold != loggedHold || platform.Regime != loggedRegime)
            {
                if (hold == PlatformHold.None && loggedHold == PlatformHold.Insertion)
                    Log("ORBIT INSERTION CONFIRMED · " + platform.Orbit.Name);
                else if (hold == PlatformHold.None && loggedHold != PlatformHold.None)
                    Log("ON STATION · " + StationKeeping.Name(platform.PositionIndex));
                else if (hold == PlatformHold.Transfer) Log("TRANSFER BURN · " + platform.Orbit.Name);
                else if (hold == PlatformHold.Rephase) Log("RELOCATING · " + StationKeeping.Name(platform.PositionIndex));
                loggedHold = hold;
                loggedRegime = platform.Regime;
            }
            if (phase != loggedPhase)
            {
                if (phase == OrbitPhase.InPass)
                    Log("UPLINK ESTABLISHED · " + StationKeeping.Name(platform.PositionIndex));
                else if (loggedPhase == OrbitPhase.InPass)
                    Log("UPLINK PAUSED · PLATFORM MANEUVER");
                loggedPhase = phase;
            }
        }

        private void Log(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            OrbitalPlatform platform = support != null ? support.LocalPlatform : null;
            double elapsed = platform != null && platform.Exists ? platform.Elapsed(support.OrbitNow) : -1.0;
            for (int i = loop.Length - 1; i > 0; i--) loop[i] = loop[i - 1];
            loop[0] = TheaterGrid.Elapsed(elapsed) + "  " + line;
        }
    }
}

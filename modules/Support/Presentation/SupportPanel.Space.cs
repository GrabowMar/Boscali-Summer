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
    /// <para>STATUS is the glance: where the station is, what it is made of and what is wrong
    /// with it. ACTIONS is the flying half — the map abilities the station has earned, armed
    /// in one click. PAW S1 deleted the full-screen rooms: everything that <em>grows</em> the
    /// station (loadouts, launches, relocation) returns as Tier-2 MFD tabs in S2; core launch
    /// is a direct host request from the LAUNCH CORE button.</para>
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
        /// The voice loop, newest line first. Ring-buffer semantics: the newest line is always
        /// at index 0 and older lines shift down.
        /// </summary>
        private readonly string[] loop = new string[LoopLines];
        private readonly PlatformProducts products = new PlatformProducts();
        private readonly PlatformPlan plan = new PlatformPlan();

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
            Log("SPACE ONLINE · " + OrbitalPlatform.Callsign + " LINK READY");
        }

        private void RefreshSpace(bool bypass)
        {
            OrbitalPlatform platform = support.LocalPlatform;
            double now = support.OrbitNow;
            if (spacePage.Sub == 0) RefreshStationPage(platform, now);
            else RefreshSpaceOpsPage(bypass, platform, now);
        }

        /// <summary>Work that must not wait for the SPACE page: the sensor product and the loop.</summary>
        private void TickSpaceBackground()
        {
            if (spacePage == null) return;
            OrbitalPlatform platform = support.LocalPlatform;
            double now = support.OrbitNow;
            TrackLoopEvents(platform, now);
            // The product forms while the OPS screen is actually on screen.
            if (viewOpen)
            {
                if (products.Tick(support, UnityEngine.Time.unscaledDeltaTime))
                    Log("RADAR SCAN · SCENE FORMING · " + support.RadarScanContacts + " STATIONARY CONTACT(S)");
            }
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

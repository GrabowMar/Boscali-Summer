using System;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPACE — the orbital platform, split the same way CYBER is.
    ///
    /// <para>STATUS is the glance: where the station is, what it is made of, what is wrong with
    /// it, and one button to the full-screen console. ACTIONS is the flying half — the map
    /// abilities the station has earned, armed in one click. Everything that <em>grows</em> the
    /// station (mission loadouts, the truss, the catalogue, launches, jettisons) lives in
    /// the station wall (<see cref="Views.StationView"/>), because that is the demanding half of the loop and it has
    /// no business competing for attention with the aircraft.</para>
    ///
    /// <para>This file is the shell: sub-tabs, the work that must keep running with the page
    /// closed (the radar product and the voice loop) and the widgets the pages share. Every
    /// figure comes from the station model; every control is a host request.</para>
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const int SubStatus = 0;
        private const int SubActions = 1;
        private const int LoopLines = 6;
        private const float TileHeight = 46f;

        private static readonly string[] SubLabels = { "STATUS", "ACTIONS" };

        /// <summary>Mobility's tint, taken from the live theme so a wash and its rail can never disagree.</summary>
        private static Color MobilityColour => AvTheme.Warning;

        private readonly GameObject[] spaceSubPages = new GameObject[2];
        private int spaceSub;

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
            for (int i = 0; i < spaceSubPages.Length; i++) spaceSubPages[i] = null;
            for (int i = 0; i < loop.Length; i++) loop[i] = null;
            spaceSub = SubStatus;
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

        private void BuildSpacePage()
        {
            var page = (RectTransform)shell.CreatePage(TabSpace, "SpacePage").transform;
            // Each sub-page carries its own title row with the STATUS / ACTIONS toggle (M1).
            Rect subBody = shell.Body;
            for (int i = 0; i < spaceSubPages.Length; i++)
            {
                var go = new GameObject("Space" + i, typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(page, false);
                AvKit.Stretch(rect);
                spaceSubPages[i] = go;
            }

            BuildStationPage((RectTransform)spaceSubPages[SubStatus].transform, subBody);
            BuildSpaceOpsPage((RectTransform)spaceSubPages[SubActions].transform, subBody);
            SelectSpaceSub(SubStatus);
            Log("CONSOLE ONLINE · FLIGHT HAS THE ROOM");
        }

        private static RectTransform BeginSub(RectTransform root, Rect body, float contentHeight,
                                              out float x, out float y, out float width)
        {
            RectTransform parent = AvScreen.Scroll(root, body, contentHeight, out Rect area);
            x = area.x + 4f;
            y = area.y;
            width = area.width - 8f;
            return parent;
        }

        private void SelectSpaceSub(int sub)
        {
            spaceSub = Mathf.Clamp(sub, 0, spaceSubPages.Length - 1);
            AvButton.ClearTooltip();
            for (int i = 0; i < spaceSubPages.Length; i++)
                if (spaceSubPages[i] != null) spaceSubPages[i].SetActive(i == spaceSub);
            nextRefresh = 0f;
        }

        private void RefreshSpace(bool bypass)
        {
            if (spaceSubPages[SubStatus] == null) return;
            OrbitalPlatform platform = support.LocalPlatform;
            double now = support.OrbitNow;
            if (spaceSub == SubStatus) RefreshStationPage(platform, now);
            else RefreshSpaceOpsPage(bypass, platform, now);
        }

        // ---- Full-screen instruments ---------------------------------------------------------

        /// <summary>The station wall: mission loadouts, the blueprint and every launch.</summary>
        private void OpenStationConsole()
        {
            if (FullscreenInput.AnyOpen && !Window.OpsWindow.IsOpen) return;
            OpenRoom(StationRoom(), null, consoleButton);
            Log("FLIGHT · " + OrbitalPlatform.Callsign + " ON THE BIG BOARD");
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
            if (spaceSubPages[SubStatus] == null) return;
            // The SAR scene is expensive (rays, raster, texture upload), so it only forms
            // while the OPS screen or the uplink feed is actually on screen.
            if (viewOpen || ImagerOpen)
            {
                if (products.Tick(support, Time.unscaledDeltaTime))
                    Log("RADAR SCAN · SCENE FORMING · " + support.RadarScanContacts + " STATIONARY CONTACT(S)");
            }

            if (Time.unscaledTime < nextBackground) return;
            nextBackground = Time.unscaledTime + 0.2f;
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

        private static void WriteLoop(TMP_Text[] labels, string[] lines)
        {
            for (int i = 0; i < labels.Length && i < lines.Length; i++)
                if (labels[i] != null) labels[i].text = lines[i] ?? "";
        }

        // ---- Shared widgets --------------------------------------------------------------------

        /// <summary>Module category tint.</summary>
        private static Color CategoryColour(ModuleCategory category)
        {
            switch (category)
            {
                case ModuleCategory.Power: return AvTheme.RailCaution;
                case ModuleCategory.Utility: return AvTheme.RailInfo;
                case ModuleCategory.Sensor: return AvTheme.RailReady;
                case ModuleCategory.Weapon: return AvTheme.RailDanger;
                case ModuleCategory.Mobility: return AvTheme.Warning;
                default: return AvTheme.TextPrimary;
            }
        }

        /// <summary>A flight-deck annunciator: small channel key, large written state, semantic rail.</summary>
        private sealed class Tile
        {
            public Image Fill;
            public Image[] Frame;
            public TMP_Text Value;
            public Tone LastTone = (Tone)255;
            public string LastValue;
        }

        private static Tile BuildTile(RectTransform parent, Rect area, string key)
        {
            var tile = new Tile
            {
                Fill = AvKit.Panel(parent, area, AvTheme.SurfaceInert),
                Frame = new[]
                {
                    AvKit.Rule(parent, new Rect(area.x, area.y, 2f, area.height), AvTheme.RailInert),
                    AvKit.Rule(parent, new Rect(area.x + 2f, area.y, area.width - 2f, 1f), AvTheme.Hairline)
                }
            };
            AvKit.Label(parent, key, new Rect(area.x + 9f, area.y - 5f, area.width - 18f, 13f), AvTheme.Dim,
                AvTokens.FontMicro, FontStyles.Bold).characterSpacing = 0.6f;
            tile.Value = AvKit.Label(parent, "", new Rect(area.x + 9f, area.y - 21f, area.width - 18f, 21f),
                AvTheme.TextPrimary, AvTokens.FontLead, FontStyles.Bold);
            // Four tiles share 480 px; a long reading shrinks toward the 10 px floor rather than clipping.
            tile.Value.enableAutoSizing = true;
            tile.Value.fontSizeMin = AvTokens.FontMicro;
            tile.Value.fontSizeMax = AvTokens.FontLead;
            return tile;
        }

        private static void PaintTile(Tile tile, string value, Tone tone)
        {
            if (tile == null || (tile.LastTone == tone && tile.LastValue == value)) return;
            tile.LastTone = tone;
            tile.LastValue = value;
            Color colour = tone == Tone.Locked ? AvTheme.Disabled : StatusColor(tone);
            tile.Value.text = value;
            tile.Value.color = tone == Tone.Locked ? AvTheme.Dim : colour;
            tile.Fill.color = tone == Tone.Danger ? colour.WithAlpha(0.08f) : AvTheme.SurfaceInert;
            tile.Frame[0].color = colour;
            tile.Frame[1].color = tone == Tone.Locked ? AvTheme.Hairline : colour.WithAlpha(0.5f);
        }
    }
}

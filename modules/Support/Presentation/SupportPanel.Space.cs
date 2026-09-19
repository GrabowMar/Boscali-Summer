using System;
using System.Globalization;
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
    /// SPACE — the station's mission operations console. PLATFORM flies the station (status,
    /// resources, schematic, abilities, voice loop), MISSION PLANNER builds it (grid, catalogue,
    /// launches) and ENEMY ACTIVITY watches the other side's stations. This file is the shell:
    /// sub-tabs, the work that must keep running with the page closed (launch count, radar
    /// product, voice loop) and the widgets the three pages share. Every figure comes from the
    /// station model; every control is a host request.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const int SubPlatform = 0;
        private const int SubPlanner = 1;
        private const int SubEnemy = 2;
        private const float SubNavHeight = 30f;
        private const int LoopLines = 6;
        private const float LoopPitch = 30f;
        private const float CountdownSeconds = 5f;
        private const float ConfirmSeconds = 3f;

        private static readonly string[] SubLabels = { "PLATFORM", "MISSION PLANNER", "ENEMY ACTIVITY" };
        private static readonly string[] SubTips =
        {
            "Station status, resources, truss and abilities.",
            "Design the station, launch modules and cargo.",
            "Hostile stations the mirror tracks, and the counterspace desk."
        };
        /// <summary>Mobility's tint, taken from the live theme so a wash and its rail can never disagree.</summary>
        private static Color MobilityColour => AvTheme.Warning;

        private readonly GameObject[] spaceSubPages = new GameObject[3];
        private readonly AvButton[] spaceSubTabs = new AvButton[3];
        private int spaceSub;

        private readonly string[] loop = new string[LoopLines];
        private readonly PlatformProducts products = new PlatformProducts();
        private PlatformUplink uplink;
        private float nextBackground;

        private enum LaunchKind : byte
        {
            None,
            Core,
            Module,
            Cargo
        }

        private LaunchKind countdownKind;
        private ModuleKind countdownModule;
        private int countdownCell;
        private byte countdownBand;
        private float countdownEnd = -1f;
        private int countdownShown = -1;

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
            for (int i = 0; i < spaceSubPages.Length; i++)
            {
                spaceSubPages[i] = null;
                spaceSubTabs[i] = null;
            }
            for (int i = 0; i < loop.Length; i++) loop[i] = null;
            spaceSub = SubPlatform;
            countdownKind = LaunchKind.None;
            countdownEnd = -1f;
            countdownShown = -1;
            loggedStatus = null;
            loggedExists = false;
            loggedPending = ModuleKind.None;
            loggedNotice = 0;
            loggedBrownout = false;
            nextBackground = 0f;
            products.Reset();
            if (uplink != null) Destroy(uplink.gameObject);
            uplink = null;
            ResetPlatformPage();
            ResetPlannerPage();
            ResetEnemyPage();
        }

        // ---- Build ---------------------------------------------------------------------------

        private void BuildSpacePage()
        {
            var page = (RectTransform)shell.CreatePage(TabSpace, "SpacePage").transform;
            Rect body = shell.Body;
            float segment = (body.width - 8f) / SubLabels.Length;
            for (int i = 0; i < SubLabels.Length; i++)
            {
                int sub = i;
                spaceSubTabs[i] = AvStyled.Button(page, new Rect(body.x + i * (segment + 4f), body.y, segment, SubNavHeight),
                    SubLabels[i], "tab", () => SelectSpaceSub(sub), AvButtonStyle.Tab)
                    .WithTooltip(SubTips[i]);
            }

            var subBody = new Rect(body.x, body.y - SubNavHeight - 8f, body.width, body.height - SubNavHeight - 8f);
            for (int i = 0; i < spaceSubPages.Length; i++)
            {
                var go = new GameObject("Space" + i, typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(page, false);
                AvKit.Stretch(rect);
                spaceSubPages[i] = go;
            }

            BuildPlatformPage((RectTransform)spaceSubPages[SubPlatform].transform, subBody);
            BuildPlannerPage((RectTransform)spaceSubPages[SubPlanner].transform, subBody);
            BuildEnemyPage((RectTransform)spaceSubPages[SubEnemy].transform, subBody);
            SelectSpaceSub(SubPlatform);
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
            {
                if (spaceSubPages[i] != null) spaceSubPages[i].SetActive(i == spaceSub);
                if (spaceSubTabs[i] != null) spaceSubTabs[i].SetLatched(i == spaceSub);
            }
            nextRefresh = 0f;
        }

        private void RefreshSpace(bool bypass)
        {
            if (spaceSubPages[SubPlatform] == null) return;
            OrbitalPlatform platform = support.LocalPlatform;
            double now = support.OrbitNow;
            OrbitClock clock = support.OrbitClock;
            switch (spaceSub)
            {
                case SubPlatform: RefreshPlatformPage(bypass, platform, now, clock); break;
                case SubPlanner: RefreshPlannerPage(bypass, platform, now, clock); break;
                default: RefreshEnemyPage(now, clock); break;
            }
        }

        // ---- Uplink ------------------------------------------------------------------------------

        private void OpenUplink(GlobalPosition? aim)
        {
            if (uplink == null) uplink = PlatformUplink.Create(support, products);
            uplink.Show(aim);
            Log("UPLINK · " + OrbitalPlatform.Callsign + " FEED ON THE MAIN SCREEN");
        }

        // ---- Background ----------------------------------------------------------------------------

        /// <summary>Work that must not wait for the SPACE page to be on screen.</summary>
        private void TickSpaceBackground()
        {
            if (spaceSubPages[SubPlatform] == null) return;
            if (products.Tick(support, Time.unscaledDeltaTime))
                Log("RADAR SCAN · SCENE FORMING · " + support.RadarScanContacts + " STATIONARY CONTACT(S)");

            if (Time.unscaledTime < nextBackground) return;
            nextBackground = Time.unscaledTime + 0.2f;
            OrbitalPlatform platform = support.LocalPlatform;
            RunCountdown(support.BypassRequirements, platform);
            TrackLoopEvents(platform, support.OrbitNow, support.OrbitClock);
        }

        private void StartCountdown(LaunchKind kind, ModuleKind module, int cell, byte band)
        {
            countdownKind = kind;
            countdownModule = module;
            countdownCell = cell;
            countdownBand = band;
            countdownEnd = Time.unscaledTime + CountdownSeconds;
            countdownShown = -1;
            Log("FLIGHT · " + CountdownName() + " · POLL THE ROOM");
            nextRefresh = 0f;
        }

        private void HoldCountdown()
        {
            if (countdownEnd <= 0f) return;
            countdownEnd = -1f;
            countdownShown = -1;
            countdownKind = LaunchKind.None;
            Log("FLIGHT · HOLD HOLD HOLD · COUNT RECYCLED");
            nextRefresh = 0f;
        }

        private string CountdownName()
        {
            switch (countdownKind)
            {
                case LaunchKind.Core: return "CORE TO " + OrbitRegimes.Get(countdownBand).Code;
                case LaunchKind.Cargo: return "CARGO RESUPPLY";
                default: return PlatformModules.Info(countdownModule).Code + " TO " + OrbitalPlatform.CellName(countdownCell);
            }
        }

        private float CountdownRemaining => countdownEnd > 0f ? Mathf.Max(0f, countdownEnd - Time.unscaledTime) : 0f;

        private void RunCountdown(bool bypass, OrbitalPlatform platform)
        {
            if (countdownEnd <= 0f) return;
            int left = Mathf.Max(0, Mathf.CeilToInt(countdownEnd - Time.unscaledTime));
            if (left != countdownShown)
            {
                countdownShown = left;
                if (left > 0 && left < CountdownSeconds) Log("T-" + left.ToString("00", Invariant) + " · " + PollLine(left));
            }
            if (Time.unscaledTime < countdownEnd) return;

            LaunchKind kind = countdownKind;
            string name = CountdownName();
            countdownEnd = -1f;
            countdownShown = -1;
            countdownKind = LaunchKind.None;

            ModuleKind costed = kind == LaunchKind.Core ? ModuleKind.Core
                : kind == LaunchKind.Cargo ? ModuleKind.Cargo : countdownModule;
            bool affordable = bypass || support.LocalAllocation + 0.001f >= support.LaunchCost(costed);
            double now = support.OrbitNow;
            bool valid = kind == LaunchKind.Cargo
                ? platform != null && platform.Exists && platform.Pending == ModuleKind.None
                : platform != null && platform.CheckPlacement(costed, countdownCell, countdownBand, now) == PlacementFailure.None;
            if (!affordable || !valid || support.CommandPending)
            {
                Log("FLIGHT · NO-GO AT T-0 · SCRUB");
                return;
            }

            Log("LIFTOFF · " + name);
            switch (kind)
            {
                case LaunchKind.Core: support.RequestCoreLaunch(countdownBand); break;
                case LaunchKind.Cargo: support.RequestResupply(); break;
                default: support.RequestModuleLaunch(countdownModule, countdownCell); break;
            }
        }

        private static string PollLine(int secondsLeft)
        {
            switch (secondsLeft)
            {
                case 4: return "RANGE GO · WEATHER GO · GUIDANCE GO";
                case 3: return "FLIGHT: GO FOR LAUNCH";
                case 2: return "IGNITION SEQUENCE START";
                case 1: return "MAIN ENGINE START";
                default: return "LAUNCH DIRECTOR POLLING THE ROOM";
            }
        }

        private void TrackLoopEvents(OrbitalPlatform platform, double now, in OrbitClock clock)
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
                Log(exists ? "LIFTOFF CONFIRMED · " + OrbitalPlatform.Callsign + " CORE CLIMBING" : OrbitalPlatform.Callsign + " OFF THE BOARD");
                if (exists)
                {
                    loggedNotice = platform.NoticeSerial;
                    loggedPending = platform.Pending;
                    loggedHold = platform.HoldAt(now);
                    loggedRegime = platform.Regime;
                    loggedPhase = platform.State(now, clock).Phase;
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
            OrbitPhase phase = platform.State(now, clock).Phase;
            if (hold != loggedHold || platform.Regime != loggedRegime)
            {
                if (hold == PlatformHold.None && loggedHold == PlatformHold.Insertion) Log("ORBIT INSERTION CONFIRMED · " + platform.Orbit.Name);
                else if (hold == PlatformHold.None && loggedHold != PlatformHold.None) Log("BURN COMPLETE · " + platform.Orbit.Name);
                else if (hold == PlatformHold.Transfer) Log("TRANSFER BURN · " + platform.Orbit.Name);
                else if (hold == PlatformHold.Rephase) Log("PHASING BURN · NEXT PASS IN SECONDS");
                loggedHold = hold;
                loggedRegime = platform.Regime;
            }
            if (phase != loggedPhase)
            {
                if (phase == OrbitPhase.InPass) Log("AOS · " + OrbitalPlatform.Callsign + " OVERHEAD · " + platform.Orbit.Code + " PASS");
                else if (loggedPhase == OrbitPhase.InPass) Log("LOS · " + OrbitalPlatform.Callsign + " OVER THE HORIZON");
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

        private TMP_Text[] BuildLoop(RectTransform parent, float x, ref float y, float width)
        {
            Header(parent, x, ref y, width, "VOICE LOOP", "FLIGHT DIRECTOR");
            var labels = new TMP_Text[LoopLines];
            for (int i = 0; i < LoopLines; i++)
            {
                labels[i] = Wrapped(AvStyled.Label(parent, new Rect(x, y - i * LoopPitch, width, 28f), "", "row-sub"));
                labels[i].color = i == 0 ? AvTheme.TextPrimary : AvTheme.Dim;
            }
            y -= LoopLines * LoopPitch;
            return labels;
        }

        /// <summary>Support-action gate for rows outside SPACE; orbital actions live on PLATFORM cards.</summary>
        private string OrbitalGate(SupportActionDefinition action, out bool open)
        {
            PlatformAbility? ability = SupportManager.OrbitalAbility(action.Id);
            if (!ability.HasValue)
            {
                open = true;
                return null;
            }
            PlatformDenial denial = support.PlatformCheck(ability.Value);
            open = denial == PlatformDenial.None;
            return PlatformWords.Denial(denial, support.LocalPlatform, ability.Value, support.OrbitNow, support.OrbitClock);
        }

        // ---- Shared widgets --------------------------------------------------------------------------

        private static Color CategoryColour(ModuleCategory category)
        {
            switch (category)
            {
                case ModuleCategory.Power: return AvTheme.RailCaution;
                case ModuleCategory.Utility: return AvTheme.RailInfo;
                case ModuleCategory.Sensor: return AvTheme.RailReady;
                case ModuleCategory.Weapon: return AvTheme.RailDanger;
                case ModuleCategory.Mobility: return MobilityColour;
                default: return AvTheme.TextPrimary;
            }
        }

        private static Color ToneColour(Tone tone) => tone == Tone.Locked ? AvTheme.Dim : StatusColor(tone);

        /// <summary>A lit annunciator: key on top, state word below, fill and border tinted by tone.</summary>
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
                Fill = AvKit.Panel(parent, area, Color.clear),
                Frame = AvKit.Outline(parent, area, AvTheme.Hairline)
            };
            AvKit.Label(parent, key, new Rect(area.x + 8f, area.y - 6f, area.width - 16f, 14f), AvTheme.Dim,
                AvTokens.FontSmall, FontStyles.Normal);
            tile.Value = AvKit.Label(parent, "", new Rect(area.x + 8f, area.y - 24f, area.width - 16f, 16f),
                AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold);
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
            tile.Fill.color = tone == Tone.Danger ? colour.WithAlpha(0.08f) : AvTheme.Surface;
            for (int i = 0; i < tile.Frame.Length; i++) tile.Frame[i].color = AvTheme.Hairline;
        }

        /// <summary>A labelled bar: key left, reading right, a bar under both.</summary>
        private sealed class Gauge
        {
            public TMP_Text Reading;
            public Image Fill;
            public TMP_Text Note;
        }

        private static Gauge BuildGauge(RectTransform parent, float x, float y, float width, string key, Color fill)
        {
            AvStyled.Label(parent, new Rect(x, y, width, 14f), key, "kv-key");
            var gauge = new Gauge
            {
                Reading = AvStyled.Label(parent, new Rect(x, y - 18f, width, 18f), "—", "kv-value"),
                Fill = AvKit.ProgressBar(parent, new Rect(x, y - 39f, width, 4f), 0f, fill)
            };
            gauge.Note = SingleLine(AvStyled.Label(parent, new Rect(x, y - 47f, width, 14f), "", "row-sub"));
            gauge.Note.color = AvTheme.Dim;
            return gauge;
        }

        private const int CellCount = OrbitalPlatform.CellCount;

        /// <summary>The station truss drawn cell by cell, with connectors between docked neighbours.</summary>
        private sealed class GridView
        {
            public readonly Image[] Fill = new Image[CellCount];
            public readonly Image[][] Frame = new Image[CellCount][];
            public readonly TMP_Text[] Code = new TMP_Text[CellCount];
            public readonly TMP_Text[] Tag = new TMP_Text[CellCount];
            public readonly Image[] Across = new Image[OrbitalPlatform.Rows * (OrbitalPlatform.Columns - 1)];
            public readonly Image[] Down = new Image[(OrbitalPlatform.Rows - 1) * OrbitalPlatform.Columns];
            public readonly string[] LastCode = new string[CellCount];
            public readonly string[] LastTag = new string[CellCount];
        }

        private static GridView BuildGrid(RectTransform parent, float x, float y, float width, float cellHeight,
                                          bool large, Action<int> onCell)
        {
            const float gap = 6f;
            float cellWidth = (width - gap * (OrbitalPlatform.Columns - 1)) / OrbitalPlatform.Columns;
            var view = new GridView();
            for (int cell = 0; cell < CellCount; cell++)
            {
                float cx = x + OrbitalPlatform.Column(cell) * (cellWidth + gap);
                float cy = y - OrbitalPlatform.Row(cell) * (cellHeight + gap);
                var area = new Rect(cx, cy, cellWidth, cellHeight);
                view.Fill[cell] = AvKit.Panel(parent, area, Color.clear);
                view.Frame[cell] = AvKit.Outline(parent, area, AvTheme.Hairline);
                view.Code[cell] = AvKit.Label(parent, "", new Rect(cx, cy - (large ? 8f : 2f), cellWidth, large ? 20f : 14f),
                    AvTheme.TextPrimary, large ? AvTokens.FontTitle : AvTokens.FontSmall, FontStyles.Bold,
                    TextAlignmentOptions.Center);
                view.Tag[cell] = AvKit.Label(parent, "", new Rect(cx + 2f, cy - (large ? 32f : 14f), cellWidth - 4f, large ? 16f : 11f),
                    AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
                if (onCell != null)
                {
                    int index = cell;
                    AvKit.HitButton(parent, area, () => onCell(index))
                        .WithTooltip(OrbitalPlatform.CellName(cell) + " · select this cell for the inspector.");
                }
            }

            for (int row = 0; row < OrbitalPlatform.Rows; row++)
            {
                for (int column = 0; column < OrbitalPlatform.Columns - 1; column++)
                {
                    float cx = x + (column + 1) * (cellWidth + gap) - gap;
                    float cy = y - row * (cellHeight + gap) - cellHeight * 0.5f + 2f;
                    view.Across[row * (OrbitalPlatform.Columns - 1) + column] =
                        AvKit.Rule(parent, new Rect(cx, cy, gap, 4f), Color.clear);
                }
            }
            for (int row = 0; row < OrbitalPlatform.Rows - 1; row++)
            {
                for (int column = 0; column < OrbitalPlatform.Columns; column++)
                {
                    float cx = x + column * (cellWidth + gap) + cellWidth * 0.5f - 2f;
                    float cy = y - (row + 1) * (cellHeight + gap) + gap;
                    view.Down[row * OrbitalPlatform.Columns + column] =
                        AvKit.Rule(parent, new Rect(cx, cy, 4f, gap), Color.clear);
                }
            }
            return view;
        }

        /// <summary>Paint every cell from the station. <paramref name="selected"/> outlines one cell white.</summary>
        private static void PaintGrid(GridView view, OrbitalPlatform platform, double now, int selected, bool large)
        {
            bool station = platform != null && platform.Exists;
            for (int cell = 0; cell < CellCount; cell++)
            {
                ModuleKind kind = station ? platform.Cell(cell) : ModuleKind.None;
                bool pending = station && platform.Pending != ModuleKind.None && platform.Pending != ModuleKind.Cargo &&
                               platform.PendingCell == cell;
                string code, tag;
                Color border, fill, text;
                if (kind != ModuleKind.None)
                {
                    ModuleInfo info = PlatformModules.Info(kind);
                    Color colour = CategoryColour(info.Category);
                    bool online = platform.IsOnline(cell, now);
                    code = info.Code;
                    tag = CellTag(platform, cell, kind, now, large);
                    border = online ? colour.WithAlpha(0.85f) : AvTheme.RailDanger;
                    fill = online ? colour.WithAlpha(0.16f) : AvTheme.RailDanger.WithAlpha(0.12f);
                    text = online ? AvTheme.TextPrimary : AvTheme.RailDanger;
                }
                else if (pending)
                {
                    code = PlatformModules.Info(platform.Pending).Code;
                    tag = "DOCK " + PlatformWords.Clock(platform.DockAt - now);
                    border = AvTheme.RailInfo;
                    fill = AvTheme.RailInfo.WithAlpha(0.08f + 0.06f * Mathf.PingPong(Time.unscaledTime * 2f, 1f));
                    text = AvTheme.RailInfo;
                }
                else if (station && platform.CanAttach(cell))
                {
                    code = "+";
                    tag = large ? OrbitalPlatform.CellName(cell) + " · FREE" : "";
                    border = AvTheme.Hairline.WithAlpha(0.9f);
                    fill = Color.clear;
                    text = AvTheme.Dim;
                }
                else
                {
                    code = "";
                    tag = large ? OrbitalPlatform.CellName(cell) : "";
                    border = AvTheme.Hairline.WithAlpha(0.25f);
                    fill = Color.clear;
                    text = AvTheme.Disabled;
                }

                if (cell == selected)
                {
                    border = Color.white;
                    fill = fill.a > 0f ? fill.WithAlpha(fill.a + 0.08f) : AvTheme.Accent.WithAlpha(0.08f);
                }

                view.Fill[cell].color = fill;
                for (int i = 0; i < view.Frame[cell].Length; i++) view.Frame[cell][i].color = border;
                if (view.LastCode[cell] != code) view.Code[cell].text = view.LastCode[cell] = code;
                if (view.LastTag[cell] != tag) view.Tag[cell].text = view.LastTag[cell] = tag;
                view.Code[cell].color = text;
                view.Tag[cell].color = kind != ModuleKind.None && !platform.IsOnline(cell, now) ? AvTheme.RailDanger : AvTheme.Dim;
            }

            for (int row = 0; row < OrbitalPlatform.Rows; row++)
            {
                for (int column = 0; column < OrbitalPlatform.Columns - 1; column++)
                {
                    int a = row * OrbitalPlatform.Columns + column;
                    bool linked = station && platform.Cell(a) != ModuleKind.None && platform.Cell(a + 1) != ModuleKind.None;
                    view.Across[row * (OrbitalPlatform.Columns - 1) + column].color = linked ? AvTheme.Frame : Color.clear;
                }
            }
            for (int row = 0; row < OrbitalPlatform.Rows - 1; row++)
            {
                for (int column = 0; column < OrbitalPlatform.Columns; column++)
                {
                    int a = row * OrbitalPlatform.Columns + column;
                    bool linked = station && platform.Cell(a) != ModuleKind.None &&
                                  platform.Cell(a + OrbitalPlatform.Columns) != ModuleKind.None;
                    view.Down[a].color = linked ? AvTheme.Frame : Color.clear;
                }
            }
        }

        /// <summary>The one-word condition a cell shows: outage, heat, relay boost, shielding.</summary>
        private static string CellTag(OrbitalPlatform platform, int cell, ModuleKind kind, double now, bool large)
        {
            if (!platform.IsOnline(cell, now)) return "OFF " + PlatformWords.Clock(platform.OfflineRemaining(cell, now));
            ModuleInfo info = PlatformModules.Info(kind);
            if (info.Hot && !platform.IsCooled(cell, now)) return "HOT";
            if ((kind == ModuleKind.Imager || kind == ModuleKind.Sigint) && platform.IsBoosted(cell, now)) return "+REL";
            if (info.Hot) return "COOLED";
            if (large && platform.IsShielded(cell) && kind != ModuleKind.Shield) return "SHD";
            return large ? OrbitalPlatform.CellName(cell) : "";
        }

        private static string Degrees(double radians) =>
            (radians / OrbitMath.Deg).ToString("0.0", CultureInfo.InvariantCulture) + "°";
    }
}

using System.Text;
using BoscaliSummer.Features.Support.Domain.Orbital;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPACE › MISSION PLANNER — build the station. The truss grid is the design surface: pick a
    /// cell, pick a module, read the projected mass and power, and launch it through a short
    /// GO/NO-GO count; it docks where you put it. The inspector explains a cell's utilities and
    /// jettisons (click twice). With no station the launch card becomes step one: pick a band
    /// and launch the core. Cargo resupply refills tanks and magazines.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float PlannerCellHeight = 50f;
        private const float InspectorHeight = 72f;
        private const float LaunchCardHeight = 112f;
        private const float CatalogueTileHeight = 44f;
        private const int RuleLines = 4;

        private static readonly string[] Rules =
        {
            "DOCK NEXT TO THE STATION · ONE LAUNCH AT A TIME · 40 T STRUCTURE",
            "RAD COOLS NEIGHBOURS · EMP AND RTG RUN DEGRADED WITHOUT ONE",
            "REL BOOSTS NEIGHBOURING IMG / SIG · SHD SHIELDS ITSELF AND NEIGHBOURS",
            "A CORE LAUNCHES TO MID OR HIGH · LOW NEEDS PRP AND BURNS DRAG FUEL"
        };

        private sealed class CatalogueTile
        {
            public ModuleKind Kind;
            public Image Fill;
            public TMP_Text State;
            public string LastState;
            public Tone LastTone = (Tone)255;
        }

        private GridView plannerGrid;
        private TMP_Text plannerNote;
        private TMP_Text inspectorTitle, inspectorSummary, inspectorEffects;
        private AvButton jettisonButton;
        private TMP_Text launchTitle, launchPrice, launchLine, launchProjection, launchStatus;
        private AvButton launchButton;
        private readonly AvButton[] bandButtons = new AvButton[OrbitRegimes.All.Length];
        private Image launchProgress;
        private readonly CatalogueTile[] catalogue = new CatalogueTile[PlatformModules.Designs.Length - 1];
        private OpsRow cargoRow;
        private int plannerCell = -1;
        private ModuleKind plannerModule = ModuleKind.None;
        private byte coreBand = OrbitRegimes.Mid;
        private int jettisonArmedCell = -1;
        private float jettisonArmedUntil;
        private readonly StringBuilder effects = new StringBuilder(96);
        private static readonly int[] HintScratch = new int[4];

        private void ResetPlannerPage()
        {
            plannerGrid = null;
            plannerNote = inspectorTitle = inspectorSummary = inspectorEffects = null;
            jettisonButton = launchButton = null;
            launchTitle = launchPrice = launchLine = launchProjection = launchStatus = null;
            launchProgress = null;
            for (int i = 0; i < bandButtons.Length; i++) bandButtons[i] = null;
            for (int i = 0; i < catalogue.Length; i++) catalogue[i] = null;
            cargoRow = null;
            plannerCell = -1;
            plannerModule = ModuleKind.None;
            coreBand = OrbitRegimes.Mid;
            jettisonArmedCell = -1;
        }

        private void BuildPlannerPage(RectTransform root, Rect body)
        {
            int tileRows = (catalogue.Length + 1) / 2;
            float gridHeight = OrbitalPlatform.Rows * PlannerCellHeight + (OrbitalPlatform.Rows - 1) * 6f;
            float height = HeaderHeight + gridHeight + SectionGap +
                           InspectorHeight + SectionGap +
                           LaunchCardHeight + SectionGap +
                           HeaderHeight + tileRows * (CatalogueTileHeight + 4f) + SectionGap +
                           HeaderHeight + CompactRowHeight + SectionGap +
                           HeaderHeight + RuleLines * 14f + SectionGap;
            RectTransform parent = BeginSub(root, body, height, out float x, out float y, out float width);

            plannerNote = Header(parent, x, ref y, width, "STATION LAYOUT", "");
            plannerGrid = BuildGrid(parent, x, y, width, PlannerCellHeight, true, OnPlannerCell);
            y -= gridHeight + SectionGap;

            BuildInspector(parent, x, y, width);
            y -= InspectorHeight + SectionGap;

            BuildLaunchCard(parent, x, y, width);
            y -= LaunchCardHeight + SectionGap;

            Header(parent, x, ref y, width, "MODULE CATALOGUE", "13 DESIGNS · A STATION CARRIES ABOUT 8");
            float tileWidth = (width - 6f) * 0.5f;
            int index = 0;
            for (int i = 0; i < PlatformModules.Designs.Length; i++)
            {
                ModuleInfo info = PlatformModules.Designs[i];
                if (info.Kind == ModuleKind.Core) continue;
                float tx = x + (index % 2) * (tileWidth + 6f);
                float ty = y - (index / 2) * (CatalogueTileHeight + 4f);
                catalogue[index++] = BuildCatalogueTile(parent, new Rect(tx, ty, tileWidth, CatalogueTileHeight), info);
            }
            y -= tileRows * (CatalogueTileHeight + 4f) + SectionGap;

            Header(parent, x, ref y, width, "LOGISTICS", "CARGO VEHICLE");
            cargoRow = Row(parent, x, y, width, true, "CGO", "CARGO RESUPPLY", null, "LAUNCH", OnCargo);
            y -= CompactRowHeight + SectionGap;

            Header(parent, x, ref y, width, "DESIGN RULES", "GRID UTILITIES · MASS");
            for (int i = 0; i < Rules.Length; i++)
            {
                TMP_Text rule = SingleLine(AvStyled.Label(parent, new Rect(x, y - i * 14f, width, 13f), Rules[i], "row-sub"));
                rule.color = AvTheme.Dim;
            }
        }

        private void BuildInspector(RectTransform parent, float x, float y, float width)
        {
            AvKit.TacticalCard(parent, new Rect(x, y, width, InspectorHeight), AvTheme.RailInfo);
            inspectorTitle = SingleLine(AvStyled.Label(parent, new Rect(x + 12f, y - 8f, width - 124f, 16f), "", "row-name"));
            inspectorSummary = AvStyled.Label(parent, new Rect(x + 12f, y - 27f, width - 124f, 26f), "", "row-sub");
            inspectorSummary.maxVisibleLines = 2;
            inspectorEffects = SingleLine(AvKit.Label(parent, "", new Rect(x + 12f, y - 54f, width - 24f, 13f),
                AvTheme.RailInfo, AvTokens.FontMicro, FontStyles.Bold));
            jettisonButton = AvStyled.Button(parent, new Rect(x + width - 104f, y - 10f, 92f, 24f), "JETTISON", "btn",
                OnJettison, AvButtonStyle.Danger);
        }

        private void BuildLaunchCard(RectTransform parent, float x, float y, float width)
        {
            AvKit.TacticalCard(parent, new Rect(x, y, width, LaunchCardHeight), MobilityColour);
            launchTitle = SingleLine(AvStyled.Label(parent, new Rect(x + 12f, y - 8f, width - 140f, 16f), "", "row-name"));
            launchPrice = AvStyled.Label(parent, new Rect(x + width - 132f, y - 8f, 120f, 16f), "", "row-value",
                align: TextAlignmentOptions.MidlineRight);
            launchLine = SingleLine(AvStyled.Label(parent, new Rect(x + 12f, y - 29f, width - 24f, 14f), "", "row-main"));
            float bandWidth = 64f;
            for (int i = 0; i < bandButtons.Length; i++)
            {
                byte band = (byte)i;
                bandButtons[i] = AvStyled.Button(parent, new Rect(x + 12f + i * (bandWidth + 4f), y - 26f, bandWidth, 20f),
                    OrbitRegimes.All[i].Code, "btn", () => { coreBand = band; nextRefresh = 0f; }, AvButtonStyle.Quiet)
                    .WithTooltip(OrbitRegimes.All[i].Name + " — " + OrbitRegimes.All[i].Summary);
            }
            launchProjection = SingleLine(AvKit.Label(parent, "", new Rect(x + 12f, y - 50f, width - 24f, 13f),
                AvTheme.RailInfo, AvTokens.FontMicro, FontStyles.Bold));
            launchStatus = SingleLine(AvStyled.Label(parent, new Rect(x + 12f, y - 70f, width - 130f, 14f), "", "row-sub"));
            launchButton = AvStyled.Button(parent, new Rect(x + width - 112f, y - 66f, 100f, 26f), "LAUNCH", "btn",
                OnLaunch, AvButtonStyle.Primary);
            launchProgress = AvKit.ProgressBar(parent, new Rect(x + 12f, y - 98f, width - 24f, 5f), 0f, AvTheme.RailInfo);
        }

        private CatalogueTile BuildCatalogueTile(RectTransform parent, Rect area, in ModuleInfo info)
        {
            var tile = new CatalogueTile { Kind = info.Kind, Fill = AvKit.Panel(parent, area, AvTheme.Surface, AvSprites.Card) };
            AvKit.Outline(parent, area, AvTheme.Hairline);
            Color colour = CategoryColour(info.Category);
            AvKit.Rule(parent, new Rect(area.x, area.y, 3f, area.height), colour);
            AvKit.Label(parent, info.Code, new Rect(area.x + 9f, area.y - 5f, 36f, 16f), colour, AvTokens.FontLead, FontStyles.Bold);
            SingleLine(AvStyled.Label(parent, new Rect(area.x + 46f, area.y - 5f, area.width - 118f, 16f), info.Name, "row-sub"))
                .color = AvTheme.TextPrimary;
            tile.State = AvKit.Label(parent, "", new Rect(area.x + area.width - 76f, area.y - 5f, 70f, 14f), AvTheme.Dim,
                AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Right);
            SingleLine(AvKit.Label(parent, Spec(info), new Rect(area.x + 9f, area.y - 25f, area.width - 16f, 13f), AvTheme.Dim,
                AvTokens.FontMicro));
            ModuleKind kind = info.Kind;
            AvKit.HitButton(parent, area, () =>
            {
                plannerModule = plannerModule == kind ? ModuleKind.None : kind;
                nextRefresh = 0f;
            }).WithTooltip(info.Name + " — " + info.Summary);
            return tile;
        }

        private static string Spec(in ModuleInfo info)
        {
            string power = info.SolarKw > 0f ? "+" + info.SolarKw.ToString("0", Invariant) + " KW SUN"
                : info.SteadyKw > 0f ? "+" + info.SteadyKw.ToString("0", Invariant) + " KW"
                : info.StorageKj > 0f ? "+" + PlatformWords.Whole(info.StorageKj) + " KJ"
                : info.LoadKw > 0f ? PlatformWords.Kilowatts(-info.LoadKw)
                : "NO LOAD";
            return PlatformWords.Tonnes(info.Mass) + " · " + power + " · " +
                   PlatformWords.Whole(PlatformModules.LaunchPrice(info.Kind)) + " BASE";
        }

        // ---- Actions -------------------------------------------------------------------------------

        private void OnPlannerCell(int cell)
        {
            plannerCell = cell;
            jettisonArmedCell = -1;
            nextRefresh = 0f;
        }

        private void OnJettison()
        {
            OrbitalPlatform platform = support.LocalPlatform;
            if (platform == null || !platform.Exists || !OrbitalPlatform.InGrid(plannerCell) ||
                platform.Cell(plannerCell) == ModuleKind.None || support.CommandPending) return;
            if (jettisonArmedCell != plannerCell || Time.unscaledTime > jettisonArmedUntil)
            {
                jettisonArmedCell = plannerCell;
                jettisonArmedUntil = Time.unscaledTime + ConfirmSeconds;
                nextRefresh = 0f;
                return;
            }
            jettisonArmedCell = -1;
            ModuleKind kind = platform.Cell(plannerCell);
            Log(kind == ModuleKind.Core
                ? "FLIGHT · DEORBIT " + OrbitalPlatform.Callsign + " · ALL MODULES"
                : "FLIGHT · JETTISON " + PlatformModules.Info(kind).Code + " " + OrbitalPlatform.CellName(plannerCell));
            support.RequestJettison(plannerCell);
            nextRefresh = 0f;
        }

        private void OnLaunch()
        {
            if (countdownEnd > 0f)
            {
                if (countdownKind != LaunchKind.Cargo) HoldCountdown();
                return;
            }
            OrbitalPlatform platform = support.LocalPlatform;
            if (platform == null || !platform.Exists) StartCountdown(LaunchKind.Core, ModuleKind.Core, OrbitalPlatform.CoreCell, coreBand);
            else if (plannerModule != ModuleKind.None && OrbitalPlatform.InGrid(plannerCell))
                StartCountdown(LaunchKind.Module, plannerModule, plannerCell, 0);
        }

        private void OnCargo()
        {
            if (countdownEnd > 0f)
            {
                if (countdownKind == LaunchKind.Cargo) HoldCountdown();
                return;
            }
            StartCountdown(LaunchKind.Cargo, ModuleKind.Cargo, OrbitalPlatform.CoreCell, 0);
        }

        // ---- Refresh ---------------------------------------------------------------------------------

        private void RefreshPlannerPage(bool bypass, OrbitalPlatform platform, double now, in OrbitClock clock)
        {
            if (plannerGrid == null) return;
            bool station = platform != null && platform.Exists;
            PlatformStats stats = station ? platform.Stats(now) : default;

            if (station && !OrbitalPlatform.InGrid(plannerCell)) plannerCell = FirstFreeCell(platform);
            plannerNote.text = station
                ? "MASS " + PlatformWords.Tonnes(stats.Mass) + " / " + PlatformWords.Tonnes(OrbitalPlatform.MassLimit) +
                  " · " + stats.Modules + "/" + OrbitalPlatform.CellCount + " CELLS"
                : "NO STATION · CORE GOES IN B3";
            PaintGrid(plannerGrid, platform, now, station ? plannerCell : OrbitalPlatform.CoreCell, true);

            RefreshInspector(platform, station, now);
            RefreshLaunchCard(bypass, platform, station, stats, now, clock);
            RefreshCatalogue(platform, station, now);
            RefreshCargo(bypass, platform, station);
        }

        private static int FirstFreeCell(OrbitalPlatform platform)
        {
            for (int i = 0; i < OrbitalPlatform.CellCount; i++)
                if (platform.CanAttach(i)) return i;
            return OrbitalPlatform.CoreCell;
        }

        private void RefreshInspector(OrbitalPlatform platform, bool station, double now)
        {
            bool confirming = jettisonArmedCell == plannerCell && Time.unscaledTime <= jettisonArmedUntil;
            if (!station)
            {
                inspectorTitle.text = "B3 · CORE MODULE (NOT LAUNCHED)";
                inspectorSummary.text = PlatformModules.Info(ModuleKind.Core).Summary +
                                        " Launch it below; every other module docks to it.";
                inspectorEffects.text = "12.0 T · +4 KW SUN · 600 KJ · HEAVY LIFT";
                jettisonButton.gameObject.SetActive(false);
                return;
            }

            string name = OrbitalPlatform.CellName(plannerCell);
            ModuleKind kind = platform.Cell(plannerCell);
            bool pending = platform.Pending != ModuleKind.None && platform.Pending != ModuleKind.Cargo &&
                           platform.PendingCell == plannerCell;
            jettisonButton.gameObject.SetActive(kind != ModuleKind.None);

            if (kind != ModuleKind.None)
            {
                ModuleInfo info = PlatformModules.Info(kind);
                inspectorTitle.text = name + " · " + info.Name + (platform.IsOnline(plannerCell, now) ? "" : " · OFFLINE");
                inspectorSummary.text = info.Summary;
                inspectorEffects.text = Effects(platform, plannerCell, kind, now);
                bool core = kind == ModuleKind.Core;
                bool strands = !core && platform.WouldStrand(plannerCell);
                jettisonButton.SetEnabled(!support.CommandPending && !strands);
                jettisonButton.SetText(confirming ? "CONFIRM" : core ? "DEORBIT" : "JETTISON");
                jettisonButton.SetLatched(confirming);
                float refund = (core ? EstimatedStationValue(platform) : support.LaunchCost(kind)) * support.JettisonRefund;
                jettisonButton.WithTooltip(strands
                    ? "Other modules dock through this one; jettison them first."
                    : (core ? "Deorbit the whole station" : "Jettison this module") + " — refunds about " +
                      Figure(refund) + " (" + Mathf.RoundToInt(support.JettisonRefund * 100f) + "% of what was paid). Click twice.");
            }
            else if (pending)
            {
                inspectorTitle.text = name + " · " + PlatformModules.Info(platform.Pending).Name + " IN FLIGHT";
                inspectorSummary.text = "Hard dock in " + PlatformWords.Clock(platform.DockAt - now) +
                                        ". The cell is reserved until it arrives.";
                inspectorEffects.text = "ONE LAUNCH AT A TIME";
            }
            else if (platform.CanAttach(plannerCell))
            {
                inspectorTitle.text = name + " · FREE CELL";
                inspectorSummary.text = "Pick a module in the catalogue to dock here, then launch it below.";
                inspectorEffects.text = NeighbourHint(platform, plannerCell);
            }
            else
            {
                inspectorTitle.text = name + " · NOT CONNECTED";
                inspectorSummary.text = "Modules dock next to the station. Build toward this cell first.";
                inspectorEffects.text = "";
            }
        }

        private float EstimatedStationValue(OrbitalPlatform platform)
        {
            float total = 0f;
            for (int i = 0; i < OrbitalPlatform.CellCount; i++)
                if (platform.Cell(i) != ModuleKind.None) total += support.LaunchCost(platform.Cell(i));
            return total;
        }

        private string Effects(OrbitalPlatform platform, int cell, ModuleKind kind, double now)
        {
            ModuleInfo info = PlatformModules.Info(kind);
            effects.Clear();
            effects.Append(PlatformWords.Tonnes(info.Mass));
            if (info.SolarKw > 0f) effects.Append(" · +").Append(info.SolarKw.ToString("0", Invariant)).Append(" KW SUN");
            if (info.SteadyKw > 0f) effects.Append(" · +").Append(info.SteadyKw.ToString("0", Invariant)).Append(" KW");
            if (info.LoadKw > 0f) effects.Append(" · ").Append(PlatformWords.Kilowatts(-info.LoadKw));
            if (info.StorageKj > 0f) effects.Append(" · ").Append(PlatformWords.Whole(info.StorageKj)).Append(" KJ");
            if (info.Fuel > 0f) effects.Append(" · ").Append(PlatformWords.Whole(info.Fuel)).Append(" FUEL");
            if (info.Rods > 0) effects.Append(" · ").Append(info.Rods).Append(" RODS");
            if (info.Hot) effects.Append(platform.IsCooled(cell, now) ? " · COOLED" : " · HOT, NEEDS RAD");
            if ((kind == ModuleKind.Imager || kind == ModuleKind.Sigint) && platform.IsBoosted(cell, now)) effects.Append(" · +35% REL");
            if (platform.IsShielded(cell)) effects.Append(" · SHIELDED");
            if (!platform.IsOnline(cell, now))
                effects.Append(" · OFFLINE ").Append(PlatformWords.Clock(platform.OfflineRemaining(cell, now)));
            return effects.ToString();
        }

        private static string NeighbourHint(OrbitalPlatform platform, int cell)
        {
            int[] around = HintScratch;
            OrbitalPlatform.Neighbours(cell, around);
            bool radiator = false, relay = false, shield = false, hot = false, sensor = false;
            for (int i = 0; i < 4; i++)
            {
                ModuleKind kind = platform.Cell(around[i]);
                radiator |= kind == ModuleKind.Radiator;
                relay |= kind == ModuleKind.Relay;
                shield |= kind == ModuleKind.Shield;
                hot |= kind != ModuleKind.None && PlatformModules.Info(kind).Hot;
                sensor |= kind == ModuleKind.Imager || kind == ModuleKind.Sigint;
            }
            string hint = "";
            if (radiator) hint += "COOLED HERE · ";
            if (relay) hint += "RELAY BOOST HERE · ";
            if (shield) hint += "SHIELDED HERE · ";
            if (hot) hint += "A RAD HERE COOLS A HOT NEIGHBOUR · ";
            if (sensor) hint += "A REL HERE BOOSTS A SENSOR · ";
            return hint.Length > 3 ? hint.Substring(0, hint.Length - 3) : "NO NEIGHBOUR UTILITIES";
        }

        private void RefreshLaunchCard(bool bypass, OrbitalPlatform platform, bool station, in PlatformStats stats,
                                       double now, in OrbitClock clock)
        {
            for (int i = 0; i < bandButtons.Length; i++)
            {
                bandButtons[i].gameObject.SetActive(!station);
                bandButtons[i].SetLatched(i == coreBand);
                bandButtons[i].SetEnabled(i != OrbitRegimes.Low);
            }
            launchLine.gameObject.SetActive(station);
            if (!station && coreBand == OrbitRegimes.Low) coreBand = OrbitRegimes.Mid;

            bool counting = countdownEnd > 0f && countdownKind != LaunchKind.Cargo;
            Tone tone;
            string status;
            bool enabled;
            float progress = 0f;
            ModuleKind costed;

            if (!station)
            {
                OrbitRegime band = OrbitRegimes.Get(coreBand);
                costed = ModuleKind.Core;
                launchTitle.text = "STEP 1 · LAUNCH THE CORE";
                launchProjection.text = band.Name + " · PASS ~" + PlatformWords.Clock(TheaterTrack.WindowSeconds(band)) +
                                        " · GAP " + PlatformWords.Clock(band.GapSeconds * clock.GapScale) + " · GSD " +
                                        band.NadirGsd.ToString("0.0", Invariant) + " M · INSERTION " +
                                        Mathf.RoundToInt(support.Settings != null ? support.Settings.PlatformInsertionSeconds.Value : 45f) + " S";
            }
            else if (platform.Pending != ModuleKind.None)
            {
                costed = platform.Pending;
                ModuleInfo flying = PlatformModules.Info(platform.Pending);
                launchTitle.text = "IN FLIGHT · " + flying.Name;
                launchLine.text = (platform.Pending == ModuleKind.Cargo ? "DOCKING AT THE CORE"
                    : "DOCKING AT " + OrbitalPlatform.CellName(platform.PendingCell)) + " · " +
                    PlatformModules.VehicleFor(flying.Mass).Code + " LIFT";
                launchProjection.text = "HARD DOCK IN " + PlatformWords.Clock(platform.DockAt - now);
                float total = support.Settings != null ? support.Settings.PlatformDockingSeconds.Value : 20f;
                progress = 1f - Mathf.Clamp01((float)((platform.DockAt - now) / Mathf.Max(1f, total)));
            }
            else if (plannerModule == ModuleKind.None)
            {
                costed = ModuleKind.None;
                launchTitle.text = "NEXT LAUNCH";
                launchLine.text = "PICK A MODULE IN THE CATALOGUE · DOCKS AT " + OrbitalPlatform.CellName(plannerCell);
                launchProjection.text = "AFTER · MASS " + PlatformWords.Tonnes(stats.Mass) + " / 40 T · SUN " +
                                        PlatformWords.Kilowatts(stats.NetSunKw) + " · DARK " +
                                        PlatformWords.Kilowatts(stats.NetEclipseKw);
            }
            else
            {
                costed = plannerModule;
                ModuleInfo info = PlatformModules.Info(plannerModule);
                launchTitle.text = "NEXT LAUNCH · " + info.Name;
                launchLine.text = info.Code + " → " + OrbitalPlatform.CellName(plannerCell) + " · " +
                                  PlatformWords.Tonnes(info.Mass) + " · " + PlatformModules.VehicleFor(info.Mass).Code + " LIFT";
                float sun = stats.NetSunKw + info.SolarKw + info.SteadyKw - info.LoadKw;
                float dark = stats.NetEclipseKw + info.SteadyKw - info.LoadKw;
                launchProjection.text = "AFTER · MASS " + PlatformWords.Tonnes(stats.Mass + info.Mass) + " / 40 T · SUN " +
                                        PlatformWords.Kilowatts(sun) + " · DARK " + PlatformWords.Kilowatts(dark) +
                                        " · STORE " + PlatformWords.Whole(stats.StorageKj + info.StorageKj) + " KJ";
            }

            float cost = costed != ModuleKind.None ? support.LaunchCost(costed) : 0f;
            launchPrice.text = cost > 0f && (!station || platform.Pending == ModuleKind.None) ? Figure(cost) : "";

            if (counting)
            {
                int left = Mathf.CeilToInt(CountdownRemaining);
                tone = Tone.Armed;
                status = "T-" + left.ToString("00", Invariant) + " · " + PollLine(left);
                enabled = true;
                progress = 1f - CountdownRemaining / CountdownSeconds;
            }
            else if (countdownEnd > 0f)
            {
                tone = Tone.Pending;
                status = "CARGO COUNT IN PROGRESS";
                enabled = false;
            }
            else if (support.CommandPending)
            {
                tone = Tone.Pending;
                status = "COMMAND PENDING · AWAITING HOST";
                enabled = false;
            }
            else if (station && platform.Pending != ModuleKind.None)
            {
                tone = Tone.Pending;
                status = "ONE LAUNCH AT A TIME · WAIT FOR DOCKING";
                enabled = false;
            }
            else if (costed == ModuleKind.None)
            {
                tone = Tone.Locked;
                status = "SELECT A MODULE";
                enabled = false;
            }
            else
            {
                PlacementFailure placement = platform == null ? PlacementFailure.NoPlatform
                    : station ? platform.CheckPlacement(costed, plannerCell, 0, now)
                    : platform.CheckPlacement(ModuleKind.Core, OrbitalPlatform.CoreCell, coreBand, now);
                if (placement != PlacementFailure.None)
                {
                    tone = placement == PlacementFailure.OverMass || placement == PlacementFailure.CopyLimit ? Tone.Danger : Tone.Locked;
                    status = PlatformWords.Placement(placement) + (placement == PlacementFailure.NotAttached ||
                             placement == PlacementFailure.CellOccupied ? " · PICK A FREE CELL" : "");
                    enabled = false;
                }
                else if (!bypass && support.LocalAllocation + 0.001f < cost)
                {
                    tone = Tone.Danger;
                    status = "INSUFFICIENT ALLOCATION";
                    enabled = false;
                }
                else
                {
                    tone = Tone.Ready;
                    status = "GO · " + Mathf.RoundToInt(CountdownSeconds) + " S COUNT · " +
                             (station ? "DOCKS " + Mathf.RoundToInt(support.Settings != null ? support.Settings.PlatformDockingSeconds.Value : 20f) + " S LATER"
                                      : "HEAVY LIFT");
                    enabled = true;
                }
            }

            launchStatus.text = status;
            launchStatus.color = ToneColour(tone);
            launchProgress.fillAmount = Mathf.Clamp01(progress);
            launchProgress.color = counting ? AvTheme.RailCaution : AvTheme.RailInfo;
            launchButton.SetEnabled(enabled);
            launchButton.SetLatched(counting);
            launchButton.SetText(counting ? "HOLD" : station ? "LAUNCH" : "LAUNCH CORE");
            launchButton.WithTooltip(counting
                ? "Hold the count. Nothing has been sent or charged yet."
                : "Start a " + Mathf.RoundToInt(CountdownSeconds) + " s GO/NO-GO count; the host charges at liftoff. " + status + ".");
        }

        private void RefreshCatalogue(OrbitalPlatform platform, bool station, double now)
        {
            int cell = station ? plannerCell : OrbitalPlatform.CoreCell;
            for (int i = 0; i < catalogue.Length; i++)
            {
                CatalogueTile tile = catalogue[i];
                if (tile == null) continue;
                Tone tone;
                string state;
                if (!station)
                {
                    tone = Tone.Locked;
                    state = "NEEDS CORE";
                }
                else
                {
                    PlacementFailure placement = platform.CheckPlacement(tile.Kind, cell, 0, now);
                    switch (placement)
                    {
                        case PlacementFailure.None: tone = Tone.Ready; state = "FITS"; break;
                        case PlacementFailure.OverMass: tone = Tone.Danger; state = "OVER MASS"; break;
                        case PlacementFailure.CopyLimit:
                            tone = Tone.Locked;
                            state = "LIMIT " + PlatformModules.Info(tile.Kind).MaxCopies;
                            break;
                        case PlacementFailure.LaunchInFlight: tone = Tone.Pending; state = "IN FLIGHT"; break;
                        default: tone = Tone.Locked; state = "FREE CELL?"; break;
                    }
                }

                bool selected = tile.Kind == plannerModule;
                tile.Fill.color = selected ? AvTheme.Accent.WithAlpha(0.2f) : AvTheme.Surface;
                if (tile.LastState == state && tile.LastTone == tone) continue;
                tile.LastState = state;
                tile.LastTone = tone;
                tile.State.text = state;
                tile.State.color = ToneColour(tone);
            }
        }

        private void RefreshCargo(bool bypass, OrbitalPlatform platform, bool station)
        {
            float cost = support.LaunchCost(ModuleKind.Cargo);
            bool counting = countdownEnd > 0f && countdownKind == LaunchKind.Cargo;
            cargoRow.Value.text = Figure(cost);
            Tone tone;
            string status;
            bool enabled = false;
            if (counting)
            {
                int left = Mathf.CeilToInt(CountdownRemaining);
                tone = Tone.Armed;
                status = "T-" + left.ToString("00", Invariant) + " · " + PollLine(left);
                enabled = true;
            }
            else if (!station) { tone = Tone.Locked; status = "NO STATION TO RESUPPLY"; }
            else if (countdownEnd > 0f) { tone = Tone.Pending; status = "LAUNCH COUNT IN PROGRESS"; }
            else if (support.CommandPending) { tone = Tone.Pending; status = "COMMAND PENDING"; }
            else if (platform.Pending != ModuleKind.None) { tone = Tone.Pending; status = "ONE LAUNCH AT A TIME"; }
            else if (!bypass && support.LocalAllocation + 0.001f < cost) { tone = Tone.Danger; status = "INSUFFICIENT ALLOCATION"; }
            else
            {
                PlatformStats stats = platform.Stats(support.OrbitNow);
                bool needed = platform.Fuel < stats.FuelCapacity - 0.5f || platform.Rods < stats.RodCapacity;
                tone = needed ? Tone.Ready : Tone.Locked;
                status = needed ? "READY · REFILLS " + PlatformWords.Whole(stats.FuelCapacity) + " FUEL, " + stats.RodCapacity + " RODS"
                                : "TANKS AND MAGAZINES FULL";
                enabled = true;
            }
            cargoRow.Primary.SetEnabled(enabled);
            cargoRow.Primary.SetLatched(counting);
            cargoRow.Primary.SetText(counting ? "HOLD" : "LAUNCH");
            if (Paint(cargoRow, tone, status))
                cargoRow.Primary.WithTooltip("Uncrewed freighter: refills every fuel tank and rod magazine when it docks. " + status + ".");
        }
    }
}

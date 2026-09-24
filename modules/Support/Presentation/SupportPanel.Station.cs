using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPACE status: fixed station sector, construction, health and voice loop.
    /// The 3×3 sector grid matches station control. Orders belong to the station room.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float PositionSize = 124f;
        private const float MiniCellGap = 5f;

        private static readonly string[] TileKeys =
            { "POWER", "THERMAL", "FUEL", "LINK", "CREW", "DEBRIS", "ORBIT", "MODULES" };
        private static readonly string[] StationTips =
        {
            "The station at a glance: fixed position, modules, health and the voice loop.",
            "The station's abilities and their module requirements."
        };

        private RectTransform stationHero, stationEmptyCard, stationBlueprint, stationHealth, stationLoopGroup;
        private Image bannerRail;
        private readonly Image[] stationSectors = new Image[9];
        private TMP_Text bannerWord, bannerClock, bannerBand, bannerNote, bannerMission;
        private AvButton consoleButton;
        private readonly Image[] miniFill = new Image[OrbitalPlatform.CellCount];
        private readonly Image[][] miniEdge = new Image[OrbitalPlatform.CellCount][];
        private readonly Image[] miniGlyph = new Image[OrbitalPlatform.CellCount];
        private readonly TMP_Text[] miniCode = new TMP_Text[OrbitalPlatform.CellCount];
        private readonly Tile[] tiles = new Tile[8];
        private TMP_Text trussNote, resourceLine, rodsLine;
        private Image energyBar, fuelBar;
        private float resourceBarWidth;
        private TMP_Text[] stationLoop;

        private void ResetStationPage()
        {
            stationHero = stationEmptyCard = stationBlueprint = stationHealth = stationLoopGroup = null;
            bannerRail = null;
            for (int i = 0; i < stationSectors.Length; i++) stationSectors[i] = null;
            bannerWord = bannerClock = bannerBand = bannerNote = bannerMission = null;
            consoleButton = null;
            for (int i = 0; i < miniFill.Length; i++)
            {
                miniFill[i] = null;
                miniEdge[i] = null;
                miniGlyph[i] = null;
                miniCode[i] = null;
            }
            for (int i = 0; i < tiles.Length; i++) tiles[i] = null;
            trussNote = resourceLine = rodsLine = null;
            energyBar = fuelBar = null;
            stationLoop = null;
        }

        // ---- Build -----------------------------------------------------------------------------

        private void BuildStationPage(RectTransform root, Rect body)
        {
            OpsSprites.Ensure();
            Rect content = PageFrame(root, body, OpsDomain.Space, OrbitalPlatform.Callsign + " · STATION", SubStatus, SelectSpaceSub, StationTips, out _);
            Rect[] at = Stack(content, new[]
            {
                new StackPiece(1, Mathf.Min(152f, content.height), 0f, false),
                new StackPiece(2, 112f, 112f, false),
                new StackPiece(3, 166f, 166f, false),
                new StackPiece(4, 0f, 64f, true)
            }, 8f);

            stationHero = Section(root, "StationPosition", at[0]);
            BuildStationPosition(stationHero, at[0].width, at[0].height);
            // With no station the one card takes the hero's, blueprint's and health's room (M2, M6).
            float span = at[3].height > 0f ? at[0].y - at[3].y - 8f : content.height;
            var emptyAt = new Rect(at[0].x, at[0].y, at[0].width, span);
            stationEmptyCard = Section(root, "NoStation", emptyAt);
            BuildStationEmpty(stationEmptyCard, emptyAt.width, emptyAt.height);
            stationBlueprint = Section(root, "MiniBlueprint", at[1]);
            if (at[1].height > 0f) BuildMiniBlueprint(stationBlueprint, at[1].width, at[1].height);
            stationHealth = Section(root, "Health", at[2]);
            if (at[2].height > 0f) BuildHealth(stationHealth, at[2].width);
            stationLoopGroup = Section(root, "VoiceLoop", at[3]);
            if (at[3].height > 0f) stationLoop = BuildLoopLines(stationLoopGroup, at[3].width, at[3].height, "VOICE LOOP · FLIGHT");
        }

        private void BuildStationPosition(RectTransform parent, float w, float h)
        {
            bannerRail = InstrumentPlate(parent, new Rect(0f, 0f, w, h), AvTheme.RailInfo);
            bool compact = h < 140f;
            float d = Mathf.Min(PositionSize, h - 16f);
            var sectorGrid = new Rect(14f, -(h - d) * 0.5f, d, d);
            Image plotting = AvKit.Panel(parent, sectorGrid, AvTheme.RailInfo.WithAlpha(0.14f), OpsSprites.Blueprint);
            plotting.type = Image.Type.Tiled;
            AvKit.Rule(parent, new Rect(sectorGrid.x + d * 0.5f, sectorGrid.y, 1f, d), AvTheme.RailInfo.WithAlpha(0.4f));
            AvKit.Rule(parent, new Rect(sectorGrid.x, sectorGrid.y - d * 0.5f, d, 1f), AvTheme.RailInfo.WithAlpha(0.4f));
            Image ring = AvKit.Panel(parent, new Rect(sectorGrid.x + 2f, sectorGrid.y - 2f, d - 4f, d - 4f),
                AvTheme.RailInfo.WithAlpha(0.38f), OpsSprites.Ring);
            ring.raycastTarget = false;
            string[] labels = { "NW", "N", "NE", "W", "C", "E", "SW", "S", "SE" };
            float pitch = d / 3f;
            for (int i = 0; i < stationSectors.Length; i++)
            {
                var cell = new Rect(sectorGrid.x + (i % 3) * pitch, sectorGrid.y - (i / 3) * pitch, pitch - 4f, pitch - 4f);
                stationSectors[i] = AvKit.Panel(parent, cell, AvTheme.SurfaceInert.WithAlpha(0.65f));
                AvKit.Outline(parent, cell, AvTheme.RailInfo.WithAlpha(0.5f));
                AvKit.Label(parent, labels[i], cell, AvTheme.TextPrimary, 12f, FontStyles.Bold, TextAlignmentOptions.Center);
            }

            float x0 = d + 30f, cw = w - x0 - 10f;
            bannerWord = AvKit.Label(parent, "", new Rect(x0, -8f, cw, 28f), AvTheme.Dim, 22f, FontStyles.Bold);
            bannerWord.enableAutoSizing = true;
            bannerWord.fontSizeMin = AvTokens.FontLead;
            bannerWord.fontSizeMax = 22f;
            bannerClock = SingleLine(AvKit.Label(parent, "", new Rect(x0, -36f, cw, 18f), AvTheme.TextPrimary, AvTokens.FontLead,
                FontStyles.Bold));
            bannerBand = SingleLine(AvStyled.Label(parent, new Rect(x0, -56f, cw, 14f), "", "row-sub"));
            bannerNote = SingleLine(AvStyled.Label(parent, new Rect(x0, -72f, cw, 14f), "", "row-sub"));
            bannerMission = SingleLine(AvStyled.Label(parent, new Rect(x0, -88f, cw, 14f), "", "row-sub"));
            // At 420 retain status, sector and altitude; notes wait for a taller panel.
            bannerNote.gameObject.SetActive(!compact);
            bannerMission.gameObject.SetActive(!compact);
            consoleButton = AvStyled.Button(parent, new Rect(x0, -h + (compact ? 32f : 36f), cw, 26f), "OPEN STATION WALL", "btn",
                OpenStationConsole, AvButtonStyle.Primary)
                .WithTooltip("The flight-control wall: loadouts, the blueprint, the module rack and every launch.");
        }

        /// <summary>One card, one call to action (M6): nothing else on the page repeats it.</summary>
        private void BuildStationEmpty(RectTransform parent, float w, float h)
        {
            InstrumentPlate(parent, new Rect(0f, 0f, w, h), AvTheme.RailInfo);
            Image ghost = AvKit.Panel(parent, new Rect(w - 100f, -8f, 76f, 76f), AvTheme.RailInfo.WithAlpha(0.18f),
                OpsSprites.Glyph(OpsSprites.G.Space));
            ghost.raycastTarget = false;
            AvKit.Label(parent, "NO STATION ON ORBIT", new Rect(14f, -12f, w - 28f, 24f), AvTheme.TextPrimary, AvTokens.FontTitle,
                FontStyles.Bold).characterSpacing = 2f;
            bool roomy = h >= 150f;
            if (roomy)
                Wrapped(AvStyled.Label(parent, new Rect(14f, -42f, w - 28f, 44f),
                    "Launch " + OrbitalPlatform.Callsign + "'s core from the station wall; every other module docks to it, " +
                    "providing persistent coverage from a fixed position.", "row-sub"));
            // The launch is the next decision; keep its control above the optional loadout briefing.
            AvStyled.Button(parent, new Rect(14f, roomy ? -94f : -h + 38f, w - 28f, 30f),
                "OPEN STATION WALL · LAUNCH THE CORE", "btn", OpenStationConsole, AvButtonStyle.Primary)
                .WithTooltip("Opens the station wall with the core launch as the next step.");
            float y = -136f;
            for (int i = 0; i < PlatformMissions.Count && y - 54f > -h + 16f; i++)
            {
                var mission = (PlatformMission)i;
                AvKit.Panel(parent, new Rect(14f, y, w - 28f, 52f), AvTheme.Surface);
                AvKit.Rule(parent, new Rect(14f, y, 2f, 52f), AvTheme.RailInfo);
                SingleLine(AvStyled.Label(parent, new Rect(24f, y - 5f, 24f, 15f), "0" + (i + 1), "row-sub"));
                SingleLine(AvStyled.Label(parent, new Rect(52f, y - 5f, w - 66f, 16f), PlatformMissions.Name(mission) + " LOADOUT",
                    "row-name"));
                Wrapped(AvStyled.Label(parent, new Rect(52f, y - 23f, w - 66f, 27f), PlatformMissions.Brief(mission), "row-sub"))
                    .color = AvTheme.Dim;
                y -= 58f;
            }
            float schematicHeight = h + y - 22f;
            if (schematicHeight >= 84f)
            {
                Rect plot = new Rect(14f, y - 6f, w - 28f, schematicHeight);
                Image grid = AvKit.Panel(parent, plot, AvTheme.RailInfo.WithAlpha(0.12f), OpsSprites.Blueprint);
                grid.type = Image.Type.Tiled;
                AvKit.Outline(parent, plot, AvTheme.Hairline);
                float cx = plot.x + plot.width * 0.5f;
                float cy = plot.y - plot.height * 0.5f;
                AvKit.Rule(parent, new Rect(plot.x + 12f, cy, plot.width - 24f, 1f), AvTheme.RailInfo.WithAlpha(0.32f));
                AvKit.Rule(parent, new Rect(cx, plot.y - 12f, 1f, plot.height - 24f), AvTheme.RailInfo.WithAlpha(0.32f));
                float diameter = Mathf.Min(84f, plot.height - 22f);
                Image ring = AvKit.Panel(parent, new Rect(cx - diameter * 0.5f, cy + diameter * 0.5f,
                    diameter, diameter), AvTheme.RailInfo.WithAlpha(0.6f), OpsSprites.Ring);
                ring.raycastTarget = false;
                Image core = AvKit.Panel(parent, new Rect(cx - 11f, cy + 11f, 22f, 22f),
                    AvTheme.RailInfo, OpsSprites.Glyph(OpsSprites.G.Space));
                core.raycastTarget = false;
                AvStyled.Label(parent, new Rect(plot.x + 8f, plot.y - 4f, plot.width - 16f, 13f),
                    "CORE SLOT / EMPTY ORBIT", "section-title-note");
            }
            parent.gameObject.SetActive(false);
        }

        private void BuildMiniBlueprint(RectTransform parent, float w, float h)
        {
            AvStyled.Label(parent, new Rect(0f, 0f, 200f, 16f), "STATION", "section-title");
            trussNote = SingleLine(AvStyled.Label(parent, new Rect(w - 260f, 0f, 260f, 16f), "", "section-title-note",
                align: TextAlignmentOptions.MidlineRight));
            float top = -22f;
            float cellW = (w - MiniCellGap * (OrbitalPlatform.Columns - 1)) / OrbitalPlatform.Columns;
            float cellH = (h - 22f - MiniCellGap * (OrbitalPlatform.Rows - 1)) / OrbitalPlatform.Rows;
            for (int i = 0; i < OrbitalPlatform.CellCount; i++)
            {
                float x = OrbitalPlatform.Column(i) * (cellW + MiniCellGap);
                float y = top - OrbitalPlatform.Row(i) * (cellH + MiniCellGap);
                var cell = new Rect(x, y, cellW, cellH);
                miniFill[i] = AvKit.Panel(parent, cell, AvTheme.Surface);
                miniEdge[i] = AvKit.Outline(parent, cell, AvTheme.Hairline);
                miniGlyph[i] = AvKit.Panel(parent, new Rect(x + 6f, y - (cellH - 18f) * 0.5f, 18f, 18f), AvTheme.TextPrimary);
                miniCode[i] = AvKit.Label(parent, "", new Rect(x + 28f, y, cellW - 32f, cellH), AvTheme.TextPrimary, AvTokens.FontSmall,
                    FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
                miniCode[i].enableAutoSizing = true;
                miniCode[i].fontSizeMin = AvTokens.FontMicro;
                miniCode[i].fontSizeMax = AvTokens.FontSmall;
            }
        }

        private void BuildHealth(RectTransform parent, float w)
        {
            AvStyled.Label(parent, new Rect(0f, 0f, 240f, 16f), "HEALTH · RESOURCES", "section-title");
            float tileWidth = (w - 12f) / 4f;
            for (int i = 0; i < tiles.Length; i++)
                tiles[i] = BuildTile(parent, new Rect((i % 4) * (tileWidth + 4f), -20f - (i / 4) * (TileHeight + 4f), tileWidth, TileHeight),
                    TileKeys[i]);
            float y = -20f - TileHeight * 2f - 10f;
            float half = (w - 12f) * 0.5f;
            resourceBarWidth = half;
            AvKit.Panel(parent, new Rect(0f, y, half, 4f), AvTheme.SurfaceInert);
            energyBar = AvKit.Panel(parent, new Rect(0f, y, 0f, 4f), AvTheme.RailReady);
            AvKit.Panel(parent, new Rect(half + 12f, y, half, 4f), AvTheme.SurfaceInert);
            fuelBar = AvKit.Panel(parent, new Rect(half + 12f, y, 0f, 4f), MobilityColour);
            resourceLine = SingleLine(AvStyled.Label(parent, new Rect(0f, y - 8f, w, 14f), "", "row-sub"));
            rodsLine = SingleLine(AvStyled.Label(parent, new Rect(0f, y - 24f, w, 14f), "", "row-sub"));
        }

        /// <summary>A log that takes whatever height the stack left it.</summary>
        private static TMP_Text[] BuildLoopLines(RectTransform parent, float w, float h, string title)
        {
            AvStyled.Label(parent, new Rect(0f, 0f, w, 16f), title, "section-title");
            const float pitch = 18f;
            int lines = Mathf.Clamp(Mathf.FloorToInt((h - 20f) / pitch), 1, LoopLines);
            var labels = new TMP_Text[lines];
            for (int i = 0; i < lines; i++)
            {
                labels[i] = SingleLine(AvStyled.Label(parent, new Rect(0f, -20f - i * pitch, w, pitch), "", "row-sub"));
                labels[i].color = i == 0 ? AvTheme.TextPrimary : AvTheme.Dim;
                labels[i].enableAutoSizing = true;
                labels[i].fontSizeMin = AvTokens.FontMicro;
                labels[i].fontSizeMax = labels[i].fontSize;
            }
            return labels;
        }

        // ---- Refresh ---------------------------------------------------------------------------

        private void RefreshStationPage(OrbitalPlatform platform, double now, in OrbitClock clock)
        {
            if (bannerWord == null) return;
            bool station = platform != null && platform.Exists;
            if (stationHero.gameObject.activeSelf != station) stationHero.gameObject.SetActive(station);
            if (stationEmptyCard.gameObject.activeSelf == station) stationEmptyCard.gameObject.SetActive(!station);
            if (stationBlueprint.gameObject.activeSelf != (station && miniFill[0] != null))
                stationBlueprint.gameObject.SetActive(station && miniFill[0] != null);
            if (stationHealth.gameObject.activeSelf != (station && tiles[0] != null))
                stationHealth.gameObject.SetActive(station && tiles[0] != null);
            if (stationLoop != null) WriteLoop(stationLoop, loop);
            if (!station) return;
            PlatformStats stats = platform.Stats(now);
            OrbitState state = platform.State(now, clock);
            RefreshStationPosition(platform, state, now, clock);
            if (miniFill[0] != null) RefreshMiniBlueprint(platform, stats, now);
            if (tiles[0] != null)
            {
                RefreshTiles(platform, stats, state, now);
                RefreshResources(platform, stats);
            }
        }

        private void RefreshStationPosition(OrbitalPlatform platform, in OrbitState state, double now, in OrbitClock clock)
        {
            OrbitRegime orbit = platform.Orbit;
            bannerBand.text = orbit.Name + " · " + TheaterGrid.Km(orbit.Altitude) + " KM · " +
                              orbit.InclinationDeg.ToString("0.0", Invariant) + "°";
            PlatformFitStep step = PlatformMissions.Next(platform, plan.Mission, now);
            bannerMission.text = step.Complete
                ? PlatformMissions.Name(plan.Mission) + " LOADOUT FITTED"
                : PlatformMissions.Name(plan.Mission) + " " + step.Fitted + "/" + step.Total + " · NEXT " + PlatformModules.Info(step.Module).Name;
            bannerMission.color = step.Complete ? AvTheme.RailReady : AvTheme.RailInfo;

            PlatformHold hold = platform.HoldAt(now);
            bool ready = hold == PlatformHold.None;
            Color tone = ready ? platform.Brownout ? AvTheme.RailDanger : AvTheme.RailReady : AvTheme.RailInfo;
            bannerRail.color = tone;
            bannerWord.text = ready ? platform.Brownout ? "POWER LOW" : "ON STATION" : PlatformWords.Hold(hold);
            bannerWord.color = tone;
            bannerClock.text = StationKeeping.Name(platform.PositionIndex) + (ready ? " · FIXED" : " · " + PlatformWords.Clock(platform.CycleStart - now));
            bannerClock.color = tone;
            bannerNote.text = platform.FittedOnline(ModuleKind.Propulsion, now) ? "PROPULSION FITTED · RELOCATE FROM STATION WALL" : "PERSISTENT COVERAGE · FIT PROPULSION TO MOVE";
            for (int i = 0; i < stationSectors.Length; i++)
                stationSectors[i].color = i == platform.PositionIndex ? tone.WithAlpha(0.3f) : AvTheme.SurfaceInert;
        }

        private void RefreshMiniBlueprint(OrbitalPlatform platform, in PlatformStats stats, double now)
        {
            trussNote.text = stats.Modules + "/" + OrbitalPlatform.CellCount + " CELLS · " + stats.Online + " ONLINE";
            for (int i = 0; i < OrbitalPlatform.CellCount; i++)
            {
                ModuleKind kind = platform.Cell(i);
                bool port = kind == ModuleKind.None && platform.CanAttach(i);
                bool pending = platform.Pending != ModuleKind.None && platform.Pending != ModuleKind.Cargo && platform.PendingCell == i;
                bool module = kind != ModuleKind.None || pending;
                ModuleKind shown = pending ? platform.Pending : kind;
                bool online = pending || kind == ModuleKind.None || platform.IsOnline(i, now);
                Color edge = !module ? (port ? AvTheme.Hairline : Color.clear)
                    : !online ? AvTheme.RailDanger : pending ? AvTheme.RailInfo : CategoryColour(PlatformModules.Info(shown).Category);
                miniFill[i].color = module ? AvTheme.Surface : port ? AvTheme.SurfaceInert.WithAlpha(0.5f) : Color.clear;
                for (int e = 0; e < miniEdge[i].Length; e++) miniEdge[i][e].color = edge;
                miniGlyph[i].enabled = module;
                if (module) miniGlyph[i].sprite = OpsSprites.Glyph((int)shown);
                miniGlyph[i].color = online ? AvTheme.TextPrimary : AvTheme.RailDanger;
                string code = module ? PlatformModules.Info(shown).Code + (pending ? " ···" : !online ? " OFF" : "") : port ? "+" : "";
                if (miniCode[i].text != code) miniCode[i].text = code;
                miniCode[i].color = module ? (online ? AvTheme.TextPrimary : AvTheme.RailDanger) : AvTheme.Dim;
            }
        }

        private void RefreshTiles(OrbitalPlatform platform, in PlatformStats stats, in OrbitState state, double now)
        {
            float charge = stats.StorageKj > 0f ? platform.Energy / stats.StorageKj : 0f;
            PaintTile(tiles[0],
                platform.Brownout ? "BROWNOUT" : charge < 0.25f ? "LOW " + Mathf.RoundToInt(charge * 100f) + "%"
                : stats.NetEclipseKw < 0f && state.InPass ? "ON CELLS" : "NOMINAL",
                platform.Brownout ? Tone.Danger : charge < 0.25f ? Tone.Armed : Tone.Ready);
            string hot = platform.RunsHot(ModuleKind.Emp, now) && platform.Fitted(ModuleKind.Emp) ? "EMP"
                : platform.RunsHot(ModuleKind.Reactor, now) && platform.Fitted(ModuleKind.Reactor) ? "RTG" : null;
            PaintTile(tiles[1], hot != null ? "HOT · " + hot : "NOMINAL", hot != null ? Tone.Armed : Tone.Ready);
            if (stats.FuelCapacity <= 0f) PaintTile(tiles[2], "NO TANKS", Tone.Locked);
            else
            {
                float fuel = platform.Fuel / stats.FuelCapacity;
                string drag = platform.Orbit.DragFuelPerSecond > 0f ? " · DRAG" : "";
                PaintTile(tiles[2], platform.Fuel <= 0.5f ? "DRY" : Mathf.RoundToInt(fuel * 100f) + "%" + drag,
                    platform.Fuel <= 0.5f ? Tone.Danger : fuel < 0.25f ? Tone.Armed : Tone.Ready);
            }
            PlatformHold hold = platform.HoldAt(now);
            PaintTile(tiles[3], hold != PlatformHold.None ? "HOLD" : "LINKED",
                hold != PlatformHold.None ? Tone.Pending : Tone.Ready);
            PaintTile(tiles[4], stats.Crewed ? "3 ABOARD" : "UNCREWED", stats.Crewed ? Tone.Ready : Tone.Locked);
            int down = -1;
            for (int i = 0; i < OrbitalPlatform.CellCount && down < 0; i++)
                if (platform.Cell(i) != ModuleKind.None && !platform.IsOnline(i, now)) down = i;
            bool deflected = platform.Notice == PlatformNotice.DebrisDeflected;
            PaintTile(tiles[5], down >= 0 ? "HIT · " + PlatformModules.Info(platform.Cell(down)).Code : deflected ? "DEFLECTED" : "CLEAR",
                down >= 0 ? Tone.Danger : deflected ? Tone.Pending : Tone.Ready);
            PaintTile(tiles[6], platform.Orbit.Code + (hold == PlatformHold.None ? "" : " · BURN"),
                hold == PlatformHold.None ? Tone.Ready : Tone.Pending);
            PaintTile(tiles[7], stats.Online + "/" + stats.Modules + " ONLINE", stats.Online < stats.Modules ? Tone.Danger : Tone.Ready);
        }

        private void RefreshResources(OrbitalPlatform platform, in PlatformStats stats)
        {
            float energy = stats.StorageKj > 0f ? Mathf.Clamp01(platform.Energy / stats.StorageKj) : 0f;
            float fuel = stats.FuelCapacity > 0f ? Mathf.Clamp01(platform.Fuel / stats.FuelCapacity) : 0f;
            energyBar.rectTransform.sizeDelta = new Vector2(resourceBarWidth * energy, 4f);
            energyBar.color = platform.Brownout ? AvTheme.RailDanger : AvTheme.RailReady;
            fuelBar.rectTransform.sizeDelta = new Vector2(resourceBarWidth * fuel, 4f);
            resourceLine.text = "ENERGY " + PlatformWords.Whole(platform.Energy) + "/" + PlatformWords.Whole(stats.StorageKj) + " KJ · SUN " +
                                PlatformWords.Kilowatts(stats.NetSunKw) + " · DARK " + PlatformWords.Kilowatts(stats.NetEclipseKw);
            rodsLine.text = (stats.FuelCapacity > 0f ? "FUEL " + PlatformWords.Whole(platform.Fuel) : "NO TANKS") + " · " +
                            (stats.RodCapacity > 0 ? "RODS " + platform.Rods + "/" + stats.RodCapacity : "NO RODS") + " · " +
                            PlatformWords.Tonnes(stats.Mass) + "/" + PlatformWords.Tonnes(OrbitalPlatform.MassLimit);
        }
    }
}

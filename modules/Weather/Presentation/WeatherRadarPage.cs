using System;
using System.Globalization;
using System.Text;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Runtime;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.SavedMission;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Weather.Presentation
{
    /// <summary>
    /// The WEA panel's radar page: a north-up reflectivity picture of the whole map, painted from
    /// the deterministic storm field and the front. The page is model output and says so once,
    /// quietly; it is never dressed as a game radar return.
    ///
    /// <para>The texture is rebuilt on a 1 Hz budget, and immediately when the storm mode, the
    /// front's step or a layer/zoom/time control changes — never per frame. One texture, one pixel
    /// buffer and one <see cref="RadarImage"/> grid are reused for the whole scene; every marker is
    /// moved rather than rebuilt, and every label is written through a reused
    /// <see cref="StringBuilder"/>.</para>
    ///
    /// <para>The time scrub is exact rather than animated: the schedule is a pure function of
    /// (seed, mission time), so +30 and +60 are the same closed-form sample the manager's own
    /// forecast takes, and the boundary is translated along its own normal.</para>
    /// </summary>
    internal sealed class WeatherRadarPage
    {
        // ---- layers
        private const int LayerMap = 1;
        private const int LayerEcho = 2;
        private const int LayerWind = 4;
        private const int LayerTemp = 8;
        private const int LayerDefault = LayerMap | LayerEcho;

        /// <summary>Owner id for the shared armed map gesture. Not a MapPicker constant: this one is ours.</summary>
        private const string PickOwner = "boscali.weather.radar";

        /// <summary>The scope's old range ladder, now the half-height of the window in metres.</summary>
        private static readonly float[] RangeMetres = { 20000f, 40000f, 80000f, 160000f };
        private const int DefaultRangeIndex = 1;

        private static readonly float[] ForecastOffsets = { 0f, 1800f, 3600f };
        private static readonly string[] ForecastLabels = { "NOW", "+30", "+60" };

        private static readonly string[] LayerLabels = { "MAP", "ECHO", "WIND", "TEMP" };
        private static readonly int[] LayerFlags = { LayerMap, LayerEcho, LayerWind, LayerTemp };

        /// <summary>The two rings, as fractions of the window's half-height, so they scale with the zoom.</summary>
        private static readonly float[] RingFractions = { 0.5f, 1f };

        private const float HeaderHeight = 16f;
        private const float ReadoutHeight = 13f;
        private const float MapHeightRatio = 0.5f;
        private const float MinMapHeight = 140f;
        private const float MaxMapHeight = 260f;
        private const float MapGap = 6f;
        private const float LegendHeight = 18f;
        private const float ControlHeight = 22f;
        private const float ControlGap = 4f;
        private const float SectionHeight = 14f;
        private const float TableHeaderHeight = 12f;
        private const float RowHeight = 16f;
        private const float RowPitch = 16f;
        private const float RailWidth = 3f;
        private const float RowInset = 10f;
        private const float NoteHeight = 12f;

        /// <summary>Repaint cadence. The picture is 1 Hz; the markers ride the panel's own tick.</summary>
        private const float RebuildInterval = 1f;

        /// <summary>How far the boundary has to move before the picture is worth repainting.</summary>
        private const float FrontStepMetres = 1500f;

        private const float MinPipPixels = 5f;
        private const float PipAlphaMin = 0.35f;
        private const float MarkerPixels = 3f;
        private const float OwnshipPixels = 7f;
        private const float SelectionPixels = 15f;
        private const float TrackPixelsMax = 46f;
        private const float TrackSeconds = 60f;
        private const float TrackThickness = 1.5f;

        private const float FrontThickness = 2f;
        private const float FrontArrowOffset = 12f;
        private const float FrontGlyphWidth = 40f;
        private const float FrontLabelWidth = 150f;
        private const int MaxFrontGlyphs = 3;

        private const float WindArrowPixels = 28f;
        private const float WindArrowMargin = 42f;
        private const float PickSlopPixels = 6f;
        private const float PickTimeout = 25f;

        private const int MaxAirfields = 16;
        private const int MaxAirfieldScan = 64;
        private const int MaxWaypoints = 16;
        private const int Columns = 7;

        private const int RingSpriteSize = 128;
        private const float RingSpriteThickness = 1.5f;
        private const int DotSpriteSize = 64;
        private const int ArrowSpriteSize = 32;

        private static readonly Color32 ClearPixel = new Color32(0, 0, 0, 0);
        private static readonly Color32 RampLight = new Color32(77, 199, 92, 190);
        private static readonly Color32 RampModerate = new Color32(237, 219, 61, 220);
        private static readonly Color32 RampHeavy = new Color32(242, 148, 38, 235);
        private static readonly Color32 RampIntense = new Color32(230, 66, 46, 245);
        private static readonly Color32 RampExtreme = new Color32(219, 82, 242, 255);

        /// <summary>
        /// One reflectivity band: the threshold, the colour and the label together, so the picture
        /// and the legend cannot drift apart and severity never rides on colour alone.
        /// </summary>
        private readonly struct Band
        {
            public readonly float From;
            public readonly Color32 Colour;
            public readonly string Label;

            public Band(float from, Color32 colour, string label)
            {
                From = from;
                Colour = colour;
                Label = label;
            }
        }

        private static readonly Band[] Bands =
        {
            new Band(RadarImage.NoEcho, RampLight, "8-30 LIGHT"),
            new Band(0.30f, RampModerate, "30-50 MOD"),
            new Band(0.50f, RampHeavy, "50-68 HEAVY"),
            new Band(0.68f, RampIntense, "68-85 INTENSE"),
            new Band(0.85f, RampExtreme, "85-100% EXTREME"),
        };

        private static readonly string[] ColumnKeys = { "KIND", "TOP", "BASE", "RAD", "VECTOR M/S", "WARN", "RNG / BRG" };
        private static readonly float[] ColumnWidths = { 78f, 54f, 54f, 48f, 66f, 52f, 74f };

        private const string MarkerLegend =
            "FRONT ▲ COLD ◗ WARM ▲◗ OCD △ DRY · AIRFIELD AND WAYPOINT PIPS · DOT IS YOU";

        private readonly WeatherSettings settings;
        private readonly StringBuilder text = new StringBuilder(96);
        private readonly RadarImage radarImage = new RadarImage();
        private readonly StormCell[] forecastCells = new StormCell[StormField.MaxCells];
        private readonly StormCell[] oneCell = new StormCell[1];

        private readonly Image[] cells = new Image[StormField.MaxCells];
        private readonly Image[] rails = new Image[StormField.MaxCells];
        private readonly TMP_Text[] table = new TMP_Text[StormField.MaxCells * Columns];
        private readonly Image[] airfields = new Image[MaxAirfields];
        private readonly Image[] waypoints = new Image[MaxWaypoints];
        private readonly Image[] ringImages = new Image[RingFractions.Length];
        private readonly TMP_Text[] ringLabels = new TMP_Text[RingFractions.Length];
        private readonly TMP_Text[] glyphs = new TMP_Text[MaxFrontGlyphs];
        private readonly AvButton[] layerButtons = new AvButton[LayerFlags.Length];

        private RectTransform mapRoot;
        private Rect mapArea;
        private Image mapImage;
        private RawImage echoImage;
        private Image frontLine;
        private Image frontArrow;
        private Image windArrow;
        private Image ownship;
        private Image trackLine;
        private Image selection;
        private TMP_Text frontEta;
        private TMP_Text windValue;
        private TMP_Text tempValue;
        private TMP_Text stabilityValue;
        private TMP_Text ownValue;
        private TMP_Text refValue;
        private TMP_Text headerNote;
        private TMP_Text sectionNote;
        private TMP_Text noteLine;
        private AvButton zoomButton;
        private AvButton forecastButton;
        private AvButton pickButton;
        private Sprite ringSprite;
        private Sprite dotSprite;
        private Sprite arrowSprite;

        private Texture2D texture;
        private Color32[] pixels;

        private int layers = LayerDefault;
        private int rangeIndex = DefaultRangeIndex;
        private int forecastIndex;
        private int paintedStormMode = -1;
        private int paintedFrontKey = int.MinValue;
        private int paintedViewKey = int.MinValue;
        private int paintedCount;

        private WeatherSnapshot lastSnapshot = WeatherSnapshot.Unavailable;
        private WeatherFront paintedFront = WeatherFront.None;
        private float lastPlayerX;
        private float lastPlayerZ;
        private float nextPaintAt;
        private bool painted;
        private bool forecastView;

        private float rangeMetres = RangeMetres[DefaultRangeIndex];
        private float halfX;
        private float halfZ = RangeMetres[DefaultRangeIndex];
        private float pixelsPerMetre = 1f;

        private bool hasTrack;
        private float trackVx;
        private float trackVz;

        private bool hasSelection;
        private float selectionX;
        private float selectionZ;

        private float pressX = float.NaN;
        private float pressY = float.NaN;
        private float armedAt;
        private bool armed;
        private string echoText;
        private float echoUntil;

        private string shownHeader;
        private string shownSection;
        private string shownNote;

        private int seed;
        private Mission seedMission;
        private string seedMapName;
        private bool seedValid;

        public WeatherRadarPage(RectTransform parent, float x, float y, float width, WeatherSettings settings)
        {
            this.settings = settings;
            rangeIndex = InitialRangeIndex(settings);
            rangeMetres = RangeMetres[rangeIndex];
            halfZ = rangeMetres;
        }

        // ---- Build -----------------------------------------------------------------------

        /// <summary>Draws the whole page inside <paramref name="width"/> and returns the bottom y.</summary>
        public float Build(RectTransform parent, float x, float y, float width)
        {
            if (parent == null) return y;

            AvStyled.Label(parent, new Rect(x, y, width * 0.5f, 14f), "WEATHER RADAR", "section-title");
            headerNote = AvStyled.Label(parent, new Rect(x + width * 0.5f, y, width * 0.5f, 14f), "",
                "section-title-note", align: TextAlignmentOptions.MidlineRight);
            y -= HeaderHeight;

            ownValue = AvStyled.Label(parent, new Rect(x, y, width, ReadoutHeight), "", "row-sub");
            y -= ReadoutHeight;
            refValue = AvStyled.Label(parent, new Rect(x, y, width, ReadoutHeight), "", "row-sub");
            y -= ReadoutHeight;

            BuildMap(parent, x, y, width);
            y -= mapArea.height + MapGap;

            BuildLegend(parent, x, y, width);
            y -= LegendHeight + MapGap;

            BuildControls(parent, x, y, width);
            y -= ControlHeight * 2f + ControlGap + MapGap;

            BuildTable(parent, x, y, width);
            y -= SectionHeight + TableHeaderHeight + StormField.MaxCells * RowPitch + NoteHeight;

            paintedViewKey = int.MinValue;
            return y;
        }

        private void BuildMap(RectTransform parent, float x, float y, float width)
        {
            float height = Mathf.Clamp(width * MapHeightRatio, MinMapHeight, MaxMapHeight);
            mapArea = new Rect(x, y - height, width, height);

            ringSprite = CreateRingSprite();
            dotSprite = CreateDotSprite();
            arrowSprite = CreateArrowSprite();

            // A mask, because the map raster is drawn at its true world size and a window slightly
            // smaller than the map would otherwise bleed terrain past the frame.
            var root = new GameObject("WeatherRadarMap", typeof(RectTransform), typeof(RectMask2D));
            mapRoot = (RectTransform)root.transform;
            mapRoot.SetParent(parent, false);
            AvKit.Place(mapRoot, mapArea);

            // A dark bed under everything: the map raster is clipped to its own extent, so the
            // corners of a zoomed-out window read as empty water rather than as the panel behind.
            AvKit.Panel(mapRoot, new Rect(0f, 0f, mapArea.width, mapArea.height), AvTheme.Ground);

            var mapObject = new GameObject("RadarTerrain", typeof(RectTransform), typeof(Image));
            var mapRect = (RectTransform)mapObject.transform;
            mapRect.SetParent(mapRoot, false);
            mapImage = mapObject.GetComponent<Image>();
            mapImage.raycastTarget = false;
            mapImage.preserveAspect = false;

            var echoObject = new GameObject("RadarEcho", typeof(RectTransform), typeof(RawImage));
            var echoRect = (RectTransform)echoObject.transform;
            echoRect.SetParent(mapRoot, false);
            echoRect.anchorMin = Vector2.zero;
            echoRect.anchorMax = Vector2.one;
            echoRect.offsetMin = Vector2.zero;
            echoRect.offsetMax = Vector2.zero;
            echoImage = echoObject.GetComponent<RawImage>();
            echoImage.raycastTarget = false;
            echoImage.color = Color.white;

            for (int i = 0; i < ringImages.Length; i++)
            {
                ringImages[i] = Marker(mapRoot, ringSprite, AvTheme.Hairline);
                ringLabels[i] = AvStyled.Label(mapRoot, new Rect(0f, 0f, 90f, 11f), "", "section-title-note",
                    align: TextAlignmentOptions.Center);
            }

            for (int i = 0; i < airfields.Length; i++) airfields[i] = Marker(mapRoot, dotSprite, AvTheme.TextPrimary);
            for (int i = 0; i < waypoints.Length; i++) waypoints[i] = Marker(mapRoot, dotSprite, AvTheme.RailInfo);
            for (int i = 0; i < cells.Length; i++) cells[i] = Marker(mapRoot, dotSprite, AvTheme.RailInert);

            frontLine = Bar(mapRoot, AvTheme.RailDanger);
            frontArrow = Marker(mapRoot, arrowSprite, AvTheme.Accent);
            windArrow = Marker(mapRoot, arrowSprite, AvTheme.RailInfo);
            for (int i = 0; i < glyphs.Length; i++)
            {
                glyphs[i] = AvStyled.Label(mapRoot, new Rect(0f, 0f, FrontGlyphWidth, 12f), "", "row-sub",
                    align: TextAlignmentOptions.Center);
            }

            trackLine = Bar(mapRoot, AvTheme.Accent);
            ownship = Marker(mapRoot, dotSprite, AvTheme.Accent);
            selection = Marker(mapRoot, ringSprite, AvTheme.RailInfo);
            frontEta = AvStyled.Label(mapRoot, new Rect(0f, 0f, FrontLabelWidth, 12f), "", "row-sub",
                align: TextAlignmentOptions.Center);

            AvStyled.Label(mapRoot, new Rect(5f, 12f, 70f, 11f), "SYNTHETIC", "section-title-note");
            windValue = AvStyled.Label(mapRoot, new Rect(mapArea.width - 150f, 12f, 144f, 12f), "", "row-value");
            tempValue = AvStyled.Label(mapRoot, new Rect(5f, -(mapArea.height - 4f), mapArea.width - 12f, 11f),
                "", "row-sub");
            stabilityValue = AvStyled.Label(mapRoot, new Rect(5f, -(mapArea.height - 14f), mapArea.width - 12f, 11f),
                "", "row-sub");

            HideAirfields(0);
            HideWaypoints(0);
            for (int i = 0; i < cells.Length; i++) SetActive(cells[i], false);
            SetActive(frontLine, false);
            SetActive(frontArrow, false);
            SetActive(windArrow, false);
            SetActive(ownship, false);
            SetActive(trackLine, false);
            SetActive(selection, false);
            SetActive(frontEta, false);
            SetActive(windValue, false);
            SetActive(tempValue, false);
            SetActive(stabilityValue, false);

            AvKit.Outline(parent, mapArea, AvTheme.Hairline);
            AvKit.CornerTicks(parent, mapArea, AvTheme.Hairline);
        }

        private void BuildLegend(RectTransform parent, float x, float y, float width)
        {
            float cell = width / Bands.Length;
            for (int i = 0; i < Bands.Length; i++)
            {
                float cellX = x + i * cell;
                AvKit.Panel(parent, new Rect(cellX, y, Mathf.Max(1f, cell - 3f), 7f), Bands[i].Colour);
                AvStyled.Label(parent, new Rect(cellX, y - 7f, cell, 11f), Bands[i].Label, "section-title-note");
            }
        }

        private void BuildControls(RectTransform parent, float x, float y, float width)
        {
            float toggleWidth = (width - ControlGap * (LayerFlags.Length - 1)) / LayerFlags.Length;
            for (int i = 0; i < LayerFlags.Length; i++)
            {
                int flag = LayerFlags[i];
                layerButtons[i] = AvStyled.Button(
                    parent, new Rect(x + i * (toggleWidth + ControlGap), y, toggleWidth, ControlHeight),
                    LayerLabels[i], "btn", () => ToggleLayer(flag), AvButtonStyle.Toggle);
                layerButtons[i].WithTooltip(LayerTooltip(i));
            }

            float buttonY = y - ControlHeight - ControlGap;
            float buttonWidth = (width - ControlGap * 2f) / 3f;
            zoomButton = AvStyled.Button(parent, new Rect(x, buttonY, buttonWidth, ControlHeight), "", "btn", CycleRange)
                .WithTooltip("Cycle the window: the half-height of the picture in kilometres. " +
                             "Weather beyond the map edge still paints.");
            forecastButton = AvStyled.Button(
                parent, new Rect(x + buttonWidth + ControlGap, buttonY, buttonWidth, ControlHeight), "", "btn", CycleForecast)
                .WithTooltip("Deterministic forecast: the same pure schedule sampled at +30 and +60 minutes. " +
                             "Nothing is simulated, nothing is synced.");
            pickButton = AvStyled.Button(
                parent, new Rect(x + (buttonWidth + ControlGap) * 2f, buttonY, buttonWidth, ControlHeight), "", "btn", TogglePick)
                .WithTooltip("Arm a map click for a reference point: bearing and range from you, on the status panel strip.");
            SetPickLabel(false);
        }

        private void BuildTable(RectTransform parent, float x, float y, float width)
        {
            AvStyled.Label(parent, new Rect(x, y, width * 0.5f, 14f), "CELLS", "section-title");
            sectionNote = AvStyled.Label(parent, new Rect(x + width * 0.5f, y, width * 0.5f, 14f), "",
                "section-title-note", align: TextAlignmentOptions.MidlineRight);
            y -= SectionHeight;

            float columnX = x + RowInset;
            for (int i = 0; i < Columns; i++)
            {
                AvStyled.Label(parent, new Rect(columnX, y, ColumnWidths[i], TableHeaderHeight), ColumnKeys[i],
                    "section-title-note");
                columnX += ColumnWidths[i];
            }
            y -= TableHeaderHeight;

            for (int i = 0; i < StormField.MaxCells; i++)
            {
                float rowY = y - i * RowPitch;
                rails[i] = AvStyled.Rail(parent, new Rect(x, rowY, RailWidth, RowHeight), "locked");
                columnX = x + RowInset;
                for (int c = 0; c < Columns; c++)
                {
                    table[i * Columns + c] = AvStyled.Label(
                        parent, new Rect(columnX, rowY, ColumnWidths[c], RowHeight), "", "row-sub");
                    columnX += ColumnWidths[c];
                }
                HideRow(i);
            }
            y -= StormField.MaxCells * RowPitch;

            noteLine = AvStyled.Label(parent, new Rect(x, y + 2f, width, NoteHeight), "", "section-title-note");
        }

        private static string LayerTooltip(int index)
        {
            switch (index)
            {
                case 1: return "Paint the model's reflectivity field over the map. Every echo is derived, not received.";
                case 2: return "Wind and gust at your aircraft, from the atmosphere model.";
                case 3: return "Temperature, dewpoint and stability from the atmosphere model.";
                default: return "Draw the mission map under the picture: coastline, terrain and airfields.";
            }
        }

        // ---- Refresh ---------------------------------------------------------------------

        public void Refresh(WeatherSnapshot snapshot, WeatherManager manager, float playerX, float playerZ)
        {
            if (mapRoot == null || manager == null) return;

            lastSnapshot = snapshot;
            lastPlayerX = playerX;
            lastPlayerZ = playerZ;

            if (armed && Time.unscaledTime - armedAt > PickTimeout) Disarm();
            ConsumePick();
            BindOwnship();
            BindReadouts(snapshot, manager);

            int viewKey = ViewKey();
            if (Time.unscaledTime < nextPaintAt &&
                (int)snapshot.StormMode == paintedStormMode &&
                FrontKey(snapshot.Front) == paintedFrontKey &&
                viewKey == paintedViewKey)
            {
                return;
            }

            nextPaintAt = Time.unscaledTime + RebuildInterval;
            Paint(snapshot, manager, viewKey);
        }

        /// <summary>
        /// Rebuild the picture at the painted instant, then move every marker that belongs to it.
        /// The table is bound here too: it describes the picture, so the two cannot disagree.
        /// </summary>
        private void Paint(WeatherSnapshot snapshot, WeatherManager manager, int viewKey)
        {
            Vector2 span = TheaterFrame.Resolve();
            float mapSize = MapSize();
            forecastView = forecastIndex > 0;

            halfZ = rangeMetres;
            halfX = halfZ * (mapArea.width / Mathf.Max(1f, mapArea.height));
            pixelsPerMetre = mapArea.height / (2f * halfZ);

            StormCell[] source = snapshot.Cells;
            int count = ClampCount(snapshot);
            WeatherFront front = snapshot.Front;
            if (forecastView && mapSize > 0f)
            {
                count = ForecastField(snapshot, manager, mapSize, out front);
                source = forecastCells;
            }
            if (source == null || count <= 0)
            {
                count = 0;
                source = forecastCells;
            }

            paintedFront = front;
            paintedCount = count;
            paintedStormMode = (int)snapshot.StormMode;
            paintedFrontKey = FrontKey(snapshot.Front);
            paintedViewKey = viewKey;
            painted = true;

            EnsureTexture();
            radarImage.Sample(source, count, in front, halfX, halfZ);
            RampIntoTexture();

            BindMapSprite(span);
            BindRanges();
            BindCells(source, count);
            BindFront(front);
            BindAirfields();
            BindWaypoints();
            BindTable(source, count);
            BindHeader(count);
            BindLayers();
        }

        /// <summary>
        /// The storm population at the scrubbed instant. Storm positions and the boundary's travel
        /// are exact — the same closed-form functions the live field uses — and the seed is the one
        /// <c>WeatherManager</c> derives from the mission identity, so the forecast picture is what
        /// the host flies through rather than a guess in the same colours. The air mass is held at
        /// its live sample: the schedule's own atmosphere at a future instant is not published on
        /// the snapshot, so this is the one term the scrub does not re-derive.
        /// </summary>
        private int ForecastField(
            WeatherSnapshot snapshot, WeatherManager manager, float mapSize, out WeatherFront front)
        {
            float offset = ForecastOffsets[forecastIndex];
            float at = manager.MissionTime + offset;
            int missionSeed = SeedForMission();
            WeatherState state = WeatherModel.Sample(missionSeed, at);
            front = Advance(snapshot.Front, offset);

            return StormField.Fill(
                forecastCells, missionSeed, at, mapSize,
                state.Conditions, state.WindHeading, state.WindSpeed,
                front, snapshot.Atmosphere, out _);
        }

        /// <summary>A boundary at a future instant: the model's front keeps its normal and its speed.</summary>
        private static WeatherFront Advance(in WeatherFront front, float seconds)
        {
            if (!front.Present || front.Speed <= 0f) return front;
            return new WeatherFront(
                front.Present, front.Kind, front.NormalX, front.NormalZ,
                front.Position + front.Speed * seconds, front.Speed, front.Width, front.Activity,
                front.Behind, front.Ahead);
        }

        /// <summary>
        /// The mission seed, cached against the mission object and the map's name. This is the
        /// identity <c>WeatherManager.MissionIdentity</c> derives its own seed from; keep the two
        /// in step.
        /// </summary>
        private int SeedForMission()
        {
            Mission mission = MissionManager.CurrentMission;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings map = level != null ? level.LoadedMapSettings : null;
            string name = map != null ? map.name : null;

            if (seedValid && ReferenceEquals(mission, seedMission) &&
                string.Equals(name, seedMapName, StringComparison.Ordinal))
            {
                return seed;
            }

            seedMission = mission;
            seedMapName = name;
            seedValid = true;
            if (mission != null && !string.IsNullOrEmpty(mission.Name)) seed = WeatherModel.Seed(mission.Name);
            else seed = WeatherModel.Seed(name != null ? "map:" + name : "unknown");
            return seed;
        }

        /// <summary>The field's own map size in metres, rescaled exactly as the manager rescales it.</summary>
        private static float MapSize()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            float size = level != null ? level.mapSize : 0f;
            if (float.IsNaN(size) || float.IsInfinity(size) || size <= 0f) return 0f;
            if (size < 1000f) size *= 1000f;
            return size;
        }

        private static int ClampCount(in WeatherSnapshot snapshot)
        {
            StormCell[] source = snapshot.Cells;
            if (source == null || snapshot.CellCount <= 0) return 0;
            int count = snapshot.CellCount;
            if (count > source.Length) count = source.Length;
            return count > StormField.MaxCells ? StormField.MaxCells : count;
        }

        private int ViewKey() => ((layers * 4 + rangeIndex) * 3) + forecastIndex;

        private static int FrontKey(in WeatherFront front)
        {
            if (!front.Present) return 0;
            return ((int)front.Kind + 1) * 1000000 + (int)(front.Position / FrontStepMetres);
        }

        // ---- Map context -----------------------------------------------------------------

        private void BindMapSprite(Vector2 span)
        {
            bool visible = (layers & LayerMap) != 0;
            if (!visible)
            {
                SetActive(mapImage, false);
                return;
            }

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            Sprite sprite = null;
            if (map != null && map.mapImage != null)
            {
                Image source = map.mapImage.GetComponent<Image>();
                if (source != null) sprite = source.sprite;
            }

            // No raster for this mission: the map layer has nothing honest to draw.
            if (sprite == null || Mathf.Abs(span.x) <= 1000f || Mathf.Abs(span.y) <= 1000f)
            {
                mapImage.sprite = null;
                SetActive(mapImage, false);
                return;
            }

            float width = span.x * pixelsPerMetre;
            float height = span.y * pixelsPerMetre;
            mapImage.sprite = sprite;
            mapImage.color = Color.white;
            AvKit.Place(mapImage.rectTransform,
                new Rect((mapArea.width - width) * 0.5f, -(mapArea.height - height) * 0.5f, width, height));
            SetActive(mapImage, true);
        }

        private void BindAirfields()
        {
            int placed = 0;
            if ((layers & LayerMap) != 0 && mapImage.sprite != null)
            {
                var lookup = FactionRegistry.airbaseLookup;
                if (lookup != null)
                {
                    try
                    {
                        int scanned = 0;
                        foreach (Airbase airbase in lookup.Values)
                        {
                            if (++scanned > MaxAirfieldScan || placed >= MaxAirfields) break;
                            if (airbase == null) continue;

                            Transform anchor = airbase.center != null ? airbase.center : airbase.transform;
                            if (anchor == null) continue;
                            Vector3 position = anchor.position;
                            if (PlaceMarker(airfields[placed], position.x, position.z, MarkerPixels)) placed++;
                        }
                    }
                    catch (Exception)
                    {
                        // A base registered mid-scan is not a reason to lose the picture.
                        placed = 0;
                    }
                }
            }
            HideAirfields(placed);
        }

        private void HideAirfields(int from)
        {
            for (int i = from; i < airfields.Length; i++) SetActive(airfields[i], false);
        }

        private void BindWaypoints()
        {
            int placed = 0;
            if ((layers & LayerMap) != 0)
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null && map.waypoints != null)
                {
                    try
                    {
                        int count = Mathf.Min(map.waypoints.Count, MaxWaypoints);
                        for (int i = 0; i < count; i++)
                        {
                            MapWaypoint waypoint = map.waypoints[i];
                            if (waypoint == null) continue;
                            if (PlaceMarker(waypoints[placed], waypoint.waypointPosition.x,
                                    waypoint.waypointPosition.z, MarkerPixels))
                            {
                                placed++;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // A plan replaced mid-scan is not a reason to lose the picture.
                        placed = 0;
                    }
                }
            }
            HideWaypoints(placed);
        }

        private void HideWaypoints(int from)
        {
            for (int i = from; i < waypoints.Length; i++) SetActive(waypoints[i], false);
        }

        // ---- Picture ---------------------------------------------------------------------

        private void EnsureTexture()
        {
            float scale = Mathf.Min(1f,
                RadarImage.MaxResolution / Mathf.Max(1f, Mathf.Max(mapArea.width, mapArea.height)));
            int width = Mathf.Max(RadarImage.MinResolution, Mathf.RoundToInt(mapArea.width * scale));
            int height = Mathf.Max(RadarImage.MinResolution, Mathf.RoundToInt(mapArea.height * scale));
            if (!radarImage.Resize(width, height)) return;

            int count = radarImage.Width * radarImage.Height;
            bool sized = texture != null && texture.width == radarImage.Width && texture.height == radarImage.Height;
            if (sized)
            {
                if (pixels == null || pixels.Length != count) pixels = new Color32[count];
                return;
            }

            // Release before allocating: ReleaseTexture nulls both fields, so allocating first
            // left the fresh texture uploaded with no pixel buffer until the next paint.
            ReleaseTexture();
            pixels = new Color32[count];
            texture = new Texture2D(radarImage.Width, radarImage.Height, TextureFormat.RGBA32, mipChain: false)
            {
                name = "BoscaliWeather.Radar",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            echoImage.texture = texture;
        }

        private void RampIntoTexture()
        {
            if (texture == null || pixels == null || pixels.Length != texture.width * texture.height) return;
            float[] values = radarImage.Values;
            if (values == null || values.Length != pixels.Length) return;

            // Unity textures are bottom-up and the grid is chart-ordered top-down, so the ramp
            // copies rows flipped: row 0 of the image is the north edge of the window.
            int width = texture.width;
            int height = texture.height;
            for (int row = 0; row < height; row++)
            {
                int source = row * width;
                int target = (height - 1 - row) * width;
                for (int column = 0; column < width; column++) pixels[target + column] = Ramp(values[source + column]);
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        private static Color32 Ramp(float value)
        {
            if (float.IsNaN(value) || value < RadarImage.NoEcho) return ClearPixel;
            for (int i = Bands.Length - 1; i > 0; i--)
            {
                if (value >= Bands[i].From) return Bands[i].Colour;
            }
            return Bands[0].Colour;
        }

        private void BindRanges()
        {
            Vector2 centre = MapPoint(0f, 0f, out _);
            for (int i = 0; i < ringImages.Length; i++)
            {
                float distance = halfZ * RingFractions[i];
                float diameter = distance * 2f * pixelsPerMetre;
                bool inside = diameter <= mapArea.width * 1.5f && diameter <= mapArea.height * 1.5f;
                SetActive(ringImages[i], inside);
                SetActive(ringLabels[i], inside);
                if (!inside) continue;

                PlaceRotated(ringImages[i].rectTransform, centre, diameter, diameter, 0f);
                text.Length = 0;
                text.Append(StormReadout.NauticalMiles(distance));
                text.Append(" / ");
                text.Append(Kilometres(distance));
                text.Append(" KM");
                ringLabels[i].SetText(text);
                PlaceRotated(ringLabels[i].rectTransform, ClampIntoArea(MapPoint(0f, distance, out _), 6f), 90f, 11f, 0f);
            }
        }

        private void BindCells(StormCell[] source, int count)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (i >= count)
                {
                    SetActive(cells[i], false);
                    continue;
                }

                StormCell cell = source[i];
                Vector2 point = MapPoint(cell.X, cell.Z, out bool inside);
                if (!inside)
                {
                    SetActive(cells[i], false);
                    continue;
                }

                oneCell[0] = cell;
                float peak = RadarImage.ReflectivityAt(oneCell, 1, WeatherFront.None, cell.X, cell.Z);
                Color32 colour = Ramp(peak);
                colour.a = (byte)Mathf.RoundToInt(255f * Mathf.Lerp(PipAlphaMin, 1f, Mathf.Clamp01(peak)));

                float diameter = Mathf.Max(MinPipPixels, cell.Radius * 2f * pixelsPerMetre);
                PlaceRotated(cells[i].rectTransform, point, diameter, diameter, 0f);
                cells[i].color = colour;
                SetActive(cells[i], true);
            }
        }

        private void BindFront(WeatherFront front)
        {
            bool visible = front.Present && (layers & LayerEcho) != 0;
            SetActive(frontLine, false);
            SetActive(frontArrow, visible);
            SetActive(frontEta, visible);
            for (int i = 0; i < glyphs.Length; i++) SetActive(glyphs[i], false);
            if (!visible) return;

            Vector2 normal = new Vector2(front.NormalX, front.NormalZ);
            Vector2 centre = MapPoint(0f, 0f, out _);
            Vector2 anchor = centre + normal * (front.Position * pixelsPerMetre);
            Vector2 direction = new Vector2(-normal.y, normal.x);

            if (!ClipToArea(anchor, direction, out Vector2 start, out Vector2 end)) return;

            Vector2 mid = (start + end) * 0.5f;
            float length = Vector2.Distance(start, end);
            PlaceRotated(frontLine.rectTransform, mid, length, FrontThickness,
                PixelAngle(-front.NormalZ, front.NormalX));
            frontLine.color = AvTheme.Unity(AvTokens.RailDanger.WithAlpha(0.85f));
            SetActive(frontLine, true);

            for (int i = 0; i < glyphs.Length; i++)
            {
                glyphs[i].SetText(FrontKinds.Glyph(front.Kind));
                PlaceRotated(glyphs[i].rectTransform,
                    Vector2.Lerp(start, end, (i + 1f) / (glyphs.Length + 1f)), FrontGlyphWidth, 12f, 0f);
                SetActive(glyphs[i], true);
            }

            float signed = front.SignedDistanceTo(lastPlayerX, lastPlayerZ);
            Vector2 nearest = MapPoint(
                lastPlayerX - front.NormalX * signed, lastPlayerZ - front.NormalZ * signed, out bool near);
            if (!near) nearest = ClampIntoArea(nearest, 14f);
            PlaceRotated(frontArrow.rectTransform, nearest + normal * FrontArrowOffset, 15f, 15f,
                PixelAngle(front.NormalX, front.NormalZ));

            text.Length = 0;
            text.Append(FrontKinds.Label(front.Kind));
            text.Append(" · ETA ");
            text.Append(WeatherReadout.InSeconds(front.SecondsUntil(lastPlayerX, lastPlayerZ)));
            frontEta.SetText(text);
            PlaceRotated(frontEta.rectTransform, nearest - normal * (FrontArrowOffset + 6f), FrontLabelWidth, 12f, 0f);
            SetActive(frontEta, true);
        }

        private void BindTable(StormCell[] source, int count)
        {
            if (!lastSnapshot.Available)
            {
                ShowNote("NO READOUT — NO WEATHER SCHEDULE ON THIS MISSION");
                return;
            }
            if (count <= 0)
            {
                ShowNote(forecastView
                    ? "NO ECHOES IN THE " + ForecastLabels[forecastIndex] + " MIN FORECAST — THE SCHEDULE RAISES NONE"
                    : "NO ECHOES — THE SCHEDULE RAISES NO CELLS IN THIS FRONT");
                return;
            }

            SetActive(noteLine, true);
            SetNote(noteLine, ref shownNote, MarkerLegend);
            for (int i = 0; i < StormField.MaxCells; i++)
            {
                if (i >= count)
                {
                    HideRow(i);
                    continue;
                }

                StormCell cell = source[i];
                StormWarning tier = cell.WarningAt(lastPlayerX, lastPlayerZ);
                float speed = (float)Math.Sqrt(cell.VelocityX * cell.VelocityX + cell.VelocityZ * cell.VelocityZ);

                SetCell(i, 0, StormReadout.Kind(cell.Kind));
                SetCell(i, 1, WeatherReadout.Meters(cell.TopHeight));
                SetCell(i, 2, WeatherReadout.Meters(cell.CloudBase));
                SetCell(i, 3, WeatherReadout.Meters(cell.Radius));
                SetCell(i, 4, StormReadout.BearingTo(0f, 0f, cell.VelocityX, cell.VelocityZ) + " " +
                              speed.ToString("0", CultureInfo.InvariantCulture));
                SetCell(i, 5, StormReadout.WarningShortCode(tier));
                SetCell(i, 6, StormReadout.BearingTo(lastPlayerX, lastPlayerZ, cell.X, cell.Z) + " " +
                              StormReadout.NauticalMiles(cell.DistanceTo(lastPlayerX, lastPlayerZ)));

                rails[i].color = RailColor(tier);
                SetActive(rails[i], true);
            }

            text.Length = 0;
            text.Append(ForecastLabels[forecastIndex]);
            text.Append(" · ");
            text.Append(StormModes.Label(lastSnapshot.StormMode));
            text.Append(" · ");
            text.Append(count);
            text.Append(count == 1 ? " CELL" : " CELLS");
            text.Append(forecastView ? " · FORECAST" : "");
            SetNote(sectionNote, ref shownSection, text.ToString());
        }

        private void SetCell(int row, int column, string value)
        {
            TMP_Text label = table[row * Columns + column];
            label.text = value ?? WeatherReadout.Unknown;
            SetActive(label, true);
        }

        private void HideRow(int row)
        {
            for (int c = 0; c < Columns; c++) SetActive(table[row * Columns + c], false);
            SetActive(rails[row], false);
        }

        private void ShowNote(string message)
        {
            for (int i = 0; i < StormField.MaxCells; i++) HideRow(i);
            SetActive(noteLine, true);
            SetNote(noteLine, ref shownNote, message);
        }

        private static void SetNote(TMP_Text label, ref string cached, string value)
        {
            if (label == null || string.Equals(value, cached, StringComparison.Ordinal)) return;
            cached = value;
            label.text = value;
        }

        private void BindHeader(int count)
        {
            text.Length = 0;
            text.Append(ForecastLabels[forecastIndex]);
            text.Append(" · ");
            text.Append(Kilometres(rangeMetres));
            text.Append(" KM · ");
            text.Append(count);
            text.Append(count == 1 ? " ECHO" : " ECHOES");
            SetNote(headerNote, ref shownHeader, text.ToString());
        }

        // ---- Markers ---------------------------------------------------------------------

        private void BindOwnship()
        {
            if (!painted) return;

            hasTrack = TryTrack(out trackVx, out trackVz);
            Vector2 marker = MapPoint(lastPlayerX, lastPlayerZ, out bool inside);
            if (!inside) marker = ClampIntoArea(marker, 4f);

            PlaceRotated(ownship.rectTransform, marker, OwnshipPixels, OwnshipPixels, 0f);
            SetActive(ownship, true);

            if (hasTrack)
            {
                float speed = (float)Math.Sqrt(trackVx * trackVx + trackVz * trackVz);
                float length = Mathf.Min(TrackPixelsMax, speed * TrackSeconds * pixelsPerMetre);
                bool show = length > 3f;
                SetActive(trackLine, show);
                if (show)
                {
                    Vector2 direction = new Vector2(trackVx, trackVz).normalized;
                    PlaceRotated(trackLine.rectTransform, marker + direction * (length * 0.5f), length, TrackThickness,
                        PixelAngle(trackVx, trackVz));
                }
            }
            else
            {
                SetActive(trackLine, false);
            }

            SetActive(selection, hasSelection);
            if (!hasSelection) return;

            Vector2 selected = MapPoint(selectionX, selectionZ, out bool visible);
            if (!visible) selected = ClampIntoArea(selected, 8f);
            PlaceRotated(selection.rectTransform, selected, SelectionPixels, SelectionPixels, 0f);
        }

        /// <summary>The local aircraft's ground vector: cockpit-local presentation, never gameplay.</summary>
        private static bool TryTrack(out float vx, out float vz)
        {
            vx = 0f;
            vz = 0f;
            GameManager.GetLocalAircraft(out Aircraft local);
            if (local == null || local.rb == null) return false;

            Vector3 velocity = local.rb.velocity;
            if (float.IsNaN(velocity.x) || float.IsNaN(velocity.z)) return false;
            vx = velocity.x;
            vz = velocity.z;
            return true;
        }

        private bool PlaceMarker(Image marker, float worldX, float worldZ, float size)
        {
            Vector2 point = MapPoint(worldX, worldZ, out bool inside);
            if (!inside)
            {
                SetActive(marker, false);
                return false;
            }

            PlaceRotated(marker.rectTransform, point, size, size, 0f);
            SetActive(marker, true);
            return true;
        }

        // ---- Readouts --------------------------------------------------------------------

        private void BindReadouts(WeatherSnapshot snapshot, WeatherManager manager)
        {
            bool available = snapshot.Available;
            StormCell[] source = forecastView ? forecastCells : snapshot.Cells;
            int count = forecastView ? paintedCount : ClampCount(snapshot);

            StormWarning tier = StormWarning.None;
            float local = 0f;
            if (available && source != null && count > 0)
            {
                tier = StormField.WarningAt(source, count, lastPlayerX, lastPlayerZ, out _);
                local = RadarImage.ReflectivityAt(source, count, in paintedFront, lastPlayerX, lastPlayerZ);
            }

            text.Length = 0;
            text.Append("YOU · WARN ");
            text.Append(available ? StormReadout.Warning(tier) : WeatherReadout.Unknown);
            text.Append(" · ECHO ");
            text.Append(available ? WeatherReadout.Percent01(local) : WeatherReadout.Unknown);
            if (hasTrack)
            {
                float speed = (float)Math.Sqrt(trackVx * trackVx + trackVz * trackVz);
                text.Append(" · GS ");
                text.Append(speed.ToString("0", CultureInfo.InvariantCulture));
                text.Append(" · TRK ");
                text.Append(WeatherReadout.Compass16((float)(Math.Atan2(trackVx, trackVz) * 180.0 / Math.PI)));
            }
            if (!available) text.Append(" · NO SCHEDULE");
            ownValue.SetText(text);

            text.Length = 0;
            if (hasSelection)
            {
                text.Append("REF · ");
                text.Append(StormReadout.BearingTo(lastPlayerX, lastPlayerZ, selectionX, selectionZ));
                text.Append(" · ");
                text.Append(StormReadout.NauticalMiles(Distance(lastPlayerX, lastPlayerZ, selectionX, selectionZ)));
                text.Append(" · ");
                text.Append(manager.HostAuthority ? "HOST" : "CLIENT");
            }
            else if (armed)
            {
                text.Append("REF · CLICK THE MAP TO SET A REFERENCE");
            }
            else if (Time.unscaledTime < echoUntil && !string.IsNullOrEmpty(echoText))
            {
                text.Append(echoText);
            }
            else if (!available)
            {
                text.Append("REF · NO READOUT — NO WEATHER SCHEDULE ON THIS MISSION");
            }
            else
            {
                text.Append("REF · PICK A POINT ON THE MAP · ");
                text.Append(manager.HostAuthority ? "HOST" : "CLIENT");
            }
            refValue.SetText(text);

            BindAtmosphere(snapshot);
        }

        /// <summary>
        /// WIND and TEMP are read-outs of the atmosphere model, not interpolated rasters: the model
        /// carries no field to paint, and a fabricated gradient would be a lie on a chart. Both stay
        /// dark until the physics that fills <c>Atmosphere</c> is present.
        /// </summary>
        private void BindAtmosphere(WeatherSnapshot snapshot)
        {
            Atmosphere atmosphere = snapshot.Atmosphere;
            bool available = atmosphere.Available;

            bool wind = available && (layers & LayerWind) != 0;
            SetActive(windArrow, wind);
            SetActive(windValue, wind);
            if (wind)
            {
                text.Length = 0;
                text.Append(WeatherReadout.Wind(snapshot.LocalWindSpeed, snapshot.LocalWindHeading));
                text.Append(" · GUST ");
                text.Append(atmosphere.GustSpeed.ToString("0", CultureInfo.InvariantCulture));
                windValue.SetText(text);

                float emphasis = Mathf.Clamp01(atmosphere.GustSpeed / 25f);
                Vector2 point = new Vector2(mapArea.width - WindArrowMargin, -WindArrowMargin * 0.5f);
                PlaceRotated(windArrow.rectTransform, point, WindArrowPixels * Mathf.Lerp(0.6f, 1f, emphasis),
                    WindArrowPixels * 0.5f, PixelAngle(snapshot.LocalWindX, snapshot.LocalWindZ));
            }

            bool temp = available && (layers & LayerTemp) != 0;
            SetActive(tempValue, temp);
            SetActive(stabilityValue, temp);
            if (!temp) return;

            text.Length = 0;
            text.Append("T ");
            text.Append(WeatherReadout.Decimal(atmosphere.TemperatureC, 1));
            text.Append("°C · TD ");
            text.Append(WeatherReadout.Decimal(atmosphere.DewpointC, 1));
            text.Append(" · LCL ");
            text.Append(WeatherReadout.Meters(atmosphere.Lcl));
            text.Append(" · ");
            text.Append(AirMasses.Name(atmosphere.AirMass));
            tempValue.SetText(text);

            text.Length = 0;
            text.Append(Atmospheres.Label(atmosphere.Category));
            text.Append(" · CAPE ");
            text.Append(WeatherReadout.Percent01(atmosphere.Cape));
            text.Append(" · CIN ");
            text.Append(WeatherReadout.Percent01(atmosphere.Cin));
            text.Append(" · SHEAR ");
            text.Append(WeatherReadout.Percent01(atmosphere.Shear));
            text.Append(" · ");
            text.Append(Atmospheres.Label(atmosphere.Precipitation));
            stabilityValue.SetText(text);
        }

        private static float Distance(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX;
            float dz = toZ - fromZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static string Kilometres(float metres) =>
            (metres / 1000f).ToString("0", CultureInfo.InvariantCulture);

        // ---- Controls --------------------------------------------------------------------

        private void ToggleLayer(int flag)
        {
            if ((flag == LayerWind || flag == LayerTemp) && !lastSnapshot.Atmosphere.Available) return;
            layers ^= flag;
        }

        private void CycleRange()
        {
            rangeIndex = (rangeIndex + 1) % RangeMetres.Length;
            rangeMetres = RangeMetres[rangeIndex];
            if (settings != null) settings.RadarRangeKm.Value = (int)(rangeMetres / 1000f);
            if (zoomButton != null) zoomButton.SetText(ZoomLabel());
        }

        private string ZoomLabel() => Label("ZOOM ", Kilometres(rangeMetres), " KM");

        private void CycleForecast()
        {
            forecastIndex = (forecastIndex + 1) % ForecastOffsets.Length;
            if (forecastButton != null) forecastButton.SetText(TimeLabel());
        }

        private string TimeLabel() => Label("TIME ", ForecastLabels[forecastIndex],
            forecastIndex > 0 ? " FORECAST" : "");

        private string Label(string first, string middle, string last)
        {
            text.Length = 0;
            text.Append(first);
            text.Append(middle);
            text.Append(last);
            return text.ToString();
        }

        private void TogglePick()
        {
            if (armed)
            {
                Disarm();
                return;
            }

            if (MapPicker.IsBusy ||
                !MapPicker.TryArm(PickOwner, MapPicker.GestureLeft, "RADAR REFERENCE · CLICK THE RADAR MAP"))
            {
                Echo("MAP BUSY — " + (MapPicker.Prompt ?? "ARMED"));
                return;
            }

            armed = true;
            armedAt = Time.unscaledTime;
            SetPickLabel(true);
        }

        /// <summary>Confirm an action on the reference line for a moment.</summary>
        private void Echo(string message)
        {
            echoText = message;
            echoUntil = Time.unscaledTime + 2.5f;
        }

        private void Disarm()
        {
            MapPicker.Disarm(PickOwner);
            armed = false;
            pressX = float.NaN;
            pressY = float.NaN;
            SetPickLabel(false);
        }

        private void SetPickLabel(bool on)
        {
            if (pickButton == null) return;
            pickButton.SetText(on ? "PICK ARMED" : "PICK");
            pickButton.SetLatched(on);
        }

        /// <summary>
        /// The armed click, read through the shared picker like every other map gesture: a press
        /// released without travel, inside this page's own map, resolves to a world point. It is
        /// consumed only while this page owns the picker, so it cannot double as a wing order or a
        /// support call-in.
        /// </summary>
        private void ConsumePick()
        {
            if (!armed) return;
            if (!MapPicker.IsOwner(PickOwner))
            {
                Disarm();
                return;
            }

            Vector3 pointer = Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                pressX = pointer.x;
                pressY = pointer.y;
            }

            if (!Input.GetMouseButtonUp(0)) return;

            bool click = !float.IsNaN(pressX) &&
                         (pointer.x - pressX) * (pointer.x - pressX) + (pointer.y - pressY) * (pointer.y - pressY)
                         <= PickSlopPixels * PickSlopPixels;
            pressX = float.NaN;
            pressY = float.NaN;
            if (!click) return;

            Canvas canvas = mapRoot.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRoot, pointer, camera, out Vector2 local))
                return;
            if (local.x < 0f || local.x > mapArea.width || local.y > 0f || local.y < -mapArea.height) return;

            selectionX = local.x / pixelsPerMetre - halfX;
            selectionZ = local.y / pixelsPerMetre + halfZ;
            hasSelection = true;
            Disarm();
        }

        /// <summary>The nearest ladder rung to whatever the config holds, so a hand-edited value lands.</summary>
        private static int InitialRangeIndex(WeatherSettings settings)
        {
            if (settings == null) return DefaultRangeIndex;

            float metres = settings.RadarRangeKm.Value * 1000f;
            int best = DefaultRangeIndex;
            float bestDelta = float.MaxValue;
            for (int i = 0; i < RangeMetres.Length; i++)
            {
                float delta = Mathf.Abs(RangeMetres[i] - metres);
                if (delta >= bestDelta) continue;
                bestDelta = delta;
                best = i;
            }
            return best;
        }

        private void BindLayers()
        {
            if (!lastSnapshot.Atmosphere.Available) layers &= ~(LayerWind | LayerTemp);

            for (int i = 0; i < layerButtons.Length; i++)
            {
                AvButton button = layerButtons[i];
                if (button == null) continue;

                bool gated = LayerFlags[i] == LayerWind || LayerFlags[i] == LayerTemp;
                bool supported = !gated || lastSnapshot.Atmosphere.Available;
                button.SetEnabled(supported);
                button.SetLatched((layers & LayerFlags[i]) != 0);
                button.WithTooltip(supported
                    ? LayerTooltip(i)
                    : "The atmosphere model is not in this build yet: this layer has nothing honest to draw.");
            }

            if (zoomButton != null) zoomButton.SetText(ZoomLabel());
            if (forecastButton != null) forecastButton.SetText(TimeLabel());
        }

        // ---- Geometry --------------------------------------------------------------------

        /// <summary>
        /// World to map pixels: <c>x</c> from the window's west edge, <c>y</c> from its north edge
        /// and growing downward, which is the convention <c>AvKit.Place</c> writes. The window is
        /// always centred on the map origin and its aspect matches the pixel area, so both axes
        /// share one metres-to-pixels scale.
        /// </summary>
        private Vector2 MapPoint(float worldX, float worldZ, out bool inside)
        {
            float px = (worldX + halfX) * pixelsPerMetre;
            float py = (worldZ - halfZ) * pixelsPerMetre;
            inside = px >= 0f && px <= mapArea.width && py <= 0f && py >= -mapArea.height;
            return new Vector2(px, py);
        }

        private Vector2 ClampIntoArea(Vector2 point, float margin)
        {
            return new Vector2(
                Mathf.Clamp(point.x, margin, mapArea.width - margin),
                Mathf.Clamp(point.y, -mapArea.height + margin, -margin));
        }

        /// <summary>
        /// Rotation for a rect whose sprite points along +x at rest, given a direction in world
        /// <c>x</c>/<c>z</c>. World +Z is up the map, which is where pixel +y already points, so
        /// this is the one conversion the markers share.
        /// </summary>
        private static float PixelAngle(float worldX, float worldZ) =>
            (float)(Math.Atan2(worldZ, worldX) * 180.0 / Math.PI);

        private bool ClipToArea(Vector2 point, Vector2 direction, out Vector2 start, out Vector2 end)
        {
            float tMin = float.NegativeInfinity;
            float tMax = float.PositiveInfinity;
            start = point;
            end = point;

            if (!ClipSlab(point.x, direction.x, 0f, mapArea.width, ref tMin, ref tMax)) return false;
            if (!ClipSlab(point.y, direction.y, -mapArea.height, 0f, ref tMin, ref tMax)) return false;
            if (tMin > tMax) return false;

            start = point + direction * tMin;
            end = point + direction * tMax;
            return true;
        }

        private static bool ClipSlab(float origin, float direction, float low, float high, ref float tMin, ref float tMax)
        {
            if (Mathf.Abs(direction) < 1e-6f) return origin >= low && origin <= high;

            float a = (low - origin) / direction;
            float b = (high - origin) / direction;
            if (a > b)
            {
                float swap = a;
                a = b;
                b = swap;
            }

            if (a > tMin) tMin = a;
            if (b < tMax) tMax = b;
            return true;
        }

        private static void PlaceRotated(RectTransform rect, Vector2 centre, float width, float height, float degrees)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = centre;
            rect.sizeDelta = new Vector2(width, height);
            rect.localRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        private static void SetActive(Component component, bool on)
        {
            if (component == null) return;
            if (component.gameObject.activeSelf != on) component.gameObject.SetActive(on);
        }

        private static Color RailColor(StormWarning warning) =>
            AvStyleHost.Resolve(AvStyleHost.Style(StormReadout.RailClass(warning)).Background, AvTheme.RailInert);

        private static Image Marker(Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject("Marker", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(4f, 4f);

            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            image.color = color;
            return image;
        }

        private static Image Bar(Transform parent, Color color)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(4f, 4f);

            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = color;
            return image;
        }

        // ---- Sprites ---------------------------------------------------------------------

        private static Sprite CreateRingSprite()
        {
            var texture = new Texture2D(RingSpriteSize, RingSpriteSize, TextureFormat.RGBA32, mipChain: false)
            {
                name = "BoscaliWeather.Ring",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float centre = RingSpriteSize * 0.5f;
            float radius = centre - RingSpriteThickness - 1f;
            for (int y = 0; y < RingSpriteSize; y++)
            {
                for (int x = 0; x < RingSpriteSize; x++)
                {
                    float dx = x + 0.5f - centre;
                    float dy = y + 0.5f - centre;
                    float band = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - radius);
                    Color pixel = Color.white;
                    pixel.a = Mathf.Clamp01(RingSpriteThickness * 0.5f + 0.5f - band);
                    texture.SetPixel(x, y, pixel);
                }
            }
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return SpriteOf(texture, "BoscaliWeather.Ring");
        }

        /// <summary>A soft dot for echoes, airfields, waypoints and the own-ship marker, built once.</summary>
        private static Sprite CreateDotSprite()
        {
            var texture = new Texture2D(DotSpriteSize, DotSpriteSize, TextureFormat.RGBA32, mipChain: false)
            {
                name = "BoscaliWeather.Dot",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float centre = DotSpriteSize * 0.5f;
            float radius = centre - 1f;
            for (int y = 0; y < DotSpriteSize; y++)
            {
                for (int x = 0; x < DotSpriteSize; x++)
                {
                    float dx = x + 0.5f - centre;
                    float dy = y + 0.5f - centre;
                    float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / radius);
                    Color pixel = Color.white;
                    pixel.a = falloff * falloff;
                    texture.SetPixel(x, y, pixel);
                }
            }
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return SpriteOf(texture, "BoscaliWeather.Dot");
        }

        /// <summary>A small arrow pointing along +x at rest, for the front's motion and the wind.</summary>
        private static Sprite CreateArrowSprite()
        {
            var texture = new Texture2D(ArrowSpriteSize, ArrowSpriteSize, TextureFormat.RGBA32, mipChain: false)
            {
                name = "BoscaliWeather.Arrow",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float half = ArrowSpriteSize * 0.5f;
            for (int y = 0; y < ArrowSpriteSize; y++)
            {
                for (int x = 0; x < ArrowSpriteSize; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float span = Mathf.InverseLerp(half - 2f, 0f, dx) * half * 0.8f;
                    Color pixel = Color.white;
                    pixel.a = Mathf.Abs(dy) <= span && dx >= -half + 2f ? 1f : 0f;
                    texture.SetPixel(x, y, pixel);
                }
            }
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return SpriteOf(texture, "BoscaliWeather.Arrow");
        }

        private static Sprite SpriteOf(Texture2D texture, string name)
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // ---- Teardown --------------------------------------------------------------------

        /// <summary>
        /// Called before the page's GameObjects are destroyed, and again on every scene reset.
        /// Safe to call twice: the picker, the texture, its pixels and the generated sprites are the
        /// only things this page owns, and the panel owns the GameObjects.
        /// </summary>
        public void Reset()
        {
            Disarm();
            ReleaseTexture();

            for (int i = 0; i < cells.Length; i++) cells[i] = null;
            for (int i = 0; i < rails.Length; i++) rails[i] = null;
            for (int i = 0; i < table.Length; i++) table[i] = null;
            for (int i = 0; i < airfields.Length; i++) airfields[i] = null;
            for (int i = 0; i < waypoints.Length; i++) waypoints[i] = null;
            for (int i = 0; i < ringImages.Length; i++)
            {
                ringImages[i] = null;
                ringLabels[i] = null;
            }
            for (int i = 0; i < glyphs.Length; i++) glyphs[i] = null;
            for (int i = 0; i < layerButtons.Length; i++) layerButtons[i] = null;

            mapRoot = null;
            mapImage = null;
            echoImage = null;
            frontLine = null;
            frontArrow = null;
            windArrow = null;
            ownship = null;
            trackLine = null;
            selection = null;
            frontEta = null;
            windValue = null;
            tempValue = null;
            stabilityValue = null;
            ownValue = null;
            refValue = null;
            headerNote = null;
            sectionNote = null;
            noteLine = null;
            zoomButton = null;
            forecastButton = null;
            pickButton = null;

            ReleaseSprite(ref ringSprite);
            ReleaseSprite(ref dotSprite);
            ReleaseSprite(ref arrowSprite);

            lastSnapshot = WeatherSnapshot.Unavailable;
            paintedFront = WeatherFront.None;
            painted = false;
            hasTrack = false;
            hasSelection = false;
            forecastView = false;
            seedValid = false;
            seedMission = null;
            seedMapName = null;
            paintedStormMode = -1;
            paintedFrontKey = int.MinValue;
            paintedViewKey = int.MinValue;
            paintedCount = 0;
            shownHeader = null;
            shownSection = null;
            shownNote = null;
            nextPaintAt = 0f;
        }

        private void ReleaseTexture()
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = null;
            pixels = null;
        }

        private static void ReleaseSprite(ref Sprite sprite)
        {
            if (sprite == null) return;
            if (sprite.texture != null) UnityEngine.Object.Destroy(sprite.texture);
            UnityEngine.Object.Destroy(sprite);
            sprite = null;
        }
    }
}

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
    /// The WEA panel's radar section: a north-up reflectivity picture of the whole map, painted
    /// from the deterministic storm field and the front, with the bounded cell table that
    /// describes it. It is model output and says so once, quietly; it is never dressed as a game
    /// radar return.
    ///
    /// <para>This is a section of the one ENV page, not a page of its own. It draws nothing above
    /// its own map and nothing over it: the page budgets <see cref="ChromeHeight"/> below the
    /// picture, the section trims the cell table to <see cref="MaxTableRows"/> nearest cells and
    /// counts the remainder instead of overflowing.</para>
    ///
    /// <para>The texture is rebuilt on a 1 Hz budget, and immediately when the view (range, time
    /// or the map layer) changes — never per frame. One texture, one pixel buffer and one
    /// <see cref="RadarImage"/> grid are reused for the whole scene; every marker is moved rather
    /// than rebuilt, and every label is written through a reused <see cref="StringBuilder"/>.</para>
    /// </summary>
    internal sealed class WeatherRadarPage
    {
        /// <summary>Cell rows the section can afford. The nearest cells win; the rest are counted.</summary>
        public const int MaxTableRows = 3;

        /// <summary>One table row's pitch, shared by the forecast table above the radar.</summary>
        public const float RowPitch = 16f;

        private const float MapGap = 4f;
        private const float RampHeight = 18f;
        private const float ControlHeight = AvTokens.RowHeight;
        private const float RefHeight = 12f;
        private const float TableHeaderHeight = 12f;
        private const float NoteHeight = 12f;

        /// <summary>Everything this section draws below its map, so the page can budget for it.</summary>
        public const float ChromeHeight = RampHeight + ControlHeight + RefHeight
                                        + TableHeaderHeight + MaxTableRows * RowPitch + NoteHeight
                                        + 3f * MapGap;

        /// <summary>
        /// The one table grid both data tables read. The forecast puts AGE, SKY and TREND on
        /// the first three edges and its WIND column across the last two; the echo table adds
        /// RNG / BRG on the fifth. A column edge therefore lands on the same x in both tables,
        /// and the last column takes whatever width the section has left.
        /// </summary>
        public const float TableInset = 8f;
        public static readonly float[] TableColumnEdges = { 0f, 100f, 184f, 248f, 306f };

        /// <summary>Owner id for the shared armed map gesture. Not a MapPicker constant: this one is ours.</summary>
        private const string PickOwner = "boscali.weather.radar";

        /// <summary>The scope's range ladder: the half-height of the window in metres.</summary>
        private static readonly float[] RangeMetres = { 20000f, 40000f, 80000f, 160000f };
        private const int DefaultRangeIndex = 1;

        private static readonly float[] ForecastOffsets = { 0f, 1800f, 3600f };
        private static readonly string[] ForecastLabels = { "NOW", "+30", "+60" };

        /// <summary>The two rings, as fractions of the window's half-height, so they scale with the zoom.</summary>
        private static readonly float[] RingFractions = { 0.5f, 1f };

        /// <summary>How far the boundary has to move before the picture is worth repainting.</summary>
        private const float FrontStepMetres = 1500f;

        /// <summary>Repaint cadence. The picture is 1 Hz; the markers ride the panel's own tick.</summary>
        private const float RebuildInterval = 1f;

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
        private const float FrontTokenPixels = 9f;
        private const float FrontLabelWidth = 150f;
        private const int MaxFrontGlyphs = 3;

        private const float PickSlopPixels = 6f;
        private const float PickTimeout = 25f;

        private const int MaxAirfields = 16;
        private const int MaxAirfieldScan = 64;
        private const int MaxWaypoints = 16;
        private const int Columns = 5;
        private const int ColumnKind = 0;
        private const int ColumnWarn = 2;

        private const int RingSpriteSize = 128;
        private const float RingSpriteThickness = 1.5f;
        private const int DotSpriteSize = 64;
        private const int ArrowSpriteSize = 32;

        private const float ControlGap = 4f;

        private static readonly Color32 ClearPixel = new Color32(0, 0, 0, 0);
        private static readonly Color32 RampLight = new Color32(77, 199, 92, 190);
        private static readonly Color32 RampModerate = new Color32(237, 219, 61, 220);
        private static readonly Color32 RampHeavy = new Color32(242, 148, 38, 235);
        private static readonly Color32 RampIntense = new Color32(230, 66, 46, 245);
        private static readonly Color32 RampExtreme = new Color32(219, 82, 242, 255);

        /// <summary>
        /// One reflectivity band: the threshold, the colour and the label together, so the picture
        /// and the legend cannot drift apart and severity never rides on colour alone. Labels are
        /// short enough for five equal cells at the panel width.
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
            new Band(RadarImage.NoEcho, RampLight, "LIGHT 8-30"),
            new Band(0.30f, RampModerate, "MOD 30-50"),
            new Band(0.50f, RampHeavy, "HEAVY 50-68"),
            new Band(0.68f, RampIntense, "INTENSE 68-85"),
            new Band(0.85f, RampExtreme, "EXTREME 85+"),
        };

        private static readonly string[] ColumnKeys = { "KIND", "TOP", "WARN", "VECTOR", "RNG / BRG" };

        /// <summary>The map is the armed pick's target, so it carries the help a control would.</summary>
        private const string MapTooltip =
            "The scope is north-up, centred on the map. Arm PICK, then click it to set a reference point.";

        /// <summary>Wide enough for "21.6 NM / 40 KM"; a 90px box ellipsised the unit away.</summary>
        private const float RingLabelWidth = 132f;

        /// <summary>
        /// What the picture's own marks are, read on the ref line when nothing is picked: the
        /// front kind is already named in words on the map and in the wind section, so this only
        /// spells the two marks that have no word of their own.
        /// </summary>
        private const string MarkerHint = "DOTS = CELLS · RING = YOU";

        private readonly WeatherSettings settings;
        private readonly StringBuilder text = new StringBuilder(96);
        private readonly RadarImage radarImage = new RadarImage();
        private readonly StormCell[] forecastCells = new StormCell[StormField.MaxCells];
        private readonly StormCell[] tableCells = new StormCell[MaxTableRows];
        private readonly StormCell[] oneCell = new StormCell[1];

        private readonly Image[] cells = new Image[StormField.MaxCells];
        private readonly Image[] rails = new Image[MaxTableRows];
        private readonly TMP_Text[] table = new TMP_Text[MaxTableRows * Columns];
        private readonly Image[] airfields = new Image[MaxAirfields];
        private readonly Image[] waypoints = new Image[MaxWaypoints];
        private readonly Image[] ringImages = new Image[RingFractions.Length];
        private readonly TMP_Text[] ringLabels = new TMP_Text[RingFractions.Length];
        private readonly Image[] glyphs = new Image[MaxFrontGlyphs];

        private RectTransform mapRoot;
        private Rect mapArea;
        private Image mapImage;
        private RawImage echoImage;
        private Image frontLine;
        private Image frontArrow;
        private Image ownship;
        private Image trackLine;
        private Image selection;
        private TMP_Text frontEta;
        private TMP_Text refValue;
        private TMP_Text headerNote;
        private TMP_Text noteLine;
        private AvButton zoomButton;
        private AvButton forecastButton;
        private AvButton pickButton;
        private AvButton mapButton;
        private Image pickRail;
        private AvTooltipTarget mapHover;
        private Sprite ringSprite;
        private Sprite dotSprite;
        private Sprite arrowSprite;

        private Texture2D texture;
        private Color32[] pixels;

        private bool mapLayer = true;
        private int rangeIndex = DefaultRangeIndex;
        private int forecastIndex;
        private int paintedStormMode = -1;
        private int paintedFrontKey = int.MinValue;
        private int paintedViewKey = int.MinValue;

        private WeatherSnapshot lastSnapshot = WeatherSnapshot.Unavailable;
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
        private string shownNote;
        private string shownRef;

        private int seed;
        private Mission seedMission;
        private string seedMapName;
        private bool seedValid;

        public WeatherRadarPage(WeatherSettings settings)
        {
            this.settings = settings;
            rangeIndex = InitialRangeIndex(settings);
            rangeMetres = RangeMetres[rangeIndex];
            halfZ = rangeMetres;
        }

        // ---- Build -----------------------------------------------------------------------

        /// <summary>
        /// Draws the whole section inside <paramref name="width"/>, starting at
        /// <paramref name="y"/> with the map <paramref name="mapHeight"/> tall. The page owns the
        /// section header and hands in its note label; the section writes the scan line into it.
        /// Returns the bottom y so the page can carry on with its grid.
        /// </summary>
        public float Build(RectTransform parent, TMP_Text header, float x, float y, float width, float mapHeight)
        {
            if (parent == null) return y;
            headerNote = header;

            BuildMap(parent, x, y, width, mapHeight);
            y -= mapArea.height + MapGap;

            BuildLegend(parent, x, y, width);
            y -= RampHeight + MapGap;

            BuildControls(parent, x, y, width);
            y -= ControlHeight + MapGap;

            // The line takes the section's full width and its readings are bounded, so overflow
            // rather than ellipsis: a clipped reference reading is worse than a long one.
            refValue = AvStyled.Label(parent, new Rect(x, y, width, RefHeight), "", "row-sub");
            refValue.enableWordWrapping = false;
            refValue.overflowMode = TextOverflowModes.Overflow;
            y -= RefHeight;

            BuildTable(parent, x, y, width);
            y -= TableHeaderHeight + MaxTableRows * RowPitch + NoteHeight;

            paintedViewKey = int.MinValue;
            return y;
        }

        private void BuildMap(RectTransform parent, float x, float y, float width, float height)
        {
            // AvKit.Place writes the rect's y as the *top* edge (pivot 0,1), which is the one
            // convention every rect on the page uses. A bottom-edge rect here placed the whole
            // map one map-height low, over the legend and the cell table.
            mapArea = new Rect(x, y, width, height);

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
            // It is also the section's one pointer surface, so the map itself can publish hover
            // help even though the click it takes is the armed picker's, not a button's.
            Image bed = AvKit.Panel(mapRoot, new Rect(0f, 0f, mapArea.width, mapArea.height), AvTheme.Ground);
            bed.raycastTarget = true;
            mapHover = bed.gameObject.AddComponent<AvTooltipTarget>();
            mapHover.Initialise(MapTooltip);

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
            // A RawImage with no texture is an opaque white quad. Stay invisible until the
            // first paint hands it a texture, or the section flashes white over the legend.
            echoImage.color = Color.clear;

            for (int i = 0; i < ringImages.Length; i++)
            {
                ringImages[i] = Marker(mapRoot, ringSprite, AvTheme.Hairline);
                ringLabels[i] = AvStyled.Label(mapRoot, new Rect(0f, 0f, RingLabelWidth, 11f), "", "section-title-note",
                    align: TextAlignmentOptions.Center);
            }

            for (int i = 0; i < airfields.Length; i++) airfields[i] = Marker(mapRoot, dotSprite, AvTheme.TextPrimary);
            for (int i = 0; i < waypoints.Length; i++) waypoints[i] = Marker(mapRoot, dotSprite, AvTheme.RailInfo);
            for (int i = 0; i < cells.Length; i++) cells[i] = Marker(mapRoot, dotSprite, AvTheme.RailInert);

            frontLine = Bar(mapRoot, AvTheme.RailDanger);
            frontArrow = Marker(mapRoot, arrowSprite, AvTheme.Accent);
            for (int i = 0; i < glyphs.Length; i++) glyphs[i] = Marker(mapRoot, arrowSprite, AvTheme.RailDanger);

            trackLine = Bar(mapRoot, AvTheme.Accent);
            ownship = Marker(mapRoot, dotSprite, AvTheme.Accent);
            selection = Marker(mapRoot, ringSprite, AvTheme.RailInfo);
            frontEta = AvStyled.Label(mapRoot, new Rect(0f, 0f, FrontLabelWidth, 12f), "", "row-sub",
                align: TextAlignmentOptions.Center);
            // The boundary's ETA rides over the echo band, so it is the one map label read at
            // full ink rather than the dim note weight.
            frontEta.color = AvTheme.TextPrimary;

            // The raster runs under this tag; a bed keeps it readable over bright terrain.
            AvKit.Panel(mapRoot, new Rect(3f, 11f, 74f, 13f), AvTheme.Ground);
            AvStyled.Label(mapRoot, new Rect(5f, 12f, 70f, 11f), "SYNTHETIC", "section-title-note");

            HideAirfields(0);
            HideWaypoints(0);
            for (int i = 0; i < cells.Length; i++) SetActive(cells[i], false);
            SetActive(frontLine, false);
            SetActive(frontArrow, false);
            SetActive(ownship, false);
            SetActive(trackLine, false);
            SetActive(selection, false);
            SetActive(frontEta, false);

            AvKit.Outline(parent, mapArea, AvTheme.Hairline);
            AvKit.CornerTicks(parent, mapArea, AvTheme.Hairline);
        }

        private void BuildLegend(RectTransform parent, float x, float y, float width)
        {
            float cell = width / Bands.Length;
            for (int i = 0; i < Bands.Length; i++)
            {
                float cellX = x + i * cell;
                // Swatch over label, both on the cell's left edge and the cell's width, so the
                // colour and the dBZ range read as one entry and cannot drift apart.
                AvKit.Panel(parent, new Rect(cellX, y, Mathf.Max(1f, cell - 3f), 7f), Bands[i].Colour);
                TMP_Text label = AvStyled.Label(
                    parent, new Rect(cellX, y - 7f, cell, 11f), Bands[i].Label, "section-title-note");
                // The sheet's tracking pushes "INTENSE 68-85" past its cell at the game's own
                // (wider) MFD font; the legend drops the tracking rather than the range.
                label.characterSpacing = 0f;
            }
        }

        private void BuildControls(RectTransform parent, float x, float y, float width)
        {
            float buttonWidth = (width - ControlGap * 3f) / 4f;
            mapButton = AvStyled.Button(
                parent, new Rect(x, y, buttonWidth, ControlHeight), MapLabel(), "btn", ToggleMap, AvButtonStyle.Toggle)
                .WithTooltip("Draw the mission map under the picture: coastline, terrain and airfields. " +
                             "The echoes are always painted.");
            zoomButton = AvStyled.Button(
                parent, new Rect(x + buttonWidth + ControlGap, y, buttonWidth, ControlHeight), "", "btn", CycleRange)
                .WithTooltip("Cycle the window: the half-height of the picture in kilometres. " +
                             "Weather beyond the map edge still paints.");
            forecastButton = AvStyled.Button(
                parent, new Rect(x + (buttonWidth + ControlGap) * 2f, y, buttonWidth, ControlHeight), "", "btn",
                CycleForecast)
                .WithTooltip("Deterministic forecast: the same pure schedule sampled at +30 and +60 minutes. " +
                             "Nothing is simulated, nothing is synced.");
            pickButton = AvStyled.Button(
                parent, new Rect(x + (buttonWidth + ControlGap) * 3f, y, buttonWidth, ControlHeight), "", "btn",
                TogglePick)
                .WithTooltip("Arm a map click for a reference point: bearing and range from you, read under the map.");

            // Armed is a state, not a colour: the button prints ARMED and this rail repeats it,
            // so the caution tint is never the only thing carrying the mode.
            pickRail = AvKit.Rule(parent,
                new Rect(x + (buttonWidth + ControlGap) * 3f, y - ControlHeight + 1f, buttonWidth, 3f),
                AvTheme.RailInert);
            SetPickLabel(false);
        }

        private void BuildTable(RectTransform parent, float x, float y, float width)
        {
            // The shared column edges; the last column takes whatever the table has left, so a
            // range/bearing reading is never ellipsised while the row sits empty.
            float[] edges = TableColumnEdges;
            float columnX = x + TableInset;
            for (int i = 0; i < Columns; i++)
            {
                float right = i + 1 < edges.Length ? x + TableInset + edges[i + 1] : x + width;
                AvStyled.Label(parent, new Rect(columnX, y, right - columnX, TableHeaderHeight), ColumnKeys[i],
                    "section-title-note", align: ColumnAlign(i));
                columnX = right;
            }
            y -= TableHeaderHeight;

            for (int i = 0; i < MaxTableRows; i++)
            {
                float rowY = y - i * RowPitch;
                rails[i] = AvStyled.Rail(parent, new Rect(x, rowY, TableInset - 5f, RowPitch - 1f), "locked");
                columnX = x + TableInset;
                for (int c = 0; c < Columns; c++)
                {
                    float right = c + 1 < edges.Length ? x + TableInset + edges[c + 1] : x + width;
                    // The kind is a name, the other four columns are readings: the same dim/bold
                    // pair the forecast rows use, so a column reads the same in either table.
                    // Column widths are chosen to fit their own readings (the last takes the
                    // remainder), so overflow is safe where ellipsis could cut a warning away.
                    TMP_Text label = AvStyled.Label(
                        parent, new Rect(columnX, rowY, right - columnX, RowPitch - 1f), "",
                        c == ColumnKind ? "kv-key" : "kv-value");
                    label.enableWordWrapping = false;
                    label.overflowMode = TextOverflowModes.Overflow;
                    table[i * Columns + c] = label;
                    columnX = right;
                }
                HideRow(i);
            }
            y -= MaxTableRows * RowPitch;

            noteLine = AvStyled.Label(parent, new Rect(x, y, width, NoteHeight), "", "section-title-note");
        }

        /// <summary>The kind column is a name read left; every numeric column reads right.</summary>
        private static TextAlignmentOptions ColumnAlign(int column) =>
            column == ColumnKind ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;

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
            BindRef(manager);

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
        /// The scan line, the cell table and the note all read the one <c>count</c> this pass
        /// produced, so they cannot disagree about how many echoes exist.
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

            lastSnapshot = snapshot;
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
            BindControls();
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

        private int ViewKey() => ((mapLayer ? 1 : 0) * 4 + rangeIndex) * 3 + forecastIndex;

        private static int FrontKey(in WeatherFront front)
        {
            if (!front.Present) return 0;
            return ((int)front.Kind + 1) * 1000000 + (int)(front.Position / FrontStepMetres);
        }

        // ---- Map context -----------------------------------------------------------------

        private void BindMapSprite(Vector2 span)
        {
            if (!mapLayer)
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
            if (mapLayer && mapImage.sprite != null)
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
            if (mapLayer)
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
            echoImage.color = Color.white;
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
                PlaceRotated(ringLabels[i].rectTransform, ClampIntoArea(MapPoint(0f, distance, out _), 6f),
                    RingLabelWidth, 11f, 0f);
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
            bool visible = front.Present;
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
                // Tokens, not text: the cockpit font has no triangles, so the boundary's own
                // symbol is the same drawn arrow the travel marker uses, pointing the way the
                // front moves — which is what the meteorology means anyway.
                PlaceRotated(glyphs[i].rectTransform,
                    Vector2.Lerp(start, end, (i + 1f) / (glyphs.Length + 1f)), FrontTokenPixels, FrontTokenPixels,
                    PixelAngle(front.NormalX, front.NormalZ));
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

        /// <summary>
        /// The nearest <see cref="MaxTableRows"/> cells, nearest first. A bounded selection sort
        /// over the manager's buffer: no allocation and no LINQ in a path that runs on every
        /// repaint.
        /// </summary>
        private int SelectNearest(StormCell[] source, int count, float x, float z)
        {
            int rows = Mathf.Min(MaxTableRows, count);
            uint taken = 0u;
            for (int row = 0; row < rows; row++)
            {
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    if ((taken & (1u << i)) != 0u) continue;
                    float distance = source[i].DistanceTo(x, z);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = i;
                }
                if (best < 0)
                {
                    rows = row;
                    break;
                }
                taken |= 1u << best;
                tableCells[row] = source[best];
            }
            return rows;
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
            int rows = SelectNearest(source, count, lastPlayerX, lastPlayerZ);
            for (int i = 0; i < MaxTableRows; i++)
            {
                if (i >= rows)
                {
                    HideRow(i);
                    continue;
                }

                StormCell cell = tableCells[i];
                StormWarning tier = cell.WarningAt(lastPlayerX, lastPlayerZ);
                float speed = (float)Math.Sqrt(cell.VelocityX * cell.VelocityX + cell.VelocityZ * cell.VelocityZ);
                float heading = StormReadout.BearingDegrees(0f, 0f, cell.VelocityX, cell.VelocityZ);

                SetCell(i, 0, TableKind(cell.Kind));
                SetCell(i, 1, WeatherReadout.Meters(cell.TopHeight));
                SetCell(i, 2, StormReadout.WarningShortCode(tier));
                // The warning word is the state; the tint and the rail only repeat it.
                table[i * Columns + ColumnWarn].color = WarningInk(tier);
                text.Length = 0;
                text.Append(WeatherReadout.Compass16(heading));
                text.Append(' ');
                text.Append(speed.ToString("0", CultureInfo.InvariantCulture));
                SetCell(i, 3, text.ToString());
                text.Length = 0;
                text.Append(StormReadout.BearingTo(lastPlayerX, lastPlayerZ, cell.X, cell.Z));
                text.Append(' ');
                text.Append(StormReadout.NauticalMiles(cell.DistanceTo(lastPlayerX, lastPlayerZ)));
                SetCell(i, 4, text.ToString());

                rails[i].color = RailColor(tier);
                SetActive(rails[i], true);
            }

            text.Length = 0;
            text.Append(rows);
            text.Append(rows == 1 ? " CELL" : " CELLS");
            if (count > rows)
            {
                text.Append(" SHOWN OF ");
                text.Append(count);
            }
            text.Append(" · ");
            text.Append(ForecastLabels[forecastIndex]);
            text.Append(" · ");
            text.Append(StormModes.Label(lastSnapshot.StormMode));
            SetNote(noteLine, ref shownNote, text.ToString());
        }

        /// <summary>"TOWERING CUMULUS" does not fit the kind column; the table's own short form does.</summary>
        private static string TableKind(StormKind kind)
        {
            switch (kind)
            {
                case StormKind.Supercell: return "SUPERCELL";
                case StormKind.ToweringCumulus: return "TOW CUMULUS";
                default: return "CUMULUS";
            }
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
            for (int i = 0; i < MaxTableRows; i++) HideRow(i);
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
            if (forecastIndex > 0)
            {
                text.Append("FORECAST ");
                text.Append(ForecastLabels[forecastIndex]);
            }
            else
            {
                text.Append("SCAN NOW");
            }
            text.Append(" · ");
            text.Append(Kilometres(rangeMetres));
            text.Append(" KM · ");
            text.Append(count);
            text.Append(count == 1 ? " ECHO" : " ECHOES");
            SetNote(headerNote, ref shownHeader, text.ToString());
        }

        /// <summary>The picker's persistent readout, under the map where it cannot cover it.</summary>
        private void BindRef(WeatherManager manager)
        {
            if (refValue == null) return;

            text.Length = 0;
            if (hasSelection)
            {
                text.Append("REF · ");
                text.Append(StormReadout.BearingTo(lastPlayerX, lastPlayerZ, selectionX, selectionZ));
                text.Append(" · ");
                text.Append(StormReadout.NauticalMiles(Distance(lastPlayerX, lastPlayerZ, selectionX, selectionZ)));
            }
            else if (armed)
            {
                text.Append("REF · CLICK THE MAP TO SET A REFERENCE");
            }
            else if (Time.unscaledTime < echoUntil && !string.IsNullOrEmpty(echoText))
            {
                text.Append(echoText);
            }
            else if (!lastSnapshot.Available)
            {
                text.Append("REF · NO READOUT — NO WEATHER SCHEDULE ON THIS MISSION");
            }
            else
            {
                text.Append("REF · PICK THE MAP · ");
                text.Append(MarkerHint);
                text.Append(" · ");
                text.Append(manager.HostAuthority ? "HOST" : "CLIENT");
            }
            SetNote(refValue, ref shownRef, text.ToString());
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

        private static float Distance(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX;
            float dz = toZ - fromZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static string Kilometres(float metres) =>
            (metres / 1000f).ToString("0", CultureInfo.InvariantCulture);

        // ---- Controls --------------------------------------------------------------------

        private string MapLabel() => mapLayer ? "MAP ON" : "MAP OFF";

        private void ToggleMap()
        {
            mapLayer = !mapLayer;
            if (mapButton != null) mapButton.SetText(MapLabel());
        }

        private void CycleRange()
        {
            rangeIndex = (rangeIndex + 1) % RangeMetres.Length;
            rangeMetres = RangeMetres[rangeIndex];
            // The settings write is deliberate and client-local: RadarRangeKm is documented as
            // the scope's own initial range, cycled on the scope itself, so a pilot keeps their
            // chosen zoom across missions. It changes nothing in the world.
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
            forecastIndex > 0 ? " F" : "");

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
            pickButton.SetText(on ? "ARMED" : "PICK");
            // Latched, not painted: the sheet's latched button is a translucent wash, and the
            // word plus the rail carry the state without a solid warning plate.
            pickButton.SetLatched(on);
            if (pickRail != null) pickRail.color = on ? AvTheme.RailCaution : AvTheme.RailInert;
        }

        /// <summary>
        /// The armed click, read through the shared picker like every other map gesture: a press
        /// released without travel, inside this section's own map, resolves to a world point. It is
        /// consumed only while this section owns the picker, so it cannot double as a wing order or
        /// a support call-in.
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

        private void BindControls()
        {
            if (mapButton != null)
            {
                mapButton.SetText(MapLabel());
                mapButton.SetLatched(mapLayer);
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

        /// <summary>The warning word's ink, on the same literal ladder as its rail.</summary>
        private static Color WarningInk(StormWarning warning)
        {
            switch (warning)
            {
                case StormWarning.Warning: return AvTheme.RailDanger;
                case StormWarning.Watch: return AvTheme.RailCaution;
                case StormWarning.Advisory: return AvTheme.RailInfo;
                default: return AvTheme.Dim;
            }
        }

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
        /// Called before the section's GameObjects are destroyed, and again on every scene reset.
        /// Safe to call twice: the picker, the texture, its pixels and the generated sprites are the
        /// only things this section owns, and the panel owns the GameObjects.
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

            mapRoot = null;
            mapImage = null;
            echoImage = null;
            frontLine = null;
            frontArrow = null;
            ownship = null;
            trackLine = null;
            selection = null;
            frontEta = null;
            refValue = null;
            headerNote = null;
            noteLine = null;
            zoomButton = null;
            forecastButton = null;
            pickButton = null;
            mapButton = null;
            pickRail = null;
            mapHover = null;

            ReleaseSprite(ref ringSprite);
            ReleaseSprite(ref dotSprite);
            ReleaseSprite(ref arrowSprite);

            lastSnapshot = WeatherSnapshot.Unavailable;
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
            shownHeader = null;
            shownNote = null;
            shownRef = null;
            nextPaintAt = 0f;
        }

        private void ReleaseTexture()
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = null;
            pixels = null;
            if (echoImage != null)
            {
                echoImage.texture = null;
                echoImage.color = Color.clear;
            }
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

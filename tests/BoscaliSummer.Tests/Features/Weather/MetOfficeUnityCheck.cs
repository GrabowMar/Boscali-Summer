#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Presentation;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

/// <summary>
/// Renders the production Met Office window (the weather console) from pure fixture models at 1920 x 1080 and 1.5x, the scale it has in game, in ten states: a busy host,
/// a pinned and held host, a storm host, a client (read only, with a Pace setting of its own that disagrees with the host's speed), a world still waiting for the mission environment, an empty
/// clear day, the longest strings (sky name, host log, story), a cluttered corner (anchor on the camera, eye, lenticulars and supercell at 100%, a front across them, a wind from the east),
/// a forced fog bank (nothing placed) and a model full of NaN. It gates every text (no overflow, no truncation, 11 px floor, contrast, no two texts colliding, nothing outside the window),
/// requires every part of the mockup to exist BY NAME (cards by title, section headers, chips, cells, tiles, pips), proves no two interactive controls overlap and that no Selectable takes
/// flight navigation, that every disabled control states its reason, that every hover sentence fits the footer, that each column fits above the command bar, and that a click on every
/// control, chip and bar button sends exactly the request the catalog says.
/// A pass is not visual acceptance: read the PNGs. Input, networking and the live world are untested here.
/// </summary>
public static class MetOfficeUnityCheck
{
    /// <summary>The plan task whose work the window must already show: 5 shell, 6 synoptic column, 7 instrument column, 8 time column, 9 command bar and press mapping, 10 seats and every theme. Each task raises it first, then makes it pass.</summary>
    private const int Built = 8;

    /// <summary>Every state is required: cluttered (Review Focus 5), fog (a forced fog bank is not a formation), nan (Review Focus 4) and longest (the longest strings) each pin a failure the busy host cannot show.</summary>
    private static readonly string[] States = { "host", "pinned-held", "storm", "client", "waiting", "empty", "longest", "cluttered", "fog", "nan" };

    private static readonly List<string> Failures = new List<string>();
    private static int gatedTexts, hits, hovers;
    private static bool engineError;

    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;
            Application.logMessageReceived += (message, _, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { engineError = true; Failures.Add("engine " + type + ": " + message); }
                if (type == LogType.Warning && message.Contains("font asset") && message.Contains("was not found")) Failures.Add("Shipped font or fallback is missing a displayed glyph: " + message);
            };
            SetExecutablePath("MetOfficePreview.exe");
            InitAvionics(AvFxTier.Full);   // the shared bring-up: it throws when the shipped fonts and icons did not load (fallback fonts would pass the width, overflow and contrast gates falsely)
            new GameObject("Events", typeof(UnityEngine.EventSystems.EventSystem));
            Directory.CreateDirectory("renders");
            bool fast = Environment.GetEnvironmentVariable("MET_FAST") == "1";   // Portal only: the loop while building a column
            foreach (AvThemeId theme in (AvThemeId[])Enum.GetValues(typeof(AvThemeId)))
            {
                if (fast && theme != AvThemeId.Portal) continue;
                AvStyleHost.SetTheme(theme);
                foreach (string state in States)
                    Render(state, theme, theme == AvThemeId.Portal);
            }
            AvStyleHost.SetTheme(AvThemeId.Portal);
            if (Built >= 9) CheckPresses();
            Check(!engineError, "Unity reported an engine error while painting the Met Office.");
            AvBundle.ResetForTests();
            if (Failures.Count > 0) throw new Exception(Failures.Count + " gate failures:\n" + string.Join("\n", Failures.GetRange(0, Math.Min(80, Failures.Count))));
            File.WriteAllText("result.txt", "PASS: MET OFFICE ten states on " + (fast ? "the Portal theme" : "every theme") + " at 1920x1080 (1.5x), " + gatedTexts + " gated texts (overflow, truncation, 11 px floor, contrast, collisions, outside window), "
                + hits + " interactive controls (no overlap, disabled ones say why, no flight navigation), " + hovers + " hover sentences, every mockup part present by name, column budgets and click-to-request mapping. Pure fixture data; input, networking and live state untested.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    // ---- Fakes ----------------------------------------------------------------------------------------------------------------

    private sealed class FakeWorld : IWeatherChartSource, IMetOfficeCommands
    {
        public MetOfficeModel Source = new MetOfficeModel();
        public readonly List<string> Calls = new List<string>();
        public void Fill(MetOfficeModel into) => Copy(Source, into);
        public bool SetTapeMoves(bool moves) { Calls.Add("tape " + moves); return true; }
        public bool SetPace(int pace) { Calls.Add("pace " + pace); return true; }
        public bool PinSky(int sky) { Calls.Add("pin " + sky); return true; }
        public bool Reroll() { Calls.Add("reroll"); return true; }
        public bool SetSets(byte mask) { Calls.Add("sets " + mask); return true; }
        public bool PlaceAhead(float metres) { Calls.Add("ahead " + metres); return true; }
        public bool ClearPlacement() { Calls.Add("default"); return true; }
        public bool SetRain(float? intensity) { Calls.Add("rain " + (intensity.HasValue ? intensity.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "auto")); return true; }
        public bool Scenario(WeatherScenario scenario) { Calls.Add("scenario " + scenario); return true; }
        public bool SetStory(int story) { Calls.Add("story " + story); return true; }          // the 4B and S5 controls are disabled, so no click reaches these two
        public bool FrontIn(int minutes) { Calls.Add("front " + minutes); return true; }
    }

    /// <summary>A shallow field-by-field copy of the model (fixtures are built once and handed over on every Fill).</summary>
    private static void Copy(MetOfficeModel from, MetOfficeModel to)
    {
        to.Reset();
        to.Host = from.Host; to.Captured = from.Captured; to.HasCamera = from.HasCamera; to.Class = from.Class; to.Cover = from.Cover;
        to.SkyNumber = from.SkyNumber; to.SkyName = from.SkyName; to.Metar = from.Metar; to.PinnedSky = from.PinnedSky;
        Array.Copy(from.Layers, to.Layers, from.Layers.Length);
        to.Running = from.Running; to.HeldBy = from.HeldBy; to.Pace = from.Pace; to.SpeedMps = from.SpeedMps; to.WindMps = from.WindMps; to.WindFromDeg = from.WindFromDeg;
        to.HasStory = from.HasStory; to.StoryName = from.StoryName; to.SkyRole = from.SkyRole; to.StoryIndex = from.StoryIndex; to.StoryCount = from.StoryCount;
        to.Pending = from.Pending; Array.Copy(from.Segments, to.Segments, from.Segments.Length);
        to.BlockCount = from.BlockCount; to.MarkCount = from.MarkCount; to.StripNote = from.StripNote;
        Array.Copy(from.Blocks, to.Blocks, from.Blocks.Length); Array.Copy(from.Marks, to.Marks, from.Marks.Length);
        to.FrontCount = from.FrontCount; to.CellCount = from.CellCount;
        for (int i = 0; i < from.Fronts.Length; i++)
        {
            MetFront a = from.Fronts[i], b = to.Fronts[i];
            b.Kind = a.Kind; b.Strength = a.Strength; b.Count = a.Count; b.EtaMinutes = a.EtaMinutes; b.DirX = a.DirX; b.DirZ = a.DirZ;
            Array.Copy(a.X, b.X, a.X.Length); Array.Copy(a.Z, b.Z, a.Z.Length);
        }
        Array.Copy(from.Cells, to.Cells, from.Cells.Length); Array.Copy(from.Sets, to.Sets, from.Sets.Length);
        to.HasAnchor = from.HasAnchor; to.AnchorMark = from.AnchorMark; to.AnchorX = from.AnchorX; to.AnchorZ = from.AnchorZ; to.CamX = from.CamX; to.CamZ = from.CamZ; to.CamHeadingDeg = from.CamHeadingDeg;
        to.HalfXKm = from.HalfXKm; to.HalfZKm = from.HalfZKm; to.ForcedRain = from.ForcedRain; to.HasOutlook = from.HasOutlook; to.OutlookHeld = from.OutlookHeld;
        Array.Copy(from.Outlook, to.Outlook, from.Outlook.Length);
        to.SeedMode = from.SeedMode; to.SeedHex = from.SeedHex; to.TapeHex = from.TapeHex; to.SerialHex = from.SerialHex; to.LayoutSalt = from.LayoutSalt;
        to.MissionSeconds = from.MissionSeconds; to.LogText = from.LogText;
    }

    // ---- Fixtures: the mockup's busy host and its variants --------------------------------------------------------------------

    private static void Front(MetOfficeModel m, int slot, MetFrontKind kind, float x, float phase)
    {
        MetFront f = m.Fronts[slot];
        f.Kind = kind; f.Count = MetFront.MaxPoints; f.Strength = 0.8f; f.DirX = 1f; f.DirZ = 0f; f.EtaMinutes = float.NaN;
        for (int i = 0; i < f.Count; i++) { float z = -150f + i * 300f / (f.Count - 1); f.X[i] = x + 6f * Mathf.Sin(z * 0.05f + phase); f.Z[i] = z; }
        m.FrontCount = Math.Max(m.FrontCount, slot + 1);
    }

    private static MetOfficeModel Busy()
    {
        var m = new MetOfficeModel
        {
            Host = true, Captured = true, HasCamera = true, Class = WeatherRegimeType.Broken, Cover = 0.56f, SkyName = "Lowering altostratus", Metar = "FEW023 BKN105 SCT270",
            Running = true, Pace = 3, SpeedMps = 27f, WindMps = 12f, WindFromDeg = 270, HalfXKm = 81.92f, HalfZKm = 40.96f, CamHeadingDeg = 45f,
            HasAnchor = true, AnchorX = 21f, AnchorZ = 21f, HasOutlook = true, StripNote = "NEXT OVC IN 14:00", SeedMode = "Mission", SeedHex = "9A3F21C7",
            TapeHex = "9A3F21C7", SerialHex = "9A3F", LayoutSalt = 7, MissionSeconds = 2467f,
            LogText = "00:42:08 HOST SUPERCELL ON FADE 20 S\n00:41:30 HOST PACE 2 → 3 SMOOTH 20 → 27 M/S\n00:40:12 HOST LENTICULARS ON FADE 20 S\n00:38:55 HOST TAPE RESUMED SMOOTH",
        };
        m.Layers[0] = new MetLayer { Present = true, Label = "HIGH", BaseMetres = 8300f, Cover = 0.5f };
        m.Layers[1] = new MetLayer { Present = true, Label = "MID", BaseMetres = 3200f, Cover = 0.8f };
        m.Layers[2] = new MetLayer { Present = true, Label = "LOW", BaseMetres = 700f, Cover = 0.15f };
        Front(m, 0, MetFrontKind.Warm, -48f, 0.4f); Front(m, 1, MetFrontKind.Cold, -106f, 1.2f); Front(m, 2, MetFrontKind.Squall, 20f, 2.1f);
        m.CellCount = 6;
        for (int i = 0; i < 6; i++) m.Cells[i] = new MetCell { X = -118f + (i % 3) * 3f, Z = -96f + i * 36f, Radius = 6f + i, Severe = i == 2 };
        m.Sets[1] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = -95f, Z = 22f, Strength = 0.62f };
        m.Sets[4] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = 21f, Z = 21f, Strength = 1f };
        m.Sets[0] = new MetSetPiece { Forced = false, Present = true, Placed = true, X = 20f, Z = 60f, Strength = 0.4f };
        m.AnchorMark = 1;                                                        // the host put the anchor 30 km ahead
        for (int i = 0; i < 9; i++) m.Segments[i] = default;                      // no story yet (4B)
        m.BlockCount = 2;
        m.Blocks[0] = new MetBlock { FromMin = -93f, ToMin = 14f, Class = WeatherRegimeType.Broken, Label = "BKN" };
        m.Blocks[1] = new MetBlock { FromMin = 14f, ToMin = 93f, Class = WeatherRegimeType.Overcast, Label = "OVC" };
        m.MarkCount = 1; m.Marks[0] = new MetMark { AtMin = 14f, Step = true, Label = "STEP" };
        string[] codes = { "BKN", "OVC", "RA+", "RA+", "RA+", "BKN" };
        WeatherRegimeType[] classes = { WeatherRegimeType.Broken, WeatherRegimeType.Overcast, WeatherRegimeType.RainSquall, WeatherRegimeType.RainSquall, WeatherRegimeType.RainSquall, WeatherRegimeType.Broken };
        float[] rain = { 0f, 0f, 0.2f, 0.7f, 0.12f, 0f };
        for (int i = 0; i < 6; i++) m.Outlook[i] = new MetOutlookCell { Minutes = WeatherForecast.DefaultOffsetsMinutes[i], Class = classes[i], Code = codes[i], Rain = rain[i] };
        return m;
    }

    private static MetOfficeModel State(string name)
    {
        MetOfficeModel m = Busy();
        switch (name)
        {
            case "pinned-held":
                m.PinnedSky = 26; m.SkyNumber = 27; m.SkyName = "Lowering altostratus"; m.Running = false; m.OutlookHeld = true; m.FrontCount = 0; m.ForcedRain = 0.4f; m.Pace = 6; m.SpeedMps = 45f;
                m.BlockCount = 1; m.MarkCount = 0; m.StripNote = "HELD";
                break;
            case "storm":
                m.Class = WeatherRegimeType.Storm; m.Cover = 0.97f; m.Metar = "BKN012 OVC095 +RA TS"; m.ForcedRain = 1f; m.HasAnchor = false; m.Pace = 1; m.SpeedMps = 18f;
                for (int i = 0; i < 6; i++) m.Outlook[i].Class = WeatherRegimeType.Storm;
                m.Sets[3] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = 0f, Z = 0f, Strength = 1f };
                break;
            case "longest":
                int longest = 0;
                for (int i = 1; i < SkyLibrary.Count; i++) if (SkyLibrary.Get(i).Name.Length > SkyLibrary.Get(longest).Name.Length) longest = i;
                m.PinnedSky = longest; m.SkyNumber = longest + 1; m.SkyName = SkyLibrary.Get(longest).Name; m.Class = SkyLibrary.ClassOf(longest);
                // the worst host-log lines the adapter can write (28-character notes), and a story with all nine segments
                m.LogText = string.Join("\n", MetOfficeText.LogLine(35999f, "HOST", MetOfficeText.HoldNote(true)), MetOfficeText.LogLine(35990f, "HOST", MetOfficeText.SetNote(4, false)),
                    MetOfficeText.LogLine(35980f, "HOST", MetOfficeText.PinNote(longest)), MetOfficeText.LogLine(35970f, "HOST", MetOfficeText.ScenarioNote(4)));
                m.HasStory = true; m.StoryName = "Pre-frontal warm sector"; m.SkyRole = "PRE-WARM"; m.StoryIndex = 3; m.Pending = "NEXT SKY IN 00:20";
                for (int i = 0; i < 9; i++) m.Segments[i] = new MetSegment { Present = true, Label = MetCatalog.ClassCodes[i % 7], Class = (WeatherRegimeType)(i % 7) };
                break;
            case "client":
                // a client's own Pace and SeedMode settings disagree with the host's world: the window must print neither (only the speed travels in the key)
                m.Host = false; m.LogText = ""; m.Pace = 5; m.SpeedMps = 18f; m.SeedMode = "Fixed";
                break;
            case "cluttered":
                m.CamX = 118f; m.CamZ = 125f; m.CamHeadingDeg = 40f; m.HasAnchor = true; m.AnchorX = 118f; m.AnchorZ = 125f; m.AnchorMark = 0;   // ANCHOR HERE: anchor and camera on one pixel
                m.WindFromDeg = 90; m.WindMps = 25f;                                                                                              // a wind from the east
                m.Sets[3] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = 118f, Z = 125f, Strength = 1f };                   // storm eye on the camera
                m.Sets[4] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = 148f, Z = 125f, Strength = 1f };                   // lenticulars 30 km east of it
                m.Sets[1] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = 133f, Z = 112f, Strength = 1f };                   // a supercell between them
                Front(m, 3, MetFrontKind.Cold, 128f, 0.9f);                                                                                       // and a front across all three
                break;
            case "fog":
                Array.Clear(m.Sets, 0, m.Sets.Length);
                m.Sets[5] = new MetSetPiece { Forced = true, Present = true };                                                                    // forced and drawn by the sky, nothing placed
                break;
            case "nan":
                m.CamX = float.NaN; m.Cover = float.NaN; m.WindMps = float.NaN; m.Sets[1].Strength = float.NaN; m.Cells[2].X = float.NaN;
                break;
            case "waiting": m.Captured = false; m.HasCamera = false; m.HasOutlook = false; break;
            case "empty":
                m.Class = WeatherRegimeType.Clear; m.Cover = 0.03f; m.Metar = "SKC"; m.FrontCount = 0; m.CellCount = 0; m.HasAnchor = false; m.HasCamera = false; m.BlockCount = 1; m.MarkCount = 0;
                Array.Clear(m.Sets, 0, m.Sets.Length); Array.Clear(m.Layers, 0, m.Layers.Length); m.LogText = ""; m.SeedHex = ""; m.StripNote = "HELD"; m.WindMps = 0f;
                break;
        }
        return m;
    }

    // ---- Rendering and the gate -----------------------------------------------------------------------------------------------

    private static MetOfficeWindow Build(RectTransform root, FakeWorld world)
    {
        MetOfficeWindow window = MetOfficeWindow.Create(root, world, world);
        window.Window.Root.localScale = Vector3.one * 1.5f;   // 1080p on the 1280 x 720 reference canvas
        window.ShowFrame();
        for (int i = 0; i < 8; i++) { window.Window.Ticker.TickNow(); Canvas.ForceUpdateCanvases(); }
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        window.RefreshNow();
        for (int i = 0; i < 4; i++) { window.Window.Ticker.TickNow(); Canvas.ForceUpdateCanvases(); }
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        return window;
    }

    private static void Render(string state, AvThemeId theme, bool picture)
    {
        var canvasObject = new GameObject("MetPreview", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvasObject.transform;
        root.sizeDelta = new Vector2(1920f, 1080f);
        Backdrop(root);
        var world = new FakeWorld { Source = State(state) };
        MetOfficeWindow window = Build(root, world);
        string where = state + "/" + theme;
        window.Window.Root.localScale = Vector3.one;      // the gate measures in window units; the picture is taken at 1.5x below
        Gate(window.Window.Root, where);
        RequireSections(window.Window.Root, where, state);
        RequireStructure(window, where);
        StateExtras(window.Window.Root, where, state);
        if (Built >= 10) RequireSeat(window.Window.Root, where, state);
        Controls(window, where, state);
        Navigation(window.Window.Root, where);
        Tips(window, where);
        Columns(window, where);
        window.Window.Root.localScale = Vector3.one * 1.5f;
        if (picture)
        {
            Camera camera = OrthoCamera("Capture", 540f, new Color(0.02f, 0.03f, 0.03f));
            CapturePng(camera, 1920, 1080, Path.GetFullPath("renders/met-" + state + ".png"));
            Object.DestroyImmediate(camera.gameObject);
        }
        window.Close();
        Object.DestroyImmediate(canvasObject);
    }

    private static void Backdrop(RectTransform root)
    {
        var go = new GameObject("GameView", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(root, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.20f, 0.30f, 0.42f);
        img.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void Fail(string message) => Failures.Add(message);

    private static void Gate(RectTransform root, string where)
    {
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        var placed = new List<KeyValuePair<TMP_Text, Rect>>(400);
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t)) continue;
            gatedTexts++;
            t.ForceMeshUpdate();
            Rect r = t.rectTransform.rect;
            bool icon = t.name.StartsWith("Icon");
            Bounds b = t.textBounds;
            string tag = where + ": '" + t.text.Replace("\n", " / ") + "' (" + t.name + ")";
            if (!icon)
            {
                if (b.size.x > r.width + 1.5f) Fail(tag + " overflows width " + b.size.x.ToString("0") + " > " + r.width.ToString("0"));
                if (b.size.y > r.height + 1.5f) Fail(tag + " overflows height " + b.size.y.ToString("0") + " > " + r.height.ToString("0"));
                if (t.fontSize < AvTypeScale.Floor - 0.01f) Fail(tag + " below the 11 px floor " + t.fontSize.ToString("0.0"));
                if (t.isTextTruncated) Fail(tag + " is truncated");
            }
            AvControl owner = t.GetComponentInParent<AvControl>();
            bool disabled = owner != null && !owner.Interactable;
            if (!icon && !disabled && t.color.a > 0.5f)
            {
                Color back = BackgroundOf(t, ground);
                float contrast = Rgba.Contrast(t.color.ToRgba().WithAlpha(1f).Over(back.ToRgba()), back.ToRgba());
                if (contrast < 4.5f) Fail(tag + " contrast " + contrast.ToString("0.00"));
            }
            if (!icon)
            {
                Vector3 lo = root.InverseTransformPoint(t.rectTransform.TransformPoint(b.min));
                Vector3 hi = root.InverseTransformPoint(t.rectTransform.TransformPoint(b.max));
                var ink = Rect.MinMaxRect(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y), Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y));
                if (ink.xMin < root.rect.xMin - 1f || ink.xMax > root.rect.xMax + 1f || ink.yMin < root.rect.yMin - 1f || ink.yMax > root.rect.yMax + 1f) Fail(tag + " outside the window");
                placed.Add(new KeyValuePair<TMP_Text, Rect>(t, ink));
            }
        }
        for (int i = 0; i < placed.Count; i++)
            for (int j = i + 1; j < placed.Count; j++)
            {
                Rect a = placed[i].Value, b = placed[j].Value;
                float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                if (w > 1f && h > 1.5f)
                    Fail(where + ": texts overlap '" + placed[i].Key.text.Replace("\n", " / ") + "' (" + placed[i].Key.name + ") and '" + placed[j].Key.text.Replace("\n", " / ") + "' (" + placed[j].Key.name + ")");
            }
    }

    private static Color BackgroundOf(TMP_Text t, Color ground)
    {
        for (Transform x = t.transform.parent; x != null; x = x.parent)
            for (int ci = x.childCount - 1; ci >= 0; ci--)
            {
                Transform child = x.GetChild(ci);
                if (child == t.transform || !child.gameObject.activeInHierarchy) continue;
                var f = child.GetComponent<AvFrame>();
                var img = child.GetComponent<Image>();
                Color c = default; bool has = false;
                if (f != null && f.enabled && f.Fill && f.FillColor.a > 0.2f) { c = f.FillColor; has = true; }
                else if (img != null && img.enabled && img.color.a > 0.2f) { c = img.color; has = true; }
                if (!has) continue;
                var corners = new Vector3[4];
                ((RectTransform)child).GetWorldCorners(corners);
                Vector3 centre = t.rectTransform.TransformPoint(t.rectTransform.rect.center);
                if (centre.x >= corners[0].x && centre.x <= corners[2].x && centre.y >= corners[0].y && centre.y <= corners[2].y)
                {
                    Rgba o = c.ToRgba().Over(ground.ToRgba());
                    return new Color(o.R, o.G, o.B);
                }
            }
        return ground;
    }

    /// <summary>Flavour only: the wording the mockup shows must be somewhere in the window. This proves nothing about structure (generic words such as LOW, TIME or LOG are satisfied by other labels), which is <see cref="RequireStructure"/>'s job.</summary>
    private static void RequireSections(RectTransform root, string where, string state)
    {
        string all = "\n";
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false)) if (t.isActiveAndEnabled && CanvasOn(t)) all += t.text.ToUpperInvariant() + "\n";
        for (int stage = 5; stage <= Math.Min(Built, 9); stage++)
            foreach (string s in MustByStage[stage - 5])
                if (!all.Contains(s)) Fail(where + ": missing '" + s + "' (stage " + stage + ")");
    }

    /// <summary>What each plan task adds to the window, by stage: 5 shell, 6 synoptic column, 7 instrument column, 8 time column, 9 command bar.</summary>
    private static readonly string[][] MustByStage =
    {
        new[] { "WEATHER // MET OFFICE", "SMOOTH", "FADE 20 S", "CUT", "SYS", "MET" },
        new[] { "SYNOPTIC CHART", "NEXT FRONTS", "IDENTITY", "RE-ROLL", "R4", "R8", "4B", "THEATER", "WARM", "COLD", "OCCL.", "SQUALL", "CELL", "SET-PC", "CAM", "ANCHOR" },
        new[] { "SKY NOW", "PIN SKY", "OUTLOOK 60 MIN", "SET-PIECES", "METAR", "COVER", "HIGH", "MID", "LOW", "CLR", "FEW", "SCT", "BKN", "OVC", "RA+", "TS", "AUTO", "SQUALL LINE", "SUPERCELL",
            "DISTANT CB", "STORM EYE", "LENTICULARS", "SEA FOG", "RAIN", "R1", "LIVE", "NOW" },
        new[] { "STORY", "TIME", "SCENARIOS", "LOG", "PACE", "FRONT IN", "HURRICANE EYE", "SQUALL ASSAULT", "MOUNTAIN WAVE", "FRONTAL PASSAGE", "RESET ALL", "S5", "R3", "TAPE" },
        new[] { "MET>", "BKSP", "P  PIN SKY", "R  RE-ROLL" },
    };

    private static Transform Find(Transform root, string name, bool prefix = false)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(false))
            if (t.gameObject.activeInHierarchy && (prefix ? t.name.StartsWith(name, StringComparison.Ordinal) : t.name == name)) return t;
        return null;
    }

    private static int CountOf(Transform root, string name)
    {
        int n = 0;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(false)) if (t.gameObject.activeInHierarchy && t.name == name) n++;
        return n;
    }

    private static void Need(Transform root, string where, string name, bool prefix = false)
    {
        if (Find(root, name, prefix) == null) Fail(where + ": missing active '" + name + "'");
    }

    /// <summary>A card exists as the active object MetCard names "Card &lt;TITLE&gt;" and its title text equals the title exactly.</summary>
    private static void NeedCard(Transform root, string where, string title)
    {
        Transform card = Find(root, "Card " + title);
        if (card == null) { Fail(where + ": missing card '" + title + "'"); return; }
        Transform t = card.Find("Title");
        TMP_Text text = t != null ? t.GetComponent<TMP_Text>() : null;
        if (text == null || text.text != title) Fail(where + ": card '" + title + "' has the title '" + (text != null ? text.text : "(none)") + "'");
    }

    /// <summary>Structure, not substrings: every card, section header, chip, cell, tile and pip of the mockup must exist by name or count, so deleting one cannot hide behind another label.</summary>
    private static void RequireStructure(MetOfficeWindow window, string where)
    {
        RectTransform root = window.Window.Root;
        if (Built >= 6)
        {
            Need(root, where, "ChartColumn"); Need(root, where, "Section SYNOPTIC CHART"); Need(root, where, "Synoptic chart");
            NeedCard(root, where, "NEXT FRONTS"); NeedCard(root, where, "IDENTITY"); Need(root, where, "Control RE-ROLL");
        }
        if (Built >= 7)
        {
            Need(root, where, "InstrumentColumn"); NeedCard(root, where, "SKY NOW"); NeedCard(root, where, "PIN SKY");
            Need(root, where, "Section OUTLOOK 60 MIN"); Need(root, where, "Section SET-PIECES"); Need(root, where, "Segmented ANCHOR"); Need(root, where, "Segmented RAIN");
            foreach (string code in MetCatalog.ClassCodes) Need(root, where, "Control " + code);                // the seven class chips
            foreach (string set in MetCatalog.SetNames) Need(root, where, "Control " + set);                    // the six set-piece cells
            for (int i = 0; i < MetOfficeModel.LayerCount; i++) Need(root, where, "LayerKey" + i);              // the three layer tiles
            for (int i = 0; i < MetOfficeModel.OutlookCount; i++) Need(root, where, "Time" + i);                // the six outlook cells
        }
        if (Built >= 8)
        {
            Need(root, where, "TimeColumn"); NeedCard(root, where, "STORY"); NeedCard(root, where, "TIME"); NeedCard(root, where, "SCENARIOS"); NeedCard(root, where, "LOG · PENDING");
            Need(root, where, "Row FRONT IN"); Need(root, where, "Control HOLD TAPE");
            if (CountOf(root, "StepRow") != 3) Fail(where + ": expected the story, pin and pace step rows, found " + CountOf(root, "StepRow"));
            foreach (string scenario in MetCatalog.ScenarioNames) Need(root, where, "Control " + scenario, true);
            if (window.Probe("pips") != MetOfficeModel.SegmentCount) Fail(where + ": the story strip painted " + window.Probe("pips") + " pips");
        }
        if (Built >= 9)
        {
            Transform prompt = Find(root, "CommandPrompt");
            if (prompt == null || prompt.GetComponent<TMP_Text>().text != "MET>") Fail(where + ": missing the MET> prompt");
            if (window.Probe("bar") != 8) Fail(where + ": the command line has " + window.Probe("bar") + " buttons, expected 8");
        }
    }

    /// <summary>What only some fixtures can prove: a forced fog bank draws nothing on the chart, a client never prints another seat's settings, and nothing prints NaN.</summary>
    private static void StateExtras(RectTransform root, string where, string state)
    {
        string all = AllText(root);
        if (System.Text.RegularExpressions.Regex.IsMatch(all, @"\bNAN\b|INFINITY")) Fail(where + ": a non-finite number reached the screen");
        Transform chartColumn = root.Find("ChartColumn");
        if (Built >= 6 && state == "fog")
        {
            foreach (TMP_Text t in chartColumn.GetComponentsInChildren<TMP_Text>(false))
                if (t.isActiveAndEnabled && t.text.ToUpperInvariant().Contains("SEA FOG")) Fail(where + ": the chart labels a fog bank that is not placed: '" + t.text + "'");
        }
        if (Built >= 7 && state == "fog" && !all.Contains("LOW LAYER")) Fail(where + ": the fog cell lost its LOW LAYER line");
        if (Built >= 6 && state == "cluttered")
        {
            string chartText = AllText((RectTransform)chartColumn);
            foreach (string label in new[] { "STORM EYE 100%", "LENTICULARS 100%", "SUPERCELL 100%" })
                if (!chartText.Contains(label)) Fail(where + ": the cluttered chart lost its label '" + label + "'");
        }
        if (Built >= 7 && state == "cluttered" && !System.Text.RegularExpressions.Regex.IsMatch(all, @"(?<!\d)0 KM"))
            Fail(where + ": the storm eye cell on the camera does not read 0 KM (set-pieces measure from the camera, not the theatre centre)");
        if (Built >= 8 && state == "client")
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(all, @"\bX5\b")) Fail(where + ": a client prints the pace of its own settings");
            if (!all.Contains("— · SEED")) Fail(where + ": a client prints a seed mode that is not the host's");
        }
    }

    /// <summary>Spec 6.4 (the old console check kept it): no Selectable under the window may answer flight-stick navigation.</summary>
    private static void Navigation(RectTransform root, string where)
    {
        foreach (Selectable s in root.GetComponentsInChildren<Selectable>(true))
            if (s.navigation.mode != UnityEngine.UI.Navigation.Mode.None) Fail(where + ": selectable responds to flight navigation " + s.name);
    }

    /// <summary>Every hover sentence (the help of each control and the tip of each part, chips and tags included) is at most 150 characters and, set into the footer, fits its box at or above the 11 px floor.</summary>
    private static void Tips(MetOfficeWindow window, string where)
    {
        TMP_Text footer = window.Window.Footer.Rect.Find("Text").GetComponent<TMP_Text>();
        var seen = new HashSet<string>();
        foreach (AvHelpTip tip in window.Window.Root.GetComponentsInChildren<AvHelpTip>(true))
        {
            string sentence = tip.Text;
            if (string.IsNullOrEmpty(sentence) || !seen.Add(sentence)) continue;
            hovers++;
            if (sentence.Length > MetCatalog.HelpLimit) Fail(where + ": hover sentence of " + sentence.Length + " characters: '" + sentence + "'");
            window.Window.Footer.SetHint(sentence);
            footer.ForceMeshUpdate();
            Rect r = footer.rectTransform.rect;
            if (footer.textBounds.size.y > r.height + 1.5f || footer.textBounds.size.x > r.width + 1.5f) Fail(where + ": hover sentence overflows the footer: '" + sentence + "'");
            if (footer.fontSize < AvTypeScale.Floor - 0.01f) Fail(where + ": hover sentence below the 11 px floor (" + footer.fontSize.ToString("0.0") + "): '" + sentence + "'");
            if (footer.isTextTruncated) Fail(where + ": hover sentence is truncated: '" + sentence + "'");
        }
        window.Window.Footer.SetHint(null);
    }

    private static string AllText(RectTransform root)
    {
        string all = "\n";
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false)) if (t.isActiveAndEnabled && CanvasOn(t)) all += t.text.ToUpperInvariant() + "\n";
        return all;
    }

    /// <summary>The seat is always stated: HOST, READ ONLY with the reason and a host-less log, or WAITING FOR MISSION ENVIRONMENT.</summary>
    private static void RequireSeat(RectTransform root, string where, string state)
    {
        string all = AllText(root);
        void Need(string s) { if (!all.Contains(s)) Fail(where + ": missing '" + s + "'"); }
        if (state == "host") Need("HOST // ALL PLAYERS");
        if (state == "client") { Need("READ ONLY"); Need("HOST CONTROLS THE WEATHER"); Need("HOST ACTIONS ARE LOGGED ON THE HOST"); }
        if (state == "waiting") Need("WAITING FOR MISSION ENVIRONMENT");
        if (state == "client" && all.Contains("HOST // ALL PLAYERS")) Fail(where + ": a client must not read HOST // ALL PLAYERS");
    }

    private static void Controls(MetOfficeWindow window, string where, string state)
    {
        var rects = new List<KeyValuePair<AvHit, Rect>>();
        foreach (AvHit hit in window.Window.Root.GetComponentsInChildren<AvHit>(false))
        {
            if (!hit.isActiveAndEnabled) continue;
            hits++;
            var corners = new Vector3[4];
            ((RectTransform)hit.transform).GetWorldCorners(corners);
            Vector3 lo = window.Window.Root.InverseTransformPoint(corners[0]), hi = window.Window.Root.InverseTransformPoint(corners[2]);
            rects.Add(new KeyValuePair<AvHit, Rect>(hit, Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y)));
        }
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                Rect a = rects[i].Value, b = rects[j].Value;
                float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                if (w > 1f && h > 1f)
                    Fail(where + ": hit regions overlap " + rects[i].Key.transform.parent.name + " and " + rects[j].Key.transform.parent.name);
            }
        foreach (AvControl c in window.Window.Root.GetComponentsInChildren<AvControl>(false))
        {
            if (c.Interactable || c == window.Window.CloseControl) continue;
            string help = c.Help ?? "";
            if (help.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) < 0 && help.IndexOf("arrives", StringComparison.OrdinalIgnoreCase) < 0)
                Fail(where + ": disabled control '" + c.name + "' does not say why: '" + help + "'");
        }
        int enabled = 0;
        foreach (AvControl c in window.Window.Root.GetComponentsInChildren<AvControl>(false)) if (c.Interactable && c != window.Window.CloseControl) enabled++;
        if (Built >= 10 && (state == "client" || state == "waiting") && enabled != 0) Fail(where + ": " + enabled + " controls are enabled for a " + state + " seat");
        if (Built >= 10 && state == "host" && enabled < 30) Fail(where + ": only " + enabled + " controls are enabled for a ready host");
    }

    /// <summary>Each column's parts end above the command bar, in window units.</summary>
    private static void Columns(MetOfficeWindow window, string where)
    {
        RectTransform root = window.Window.Root;
        float limit = root.rect.yMax - MetOfficeLayout.BodyBottom;
        string[] columns = { "ChartColumn", "InstrumentColumn", "TimeColumn" };
        for (int c = 0; c < columns.Length && c + 6 <= Built; c++)
        {
            string name = columns[c];
            Transform column = root.Find(name);
            if (column == null) { Fail(where + ": no " + name); continue; }
            foreach (RectTransform part in column.GetComponentsInChildren<RectTransform>(false))
            {
                if (part == column || part.parent != column) continue;
                var corners = new Vector3[4];
                part.GetWorldCorners(corners);
                float bottom = root.InverseTransformPoint(corners[0]).y;
                if (bottom < limit - 0.5f) Fail(where + ": " + name + "/" + part.name + " ends " + (limit - bottom).ToString("0.0") + " units below the body");
            }
        }
    }

    // ---- A click is one request --------------------------------------------------------------------------------------------------

    /// <summary>The request the Busy fixture (pace 3, running, supercell and lenticulars forced, an anchor, class BKN, no pin) must produce for a click on <paramref name="id"/>; null = no request.</summary>
    private static string ExpectedCall(MetControl id)
    {
        byte forced = (byte)(MetCatalog.SetBits[1] | MetCatalog.SetBits[4]);
        switch (id)
        {
            case MetControl.Hold: return "tape False";
            case MetControl.PaceDown: return "pace 2";
            case MetControl.PaceUp: return "pace 4";
            case MetControl.PinAuto: return "pin -1";
            case MetControl.PinPrev: return "pin 27";           // BKN is skies 22-28: the picker starts on 22 (index 21); a step back wraps to 28 (index 27) ...
            case MetControl.PinNext: return "pin 21";           // ... and a step on wraps to 22 again
            case MetControl.PinSelected: return "pin 21";
            case MetControl.Reroll: return "reroll";
            case MetControl.AnchorHere: return "ahead 0";
            case MetControl.AnchorAhead: return "ahead 30000";
            case MetControl.AnchorDefault: return "default";
            case MetControl.RainAuto: return "rain auto";
            case MetControl.RainDry: return "rain 0.0";
            case MetControl.RainLight: return "rain 0.4";
            case MetControl.RainHeavy: return "rain 1.0";
            default:
                if (id >= MetControl.SetSquall && id <= MetControl.SetSeaFog) return "sets " + (byte)(forced ^ MetCatalog.SetBits[MetCatalog.SetIndex(id)]);
                if (id >= MetControl.ScenHurricane && id <= MetControl.ScenReset) return "scenario " + MetCatalog.Scenarios[MetCatalog.ScenarioIndex(id)];
                return null;                                    // story and front-in-N: disabled until 4B / S5, so no request
        }
    }

    private static void CheckPresses()
    {
        var canvasObject = new GameObject("MetPress", typeof(RectTransform), typeof(Canvas));
        var root = (RectTransform)canvasObject.transform;
        root.sizeDelta = new Vector2(1920f, 1080f);
        var world = new FakeWorld { Source = Busy() };
        MetOfficeWindow w = Build(root, world);

        // Every control of the catalog, in catalog order, clicked through its AvHit like the real mouse: wrong wiring (a Spec lambda, a Reg() on the wrong id, a step row's minus and plus) shows here.
        foreach (MetControl id in (MetControl[])Enum.GetValues(typeof(MetControl)))
        {
            if (id == MetControl.PinClass) continue;            // the seven class chips, below
            AvControl c = w.ControlOf(id);
            if (c == null) { Fail("no control is registered as " + id); continue; }
            string label = c.name.Substring("Control ".Length).Split('\n')[0];
            if (label.Length > 0 && MetCatalog.Info(id).Name.IndexOf(label, StringComparison.Ordinal) < 0)
                Fail("control '" + c.name + "' is registered as " + id + " (" + MetCatalog.Info(id).Name + ")");
            MetControl at = id;
            Expect(world, at.ToString(), () => w.Click(at), ExpectedCall(at));
        }

        // The command line, in the order of its buttons (Hold, Pace, Story, Pin sky, Front in 5, Re-roll, Rain, Reset all); the picker still stands on sky 22.
        string[] bar = { "tape False", "pace 4", null, "pin 21", null, "reroll", "rain 0.0", "scenario ResetAll" };
        Check(w.Probe("bar") == bar.Length, "the command line has " + w.Probe("bar") + " buttons");
        for (int i = 0; i < bar.Length; i++) { int at = i; Expect(world, "bar button " + i, () => w.ClickPart("bar", at), bar[i]); }
        world.Source.Pace = 6;
        Expect(world, "bar PACE at the fastest pace", () => w.ClickPart("bar", 1), "pace 1");      // the one-button pace wraps; the , and . keys step down and up

        // The seven class chips pin the first sky of their class.
        for (int i = 0; i < 7; i++) { int cls = i; Expect(world, "class chip " + cls, () => w.ClickPart("class", cls), "pin " + MetSkyPicker.FirstOf((WeatherRegimeType)cls)); }

        // The world moved since the last refresh: a click reads it again.
        world.Source.Pace = 5;
        Expect(world, "PaceUp after the world moved", () => w.Click(MetControl.PaceUp), "pace 6");
        world.Source.Pace = 3;

        // A client, and a world that is not ready, send nothing whatever is clicked.
        foreach (string seat in new[] { "client", "waiting" })
        {
            world.Source.Host = seat != "client"; world.Source.Captured = seat != "waiting";
            foreach (MetControl id in (MetControl[])Enum.GetValues(typeof(MetControl)))
            {
                if (id == MetControl.PinClass) continue;
                MetControl at = id;
                Expect(world, seat + " " + at, () => w.Click(at), null);
            }
            for (int i = 0; i < 7; i++) { int cls = i; Expect(world, seat + " class chip " + cls, () => w.ClickPart("class", cls), null); }
            for (int i = 0; i < bar.Length; i++) { int at = i; Expect(world, seat + " bar button " + at, () => w.ClickPart("bar", at), null); }
        }
        Check(w.Window.Footer != null, "the window has a footer");
        w.Close();
        Object.DestroyImmediate(canvasObject);
    }

    /// <summary>Runs <paramref name="act"/> (a click) and compares the single request the fake world recorded with <paramref name="call"/> (null = none).</summary>
    private static void Expect(FakeWorld world, string what, Func<bool> act, string call)
    {
        world.Calls.Clear();
        if (!act()) Fail("click " + what + ": there is nothing to click");
        string got = world.Calls.Count == 0 ? null : string.Join("|", world.Calls);
        if (got != call) Fail("click " + what + ": expected " + (call ?? "no request") + " got " + (got ?? "no request"));
    }
}
#endif

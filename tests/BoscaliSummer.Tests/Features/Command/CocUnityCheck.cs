#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Presentation;
using BoscaliSummer.Modules.Command.Runtime;
using BoscaliSummer.Core.Contracts;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Standalone render check for the STR console — SITUATION (threat ladder, force balance, air tasking
/// order, readout tiles), COMMAND (org chart, dossier, staff log) and OPERATIONS (op card, proposal cards,
/// front rows, posture) pages, plus the operations-room floating window — all on kit v2 with the STR
/// module's own parts. The run FAILS on text overflow, 11 px floor breaches, gutter entry, contrast below
/// 4.5 or overlapping parts (the same gate as the kit gallery). None of this can be exercised
/// by the pure net8 tests. This check builds the real console and the real page builders from
/// the production sources, feeds them a deterministic stubbed IHighCommandView staff and writes
/// one PNG per scenario so the page can be reviewed without launching the game.
///
/// Game/domain adapters the panel compiles against live in SettingsUnityStubs.cs. Production
/// members are reached only through reflection; no production file is modified.
/// </summary>
public static class CocUnityCheck
{
    private const float Width = AvTokens.PanelWidth;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly List<string> Notes = new List<string>();
    private static int captures;

    public static void Run()
    {
        try
        {
            if (Shader.Find("TextMeshPro/Distance Field") == null)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += Run;
                AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
                return;
            }

            var setPaths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var parameters = setPaths.GetParameters();
            var arguments = new object[parameters.Length];
            arguments[0] = Path.GetFullPath("CocCheck.exe");
            for (int i = 1; i < arguments.Length; i++) arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            setPaths.Invoke(null, arguments);

            AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvStyleHost.SetTheme(AvThemeId.Portal);
            new GameObject("Events", typeof(EventSystem));

            IHighCommandView staff = Staff();

            // ---- COMMAND page: org chart, dossier, staff log ("tall" renders show the whole page in one PNG).
            RenderScenario(staff, 1500f, 0, false, "coc-tall-allied.png",
                "tall, ALLIED side, dossier GEN. D. HALVERSON (tier 0, long bio, two traits)");
            RenderScenario(staff, 596f, 0, false, "coc-596.png",
                "height 596 (AvTokens.PanelHeight), ALLIED side, dossier open");
            RenderScenario(staff, 896f, 3, false, "coc-896.png",
                "height 896 (AvTokens.PanelHeightMax), ALLIED side, dossier MAJ. T. VOSSBERG (tier 2, InTransit)");
            RenderScenario(staff, 1500f, 6, true, "coc-tall-hostile.png",
                "tall, HOSTILE side, dossier COL. V. KRUPIN (known enemy, IntelAge 41s)");
            RenderScenario(staff, 1500f, 7, true, "coc-tall-hostile-sealed.png",
                "tall, HOSTILE side, dossier MAJ. E. ROUX (unconfirmed, sealed file)");
            RenderScenario(staff, 1500f, -1, false, "coc-tall-nopost.png",
                "tall, ALLIED side, no post open: one compact note instead of a dossier");
            RenderScenario(Staff(available: false), 596f, -1, false, "coc-nostaff.png",
                "height 596, no staff running: one note, the map highlight cleared");

            // ---- SITUATION + OPERATIONS: populated and empty states, tall and at the real panel heights.
            foreach (bool populated in new[] { true, false })
            {
                string tag = populated ? "populated" : "empty";
                GameObject sa = Build(1500f, staff, -1, false, 0, populated: populated);
                Capture(sa, 1500f, "situation-tall-" + tag + ".png");
                Gate(sa, "situation-" + tag, 0);
                Object.DestroyImmediate(sa);

                foreach (float height in new[] { 596f, 896f })
                {
                    GameObject page = Build(height, staff, -1, false, 0, populated: populated);
                    string prefix = "situation-" + tag + "-" + height;
                    Capture(page, height, prefix + ".png");
                    ScrollRect scroll = page.GetComponentInChildren<ScrollRect>();
                    if (scroll != null && scroll.content.rect.height > scroll.viewport.rect.height + 1f)
                    {
                        scroll.verticalNormalizedPosition = 0f;
                        Capture(page, height, prefix + "-bottom.png");
                    }
                    Object.DestroyImmediate(page);
                }
            }

            foreach (WarMode mode in new[] { WarMode.Rich, WarMode.Basic, WarMode.Idle, WarMode.Unavailable })
            {
                string tag = mode.ToString().ToLowerInvariant();
                GameObject ops = Build(1500f, staff, -1, false, 2, war: new WarStub(mode));
                Capture(ops, 1500f, "operations-tall-" + tag + ".png");
                Gate(ops, "operations-" + tag, 2);
                Object.DestroyImmediate(ops);
            }
            foreach (float height in new[] { 596f, 896f })
            {
                GameObject ops = Build(height, staff, -1, false, 2, war: new WarStub(WarMode.Rich));
                Capture(ops, height, "operations-rich-" + height + ".png");
                ScrollRect scroll = ops.GetComponentInChildren<ScrollRect>();
                if (scroll != null && scroll.content.rect.height > scroll.viewport.rect.height + 1f)
                {
                    scroll.verticalNormalizedPosition = 0f;
                    Capture(ops, height, "operations-rich-" + height + "-bottom.png");
                }
                Check(Array.Exists(ops.GetComponentsInChildren<TMP_Text>(true), t => t.text.Contains("RIDGE")),
                    "STR must show the staff's current proposal.");
                Object.DestroyImmediate(ops);
            }

            var mapRoot = new GameObject("MapCheck");
            DynamicMap liveMap = mapRoot.AddComponent<DynamicMap>();
            liveMap.mapImage = new GameObject("MapImage", typeof(RectTransform), typeof(Image))
                .GetComponent<Image>();
            liveMap.mapImage.transform.SetParent(mapRoot.transform, false);
            var mapTexture = new Texture2D(64, 48, TextureFormat.RGBA32, false);
            var mapPixels = new Color32[64 * 48];
            for (int py = 0; py < 48; py++)
                for (int px = 0; px < 64; px++)
                    mapPixels[py * 64 + px] = new Color32(
                        (byte)(18 + px / 3), (byte)(35 + py / 4),
                        (byte)(48 + (px + py) / 5), 255);
            mapTexture.SetPixels32(mapPixels);
            mapTexture.Apply();
            liveMap.mapImage.sprite = Sprite.Create(mapTexture,
                new Rect(0f, 0f, 64f, 48f), new Vector2(.5f, .5f));
            SceneSingleton<DynamicMap>.i = liveMap;

            foreach (WarMode mode in new[] { WarMode.Rich, WarMode.Idle, WarMode.Unavailable })
            {
                string tag = mode.ToString().ToLowerInvariant();
                StrPlanningWindow room = StrPlanningWindow.Create(new WarStub(mode), new ComMapOverlay());
                room.Show();
                var roomWindow = (AvWindow)GetFieldValue(room, "window");
                for (int i = 0; i < 4; i++) roomWindow.Ticker.TickNow();
                Check(Array.Exists(room.GetComponentsInChildren<TMP_Text>(true), t =>
                    t.text.Contains("OPERATIONS ROOM")), "Room must keep the AvWindow title chrome.");
                if (mode == WarMode.Rich)
                {
                    Check(Array.Exists(room.GetComponentsInChildren<TMP_Text>(true), t =>
                        t.text.Contains("NORTH RIDGE")), "Room must name the active operation.");
                    object mapPart = GetFieldValue(room, "map");
                    Image[] frontPins = (Image[])mapPart.GetType().GetField("frontMarkers", Private).GetValue(mapPart);
                    Check(frontPins[0].enabled && !frontPins[1].enabled,
                        "Only observed fronts may receive an exact map marker.");
                }
                CaptureWindow(room, "war-room-" + tag + "-1920.png");
                GateWindow(room, "war-room-" + tag);
                if (mode == WarMode.Rich) CaptureWindow(room, "war-room-rich-1280.png", 1280f, 720f);
                room.Close();
                Check(!StrPlanningWindow.IsOpen, "Closing the room must release the input guard.");
                Object.DestroyImmediate(room.gameObject);
            }
            SceneSingleton<DynamicMap>.i = null;
            Object.DestroyImmediate(mapRoot);
            Object.DestroyImmediate(mapTexture);

            if (Failures.Count > 0)
            {
                File.WriteAllText("result.txt", "FAIL: " + Failures.Count + " gate failure(s)\n" + string.Join("\n", Failures));
                EditorApplication.Exit(1);
                return;
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine("PASS: the real STR console pages and live operations room rendered offline on kit v2; gate clean (" + checkedTexts + " texts checked: no overflow, no 11px-floor breach, no gutter entry, contrast >= 4.5, no part overlap).");
            report.AppendLine(captures + " captures: COMMAND allied/hostile/sealed/no-post/no-staff, SITUATION populated+empty, OPERATIONS rich/basic/idle/unavailable, operations room x3 modes.");
            report.AppendLine("Content is bound after Finish() and advanced with Ticker.TickNow(), as the game does; the stub IHighCommandView records Highlight(id) and every COMMAND scenario asserts it matches the open file.");
            report.AppendLine("Renders (path | bytes | setup):");
            foreach (string note in Notes) report.AppendLine(note);
            report.AppendLine("Reflection used: fields console/highCommand/settings/command/overlay/theaterWar, cocShowHostile, cocSelectedId, cocBuilt, radarTile/adTile; methods BuildSaPage/BuildCocPage/BuildCmdPage, Refresh(), SelectCoc(int).");
            File.WriteAllText("result.txt", report.ToString());
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    // ------------------------------------------------------------------ scenarios

    private enum WarMode { Rich, Basic, Idle, Unavailable }

    private sealed class WarStub : ITheaterWarView
    {
        private readonly WarMode mode;
        public WarStub(WarMode mode)
        {
            this.mode = mode;
            bool rich = mode == WarMode.Rich, basic = mode == WarMode.Basic;
            Fronts = rich ? new[]
                {
                    new TheaterFrontView("ridge", "NORTH RIDGE", 1200f, 1700f, "IN CONTACT", .75f, .1f, true, 0f),
                    new TheaterFrontView("harbor", "HARBOR RUMOR", float.NaN, float.NaN, "UNCONFIRMED", .2f, 0f, false, 45f),
                    new TheaterFrontView("depot", "WEST DEPOT", -2000f, 900f, "UNDER PRESSURE", .62f, .05f, true, 0f),
                    new TheaterFrontView("pass", "EASTERN PASS", 2800f, -600f, "HOLDING", .18f, -.05f, true, 0f),
                    new TheaterFrontView("strip", "CAPTURE AIRSTRIP", -500f, -2200f, "UNCONFIRMED", 0f, 0f, false, -1f),
                    new TheaterFrontView("f45", "FRONT -5/-4", -5000f, -4000f, "UNCONFIRMED", 0f, 0f, false, -1f),
                    new TheaterFrontView("f81", "FRONT -8/-1", -8000f, -1000f, "UNDER PRESSURE", 1f, .1f, true, 0f),
                }
                : basic ? new[]
                {
                    new TheaterFrontView("ridge", "NORTH RIDGE", 1200f, 1700f, "IN CONTACT", .75f, .1f, true, 0f),
                    new TheaterFrontView("harbor", "HARBOR RUMOR", float.NaN, float.NaN, "UNCONFIRMED", .2f, 0f, false, 45f),
                }
                : new TheaterFrontView[0];
            Proposals = rich ? new[]
                {
                    new TheaterProposalView(1, 3, "EXPLOIT", "NORTH RIDGE", "ridge", 1200f, 1700f,
                        "Pressure is shifting toward the ridge; a combined push could take it before the line reforms.", "MEDIUM", "2 ground, 1 air", 38f),
                    new TheaterProposalView(2, 3, "DEFEND", "HARBOR", "harbor", -1400f, -800f,
                        "Reinforce the approach.", "LOW", "1 ground, 1 naval", 31f),
                    new TheaterProposalView(3, 3, "ASSAULT", "WEST DEPOT", "depot", -2000f, 900f,
                        "Depot garrison is thin but the road is covered by known air defence.", "HIGH", "MISSION FORCES", 12f),
                }
                : basic ? new[]
                {
                    new TheaterProposalView(1, 3, "EXPLOIT", "NORTH RIDGE", "ridge", 1200f, 1700f, "Pressure is shifting.", "MEDIUM", "2 ground, 1 air", 38f),
                    new TheaterProposalView(2, 3, "DEFEND", "HARBOR", "harbor", -1400f, -800f, "Reinforce the approach.", "LOW", "1 ground, 1 naval", 38f),
                }
                : new TheaterProposalView[0];
            ActiveOperation = mode == WarMode.Rich || basic
                ? new TheaterLiveOperationView(7, 3, "ASSAULT", "ridge", "NORTH RIDGE", 1200f, 1700f,
                    "IN CONTACT", "Ground and air groups are pressing the ridge from two axes.", rich ? 7 : 2, rich ? 3 : 1, 0)
                : null;
        }

        public bool Available => mode != WarMode.Unavailable;
        public bool CanCommand => mode != WarMode.Unavailable;
        public TheaterWarPosture Posture => mode == WarMode.Rich ? TheaterWarPosture.Bold : TheaterWarPosture.Steady;
        public IReadOnlyList<TheaterFrontView> Fronts { get; }
        public IReadOnlyList<TheaterProposalView> Proposals { get; }
        public TheaterLiveOperationView ActiveOperation { get; }
        public IReadOnlyList<string> StaffLog { get; } = new[] { "Ridge pressure rose." };
        public void Refresh() { }
        public bool RequestPick(int proposalId, int revision) => true;
        public bool RequestCancel(int operationId, int revision) => true;
        public bool RequestPosture(TheaterWarPosture posture) => true;
    }

    private static void RenderScenario(
        IHighCommandView staff, float height, int selectedId, bool hostile, string file, string description)
    {
        GameObject canvas = Build(height, staff, selectedId, hostile);
        StrMfdPanel builtPanel = canvas.GetComponentInChildren<StrMfdPanel>();
        var console = (AvConsole)GetFieldValue(builtPanel, "console");
        string expected = !staff.Available ? "NO STAFF BOARD" : "NO FILE OPEN";
        if (staff.Available && selectedId >= 0)
            foreach (CommanderView commander in staff.Commanders)
                if (commander.Id == selectedId) expected = commander.Name;
        Check(Array.Exists(console.Page(1).Content.GetComponentsInChildren<TMP_Text>(false),
            t => t.text == expected), file + ": selected file/state was not bound: " + expected);
        string path = Capture(canvas, height, file);
        Gate(canvas, file, 1);
        if (selectedId >= 0)
        {
            StrMfdPanel panel = canvas.GetComponentInChildren<StrMfdPanel>();
            Call(panel, "SelectCoc", selectedId);
            Call(panel, "Refresh");
            SettleTicker(canvas);

            SetField(panel, "cocSelectedId", -1);
            Call(panel, "Refresh");
            Call(panel, "SelectCoc", selectedId);
            Call(panel, "Refresh");
        }
        Object.DestroyImmediate(canvas);

        // The page selects a post on the map: whatever file is open is the post the map
        // brackets, and a closed file clears it. This is the wiring check the render cannot show.
        var stub = (CocStaffStub)staff;
        Check(stub.HighlightedId == selectedId,
            file + ": the map highlight should be " + selectedId + " but the page asked for " +
            stub.HighlightedId);

        long bytes = new FileInfo(path).Length;
        Notes.Add(path + " | " + bytes + " bytes | highlight " + stub.HighlightedId + " | " + description);
        Debug.Log("[CocUnityCheck] " + path + " (" + bytes + " bytes)");
    }

    private static void SettleTicker(GameObject canvas)
    {
        StrMfdPanel panel = canvas.GetComponentInChildren<StrMfdPanel>();
        var con = (AvConsole)GetFieldValue(panel, "console");
        for (int i = 0; i < 4; i++) con.Ticker.TickNow();
    }

    private static TacticalTheaterState PopulatedState()
    {
        var s = new TacticalTheaterState
        {
            FriendlyAircraftCount = 3,
            HostileAircraftCount = 3,
            FriendlyAirbaseCount = 6,
            HostileAirbaseCount = 6,
            NeutralAirbaseCount = 0,
            ContestedAirbaseCount = 1,
            FriendlyRadarCount = 3,
            FriendlyGroundUnitsCount = 219,
            HostileGroundUnitsCount = 13,
            FriendlySectorCount = 3211,
            ContestedSectorCount = 236,
            HostileSectorCount = 3277,
            NeutralSectorCount = 240,
            TotalNodesCount = 132,
            FrontlineSegmentCount = 41,
            FrontlineLengthMetres = 112700f,
            TerritoryControlRatio = .49f,
            AirSuperiorityRatio = .5f,
            DefconLevel = 2,
            PrimaryThreatDescription = "ACTIVE GROUND BATTLE",
            ActiveThreatWarning = "AMBER ALERT: 236 CONTESTED SECTORS IN CONFLICT",
        };
        s.Sorties = new SortieTally { Cap = 4, Sead = 1, Cas = 6, Strike = 2, Transit = 3, Observed = 16 };
        return s;
    }

    private static void FillNodes(ComMapOverlay overlay)
    {
        var grid = overlay.Grid;
        var nodes = (List<TacticalSectorGrid.TacticalNode>)typeof(TacticalSectorGrid)
            .GetField("nodes", Private).GetValue(grid);
        nodes.Clear();
        string[] names = { "NORTH RIDGE AIRBASE", "KESTREL CROSSING", "WEST DEPOT", "HARBOR STRONGPOINT", "IRON PASS", "SOUTH FORD", "RADAR HILL" };
        for (int i = 0; i < names.Length; i++)
            nodes.Add(new TacticalSectorGrid.TacticalNode
            {
                IsContested = true,
                CaptureProgress = .9f - i * .12f,
                Faction = i % 2 == 0 ? SectorControl.Friendly : SectorControl.Hostile,
                Name = names[i],
                IsAirbase = i % 3 == 0,
            });
    }

    private static GameObject Build(float height, IHighCommandView staff, int selectedId, bool hostile,
        int pageIndex = 1, WarStub war = null, bool populated = false)
    {
        var canvasObject = new GameObject("CocCanvas", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(Width, height);

        var panelObject = new GameObject("StrMfdPanel");
        panelObject.transform.SetParent(canvasObject.transform, false);
        StrMfdPanel panel = panelObject.AddComponent<StrMfdPanel>();

        AvConsole con = AvConsole.Build((RectTransform)canvasObject.transform, "STR", "STRATEGY", 3, Width, height);
        con.Tabs((AvIcon.Radar2, "SITUATION"), (AvIcon.UsersGroup, "COMMAND"), (AvIcon.Flag, "OPERATIONS"));
        AvMetric[] metrics = con.Metrics("SECTORS", "AIR", "STAFF", "FRONT");

        var manager = new CommandManager();
        if (populated)
        {
            TacticalTheaterState st = PopulatedState();
            var target = manager.TheaterState;
            foreach (FieldInfo f in typeof(TacticalTheaterState).GetFields()) f.SetValue(target, f.GetValue(st));
        }

        SetField(panel, "console", con);
        SetField(panel, "metrics", metrics);
        SetField(panel, "highCommand", staff);
        SetField(panel, "settings", new CommandSettings());
        SetField(panel, "command", manager);
        if (populated)
        {
            var overlay = new ComMapOverlay();
            FillNodes(overlay);
            SetField(panel, "overlay", overlay);
        }
        if (pageIndex == 2) SetField(panel, "theaterWar", war ?? new WarStub(WarMode.Basic));

        if (pageIndex == 0) Call(panel, "BuildSaPage", con.Page(0));
        else if (pageIndex == 1)
        {
            Call(panel, "BuildCocPage", con.Page(1));
            SetField(panel, "cocBuilt", true);
        }
        else Call(panel, "BuildCmdPage", con.Page(2));
        con.Finish();
        con.SetPage(pageIndex);

        if (hostile) SetField(panel, "cocShowHostile", true);
        if (selectedId >= 0) SetField(panel, "cocSelectedId", selectedId);

        // Content binds AFTER Finish(), exactly as the game's 4 Hz refresh does, and the ticker advances
        // the way the game's does (offline, Unity time does not advance, so TickNow stands in).
        Call(panel, "Refresh");
        if (populated && pageIndex == 0)
        {
            // Tiles fed by Intel / the HQ sensor net cannot be reached offline; feed them the way the
            // panel would with a ready picture so the populated render shows the real figures.
            ((StrTile)GetFieldValue(panel, "radarTile")).Set("3", "FRIENDLY EMITTERS ON NET", AvState.Info);
            ((StrTile)GetFieldValue(panel, "adTile")).Set("57 SITES", "11 RADAR (45 PRE-WAR, 1 STALE)", AvState.Info);
        }
        for (int i = 0; i < 4; i++) con.Ticker.TickNow();
        return canvasObject;
    }

    private static string Capture(GameObject canvasObject, float height, string file, float width = Width,
        Vector2? center = null)
    {
        Canvas.ForceUpdateCanvases();
        // Offline executeMethod does not advance time: end the page-change scan cover before capturing
        // the settled page, otherwise the real bundle's effect hides every body despite a passing text gate.
        foreach (StrMfdPanel panel in canvasObject.GetComponentsInChildren<StrMfdPanel>())
            ((Image)typeof(AvConsole).GetField("scanCover", Private).GetValue(GetFieldValue(panel, "console"))).enabled = false;
        foreach (AvReveal reveal in canvasObject.GetComponentsInChildren<AvReveal>(true)) reveal.Finish();
        foreach (ScrollRect scroll in canvasObject.GetComponentsInChildren<ScrollRect>())
            scroll.Rebuild(CanvasUpdate.PostLayout);
        foreach (TMP_Text text in canvasObject.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvasObject.transform);

        int pixelWidth = Mathf.RoundToInt(width * 2f);
        int pixelHeight = Mathf.RoundToInt(height * 2f);

        var cameraObject = new GameObject("CocCamera", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = height * 0.5f;
        camera.transform.position = new Vector3(center?.x ?? 0f, center?.y ?? 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.04f, 0.07f, 0.06f);

        var target = new RenderTexture(pixelWidth, pixelHeight, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;

        var image = new Texture2D(pixelWidth, pixelHeight, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, pixelWidth, pixelHeight), 0, 0);
        image.Apply();
        string path = Path.GetFullPath(file);
        File.WriteAllBytes(path, image.EncodeToPNG());

        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(cameraObject);

        var info = new FileInfo(path);
        Check(info.Exists && info.Length > 0, "render produced no bytes: " + file);
        captures++;
        return path;
    }

    private static void CaptureWindow(StrPlanningWindow window, string file,
        float width = 1920f, float height = 1080f)
    {
        Canvas canvas = window.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.GetComponent<CanvasScaler>().enabled = false;
        canvas.transform.localScale = Vector3.one;
        canvas.transform.position = Vector3.zero;
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
        Capture(window.gameObject, height, file, width, canvas.transform.position);
    }

    private static object GetFieldValue(object target, string field)
    {
        FieldInfo info = target.GetType().GetField(field, Private);
        Check(info != null, "missing field " + field);
        return info.GetValue(target);
    }

    // ------------------------------------------------------------------ gate

    private static readonly List<string> Failures = new List<string>();
    private static int checkedTexts;

    /// <summary>Console gate: overflow, floor, gutter, contrast and part overlap on the page in view.</summary>
    private static void Gate(GameObject canvas, string where, int page)
    {
        StrMfdPanel panel = canvas.GetComponentInChildren<StrMfdPanel>();
        var con = (AvConsole)GetFieldValue(panel, "console");
        float gutterLeft = AvTokens.PanelWidth - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;
        GateTexts(con.Root, where, gutterLeft, con.Root);
        PartOverlap(con.Page(page).Content, where);
        foreach (Transform s in con.Root.GetComponentsInChildren<Transform>(true))
            if (s.name.StartsWith("Section ") && s.Find("Icon None") != null)
                Failures.Add(where + ": section without icon " + s.name);
    }

    private static void GateWindow(StrPlanningWindow room, string where)
    {
        var window = (AvWindow)GetFieldValue(room, "window");
        GateTexts(window.Root, where, float.MaxValue, window.Root);
        PartOverlap(window.Body.Content, where);
        RectTransform root = window.Root;
        if (root.rect.width > 1880.5f || root.rect.height > 1040.5f)
            Failures.Add(where + ": window " + root.rect.size + " does not fit the 1080p reference screen");
    }

    private static void GateTexts(RectTransform scope, string where, float gutterLeft, RectTransform space)
    {
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        foreach (TMP_Text t in scope.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t)) continue;
            checkedTexts++;
            t.ForceMeshUpdate();
            Rect r = t.rectTransform.rect;
            bool icon = t.name.StartsWith("Icon");
            if (!icon)
            {
                Bounds b = t.textBounds;
                if (b.size.x > r.width + 1.5f)
                    Failures.Add(where + ": overflows width (" + b.size.x.ToString("0") + " > " + r.width.ToString("0") + ") '" + t.text + "'");
                if (b.size.y > r.height + 1.5f)
                    Failures.Add(where + ": overflows height (" + b.size.y.ToString("0") + " > " + r.height.ToString("0") + ") '" + t.text + "'");
                if (t.fontSize < AvTypeScale.Floor - 0.01f && t.name != "Number")
                    Failures.Add(where + ": below the 11 px floor (" + t.fontSize.ToString("0.0") + ") '" + t.text + "'");
            }
            if (gutterLeft < float.MaxValue && t.GetComponentInParent<ScrollRect>() != null)
            {
                var corners = new Vector3[4];
                t.rectTransform.GetWorldCorners(corners);
                float right = space.InverseTransformPoint(corners[2]).x;
                if (right > gutterLeft) Failures.Add(where + ": enters the gutter (" + right.ToString("0") + ") '" + t.text + "'");
            }
            if (!icon && t.color.a > 0.5f)
            {
                Color back = BackgroundOf(t, ground);
                float contrast = Rgba.Contrast(t.color.ToRgba().WithAlpha(1f).Over(back.ToRgba()), back.ToRgba());
                if (contrast < 4.5f) Failures.Add(where + ": contrast " + contrast.ToString("0.00") + " for '" + t.text + "' (" + t.name + ")");
            }
        }
    }

    /// <summary>No two active top-level parts of a page may intersect.</summary>
    private static void PartOverlap(RectTransform content, string where)
    {
        var rects = new List<RectTransform>();
        foreach (Transform child in content)
            if (child.gameObject.activeSelf) rects.Add((RectTransform)child);
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                RectTransform a = rects[i], b = rects[j];
                float ax0 = a.anchoredPosition.x, ax1 = ax0 + a.rect.width, ay0 = -a.anchoredPosition.y, ay1 = ay0 + a.rect.height;
                float bx0 = b.anchoredPosition.x, bx1 = bx0 + b.rect.width, by0 = -b.anchoredPosition.y, by1 = by0 + b.rect.height;
                bool overlap = ax0 < bx1 - 0.5f && bx0 < ax1 - 0.5f && ay0 < by1 - 0.5f && by0 < ay1 - 0.5f;
                if (overlap && a.rect.height > 0.5f && b.rect.height > 0.5f)
                    Failures.Add(where + ": part '" + a.name + "' overlaps '" + b.name + "'");
            }
    }

    private static bool CanvasOn(TMP_Text t)
    {
        for (Transform x = t.transform; x != null; x = x.parent)
        {
            var c = x.GetComponent<Canvas>();
            if (c != null && !c.enabled) return false;
        }
        return true;
    }

    // The nearest opaque-ish fill behind a label: an AvFrame sibling/ancestor fill, else an Image, else ground.
    private static Color BackgroundOf(TMP_Text t, Color ground)
    {
        for (Transform x = t.transform.parent; x != null; x = x.parent)
        {
            foreach (Transform child in x)
            {
                var f = child.GetComponent<AvFrame>();
                if (f != null && f.enabled && f.Fill && f.FillColor.a > 0.35f && child != t.transform)
                {
                    Rgba o = f.FillColor.ToRgba().Over(ground.ToRgba());
                    return new Color(o.R, o.G, o.B);
                }
            }
            var img = x.GetComponent<Image>();
            if (img != null && img.enabled && img.color.a > 0.35f)
                return new Color(img.color.r, img.color.g, img.color.b);
        }
        return ground;
    }

    // ---------------------------------------------------------------------- staff

    /// <summary>
    /// A portrait the way Wing Command hands them over: shapes differ, and so do the sprite
    /// pivots. Kit v2's COC page no longer renders a portrait plate (dropped as decorative,
    /// no reading depended on it), but the contract still carries one, so the stub keeps
    /// generating it to exercise the constructor faithfully.
    /// </summary>
    private static Sprite Portrait(int seed, int width, int height, Vector2 pivot)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x < 2 || y < 2 || x >= width - 2 || y >= height - 2;
                int wash = 40 + (x + y) * 170 / (width + height);
                pixels[y * width + x] = border
                    ? new Color32(235, 255, 245, 255)
                    : new Color32((byte)(wash + seed * 5), (byte)(wash + 50), (byte)(wash + 90), 255);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, width, height), pivot, 100f);
    }

    private static IHighCommandView Staff(bool available = true)
    {
        const string LongBio =
            "Born in the northern shipyards and raised on maintenance decks, Halverson flew three combat tours " +
            "before taking the theater chair. He reads a front line the way other officers read a ledger: supply " +
            "first, then the shape of the ground, and only then the aircraft. His staff say he has never once " +
            "raised his voice on the radio, which is somehow worse. He keeps a personal map of every airstrip " +
            "the faction has ever lost and marks the date of each loss in its margin.";

        var commanders = new List<CommanderView>
        {
            // 0 - theater commander, tier 0, long bio, two-entry bonus.
            new CommanderView(0, -1, 0, true, true, false, false, false, false,
                "GEN. D. HALVERSON", "GENERAL", "THEATER COMMANDER", "theater_hq_delta",
                "LOGISTICS MIND +15% STIPEND · RECLUSE -30% PATROL SIGHT", LongBio,
                0x1101, Portrait(1, 96, 96, new Vector2(0.5f, 0.5f)), -1f, 0.40f, 0f, 0f),

            // 1 - air component commander, tier 1.
            new CommanderView(1, 0, 1, true, true, false, false, false, false,
                "COL. R. MARCHETTI", "COLONEL", "AIR COMPONENT CMDR", "airbase_west_complex",
                "WING DOCTRINE +10% SORTIE READINESS",
                "Flew the first sortie of the war and has not left the ops room since.",
                0x1202, Portrait(2, 80, 120, Vector2.zero), -1f, 0.20f, 0f, 0f),

            // 2 - ground component commander, tier 1, under fire.
            new CommanderView(2, 0, 1, true, true, false, false, false, true,
                "COL. A. OKONKWO", "COLONEL", "GROUND COMPONENT CMDR", "garrison_north",
                "IRON GRIP -20% SUPPLY LOSS",
                "Holds the northern shoulder with two battalions and a longer memory.",
                0x1303, Portrait(3, 128, 72, Vector2.one), -1f, 0.20f, 0f, 0f),

            // 3 - base commander, tier 2, in transit, very long location, short bio.
            new CommanderView(3, 1, 2, true, true, false, true, false, false,
                "MAJ. T. VOSSBERG", "MAJOR", "BASE COMMANDER", "airstrip_city2_northern_annex",
                "SAPPER'S EYE +12% FORTIFICATION SPEED",
                "Kept the annex running on borrowed parts and stubbornness.",
                0x1404, Portrait(4, 64, 64, new Vector2(0.5f, 0.5f)), -1f, 0.07f, 0f, 0f),

            // 4 - base commander, tier 2, KIA.
            new CommanderView(4, 2, 2, true, true, true, false, false, false,
                "MAJ. L. FERRO", "MAJOR", "BASE COMMANDER", "depot_south_ridge",
                "HARD SCHEDULE +8% CONVOY THROUGHPUT",
                "Ran the southern depot for two years without a late delivery.",
                0x1505, null, -1f, 0.07f, 0f, 0f),

            // 5 - base commander, tier 2, succession running.
            new CommanderView(5, 1, 2, true, true, false, false, true, false,
                "CPT. M. SATO", "CAPTAIN", "BASE COMMANDER", "radar_site_9",
                "QUIET WATCH -15% PATROL SIGHT",
                "Keeps the eastern radar net lit through every raid.",
                0x1606, Portrait(6, 72, 128, new Vector2(0f, 1f)), -1f, 0.06f, 0f, 0f),

            // 6 - enemy post, known, intel 41 seconds old.
            new CommanderView(6, -1, 1, false, true, false, false, false, false,
                "COL. V. KRUPIN", "COLONEL", "AIR COMPONENT CMDR", "enemy_airbase_icaria",
                "REAPER DOCTRINE +20% KILL VALUE",
                "Commands the enemy air component from a hardened strip; his patrols arrive on schedule and leave on time.",
                0x1707, Portrait(7, 100, 100, new Vector2(1f, 0f)), 41f, 0.25f, 0f, 0f),

            // 7 - enemy post, unconfirmed.
            new CommanderView(7, 6, 2, false, false, false, false, false, false,
                "MAJ. E. ROUX", "MAJOR", "BASE COMMANDER", "unknown_post",
                "", "Local intel has not confirmed this post.",
                0x1808, null, -1f, 0.10f, 0f, 0f),
        };

        var log = new List<CommanderLogLine>
        {
            new CommanderLogLine(0, CommanderLogTone.Economy, "THEATER COMMANDER SECURED A SUPPLY CONTRACT.", 12f),
            new CommanderLogLine(5, CommanderLogTone.Order, "BASE COMMANDER SATO REPORTS PATROL ROUTE SET.", 95f),
            new CommanderLogLine(6, CommanderLogTone.Contact, "HOSTILE POST IDENTIFIED NEAR ICARIA.", 41f),
            new CommanderLogLine(4, CommanderLogTone.Loss, "BASE COMMANDER FERRO KILLED AT DEPOT SOUTH RIDGE.", 610f),
            new CommanderLogLine(1, CommanderLogTone.Alert, "AIR COMPONENT CMDR UNDER FIRE.", 3f),
        };
        var hostileLog = new List<CommanderLogLine>
        {
            new CommanderLogLine(6, CommanderLogTone.Contact, "ENEMY AIR COMPONENT CMDR SPOTTED.", 41f),
            new CommanderLogLine(4, CommanderLogTone.Economy, "ENEMY STIPEND PAYOUT OBSERVED.", 190f),
        };
        return new CocStaffStub(commanders, log, hostileLog, available);
    }

    private sealed class CocStaffStub : IHighCommandView
    {
        private readonly IReadOnlyList<CommanderView> commanders;
        private readonly IReadOnlyList<CommanderLogLine> log;
        private readonly IReadOnlyList<CommanderLogLine> hostileLog;
        private readonly bool available;

        public CocStaffStub(
            IReadOnlyList<CommanderView> commanders,
            IReadOnlyList<CommanderLogLine> log,
            IReadOnlyList<CommanderLogLine> hostileLog,
            bool available)
        {
            this.commanders = commanders;
            this.log = log;
            this.hostileLog = hostileLog;
            this.available = available;
        }

        public bool Available => available;
        public string Status => available ? "chain of command is running" : "No staff has formed for your faction.";
        public string Signal => "STIPEND PAID: 15% COMMAND SHARE";
        public float FriendlyCohesion => 0.72f;
        public int FriendlyActive => 5;
        public int FriendlyKia => 1;
        public IReadOnlyList<CommanderView> Commanders => commanders;
        public IReadOnlyList<CommanderLogLine> Log => log;
        public IReadOnlyList<CommanderLogLine> HostileLog => hostileLog;
        public int HighlightedId { get; private set; } = int.MinValue;

        public void Highlight(int id) => HighlightedId = id;

        public void Refresh() { }

        public bool TryGetCohesion(FactionHQ hq, out float cohesion)
        {
            cohesion = FriendlyCohesion;
            return available;
        }
    }

    // ------------------------------------------------------------------ plumbing

    private sealed class PriorityStub : ITheaterPriorityView
    {
        private readonly string label;
        public PriorityStub(string priorityLabel = "WEST DEPOT") { label = priorityLabel; }
        public bool Available => true;
        public bool HasPriority => label != null;
        public string PriorityLabel => label;
        public IReadOnlyList<TheaterPriorityOption> Options => new[]
        {
            new TheaterPriorityOption("north", "NORTH RIDGE AIRBASE", "AIRBASE · ACTIVE", 1200f, 4200f),
            new TheaterPriorityOption("east", "EASTERN PASS", "GROUND · ACTIVE"),
            new TheaterPriorityOption("west", "WEST DEPOT", "DEPOT · ACTIVE"),
        };
        public void Refresh() { }
    }

    private sealed class LogisticsStub : ITheaterLogisticsView
    {
        private readonly bool emptyPool, canCommand;
        public LogisticsStub(bool emptyPool = false, bool canCommand = true)
        {
            this.emptyPool = emptyPool;
            this.canCommand = canCommand;
        }
        public bool Available => true;
        public bool CanCommand => canCommand;
        public float FactionFunds => emptyPool ? 0f : 40f;
        public IReadOnlyList<ReinforcementOption> Reinforcements => new[]
        {
            new ReinforcementOption("armor", "ARMORED COLUMN", "4 × MBT  ·  2 × IFV", 5f, !emptyPool, !emptyPool, 0f),
            new ReinforcementOption("motor", "MOTORIZED", "3 × APC  ·  2 × SPAA", 3f, !emptyPool, !emptyPool, 0f),
            new ReinforcementOption("rocket", "ROCKET BATTERY", "2 × MLRS  ·  1 × TRUCK", 4f, false, true, 60f),
            new ReinforcementOption("supply", "SUPPLY TRAIN", "4 × TRUCK", 2f, !emptyPool, !emptyPool, 0f),
        };
        public ReadinessSummary Readiness => default;
        public void Refresh() { }
        public bool RequestReinforcement(string key) => false;
    }

    private sealed class EventsStub : IActiveEventsView
    {
        public event Action<int, float> MoraleAwarded { add { } remove { } }
        public bool Available => true;
        public ActiveEventView Current => new ActiveEventView("storm", "FRONTLINE STATIC",
            "Dispatches report a hard road to the ridge.", "WAR", "MEDIUM", "ALL THEATER",
            false, "", "+25% SUPPORT COST", null, 0f, 600f);
        public IReadOnlyList<ActiveEventView> History => new ActiveEventView[0];
        public float SupportCostMultiplier => 1.25f;
        public float SupportCostMultiplierFor(ulong playerId) => 1.25f;
        public float SupportCooldownMultiplierFor(ulong playerId) => 1f;
        public bool AffectsFaction(string factionName) => true;
        public string PriceSummaryForFaction(string factionName) => Current.EffectSummary;
    }

    private sealed class StrikeStub : ITheaterStrikePicture
    {
        public bool TryGetNear(float x, float z, float vicinity,
            out string label, out float secondsToImpact)
        {
            label = x == 1200f && z == 4200f ? "FLARE BARRAGE" : null;
            secondsToImpact = label == null ? 0f : 8f;
            return label != null;
        }
    }

    private static void SetField(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, Private);
        Check(info != null, "missing field " + field);
        info.SetValue(target, value);
    }

    private static object Call(object target, string method, params object[] arguments)
    {
        MethodInfo info = target.GetType().GetMethod(method, Private);
        Check(info != null, "missing method " + method);
        return info.Invoke(target, arguments);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

namespace BoscaliSummer.Modules.Command.Configuration
{
    internal sealed class CommandSettings
    {
        public readonly Setting<bool> Enabled = new Setting<bool>(true);
        public readonly Setting<bool> FrontlinesOverlay = new Setting<bool>(false);
        internal sealed class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    }
}

namespace BoscaliSummer.Modules.Command.Runtime
{
    internal enum SectorControl : byte { Neutral = 0, Friendly = 1, Hostile = 2, Contested = 3 }

    internal sealed class TacticalSectorGrid
    {
        public const int MaximumNodes = 128;
        public float WorldSizeX => 10000f;
        public float WorldSizeY => 10000f;
        public ulong FrontlineHash => 1UL;
        public int CopyFrontlineTraces(FrontlineTracePoint[] points, int[] lengths, float[] pressure)
        {
            points[0] = new FrontlineTracePoint(-3500f, -1400f);
            points[1] = new FrontlineTracePoint(0f, 100f);
            points[2] = new FrontlineTracePoint(3300f, 1600f);
            lengths[0] = 3;
            pressure[0] = .8f;
            return 1;
        }
        public struct TacticalNode
        {
            public bool IsContested;
            public float CaptureProgress;
            public SectorControl Faction;
            public string Name;
            public bool IsAirbase;
        }
        private readonly System.Collections.Generic.List<TacticalNode> nodes =
            new System.Collections.Generic.List<TacticalNode>();
        public System.Collections.Generic.IReadOnlyList<TacticalNode> GetNodes() => nodes;
    }

    internal sealed class CommandManager
    {
        public BoscaliSummer.Modules.Command.Domain.TacticalTheaterState TheaterState { get; } =
            new BoscaliSummer.Modules.Command.Domain.TacticalTheaterState();
        public void UpdateTelemetry(FactionHQ hq) { }
    }
}

namespace BoscaliSummer.Modules.Command.Presentation
{
    public partial class ComMapOverlay
    {
        internal BoscaliSummer.Modules.Command.Runtime.TacticalSectorGrid Grid { get; } =
            new BoscaliSummer.Modules.Command.Runtime.TacticalSectorGrid();
    }
}

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal sealed class FrontlineGraphic : MaskableGraphic
    {
        public void SetSource(BoscaliSummer.Modules.Command.Runtime.TacticalSectorGrid source) { }
        protected override void OnPopulateMesh(VertexHelper mesh) => mesh.Clear();
    }
}
#endif

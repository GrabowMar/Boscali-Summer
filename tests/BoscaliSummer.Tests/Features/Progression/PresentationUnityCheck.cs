#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Production presentation builders with deterministic display fixtures; no game session.</summary>
public static class PresentationUnityCheck
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly Mod = typeof(AvConsole).Assembly;
    private static int assertions;
    private static int captures;

    public static void Run() => Execute(false, false);

    public static void RunEventAlertOnly() => Execute(true, false);

    public static void RunSqdOnly() => Execute(false, true);

    private static void Execute(bool eventAlertOnly, bool sqdOnly)
    {
        try
        {
            if (Shader.Find("TextMeshPro/Distance Field") == null)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                AssetDatabase.importPackageCompleted += _ =>
                    EditorApplication.delayCall += () => Execute(eventAlertOnly, sqdOnly);
                AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath,
                    "Package Resources/TMP Essential Resources.unitypackage"), false);
                return;
            }

            MethodInfo setPaths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", All);
            ParameterInfo[] pathParameters = setPaths.GetParameters();
            var pathArguments = new object[pathParameters.Length];
            pathArguments[0] = Path.GetFullPath("PresentationPreview.exe");
            for (int i = 1; i < pathArguments.Length; i++)
                pathArguments[i] = pathParameters[i].HasDefaultValue ? pathParameters[i].DefaultValue : null;
            setPaths.Invoke(null, pathArguments);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            new GameObject("Events", typeof(EventSystem));
            if (!eventAlertOnly)
            {
                foreach (float height in new[] { 420f, 596f, 896f })
                {
                    RenderSqd(height);
                    if (!sqdOnly && height >= 896f) RenderArchive();
                }
            }
            if (!sqdOnly) RenderEventAlert();
            File.WriteAllText("result.txt", "PASS: " + captures +
                " production-builder renders; " + assertions +
                " compact scroll/readability assertions. Offline fixture text only; no live game, input, audio or networking claim.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static void RenderSqd(float height)
    {
        GameObject canvasObject = MakeCanvas("SqdPreview", height, out RectTransform root);
        AvConsole console = AvConsole.Build(root, "PILOT", "SQUADRON DOSSIER", 5, 480f, height);
        AvChip[] chips = console.Chips(3);
        AvMetric[] metrics = console.Metrics("PILOT SCORE", "QUAL PICKS");
        console.Tabs((AvIcon.User, "PILOT"), (AvIcon.Star, "SKILLS"), (AvIcon.Skull, "ACES"),
            (AvIcon.Pencil, "STUDIO"), (AvIcon.Plane, "PLANE"));
        chips[0].Set("PERSONNEL FILE", AvState.Ready);
        chips[1].Set("RANK 3", AvState.Info);
        chips[2].Set("2 PICKS", AvState.Ready);
        metrics[0].Set("2,450", "NEXT IN 550", .72f, AvState.Ready);
        metrics[1].Set("2 PICKS", "4/7 EARNED · +0", .57f, AvState.Ready);
        console.Footer.Set("Offline layout check · production builder · no live game state.");

        var panelObject = new GameObject("SqdMfdPanel");
        object panel = panelObject.AddComponent(TypeOf("BoscaliSummer.Features.Progression.Presentation.SqdMfdPanel"));
        ((Behaviour)panel).enabled = false;
        var managerObject = new GameObject("ProgressionManager");
        object manager = managerObject.AddComponent(TypeOf("BoscaliSummer.Features.Progression.Runtime.ProgressionManager"));
        ((Behaviour)manager).enabled = false;
        object settings = NewSettings("BoscaliSummer.Features.Progression.Configuration.ProgressionSettings",
            "progression-fixture.cfg");
        Call(manager, "Configure", settings, null, null, null);
        Set(manager, "localRank", 3);
        Set(manager, "localScore", 2450);
        Set(manager, "localEarnedPoints", 4);
        Set(manager, "localMaximumPoints", 7);
        Set(manager, "localScorePerPoint", 750);
        Set(panel, "progression", manager);
        Set(panel, "settings", settings);
        Set(panel, "console", console);

        Type wingLink = TypeOf("BoscaliSummer.Runtime.WingLink");
        SetStatic(wingLink, "studioResolved", true);
        SetStatic(wingLink, "studioUnavailableReason", string.Empty);

        string[] methods = { "BuildPilotPage", "BuildSkillsPage", "BuildWingsPage", "BuildStudioPage", "BuildPlanePage" };
        string[] names = { "pilot", "skills", "wings", "studio", "plane" };
        for (int i = 0; i < methods.Length; i++) Call(panel, methods[i], console.Page(i));
        console.Finish();
        // Hover help survives the kit v2 conversion: plane pager (2), skills confirm (1), studio buttons, rows and fields (18).
        int helpTips = root.GetComponentsInChildren<AvHelpTip>(true).Length;
        Check(helpTips >= 21, "SQD pages must carry their hover help, found " + helpTips);
        SeedSqd(panel);
        ValidateSkillRows(panel);

        for (int page = 0; page < names.Length; page++)
        {
            console.SetPage(page);
            console.Page(page).Relayout();
            ValidateReadable(root, "SQD " + names[page]);
            ValidateFit(console, "SQD " + names[page]);
            CaptureConsole(canvasObject, height, "sqd-" + names[page] + "-" + height);
            if (page == 4)
            {
                SeedPlane(panel);
                console.Page(page).Relayout();
                ValidateReadable(root, "SQD plane populated");
                ValidateFit(console, "SQD plane populated");
                CaptureConsole(canvasObject, height, "sqd-plane-populated-" + height);
            }
        }
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(panelObject);
        Object.DestroyImmediate(managerObject);
    }

    private static void SeedPlane(object panel)
    {
        Call(Get(panel, "planeIdentity"), "Set", "SAF-22 CHICANE", null, "AIRBORNE", AvState.Ready);
        Call(Get(panel, "planeSelected"), "Set", "AAM-10", null, "4 / 6", AvState.Info);
        Call(Get(panel, "planeStoreOverflow"), "Set", "1–4 OF 8");
        Call(Get(panel, "planeTuneName"), "Set", "RANGE");
        Call(Get(panel, "planeTuneState"), "Set", "RANGE: 10% less fuel draw; 85% throttle ceiling.");
        string[] flight = { "940 km/h", "870 km/h", "6,120m", "+18.2m/s", "287°", "3.4 G" };
        string[] systems = { "36%", "82%", "UP", "ON", "12 READY", "2 TRACKED" };
        string[] faults = { "LEFT WINGROOT", "ENGINE RIGHT", "RUDDER", "COCKPIT" };
        string[] values = { "42%", "67%", "88%", "100%" };
        Array tiles = (Array)Get(panel, "planeFlight");
        Array rows = (Array)Get(panel, "planeSystems");
        AvRow[] stores = (AvRow[])Get(panel, "planeStores");
        AvRow[] faultRows = (AvRow[])Get(panel, "planeFaults");
        for (int i = 0; i < tiles.Length; i++) Call(tiles.GetValue(i), "Set", flight[i]);
        for (int i = 0; i < rows.Length; i++) Call(rows.GetValue(i), "Set", systems[i], AvState.Inert);
        string[] storeNames = { "01  AAM-10", "02  AAM-10", "03  CBU-12", "04  20MM CANNON" };
        string[] ammo = { "4 / 6  SELECTED", "2 / 4", "3 / 3", "320" };
        for (int i = 0; i < stores.Length; i++)
            stores[i].Set(storeNames[i], null, ammo[i], i == 0 ? AvState.Info : AvState.Inert);
        for (int i = 0; i < faultRows.Length; i++)
            faultRows[i].Set(faults[i], null, values[i], i == 3 ? AvState.Ready : AvState.Caution);
    }

    private static void SeedSqd(object panel)
    {
        Call(Get(panel, "pilotIdentity"), "Set", "LOCAL PROFILE", "DAYMAN", "M. FONTAINE",
            "RANK 3   ·   GEN 1   ·   FLIGHT LEAD", "AWAITING AIRCRAFT", (Sprite)null, (Sprite)null,
            "BOSCALI SUMMER", AvState.Ready, "ACTIVE");
        foreach (string tile in new[] { "tileSortie", "tileTime", "tileFuel", "tileDeaths" })
            Call(Get(panel, tile), "Set", tile == "tileTime" ? "10:39" : "—");
        Call(Get(panel, "pilotBackground"), "Set",
            "Flew medical supply routes before joining the reserves. Precise on the radio and calm under pressure.");
        string[] kv = { "pilotMode", "pilotStatus", "pilotDeaths", "pilotGeneration", "runAirframeValue", "runTimeValue",
            "runFlightStatusValue", "runFuelValue", "runSortieScoreValue", "runRankValue", "runMissionScoreValue",
            "pilotScoreValue", "aceBonusValue", "runNextPerkValue", "earnedValue", "spentValue", "availableValue" };
        string[] kvText = { "RESPAWNING", "AWAITING AIRCRAFT", "0", "1", "NO AIRCRAFT", "10:39", "GROUND", "—",
            "—", "3", "2,450", "2,450", "+0P", "550", "4/7", "2", "2" };
        for (int i = 0; i < kv.Length; i++) Call(Get(panel, kv[i]), "Set", kvText[i], AvState.Inert);
        AvChip[] committed = (AvChip[])Get(panel, "committedChips");
        string[] words = { "STK", "COMBAT", "SURVEILLANCE" };
        for (int i = 0; i < committed.Length; i++)
        {
            committed[i].Rect.gameObject.SetActive(i < words.Length);
            if (i < words.Length) committed[i].Set(words[i], i == 0 ? AvState.Info : AvState.Ready);
        }
        Call(Get(panel, "committedSkillsEmpty"), "Set", "No skills committed yet. Open SKILLS to choose one.");

        Call(Get(panel, "skillBudgetNote"), "Set", "2 PICKS UNSPENT · 550 TO NEXT GRADE");
        Call(Get(panel, "skillStripTitle"), "Set", "SELECT A QUALIFICATION");
        Call(Get(panel, "skillStripDetail"), "Set", "Compare the same tier across lanes, then unlock the selected grade.");
        foreach (object row in (IEnumerable)Get(panel, "skillRows"))
            ((AvRow)Get(row, "Row")).Set("ENGINEER QUALIFICATION", "AVAILABLE", null, AvState.Info);
        foreach (object branch in (IEnumerable)Get(panel, "skillBranches")) Text(branch, "Note", "0/6 OPEN");

        ((AvRow)Get(panel, "huntRow")).Set("ACE HUNT STANDBY", "No hostile ace is currently assigned to this pilot.", null, AvState.Info);
        ((AvRow)Get(panel, "wingLeadRow")).Set("DAYMAN", "M. FONTAINE   ·   FLIGHT LEAD", "NO AIRCRAFT", AvState.Inert);
        ((AvRow)Get(panel, "wingCountRow")).Set("NO RECRUITED WING", "WMC OFFLINE", null, AvState.Inert);
        foreach (AvRow slot in (AvRow[])Get(panel, "wingmanSlots")) slot.Rect.gameObject.SetActive(false);
        Call(Get(panel, "rosterPage"), "Set", "1–2 OF 4 WINGS");
        int index = 0;
        foreach (object row in (IEnumerable)Get(panel, "wingRows"))
        {
            Text(row, "Symbol", index == 0 ? "▲" : "◆");
            Text(row, "Wing", index == 0 ? "NIGHT LANCE" : "BLACK TIDE");
            Text(row, "Ace", index == 0 ? "ACE  REVENANT" : "ACE  MARROW");
            Text(row, "Skill", index == 0 ? "TIER 3 · SKILL VETERAN · FIRST ENCOUNTER" : "TIER 2 · SKILL TRAINED · RETURN #1");
            Text(row, "Status", index == 0 ? "HUNTING" : "PATROLLING");
            Text(row, "Members", index == 0 ? "3 / 4 ALIVE" : "2 / 3 ALIVE");
            Text(row, "Target", index == 0 ? "TARGET: YOU" : "TARGET: NORMAL OPERATIONS");
            foreach (TMP_Text badge in (IEnumerable)Get(row, "Badges")) badge.gameObject.SetActive(true);
            TMP_Text noSkills = Get(row, "NoSkills") as TMP_Text;
            if (noSkills != null) noSkills.gameObject.SetActive(false);
            index++;
        }

        Call(Get(panel, "studioStatus"), "Set", "Wing Command connected · 4 local custom pilots.");
        Call(Get(panel, "studioPagerLabel"), "Set", "1–4 OF 4 · PAGE 1/1");
        string[] calls = { "DAYMAN", "VIXEN" };
        index = 0;
        foreach (AvRow row in (AvRow[])Get(panel, "studioRows"))
        {
            row.Set(calls[index], "CUSTOM PILOT " + (index + 1) + "   ·   RANK " + (index + 1),
                index == 0 ? "IN SQUADRON" : "READY", index == 0 ? AvState.Ready : AvState.Info);
            index++;
        }
        Call(Get(panel, "studioMessageText"), "Set", "Custom pilots stay local. Nothing is uploaded.");
    }

    /// <summary>Kit v2 fit gate for a console page: no wrapped-off text, no text into the scroll gutter.</summary>
    private static void ValidateFit(AvConsole console, string name)
    {
        float gutterLeft = 480f - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;
        var bad = new List<string>();
        foreach (TMP_Text t in console.Page(console.CurrentPage).Content.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text) || t.name.StartsWith("Icon")) continue;
            t.ForceMeshUpdate();
            Rect r = t.rectTransform.rect;
            Bounds b = t.textBounds;
            if (b.size.x > r.width + 1.5f) bad.Add(name + " width " + b.size.x + " > " + r.width + ": " + t.text);
            if (b.size.y > r.height + 1.5f) bad.Add(name + " height " + b.size.y + " > " + r.height + ": " + t.text);
            var corners = new Vector3[4];
            t.rectTransform.GetWorldCorners(corners);
            float right = console.Root.InverseTransformPoint(corners[2]).x;
            if (right > gutterLeft) bad.Add(name + " enters the scroll gutter: " + t.text);
        }
        Check(bad.Count == 0, "fit failures (" + bad.Count + "): " + string.Join(" | ", bad.GetRange(0, Math.Min(40, bad.Count))));
    }

    private static void CaptureConsole(GameObject canvas, float height, string prefix)
    {
        foreach (ScrollRect scroll in canvas.GetComponentsInChildren<ScrollRect>(true))
            if (scroll.gameObject.activeInHierarchy) scroll.verticalNormalizedPosition = 1f;
        Capture(canvas, 480f, height, prefix + "-top.png");
        foreach (ScrollRect scroll in canvas.GetComponentsInChildren<ScrollRect>(true))
            if (scroll.gameObject.activeInHierarchy) scroll.verticalNormalizedPosition = 0f;
        Capture(canvas, 480f, height, prefix + "-bottom.png");
    }

    private static void RenderArchive()
    {
        object archive = CallStatic(TypeOf("BoscaliSummer.Features.Events.Presentation.EventDeskArchive"), "Create");
        Call(archive, "Show", 2);
        GameObject archiveObject = ((Component)archive).gameObject;
        PrepareWorldCanvas(archiveObject.GetComponent<Canvas>(), 1920f, 1080f);
        archiveObject.GetComponent<CanvasScaler>().enabled = false;
        RectTransform archiveRoot = (RectTransform)archiveObject.transform;
        archiveRoot.pivot = new Vector2(0f, 1f);
        archiveRoot.position = new Vector3(-960f, 540f, 0f);
        Set(archive, "fitted", Vector2.zero);
        Call(archive, "Fit");
        Capture(archiveObject, 1920f, 1080f, "evn-archive-world.png");
        Call(archive, "SelectSection", 1);
        Capture(archiveObject, 1920f, 1080f, "evn-archive-events.png");
        Call(archive, "SelectSection", 0);
        Capture(archiveObject, 1920f, 1080f, "evn-archive-aircraft-empty.png");
        var aircraftDefinition = ScriptableObject.CreateInstance<AircraftDefinition>();
        aircraftDefinition.unitName = "MODEL PREVIEW FIXTURE";
        aircraftDefinition.code = "QA-1";
        aircraftDefinition.description = "Mesh only. No aircraft simulation is created in the field archive.";
        GameObject prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        prefab.name = "PreviewMeshFixture";
        aircraftDefinition.unitPrefab = prefab;
        ((List<AircraftDefinition>)Get(archive, "aircraft")).Add(aircraftDefinition);
        Call(archive, "SelectSection", 0);
        Check(Get(archive, "preview") != null, "EVN aircraft preview must create a mesh-only viewer.");
        Capture(archiveObject, 1920f, 1080f, "evn-archive-aircraft-model.png");
        Call(archive, "Close");
        Object.DestroyImmediate(archiveObject);
        Object.DestroyImmediate(prefab);
        Object.DestroyImmediate(aircraftDefinition);
    }

    private static void RenderEventAlert()
    {
        Type toneType = TypeOf("BoscaliSummer.Features.Events.Presentation.EventAlertTone");
        AudioClip tone = (AudioClip)toneType.GetMethod("Clip", All).Invoke(null, null);
        var toneSamples = new float[tone.samples];
        Check(tone.GetData(toneSamples, 0), "Superevent chime samples must be readable.");
        float tonePeak = 0f;
        foreach (float sample in toneSamples) tonePeak = Mathf.Max(tonePeak, Mathf.Abs(sample));
        Check(tonePeak > .1f && tonePeak < .5f && Mathf.Abs(toneSamples[0]) < .001f &&
              Mathf.Abs(toneSamples[toneSamples.Length - 1]) < .01f,
            "Superevent chime must have headroom and quiet edges.");
        var componentObject = new GameObject("AlertComponent");
        object alert = componentObject.AddComponent(TypeOf("BoscaliSummer.Features.Events.Presentation.SuperEventAlert"));
        ((Behaviour)alert).enabled = false;
        Call(alert, "Build");
        GameObject root = (GameObject)Get(alert, "root");
        Canvas canvas = root.GetComponent<Canvas>();
        PrepareWorldCanvas(canvas, 1920f, 1080f);
        ((CanvasGroup)Get(alert, "group")).alpha = 1f;
        Text(alert, "stamp", "SUPEREVENT  /  POLITICAL");
        Text(alert, "target", "ALL THEATER");
        Text(alert, "eyebrow", "THEATER DISPATCH  /  LIVE");
        Text(alert, "title", "CEASEFIRE ULTIMATUM");
        Text(alert, "scope", "DIRECTED TO  /  ALL THEATER");
        Text(alert, "flavor", "Diplomats have set a deadline. Both sides rush stocked materiel toward the line before the embargo, temporarily cutting support costs and dispatching convoys.");
        Text(alert, "impact", "-30% SUPPORT COST");
        Text(alert, "nextOrder", "T-0:08   FRONTLINE CONVOY REQUESTED");
        Text(alert, "clock", "ON AIR  0:24");
        Text(alert, "compactTitle", "CEASEFIRE ULTIMATUM");
        Text(alert, "compactImpact", "-30% SUPPORT COST");
        Text(alert, "compactClock", "0:12");
        var relief = new Color32(111, 219, 191, 255);
        ((TMP_Text)Get(alert, "impact")).color = relief;
        ((TMP_Text)Get(alert, "compactImpact")).color = relief;
        Type cache = TypeOf("BoscaliSummer.Features.Events.Presentation.EventArtCache");
        Type catalog = TypeOf("BoscaliSummer.Features.Events.Domain.EventCatalog");
        Array entries = (Array)catalog.GetField("All", All).GetValue(null);
        MethodInfo atlasTile = cache.GetMethod("AtlasTile", All);
        Texture2D sharedTexture = null;
        for (int i = 0; i < entries.Length; i++)
        {
            string key = (string)entries.GetValue(i).GetType().GetProperty("IconKey", All)
                .GetValue(entries.GetValue(i));
            Sprite tile = (Sprite)atlasTile.Invoke(null, new object[] { key });
            Check(tile != null && tile.rect.width == 192f && tile.rect.height == 108f,
                "Every event must resolve a 192x108 atlas tile.");
            if (sharedTexture == null) sharedTexture = tile.texture;
            Check(ReferenceEquals(sharedTexture, tile.texture),
                "All event tiles must share one decoded atlas texture.");
        }
        Sprite poster = (Sprite)cache.GetMethod("Get", All).Invoke(null,
            new object[] { "ceasefire_ultimatum", "tier_super" });
        Check(poster != null, "The superevent dispatch must load its embedded poster.");
        Image art = (Image)Get(alert, "art"); art.sprite = poster; art.enabled = poster != null;
        Image compactArt = (Image)Get(alert, "compactArt"); compactArt.sprite = poster;
        compactArt.enabled = poster != null;
        Component glyph = (Component)Get(alert, "glyph"); glyph.gameObject.SetActive(poster == null);
        Component compactGlyph = (Component)Get(alert, "compactGlyph");
        compactGlyph.gameObject.SetActive(poster == null);
        ((Image)Get(alert, "stripes")).gameObject.SetActive(poster == null);
        ((Image)Get(alert, "compactStripes")).gameObject.SetActive(poster == null);
        TMP_Text title = (TMP_Text)Get(alert, "title");
        TMP_Text next = (TMP_Text)Get(alert, "nextOrder");
        TMP_Text flavor = (TMP_Text)Get(alert, "flavor");
        foreach (object entry in entries)
        {
            Type entryType = entry.GetType();
            if (!(bool)entryType.GetProperty("IsSuper", All).GetValue(entry)) continue;
            title.text = ((string)entryType.GetProperty("Title", All).GetValue(entry)).ToUpperInvariant();
            flavor.text = (string)entryType.GetProperty("FlavorText", All).GetValue(entry);
            Canvas.ForceUpdateCanvases();
            Check(!title.isTextOverflowing && !flavor.isTextOverflowing,
                "Superevent headline and flavor must fit: " + title.text);
        }
        title.text = "CEASEFIRE ULTIMATUM";
        flavor.text = "Diplomats have set a deadline. Both sides rush stocked materiel toward the line before the embargo, temporarily cutting support costs and dispatching convoys.";
        ValidateReadable((RectTransform)root.transform, "EVN alert");
        Check(!title.isTextOverflowing && !next.isTextOverflowing,
            "Superevent headline and next order must fit their dispatch areas.");
        Capture(root, 1920f, 1080f, "evn-alert.png");
        PrepareWorldCanvas(canvas, 1920f, 1080f);
        // The production canvas uses ScaleWithScreenSize/Expand: 720p renders the
        // 1920x1080 layout at two-thirds scale, not at an unscaled 1280-unit width.
        root.transform.localScale = Vector3.one * (720f / 1080f);
        Capture(root, 1280f, 720f, "evn-alert-720p.png");
        PrepareWorldCanvas(canvas, 1920f, 1080f);
        ((RectTransform)Get(alert, "expandedPanel")).gameObject.SetActive(false);
        ((RectTransform)Get(alert, "compactPanel")).gameObject.SetActive(true);
        ((CanvasGroup)Get(alert, "compactGroup")).alpha = 1f;
        ValidateReadable((RectTransform)root.transform, "EVN compact alert");
        Capture(root, 1920f, 1080f, "evn-alert-compact.png");
        Object.DestroyImmediate(root); Object.DestroyImmediate(componentObject);
    }

    private static void ValidateSkillRows(object panel)
    {
        Canvas.ForceUpdateCanvases();
        var rects = new List<Vector3[]>();
        foreach (object row in (IEnumerable)Get(panel, "skillRows"))
        {
            RectTransform cell = ((AvRow)Get(row, "Row")).Rect;
            var corners = new Vector3[4];
            cell.GetWorldCorners(corners);
            Check(corners[2].x - corners[0].x >= 88f && corners[2].y - corners[0].y >= 68f,
                "SQD skill cell is too small to hold a name and its state.");
            foreach (Vector3[] other in rects)
                Check(corners[2].x <= other[0].x + .5f || corners[0].x >= other[2].x - .5f ||
                      corners[2].y <= other[0].y + .5f || corners[0].y >= other[2].y - .5f,
                    "SQD skill cells overlap.");
            rects.Add(corners);
        }
    }

    private static GameObject MakeCanvas(string name, float height, out RectTransform root)
    {
        var canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        PrepareWorldCanvas(canvas, 480f, height);
        root = (RectTransform)canvasObject.transform;
        AvLay.Fill(AvLay.Solid(root, "Ground", AvTheme.SurfaceInert).rectTransform);
        return canvasObject;
    }

    private static void PrepareWorldCanvas(Canvas canvas, float width, float height)
    {
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform root = (RectTransform)canvas.transform;
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition3D = Vector3.zero;
        root.localScale = Vector3.one;
        root.sizeDelta = new Vector2(width, height);
    }

    private static void ValidateReadable(RectTransform root, string name)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!text.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(text.text) || text.name.StartsWith("Icon")) continue; // kit icon glyphs are not type
            text.ForceMeshUpdate();
            Check(text.fontSize >= 10f, name + " has unreadable type: " + text.text);
            Check(text.color.a >= .35f, name + " has near-invisible type: " + text.text);
        }
    }

    private static void Capture(GameObject canvas, float width, float height, string file)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        var cameraObject = new GameObject("Capture", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = height * .5f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.backgroundColor = AvTheme.SurfaceInert;
        camera.clearFlags = CameraClearFlags.SolidColor;
        var target = new RenderTexture((int)width * 2, (int)height * 2, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.GetFullPath(file), image.EncodeToPNG());
        RenderTexture.active = null;
        Object.DestroyImmediate(image); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(target);
        captures++;
    }

    private static Type TypeOf(string name) => Mod.GetType(name, true);
    private static object NewSettings(string type, string file) =>
        Activator.CreateInstance(TypeOf(type), new ConfigFile(Path.GetFullPath(file), false));
    private static object Get(object target, string field) => target.GetType().GetField(field, All)?.GetValue(target);
    private static object GetStatic(Type type, string field) => type.GetField(field, All)?.GetValue(null);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, All).SetValue(target, value);
    private static void SetStatic(Type type, string field, object value) => type.GetField(field, All).SetValue(null, value);
    private static object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, All).Invoke(target, args);
    private static object CallStatic(Type type, string method, params object[] args) =>
        type.GetMethod(method, All).Invoke(null, args);
    private static void Text(object target, string field, string value)
    {
        TMP_Text label = target == null ? null : target.GetType().GetField(field, All)?.GetValue(target) as TMP_Text;
        if (label != null) label.text = value;
    }
    private static void SetEntry(object settings, string property, object value)
    {
        object entry = settings.GetType().GetProperty(property, All).GetValue(settings);
        entry.GetType().GetProperty("Value", All).SetValue(entry, value);
    }
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
}
#endif

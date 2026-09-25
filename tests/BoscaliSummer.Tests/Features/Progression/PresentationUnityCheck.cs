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
    private static readonly Assembly Mod = typeof(AvScreen).Assembly;
    private static int assertions;
    private static int captures;

    public static void Run() => Execute(false, false);

    public static void RunEventAlertOnly() => Execute(true, false);

    public static void RunSqdOnly() => Execute(false, true);

    public static void RunMfdOnly() => Execute(false, false, true);

    private static void Execute(bool eventAlertOnly, bool sqdOnly, bool mfdOnly = false)
    {
        try
        {
            if (Shader.Find("TextMeshPro/Distance Field") == null)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                AssetDatabase.importPackageCompleted += _ =>
                    EditorApplication.delayCall += () => Execute(eventAlertOnly, sqdOnly, mfdOnly);
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
            AvFont.Font = TMP_FontAsset.CreateFontAsset(new Font("C:/Windows/Fonts/consola.ttf"));
            new GameObject("Events", typeof(EventSystem));
            if (!eventAlertOnly)
            {
                foreach (float height in new[] { 420f, 596f, 896f })
                {
                    RenderSqd(height);
                    if (!sqdOnly)
                    {
                        RenderRadio(height);
                        RenderEvents(height);
                        RenderWeather(height);
                    }
                }
            }
            if (!sqdOnly) RenderEventAlert();
            if (!eventAlertOnly && !sqdOnly && !mfdOnly) RenderTargetBoard();
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
        AvScreen shell = AvScreen.Build(root, "PILOT",
            new[] { "PILOT", "SKILLS", "ACES", "STUDIO", "PLANE" },
            new[] { new[] { "PILOT SCORE", "THIS PILOT" }, new[] { "QUAL. PICKS", "UNSPENT" } },
            3, 480f, height, _ => { });
        SeedShell(shell, "PERSONNEL FILE / PILOT");
        shell.Metrics[0].Set("2,450", "NEXT IN 550", .72f, AvTheme.RailReady);
        shell.Metrics[1].Set("2 PICKS", "4/7 EARNED", .57f, AvTheme.RailInfo);

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
        Set(panel, "shell", shell);
        Call(panel, "PrepareShell");

        Type wingLink = TypeOf("BoscaliSummer.Runtime.WingLink");
        SetStatic(wingLink, "studioResolved", true);
        SetStatic(wingLink, "studioUnavailableReason", string.Empty);

        string[] methods = { "BuildPilotPage", "BuildSkillsPage", "BuildWingsPage", "BuildStudioPage", "BuildPlanePage" };
        string[] names = { "pilot", "skills", "wings", "studio", "plane" };
        for (int i = 0; i < methods.Length; i++)
        {
            RectTransform page = (RectTransform)shell.CreatePage(i, names[i]).transform;
            Call(panel, methods[i], page, shell.Body);
        }
        SeedSqd(panel);
        ValidateSkillRows(panel);

        for (int page = 0; page < names.Length; page++)
        {
            shell.SetPage(page);
            shell.DataBar.State.text = "PERSONNEL FILE / " + names[page].ToUpperInvariant();
            ValidateReadable(root, "SQD " + names[page]);
            CaptureScrolled(canvasObject, height, "sqd-" + names[page] + "-" + height);
            if (page == 4)
            {
                SeedPlane(panel);
                ValidateReadable(root, "SQD plane populated");
                CaptureScrolled(canvasObject, height, "sqd-plane-populated-" + height);
            }
        }
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(panelObject);
        Object.DestroyImmediate(managerObject);
    }

    private static void SeedPlane(object panel)
    {
        Text(panel, "planeName", "SAF-22 CHICANE");
        Text(panel, "planeState", "AIRBORNE");
        Text(panel, "planeSelected", "AAM-10  ·  4 / 6");
        Text(panel, "planeStoreOverflow", "1–4 OF 8");
        Text(panel, "planePlotState", "LIVE PART CONDITION");
        Text(panel, "planeTuneName", "RANGE");
        Text(panel, "planeTuneState", "RANGE: 10% less fuel draw; 85% throttle ceiling.");
        string[] flight = { "940 km/h", "870 km/h", "6,120m", "+18.2m/s", "287°", "3.4 G" };
        string[] systems = { "36%", "82%", "UP", "ON", "12 READY", "2 TRACKED" };
        string[] stores = { "01  AAM-10  ·  4 / 6  SELECTED", "02  AAM-10  ·  2 / 4", "03  CBU-12  ·  3 / 3", "04  20MM CANNON  ·  320" };
        string[] faults = { "LEFT WINGROOT", "ENGINE RIGHT", "RUDDER", "COCKPIT" };
        string[] values = { "42%", "67%", "88%", "100%" };
        TMP_Text[] tiles = (TMP_Text[])Get(panel, "planeFlight");
        TMP_Text[] rows = (TMP_Text[])Get(panel, "planeSystems");
        TMP_Text[] storeRows = (TMP_Text[])Get(panel, "planeStores");
        TMP_Text[] faultNames = (TMP_Text[])Get(panel, "planeFaultNames");
        TMP_Text[] faultValues = (TMP_Text[])Get(panel, "planeFaultValues");
        Image[] bars = (Image[])Get(panel, "planeFaultBars");
        for (int i = 0; i < tiles.Length; i++) tiles[i].text = flight[i];
        for (int i = 0; i < rows.Length; i++) rows[i].text = systems[i];
        for (int i = 0; i < storeRows.Length; i++) storeRows[i].text = stores[i];
        for (int i = 0; i < faultNames.Length; i++)
        {
            faultNames[i].text = faults[i];
            faultValues[i].text = values[i];
            bars[i].fillAmount = new[] { .42f, .67f, .88f, 1f }[i];
            Color ink = i == 3 ? new Color(.48f, 1f, .44f) : new Color(1f, .72f, .27f);
            faultValues[i].color = ink;
            bars[i].color = ink;
        }
    }

    private static void SeedSqd(object panel)
    {
        Text(panel, "pilotCallsign", "DAYMAN");
        Text(panel, "pilotProfileTag", "LOCAL PROFILE");
        Text(panel, "pilotName", "M. FONTAINE");
        Text(panel, "pilotRankLine", "RANK 3  ·  GENERATION 1");
        Text(panel, "pilotStatusLine", "AWAITING AIRCRAFT");
        Text(panel, "pilotBackground", "Flew medical supply routes before joining the reserves. Precise on the radio and calm under pressure.");
        Text(panel, "tileSortie", "—"); Text(panel, "tileTime", "10:39");
        Text(panel, "tileFuel", "—"); Text(panel, "tileDeaths", "0");
        Text(panel, "pilotMode", "RESPAWNING"); Text(panel, "pilotStatus", "AWAITING AIRCRAFT");
        Text(panel, "pilotDeaths", "0"); Text(panel, "pilotGeneration", "1");
        Text(panel, "runAirframeValue", "NO AIRCRAFT"); Text(panel, "runTimeValue", "10:39");
        Text(panel, "runFlightStatusValue", "GROUND"); Text(panel, "runFuelValue", "—");
        Text(panel, "runSortieScoreValue", "—"); Text(panel, "runRankValue", "3");
        Text(panel, "runMissionScoreValue", "2,450"); Text(panel, "pilotScoreValue", "2,450");
        Text(panel, "aceBonusValue", "+0"); Text(panel, "runNextPerkValue", "550");
        Text(panel, "earnedValue", "4"); Text(panel, "spentValue", "2"); Text(panel, "availableValue", "2");
        Text(panel, "pilotSquadron", "BOSCALI SUMMER");
        Text(panel, "committedSkillsEmpty", "No skills committed yet. Open SKILLS to choose one.");

        Text(panel, "skillStripTitle", "SELECT A QUALIFICATION");
        Text(panel, "skillStripDetail", "Compare the same tier across lanes, then unlock the selected grade.");
        Text(panel, "skillBudgetNote", "2 PICKS UNSPENT · 550 TO NEXT GRADE");
        foreach (object row in (IEnumerable)Get(panel, "skillRows")) Text(row, "State", "AVAILABLE");
        foreach (object branch in (IEnumerable)Get(panel, "skillBranches")) Text(branch, "Note", "0/6 OPEN");

        Text(panel, "huntTitle", "ACE HUNT STANDBY");
        Text(panel, "huntDetails", "No hostile ace is currently assigned to this pilot.");
        Text(panel, "wingCallsign", "DAYMAN"); Text(panel, "wingName", "M. FONTAINE");
        Text(panel, "wingAirframe", "NO AIRCRAFT · GROUND"); Text(panel, "wingCount", "0 / 4 ACTIVE");
        Text(panel, "wingCountNote", "RECRUIT IN WMC");
        Text(panel, "wingTeamNote", "NO RECRUITED WINGMEN · RECRUIT AND TASK THEM IN WMC.");
        Text(panel, "rosterPage", "1–2 OF 4 CONTACTS");
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
            GameObject empty = Get(row, "Empty") as GameObject;
            if (empty != null) empty.SetActive(false);
            foreach (GameObject badge in (IEnumerable)Get(row, "Badges")) badge.SetActive(true);
            TMP_Text noSkills = Get(row, "NoSkills") as TMP_Text;
            if (noSkills != null) noSkills.gameObject.SetActive(false);
            index++;
        }

        Text(panel, "studioStatus", "Wing Command connected · 4 local custom pilots.");
        Text(panel, "studioPager", "1–4 OF 4 · PAGE 1/1");
        string[] calls = { "DAYMAN", "VIXEN", "CINDER", "MICA" };
        index = 0;
        foreach (object row in (IEnumerable)Get(panel, "studioRows"))
        {
            if (row == null) continue;
            Text(row, "Callsign", calls[index]); Text(row, "Name", "CUSTOM PILOT " + (index + 1));
            Text(row, "Rank", "RANK " + (index + 1)); Text(row, "Status", index == 0 ? "PROFILE" : "LOCAL");
            index++;
        }
        Text(panel, "studioBodyValue", "BODY 1"); Text(panel, "studioFaceValue", "FACE 2/8");
        Text(panel, "studioHairValue", "HAIR 3/10"); Text(panel, "studioSuitValue", "FLIGHT SUIT");
        Text(panel, "studioBackValue", "BACK 2/6"); Text(panel, "studioStyleValue", "PROFESSIONAL");
        Text(panel, "studioShapeValue", "CHEVRON"); Text(panel, "studioChargeValue", "WING");
        Text(panel, "studioPaletteValue", "PALETTE 2/8"); Text(panel, "studioArtValue", "PROCEDURAL");
        Text(panel, "studioMessageText", "Custom pilots stay local. Nothing is uploaded.");
    }

    private static void RenderRadio(float height)
    {
        GameObject canvasObject = MakeCanvas("RadioPreview", height, out RectTransform root);
        AvScreen shell = AvScreen.Build(root, "RAD", new[] { "RECEIVER", "MUSIC" }, null,
            3, 480f, height, _ => { });
        SeedShell(shell, "SIGNAL MONITOR");
        Type radio = TypeOf("BoscaliSummer.Features.Radio.Presentation.RadioPanel");
        float receiverHeight = Convert.ToSingle(radio.GetField("RadioContentHeight", All).GetRawConstantValue());
        float deckHeight = Convert.ToSingle(radio.GetField("DeckNaturalHeight", All).GetRawConstantValue());
        RectTransform receiver = AvScreen.Scroll((RectTransform)shell.CreatePage(0, "Receiver").transform,
            shell.Body, receiverHeight, out Rect receiverArea);
        SetStatic(radio, "radioPage", receiver);
        CallStatic(radio, "BuildReceiver", receiver, receiverArea);
        RectTransform music = AvScreen.Scroll((RectTransform)shell.CreatePage(1, "Music").transform,
            shell.Body, Mathf.Max(deckHeight, shell.Body.height), out Rect musicArea);
        CallStatic(radio, "BuildDeck", music, musicArea);
        SeedRadio(radio);

        for (int page = 0; page < 2; page++)
        {
            shell.SetPage(page);
            shell.DataBar.State.text = page == 0 ? "RECEIVER / ON AIR" : "MUSIC / PLAYING";
            ValidateReadable(root, "RAD " + page);
            CaptureScrolled(canvasObject, height, "rad-" + (page == 0 ? "receiver" : "music") + "-" + height);
        }
        object waterfall = GetStatic(radio, "waterfall");
        if (waterfall is IDisposable disposable) disposable.Dispose();
        (GetStatic(radio, "dialMarkers") as IList)?.Clear();
        SetStatic(radio, "waterfall", null); SetStatic(radio, "dialRoot", null);
        SetStatic(radio, "rx", null); SetStatic(radio, "deck", null); SetStatic(radio, "radioPage", null);
        Object.DestroyImmediate(canvasObject);
    }

    private static void SeedRadio(Type radio)
    {
        object rx = GetStatic(radio, "rx");
        Text(rx, "Frequency", "101.7"); Text(rx, "Unit", "MHz");
        Text(rx, "ModeLine", "FM STEREO · 100 kHz"); Text(rx, "Station", "BOSCALI FM");
        Text(rx, "Program", "FIELD OPERATIONS"); Text(rx, "OnAir", "ON AIR · NIGHT DRIVE");
        Text(rx, "Time", "02:14 / 04:32"); Text(rx, "ScopeNote", "87.5–108.0 MHz · FM BROADCAST");
        Text(rx, "StationsNote", "8 FOUND"); Text(rx, "PageValue", "1–5 OF 8");
        int i = 0;
        foreach (object row in (IEnumerable)Get(rx, "Rows"))
        {
            Text(row, "Preset", (i + 1).ToString()); Text(row, "Badge", "B" + (i + 1));
            Text(row, "Name", new[] { "BOSCALI FM", "FRONTLINE RADIO", "AIR CONTROL", "NIGHT SIGNAL", "RESERVE NET" }[i]);
            Text(row, "Frequency", (101.7f + i * .6f).ToString("0.0")); Text(row, "Unit", "MHz");
            Text(row, "Status", i == 0 ? "STRONG" : i < 3 ? "FAIR" : "WEAK");
            i++;
        }

        object deck = GetStatic(radio, "deck");
        Text(deck, "FolderLabel", "LOCAL MUSIC · SORTIE MIX"); Text(deck, "Position", "TRACK 2 / 18");
        Text(deck, "TrackLabel", "NIGHT DRIVE"); Text(deck, "Elapsed", "02:14"); Text(deck, "Duration", "/ 04:32");
        Text(deck, "LibraryNote", "18 TRACKS"); Text(deck, "FolderValue", "1 / 3 · 18 TRK");
        Text(deck, "PageValue", "1–12 OF 18");
        GameObject list = Get(deck, "ListRoot") as GameObject; if (list != null) list.SetActive(true);
        GameObject empty = Get(deck, "EmptyRoot") as GameObject; if (empty != null) empty.SetActive(false);
        i = 0;
        foreach (object row in (IEnumerable)Get(deck, "Rows"))
        {
            GameObject rowRoot = Get(row, "Root") as GameObject; if (rowRoot != null) rowRoot.SetActive(true);
            Text(row, "Number", i == 1 ? ">" : (i + 1).ToString("00"));
            Text(row, "Title", new[] { "WHEELS UP", "NIGHT DRIVE", "LOW ALTITUDE", "RADAR SHADOW",
                "COAST RUN", "DARK APPROACH", "FUEL STATE", "HOME VECTOR", "RESERVE", "AFTERBURNER",
                "FINAL TURN", "TOUCHDOWN" }[i]);
            if (i == 1)
            {
                (Get(row, "Ground") as Image).color = AvTheme.RailInfo.WithAlpha(.1f);
                (Get(row, "Rule") as Image).color = AvTheme.RailInfo;
                (Get(row, "Title") as TMP_Text).color = AvTheme.RailInfo;
            }
            i++;
        }
    }

    private static void RenderWeather(float height)
    {
        GameObject canvasObject = MakeCanvas("WeatherPreview", height, out RectTransform root);
        AvScreen shell = AvScreen.Build(root, "ENV", new[] { "WEATHER", "SKY & AIR" },
            new[] { new[] { "COVER", "CLOUD COVER" }, new[] { "BASE", "CLOUD BASE" },
                    new[] { "WIND", "WIND FROM" }, new[] { "DENSITY", "CAMERA ALT" } },
            2, 480f, height, _ => { });
        SeedShell(shell, "METOC / BATTLEFIELD");
        shell.Metrics[0].Set("35%", "BROKEN", .35f, AvTheme.RailInfo);
        shell.Metrics[1].Set("1,800", "METRES", .60f, AvTheme.RailInfo);
        shell.Metrics[2].Set("12", "KNOTS", .40f, AvTheme.RailReady);
        shell.Metrics[3].Set("92%", "SEA LEVEL", .92f, AvTheme.RailInfo);

        var panelObject = new GameObject("WeatherMfdPanel");
        object panel = panelObject.AddComponent(TypeOf("BoscaliSummer.Features.Weather.Presentation.WeatherMfdPanel"));
        ((Behaviour)panel).enabled = false;
        Set(panel, "shell", shell);
        Call(panel, "BuildForecastPage", shell.CreatePage(0, "Forecast"));
        Call(panel, "BuildEnvironmentPage", shell.CreatePage(1, "Environment"));
        SeedWeather(panel);
        for (int page = 0; page < 2; page++)
        {
            shell.SetPage(page);
            ValidateReadable(root, "ENV page " + page);
            CaptureScrolled(canvasObject, height, "env-" + (page == 0 ? "weather-" : "sky-") + height);
        }
        Object.DestroyImmediate(panelObject);
        Object.DestroyImmediate(canvasObject);
    }

    private static void SeedWeather(object panel)
    {
        Type regimeType = TypeOf("BoscaliSummer.Features.Weather.Domain.WeatherRegimeType");
        object liveGlyph = Get(panel, "liveGlyph");
        Call(liveGlyph, "SetKind", Enum.Parse(regimeType, "Broken"));
        ((Graphic)liveGlyph).color = AvTheme.Warning;
        Text(panel, "liveRegimeTitle", "BROKEN DECK");
        Text(panel, "liveRegimeBadge", "BKN");
        Text(panel, "liveCoverLabel", "52% COVER");
        Text(panel, "liveQuickMetrics", "DECK 2200 M   /   WIND 18 KT   /   RAIN 8%");
        Text(panel, "liveTacticalBrief", "VARIABLE CEILING // WATCH CLOUD BREAKS ON INGRESS");
        string[] types = { "Clear", "Fair", "Scattered", "Broken", "RainSquall", "Storm" };
        string[] codes = { "CLR", "FEW", "SCT", "BKN", "RA+", "TS" };
        int rowIndex = 0;
        foreach (object row in (IEnumerable)Get(panel, "timelineRows"))
        {
            object glyph = Get(row, "Glyph");
            Call(glyph, "SetKind", Enum.Parse(regimeType, types[rowIndex]));
            ((Graphic)glyph).color = rowIndex >= 4 ? AvTheme.Warning : AvTheme.RailInfo;
            (Get(row, "RegimeBadgeText") as TMP_Text).text = codes[rowIndex];
            (Get(row, "ConditionsLabel") as TMP_Text).text = (5 + rowIndex * 17) + "%";
            (Get(row, "DeckLabel") as TMP_Text).text = (3200 - rowIndex * 380) + " M";
            (Get(row, "RainText") as TMP_Text).text = rowIndex >= 4 ?
                (rowIndex * 15) + "% RAIN" : "— DRY —";
            rowIndex++;
        }
        Text(panel, "advisoryLight", "TWILIGHT");
        Text(panel, "advisoryDeck", "BASE 2200 M");
        Text(panel, "advisoryWind", "18 KT");
    }

    private static void RenderEvents(float height)
    {
        GameObject canvasObject = MakeCanvas("EventsPreview", height, out RectTransform root);
        AvScreen shell = AvScreen.Build(root, "EVN", new[] { "DISPATCH", "DESK" },
            new[] { new[] { "SUPPORT COST", "YOUR SIDE" }, new[] { "SUPPORT RESET", "TEMPO" } },
            3, 480f, height, _ => { });
        SeedShell(shell, "EVENT ACTIVE");
        shell.Metrics[0].Set("+35%", "SUPPORT COST", .35f, AvTheme.RailCaution);
        shell.Metrics[1].Set("x1.20", "LONGER COOLDOWN", .20f, AvTheme.RailCaution);

        var panelObject = new GameObject("EventsMfdPanel");
        object panel = panelObject.AddComponent(TypeOf("BoscaliSummer.Features.Events.Presentation.EventsMfdPanel"));
        ((Behaviour)panel).enabled = false;
        object settings = NewSettings("BoscaliSummer.Features.Events.Configuration.EventsSettings",
            "events-fixture.cfg");
        SetEntry(settings, "HistoryLength", 4);
        Set(panel, "settings", settings); Set(panel, "shell", shell);
        Call(panel, "BuildEventsPage", shell.CreatePage(0, "Events"));
        GameObject docs = shell.CreatePage(1, "Docs");
        Call(panel, "BuildDocsPage", docs);
        foreach (TMP_Text copy in docs.GetComponentsInChildren<TMP_Text>(true))
        {
            if (copy.text.Length < 65) continue;
            RectTransform rect = (RectTransform)copy.transform;
            Check(copy.GetPreferredValues(copy.text, rect.rect.width, 0f).y <= rect.rect.height + 1f,
                "EVN docs copy must fit its reading area: " + copy.text.Substring(0, 24));
        }
        SeedEvents(panel);
        shell.SetPage(0);
        ValidateReadable(root, "EVN dispatch");
        CaptureScrolled(canvasObject, height, "evn-events-" + height);
        Call(Get(panel, "activeCard"), "BindPlaceholder", "The theater is quiet. The director is watching for a story worth telling.");
        Call(Get(panel, "decisionBoard"), "SetStandby");
        Call(panel, "LayoutHistory", 3);
        CaptureScrolled(canvasObject, height, "evn-calm-" + height);
        shell.SetPage(1);
        ValidateReadable(root, "EVN docs");
        CaptureScrolled(canvasObject, height, "evn-docs-" + height);
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(panelObject);
        if (height >= 896f) RenderArchive();
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

    private static void SeedEvents(object panel)
    {
        Text(panel, "directorLine", "DIRECTOR ARMED · TRAILING SIDE ELIGIBLE · SUPERS 1/3");
        object current = Event("supply_shock", "SUPPLY SHOCK", "A logistics corridor is under sustained pressure. Allocation costs rise until the theater stabilizes.",
            "ECONOMIC", "MEDIUM", "YOUR SIDE", false, "+35% SUPPORT COST", 120f, 520f);
        object active = Get(panel, "activeCard");
        Call(active, "Bind", current, "+35% SUPPORT COST", AvTheme.RailCaution,
            "Your side pays more for support while the corridor remains disrupted.");
        Call(active, "SetClock", "ENDS 04:18", false, false);
        Call(active, "SetProgress", .64f, AvTheme.RailCaution);

        object desk = Get(panel, "decisionBoard");
        ((RectTransform)Get(desk, "root")).gameObject.SetActive(true);
        Text(desk, "subheading", "CHOOSE ONE RESPONSE");
        var choices = (AvButton[])Get(desk, "actions");
        var details = (TMP_Text[])Get(desk, "details");
        var reasons = (TMP_Text[])Get(desk, "reasons");
        string[] names = { "CONTAIN", "TREASURY DIRECTIVE", "CONTRACT INTELLIGENCE", "PILOT CHANNEL" };
        string[] requirements = { "PERSONAL ALLOCATION", "FACTION FUNDS", "HOST CHECKS FACTION CONTRACT", "RECON QUALIFICATION REQUIRED" };
        for (int choice = 0; choice < choices.Length; choice++)
        {
            choices[choice].SetText(names[choice]);
            details[choice].text = "HOST COST QUOTE";
            reasons[choice].text = requirements[choice];
        }

        Call(panel, "LayoutHistory", 3);
        int i = 0;
        foreach (object card in (IEnumerable)Get(panel, "historyCards"))
        {
            if (i >= 3) break;
            object view = Event("history_" + i, new[] { "AIRLIFT SURGE", "RADAR BLACKOUT", "FUEL PRIORITY" }[i],
                "Completed theater event.", i == 1 ? "HAZARD" : "POLITICAL", i == 0 ? "MEDIUM" : "MINOR",
                "ALL THEATER", false, i == 1 ? "NO EFFECT" : "-15% SUPPORT COST", 0f, 1f);
            Call(card, "Bind", view, AvTheme.TextPrimary, i == 1 ? AvTheme.RailDanger : AvTheme.RailInfo,
                i == 1 ? AvTheme.RailDanger : AvTheme.RailReady);
            Call(card, "SetClock", (i + 2) + " MIN AGO");
            i++;
        }
    }

    private static object Event(string id, string title, string flavor, string category, string tier,
        string target, bool isSuper, string effect, float start, float end)
    {
        Type step = TypeOf("BoscaliSummer.Framework.Contracts.ActiveEventStep");
        Type view = TypeOf("BoscaliSummer.Framework.Contracts.ActiveEventView");
        Array steps = Array.CreateInstance(step, 0);
        string art = category == "HAZARD" ? "fuel_depot_fire" : "industrial_surge";
        return Activator.CreateInstance(view, new object[] { id, title, flavor, category, tier, target,
            isSuper, art, effect, steps, start, end, true, "+20% SUPPORT RESET" });
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

    private static void RenderTargetBoard()
    {
        // The external-view dock from modules/Hud, built from the shipped DLL. Hud/HudUnityCheck
        // pins its geometry against game stubs; this pins what the pilot reads off it: both shot
        // directions counted and drawn, every word legible, nothing past the safe area.
        var owner = new GameObject("BoardOwner");
        object board = Activator.CreateInstance(
            TypeOf("BoscaliSummer.Features.Hud.Presentation.ThirdPersonTargetBoard"), owner.transform);
        object settings = NewSettings("BoscaliSummer.Features.Hud.Configuration.HudSettings",
            "hud-board-fixture.cfg");
        object systems = Activator.CreateInstance(TypeOf("BoscaliSummer.Features.Hud.Runtime.SystemsReading"));
        Set(systems, "Valid", true);
        Set(systems, "FaultsAvailable", true);
        Set(systems, "Damaged", 2);
        Set(systems, "Failures", 1);
        object missiles = Activator.CreateInstance(TypeOf("BoscaliSummer.Features.Hud.Runtime.MissileTelemetry"));
        // A 3:2 picture in the 16:9 camera slot, so the letterboxed feed is part of what must fit.
        var picture = new RenderTexture(336, 224, 0);
        picture.Create();

        // Three out, two in, interleaved: uneven counts catch swapped IN/OUT labels, the mixed
        // order checks that lanes follow direction rather than list position, and three rows is
        // the tallest the dock gets. Tracks come from ShotMath the way the reader starts them.
        var shots = new (bool Outbound, string Seeker, string Range, float First, float Now, string Eta)[]
        {
            (true, "ARH", "4.2 km", 4200f, 3800f, "10s"),
            (false, "IR", "1.8 km", 2000f, 1500f, "3s"),
            (true, "SARH", "6.8 km", 7000f, 6800f, "34s"),
            (false, "ARH", "9.6 km", 10000f, 9600f, "24s"),
            (true, "IR", "2.1 km", 2400f, 2100f, "7s"),
        };
        Type shotEntry = TypeOf("BoscaliSummer.Features.Hud.Runtime.MissileTelemetry+ShotEntry");
        Type shotMath = TypeOf("BoscaliSummer.Features.Hud.Domain.ShotMath");
        var shown = (IList)Get(missiles, "shown");
        foreach (var shot in shots)
            shown.Add(ShotFixture(shotEntry, shotMath, shot.Outbound, shot.Seeker, shot.Range, shot.First, shot.Now));
        Set(missiles, "shownOutbound", 3);
        Set(missiles, "shownInbound", 2);
        Call(board, "Present", settings, systems, missiles, picture, "FORWARD", "MARK 1.2 km  34s");

        object surface = Get(board, "surface");
        var canvas = (Canvas)Get(surface, "Canvas");
        var panel = (RectTransform)Get(board, "panel");
        var inbound = (TMP_Text)Get(board, "inbound");
        var outbound = (TMP_Text)Get(board, "outbound");
        var rows = (IList)Get(board, "shotRows");
        Check(((GameObject)Get(surface, "Root")).activeSelf,
            "Target board must present at the editor's " + Screen.width + "x" + Screen.height + " screen.");
        Check(inbound.text == "IN 02" && outbound.text == "OUT 03",
            "Target board must count both shot directions, got " + inbound.text + " / " + outbound.text + ".");
        Check(inbound.color != outbound.color,
            "Inbound shots must set the IN count apart from the OUT count.");
        int inboundSlot = 0, outboundSlot = 3;
        foreach (var shot in shots)
        {
            object row = rows[shot.Outbound ? outboundSlot++ : inboundSlot++];
            string lane = "Target board " + (shot.Outbound ? "OUT " : "IN ") + shot.Seeker + " row";
            Check(((RectTransform)Get(row, "Rect")).gameObject.activeSelf &&
                  ((TMP_Text)Get(row, "seeker")).text == shot.Seeker &&
                  ((TMP_Text)Get(row, "range")).text == shot.Range &&
                  ((TMP_Text)Get(row, "eta")).text == shot.Eta,
                lane + " must show its seeker, range and countdown in its direction's lane.");
            // The tip sits the remaining share of first-seen range away from the row's endpoint:
            // the left end for an inbound shot, the right end for an outbound one.
            float remaining = shot.Now / shot.First;
            float tip = ((Image)Get(row, "tip")).rectTransform.anchoredPosition.x;
            Check(Mathf.Abs(tip - (3f + 132f * (shot.Outbound ? 1f - remaining : remaining))) < .5f,
                lane + " must drain against its first-seen range.");
        }
        Check(!((RectTransform)Get(rows[2], "Rect")).gameObject.activeSelf,
            "The unused third IN lane row must stay empty.");
        ValidateBoard(canvas, panel, "Target board shots");
        CaptureBoard(canvas, "target-board-shots.png");

        // The usual state: nothing in the air, no target camera, no mark. The counts still
        // answer for both directions, and the standby copy must be as legible as the rest.
        shown.Clear();
        Set(missiles, "shownOutbound", 0);
        Set(missiles, "shownInbound", 0);
        Call(board, "Present", settings, systems, missiles, null, "FORWARD", null);
        Check(inbound.text == "IN --" && outbound.text == "OUT --" && inbound.color == outbound.color,
            "With no shots both directions must read empty and IN must drop its warning tone.");
        foreach (object row in rows)
            Check(!((RectTransform)Get(row, "Rect")).gameObject.activeSelf,
                "With no shots no lane row may stay drawn.");
        ValidateBoard(canvas, panel, "Target board idle");
        CaptureBoard(canvas, "target-board-idle.png");

        Call(board, "Hide");
        Object.DestroyImmediate(owner);
        picture.Release(); Object.DestroyImmediate(picture);
    }

    private static object ShotFixture(Type shotEntry, Type shotMath, bool outbound, string seeker,
        string rangeText, float first, float range)
    {
        object track = CallStatic(shotMath, "Start", first, 0f);
        track = CallStatic(shotMath, "Update", track, range, 1f);
        object entry = Activator.CreateInstance(shotEntry, true);
        Set(entry, "Outbound", outbound);
        Set(entry, "Seeker", seeker);
        Set(entry, "RangeText", rangeText);
        Set(entry, "Track", track);
        return entry;
    }

    private static void ValidateBoard(Canvas canvas, RectTransform panel, string name)
    {
        ValidateReadable(panel, name);
        // On screen: the panel is placed in canvas units from the canvas's bottom-left, which a
        // screen-space canvas pins to the screen's, so its pixels are its placement times the scale.
        // Then every drawn piece, text, lines, backing and picture, stays inside that panel.
        Rect screen = Screen.safeArea;
        if (screen.width < 1 || screen.height < 1) screen = new Rect(0, 0, Screen.width, Screen.height);
        Vector2 min = panel.anchoredPosition * canvas.scaleFactor;
        Vector2 max = (panel.anchoredPosition + panel.sizeDelta) * canvas.scaleFactor;
        Check(min.x >= screen.xMin - .5f && min.y >= screen.yMin - .5f &&
              max.x <= screen.xMax + .5f && max.y <= screen.yMax + .5f,
            name + " must stay inside the screen's safe area.");
        foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>())
        {
            if (!graphic.enabled) continue;
            Rect rect = PanelRect(panel, graphic.rectTransform);
            Check(rect.xMin >= -.5f && rect.yMin >= -.5f &&
                  rect.xMax <= panel.rect.width + .5f && rect.yMax <= panel.rect.height + .5f,
                name + " draws " + graphic.name + " outside its own panel.");
        }
        // Legible: no line ellipsized by its own box, and no two lines drawn over each other.
        var labels = new List<TMP_Text>();
        foreach (TMP_Text label in panel.GetComponentsInChildren<TMP_Text>())
            if (!string.IsNullOrWhiteSpace(label.text)) labels.Add(label);
        for (int i = 0; i < labels.Count; i++)
        {
            Check(labels[i].preferredWidth <= labels[i].rectTransform.rect.width + .5f,
                name + " cuts \"" + labels[i].text + "\" short.");
            Rect own = PanelRect(panel, labels[i].rectTransform);
            own = new Rect(own.x + .5f, own.y + .5f, own.width - 1f, own.height - 1f);
            for (int j = i + 1; j < labels.Count; j++)
                Check(!own.Overlaps(PanelRect(panel, labels[j].rectTransform)),
                    name + " draws \"" + labels[i].text + "\" over \"" + labels[j].text + "\".");
        }
    }

    private static Rect PanelRect(RectTransform panel, RectTransform child)
    {
        var corners = new Vector3[4];
        child.GetWorldCorners(corners);
        Vector2 min = panel.InverseTransformPoint(corners[0]), max = min;
        for (int i = 1; i < 4; i++)
        {
            Vector2 corner = panel.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, corner);
            max = Vector2.Max(max, corner);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void CaptureBoard(Canvas canvas, string file)
    {
        // Frame the whole screen in canvas units so the corner placement shows, then hand the
        // canvas back to screen space for the next Present.
        float width = Screen.width / canvas.scaleFactor, height = Screen.height / canvas.scaleFactor;
        PrepareWorldCanvas(canvas, width, height);
        Capture(canvas.gameObject, width, height, file);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    }

    private static void ValidateSkillRows(object panel)
    {
        Canvas.ForceUpdateCanvases();
        foreach (object row in (IEnumerable)Get(panel, "skillRows"))
        {
            RectTransform cell = ((Image)Get(row, "Fill")).rectTransform;
            RectTransform name = ((TMP_Text)Get(row, "Name")).rectTransform;
            RectTransform state = ((TMP_Text)Get(row, "State")).rectTransform;
            Vector3[] cellCorners = new Vector3[4];
            Vector3[] nameCorners = new Vector3[4];
            Vector3[] stateCorners = new Vector3[4];
            cell.GetWorldCorners(cellCorners);
            name.GetWorldCorners(nameCorners);
            state.GetWorldCorners(stateCorners);
            Check(stateCorners[0].y + .5f >= cellCorners[0].y &&
                  stateCorners[2].y <= cellCorners[2].y + .5f,
                "SQD skill state line escapes its cell.");
            Check(stateCorners[2].y <= nameCorners[0].y + .5f,
                "SQD skill state line overlaps its name lane.");
        }
    }

    private static GameObject MakeCanvas(string name, float height, out RectTransform root)
    {
        var canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        PrepareWorldCanvas(canvas, 480f, height);
        root = (RectTransform)canvasObject.transform;
        AvKit.Panel(root, new Rect(0f, 0f, 480f, height), AvTheme.SurfaceInert);
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

    private static void SeedShell(AvScreen shell, string state)
    {
        shell.DataBar.State.text = state;
        shell.DataBar.SetChip(0, "OFFLINE QA", "info");
        shell.DataBar.SetChip(1, "FIXTURE DATA", "inert");
        shell.DataBar.SetChip(2, "NO ORDERS", "inert");
        shell.WriteStatus(null, null, "Offline layout check · production builder · no live game state.");
    }

    private static void CaptureScrolled(GameObject canvas, float height, string prefix)
    {
        foreach (ScrollRect scroll in canvas.GetComponentsInChildren<ScrollRect>(true))
        {
            if (!scroll.gameObject.activeInHierarchy) continue;
            Check(scroll.content.rect.height + .5f >= scroll.viewport.rect.height,
                prefix + " scroll content must cover its viewport.");
            scroll.verticalNormalizedPosition = 1f;
        }
        Capture(canvas, 480f, height, prefix + "-top.png");
        foreach (ScrollRect scroll in canvas.GetComponentsInChildren<ScrollRect>(true))
            if (scroll.gameObject.activeInHierarchy) scroll.verticalNormalizedPosition = 0f;
        Capture(canvas, 480f, height, prefix + "-bottom.png");
    }

    private static void ValidateReadable(RectTransform root, string name)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!text.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(text.text)) continue;
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

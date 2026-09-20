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

    public static void Run()
    {
        try
        {
            if (Shader.Find("TextMeshPro/Distance Field") == null)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += Run;
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
            foreach (float height in new[] { 420f, 596f, 896f })
            {
                RenderSqd(height);
                RenderRadio(height);
                RenderEvents(height);
            }
            RenderEventAlert();
            RenderCameraPanel();
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
        AvScreen shell = AvScreen.Build(root, "SQD",
            new[] { "01 PILOT", "02 SKILLS", "03 WINGS", "04 STUDIO" },
            new[] { new[] { "PILOT SCORE", "THIS PILOT" }, new[] { "QUAL. PICKS", "UNSPENT" } },
            3, 480f, height, _ => { });
        SeedShell(shell, "SQUADRON STATUS");
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

        Type wingLink = TypeOf("BoscaliSummer.Runtime.WingLink");
        SetStatic(wingLink, "studioResolved", true);
        SetStatic(wingLink, "studioUnavailableReason", string.Empty);

        string[] methods = { "BuildPilotPage", "BuildSkillsPage", "BuildWingsPage", "BuildStudioPage" };
        string[] names = { "pilot", "skills", "wings", "studio" };
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
            ValidateReadable(root, "SQD " + names[page]);
            CaptureScrolled(canvasObject, height, "sqd-" + names[page] + "-" + height);
        }
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(panelObject);
        Object.DestroyImmediate(managerObject);
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
            Text(row, "Number", (i + 1).ToString("00"));
            Text(row, "Title", new[] { "WHEELS UP", "NIGHT DRIVE", "LOW ALTITUDE", "RADAR SHADOW",
                "COAST RUN", "DARK APPROACH", "FUEL STATE", "HOME VECTOR", "RESERVE", "AFTERBURNER",
                "FINAL TURN", "TOUCHDOWN" }[i]);
            i++;
        }
    }

    private static void RenderEvents(float height)
    {
        GameObject canvasObject = MakeCanvas("EventsPreview", height, out RectTransform root);
        AvScreen shell = AvScreen.Build(root, "EVN", Array.Empty<string>(),
            new[] { new[] { "SUPPORT COST", "YOUR SIDE" }, new[] { "GROUND CUSTODY", "BASES" } },
            3, 480f, height, _ => { });
        SeedShell(shell, "EVENT ACTIVE");
        shell.Metrics[0].Set("+35%", "SUPPORT COST", .35f, AvTheme.RailCaution);
        shell.Metrics[1].Set("5 : 3", "CONTESTED", .63f, AvTheme.RailInfo);

        var panelObject = new GameObject("EventsMfdPanel");
        object panel = panelObject.AddComponent(TypeOf("BoscaliSummer.Features.Events.Presentation.EventsMfdPanel"));
        ((Behaviour)panel).enabled = false;
        object settings = NewSettings("BoscaliSummer.Features.Events.Configuration.EventsSettings",
            "events-fixture.cfg");
        SetEntry(settings, "HistoryLength", 4);
        Set(panel, "settings", settings); Set(panel, "shell", shell);
        Call(panel, "BuildEventsPage", shell.CreatePage(0, "Events"));
        SeedEvents(panel);
        shell.SetPage(0);
        ValidateReadable(root, "EVN");
        CaptureScrolled(canvasObject, height, "evn-events-" + height);
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(panelObject);
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

        Call(panel, "LayoutHistory", 3);
        int i = 0;
        foreach (object card in (IEnumerable)Get(panel, "historyCards"))
        {
            if (i >= 3) break;
            object view = Event("history_" + i, new[] { "AIRLIFT SURGE", "RADAR BLACKOUT", "FUEL PRIORITY" }[i],
                "Completed theater event.", i == 1 ? "HAZARD" : "POLITICAL", i == 0 ? "MEDIUM" : "MINOR",
                "ALL THEATER", false, i == 1 ? "+20% SUPPORT COST" : "-15% SUPPORT COST", 0f, 1f);
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
        return Activator.CreateInstance(view, new object[] { id, title, flavor, category, tier, target,
            isSuper, string.Empty, effect, steps, start, end });
    }

    private static void RenderEventAlert()
    {
        var componentObject = new GameObject("AlertComponent");
        object alert = componentObject.AddComponent(TypeOf("BoscaliSummer.Features.Events.Presentation.SuperEventAlert"));
        ((Behaviour)alert).enabled = false;
        Call(alert, "Build");
        GameObject root = (GameObject)Get(alert, "root");
        Canvas canvas = root.GetComponent<Canvas>();
        PrepareWorldCanvas(canvas, 1920f, 1080f);
        ((CanvasGroup)Get(alert, "group")).alpha = 1f;
        Text(alert, "stamp", "SUPEREVENT · THEATER ALERT");
        Text(alert, "subtitle", "EVENT DIRECTOR · HAZARD · ACTIVE");
        Text(alert, "title", "BROKEN ARROW");
        Text(alert, "flavor", "A strategic weapons convoy has gone dark. Every faction is searching, and the next few minutes will decide who reaches it first.");
        Text(alert, "note", "EFFECT  +50% SUPPORT COST  ·  TARGET  ALL THEATER");
        Text(alert, "clock", "AUTO-DISMISS 0:24");
        int i = 0;
        foreach (TMP_Text label in (IEnumerable)Get(alert, "stepLabels"))
        {
            label.gameObject.SetActive(i < 3);
            if (i < 3) label.text = "T+" + (i + 1) + ":00   " + new[] { "SEARCH GRID EXPANDS", "ESCORTS COMMIT", "RECOVERY WINDOW CLOSES" }[i];
            i++;
        }
        Image art = (Image)Get(alert, "art"); art.enabled = false;
        Component glyph = (Component)Get(alert, "glyph"); glyph.gameObject.SetActive(true);
        ValidateReadable((RectTransform)root.transform, "EVN alert");
        Capture(root, 1920f, 1080f, "evn-alert.png");
        Object.DestroyImmediate(root); Object.DestroyImmediate(componentObject);
    }

    private static void RenderCameraPanel()
    {
        object cameraPanel = Activator.CreateInstance(TypeOf("BoscaliSummer.Features.QoL.Presentation.ThirdPersonCameraPanel"), true);
        var owner = new GameObject("CameraOwner");
        Call(cameraPanel, "Create", owner.transform);
        GameObject root = (GameObject)Get(cameraPanel, "root");
        PrepareWorldCanvas(root.GetComponent<Canvas>(), 640f, 480f);
        Call(cameraPanel, "Layout", (object)null);
        Call(cameraPanel, "PresentFeed", null, true);
        Text(cameraPanel, "selection", "TRACKING · 3 VALID TARGETS");
        Text(cameraPanel, "contactAge", "CONTACT · CURRENT");
        Text(cameraPanel, "markStatus", "MARK · X 2480 / Z 7310 · 2s OLD");
        ValidateReadable((RectTransform)root.transform, "CAM");
        Capture(root, 640f, 480f, "qol-target-camera.png");
        Object.DestroyImmediate(root); Object.DestroyImmediate(owner);
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

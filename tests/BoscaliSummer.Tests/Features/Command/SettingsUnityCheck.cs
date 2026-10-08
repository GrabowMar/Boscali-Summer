#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using BoscaliSummer.Core.Game;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

public static class SettingsUnityCheck
{
    private static readonly List<string> Failures = new List<string>();
    private static int checkedTexts;

    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;
            SetExecutablePath("SettingsCheck.exe");
            AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            new GameObject("Events", typeof(EventSystem));
            CheckMfdLookup();
            CheckLayoutCanvas();
            CheckScreenSpaceSizing();
            foreach (int height in new[] { 896, 596, 420 }) CheckPanel(height);
            if (Failures.Count > 0)
                throw new Exception(Failures.Count + " layout failure(s) over " + checkedTexts + " texts:\n" + string.Join("\n", Failures.GetRange(0, Math.Min(40, Failures.Count))));
            File.WriteAllText("result.txt", "PASS: SET renders THIS PILOT (DISPLAY/MAP/COCKPIT/IMMERSION/PERFORMANCE) and SERVER (WORLD/FORCES/EFFECTS/TASKING) " +
                "consoles as client and host at 896, 596 and 420 units (" + checkedTexts + " texts gated for overflow, overlap, gutter and 11 px floor); " +
                "mode switch and remembered mode, toggles, background replacement and row reveal, disabled dependencies, +/- bounds, " +
                "host-only lock on SERVER rows and the flat page tree (no GameObject churn) are checked. Game adapters are stubbed; in-game acceptance remains required.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static void CheckMfdLookup()
    {
        var canvas = new GameObject("MapCanvas", typeof(Canvas)).GetComponent<Canvas>();
        var controller = new GameObject("SiblingMfd", typeof(VirtualMFD)).GetComponent<VirtualMFD>();
        controller.gameObject.SetActive(false);
        MapMfdLookup.Reset();
        Check(canvas.GetComponentInChildren<VirtualMFD>(true) == null, "Regression fixture must put MFD outside canvas");
        Check(MapMfdLookup.Resolve(canvas) == controller, "SET must find the inactive sibling controller");
        Check(MapMfdLookup.Resolve(canvas) == controller, "Manager and SET must reuse the cached controller");
        MapMfdLookup.Reset();
        Check(MapMfdLookup.Resolve(null) == controller, "SET must find the controller before the map canvas exists");
        Object.DestroyImmediate(controller.gameObject);
        Object.DestroyImmediate(canvas.gameObject);
        MapMfdLookup.Reset();
    }

    private static void CheckLayoutCanvas()
    {
        // The map canvas is a nested canvas whose rect is not synchronised until the canvas
        // is (re)activated; on the first open of a mission it can still report the previous
        // size. The layout must divide up the real UI area instead.
        var root = new GameObject("LayoutRoot", typeof(Canvas), typeof(RectTransform));
        root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rootRt = (RectTransform)root.transform;
        rootRt.sizeDelta = new Vector2(1920f, 1080f);

        var nested = new GameObject("MaximizedMapCanvas", typeof(Canvas));
        var nestedRt = (RectTransform)nested.transform;
        nestedRt.SetParent(rootRt, false);
        nestedRt.sizeDelta = new Vector2(1320f, 920f);

        Canvas nestedCanvas = nested.GetComponent<Canvas>();
        Check(MfdLayout.CanvasSize(nestedCanvas) == new Vector2(1920f, 1080f),
            "Layout must resolve the real UI area, not a stale nested-canvas rect");
        Check(MfdLayout.TryResolve(nestedCanvas, out MfdLayout.Columns columns) &&
              columns.Map == MfdLayout.Resolve(new Vector2(1920f, 1080f)).Map,
            "Full-area columns must be resolved while the nested rect is stale");

        Object.DestroyImmediate(root);
    }

    private static void CheckScreenSpaceSizing()
    {
        // The screen-space map canvas keeps its previous RectTransform size until the canvas
        // update after activation. The layout must follow the live screen and scale factor
        // instead, or the first open of a mission inherits the stale size.
        if (Screen.width < 2 || Screen.height < 2) return;

        var root = new GameObject("OverlayRoot", typeof(Canvas), typeof(RectTransform));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        ((RectTransform)root.transform).sizeDelta = new Vector2(1315f, 921f);

        Canvas canvas = root.GetComponent<Canvas>();
        const float referenceWidth = 960f;
        canvas.scaleFactor = Screen.width / referenceWidth;

        Vector2 expected = new Vector2(referenceWidth, Screen.height * referenceWidth / Screen.width);
        Check(Vector2.Distance(MfdLayout.CanvasSize(canvas), expected) < 0.5f,
            "Screen-space layout must follow the live screen and scale, not the stale canvas rect");

        Object.DestroyImmediate(root);
    }

    // Page indices per console, matching SettingsMfdPanel's private constants.
    private const int CDisplay = 0, CMap = 1, CCockpit = 2, CImmersion = 3, CPerf = 4;
    private const int SWorld = 0, SForces = 1, SEffects = 2, STasking = 3;

    private static void CheckPanel(int height)
    {
        GameAccess.ForceServer = false;
        ModuleServices.Services.Remove(typeof(ISecondaryObjectivesView));
        var hud = new HudFixture();
        ModuleServices.Services[typeof(IHudBoard)] = hud;
        var config = new CommandSettings(new ConfigFile(Path.GetFullPath("settings-" + height + "-" + Guid.NewGuid().ToString("N") + ".cfg"), false));
        // Exercise compatibility with an existing layered configuration.
        config.DeckGrid.Value = true;
        config.BackgroundImage.Value = true;
        var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = height / 2f;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.02f, .035f, .025f);
        var canvas = new GameObject("Canvas", typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(480, height);
        var panel = canvas.gameObject.AddComponent<SettingsMfdPanel>();
        var hostBoard = new HostSettingsBoard();
        var fire = new HostFixture("FIRE AND DESTRUCTION", HostSettingsPage.Effects,
            "T:FIRE IGNITION:1", "S:MAX FIRE SITES:24", "S:BURN TIME:90 s");
        var events = new HostFixture("WORLD EVENTS", HostSettingsPage.Settings, "T:SUPEREVENTS:1", "S:EVENT PACE:1.00x");
        hostBoard.Add(new HostFixture("COMMS", HostSettingsPage.Settings, "T:ALL CHANNEL:0"));
        hostBoard.Add(new HostFixture("DYNAMIC OPERATIONS", HostSettingsPage.Settings,
            "S:REWARD SCALE:1.00x", "S:CONTRACT LIMIT:3", "T:TIMED CONTRACTS:1"));
        hostBoard.Add(events);
        hostBoard.Add(new HostFixture("HIGH COMMAND", HostSettingsPage.Settings,
            "T:STIPENDS AND KILL PAY:1", "S:STAFF STIPEND:120", "T:ESCROW HOLD:0"));
        hostBoard.Add(new HostFixture("PROGRESSION", HostSettingsPage.Settings, "S:SCORE PER GRADE:100", "S:GRADE CEILING:12"));
        hostBoard.Add(new HostFixture("SQUAD AND ACES", HostSettingsPage.Settings, "S:PILOT CAREER:RESPAWN", "T:ACE HUNTS:1"));
        hostBoard.Add(new HostFixture("SUPPORT CALL-INS", HostSettingsPage.Settings,
            "T:RADAR SCAN:1", "T:ZONE FORTIFICATION:1", "T:ROD FROM GOD:0", "T:EMP SHOCK:1", "T:ELINT SWEEP:1"));
        hostBoard.Add(new HostFixture("TRENCHES", HostSettingsPage.Settings, "S:MAX NETWORKS:8"));
        hostBoard.Add(new HostFixture("URBAN COMBAT", HostSettingsPage.Settings, "T:ZONE GARRISONS:1", "S:MAX GARRISONS:12"));
        hostBoard.Add(fire);
        hostBoard.Add(new HostFixture("WEATHER", HostSettingsPage.Effects, "T:CHANGING WEATHER:1", "S:CHANGE INTERVAL:20 min"));
        var clientBoard = new ClientSettingsBoard();
        var performance = config.ExpandedMapUi.ConfigFile.Bind("Performance", "Enabled", false, "Live adaptive FX");
        var rain = config.ExpandedMapUi.ConfigFile.Bind("Weather", "RainVisualsEnabled", true, "Live rain particles");
        var canopy = config.ExpandedMapUi.ConfigFile.Bind("Weather", "CanopyRainEnabled", true, "Live canopy droplets");
        var terrain = config.ExpandedMapUi.ConfigFile.Bind("Weather", "TerrainRainEnabled", true, "Live terrain wet pass");
        clientBoard.Add("FRAME BUDGET", "ADAPTIVE FX", "Live; no restart.", performance);
        clientBoard.Add("RAIN VISUALS", "RAIN FX MASTER", "Live; no restart.", rain);
        clientBoard.Add("RAIN VISUALS", "CANOPY DROPLETS", "Live; no restart.", canopy);
        clientBoard.Add("RAIN VISUALS", "TERRAIN WET PASS", "Live; no restart.", terrain);
        panel.Configure(config, null, null, hostBoard, clientBoard);
        typeof(SettingsMfdPanel).GetField("lastServerMode", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);

        // Install() needs a real bezel claim (stubbed to always fail offline), so the consoles are built
        // through the same BuildConsoles() Install() calls, with the content bound after Finish().
        Invoke(panel, "BuildConsoles", (RectTransform)canvas.transform, (float)height);
        var clientCon = (AvConsole)Field(panel, "clientCon");
        var serverCon = (AvConsole)Field(panel, "serverCon");
        Check(clientCon != null && serverCon != null, "Both consoles must be built");
        Check(clientCon.PageCount == 5 && serverCon.PageCount == 4, "Client carries immersion alongside its four existing pages");
        Check(clientCon.Root.gameObject.activeSelf && !serverCon.Root.gameObject.activeSelf, "THIS PILOT is the default mode");
        clientCon.Ticker.TickNow();

        // ---- every page of both modes, as a remote client and as the host
        foreach (bool host in new[] { false, true })
        {
            GameAccess.ForceServer = host;
            foreach (bool server in new[] { false, true })
            {
                ShowMode(panel, clientCon, serverCon, server);
                AvConsole con = server ? serverCon : clientCon;
                for (int page = 0; page < con.PageCount; page++)
                {
                    con.SetPage(page);
                    Settle(con);
                    string tag = (server ? "server" : "client") + "-" + (host ? "host" : "remote") + "-" + page;
                    Render(camera, canvas, height, tag);
                    if (height == 596 && !server && page == CDisplay && !host)
                    {
                        Image finish = AvDisplayGlass.AttachFullDisplay((RectTransform)canvas.transform);
                        for (int color = 1; color <= 4; color++)
                        {
                            config.DisplayTint.Value = color;
                            config.DisplayTintStrength.Value = .7f;
                            config.DisplayScanlines.Value = .5f;
                            config.DisplayVignette.Value = .4f;
                            Invoke(panel, "ApplyDisplayEffects");
                            foreach (var glass in canvas.GetComponentsInChildren<AvDisplayGlass>()) glass.Update();
                            Render(camera, canvas, height, "tint-" + color);
                        }
                        Object.DestroyImmediate(finish.gameObject);
                        config.DisplayTint.Value = 0;
                        config.DisplayTintStrength.Value = .25f;
                        config.DisplayScanlines.Value = 0f;
                        config.DisplayVignette.Value = 0f;
                        Invoke(panel, "ApplyDisplayEffects");
                        foreach (var glass in canvas.GetComponentsInChildren<AvDisplayGlass>()) glass.Update();
                    }
                }
            }
        }
        GameAccess.ForceServer = false;

        // ---- the mode switch itself: real clicks, remembered across opens
        ShowMode(panel, clientCon, serverCon, false);
        Check(clientCon.Root.gameObject.activeSelf && !serverCon.Root.gameObject.activeSelf, "Client mode shows only the client console");
        Click(FindControl(clientCon.Root, "SERVER"));
        Check(!clientCon.Root.gameObject.activeSelf && serverCon.Root.gameObject.activeSelf, "SERVER click must swap the consoles");
        Check((bool)typeof(SettingsMfdPanel).GetField("lastServerMode", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null),
            "The chosen mode is remembered for the next open");
        Settle(serverCon);
        Check(serverCon.Root.GetComponentsInChildren<Transform>(true).Length > 0 &&
              serverCon.Root.Find("Mode") != null, "SERVER console carries its own mode switch");
        if (AvIcons.Available)
            Check(ServerGlyph(serverCon) == AvIcons.Glyph(AvIcon.Lock), "A remote client sees a lock on the SERVER segment");
        Click(FindControl(serverCon.Root, "THIS PILOT"));
        Check(clientCon.Root.gameObject.activeSelf && !serverCon.Root.gameObject.activeSelf, "THIS PILOT click must swap back");

        // ---- SERVER: categorisation, lock for a remote client, live for the host
        ShowMode(panel, clientCon, serverCon, true);
        RectTransform world = serverCon.Page(SWorld).Content, forces = serverCon.Page(SForces).Content,
            effects = serverCon.Page(SEffects).Content;
        foreach (string s in new[] { "DYNAMIC OPERATIONS", "WORLD EVENTS", "TRENCHES", "URBAN COMBAT" })
            Check(HasText(world, s), "WORLD lists " + s);
        foreach (string s in new[] { "HIGH COMMAND", "SUPPORT CALL-INS", "SQUAD AND ACES", "PROGRESSION", "COMMS" })
            Check(HasText(forces, s), "FORCES lists " + s);
        foreach (string s in new[] { "FIRE AND DESTRUCTION", "WEATHER", "MAX FIRE SITES" })
            Check(HasText(effects, s), "EFFECTS lists " + s);
        Check(!HasText(world, "HIGH COMMAND") && !HasText(forces, "WEATHER") && !HasText(effects, "TRENCHES"),
            "Sections appear on exactly one tab");
        serverCon.SetPage(SEffects);
        Settle(serverCon);
        Check(HasText(effects, "LOCKED. Only the host can change this; you are seeing the host's value.") ||
              HasSubText(effects, "LOCKED"), "A remote client sees why a SERVER row is locked");
        Click(Plus(effects, "MAX FIRE SITES"));
        Check(fire.Steps == 0, "A remote client cannot step a host setting");
        serverCon.SetPage(SWorld);
        Click(FindRowByName(world, "SUPEREVENTS"));
        Check(events.Toggles == 0, "A remote client cannot toggle a host setting");
        GameAccess.ForceServer = true;
        Settle(serverCon);
        serverCon.SetPage(SEffects);
        Settle(serverCon);
        Click(Plus(effects, "MAX FIRE SITES"));
        Check(fire.Steps == 1, "The host steps a host setting");
        serverCon.SetPage(SWorld);
        Settle(serverCon);
        Click(FindRowByName(world, "SUPEREVENTS"));
        Check(events.Toggles == 1, "The host toggles a host setting");
        if (AvIcons.Available)
            Check(ServerGlyph(serverCon) == AvIcons.Glyph(AvIcon.Database), "The host's SERVER segment carries no lock");

        // populated tasking board
        var board = new TaskingFixture();
        ModuleServices.Services[typeof(ISecondaryObjectivesView)] = board;
        serverCon.SetPage(STasking);
        Settle(serverCon);
        Settle(serverCon);
        Check(HasText(serverCon.Page(STasking).Content, "RELAY STRIKE"), "SERVER tasking lists the host's contracts");
        Render(camera, canvas, height, "server-host-tasking-populated");
        GameAccess.ForceServer = false;
        Settle(serverCon);
        Render(camera, canvas, height, "server-remote-tasking-populated");
        ModuleServices.Services.Remove(typeof(ISecondaryObjectivesView));
        ShowMode(panel, clientCon, serverCon, false);

        // ---- THIS PILOT: rows write their saved entries
        RectTransform mapContent = clientCon.Page(CMap).Content;
        RectTransform displayContent = clientCon.Page(CDisplay).Content;
        RectTransform cockpitContent = clientCon.Page(CCockpit).Content;
        RectTransform perfContent = clientCon.Page(CPerf).Content;

        clientCon.SetPage(CMap);
        Settle(clientCon);
        Check(!IsShown(mapContent, "IMAGE FILE") || config.BackgroundImagePreset.Value == 3,
            "IMAGE FILE only appears for the custom background");
        Click(FindRowByName(mapContent, "EXPANDED LAYOUT"));
        Check(!config.ExpandedMapUi.Value, "Expanded toggle must change persisted config");
        float before = config.DeckOpacity.Value;
        Click(Plus(mapContent, "CONSOLE OPACITY"));
        Check(config.DeckOpacity.Value == before, "Disabled controls must reject clicks");
        config.ExpandedMapUi.Value = true;
        Settle(clientCon);
        for (int i = 0; i < 30; i++) Click(Plus(mapContent, "CONSOLE OPACITY"));
        Check(Mathf.Approximately(config.DeckOpacity.Value, 1f), "Stepper must stop at its upper limit");
        Click(FindControl(mapContent, "MATTE"));
        Check(!config.DeckGrid.Value && !config.CheckerboardOverlay.Value && !config.BackgroundImage.Value,
            "Selecting matte replaces all old layers");
        Settle(clientCon);
        Check(!IsShown(mapContent, "IMAGE FILE") && !HasText(mapContent, "CHECKER STRENGTH"),
            "Matte hides image controls and retired checker controls stay removed");
        Click(FindControl(mapContent, "CUSTOM"));
        Settle(clientCon);
        Check(config.BackgroundImage.Value && config.BackgroundImagePreset.Value == 3 && !config.DeckGrid.Value,
            "Custom image choice is mutually exclusive");
        Check(IsShown(mapContent, "IMAGE FILE") && IsShown(mapContent, "IMAGE FIT") && IsShown(mapContent, "IMAGE STRENGTH") &&
              FindControl(mapContent, "RESCAN LOCAL FILES").gameObject.activeInHierarchy,
            "CUSTOM reveals the image rows and the rescan button");
        Render(camera, canvas, height, "client-map-custom");
        var reloaded = new CommandSettings(new ConfigFile(config.ExpandedMapUi.ConfigFile.ConfigFilePath, false));
        Check(reloaded.BackgroundImagePreset.Value == 3 && reloaded.BackgroundImage.Value && !reloaded.DeckGrid.Value,
            "Settings survive reloading the saved configuration");
        Click(FindRowByName(mapContent, "NEWS TICKER"));

        clientCon.SetPage(CDisplay);
        Settle(clientCon);
        for (int i = 0; i < 20; i++) Click(Plus(displayContent, "GLASS REFLECTION"));
        Check(Mathf.Approximately(config.DisplayGlass.Value, 1f), "Glass stepper must clamp at full strength");
        Click(Plus(displayContent, "SCAN TEXTURE"));
        Click(FindControl(displayContent, "GREEN"));
        Check(config.DisplayScanlines.Value > 0f && config.DisplayTint.Value == 1,
            "CRT and tint controls must write their saved entries");
        var saved = new CommandSettings(new ConfigFile(config.ExpandedMapUi.ConfigFile.ConfigFilePath, false));
        Check(saved.DisplayScanlines.Value == config.DisplayScanlines.Value && saved.DisplayTint.Value == 1,
            "Display effects survive config reload");
        Click(FindControl(displayContent, "RESET DISPLAY FILTER"));
        Check(config.DisplayScanlines.Value == 0f && config.DisplayTint.Value == 0 &&
            Mathf.Approximately(config.DisplayGlass.Value, .35f), "Reset restores the current default filter");
        Check(config.AvionicsTheme.Value == AvThemeId.Portal, "Theme defaults to Portal");
        Check(FindControl(displayContent, "ACE") == null && FindControl(displayContent, "STEEL") == null,
            "Retired theme switching stays absent from the display page");
        Click(FindCellState(displayContent, "REDUCED MOTION"));
        Check(config.AvionicsReducedMotion.Value, "REDUCED MOTION cell must write AvionicsReducedMotion");

        clientCon.SetPage(CCockpit);
        Settle(clientCon);
        Check(!Array.Exists(cockpitContent.GetComponentsInChildren<TMP_Text>(true), t => t.text == "HUD ELEMENT" || t.text == "TARGET CAMERA"),
            "The HUD ships unconfigurable: no HUD rows on the cockpit page");
        Click(FindRowByName(cockpitContent, "RADIAL PRESETS"));
        Check(!config.TargetPresetWheel.Value, "RADIAL PRESETS must write its saved entry");

        clientCon.SetPage(CPerf);
        Settle(clientCon);
        Check(Array.Exists(perfContent.GetComponentsInChildren<TMP_Text>(true), t => t.text == "NO RESTART"),
            "Performance rows must state their restart requirement");
        Click(FindCellState(perfContent, "ADAPTIVE FX"));
        Check(performance.Value, "Performance toggle must update its owning config entry");
        Click(FindCellState(perfContent, "RAIN FX MASTER"));
        Check(!rain.Value, "Weather performance toggle must update its owning config entry");
        Click(FindCellState(perfContent, "CANOPY DROPLETS"));
        Click(FindCellState(perfContent, "TERRAIN WET PASS"));
        Check(!canopy.Value && !terrain.Value, "Canopy and terrain switches must update their owning entries");
        Check(config.AvionicsFxTier.Value == AvFxTier.Full, "FX tier defaults to Full");
        Click(FindControl(perfContent, "OFF"));
        Check(config.AvionicsFxTier.Value == AvFxTier.Off, "FX TIER segmented control must write AvionicsFxTier");
        Click(FindCellState(perfContent, "BLUR BEHIND"));
        Check(config.AvionicsBlurBehind.Value, "BLUR BEHIND cell must write AvionicsBlurBehind");

        Check(HasText(clientCon.Page(CImmersion).Content, "IMMERSION MODULE NOT INSTALLED"),
            "An absent optional immersion service reads unavailable");
        // Measure the switch itself after temporary glass previews and fixture population.
        int objects = canvas.GetComponentsInChildren<Transform>(true).Length;
        for (int i = 0; i < 20; i++)
        {
            ShowMode(panel, clientCon, serverCon, i % 3 == 0);
            AvConsole current = i % 3 == 0 ? serverCon : clientCon;
            current.SetPage(i % current.PageCount);
        }
        Check(objects == canvas.GetComponentsInChildren<Transform>(true).Length, "Page and mode changes must reuse the same tree");
        if (height == 420) Check(canvas.GetComponentsInChildren<ScrollRect>(true).Length > 0, "Every page keeps its scroll viewport");
        ModuleServices.Services.Remove(typeof(ISecondaryObjectivesView));
        Object.DestroyImmediate(canvas.gameObject);
        Object.DestroyImmediate(camera.gameObject);
    }

    /// <summary>Show one console through the panel's own switch (no click), then run its ticks as the game would.</summary>
    private static void ShowMode(SettingsMfdPanel panel, AvConsole clientCon, AvConsole serverCon, bool server)
    {
        Invoke(panel, "ApplyMode", server);
        Settle(server ? serverCon : clientCon);
    }

    /// <summary>Offline time does not advance: run the console's ticks now so parts re-measure and re-lay their page.</summary>
    private static void Settle(AvConsole con)
    {
        con.Ticker.TickNow();
        con.Ticker.TickNow();
    }

    /// <summary>The glyph currently drawn on the SERVER segment (its icon object keeps its build-time name).</summary>
    private static string ServerGlyph(AvConsole con) =>
        FindControl(con.Root, "SERVER").transform.Find("Icon " + AvIcon.Database).GetComponent<TMP_Text>().text;

    private static object Field(SettingsMfdPanel panel, string name) =>
        typeof(SettingsMfdPanel).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);

    private static void Invoke(SettingsMfdPanel panel, string method, params object[] args) =>
        typeof(SettingsMfdPanel).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(panel, args);

    /// <summary>The AvRow whose ON/OFF (or numeric) badge reads exactly this text, inside one page's content.</summary>
    private static Transform FindRow(RectTransform scope, string valueText)
    {
        var rows = FindRows(scope, valueText);
        return rows.Length > 0 ? rows[0] : null;
    }

    private static Transform[] FindRows(RectTransform scope, string valueText)
    {
        var found = new List<Transform>();
        foreach (TMP_Text t in scope.GetComponentsInChildren<TMP_Text>(true))
            if (t.gameObject.name == "Value" && t.text == valueText) found.Add(t.transform.parent);
        return found.ToArray();
    }

    /// <summary>The AvRow with exactly this name text.</summary>
    private static Transform FindRowByName(RectTransform scope, string name)
    {
        foreach (TMP_Text t in scope.GetComponentsInChildren<TMP_Text>(true))
            if ((t.gameObject.name == "Name" || t.gameObject.name == "Title") && t.text == name) return t.transform.parent;
        throw new Exception("No row named " + name);
    }

    private static bool IsShown(RectTransform scope, string name) => FindRowByName(scope, name).gameObject.activeInHierarchy;

    /// <summary>The trailing + control of the named stepper row.</summary>
    private static AvControl Plus(RectTransform scope, string name)
    {
        AvControl c = Array.Find(FindRowByName(scope, name).GetComponentsInChildren<AvControl>(true),
            x => x.transform.Find("Icon " + AvIcon.Plus) != null);
        if (c == null) throw new Exception("Row " + name + " has no + control");
        return c;
    }

    private static bool HasText(RectTransform scope, string text) =>
        Array.Exists(scope.GetComponentsInChildren<TMP_Text>(true), t => t.text == text);

    private static bool HasSubText(RectTransform scope, string part) =>
        Array.Exists(scope.GetComponentsInChildren<TMP_Text>(true), t => t.text.Contains(part));

    /// <summary>A standalone AvControl (a plain button, or one option of an AvSegmented) by its visible label.</summary>
    private static AvControl FindControl(RectTransform scope, string label) =>
        Array.Find(scope.GetComponentsInChildren<AvControl>(true), c => c.Label == label);

    /// <summary>An AvCell's own click surface (its Frame, found by the cell's title text), by title.</summary>
    private static Transform FindCellState(RectTransform scope, string title)
    {
        foreach (TMP_Text t in scope.GetComponentsInChildren<TMP_Text>(true))
            if (t.gameObject.name == "Title" && t.text == title) return t.transform.parent;
        return null;
    }

    private static void Click(Transform target) =>
        target.GetComponentInChildren<AvHit>(true).OnPointerClick(new PointerEventData(EventSystem.current)
        { button = PointerEventData.InputButton.Left });

    private static void Click(AvControl control) => Click(control.transform);

    private static void Render(Camera camera, Canvas canvas, int height, string tag)
    {
        // executeMethod freezes time; capture the settled page after its transition cover/reveals.
        foreach (Image fill in canvas.GetComponentsInChildren<Image>(true))
            if (fill.name == "ScanCover") fill.enabled = false;
        foreach (AvReveal reveal in canvas.GetComponentsInChildren<AvReveal>(true)) reveal.Finish();
        Canvas.ForceUpdateCanvases();
        foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
        Gate(canvas, height + "/" + tag);
        CapturePng(camera, 480, height, "SET-" + height + "-" + tag + ".png");
    }

    /// <summary>
    /// The gallery gate's checks (text overflowing its rect, entering the scroll gutter, below the 11 px floor,
    /// tabs and sections without an icon) plus one for this console: no two visible texts may overlap.
    /// </summary>
    private static void Gate(Canvas canvas, string where)
    {
        foreach (RectTransform cell in canvas.GetComponentsInChildren<RectTransform>(false))
        {
            if (!cell.name.StartsWith("Cell ") || cell.parent == null || cell.parent.name != "RingRow") continue;
            Check(cell.rect.width >= 160f, where + ": level controls must retain room for readable values and step keys.");
        }
        float gutterLeft = AvTokens.PanelWidth - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;
        var rects = new List<KeyValuePair<TMP_Text, Rect>>();
        // Hidden pages keep their GameObjects active; only their canvas is off.
        foreach (TMP_Text t in canvas.GetComponentsInChildren<TMP_Text>(false))
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
                if (t.fontSize < AvTypeScale.Floor - 0.01f)
                    Failures.Add(where + ": below the 11 px floor (" + t.fontSize.ToString("0.0") + ") '" + t.text + "'");
                Rect ink = InkRect(t, canvas);
                ScrollRect scroll = t.GetComponentInParent<ScrollRect>();
                if (scroll != null && scroll.viewport != null)
                {
                    // Rows scrolled out of view are clipped by the viewport; they cannot overlap the footer.
                    var vc = new Vector3[4];
                    scroll.viewport.GetWorldCorners(vc);
                    Vector3 lo = canvas.transform.InverseTransformPoint(vc[0]), hi = canvas.transform.InverseTransformPoint(vc[2]);
                    float x0 = Mathf.Max(ink.xMin, lo.x), x1 = Mathf.Min(ink.xMax, hi.x);
                    float y0 = Mathf.Max(ink.yMin, lo.y), y1 = Mathf.Min(ink.yMax, hi.y);
                    if (x1 <= x0 || y1 <= y0) continue;
                    ink = Rect.MinMaxRect(x0, y0, x1, y1);
                }
                rects.Add(new KeyValuePair<TMP_Text, Rect>(t, ink));
            }
            if (t.GetComponentInParent<ScrollRect>() != null)
            {
                var corners = new Vector3[4];
                t.rectTransform.GetWorldCorners(corners);
                float right = canvas.transform.InverseTransformPoint(corners[2]).x + AvTokens.PanelWidth * 0.5f;
                if (right > gutterLeft) Failures.Add(where + ": enters the gutter (" + right.ToString("0") + ") '" + t.text + "'");
            }
        }
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                Rect a = rects[i].Value, c = rects[j].Value;
                float ox = Mathf.Min(a.xMax, c.xMax) - Mathf.Max(a.xMin, c.xMin);
                float oy = Mathf.Min(a.yMax, c.yMax) - Mathf.Max(a.yMin, c.yMin);
                if (ox > 1.5f && oy > 3f)
                    Failures.Add(where + ": text overlaps '" + rects[i].Key.text + "' / '" + rects[j].Key.text + "'");
            }
        foreach (AvControl tab in canvas.GetComponentsInChildren<AvControl>(false))
            if (tab.transform.parent != null && tab.transform.parent.name == "Tabs" && tab.transform.Find("Label") != null
                && tab.GetComponentsInChildren<TMP_Text>(true).Length < 2)
                Failures.Add(where + ": tab without icon " + tab.name);
        foreach (Transform s in canvas.GetComponentsInChildren<Transform>(false))
            if (s.name.StartsWith("Section ") && s.Find("Icon None") != null)
                Failures.Add(where + ": section without icon " + s.name);
    }

    /// <summary>Where the text really drew, in canvas space, clipped to its rect.</summary>
    private static Rect InkRect(TMP_Text t, Canvas canvas)
    {
        Bounds b = t.textBounds;
        Vector3 min = canvas.transform.InverseTransformPoint(t.transform.TransformPoint(b.min));
        Vector3 max = canvas.transform.InverseTransformPoint(t.transform.TransformPoint(b.max));
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    private sealed class HostFixture : IHostSettingsView
    {
        private readonly HostSettingView[] rows;
        private readonly bool[] on;
        private readonly string[] text;
        public int Toggles, Steps;

        /// <summary>Row specs: "T:LABEL:1" is a toggle (1 = on), "S:LABEL:1.00x" a stepper with that value text.</summary>
        public HostFixture(string section, HostSettingsPage page, params string[] spec)
        {
            Section = section;
            Page = page;
            rows = new HostSettingView[spec.Length];
            on = new bool[spec.Length];
            text = new string[spec.Length];
            for (int i = 0; i < spec.Length; i++)
            {
                string[] p = spec[i].Split(':');
                bool toggle = p[0] == "T";
                rows[i] = new HostSettingView(i + 1, toggle ? HostSettingKind.Toggle : HostSettingKind.Stepper, p[1],
                    "Live host setting: " + p[1].ToLowerInvariant() + ". Applies to everyone on this server.");
                on[i] = toggle && p[2] == "1";
                text[i] = p[2];
            }
            Refresh();
        }

        public string Section { get; }
        public HostSettingsPage Page { get; }
        public IReadOnlyList<HostSettingView> Rows => rows;

        public void Refresh()
        {
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].Value = on[i];
                rows[i].ValueText = rows[i].Kind == HostSettingKind.Toggle ? (on[i] ? "ON" : "OFF") : text[i];
                rows[i].CanDecrease = true;
                rows[i].CanIncrease = true;
            }
        }

        public void Toggle(int id) { on[id - 1] = !on[id - 1]; Toggles++; Refresh(); }
        public void Step(int id, int direction) { Steps++; }
    }

    private sealed class TaskingFixture : ISecondaryObjectivesView
    {
        public bool IsFresh => true;
        public float SnapshotAgeSeconds => 0f;
        public int SelectedForHud { get; private set; }
        public bool IsActionPending => false;
        public int PendingObjectiveId => 0;
        public string ActionResult => "";
        public void SelectForHud(int id) => SelectedForHud = id;
        private readonly SecondaryObjectiveView[] cards =
        {
            new SecondaryObjectiveView(1, "RELAY STRIKE", "Destroy the relay.", "RADAR RELAY NORTH", "IN PROGRESS", "$14,000 · 300 XP",
                .45f, 420f, 14000, 300, false, false, true, true, 100f, 100f, 400f, "MARCI"),
            new SecondaryObjectiveView(2, "CONVOY INTERDICTION", "Stop the convoy.", "ROUTE 7", "OFFERED", "$9,500 · 200 XP",
                0f, 900f, 9500, 200, false, true, false, true, 300f, 200f, 300f),
            new SecondaryObjectiveView(3, "AIRFIELD DENIAL", "Crater the runway.", "PORT AIRFIELD", "COMPLETE", "$20,000 · 450 XP",
                1f, 0f, 20000, 450, true),
        };

        public IReadOnlyList<SecondaryObjectiveView> Objectives => cards;
        public string Status => "3 CONTRACTS · HOST BOARD";
        public int ActiveLimit => 2;
        public void Refresh() { }
        public void RequestAccept(int id) { }
        public void RequestCancel(int id) { }
    }

    private sealed class HudFixture : IHudBoard
    {
        public void DeclareChannel(string key, string label) { }
        public IHudLine Acquire(string owner, string channel, string key) => null;
        public void Notice(string channel, HudTone tone, string text, string detail = null) { }
    }
}
#endif

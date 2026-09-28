#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class SettingsUnityCheck
{
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
            arguments[0] = Path.GetFullPath("SettingsCheck.exe");
            for (int i = 1; i < arguments.Length; i++) arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            setPaths.Invoke(null, arguments);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            new GameObject("Events", typeof(EventSystem));
            CheckMfdLookup();
            CheckLayoutCanvas();
            CheckScreenSpaceSizing();
            foreach (int height in new[] { 596, 420 }) CheckPanel(height);
            File.WriteAllText("result.txt", "PASS: SET (kit v2) renders its nine MAP/DISPLAY/BACKDROP/CAMERA/HUD/PERF/TASKING/HOST/EFFECTS " +
                "pages at 596 and 420 units; toggles, background replacement, disabled dependencies, +/- bounds and the flat page tree " +
                "(no per-page GameObject churn) are checked. Game adapters are stubbed; in-game acceptance remains required.");
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

    // Kit v2 page indices, matching SettingsMfdPanel's own PageMap..PageEffects constants (private there).
    private const int PMap = 0, PDisplay = 1, PBackdrop = 2, PCamera = 3, PHud = 4, PPerf = 5, PTasking = 6, PHostSettings = 7, PEffects = 8;

    private static void CheckPanel(int height)
    {
        var hud = new HudFixture();
        ModServices.Services[typeof(IHudBoard)] = hud;
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
        hostBoard.Add(new HostFixture());
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

        // Install() needs a real bezel claim (stubbed to always fail offline), so the console is built
        // directly here the same way Install() builds it, then wired into the panel's private field --
        // mirroring how the retired v1 harness hand-built a shell and injected it as the private "shell" field.
        AvConsole con = AvConsole.Build((RectTransform)canvas.transform, "SET", "TACTICAL DISPLAY", 9, 480, height);
        con.Chips(2)[0].Set("SAVED", AvState.Ready);
        typeof(SettingsMfdPanel).GetField("con", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(panel, con);

        Invoke(panel, "BuildMapPage", con.Page(PMap), PMap);
        Invoke(panel, "BuildDisplayPage", con.Page(PDisplay), PDisplay);
        Invoke(panel, "BuildBackdropPage", con.Page(PBackdrop), PBackdrop);
        Invoke(panel, "BuildCameraPage", con.Page(PCamera), PCamera);
        Invoke(panel, "BuildHudPage", con.Page(PHud), PHud);
        Invoke(panel, "BuildPerformancePage", con.Page(PPerf), PPerf);
        Invoke(panel, "BuildTaskingPage", con.Page(PTasking), PTasking);
        Invoke(panel, "BuildHostSettingsPage", con.Page(PHostSettings), PHostSettings, HostSettingsPage.Settings, "HOST SETTINGS");
        Invoke(panel, "BuildHostSettingsPage", con.Page(PEffects), PEffects, HostSettingsPage.Effects, "EFFECTS");
        con.Finish();

        int objects = canvas.GetComponentsInChildren<Transform>(true).Length;

        for (int page = 0; page < 6; page++)
        {
            con.SetPage(page);
            Render(camera, canvas, height, page);
            if (page == PDisplay && height == 596)
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
                    Render(camera, canvas, height, 10 + color);
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

        con.SetPage(PTasking);
        Check(Array.Exists(canvas.GetComponentsInChildren<TMP_Text>(true), t => t.text == "FACTION TASKING"),
            "SERVER tasking must have its own populated page");
        Render(camera, canvas, height, PTasking);
        con.SetPage(PEffects);
        Check(Array.Exists(canvas.GetComponentsInChildren<TMP_Text>(true), t => t.text == "MAX FIRE SITES"),
            "SERVER effects (fire, weather) must have a dedicated populated page");
        Render(camera, canvas, height, PEffects);

        // Kit v2 hides an inactive page's canvas rather than deactivating its GameObjects (spec section 8:
        // "hidden pages get Canvas.enabled = false", not a SetActive churn), so every row search below is
        // scoped to the page's own content -- otherwise every page's rows would be found at once.
        RectTransform mapContent = con.Page(PMap).Content;
        RectTransform displayContent = con.Page(PDisplay).Content;
        RectTransform cameraContent = con.Page(PCamera).Content;
        RectTransform hudContent = con.Page(PHud).Content;
        RectTransform perfContent = con.Page(PPerf).Content;

        con.SetPage(PMap);
        Click(FindRow(mapContent, "ON"));
        Check(!config.ExpandedMapUi.Value, "Expanded toggle must change persisted config");

        con.SetPage(PDisplay);
        var plus = FindByIcon(displayContent, AvIcon.Plus);
        for (int i = 0; i < 20; i++) Click(plus[0]);
        Check(Mathf.Approximately(config.DisplayGlass.Value, 1f), "Glass stepper must clamp at full strength");
        Click(plus[1]);
        Click(plus[3]);
        Check(config.DisplayScanlines.Value > 0f && config.DisplayTint.Value == 1,
            "CRT and tint controls must write their saved entries");
        var saved = new CommandSettings(new ConfigFile(config.ExpandedMapUi.ConfigFile.ConfigFilePath, false));
        Check(saved.DisplayScanlines.Value == config.DisplayScanlines.Value && saved.DisplayTint.Value == 1,
            "Display effects survive config reload");
        Click(FindControl(displayContent, "RESET DISPLAY FILTER"));
        Check(config.DisplayScanlines.Value == 0f && config.DisplayTint.Value == 0 &&
            Mathf.Approximately(config.DisplayGlass.Value, .6f), "Reset restores the default filter");
        var disabled = plus[5];
        float before = config.DeckOpacity.Value;
        Click(disabled);
        Check(config.DeckOpacity.Value == before, "Disabled controls must reject clicks");
        config.ExpandedMapUi.Value = true;
        for (int i = 0; i < 30; i++) Click(plus[5]);
        Check(Mathf.Approximately(config.DeckOpacity.Value, 1f), "Stepper must stop at its upper limit");
        Click(plus[6]);
        Check(!config.DeckGrid.Value && !config.CheckerboardOverlay.Value && !config.BackgroundImage.Value,
            "Selecting plain replaces all old layers");
        for (int i = 0; i < 6; i++) Click(plus[6]);
        Check(config.BackgroundImage.Value && config.BackgroundImagePreset.Value == 3 && !config.DeckGrid.Value,
            "Custom image choice is mutually exclusive");
        var reloaded = new CommandSettings(new ConfigFile(config.ExpandedMapUi.ConfigFile.ConfigFilePath, false));
        Check(reloaded.BackgroundImagePreset.Value == 3 && reloaded.BackgroundImage.Value && !reloaded.DeckGrid.Value,
            "Settings survive reloading the saved configuration");
        // The four new Avionics.* rows (spec section 11) live on DISPLAY and PERF; a segmented choice and a
        // toggle cell, both built straight from kit v2 primitives rather than the row helper above.
        Check(config.AvionicsTheme.Value == AvThemeId.Steel, "Theme defaults to Steel");
        var themeAce = FindControl(displayContent, "ACE");
        Click(themeAce);
        Check(config.AvionicsTheme.Value == AvThemeId.Ace, "THEME segmented control must write AvionicsTheme");
        var reducedMotion = FindCellState(displayContent, "REDUCED MOTION");
        Click(reducedMotion);
        Check(config.AvionicsReducedMotion.Value, "REDUCED MOTION cell must write AvionicsReducedMotion");

        con.SetPage(PHud);
        Click(FindRow(hudContent, "ON"));
        Check(!hud.Enabled, "HUD switch must write through its public settings seam");
        Click(FindControl(hudContent, "RESET STATUS LAYOUT"));
        Check(hud.Enabled && hud.Resets == 1, "HUD reset must remain usable while the overlay is disabled");

        con.SetPage(PCamera);
        Click(FindRow(cameraContent, "ON"));
        Check(!hud.CameraFeedEnabled, "TARGET CAMERA must write through the HUD board seam");
        Click(FindRow(cameraContent, "ON"));
        Check(!config.TargetPresetWheel.Value, "RADIAL PRESETS must write its saved entry");

        con.SetPage(PPerf);
        Check(Array.Exists(perfContent.GetComponentsInChildren<TMP_Text>(true), t => t.text == "NO RESTART"),
            "Performance rows must state their restart requirement");
        Click(FindRow(perfContent, "OFF"));
        Check(performance.Value, "Performance toggle must update its owning config entry");
        var onRows = FindRows(perfContent, "ON");
        Check(onRows.Length == 4, "Performance page must expose four live switches");
        Click(onRows[1]);
        Check(!rain.Value, "Weather performance toggle must update its owning config entry");
        Click(onRows[2]);
        Click(onRows[3]);
        Check(!canopy.Value && !terrain.Value, "Canopy and terrain switches must update their owning entries");
        Check(config.AvionicsFxTier.Value == AvFxTier.Full, "FX tier defaults to Full");
        Click(FindControl(perfContent, "OFF"));
        Check(config.AvionicsFxTier.Value == AvFxTier.Off, "FX TIER segmented control must write AvionicsFxTier");
        Click(FindCellState(perfContent, "BLUR BEHIND"));
        Check(config.AvionicsBlurBehind.Value, "BLUR BEHIND cell must write AvionicsBlurBehind");

        for (int i = 0; i < 20; i++) con.SetPage(i % 9);
        Check(objects == canvas.GetComponentsInChildren<Transform>(true).Length, "Page changes must reuse the same tree");
        if (height == 420) Check(canvas.GetComponentsInChildren<ScrollRect>(true).Length > 0, "Every page keeps its scroll viewport");
        Object.DestroyImmediate(canvas.gameObject);
        Object.DestroyImmediate(camera.gameObject);
    }

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

    /// <summary>A standalone AvControl (a plain button, or one option of an AvSegmented) by its visible label.</summary>
    private static AvControl FindControl(RectTransform scope, string label) =>
        Array.Find(scope.GetComponentsInChildren<AvControl>(true), c => c.Label == label);

    /// <summary>The trailing +/- (or similarly iconed) AvControls across a page, in build order.</summary>
    private static AvControl[] FindByIcon(RectTransform scope, AvIcon icon) =>
        Array.FindAll(scope.GetComponentsInChildren<AvControl>(true), c => c.transform.Find("Icon " + icon) != null);

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

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static void Render(Camera camera, Canvas canvas, int height, int page)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
        var target = new RenderTexture(480, height, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(480, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 480, height), 0, 0);
        image.Apply();
        File.WriteAllBytes("SET-" + height + "-" + page + ".png", image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(image);
    }

    private sealed class HostFixture : IHostSettingsView
    {
        private readonly HostSettingView[] rows =
        {
            new HostSettingView(1, HostSettingKind.Stepper, "MAX FIRE SITES", "New ignitions only.")
        };

        public string Section => "FIRE AND DESTRUCTION";
        public HostSettingsPage Page => HostSettingsPage.Effects;
        public System.Collections.Generic.IReadOnlyList<HostSettingView> Rows => rows;
        public void Refresh()
        {
            rows[0].ValueText = "24";
            rows[0].CanDecrease = true;
            rows[0].CanIncrease = true;
        }
        public void Toggle(int id) { }
        public void Step(int id, int direction) { }
    }

    private sealed class HudFixture : IHudBoard
    {
        public int Resets;
        public bool Enabled { get; set; } = true;
        public int ScaleStep { get; set; } = 1;
        public int OpacityStep { get; set; }
        public int MaxRows { get; set; } = 4;
        public bool NoticesEnabled { get; set; } = true;
        public float NoticeSeconds { get; set; } = 8;
        public int Contrast { get; set; } = 1;
        public bool ShowDetails { get; set; } = true;
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public System.Collections.Generic.IReadOnlyList<IHudChannel> Channels => Array.Empty<IHudChannel>();
        public void DeclareChannel(string key, string label) { }
        public IHudLine Acquire(string owner, string channel, string key) => null;
        public void Notice(string channel, HudTone tone, string text, string detail = null) { }
        public bool CameraFeedEnabled { get; set; } = true;
        public void ResetLayout() { Enabled = true; Resets++; }
    }
}
#endif

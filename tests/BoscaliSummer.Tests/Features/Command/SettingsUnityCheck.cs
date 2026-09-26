#if UNITY_EDITOR
using System;
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
            AvFont.Font = TMP_FontAsset.CreateFontAsset(new Font("C:/Windows/Fonts/consola.ttf"));
            new GameObject("Events", typeof(EventSystem));
            AvionicsUnityCheck.Check();
            CheckMfdLookup();
            CheckLayoutCanvas();
            CheckScreenSpaceSizing();
            foreach (int height in new[] { 596, 420 }) CheckPanel(height);
            File.WriteAllText("result.txt", "PASS: layout resolves the real UI area past stale canvas rects; SET renders CLIENT MAP/STYLE/IMAGE/COCKPIT/HUD/VISUALS (with COCKPIT FEEL) and SERVER at 596 and 420 units; toggles, background replacement, disabled dependencies, +/- bounds, scrolling and cached page trees checked. Game adapters are stubbed; in-game acceptance remains required.");
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

    private static void CheckPanel(int height)
    {
        var hud = new HudFixture();
        var external = new ExternalFixture();
        ModServices.Services[typeof(IHudBoard)] = hud;
        ModServices.Services[typeof(IThirdPersonHud)] = external;
        var visuals = new VisualsFixture();
        ModServices.Services[typeof(IVisualEnhancements)] = visuals;
        ModServices.Services[typeof(IImmersionSettings)] = new ImmersionFixture();
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
        panel.Configure(config, null);
        var shell = AvScreen.Build((RectTransform)canvas.transform, "SET", new[] { "CLIENT", "SERVER" }, null, 2, 480, height, null);
        shell.DataBar.SetChip(0, "SAVED", true);
        shell.DataBar.State.text = "TACTICAL DISPLAY";
        typeof(SettingsMfdPanel).GetField("shell", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(panel, shell);
        Invoke(panel, "BuildClientArea", (RectTransform)shell.CreatePage(0, "ClientPage").transform, shell.Body);
        Invoke(panel, "BuildServerPage", (RectTransform)shell.CreatePage(1, "ServerPage").transform, shell.Body);
        int objects = canvas.GetComponentsInChildren<Transform>(true).Length;
        shell.SetPage(0);
        for (int page = 0; page < 6; page++)
        {
            Invoke(panel, "SetClientPage", page);
            Refresh(panel);
            shell.WriteStatus(null, null, "Saved automatically. Hover a control for help.");
            // 5 is the SERVER render's file name; VISUALS goes to 20.
            Render(camera, canvas, height, page == 5 ? 20 : page);
            if (page == 5)
            {
                Check(Array.Exists(canvas.GetComponentsInChildren<TMP_Text>(), t => t.text == "TREE & GRASS SWAY")
                    && Array.Exists(canvas.GetComponentsInChildren<TMP_Text>(), t => t.text == "SHARPEN STRENGTH")
                    && Array.Exists(canvas.GetComponentsInChildren<TMP_Text>(), t => t.text == "SUN GLARE"),
                    "VISUALS page must list its rows");
                Click(Array.Find(canvas.GetComponentsInChildren<AvButton>(),
                    b => b.GetComponentInChildren<TMP_Text>().text == "-" && b.gameObject.activeInHierarchy));
                Check(visuals.BloomBoost < 1.35f, "BLOOM BOOST stepper must write through the visuals seam");
            }
            if (page == 1 && height == 596)
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
                    Refresh(panel);
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
        shell.SetPage(1);
        shell.DataBar.State.text = "SERVER SETTINGS";
        Refresh(panel);
        shell.WriteStatus(null, null, "Host only. These settings are read-only on a remote client.");
        Render(camera, canvas, height, 5);
        shell.SetPage(0);
        Invoke(panel, "SetClientPage", 0);
        Refresh(panel);
        Click(Find(canvas, "ON"));
        Check(!config.ExpandedMapUi.Value, "Expanded toggle must change persisted config");
        Invoke(panel, "SetClientPage", 1);
        Refresh(panel);
        var plus = Array.FindAll(canvas.GetComponentsInChildren<AvButton>(), b => b.GetComponentInChildren<TMP_Text>().text == "+");
        for (int i = 0; i < 20; i++) Click(plus[0]);
        Check(Mathf.Approximately(config.DisplayGlass.Value, 1f), "Glass stepper must clamp at full strength");
        Click(plus[1]);
        Click(plus[3]);
        Check(config.DisplayScanlines.Value > 0f && config.DisplayTint.Value == 1,
            "CRT and tint controls must write their saved entries");
        var saved = new CommandSettings(new ConfigFile(config.ExpandedMapUi.ConfigFile.ConfigFilePath, false));
        Check(saved.DisplayScanlines.Value == config.DisplayScanlines.Value && saved.DisplayTint.Value == 1,
            "Display effects survive config reload");
        Click(Find(canvas, "RESET DISPLAY FILTER"));
        Check(config.DisplayScanlines.Value == 0f && config.DisplayTint.Value == 0 &&
            Mathf.Approximately(config.DisplayGlass.Value, .6f), "Reset restores the default filter");
        var disabled = plus[5];
        float before = config.DeckOpacity.Value;
        Click(disabled);
        Check(config.DeckOpacity.Value == before, "Disabled controls must reject clicks");
        config.ExpandedMapUi.Value = true;
        Refresh(panel);
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
        Invoke(panel, "SetClientPage", 4); Refresh(panel);
        Click(Find(canvas, "ON"));
        Check(!hud.Enabled, "HUD switch must write through its public settings seam");
        Click(Find(canvas, "RESET STATUS LAYOUT"));
        Check(hud.Enabled && hud.Resets == 1, "HUD reset must remain usable while the overlay is disabled");
        Invoke(panel, "SetClientPage", 3); Refresh(panel);
        Click(Find(canvas, "ON"));
        Check(!external.IsEnabled, "External HUD switch must write through the HUD module seam");
        Click(Find(canvas, "RESET INSTRUMENT LAYOUT"));
        Check(external.Resets == 1, "Instrument reset must be wired on the scrollable cockpit page");
        for (int i = 0; i < 20; i++)
        {
            shell.SetPage(i % 2);
            Invoke(panel, "SetClientPage", i % 5);
            Refresh(panel);
        }
        Check(objects == canvas.GetComponentsInChildren<Transform>(true).Length, "Tab changes must reuse the same tree");
        if (height == 420) Check(canvas.GetComponentsInChildren<ScrollRect>(true).Length > 0, "Short panels must scroll");
        Object.DestroyImmediate(canvas.gameObject);
        Object.DestroyImmediate(camera.gameObject);
    }

    private static void Invoke(SettingsMfdPanel panel, string method, params object[] args) =>
        typeof(SettingsMfdPanel).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(panel, args);

    private static void Refresh(SettingsMfdPanel panel) => typeof(SettingsMfdPanel)
        .GetMethod("RefreshPanel", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(panel, null);
    private static AvButton Find(Canvas canvas, string label) => Array.Find(canvas.GetComponentsInChildren<AvButton>(),
        b => b.GetComponentInChildren<TMP_Text>().text == label);
    private static void Click(AvButton button) => button.OnPointerClick(new PointerEventData(EventSystem.current)
        { button = PointerEventData.InputButton.Left });
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
    private sealed class VisualsFixture : IVisualEnhancements
    {
        public bool IsEnabled => true;
        public bool CinematicPostFxEnabled { get; set; } = true;
        public float BloomBoost { get; set; } = 1.35f;
        public bool SharpenEnabled { get; set; } = true;
        public float SharpenStrength { get; set; } = 0.5f;
        public bool GForceEffectsEnabled { get; set; } = true;
        public bool FoliageDynamicsEnabled { get; set; } = true;
        public float FoliageSwayStrength { get; set; } = 1f;
    }

    private sealed class ImmersionFixture : IImmersionSettings
    {
        public bool IsEnabled => true;
        public bool HeadMotionEnabled { get; set; } = true;
        public float HeadMotionStrength { get; set; } = 1f;
        public bool ExtraShakeEnabled { get; set; } = true;
        public float ShakeStrength { get; set; } = 1f;
        public bool SunGlareEnabled { get; set; } = true;
    }

    private sealed class HudFixture : IHudBoard
    {
        public int Resets;
        public bool Enabled { get; set; } = true;
        public HudAnchor Anchor { get; set; }
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
        public void ResetLayout() { Enabled = true; Resets++; }
    }
    private sealed class ExternalFixture : IThirdPersonHud
    {
        public int Resets;
        public bool IsEnabled { get; private set; } = true;
        public bool ModifyVanillaHud { get; set; }
        public HudBounds InstrumentBounds => default;
        public void Toggle() => IsEnabled = !IsEnabled;
        public bool HidePitchLadder { get; set; } = true;
        public bool CameraFeedEnabled { get; set; } = true;
        public bool FlightCameraEnabled { get; set; } = true;
        public bool BoardEnabled { get; set; } = true;
        public bool AirframeEnabled { get; set; } = true;
        public bool ShotsEnabled { get; set; } = true;
        public bool MarkEnabled { get; set; } = true;
        public int BoardCorner { get; set; }
        public int FlightScaleStep { get; set; } = 1;
        public int FlightOpacityStep { get; set; }
        public int FlightContrast { get; set; } = 1;
        public int BoardScaleStep { get; set; } = 1;
        public int BoardOpacityStep { get; set; }
        public int BoardContrast { get; set; } = 1;
        public int BoardInsetX { get; set; }
        public int BoardInsetY { get; set; }
        public void ResetLayout() { Resets++; }
    }
}
#endif

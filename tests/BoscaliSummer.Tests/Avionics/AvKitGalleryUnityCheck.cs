#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Kit v2 visual gate (spec §10): renders specimen consoles for every theme × resolution × bundle state ×
/// FX tier to PNG and fails on text that overflows its rect, text that enters the scroll gutter, text below
/// 4.5:1 against its real fill, and tabs or section headers without an icon. P2 console slices append
/// their builders to <see cref="Specimens"/>.
/// </summary>
public static class AvKitGalleryUnityCheck
{
    public static readonly List<KeyValuePair<string, Func<RectTransform, AvConsole>>> Specimens =
        new List<KeyValuePair<string, Func<RectTransform, AvConsole>>>
        {
            new KeyValuePair<string, Func<RectTransform, AvConsole>>("specimen", BuildSpecimen),
        };

    private static readonly List<string> Failures = new List<string>();
    private static int checkedTexts;

    public static void Run()
    {
        if (Shader.Find("TextMeshPro/Distance Field") == null)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            UnityEditor.AssetDatabase.importPackageCompleted += _ => UnityEditor.EditorApplication.delayCall += Run;
            UnityEditor.AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
            return;
        }
        try
        {
            string outDir = Path.GetFullPath("gallery");
            Directory.CreateDirectory(outDir);
            byte[] bundle = File.Exists("avionics-ui.bundle") ? File.ReadAllBytes("avionics-ui.bundle") : null;
            // Offline there is no AvFxDriver updater: park _NOA_Now far in the future so every timed
            // effect (scan-in cover, dissolve) renders at its end state.
            Shader.SetGlobalFloat("_NOA_Now", 1e6f);
            foreach (bool withBundle in new[] { true, false })
            {
                AvBundle.ResetForTests();
                if (withBundle && bundle != null) AvBundle.LoadFromBytes(bundle, Debug.Log);
                foreach (AvFxTier tier in new[] { AvFxTier.Full, AvFxTier.Off })
                {
                    AvFxDriver.Configure(tier, false);
                    foreach (AvThemeId theme in (AvThemeId[])Enum.GetValues(typeof(AvThemeId)))
                    {
                        AvStyleHost.SetTheme(theme);
                        foreach (Vector2Int res in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) })
                            foreach (var spec in Specimens)
                                Render(spec.Key, spec.Value, theme, res, withBundle, tier, outDir);
                    }
                }
            }
            CheckLiveThemeSwitch();
        }
        catch (Exception e) { Failures.Add("exception: " + e); }
        File.WriteAllText("result.txt", Failures.Count == 0
            ? "OK: " + checkedTexts + " text checks"
            : "FAIL (" + Failures.Count + "):\n" + string.Join("\n", Failures.GetRange(0, Math.Min(60, Failures.Count))));
        UnityEditor.EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
    }

    public static AvConsole BuildSpecimen(RectTransform host)
    {
        AvConsole con = AvConsole.Build(host, "FAC", "BOSCALI / ECONOMY", 2);
        AvChip[] chips = con.Chips(3);
        chips[0].Set("SCORE " + AvNum.Fixed(166.9, 1), AvState.Ready);
        chips[1].Set(AvNum.Money(6.08e9), AvState.Info);
        chips[2].Set("COST UP", AvState.Caution);
        AvMetric[] m = con.Metrics("FUNDS", "WARHEADS", "MORALE");
        m[0].Set(AvNum.Money(6.08e9), "AVAILABLE", 0.62f);
        m[1].Set("20", "STOCKPILE", 0.4f);
        m[2].Set("50", "OF 100", 0.5f, AvState.Caution);
        con.Tabs((AvIcon.Coins, "ECONOMY"), (AvIcon.Shield, "FORCES"));
        AvFlow p = con.Page(0);
        p.Section(AvIcon.LayersSubtract, "GAME SYMBOLOGY", "6 NATIVE LAYERS · SECTOR LOGIC OVERRIDES ACTIVE");
        bool a = true, b = false, c = true;
        AvCellGrid g = p.Grid(2);
        g.Toggle("OBJECTIVES", "Mission markers", () => a, v => a = v);
        g.Toggle("CONTRACT INTELLIGENCE", "Complete a faction contract to unlock", () => b, v => b = v);
        g.Toggle("FRONT LINE", "Control boundary", () => c, v => c = v);
        p.Section(AvIcon.ListDetails, "CONTRACTS", "2 OPEN");
        var row = p.Add(new AvRow(p.Content, () => { }));
        row.Set("CONVOY ESCORT", "Reach Port Maris before 12:40", AvNum.Money(120000), AvState.Ready);
        var alert = p.Add(new AvAlert(p.Content));
        alert.Show(AvIcon.AlertTriangle, "GLOBAL SUPPLY CHAIN CRISIS", "Requisitions cost more until it ends.", AvState.Caution);
        p.Add(new AvStepper(p.Content, "UPDATE INTERVAL", () => AvNum.Seconds(0.5), () => { }, () => { }));
        p.Add(new AvSegmented(p.Content, "BAND", new[] { "FM", "AIR", "MW" }, () => 0, i => { }));
        p.Add(new AvSlider(p.Content, "VOLUME", () => 1f, v => { }, () => AvNum.Percent(1)));
        var chart = p.Add(new AvLineChart(p.Content));
        chart.SetSeries(new[] { 3f, 3.1f, 2.9f, 6.5f, 5.9f, 5.2f, 6.8f }, 7, "$2.86B", "$7.32B", "$6.81B");
        p.Row(new AvGauge(p.Content, "COVER", AvGaugeShape.Arc), new AvGauge(p.Content, "CHARGE", AvGaugeShape.Segments));
        p.Add(new AvField(p.Content, "Farp here, cap east…", 18, s => { }));
        p.Buttons(new AvControl.Spec("ACCEPT", () => { }, AvButtonStyle.Primary, AvIcon.CircleCheck),
                  new AvControl.Spec("DETAILS", () => { }),
                  new AvControl.Spec("DECLINE", () => { }, AvButtonStyle.Danger, AvIcon.X));
        var list = p.Add(new AvList(p.Content, con.Ticker, 3, (i, r) => r.Set("TRACK " + (i + 1), null, AvNum.Fixed(12.5 + i, 1) + " km", AvState.Info)));
        list.SetCount(7);
        AvFlow p2 = con.Page(1);
        p2.Section(AvIcon.Shield, "FORCES", "ALLIED / HOSTILE");
        p2.Add(new AvReadout(p2.Content)).Set("217", "GROUND UNITS", "Allied forces in theatre");
        con.Footer.Set("Morale scales new contract offers.");
        con.Finish();
        return con;
    }

    private static void Render(string name, Func<RectTransform, AvConsole> build, AvThemeId theme, Vector2Int res, bool bundle, AvFxTier tier, string outDir)
    {
        var camGo = new GameObject("cam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.10f, 0.12f);
        var rt = new RenderTexture(res.x, res.y, 24);
        cam.targetTexture = rt;
        var canvasGo = new GameObject("canvas", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var host = (RectTransform)new GameObject("host", typeof(RectTransform)).transform;
        host.SetParent(canvas.transform, false);
        host.anchorMin = host.anchorMax = host.pivot = new Vector2(0f, 1f);
        host.anchoredPosition = new Vector2(40f, -40f);
        host.sizeDelta = new Vector2(AvTokens.PanelWidth, AvTokens.PanelHeight);
        AvConsole con = build(host);
        string variant = theme + "-" + res.y + "p-" + (bundle ? "bundle" : "nobundle") + "-" + tier;
        for (int page = 0; page < con.PageCount; page++)
        {
            con.SetPage(page);
            con.Page(page).Relayout();
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(res.x, res.y, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, res.x, res.y), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, name + "-" + variant + "-p" + (page + 1) + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Gate(con, page, name + "/" + variant + "/p" + (page + 1));
        }
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(canvasGo);
        UnityEngine.Object.DestroyImmediate(camGo);
        rt.Release();
    }

    private static void Gate(AvConsole con, int page, string where)
    {
        float gutterLeft = AvTokens.PanelWidth - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        foreach (TMP_Text t in con.Root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t)) continue;
            checkedTexts++;
            t.ForceMeshUpdate();
            Rect r = t.rectTransform.rect;
            bool icon = t.name.StartsWith("Icon");
            if (!icon)
            {
                Vector2 pref = t.GetPreferredValues(t.text, r.width, 0f);
                if (!t.enableWordWrapping && pref.x > r.width + 1.5f)
                    Failures.Add(where + ": overflows width (" + pref.x.ToString("0") + " > " + r.width.ToString("0") + ") '" + t.text + "'");
                if (t.enableWordWrapping && pref.y > r.height + 1.5f)
                    Failures.Add(where + ": overflows height (" + pref.y.ToString("0") + " > " + r.height.ToString("0") + ") '" + t.text + "'");
            }
            if (t.GetComponentInParent<ScrollRect>() != null)
            {
                var corners = new Vector3[4];
                t.rectTransform.GetWorldCorners(corners);
                float right = con.Root.InverseTransformPoint(corners[2]).x;
                if (right > gutterLeft) Failures.Add(where + ": enters the gutter (" + right.ToString("0") + ") '" + t.text + "'");
            }
            if (!icon && t.color.a > 0.5f)
            {
                Color back = BackgroundOf(t, ground);
                float contrast = Rgba.Contrast(t.color.ToRgba().WithAlpha(1f).Over(back.ToRgba()), back.ToRgba());
                if (contrast < 4.5f) Failures.Add(where + ": contrast " + contrast.ToString("0.00") + " for '" + t.text + "' (" + t.name + ")");
            }
        }
        foreach (AvControl tab in con.Root.GetComponentsInChildren<AvControl>(true))
            if (tab.transform.parent != null && tab.transform.parent.name == "Tabs" && tab.transform.Find("Label") != null
                && tab.GetComponentsInChildren<TMP_Text>(true).Length < 2)
                Failures.Add(where + ": tab without icon " + tab.name);
        foreach (Transform s in con.Root.GetComponentsInChildren<Transform>(true))
            if (s.name.StartsWith("Section ") && s.Find("Icon None") != null)
                Failures.Add(where + ": section without icon " + s.name);
    }

    // Hidden pages keep their GameObjects active; only their canvas is off.
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

    private static void CheckLiveThemeSwitch()
    {
        // Review focus 1: a latched tab repaints when the theme changes while the console is open.
        AvBundle.ResetForTests();
        AvFxDriver.Configure(AvFxTier.Full, false);
        AvStyleHost.SetTheme(AvThemeId.Ace);
        var hostGo = new GameObject("theme-switch", typeof(RectTransform), typeof(Canvas));
        AvConsole con = BuildSpecimen((RectTransform)hostGo.transform);
        Image rail = con.Root.Find("Tabs").GetComponentInChildren<AvControl>().transform.Find("Rail").GetComponent<Image>();
        Color before = rail.color;
        AvStyleHost.SetTheme(AvThemeId.Phosphor);
        con.Ticker.RestyleAll();
        if (rail.color == before) Failures.Add("theme switch did not repaint the latched tab rail");
        UnityEngine.Object.DestroyImmediate(hostGo);
    }
}
#endif

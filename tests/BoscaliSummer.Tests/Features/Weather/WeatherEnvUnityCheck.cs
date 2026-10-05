#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// The real ENV console (WeatherEnvView from the production DLL) driven with synthetic snapshots.
// Renders both pages at 420/596/896 panel heights (top and bottom of the scrolled body) and as
// one tall full-page image, then fails on text that overflows its box, enters the scroll gutter, drops
// under the 11 px floor or 4.5:1 contrast, and on any two texts that overlap each other.
public static class WeatherEnvUnityCheck
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const string Ns = "BoscaliSummer.Modules.Weather.Presentation.";
    private const string DomainNs = "BoscaliSummer.Modules.Weather.Domain.";
    private static readonly List<string> Failures = new List<string>();
    private static readonly Assembly Asm = typeof(AvConsole).Assembly;
    private static int textChecks, captures;

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
            string outDir = Path.GetFullPath("env");
            Directory.CreateDirectory(outDir);
            MethodInfo setPaths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", All);
            ParameterInfo[] pathParameters = setPaths.GetParameters();
            var pathArguments = new object[pathParameters.Length];
            pathArguments[0] = Path.GetFullPath("WeatherEnvPreview.exe");
            for (int i = 1; i < pathArguments.Length; i++)
                pathArguments[i] = pathParameters[i].HasDefaultValue ? pathParameters[i].DefaultValue : null;
            setPaths.Invoke(null, pathArguments);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            Shader.SetGlobalFloat("_NOA_Now", 1e6f);
            AvBundle.ResetForTests();
            AvBundle.Load(Debug.Log);
            if (!AvBundle.Available || !AvIcons.Available) throw new Exception("Production fonts and icons did not load.");
            AvFxDriver.Configure(AvFxTier.Off, false);
            new GameObject("Events", typeof(UnityEngine.EventSystems.EventSystem));
            foreach (AvThemeId theme in (AvThemeId[])Enum.GetValues(typeof(AvThemeId)))
            {
                AvStyleHost.SetTheme(theme);
                foreach (string scenario in new[] { "night-sct", "clear-day", "storm-in-cloud", "dry-in-cloud", "wet-rain", "cold-moisture", "no-camera", "no-forecast", "unknown-visibility", "empty" })
                {
                    // Tall image only for the default theme; the gate runs on every theme.
                    bool pictures = theme == AvThemeId.Steel || theme == AvThemeId.Ace && scenario == "night-sct";
                    foreach (float height in new[] { 420f, 596f, 896f }) Render(scenario, theme, outDir, height, pictures);
                    if (pictures) Render(scenario, theme, outDir, 1700f, true);
                }
            }
            CheckLateFill();
            CheckImmersionLifecycle();
        }
        catch (Exception e) { Failures.Add("exception: " + e); }
        File.WriteAllText("result.txt", Failures.Count == 0
            ? "PASS: ENV " + textChecks + " text checks, " + captures + " captures"
            : "FAIL (" + Failures.Count + "):\n" + string.Join("\n", Failures));
        UnityEditor.EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
    }

    // ---- Reflection helpers -------------------------------------------------------------

    private static void CheckImmersionLifecycle()
    {
        Type managerType = T("BoscaliSummer.Modules.Immersion.Runtime.ImmersionManager");
        var host = new GameObject("Inactive manager lifecycle probe"); host.SetActive(false);
        var manager = (Behaviour)host.AddComponent(managerType); manager.enabled = false;
        var pivot = new GameObject("Native camera pivot").transform;
        var freeLook = new GameObject("Native free look").transform; freeLook.SetParent(pivot, false);
        Vector3 position = new Vector3(3f, 4f, 5f); pivot.localPosition = position;
        Quaternion native = Quaternion.Euler(3f, 6f, 2f), look = Quaternion.Euler(20f, 25f, 0f);
        Quaternion written = native * Quaternion.Euler(1.5f, -.5f, .5f);
        freeLook.localRotation = look;
        MethodInfo remove = managerType.GetMethod("RemoveCameraOffset", All);
        object owner = managerType.GetField("cameraOffset", All).GetValue(manager);
        MethodInfo applyOffset = owner.GetType().GetMethod("Apply", All);
        void Seed()
        {
            pivot.localRotation = native;
            applyOffset.Invoke(owner, new object[] { pivot, Quaternion.Euler(1.5f, -.5f, .5f) });
        }
        Seed(); remove.Invoke(manager, null); remove.Invoke(manager, null);
        if (!SameRotation(pivot.localRotation, native) || pivot.localPosition != position ||
            !SameRotation(freeLook.localRotation, look))
            Failures.Add("Immersion lifecycle: owned pivot offset must restore native rotation, position and free look");
        Seed(); Quaternion foreign = Quaternion.Euler(-7f, 12f, 9f); pivot.localRotation = foreign;
        remove.Invoke(manager, null);
        if (!SameRotation(pivot.localRotation, foreign))
            Failures.Add("Immersion lifecycle: foreign native pivot rotation must survive release");
        Seed(); Set(manager, "presenting", true);
        managerType.GetMethod("ResetForScene", All).Invoke(manager, null);
        managerType.GetProperty("IsEnabled", All).SetValue(manager, false);
        if (!SameRotation(pivot.localRotation, native) || (bool)managerType.GetField("presenting", All).GetValue(manager) ||
            (bool)managerType.GetField("environmentValid", All).GetValue(manager) ||
            (bool)owner.GetType().GetProperty("IsApplied", All).GetValue(owner))
            Failures.Add("Immersion lifecycle: scene/master-disable reset must restore and invalidate owned state");
        // Unity sends OnDestroy only to components that were active at least once.
        host.SetActive(true);
        Seed();
        // This editor fixture invokes the real callback: editor-only probes have no player lifecycle.
        managerType.GetMethod("OnDestroy", All).Invoke(manager, null);
        Object.DestroyImmediate(host);
        if (!SameRotation(pivot.localRotation, native) || pivot.localPosition != position ||
            !SameRotation(freeLook.localRotation, look))
            Failures.Add("Immersion lifecycle: manager destruction callback must preserve native camera state");
        Object.DestroyImmediate(pivot.gameObject);
        textChecks += 5;
    }
    private static bool SameRotation(Quaternion a, Quaternion b) =>
        Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y) + Math.Abs(a.z - b.z) + Math.Abs(a.w - b.w) < .0001f ||
        Math.Abs(a.x + b.x) + Math.Abs(a.y + b.y) + Math.Abs(a.z + b.z) + Math.Abs(a.w + b.w) < .0001f;

    private static Type T(string name) => Asm.GetType(name, true);
    private static object New(string name) => Activator.CreateInstance(T(name), true);
    private static void Set(object o, string field, object value)
    {
        FieldInfo f = o.GetType().GetField(field, All);
        if (f == null) throw new Exception("no field " + field + " on " + o.GetType().Name);
        f.SetValue(o, value);
    }
    private static object Regime(int v) => Enum.ToObject(T(DomainNs + "WeatherRegimeType"), v);
    private static object Row(int minutes, int regime, string code, float cover, float deck, float rain)
    {
        object r = New(Ns + "EnvForecastRow");
        Set(r, "OffsetMinutes", minutes); Set(r, "Regime", Regime(regime)); Set(r, "Code", code);
        Set(r, "Cover", cover); Set(r, "Deck", deck); Set(r, "Rain", rain);
        return r;
    }

    private static object Data(string scenario)
    {
        object d = New(Ns + "EnvData");
        Type row = T(Ns + "EnvForecastRow");
        Array rows = Array.CreateInstance(row, 6);
        switch (scenario == "cold-moisture" || scenario == "dry-in-cloud" || scenario == "wet-rain" ? "storm-in-cloud" : scenario)
        {
            case "empty":
                Set(d, "HasMission", false); Set(d, "Title", "METOC / NO MISSION");
                Set(d, "Footer", "Battlefield environment unavailable."); Set(d, "FooterState", AvState.Inert);
                return d;
            case "clear-day":
                Fill(d, 0, "CLR", "Clear Sky", "OPTIMAL VISIBILITY // FULL IR SENSOR RANGE // NO CEILING CONSTRAINTS", .05f, 3800f, 5600f, 50f, 0f, .05f, 6f, 271, 91);
                Set(d, "CameraAlt", 3100f); Set(d, "HasCamera", true); Set(d, "AirDensity", .74f); Set(d, "SoundSpeed", 318f);
                Sun(d, 54.2f, 201f, 13.4f, 6.2f, 19.1f, false, false, "Sunset in 322 min.");
                Moon(d, "Waxing Gibbous", .82f, .04f, true, false);
                rows.SetValue(Row(0, 0, "CLR", .05f, 3800f, 0f), 0); rows.SetValue(Row(5, 0, "CLR", .06f, 3800f, 0f), 1);
                rows.SetValue(Row(10, 1, "FEW", .12f, 3600f, 0f), 2); rows.SetValue(Row(15, 1, "FEW", .18f, 3200f, 0f), 3);
                rows.SetValue(Row(30, 2, "SCT", .32f, 2800f, 0f), 4); rows.SetValue(Row(60, 2, "SCT", .41f, 2400f, 0f), 5);
                break;
            case "storm-in-cloud":
                Fill(d, 6, "TS", "Thunderstorm", "SEVERE TURBULENCE // FREQUENT LIGHTNING // EXTREME LOW DECK", .95f, 500f, 5200f, 1.2f, .82f, .78f, 38f, 190, 10);
                Set(d, "CameraAlt", 1400f); Set(d, "HasCamera", true); Set(d, "AirDensity", .86f); Set(d, "SoundSpeed", 331f);
                Sun(d, -3.4f, 96f, 19.6f, 6.4f, 19.1f, false, false, "Sunset in 0 min.");
                Moon(d, "Waning Crescent", .18f, .01f, false, false);
                rows.SetValue(Row(0, 6, "TS", .95f, 500f, .9f), 0); rows.SetValue(Row(5, 6, "TS", .92f, 520f, .85f), 1);
                rows.SetValue(Row(10, 5, "RA+", .8f, 650f, .6f), 2); rows.SetValue(Row(15, 5, "RA+", .72f, 800f, .4f), 3);
                rows.SetValue(Row(30, 4, "OVC", .64f, 1200f, .12f), 4); rows.SetValue(Row(60, 3, "BKN", .5f, 1800f, 0f), 5);
                break;
            case "no-camera":
                Fill(d, 3, "BKN", "Broken Deck", "VARIABLE CEILING // RESTRICTED HIGH-ALTITUDE BOMBING // POP-UP THREATS", .55f, 1900f, 3300f, 8.5f, .05f, .22f, 14f, 90, 270);
                Set(d, "HasCamera", false);
                Sun(d, -12.5f, 12f, 23.2f, 6.4f, 19.1f, false, false, "Sunrise in 340 min.");
                Moon(d, "Full Moon", 1f, .05f, true, false);
                for (int i = 0; i < 6; i++) rows.SetValue(Row(i * 5, 3, "BKN", .55f, 1900f, .05f * i), i);
                break;
            default: // night-sct: the state from the user's screenshot
                Fill(d, 2, "SCT", "Scattered Clouds", "ISOLATED CLOUD MASKING // USABLE FOR RADAR/OPTICAL TERRAIN BREAKS", .38f, 1800f, 3400f, 10f, 0f, .12f, 7f, 271, 91);
                Set(d, "CameraAlt", 6f); Set(d, "HasCamera", true); Set(d, "AirDensity", 1.22f); Set(d, "SoundSpeed", 340f);
                Sun(d, -23.9f, 17f, 15.7f, 6.3f, 19.2f, false, false, "Sunrise in 140 min.");
                Moon(d, "New Moon", 0f, 0f, true, true);
                for (int i = 0; i < 6; i++) rows.SetValue(Row(new[] { 0, 5, 10, 15, 30, 60 }[i], 0, "CLR", 0f, 2400f, 0f), i);
                break;
        }
        Set(d, "Forecast", rows);
        if (scenario == "no-forecast") Set(d, "Forecast", null);
        if (scenario == "unknown-visibility") Set(d, "VisibilityKm", -1f);
        if (d.GetType().GetField("HasViewAir", All) != null && scenario != "no-camera")
        {
            Set(d, "HasViewAir", true);
            Set(d, "ViewCloud", scenario == "storm-in-cloud" || scenario == "cold-moisture" || scenario == "dry-in-cloud" ? .85f : 0f);
            Set(d, "ViewRain", scenario == "storm-in-cloud" || scenario == "wet-rain" ? .82f : 0f);
            Set(d, "ViewMoisture", scenario == "cold-moisture" || scenario == "dry-in-cloud" ? .9f : scenario == "wet-rain" ? .75f : .12f);
            Set(d, "ViewTemperature", scenario == "cold-moisture" ? -6f : 18f);
        }
        Set(d, "DensityByAlt", new[] { 1.225f, 1.112f, 1.007f, .909f, .819f, .736f, .660f,
            .590f, .525f, .466f, .413f, .364f, .311f });
        return d;
    }

    private static void Fill(object d, int regime, string code, string name, string brief, float cover, float deck, float top,
        float vis, float rain, float turb, float kts, int from, int to)
    {
        Set(d, "Regime", Regime(regime)); Set(d, "Code", code); Set(d, "Name", name); Set(d, "Briefing", brief);
        Set(d, "Cover", cover); Set(d, "Deck", deck); Set(d, "Top", top); Set(d, "VisibilityKm", vis); Set(d, "Rain", rain);
        Set(d, "Turbulence", turb); Set(d, "WindKts", kts); Set(d, "WindFrom", from); Set(d, "WindTo", to);
        Set(d, "Next", "NEXT 03:12"); Set(d, "NextFrac", 0.36f);
        Set(d, "Footer", regime == 6 ? "Held by weather console (Ctrl+O) — Thunderstorm." : name + " holds — next step in 03:12.");
        Set(d, "FooterState", AvState.Info);
    }

    private static void Sun(object d, float elev, float az, float tod, float rise, float set, bool polarDay, bool polarNight, string evt)
    {
        Set(d, "SunElevation", elev); Set(d, "SunAzimuth", az); Set(d, "TimeOfDay", tod); Set(d, "Sunrise", rise); Set(d, "Sunset", set);
        Set(d, "PolarDay", polarDay); Set(d, "PolarNight", polarNight); Set(d, "SunEvent", evt);
    }

    private static void Moon(object d, string phase, float lit, float glow, bool waxing, bool moonless)
    {
        Set(d, "MoonPhase", phase); Set(d, "MoonLit", lit); Set(d, "MoonGlow", glow); Set(d, "MoonWaxing", waxing); Set(d, "Moonless", moonless);
    }

    // ---- Rendering ----------------------------------------------------------------------

    private static void Render(string scenario, AvThemeId theme, string outDir, float height, bool pictures)
    {
        bool tall = height > 1000f;
        int rtW = 520, rtH = (int)height + 24;
        var camGo = new GameObject("cam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.08f, 0.09f);
        var rt = new RenderTexture(rtW, rtH, 24);
        cam.targetTexture = rt;
        var canvasGo = new GameObject("canvas", typeof(Canvas));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        var host = (RectTransform)new GameObject("host", typeof(RectTransform)).transform;
        host.SetParent(canvas.transform, false);
        host.anchorMin = host.anchorMax = host.pivot = new Vector2(0f, 1f);
        host.anchoredPosition = new Vector2(20f, -12f);
        host.sizeDelta = new Vector2(AvTokens.PanelWidth, height);

        object view = Activator.CreateInstance(T(Ns + "WeatherEnvView"), All, null,
            new object[] { host, "ENV", AvTokens.PanelWidth, height }, null);
        var con = (AvConsole)view.GetType().GetProperty("Console").GetValue(view);
        view.GetType().GetMethod("Finish").Invoke(view, null);
        // Bind after Finish and advance the way the game does: content arrives late, the ticker re-lays.
        object data = Data(scenario);
        view.GetType().GetMethod("Apply").Invoke(view, new[] { data, true });
        string variant = scenario + "-" + theme + "-" + (tall ? "full" : height.ToString("0"));
        for (int page = 0; page < con.PageCount; page++)
        {
            con.SetPage(page);
            for (int i = 0; i < 4; i++) con.Ticker.TickNow();
            Canvas.ForceUpdateCanvases();
            string where = variant + "/p" + (page + 1);
            if (pictures) Capture(cam, rt, Path.Combine(outDir, variant + "-p" + (page + 1) + ".png"));
            Gate(con, where);
            ScrollRect scroll = con.Root.GetComponentInChildren<ScrollRect>();
            float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
            if (!tall && overflow > 1f)
            {
                scroll.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                float travelled = scroll.content.anchoredPosition.y;
                if (Mathf.Abs(travelled - overflow) > 2f) Failures.Add(where + ": last row unreachable");
                if (pictures) Capture(cam, rt, Path.Combine(outDir, variant + "-p" + (page + 1) + "-bottom.png"));
                Gate(con, where + "/bottom");
                scroll.verticalNormalizedPosition = 1f;
            }
        }
        Object.DestroyImmediate(canvasGo);
        Object.DestroyImmediate(camGo);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    private static void Capture(Camera cam, RenderTexture rt, string path)
    {
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        RenderTexture.active = null;
        captures++;
    }

    // ---- Gate ---------------------------------------------------------------------------

    private static void Gate(AvConsole con, string where)
    {
        float gutterLeft = AvTokens.PanelWidth - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        var boxes = new List<KeyValuePair<TMP_Text, Rect>>();
        foreach (TMP_Text t in con.Root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t)) continue;
            textChecks++;
            t.ForceMeshUpdate();
            Rect r = t.rectTransform.rect;
            bool icon = t.name.StartsWith("Icon");
            if (!icon)
            {
                Bounds b = t.textBounds;
                if (b.size.x > r.width + 1.5f)
                    Failures.Add(where + ": overflows width (" + b.size.x.ToString("0") + " > " + r.width.ToString("0") + ") '" + t.text + "' [" + t.name + "]");
                if (b.size.y > r.height + 1.5f)
                    Failures.Add(where + ": overflows height (" + b.size.y.ToString("0") + " > " + r.height.ToString("0") + ") '" + t.text + "' [" + t.name + "]");
                if (t.fontSize < AvTypeScale.Floor - 0.01f)
                    Failures.Add(where + ": below the 11 px floor (" + t.fontSize.ToString("0.0") + ") '" + t.text + "'");
            }
            var corners = new Vector3[4];
            if (t.GetComponentInParent<ScrollRect>() != null)
            {
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
            if (!icon)
            {
                // Actual glyph extent in console space, for the text-on-text overlap test.
                Bounds b = t.textBounds;
                Vector3 lo = con.Root.InverseTransformPoint(t.transform.TransformPoint(b.min));
                Vector3 hi = con.Root.InverseTransformPoint(t.transform.TransformPoint(b.max));
                Rect box = Rect.MinMaxRect(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y), Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y));
                ScrollRect sr = t.GetComponentInParent<ScrollRect>();
                if (sr != null)
                {
                    // Only what the viewport actually shows can overlap; the rest is scrolled out and clipped.
                    sr.viewport.GetWorldCorners(corners);
                    Vector3 v0 = con.Root.InverseTransformPoint(corners[0]), v1 = con.Root.InverseTransformPoint(corners[2]);
                    float x0 = Mathf.Max(box.xMin, Mathf.Min(v0.x, v1.x)), x1 = Mathf.Min(box.xMax, Mathf.Max(v0.x, v1.x));
                    float y0 = Mathf.Max(box.yMin, Mathf.Min(v0.y, v1.y)), y1 = Mathf.Min(box.yMax, Mathf.Max(v0.y, v1.y));
                    if (x1 <= x0 || y1 <= y0) continue;
                    box = Rect.MinMaxRect(x0, y0, x1, y1);
                }
                boxes.Add(new KeyValuePair<TMP_Text, Rect>(t, box));
            }
        }
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                Rect a = boxes[i].Value, b = boxes[j].Value;
                float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                float oy = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                if (ox > 1.5f && oy > 3f)
                    Failures.Add(where + ": text overlap '" + boxes[i].Key.text + "' [" + boxes[i].Key.name + "] with '" + boxes[j].Key.text + "' [" + boxes[j].Key.name + "] (" + ox.ToString("0") + "x" + oy.ToString("0") + ")");
            }
        foreach (AvControl tab in con.Root.GetComponentsInChildren<AvControl>(true))
            if (tab.transform.parent != null && tab.transform.parent.name == "Tabs" && tab.transform.Find("Label") != null
                && tab.GetComponentsInChildren<TMP_Text>(true).Length < 2)
                Failures.Add(where + ": tab without icon " + tab.name);
        foreach (Transform s in con.Root.GetComponentsInChildren<Transform>(true))
            if (s.name.StartsWith("Section ") && s.Find("Icon None") != null)
                Failures.Add(where + ": section without icon " + s.name);
        foreach (Selectable selectable in con.Root.GetComponentsInChildren<Selectable>(true))
            if (selectable.navigation.mode != Navigation.Mode.None)
                Failures.Add(where + ": flight input can steer " + selectable.name);
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

    // Data that arrives after the first layout (empty -> populated -> empty) must re-lay the page without overlap.
    private static void CheckLateFill()
    {
        AvStyleHost.SetTheme(AvThemeId.Steel);
        var hostGo = new GameObject("late", typeof(RectTransform), typeof(Canvas));
        var host = (RectTransform)hostGo.transform;
        host.sizeDelta = new Vector2(AvTokens.PanelWidth, 1700f);
        object view = Activator.CreateInstance(T(Ns + "WeatherEnvView"), All, null,
            new object[] { host, "ENV", AvTokens.PanelWidth, 1700f }, null);
        var con = (AvConsole)view.GetType().GetProperty("Console").GetValue(view);
        view.GetType().GetMethod("Finish").Invoke(view, null);
        MethodInfo apply = view.GetType().GetMethod("Apply");
        foreach (string s in new[] { "empty", "night-sct", "storm-in-cloud", "no-forecast", "unknown-visibility", "cold-moisture", "empty", "clear-day" })
        {
            apply.Invoke(view, new[] { Data(s), true });
            for (int i = 0; i < 4; i++) con.Ticker.TickNow();
            Canvas.ForceUpdateCanvases();
            for (int page = 0; page < con.PageCount; page++)
            {
                con.SetPage(page);
                for (int i = 0; i < 2; i++) con.Ticker.TickNow();
                Canvas.ForceUpdateCanvases();
                Gate(con, "late-fill/" + s + "/p" + (page + 1));
            }
            if (s == "no-forecast")
            {
                foreach (string field in new[] { "outlook", "coverEq" })
                    if (((AvPart)view.GetType().GetField(field, All).GetValue(view)).Shown)
                        Failures.Add("late-fill: stale " + field + " remains visible after forecast data disappears");
            }
            if (s == "unknown-visibility")
            {
                con.SetPage(0);
                foreach (TMP_Text text in con.Root.GetComponentsInChildren<TMP_Text>(false))
                    if (text.name == "CatValue" && text.text != "N/A")
                        Failures.Add("late-fill: missing visibility must display N/A flight category");
            }
            if (s == "night-sct")
            {
                var metrics = (AvMetric[])view.GetType().GetField("metrics", All).GetValue(view);
                bool densityCorrect = false;
                foreach (TMP_Text text in metrics[3].Rect.GetComponentsInChildren<TMP_Text>(true))
                    if (text.text == "100%") densityCorrect = true;
                if (!densityCorrect) Failures.Add("late-fill: sea-level density sample must read 100% SL");
            }
        }
        Object.DestroyImmediate(hostGo);
    }
}
#endif

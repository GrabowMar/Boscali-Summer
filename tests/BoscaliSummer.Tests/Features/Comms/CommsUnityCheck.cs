#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// Production COM builders and refresh methods over synthetic mirrored state. No network or game scene runs.
public static class CommsUnityCheck
{
    private const string Ns = "BoscaliSummer.Modules.Comms.";
    private static readonly Assembly Asm = typeof(AvConsole).Assembly;
    private static readonly List<string> Failures = new List<string>();
    private static int textChecks, captures, audienceChecks;

    public static void Run()
    {
        if (!EnsureTmpEssentials(Run)) return;
        try
        {
            Directory.CreateDirectory("com");
            SetExecutablePath("CommsPreview.exe");
            InitAvionics();
            new GameObject("Events", typeof(UnityEngine.EventSystems.EventSystem));
            foreach (AvThemeId theme in (AvThemeId[])Enum.GetValues(typeof(AvThemeId)))
            {
                AvStyleHost.SetTheme(theme);
                foreach (string state in new[] { "empty", "populated", "stale" })
                    foreach (float height in new[] { 420f, 596f, 896f }) Render(state, theme, height, theme == AvThemeId.Steel);
            }
        }
        catch (Exception e) { Failures.Add("exception: " + e); }
        Finish(Failures.Count == 0
            ? "PASS: COM " + textChecks + " text checks, " + audienceChecks + " audience interaction checks, " + captures + " captures"
            : "FAIL (" + Failures.Count + "):\n" + string.Join("\n", Failures), Failures.Count == 0);
    }

    private static Type T(string name) => Asm.GetType(Ns + name, true);
    private static object New(string name) => Activator.CreateInstance(T(name), true);
    private static object Record(string name, params object[] fields)
    {
        object o = New("Domain." + name);
        for (int i = 0; i < fields.Length; i += 2) Set(o, (string)fields[i], fields[i + 1]);
        return o;
    }

    private static void Populate(object state)
    {
        float now = Time.unscaledTime;
        Set(state, "LocalId", 1UL);
        var seen = (IDictionary)Get(state, "seen");
        for (ulong i = 2; i < 10; i++) seen.Add(i, i == 2 ? "Long Callsign Overwatch" : "VIPER " + i);
        object board = Get(state, "Board");
        for (uint i = 1; i <= 14; i++) Call(board, "Add", Record("CommsItem", "Id", i, "Author", 2UL,
            "AuthorName", "Long Callsign Overwatch", "Style", (byte)0, "Text", "ASSEMBLY AREA NORTH", "Points", new[] { 1000, 2000 },
            "Expires", now + 60f + i), false, null);
        var feed = (IList)Get(state, "feed");
        for (int i = 0; i < 30; i++) feed.Add(Record("CommsFeedLine", "Author", 2UL, "AuthorName", "Long Callsign Overwatch",
            "Time", now - i * 2f, "Kind", Enum.ToObject(T("Domain.CommsFeedKind"), i % 2 == 0 ? 0 : 5),
            "Text", "SAM CONTACT NORTH · CHECK TARGET BEFORE COMMIT", "X", 1000f, "Z", 2000f));
    }

    private static void Render(string state, AvThemeId theme, float height, bool pictures)
    {
        var cameraGo = new GameObject("cam");
        var cam = cameraGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.06f, .08f, .09f);
        var rt = new RenderTexture(520, (int)height + 24, 24);
        cam.targetTexture = rt;
        var canvasGo = new GameObject("canvas", typeof(Canvas));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        var host = AvLay.Child((RectTransform)canvas.transform, "host");
        host.anchorMin = host.anchorMax = host.pivot = new Vector2(0f, 1f);
        host.anchoredPosition = new Vector2(20f, -12f);
        host.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
        var servicesGo = new GameObject("services");
        var manager = (Behaviour)servicesGo.AddComponent(T("Runtime.CommsManager"));
        manager.enabled = false;
        var panel = (Behaviour)servicesGo.AddComponent(T("Presentation.CommsMfdPanel"));
        panel.enabled = false;
        var config = new ConfigFile(Path.GetFullPath("com-preview.cfg"), false);
        object settings = Activator.CreateInstance(T("Configuration.CommsSettings"), All, null, new object[] { config }, null);
        Call(manager, "Configure", settings, null, null);
        Call(panel, "Configure", settings, manager, null);
        if (state != "empty") Populate(Get(manager, "State"));
        if (state == "stale") Set(manager, "<HostSilent>k__BackingField", true);
        var con = AvConsole.Build(host, "COM", "MULTIPLAYER COMMS", 2, AvTokens.PanelWidth, height);
        Set(panel, "console", con);
        Set(panel, "chips", con.Chips(3));
        if (T("Presentation.CommsMfdPanel").GetMethod("BindAudienceChip", All) != null) Call(panel, "BindAudienceChip");
        con.Tabs((AvIcon.Map2, "MAP"), (AvIcon.Message2, "COMMS"));
        Call(panel, "BuildMapPage", con.Page(0));
        Call(panel, "BuildCommsPage", con.Page(1));
        con.Finish();
        for (int page = 0; page < 2; page++)
        {
            con.SetPage(page);
            for (int i = 0; i < 4; i++) { con.Ticker.TickNow(); Call(panel, "Refresh"); }
            Canvas.ForceUpdateCanvases();
            string variant = state + "-" + theme + "-" + height.ToString("0") + "-p" + (page + 1);
            if (pictures) Capture(cam, rt, "com/" + variant + ".png");
            Gate(con, variant);
            if (state == "stale" && !con.Footer.Rect.Find("Text").GetComponent<TMP_Text>().text.Contains("HOST NOT ANSWERING"))
                Failures.Add(variant + ": stale host warning missing");
            ScrollRect scroll = con.Root.GetComponentInChildren<ScrollRect>();
            float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
            if (overflow > 1f)
            {
                scroll.verticalNormalizedPosition = .5f;
                Canvas.ForceUpdateCanvases();
                if (pictures) Capture(cam, rt, "com/" + variant + "-mid.png");
                Gate(con, variant + "/mid");
                scroll.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                if (Mathf.Abs(scroll.content.anchoredPosition.y - overflow) > 2f) Failures.Add(variant + ": last row unreachable");
                if (pictures) Capture(cam, rt, "com/" + variant + "-bottom.png");
                Gate(con, variant + "/bottom");
                scroll.verticalNormalizedPosition = 1f;
            }
            foreach (Selectable selectable in con.Root.GetComponentsInChildren<Selectable>(true))
                if (selectable.navigation.mode != Navigation.Mode.None) Failures.Add(variant + ": flight input can steer " + selectable.name);
            AvChip[] chipParts = (AvChip[])Get(panel, "chips");
            Transform audience = chipParts[1].Rect.Find("Change audience");
            if (audience != null)
            {
                AvHit hit = audience.GetComponent<AvHit>();
                var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                hit.OnPointerClick(pointer);
                audienceChecks++;
                if (Get(manager, "Channel").ToString() != "All") Failures.Add(variant + ": audience chip did not select ALL");
                hit.OnPointerClick(pointer);
                audienceChecks++;
                if (Get(manager, "Channel").ToString() != "Team") Failures.Add(variant + ": audience chip did not return to TEAM");
                var allowAll = (ConfigEntry<bool>)Get(settings, "AllowAllChannel");
                allowAll.Value = false;
                hit.OnPointerClick(pointer);
                audienceChecks++;
                if (Get(manager, "Channel").ToString() != "Team") Failures.Add(variant + ": audience chip bypassed host ALL restriction");
                allowAll.Value = true;
                // Keep the exercised refusal from replacing the next page's ambient footer.
                Call(Get(manager, "State"), "SetNotice", "", false, float.NegativeInfinity, false);
            }
            else Failures.Add(variant + ": permanent audience control missing");
        }
        Object.DestroyImmediate(servicesGo);
        Object.DestroyImmediate(canvasGo);
        Object.DestroyImmediate(cameraGo);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    private static void Capture(Camera cam, RenderTexture rt, string path)
    {
        CapturePng(cam, rt, path);
        captures++;
    }
    private static void Gate(AvConsole con, string where)
    {
        float gutterLeft = AvTokens.PanelWidth - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        var boxes = new List<KeyValuePair<TMP_Text, Rect>>();
        foreach (TMP_Text t in con.Root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t)) continue;
            t.ForceMeshUpdate();
            // TMP_InputField carries an invisible zero-width sentinel with viewport-sized bounds.
            if (string.IsNullOrWhiteSpace(t.text.Replace("\u200b", ""))) continue;
            textChecks++;
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

}
#endif

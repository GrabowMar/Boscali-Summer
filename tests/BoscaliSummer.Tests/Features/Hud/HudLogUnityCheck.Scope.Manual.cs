#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// Manual TAC BOARD slots: one to four pages in a grid of at most two across and two down, SYSTEMS always on in its own block, and a page whose feed has nothing drawing
// its own idle face. Run with -Pages manual.
public static partial class HudLogUnityCheck
{
    private static object BoardPageOf(string page) => Enum.Parse(Type("BoscaliSummer.Core.Contracts.BoardPage"), page, true);

    /// <summary>The fixture's page in slot 1. SYSTEMS (and the stores, shown with it) is no slot: the board always draws it, so slot 1 keeps what it has.</summary>
    private static void SelectFixturePage(object status, object options, string page)
    {
        if (page != "stores" && page != "systems") Set(options, "Page1", BoardPageOf(page));
        Set(status, "Options", options);
    }

    private static void ScopeManual()
    {
        foreach (string layout in new[] { "Wide", "Stack" })
            foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720) })
            {
                for (int count = 1; count <= 4; count++) ManualGridShot(layout, size, count);
                ManualGridShot(layout, size, 4, false);
            }
        ManualSelectionPersists();
        ManualCountermeasuresClearance();
    }

    private static void ManualGridShot(string layout, Vector2 size, int count, bool showStores = true)
    {
        MakeBoard(size, 6f, out Canvas canvas, out object status, out object frame);
        // Real pilot defaults (the historical fixture helpers use Scale 1 for design-coordinate checks).
        object options = Type(HudDomain + "BoardOptions").GetProperty("Default", All).GetValue(null);
        Fill(options, "Layout", LayoutOf(layout), "PageCount", count, "ShowStores", showStores, "Page1", BoardPageOf("Threat"),
            "Page2", BoardPageOf("Objective"), "Page3", BoardPageOf("Wing"), "Page4", BoardPageOf("MissileCam"));
        if (size.y < 800f) Set(options, "Scale", 1.4f);      // every page must still fit at the largest size the pilot can ask for
        Set(status, "Options", options);
        var scene = new ScopeScene(6f, size, frame);
        FixtureSystems(6f, scene);
        FixtureThreat(6f, scene);
        FixtureObjective(6f, scene);
        StoresFixture("threat", 6f, scene);
        Call(status, "Present", frame);
        Canvas.ForceUpdateCanvases();
        string tag = "manual-" + layout.ToLowerInvariant() + "-" + count + (showStores ? "" : "-nostores") + "-" + (int)size.x + "x" + (int)size.y;
        Check(Shown(status, "storesRect") == showStores, tag + ": the major stores group follows its switch");
        Check(Shown(status, "statusRect"), tag + ": SYSTEMS always shows, whatever the slots");
        Check((int)Field(status, "selectedCount") == count, tag + ": every requested page is selected");
        float unit = (float)Field(status, "unit");
        var surfaces = (Array)Field(status, "pageSurfaces");
        var xs = new List<float>();
        var ys = new List<float>();
        for (int i = 0; i < surfaces.Length; i++)
        {
            var rect = (RectTransform)Field(surfaces.GetValue(i), "Scope");
            Check(rect.gameObject.activeSelf == (i < count), tag + ": only requested page surfaces are visible");
            if (i >= count) continue;
            if (!xs.Exists(x => Mathf.Abs(x - rect.anchoredPosition.x) < 2f)) xs.Add(rect.anchoredPosition.x);
            if (!ys.Exists(y => Mathf.Abs(y - rect.anchoredPosition.y) < 2f)) ys.Add(rect.anchoredPosition.y);
        }
        Check(xs.Count <= 2 && ys.Count <= 2, tag + ": at most two pages across and two down (" + xs.Count + " x " + ys.Count + ")");
        if (xs.Count == 2) Check(Mathf.Abs(Mathf.Abs(xs[0] - xs[1]) - 452f * unit) < 2f, tag + ": columns are a page and its gap apart");
        if (ys.Count == 2) Check(Mathf.Abs(Mathf.Abs(ys[0] - ys[1]) - 298f * unit) < 2f, tag + ": rows are a page and its gap apart");
        var root = (RectTransform)Field(status, "root");
        var points = new Vector3[4];
        root.GetWorldCorners(points);
        Rect viewport = ((RectTransform)canvas.transform).rect;
        Vector3 lo = canvas.transform.InverseTransformPoint(points[0]), hi = canvas.transform.InverseTransformPoint(points[2]);
        Check(lo.x >= viewport.xMin - 1f && lo.y >= viewport.yMin - 1f && hi.x <= viewport.xMax + 1f && hi.y <= viewport.yMax + 1f,
            tag + ": the whole board stays inside the viewport");
        var words = BoardWords(status);
        if (count >= 3) Check(words.Contains("NO WING"), tag + ": an empty WING page says so");
        if (count >= 4) Check(words.Contains("NO MISSILE"), tag + ": an empty MISSILE CAM page says so");
        ScopeChecks(status, tag);
        Capture(canvas, tag + ".png");
        Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
    }

    private static void ManualSelectionPersists()
    {
        var size = new Vector2(1920, 1080);
        MakeBoard(size, 6f, out Canvas canvas, out object status, out object frame);
        object options = PilotOptions("Page1", BoardPageOf("Wing"));
        Set(status, "Options", options);
        var notices = new int[1];
        Set(status, "Notice", CountingNotice(status, notices));
        var scene = new ScopeScene(6f, size, frame);
        FixtureThreat(6f, scene); FixtureSystems(10f, scene); StoresFixture("threat", 6f, scene);
        Call(status, "Present", frame);
        Call(status, "Present", frame);
        Check(PageTitle(status) == "WING", "Manual WING stays selected while threats and critical systems are live");
        Check(notices[0] > 0, "Unselected feeds continue posting warnings");
        var words = BoardWords(status);
        Check(words.Contains("NO WING") && words.Contains("NO WINGMEN FLYING WITH YOU"), "An empty page says what it would show and why it has nothing");
        Check(!words.Contains("NO ACTIVE DATA"), "No generic standby text is left");
        Check(Shown(status, "statusRect"), "SYSTEMS shows beside an unrelated manual page");
        ScopeReset();
        foreach (float t in new[] { 7f, 60f, 300f })
        {
            Set(frame, "Now", t); Call(status, "Present", frame);
            Check(PageTitle(status) == "WING", "The selected page persists after all feeds end, at " + t + " seconds");
        }
        Canvas.ForceUpdateCanvases();
        ScopeChecks(status, "manual-empty");
        Capture(canvas, "manual-empty-1920x1080.png");

        // A quiet sky on the radar-warning page: the empty scope and CLEAR, not a blank.
        Set(options, "Page1", BoardPageOf("Threat"));
        Set(status, "Options", options); Set(frame, "Now", 301f); Call(status, "Present", frame); Call(status, "Animate", 301.3f);
        Check(PageTitle(status) == "THREAT" && BoardWords(status).Contains("CLEAR") && BoardWords(status).Contains("TTI RINGS"), "An idle THREAT page draws its empty scope and CLEAR");
        Canvas.ForceUpdateCanvases();
        ScopeChecks(status, "manual-threat-clear");
        Capture(canvas, "manual-threat-clear-1920x1080.png");

        int children = ((RectTransform)Field(status, "root")).childCount;
        Fill(options, "PageCount", 4, "Page1", BoardPageOf("Cargo"), "Page2", BoardPageOf("Approach"), "Page3", BoardPageOf("Dock"), "Page4", BoardPageOf("Strike"), "Declutter", Declutter("Minimal"));
        Set(status, "Options", options); Set(frame, "Now", 302f); Call(status, "Present", frame); Call(status, "Animate", 302.3f);
        Check(PageTitle(status) == "CARGO" && (int)Field(status, "selectedCount") == 4, "Minimal keeps every manually chosen page");
        words = BoardWords(status);
        foreach (string title in new[] { "CARGO", "APPROACH", "DOCK", "STRIKE" })
            Check(words.Contains(title), "Minimal retains the manually chosen page title: " + title);
        foreach (string idle in new[] { "NO CARGO", "NO APPROACH", "NO TANKER", "NO STRIKES" })
            Check(words.Contains(idle), "Every empty page draws its own idle word: " + idle);
        Check(((RectTransform)Field(status, "root")).childCount == children, "Changing count and pages reuses the existing surfaces");
        ScopeChecks(status, "manual-four-empty");
        Capture(canvas, "manual-four-empty-1920x1080.png");
        Set(options, "ScopeMode", Enum.Parse(Type("BoscaliSummer.Core.Contracts.BoardScopeMode"), "Off"));
        Set(status, "Options", options); Set(frame, "Now", 303f); Call(status, "Present", frame); Call(status, "Animate", 303.3f);
        foreach (object surface in (Array)Field(status, "pageSurfaces"))
            Check(!((RectTransform)Field(surface, "Scope")).gameObject.activeSelf, "The scope group hides every page");
        Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
    }

    private static void ManualCountermeasuresClearance()
    {
        MakeBoard(new Vector2(1920, 1080), 6f, out Canvas canvas, out object status, out object frame);
        Set(status, "Options", PilotOptions("ShowStores", false));
        var dock = (RectTransform)Field(status, "dock");
        object native = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Runtime.NativeCorner"), true);
        foreach (string field in new[] { "weapon", "countermeasures", "capacitor" })
        {
            var box = new GameObject(field, typeof(RectTransform), typeof(CanvasGroup));
            box.transform.SetParent(dock, false);
            box.SetActive(field == "countermeasures");
            Set(native, field, box.GetComponent<CanvasGroup>());
        }
        Set(status, "corner", native);
        Call(native, "Apply", true, true, true);
        Call(status, "Present", frame);
        Call(status, "TickNatives");
        Check((bool)Field(status, "underNative"), "Stores off reserves the native corner when only countermeasures are active");
        Check(((CanvasGroup)Field(native, "countermeasures")).alpha == 1f, "Stores off restores native countermeasures");
        var corners = new Vector3[4];
        ((RectTransform)Field(status, "root")).GetWorldCorners(corners);
        Check(dock.InverseTransformPoint(corners[2]).y <= dock.rect.yMin + 1f, "Manual pages stay below the restored native countermeasure box");
        Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
    }
}
#endif

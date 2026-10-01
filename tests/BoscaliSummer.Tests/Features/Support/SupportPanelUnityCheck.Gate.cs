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
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// The run, the render loop and the visual gate of the OPS MFD check. Each page is built the way the
/// game builds it (pages, then <c>Finish()</c>), content is bound afterwards and the console ticker
/// settles the layout (<c>TickNow</c>, never a manual relayout). A populated and an empty state are
/// rendered per page and both are gated: no text past its box, none under the 11 px floor, contrast
/// against the real fill, nothing in the scroll gutter, no two texts touching, no two parts of a flow
/// overlapping, an icon on every tab and section.
/// </summary>
public static partial class SupportPanelUnityCheck
{
    private static int gatedTexts;
    private static readonly List<string> Failures = new List<string>();

    private static void Fail(string message)
    {
        if (!Failures.Contains(message)) Failures.Add(message);
    }

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
            MethodInfo setPaths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            ParameterInfo[] parameters = setPaths.GetParameters();
            var arguments = new object[parameters.Length];
            arguments[0] = Path.GetFullPath("SupportPreview.exe");
            for (int i = 1; i < arguments.Length; i++) arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            setPaths.Invoke(null, arguments);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            // The real faces and icon font when the kit bundle is beside the project (the game always has
            // it): the panel renders and their text gates measure what the player sees.
            AvBundle.ResetForTests();
            bool bundled = File.Exists("avionics-ui.bundle");
            if (bundled) AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            AvFxDriver.Configure(AvFxTier.Full, false);
            Shader.SetGlobalFloat("_NOA_Now", 1e6f);
            foreach (float height in new[] { 420f, 596f, 896f })
                foreach (string page in Pages) Render(page, height);
            CheckActionForms();
            AvBundle.ResetForTests();
            if (Failures.Count > 0) throw new Exception(Failures.Count + " visual gate failures:\n" + string.Join("\n", Failures.GetRange(0, Math.Min(80, Failures.Count))));
            File.WriteAllText("result.txt", "PASS: " + (Pages.Length * 3) + " real OPS MFD page layouts (" + (bundled ? "kit bundle faces" : "fallback face") +
                ", populated and empty states, content bound after Finish and settled by TickNow) painted by their production refresh paths; " + assertions +
                " geometry/readability/overlap/facts assertions over " + gatedTexts + " gated texts. Production DLL builders with offline model/fixture data; no live game, state replication or input integration claimed.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static void Render(string page, float height)
    {
        var canvasObject = new GameObject("OpsPreview", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvasObject.transform;
        root.sizeDelta = new Vector2(480f, height);

        AvConsole shell = AvConsole.Build(root, "OPS", "OPERATIONS", 3, 480f, height);
        AvChip[] chips = shell.Chips(3);
        chips[0].Set("OFFLINE QA", AvState.Info);
        chips[1].Set("FIXTURE", AvState.Inert);
        chips[2].Set("NO ORDERS", AvState.Inert);
        AvMetric[] metrics = shell.Metrics("ALLOCATION", "ORBIT", "CYBER", "SPEC OPS");
        metrics[0].Set("8,089", "AVAILABLE", 1f, AvState.Ready);
        metrics[1].Set("8/15", "STATION", .53f, AvState.Info);
        metrics[2].Set("6/8", "INFOCON 3", .75f, AvState.Caution);
        metrics[3].Set("1/3", "1 POST", .25f, AvState.Ready);
        shell.Tabs((AvIcon.Satellite, "SPACE"), (AvIcon.ShieldLock, "CYBER"), (AvIcon.UsersGroup, "SPEC OPS"));
        shell.Footer.Set("Offline layout check · no live game state or orders.", AvState.Inert);

        var panelObject = new GameObject("SupportPanel");
        object panel = panelObject.AddComponent(Mod.GetType("BoscaliSummer.Features.Support.Presentation.SupportPanel", true));
        ((Behaviour)panel).enabled = false;
        var managerObject = new GameObject("SupportManager");
        object manager = managerObject.AddComponent(Mod.GetType("BoscaliSummer.Features.Support.Runtime.SupportManager", true));
        ((Behaviour)manager).enabled = false;
        Type settingsType = Mod.GetType("BoscaliSummer.Features.Support.Configuration.SupportSettings", true);
        object settings = Activator.CreateInstance(settingsType, new ConfigFile(Path.GetFullPath("fixture.cfg"), false));
        Type catalogType = Mod.GetType("BoscaliSummer.Features.Support.Runtime.SupportCatalog", true);
        object catalog = Activator.CreateInstance(catalogType, Hidden, null, new[] { settings, null }, null);
        Set(manager, "settings", settings);
        Set(manager, "catalog", catalog);
        Set(panel, "support", manager);
        Set(panel, "shell", shell);
        overlaySupport = manager;

        PageInfo(page, out int tab, out int sub, out string buildMethod, out string subField);
        // Build the way the game does: pages first, Finish(), and only then bind content and let the
        // ticker settle the layout (no manual Relayout).
        Call(panel, buildMethod, shell.Page(tab));
        shell.Finish();
        object subPage = Get(panel, subField);
        Call(subPage, "Select", sub, null);
        shell.SetPage(tab);
        var pageRoot = (RectTransform)Field(subPage, "Rect");
        string prefix = "ops-" + page.ToLowerInvariant() + "-" + height;

        Paint(panel, page, false);
        Call(panel, "RefreshActionRows", tab, false);
        Settle(shell, root);
        // Capture before asserting, so a failing page still leaves its picture.
        Capture(canvasObject, height, prefix + ".png");
        GateConsole(shell, page + " " + height + " populated");
        CheckFacts(panel, page);
        CheckActionRows(panel, page);

        bool scrolled = false;
        foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true))
        {
            if (!scroll.gameObject.activeInHierarchy) continue;
            Check(scroll.content.rect.height > 1f, "Scroll content must be laid out.");
            scroll.verticalNormalizedPosition = 0f;
            scrolled = true;
        }
        if (scrolled) { Canvas.ForceUpdateCanvases(); Capture(canvasObject, height, prefix + "-bottom.png"); }
        foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true)) scroll.verticalNormalizedPosition = 1f;

        // The tile vocabulary in every state the host can put it in (the offline fixture has no priced map).
        if (PaintTileStates(panel, page))
        {
            Settle(shell, root);
            Capture(canvasObject, height, prefix + "-states.png");
            GateConsole(shell, page + " " + height + " states");
        }

        // The missing-thing state of the same page: one compact card, no empty section headers.
        Paint(panel, page, true);
        Settle(shell, root);
        Capture(canvasObject, height, prefix + "-empty.png");
        GateConsole(shell, page + " " + height + " empty");
        CheckEmptyState(panel, page);

        // Do not call gameplay component teardown against an absent game session.
        Object.DestroyImmediate(canvasObject);
        panelObject.SetActive(false);
        managerObject.SetActive(false);
    }

    /// <summary>
    /// Repaint every ability tile of an ACTIONS page through the production tile painter with the facts
    /// <c>AbilityStatus.Describe</c> gives for ready, armed, short of allocation, gated and cooling: the
    /// wrapped readiness words are the longest strings a tile ever carries.
    /// </summary>
    private static bool PaintTileStates(object panel, string page)
    {
        if (page != "SpaceOps" && page != "CyberOps" && page != "SpecActions") return false;
        var rows = new List<object[]>();
        if (page == "SpaceOps")
            foreach (object r in (IEnumerable)Get(panel, "spaceStrikeRows")) rows.Add(new[] { Get(r, "Row"), Get(r, "Button"), Get(r, "Action") });
        else if (page == "CyberOps")
            foreach (object r in (IEnumerable)Get(panel, "cyberAbilityRows")) rows.Add(new[] { Get(r, "Row"), Get(r, "Button"), Get(r, "Action") });
        else
            foreach (object r in (IEnumerable)Get(panel, "actionRows"))
                if ((int)Get(r, "Tab") == 2) rows.Add(new[] { Get(r, "View"), Get(r, "Trailing"), Get(r, "Definition") });
        Type status = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Viz.AbilityStatus", true);
        MethodInfo describe = status.GetMethod("Describe", BindingFlags.Static | BindingFlags.Public);
        MethodInfo paint = panel.GetType().GetMethod("PaintAbilityTile", BindingFlags.Static | BindingFlags.NonPublic);
        for (int i = 0; i < rows.Count; i++)
        {
            object action = rows[i][2];
            bool cyber = (bool)Property(action, "IsCyber");
            int variant = i % 5;
            bool armed = variant == 1;
            float allocation = variant == 2 ? 10f : 2400f, cooldown = variant == 4 ? 12f : 0f;
            bool gateOpen = variant != 3;
            string gate = variant == 3 ? "RECHARGING · T-12s" : "WITHIN 6 KM OF YOUR OP";
            object facts = describe.Invoke(null, new object[] { action, cyber, 250f, 42f, armed, false, true, allocation, cooldown, false,
                variant == 2 ? 0f : 200f, gateOpen, gate });
            string readiness = (string)Field(facts, "Readiness");
            bool usable = (bool)Field(facts, "Enabled") || (bool)Field(facts, "Armed");
            string sub = usable ? readiness + "\nLEASE 71s · ONE USE · RADIUS 6 KM" : readiness;
            paint.Invoke(null, new object[] { rows[i][0], rows[i][1], action, facts, sub, "ARM", null });
        }
        return rows.Count > 0;
    }

    /// <summary>Run the console ticker as the game does until every part has re-measured.</summary>
    private static void Settle(AvConsole shell, RectTransform root)
    {
        for (int i = 0; i < 8; i++) shell.Ticker.TickNow();
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
    }

    /// <summary>The real page painters, fed production-model fixtures (or their missing-thing twins).</summary>
    private static void Paint(object panel, string page, bool empty)
    {
        switch (page)
        {
            case "Station":
            {
                if (empty) { Call(panel, "RefreshStationPage", null, 0.0); break; }
                object station = PlatformFixture(out double now);
                // The voice loop as a session fills it (the page never logs by itself offline).
                Call(panel, "Log", "LIFTOFF CONFIRMED · BASTION CORE CLIMBING");
                Call(panel, "Log", "ORBIT INSERTION CONFIRMED · LOW EARTH ORBIT");
                Call(panel, "Log", "HARD DOCK · IMG PORT");
                Call(panel, "Log", "LINK · BASTION ON STATION · CENTRE");
                Call(panel, "RefreshStationPage", station, now);
                break;
            }
            case "SpaceOps":
            {
                if (empty) { Call(panel, "RefreshSpaceOpsPage", false, null, 0.0); break; }
                object station = PlatformFixture(out double now);
                Call(panel, "RefreshSpaceOpsPage", false, station, now);
                break;
            }
            case "Status":
            {
                if (empty) { Call(panel, "RefreshCyberStatusPage", null, 0.0); break; }
                object network = CyberFixture(out int held, out double now);
                Call(panel, "SelectSite", held);
                Check((int)Get(panel, "selectedSite") == held, "A node click must select that slot.");
                Call(panel, "CyberLog", "WATCH FLOOR ONLINE · AEGIS NET STANDING BY");
                Call(panel, "CyberLog", "C2 UP · CENTRAL AIRBASE");
                Call(panel, "CyberLog", "BREACH OPEN · CITY 11");
                Call(panel, "CyberLog", "ACCESS OPEN · CTY-ALPHA · 75S · ONE EFFECT");
                Call(panel, "CyberLog", "!! ORIGIN TRACKED · HOSTILE SORTIE");
                Call(panel, "RefreshCyberStatusPage", network, now);
                break;
            }
            case "CyberOps":
            {
                if (empty) { Call(panel, "RefreshCyberOpsPage", false, null, 0.0); break; }
                object network = CyberFixture(out _, out double now);
                Call(panel, "RefreshCyberOpsPage", false, network, now);
                break;
            }
            case "SpecStatus":
            {
                if (empty) { Call(panel, "RefreshSpecStatusPage", null, 0.0); break; }
                object detachment = DetachmentFixture(out double now);
                Call(panel, "SpecLog", "DETACHMENT ON THE NET · ALPHA AND BRAVO STANDING BY");
                Call(panel, "SpecLog", "CHARLIE RAISED · RECRUIT");
                Call(panel, "SpecLog", "ALPHA · INSERTED · RECON KERSEY");
                Call(panel, "SpecLog", "ALPHA · OBSERVATION POST OVER KERSEY");
                Call(panel, "SpecLog", "BRAVO · EN ROUTE · AIR DEFENCE 26/-4");
                Call(panel, "SpecLog", "!! CHARLIE · ON TASK · NORTH RIDGE AIRFIELD");
                Call(panel, "RefreshSpecStatusPage", detachment, now);
                Check(Get(panel, "specDeck") != null, "SPEC OPS STATUS must lead with the squad deck.");
                break;
            }
            default:
                Call(panel, "RefreshSpecActionsPage", empty ? null : DetachmentFixture(out _));
                break;
        }
    }

    // ---- The gate: text overflow, floor, contrast, gutter, and text or part overlap ---------------------

    private static void GateConsole(AvConsole con, string where)
    {
        float gutterLeft = AvTokens.PanelWidth - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        var placed = new List<KeyValuePair<TMP_Text, Rect>>(160);
        foreach (TMP_Text t in con.Root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t)) continue;
            gatedTexts++;
            assertions++;
            t.ForceMeshUpdate();
            Rect r = t.rectTransform.rect;
            bool icon = t.name.StartsWith("Icon");
            Bounds b = t.textBounds; // what TMP actually laid out, after wrapping and auto-size
            if (!icon)
            {
                if (b.size.x > r.width + 1.5f)
                    Fail(where + ": overflows width (" + b.size.x.ToString("0") + " > " + r.width.ToString("0") + ") '" + t.text + "'");
                if (b.size.y > r.height + 1.5f)
                    Fail(where + ": overflows height (" + b.size.y.ToString("0") + " > " + r.height.ToString("0") + ") '" + t.text + "'");
                if (t.fontSize < AvTypeScale.Floor - 0.01f)
                    Fail(where + ": below the 11 px floor (" + t.fontSize.ToString("0.0") + ") '" + t.text + "'");
                if (t.isTextTruncated) Fail(where + ": text must be complete: " + t.text);
            }
            var corners = new Vector3[4];
            t.rectTransform.GetWorldCorners(corners);
            if (t.GetComponentInParent<ScrollRect>() != null)
            {
                float right = con.Root.InverseTransformPoint(corners[2]).x;
                if (right > gutterLeft) Fail(where + ": enters the gutter (" + right.ToString("0") + ") '" + t.text + "'");
            }
            if (!icon && t.color.a > 0.5f)
            {
                Color back = BackgroundOf(t, ground);
                float contrast = Rgba.Contrast(t.color.ToRgba().WithAlpha(1f).Over(back.ToRgba()), back.ToRgba());
                if (contrast < 4.5f) Fail(where + ": contrast " + contrast.ToString("0.00") + " for '" + t.text + "' (" + t.name + ")");
            }
            if (!icon && t.GetComponentInParent<ScrollRect>() != null)
            {
                // Glyph ink bounds (page texts only: the chrome above the page is the kit gallery's to gate) in console space: two texts whose ink overlaps have collided.
                Vector3 lo = con.Root.InverseTransformPoint(t.rectTransform.TransformPoint(b.min));
                Vector3 hi = con.Root.InverseTransformPoint(t.rectTransform.TransformPoint(b.max));
                placed.Add(new KeyValuePair<TMP_Text, Rect>(t, Rect.MinMaxRect(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y), Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y))));
            }
        }
        for (int i = 0; i < placed.Count; i++)
            for (int j = i + 1; j < placed.Count; j++)
            {
                Rect a = placed[i].Value, b = placed[j].Value;
                float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                assertions++;
                if (w > 1f && h > 1.5f)
                    Fail(where + ": texts overlap: '" + placed[i].Key.text + "' (" + placed[i].Key.name + ") and '" + placed[j].Key.text + "' (" + placed[j].Key.name + ")");
            }
        foreach (AvControl tab in con.Root.GetComponentsInChildren<AvControl>(true))
            if (tab.transform.parent != null && tab.transform.parent.name == "Tabs" && tab.transform.Find("Label") != null
                && tab.GetComponentsInChildren<TMP_Text>(true).Length < 2)
                Fail(where + ": tab without icon " + tab.name);
        foreach (Transform s in con.Root.GetComponentsInChildren<Transform>(true))
            if (s.name.StartsWith("Section ") && s.Find("Icon None") != null)
                Fail(where + ": section without icon " + s.name);
        // Parts of one flow never overlap each other (shown parts only).
        foreach (Transform t in con.Root.GetComponentsInChildren<Transform>(false))
            if ((t.name == "Status" || t.name == "Actions") && t is RectTransform content && CanvasOn(content)) NoPartOverlap(content, where);
    }

    private static bool CanvasOn(Component c)
    {
        for (Transform x = c.transform; x != null; x = x.parent)
        {
            var canvas = x.GetComponent<Canvas>();
            if (canvas != null && !canvas.enabled) return false;
        }
        return true;
    }

    private static void NoPartOverlap(RectTransform content, string where)
    {
        var rects = new List<RectTransform>();
        foreach (Transform child in content)
            if (child.gameObject.activeSelf && child is RectTransform rt && rt.rect.height > 0.5f) rects.Add(rt);
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                Rect a = InSpace(content, rects[i]), b = InSpace(content, rects[j]);
                float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                assertions++;
                if (w > 1f && h > 1f) Fail(where + ": parts overlap: " + rects[i].name + " and " + rects[j].name);
            }
    }

    private static Rect InSpace(RectTransform space, RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        Vector3 lo = space.InverseTransformPoint(c[0]), hi = space.InverseTransformPoint(c[2]);
        return Rect.MinMaxRect(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y), Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y));
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

    /// <summary>The missing-thing state is one compact card: the parts that would be empty are gone, not blank.</summary>
    private static void CheckEmptyState(object panel, string page)
    {
        switch (page)
        {
            case "Station":
                Check(!Shown(panel, "meterEnergy"), "No station: the health meters must collapse.");
                Check(!Shown(panel, "roster"), "No station: the roster must collapse.");
                Check(!Shown(panel, "healthSection"), "No station: no empty HEALTH header.");
                Check(!Shown(panel, "moduleSection"), "No station: no empty MODULES header.");
                Check((float)Invoke(Get(panel, "stationHero"), "Measure", 444f) > 190f, "No station: the hero is the empty-state card with its call to action.");
                Check(Shown(panel, "windowCard"), "No station: the window clock stays.");
                break;
            case "SpaceOps":
            {
                int shownRows = 0;
                bool onlyOffboard = true;
                foreach (object row in (IEnumerable)Get(panel, "spaceStrikeRows"))
                {
                    if (!(bool)Property(Get(row, "Row"), "Shown")) continue;
                    shownRows++;
                    string id = Get(Get(row, "Action"), "Id").ToString();
                    if (id != "Prsm" && id != "Cruise") onlyOffboard = false;
                }
                Check(shownRows == 2 && onlyOffboard, "No station: only the offboard FIRES rows show.");
                Check(!Shown(panel, "spaceAbilitiesSection"), "No station: no empty ABILITIES header.");
                Check(Shown(panel, "spaceFiresSection"), "No station: the OFFBOARD FIRES header stays.");
                Check(Shown(panel, "strikeStrip"), "No station: the strip still reports no fires.");
                break;
            }
            case "Status":
                Check(!Shown(panel, "cyberMap"), "No network: the mesh plot must collapse.");
                Check(!Shown(panel, "cyberMeshSection"), "No network: no empty MESH header.");
                Check(!Shown(panel, "cyberComputing"), "No network: the resource meters must collapse.");
                break;
            case "CyberOps":
            {
                int hidden = 0, shown = 0;
                foreach (object row in (IEnumerable)Get(panel, "cyberAbilityRows"))
                    if ((bool)Property(Get(row, "Row"), "Shown")) shown++; else hidden++;
                Check(hidden > 0 && shown > 0, "No network: access tiers collapse; base support stays.");
                break;
            }
            case "SpecStatus":
                Check(!Shown(panel, "specDeck"), "No detachment: the squad deck must collapse.");
                Check(!Shown(panel, "specTheatre"), "No detachment: the theatre plot must collapse.");
                break;
        }
    }

    private static bool Shown(object panel, string field) => (bool)Property(Get(panel, field), "Shown");
}
#endif

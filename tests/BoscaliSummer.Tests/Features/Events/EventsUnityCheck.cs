#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BoscaliSummer.Modules.Events.Configuration;
using BoscaliSummer.Modules.Events.Domain;
using BoscaliSummer.Modules.Events.Presentation;
using BoscaliSummer.Modules.Events.Runtime;
using BoscaliSummer.Core.Contracts;
using NOAvionics;
using NuclearOption.Networking;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Offline gate for the EVN console (empty, active, scripted superevent, decision board, late fill) and the
/// field archive window. Compiles the real presentation sources against stubbed game types, renders PNGs and
/// fails on text overflow, text entering the scroll gutter, low contrast, tabs or sections without an icon,
/// parts overlapping their neighbour, stale (unmeasured) layout and windows larger than the screen.
/// </summary>
public static class EventsUnityCheck
{
    private const int W = 1920, H = 1080;
    private static readonly List<string> Failures = new List<string>();
    private static int checkedTexts, checkedFlows;

    public static void Run()
    {
        if (Shader.Find("TextMeshPro/Distance Field") == null)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += Run;
            AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
            return;
        }
        try
        {
            var setPaths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var parameters = setPaths.GetParameters();
            var arguments = new object[parameters.Length];
            arguments[0] = Path.GetFullPath("EventsCheck.exe");
            for (int i = 1; i < arguments.Length; i++) arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            setPaths.Invoke(null, arguments);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvBundle.ResetForTests();
            if (File.Exists("avionics-ui.bundle")) AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            Check(AvIcons.Available, "production icon atlas must load before rendering");
            Shader.SetGlobalFloat("_NOA_Now", 1e6f);
            AvFxDriver.Configure(AvBundle.Available ? AvFxTier.Full : AvFxTier.Off, false);
            AvStyleHost.SetTheme(AvThemeId.Steel);
            new GameObject("Events", typeof(EventSystem));
            Directory.CreateDirectory("renders");

            foreach (float height in new[] { 420f, 596f, 896f }) CheckConsole(height);
            CheckArchive(false, new Vector2(1920f, 1080f), "1080");
            CheckArchive(true, new Vector2(1920f, 1080f), "1080");
            CheckArchive(true, new Vector2(1366f, 768f), "768");
            CheckArchive(true, new Vector2(840f, 640f), "compact");
        }
        catch (Exception e) { Failures.Add("exception: " + e); }
        File.WriteAllText("result.txt", Failures.Count == 0
            ? "PASS: EVN console (empty, active, superevent + decision board, late fill, page switch) and field archive window " +
              "(four sections, three window sizes) render without overlap or overflow; " + checkedTexts + " text checks over " +
              checkedFlows + " flows. Game adapters are stubbed; in-game acceptance remains required."
            : "FAIL (" + Failures.Count + "):\n" + string.Join("\n", Failures.GetRange(0, Math.Min(60, Failures.Count))));
        EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
    }

    // ---- Fixtures ------------------------------------------------------------------------------------------

    private sealed class Rig
    {
        public readonly GameObject CameraGo;
        public readonly Camera Cam;
        public readonly Canvas Canvas;
        public readonly RenderTexture Target;

        public Rig()
        {
            CameraGo = new GameObject("cam", typeof(Camera));
            Cam = CameraGo.GetComponent<Camera>();
            Cam.orthographic = true;
            Cam.orthographicSize = H / 2f;
            Cam.transform.position = new Vector3(0f, 0f, -10f);
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.07f, 0.09f, 0.11f);
            Target = new RenderTexture(W, H, 24);
            Cam.targetTexture = Target;
            Canvas = new GameObject("canvas", typeof(Canvas)).GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)Canvas.transform).sizeDelta = new Vector2(W, H);
        }

        /// <summary>Saves a crop (top-left pixel coordinates) of the rendered frame.</summary>
        public void Save(string name, int x, int y, int w, int h)
        {
            Canvas.ForceUpdateCanvases();
            foreach (TMP_Text t in Canvas.GetComponentsInChildren<TMP_Text>()) t.ForceMeshUpdate();
            Cam.Render();
            RenderTexture.active = Target;
            x = Mathf.Clamp(x, 0, W - 1);
            y = Mathf.Clamp(y, 0, H - 1);
            w = Mathf.Min(w, W - x);
            h = Mathf.Min(h, H - y);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(x, H - y - h, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine("renders", name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(tex);
        }

        public void Dispose()
        {
            Cam.targetTexture = null;
            Object.DestroyImmediate(Canvas.gameObject);
            Object.DestroyImmediate(CameraGo);
            Object.DestroyImmediate(Target);
        }
    }

    private sealed class PanelFixture
    {
        public Rig Rig;
        public EventsMfdPanel Panel;
        public EventsManager Events;
        public AvConsole Console;
        public MissionManager Mission;
        public float Height;
    }

    private static EventDefinition Def(string id) => Array.Find(EventCatalog.All, e => e.Id == id);
    private static EventDefinition FirstSuper() => Array.Find(EventCatalog.All, e => e.IsSuper);

    private static ActiveEventView View(EventDefinition d, float start, float end, bool resolved = true)
    {
        float mult = EventSelector.EffectiveSupportMultiplier(d.SupportCostMultiplier, 1f);
        var steps = new ActiveEventStep[d.Script.Length];
        for (int i = 0; i < steps.Length; i++) steps[i] = new ActiveEventStep(d.Script[i].Label, d.Script[i].AtSeconds);
        int percent = Mathf.RoundToInt((d.SupportCooldownMultiplier - 1f) * 100f);
        return new ActiveEventView(d.Id, d.Title, d.FlavorText, EventCatalog.CategoryLabel(d.Category),
            EventCatalog.TierLabel(d.Tier), EventCatalog.TargetLabel(d.Target), d.IsSuper, d.IconKey,
            EventSelector.EffectSummary(mult), steps, start, end, resolved,
            percent == 0 ? "" : "SUPPORT RESET " + (percent > 0 ? "+" : "") + percent + "%");
    }

    private static EventDecisionQuote Quote(EventsManager events, EventResponseKind kind)
    {
        float multiplier = events.LocalBaseMultiplier;
        int cost = kind == EventResponseKind.Treasury ? EventSelector.TreasuryCost(multiplier)
            : kind == EventResponseKind.Contain || kind == EventResponseKind.Leverage ? EventSelector.ResponseCost(multiplier) : 0;
        bool shared = kind == EventResponseKind.Treasury || kind == EventResponseKind.Contract;
        string unit = kind == EventResponseKind.Treasury ? "M FUNDS" : cost > 0 ? "ALLOC" : "NO COST";
        string reason = "";
        if (kind == EventResponseKind.Contract) reason = "COMPLETE A FACTION CONTRACT";
        else if (kind == EventResponseKind.Perk) reason = "RECON QUALIFICATION REQUIRED";
        if (events.LocalResponse != EventResponseKind.None) reason = "RESPONSE ALREADY ACTIVE";
        return new EventDecisionQuote(EventsManager.ResponseLabel(kind), cost, unit,
            EventSelector.ApplyResponse(multiplier, kind), reason.Length == 0, reason, shared);
    }

    private static PanelFixture BuildPanel(float height)
    {
        var f = new PanelFixture { Rig = new Rig(), Height = height };
        f.Mission = new MissionManager { MissionTime = 600f };
        NetworkSceneSingleton<MissionManager>.i = f.Mission;
        Encyclopedia.i = new Encyclopedia();
        for (int i = 0; i < 13; i++) Encyclopedia.i.aircraft.Add(new AircraftDefinition { unitName = "Airframe " + i });

        var settings = new EventsSettings(new ConfigFile(Path.GetFullPath("events-" + Guid.NewGuid().ToString("N") + ".cfg"), false));
        f.Events = new GameObject("EventsManager").AddComponent<EventsManager>();
        f.Events.QuoteFn = kind => Quote(f.Events, kind);
        f.Panel = new GameObject("EvnPanel").AddComponent<EventsMfdPanel>();
        f.Panel.Configure(settings, f.Events, null);

        var template = new GameObject("Template", typeof(RectTransform), typeof(MFDScreen)).GetComponent<MFDScreen>();
        var templateRect = (RectTransform)template.transform;
        RectTransform mount = AvLay.Child((RectTransform)f.Rig.Canvas.transform, "MfdColumn");
        AvLay.Place(mount, 0f, 0f, 480f, height);
        templateRect.SetParent(mount, false);
        templateRect.anchorMin = templateRect.anchorMax = templateRect.pivot = new Vector2(0f, 1f);
        templateRect.sizeDelta = new Vector2(480f, 596f);

        var bezelGo = new GameObject("Bezel", typeof(RectTransform), typeof(Image), typeof(Button));
        bezelGo.transform.SetParent(f.Rig.Canvas.transform, false);
        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(bezelGo.transform, false);
        var highlight = new GameObject("Highlight", typeof(RectTransform), typeof(Image));
        highlight.transform.SetParent(bezelGo.transform, false);

        MethodInfo build = typeof(EventsMfdPanel).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance);
        var screen = (MFDScreen)build.Invoke(f.Panel, new object[] { template, bezelGo.GetComponent<Button>() });
        Check(screen != null, "EVN Build must return a screen");
        f.Console = (AvConsole)typeof(EventsMfdPanel).GetField("console", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f.Panel);
        Check(f.Console != null, "EVN console must exist after Build");
        // Regression: clamping console.Root after building baked a negative offset into it and shifted the page left.
        Check(f.Console.Root.anchoredPosition == Vector2.zero,
            "console root must sit at the screen root's origin, not carry a clamp offset (" + f.Console.Root.anchoredPosition + ")");
        return f;
    }

    private static void RefreshPanel(PanelFixture f)
    {
        typeof(EventsMfdPanel).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f.Panel, null);
        for (int i = 0; i < 3; i++) f.Console.Ticker.TickNow();
    }

    // ---- Console scenarios ---------------------------------------------------------------------------------

    private static void CheckConsole(float height)
    {
        PanelFixture f = BuildPanel(height);
        try
        {
            EventsManager ev = f.Events;

            // 1. Quiet theater, nothing known: one compact hero card, no response desk, empty log note.
            RefreshPanel(f);
            Snapshot(f, "empty");
            Check(!ResponseSectionShown(f), "the response desk must be hidden while no dispatch is live");

            // 2. Content arrives after Finish(): a medium dispatch, ground data, history, a decision board.
            ev.Balance = new TheaterBalance(2, 6, 4, 11, 22, 0);
            ev.SupersFired = 1;
            ev.HistoryList.Add(View(Def("monsoon_season"), 120f, 240f));
            ev.HistoryList.Add(View(Def("war_bond_drive"), 250f, 400f));
            ev.HistoryList.Add(View(Def("global_supply_chain_crisis"), 420f, 560f));
            ev.Current = View(Def("industrial_surge"), 560f, 860f);
            ev.SupportCostMultiplier = EventSelector.EffectiveSupportMultiplier(Def("industrial_surge").SupportCostMultiplier, 1f);
            ev.LocalBaseMultiplier = ev.SupportCostMultiplier;
            ev.LocalSupportCooldownMultiplier = Def("industrial_surge").SupportCooldownMultiplier;
            ev.LocalTargeted = true;
            RefreshPanel(f);
            Snapshot(f, "active");
            Check(ResponseSectionShown(f), "a priced dispatch must show the response desk");

            // Clicking CHOOSE asks the manager for the route; a locked card does nothing.
            f.Console.SetPage(0);
            AvControl[] choose = Array.FindAll(f.Console.Root.GetComponentsInChildren<AvControl>(true), c => c.Label == "CHOOSE");
            Check(choose.Length >= 1, "an available route must offer CHOOSE");
            Click(choose[0]);
            Check(ev.Requests.Count == 1, "CHOOSE must request the route once (" + ev.Requests.Count + ")");
            AvControl[] locked = Array.FindAll(f.Console.Root.GetComponentsInChildren<AvControl>(true), c => c.Label == "LOCKED");
            Check(locked.Length >= 1, "an unavailable route must read LOCKED");
            Click(locked[0]);
            Check(ev.Requests.Count == 1, "a locked route must not send a request");

            // 3. Penalty superevent with a script, ending soon, a route already chosen.
            f.Mission.MissionTime = 1000f;
            EventDefinition super = FirstSuper();
            ev.Current = View(super, 700f, 1045f);
            float superMultiplier = EventSelector.EffectiveSupportMultiplier(super.SupportCostMultiplier, 1f);
            ev.SupportCostMultiplier = superMultiplier;
            ev.LocalBaseMultiplier = superMultiplier;
            ev.LocalSupportCooldownMultiplier = super.SupportCooldownMultiplier;
            ev.LocalResponse = EventSelector.ResponseKind(superMultiplier) == EventResponseKind.Leverage
                ? EventResponseKind.Leverage : EventResponseKind.Contain;
            RefreshPanel(f);
            Snapshot(f, "super");
            AvControl[] inForce = Array.FindAll(f.Console.Root.GetComponentsInChildren<AvControl>(true), c => c.Label == "IN FORCE");
            Check(inForce.Length >= 1, "the chosen route must read IN FORCE");

            // Only the nearest unexecuted beat is NEXT; later orders remain QUEUED.
            f.Mission.MissionTime = 702f;
            RefreshPanel(f);
            Snapshot(f, "script-upcoming");
            var scripts = f.Console.Page(0).Content.GetComponentsInChildren<TMP_Text>(true);
            Check(Array.FindAll(scripts, t => t.text.Contains("[NEXT]")).Length == 1,
                "exactly one upcoming scripted order must be identified as NEXT");
            Check(Array.Exists(scripts, t => t.text.Contains("[QUEUED]")), "later scripted orders must read QUEUED");
            ev.Current = View(super, 700f, 1045f, false);
            RefreshPanel(f);
            Snapshot(f, "target-lost");
            Check(Array.Exists(scripts, t => t.text.Contains("[CANCELLED]")), "unresolved target must cancel the script");
            Check(Array.Exists(scripts, t => t.text.Contains("TARGET LOST")),
                "the live hero must rebind its cancellation notice when target resolution changes");

            // 4. Aimed at the other side: the desk collapses to one note, the hero says so.
            ev.LocalResponse = EventResponseKind.None;
            ev.LocalTargeted = false;
            ev.Current = View(Def("frontline_overstretch") ?? super, 900f, 1200f);
            ev.SupportCostMultiplier = 1f;
            ev.LocalBaseMultiplier = 1f;
            ev.LocalSupportCooldownMultiplier = 1f;
            RefreshPanel(f);
            Snapshot(f, "other-side");

            // 5. The dispatch ends: the desk closes and the gap closes with it.
            ev.Current = null;
            f.Mission.MissionTime = 1300f;
            RefreshPanel(f);
            Snapshot(f, "ended");
            Check(!ResponseSectionShown(f), "the response desk must close when the dispatch ends");
        }
        finally { f.Rig.Dispose(); Object.DestroyImmediate(f.Panel.gameObject); Object.DestroyImmediate(f.Events.gameObject); }
    }

    private static bool ResponseSectionShown(PanelFixture f)
    {
        FieldInfo field = typeof(EventsMfdPanel).GetField("responseSection", BindingFlags.NonPublic | BindingFlags.Instance);
        var section = (AvSection)field.GetValue(f.Panel);
        return section != null && section.Rect.gameObject.activeSelf;
    }

    /// <summary>Renders both console pages and runs the layout / text gate on each.</summary>
    private static void Snapshot(PanelFixture f, string name)
    {
        for (int page = 0; page < f.Console.PageCount; page++)
        {
            f.Console.SetPage(page);
            for (int i = 0; i < 3; i++) f.Console.Ticker.TickNow();
            Canvas.ForceUpdateCanvases();
            RectTransform rootRect = (RectTransform)f.Console.Root.parent;
            Vector3[] corners = new Vector3[4];
            rootRect.GetWorldCorners(corners);
            int left = Mathf.RoundToInt(corners[0].x + W / 2f), top = Mathf.RoundToInt(H / 2f - corners[1].y);
            string prefix = "EVN-" + name + "-" + f.Height + "-p" + (page + 1);
            ScrollRect scroll = f.Console.Page(page).Content.GetComponentInParent<ScrollRect>();
            float[] positions = scroll != null && f.Console.Page(page).ContentHeight > scroll.viewport.rect.height + 1f
                ? new[] { 1f, .5f, 0f } : new[] { 1f };
            foreach (float position in positions)
            {
                if (scroll != null) scroll.verticalNormalizedPosition = position;
                string suffix = position == 1f ? "-top" : position == 0f ? "-bottom" : "-mid";
                f.Rig.Save(prefix + suffix, left, top, 480, Mathf.RoundToInt(rootRect.rect.height));
            }
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            string where = "console/" + name + "/" + f.Height + "/p" + (page + 1);
            Gate(f.Console.Root, where, true);
            CheckFlow(f.Console.Page(page), where);
        }
        f.Console.SetPage(0);
    }

    // ---- Archive scenarios ---------------------------------------------------------------------------------

    private static GameObject AircraftPrefab(int seed)
    {
        var root = new GameObject("Prefab" + seed);
        root.SetActive(false);
        GameObject fuselage = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fuselage.transform.SetParent(root.transform, false);
        fuselage.transform.localScale = new Vector3(1.4f, 1.2f, 12f + seed % 4);
        GameObject wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wing.transform.SetParent(root.transform, false);
        wing.transform.localScale = new Vector3(11f, 0.2f, 3.5f);
        GameObject tail = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tail.transform.SetParent(root.transform, false);
        tail.transform.localPosition = new Vector3(0f, 1.4f, -5f);
        tail.transform.localScale = new Vector3(0.2f, 2.6f, 1.8f);
        foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        return root;
    }

    private static void CheckArchive(bool withAircraft, Vector2 units, string tag)
    {
        var rig = new Rig();
        var prefabs = new List<GameObject>();
        Encyclopedia.i = new Encyclopedia();
        if (withAircraft)
        {
            string[] names = { "A-19 Brawler", "Alkyon AB-4", "CI-22 Cricket", "EW-25 Medusa", "FS-12 Revoker", "FS-20 Vortex",
                "KR-67 Ifrit", "SAH-46 Chicane", "T/A-30 Compass", "UH-80 Ibis", "VL-49 Tarantula", "CS-3 Barrier" };
            for (int i = 0; i < names.Length; i++)
            {
                GameObject prefab = AircraftPrefab(i);
                prefabs.Add(prefab);
                Encyclopedia.i.aircraft.Add(new AircraftDefinition
                {
                    unitName = names[i], code = names[i].Split(' ')[0], unitPrefab = prefab,
                    description = "A stub airframe used to exercise the archive layout. It carries a description long enough to " +
                        "wrap over several lines in the detail pane so that the prose block, not the figures, decides how tall " +
                        "the scrollable page becomes when the record is selected.",
                    aircraftInfo = new AircraftInfo { maxSpeed = 310f + i * 12f, stallSpeed = 42f + i, emptyWeight = 6500f + i * 830f },
                });
            }
        }
        EventDeskArchive archive = null;
        try
        {
            archive = EventDeskArchive.Create(rig.Canvas.transform, units);
            AvWindow window = archive.Window;
            RectTransform root = window.Root;

            void Frame(string name, string where)
            {
                for (int i = 0; i < 3; i++) window.Ticker.TickNow();
                Canvas.ForceUpdateCanvases();
                Vector3[] c = new Vector3[4];
                root.GetWorldCorners(c);
                int x = Mathf.RoundToInt(c[0].x + W / 2f), y = Mathf.RoundToInt(H / 2f - c[1].y);
                string renderName = "ARCHIVE-" + tag + "-" + name;
                int renderW = Mathf.RoundToInt(c[2].x - c[0].x) + 12, renderH = Mathf.RoundToInt(c[1].y - c[0].y) + 12;
                rig.Save(renderName, x - 6, y - 6, renderW, renderH);
                Gate(root, where, false);
                var panes = (ArchivePanesPart)FindPanes(window.Body);
                CheckFlow(window.Body, where + "/body");
                CheckFlow(panes.Left.Flow, where + "/index");
                CheckFlow(panes.Right.Flow, where + "/detail");
                ScrollRect detailScroll = panes.Right.Root.GetComponent<ScrollRect>();
                if (detailScroll.content.rect.height > detailScroll.viewport.rect.height + 1f)
                {
                    foreach (float position in new[] { .5f, 0f })
                    {
                        detailScroll.verticalNormalizedPosition = position;
                        rig.Save(renderName + (position == 0f ? "-bottom" : "-mid"), x - 6, y - 6, renderW, renderH);
                    }
                    detailScroll.verticalNormalizedPosition = 1f;
                }
            }

            for (int section = 0; section < 4; section++)
            {
                if (section == 0 && !withAircraft)
                {
                    archive.Show(0);
                    Frame("aircraft-empty", "archive/" + tag + "/aircraft-empty");
                    continue;
                }
                archive.Show(section);
                Frame("section" + (section + 1), "archive/" + tag + "/section" + (section + 1));
            }

            // Events: a scripted superevent (timed orders) and the last record.
            int superIndex = Array.FindIndex(EventCatalog.All, e => e.IsSuper);
            archive.Show(1, superIndex);
            Frame("super", "archive/" + tag + "/super");
            archive.Show(1, EventCatalog.All.Length - 1);
            Frame("last", "archive/" + tag + "/last");

            // Window sits inside the screen and its body needs no outer scroll (the panes carry it).
            Rect r = root.rect;
            Check(r.height <= units.y - 2f * 40f + 1f && r.width <= units.x - 2f * 40f + 1f,
                "archive window " + r.size + " must fit the screen " + units + " with a margin");
            float bodyH = r.height - 30f - AvGridTokens.Footer;
            Check(window.Body.ContentHeight <= bodyH + 1f,
                "archive body content " + window.Body.ContentHeight.ToString("0") + " must fit its viewport " + bodyH.ToString("0"));
            Check(archive.transform.parent == rig.Canvas.transform && archive.GetComponentInParent<Canvas>() != null,
                "the archive must live under a canvas, not become a root canvas of its own");

            // Tabs and the list are clickable.
            Click(Array.Find(root.GetComponentsInChildren<AvControl>(true), c => c.Label == "MANUAL"));
            for (int i = 0; i < 3; i++) window.Ticker.TickNow();
            Check(Array.Exists(root.GetComponentsInChildren<TMP_Text>(true), t => t.text == "READING A DISPATCH"),
                "MANUAL tab must list the field manual");
        }
        finally
        {
            if (archive != null) Object.DestroyImmediate(archive.gameObject);
            foreach (GameObject p in prefabs) Object.DestroyImmediate(p);
            rig.Dispose();
        }
    }

    private static AvPart FindPanes(AvFlow flow)
    {
        var lines = (List<AvPart[]>)typeof(AvFlow).GetField("lines", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(flow);
        foreach (AvPart[] line in lines) foreach (AvPart p in line) if (p is ArchivePanesPart) return p;
        return null;
    }

    // ---- Gate ----------------------------------------------------------------------------------------------

    private static void Check(bool condition, string message) { if (!condition) Failures.Add(message); }

    private static void Click(AvControl control)
    {
        if (control == null) { Failures.Add("click: control not found"); return; }
        control.GetComponentInChildren<AvHit>(true).OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
    }

    /// <summary>Lines of a flow, in order: no shown part starts above the previous line's bottom, none is stale.</summary>
    private static void CheckFlow(AvFlow flow, string where)
    {
        checkedFlows++;
        var lines = (List<AvPart[]>)typeof(AvFlow).GetField("lines", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(flow);
        float previousBottom = float.NegativeInfinity;
        foreach (AvPart[] line in lines)
        {
            float lineBottom = float.NegativeInfinity;
            bool any = false;
            foreach (AvPart p in line)
            {
                if (p.Rect == null) continue;
                bool shown = p.Rect.gameObject.activeSelf;
                if (shown != p.PlacedShown) Failures.Add(where + ": " + p.Rect.name + " visibility changed without a re-layout");
                if (!shown) continue;
                any = true;
                float top = -p.Rect.anchoredPosition.y, bottom = top + p.Rect.rect.height;
                if (top < previousBottom - 0.5f)
                    Failures.Add(where + ": " + p.Rect.name + " (top " + top.ToString("0") + ") overlaps the line above (bottom " + previousBottom.ToString("0") + ")");
                float now = p.Measure(p.PlacedWidth);
                if (Mathf.Abs(now - p.PlacedHeight) > 0.6f)
                    Failures.Add(where + ": " + p.Rect.name + " is stale (measures " + now.ToString("0.0") + ", placed " + p.PlacedHeight.ToString("0.0") + ")");
                lineBottom = Mathf.Max(lineBottom, bottom);
            }
            if (any) previousBottom = lineBottom;
        }
    }

    private static void Gate(RectTransform root, string where, bool console)
    {
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false))
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
            }
            ScrollRect scroll = t.GetComponentInParent<ScrollRect>();
            if (scroll != null)
            {
                var corners = new Vector3[4];
                t.rectTransform.GetWorldCorners(corners);
                RectTransform scrollRect = (RectTransform)scroll.transform;
                float right = scrollRect.InverseTransformPoint(corners[2]).x;
                float limit = scrollRect.rect.width - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;
                if (right > limit) Failures.Add(where + ": enters the gutter (" + right.ToString("0") + " > " + limit.ToString("0") + ") '" + t.text + "'");
            }
            if (!icon && t.color.a > 0.5f)
            {
                Color back = BackgroundOf(t, ground);
                float contrast = Rgba.Contrast(t.color.ToRgba().WithAlpha(1f).Over(back.ToRgba()), back.ToRgba());
                if (contrast < 4.5f) Failures.Add(where + ": contrast " + contrast.ToString("0.00") + " for '" + t.text + "' (" + t.name + ")");
            }
        }
        if (console)
            foreach (AvControl tab in root.GetComponentsInChildren<AvControl>(true))
                if (tab.transform.parent != null && tab.transform.parent.name == "Tabs" && tab.transform.Find("Label") != null
                    && tab.GetComponentsInChildren<TMP_Text>(true).Length < 2)
                    Failures.Add(where + ": tab without icon " + tab.name);
        foreach (Transform s in root.GetComponentsInChildren<Transform>(true))
            if (s.name.StartsWith("Section ") && s.Find("Icon None") != null)
                Failures.Add(where + ": section without icon " + s.name);
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
}
#endif

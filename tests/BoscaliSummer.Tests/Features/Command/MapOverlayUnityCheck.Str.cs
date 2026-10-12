#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Presentation;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using BoscaliSummer.Tests.Features.Command;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// The STR map overlay (annex 05 P10, P12 item 7) built from the production assembly over a synthetic map: the real StrMapOverlay, StrMapTag, StrMapCard and chrome, the
// real StrViewBuilder over a StrFixtures feed, and either the flat sheet's own scale or a fake relief projection (tilt and shear, so rings come out as ellipses).
public static partial class MapOverlayUnityCheck
{
    private static bool strOnly;

    public static void RunStr()
    {
        strOnly = true;
        Run();
    }

    // ---- a hub the overlay talks to: the real builder, gate and runner over a recording fake of the verbs --------------------------------------------------------

    private sealed class FakeVerbs : IStrVerbs
    {
        public readonly List<string> Sent = new List<string>();
        public bool Pick(int proposalId, int revision) { Sent.Add("pick " + proposalId); return true; }
        public bool Cancel(int operationId, int revision) { Sent.Add("cancel " + operationId); return true; }
        public bool Posture(TheaterWarPosture posture) { Sent.Add("posture " + posture); return true; }
        public void Accept(int contractId) { Sent.Add("accept " + contractId); }
        public bool SendWing(int contractId, out string answer) { Sent.Add("wing " + contractId); answer = "WING TASKED"; return true; }
        public bool Recall(out string answer) { Sent.Add("recall"); answer = "WING RECALLED"; return true; }
    }

    private sealed class StrOverlayHub : IStrHub
    {
        private readonly StrFeeds feeds;
        private readonly StrViewModel model = new StrViewModel();
        private readonly StrOrdinals plat = new StrOrdinals("P"), sort = new StrOrdinals("S");
        private StrUiState ui;
        public readonly FakeVerbs Verbs = new FakeVerbs();
        public int Focused;

        public StrOverlayHub(StrFeeds f)
        {
            feeds = f;
            ui.ShowFronts = ui.ShowOperation = ui.ShowSorties = true;
            Tick();
        }

        public StrViewModel Current => model;
        public int Revision => model.Revision;
        public StrUiState Ui => ui;
        private void Tick() => StrViewBuilder.Build(model, in feeds, in ui, plat, sort, StrFixtures.Now);
        public void Select(StrItemId id, bool focusMap) { ui.Selected = id; if (focusMap) Focused++; Tick(); }
        public void Hover(StrItemId id) { ui.Hover = id; Tick(); }
        public void Unhover(StrItemId id) { if (ui.Hover == id) { ui.Hover = StrItemId.None; Tick(); } }
        public void ClearSelection() { ui.Selected = StrItemId.None; Tick(); }
        public StrAct Run(StrActionKind kind, StrItemId id, int arg = 0)
        {
            StrAct act = StrActions.Run(kind, id, arg, model, Verbs, ref ui, StrFixtures.Now);
            Tick();
            return act;
        }
        public void SetLayer(StrLayers layer, bool on)
        {
            if ((layer & StrLayers.Fronts) != 0) ui.ShowFronts = on;
            if ((layer & StrLayers.Operation) != 0) ui.ShowOperation = on;
            if ((layer & StrLayers.Sorties) != 0) ui.ShowSorties = on;
            Tick();
        }
    }

    // The relief's projection, faked: a tilted, sheared sheet, so a ring of metres comes out an ellipse and a straight line a straight one.
    private sealed class TiltProjection : IMapProjection
    {
        public const float Factor = 0.06f;
        public bool IsActive => true;
        public int Revision { get; set; } = 1;
        public bool TryProject(float wx, float wz, out float x, out float y)
        {
            x = (wx * 0.94f + wz * 0.24f) * Factor;
            y = wz * 0.62f * Factor;
            return true;
        }
        public bool TryScale(float wx, float wz, float metres, out float mapUnits) { mapUnits = metres * Factor * 0.78f; return true; }
    }

    /// <summary>The fullest board the builder can make: every cap filled (8 fronts, 3 proposals, 5 tied sorties, 6 posts, 8 group marks, a depot).</summary>
    private static StrFeeds FullFeeds()
    {
        StrFeeds f = StrFixtures.Feeds("rich");
        var fronts = new List<TheaterFrontView> { StrFixtures.Front("@1,-2", "KESTREL CROSSING", 2400f, -1800f, 0.62f, 0.12f, TheaterFrontStatus.InContact) };
        string[] names = { "NORTH RIDGE", "WEST DEPOT", "EASTERN PASS", "HARBOR", "AURELIA", "SALT FLATS", "OLD MINE" };
        for (int i = 0; i < 7; i++)
            fronts.Add(StrFixtures.Front("@" + (10 + i) + ",0", names[i], -1500f + 1400f * i, 800f + 700f * ((i * 3) % 7), 0.12f * i, 0.04f * (i % 3 - 1), TheaterFrontStatus.UnderPressure, observed: i != 5, observedAge: 400f));
        f.War.Fronts = fronts.ToArray();
        var marks = new TheaterForceMark[8];
        for (int i = 0; i < 8; i++) marks[i] = new TheaterForceMark { X = 2400f - 380f * (i + 1), Z = -1800f + 240f * (i + 1), Stage = 3, Alive = 4 };
        f.War.Operation.Forces.Marks = marks;
        var cards = new SecondaryObjectiveView[5];
        for (int i = 0; i < 5; i++)
            cards[i] = StrFixtures.Card(40 + i, "SORTIE " + i, ObjectiveFamily.Strike, offered: i % 2 == 0, active: i % 2 == 1, secondsRemaining: 300f + i, x: 1500f + 800f * i, z: -1500f + 1000f * i);
        f.Sorties = cards;
        var posts = new StrPostIn[6];
        for (int i = 0; i < 6; i++) posts[i] = StrFixtures.Post(20 + i, "OFFICER " + i, 1000 + 100 * i, 30f * i, 800f + 1300f * i, 6200f - 900f * i);
        f.Posts = posts; f.PostCount = 6;
        return StrFixtures.Stamped(f);
    }

    /// <summary>The rich fixture with its three sorties on three different sites (the fixture puts them all on one), as a real board has them.</summary>
    private static StrFeeds RichSpread()
    {
        StrFeeds f = StrFixtures.Feeds("rich");
        f.Sorties = new[]
        {
            StrFixtures.Card(31, "STRIKE HARBOR SAM", ObjectiveFamily.Strike, true, false, 540f, x: 3600f, z: -700f),
            StrFixtures.Card(32, "COVER THE ADVANCE", ObjectiveFamily.AirCover, true, false, 720f, x: 900f, z: -2600f),
            StrFixtures.Card(33, "SCOUT THE RIDGE", ObjectiveFamily.Reconnaissance, false, true, 580f, x: 800f, z: 400f, acceptedBy: "Viper 1-1"),
        };
        return StrFixtures.Stamped(f);
    }

    private static void StrCases()
    {
        foreach (Vector2 size in new[] { new Vector2(1280, 720), new Vector2(1920, 1080) })
            foreach (bool relief in new[] { false, true })
            {
                StrCase("rich", size, relief, 1f, null);
                StrCase("rich", size, relief, 1f, "sortie.offered");
                StrCase("proposals", size, relief, 1f, "prop.1");
                StrCase("full", size, relief, 1f, null);
            }
        StrCase("rich", new Vector2(1280, 720), false, 1.8f, "post.1");
        StrCase("rich", new Vector2(1280, 720), false, 0.7f, null);
        StrCase("reduced", new Vector2(1280, 720), false, 1f, null);
        StrCase("staffoff", new Vector2(1280, 720), false, 1f, null);
        StrInteractions();
        StrNegativeControls();
    }

    // The instruments must be able to fail: put a tag on an icon, put a blocker over a tag, push a tag out of the view, and the same measurements the gates use must say so.
    private static void StrNegativeControls()
    {
        capture = "str-negative-controls";
        StrScene s = StrBuild("rich", new Vector2(1280, 720), false, 1f);
        try
        {
            StrTick(s, 3); StrAddIcons(s); StrTick(s);
            s.Cam.Render();
            StrMapTag tag = null;
            foreach (StrMapTag t in s.Overlay.TagPool) if (t.gameObject.activeSelf) { tag = t; break; }
            Check(tag != null && s.IconScreen.Count > 0, "self-test: there is a tag and a native mark to abuse");
            if (tag == null || s.IconScreen.Count == 0) return;
            tag.PlaceAt(s.IconScreen[0], s.Cam);
            Canvas.ForceUpdateCanvases();
            Rect onIcon = StrScreenRect(s, tag.Root);
            Check(onIcon.Overlaps(new Rect(s.IconScreen[0].x - 17f, s.IconScreen[0].y - 17f, 34f, 34f)), "self-test: the icon clearance measurement catches a tag moved onto an icon");
            tag.PlaceAt(new Vector2(-400f, -400f), s.Cam);
            Canvas.ForceUpdateCanvases();
            Rect view = StrScreenRect(s, s.Viewport);
            Check(!view.Contains(StrScreenRect(s, tag.Root).center), "self-test: the viewport measurement catches a tag pushed off the map");
            tag.PlaceAt(new Vector2(300f, 300f), s.Cam);
            var blocker = new GameObject("Blocker", typeof(RectTransform), typeof(Image));
            blocker.transform.SetParent(s.Canvas.transform, false);
            ((RectTransform)blocker.transform).sizeDelta = new Vector2(1280f, 720f);
            blocker.GetComponent<Image>().raycastTarget = true;
            EditorRegister(blocker.GetComponent<Image>());
            Canvas.ForceUpdateCanvases(); s.Cam.Render();
            StrFirstHit(s, RectTransformUtility.WorldToScreenPoint(s.Cam, tag.Root.position), out GameObject top);
            Check(top != null && top.GetComponentInParent<StrMapTag>() == null, "self-test: the first-hit measurement catches a blocker laid over a tag");
            var small = new AvQuadBuffer(2);
            small.Add(0, 0, 1, 0, 1, 1, 0, 1, default, default, default, default); small.Add(0, 0, 1, 0, 1, 1, 0, 1, default, default, default, default); small.Add(0, 0, 1, 0, 1, 1, 0, 1, default, default, default, default);
            Check(small.Overflowed, "self-test: a buffer past its capacity reports overflow, the flag the overlay gate reads");
        }
        finally { StrDispose(s); }
    }

    private sealed class StrScene
    {
        public GameObject Canvas, Services, Camera;
        public Camera Cam;
        public RenderTexture Target;
        public DynamicMap Map;
        public RectTransform Viewport, Image;
        public StrMapOverlay Overlay;
        public StrOverlayHub Hub;
        public GraphicRaycaster Raycaster;
        public GameObject EventSystemObject;
        public readonly List<Vector2> IconScreen = new List<Vector2>();
        public readonly List<Image> Icons = new List<Image>();
        public float Now = 100f;
        public Vector2 Size;
    }

    private static StrScene StrBuild(string fixture, Vector2 size, bool relief, float zoom)
    {
        var s = new StrScene { Size = size };
        s.Camera = new GameObject("UI camera", typeof(Camera));
        s.Cam = s.Camera.GetComponent<Camera>(); s.Cam.enabled = false;
        s.Cam.orthographic = true; s.Cam.orthographicSize = size.y * .5f; s.Cam.transform.position = new Vector3(0f, 0f, -1000f);
        s.Cam.clearFlags = CameraClearFlags.SolidColor; s.Cam.backgroundColor = new Color(.06f, .08f, .09f); s.Cam.farClipPlane = 2000f;
        s.Target = new RenderTexture((int)size.x, (int)size.y, 24); s.Cam.targetTexture = s.Target;
        s.Canvas = new GameObject("Map canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        Canvas canvas = s.Canvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = s.Cam; ((RectTransform)s.Canvas.transform).sizeDelta = size;
        s.Raycaster = s.Canvas.GetComponent<GraphicRaycaster>();

        var viewport = new GameObject("Native map viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(s.Canvas.transform, false);
        s.Viewport = (RectTransform)viewport.transform; s.Viewport.sizeDelta = size;
        Image back = viewport.GetComponent<Image>(); back.color = new Color(.05f, .09f, .12f); back.raycastTarget = true;

        var imageGo = new GameObject("Native map image", typeof(RectTransform), typeof(RawImage));
        imageGo.transform.SetParent(viewport.transform, false);
        s.Image = (RectTransform)imageGo.transform; s.Image.sizeDelta = new Vector2(1800f, 900f);
        float scale = size.y / 720f * zoom;
        s.Image.localScale = Vector3.one * scale;
        // The cluster of the fixtures sits near world (3000, 2500) m; centre the sheet on it.
        s.Image.anchoredPosition = new Vector2(-200f, -150f) * scale;
        RawImage raster = imageGo.GetComponent<RawImage>(); raster.texture = Terrain(); raster.raycastTarget = false;

        s.Services = new GameObject("Synthetic services");
        s.Map = s.Services.AddComponent<PreviewDynamicMap>(); s.Map.enabled = false;
        s.Map.mapImage = imageGo; s.Map.iconLayer = imageGo; s.Map.mapBackground = back; s.Map.mapDisplayFactor = relief ? TiltProjection.Factor : TiltProjection.Factor;
        s.Map.HQ = s.Services.AddComponent<PreviewFactionHQ>(); s.Map.HQ.enabled = false;
        SceneSingleton<DynamicMap>.i = s.Map; Set(typeof(DynamicMap), "<mapMaximized>k__BackingField", true);

        ModuleServices.Active = new ServiceRegistry();
        if (relief) ModuleServices.Active.Add<IMapProjection>(new TiltProjection());

        s.EventSystemObject = new GameObject("Event system", typeof(EventSystem));
        EventSystem.current = s.EventSystemObject.GetComponent<EventSystem>();

        StrFeeds feeds = fixture == "full" ? FullFeeds() : fixture == "rich" ? RichSpread() : StrFixtures.Feeds(fixture);
        s.Hub = new StrOverlayHub(feeds);
        var overlayGo = new GameObject("STR overlay");
        s.Overlay = overlayGo.AddComponent<StrMapOverlay>(); s.Overlay.enabled = false; s.Overlay.HubOverride = s.Hub;
        overlayGo.transform.SetParent(s.Services.transform, false);
        return s;
    }

    // A synthetic theatre to judge contrast on: sea to the west, hostile land to the east and a cream front between them (frame C's palette).
    private static Texture2D Terrain()
    {
        const int w = 256, h = 128;
        var t = new Texture2D(w, h, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float line = 0.46f + 0.05f * Mathf.Sin(y * 0.09f) + (y - h * 0.5f) * 0.0008f, d = (x / (float)w) - line;
                float noise = Mathf.PerlinNoise(x * 0.07f, y * 0.07f) * 0.06f;
                Color c = d < 0f ? new Color(0.07f + noise * 0.4f, 0.19f + noise, 0.31f + noise) : new Color(0.27f + noise, 0.12f + noise * 0.4f, 0.11f + noise * 0.3f);
                if (Mathf.Abs(d) < 0.006f) c = new Color(0.82f, 0.88f, 0.9f);
                t.SetPixel(x, y, c);
            }
        t.Apply(false);
        return t;
    }

    private static void StrTick(StrScene s, int times = 1, bool register = true)
    {
        for (int i = 0; i < times; i++) { s.Now += 0.1f; s.Overlay.Tick(s.Now); }
        if (!register) return;
        foreach (Graphic g in s.Canvas.GetComponentsInChildren<Graphic>(true)) EditorRegister(g);
        Canvas.ForceUpdateCanvases();
    }

    private static StrItemId StrPick(StrScene s, string what)
    {
        StrViewModel vm = s.Hub.Current;
        switch (what)
        {
            case "prop.1": return vm.ProposalCount > 0 ? vm.Proposals[0].Id : StrItemId.None;
            case "post.1": return vm.PostCount > 0 ? vm.Posts[0].Id : StrItemId.None;
            case "sortie.offered":
                for (int i = 0; i < vm.SortieCount; i++) if (vm.Sorties[i].Offered) return vm.Sorties[i].Id;
                return StrItemId.None;
            default: return StrItemId.None;
        }
    }

    private static Rect StrScreenRect(StrScene s, RectTransform r)
    {
        var c = new Vector3[4]; r.GetWorldCorners(c);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(s.Cam, c[0]), max = min;
        for (int i = 1; i < 4; i++) { Vector2 a = RectTransformUtility.WorldToScreenPoint(s.Cam, c[i]); min = Vector2.Min(min, a); max = Vector2.Max(max, a); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    // The first thing under a screen point, two ways: the canvas's own GraphicRaycaster (what the game's EventSystem asks), and the topmost raycast graphic in hierarchy order.
    private static GameObject StrFirstHit(StrScene s, Vector2 screen, out GameObject hierarchyTop)
    {
        var results = new List<RaycastResult>();
        s.Raycaster.Raycast(new PointerEventData(EventSystem.current) { position = screen }, results);
        hierarchyTop = null;
        foreach (Graphic g in s.Canvas.GetComponentsInChildren<Graphic>(false))
            if (g.raycastTarget && g.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, screen, s.Cam)) hierarchyTop = g.gameObject;
        return results.Count > 0 ? results[0].gameObject : null;
    }

    private static void StrCase(string fixture, Vector2 size, bool relief, float zoom, string pick)
    {
        capture = "str-" + fixture + (pick != null ? "-" + pick.Replace('.', '-') : "") + "-" + (relief ? "relief" : "flat") + "-" + (int)size.x + "x" + (int)size.y + "-z" + zoom.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        StrScene s = StrBuild(fixture, size, relief, zoom);
        try
        {
            StrTick(s, 3);
            if (pick != null)
            {
                StrItemId id = StrPick(s, pick);
                Check(!id.IsNone, "the fixture has an item to select for " + pick);
                s.Hub.Select(id, false);
                StrTick(s, 3);
            }
            StrAddIcons(s);
            StrTick(s);
            Directory.CreateDirectory("renders");
            s.Cam.Render(); RenderTexture.active = s.Target;   // rendered before the gates, so the canvas has its draw order and the raycaster its depths
            StrGates(s, fixture, pick);
            RenderTexture.active = s.Target;
            var png = new Texture2D((int)size.x, (int)size.y, TextureFormat.RGB24, false); png.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); png.Apply();
            File.WriteAllBytes("renders/" + capture + ".png", png.EncodeToPNG()); captures.Add(capture + ".png\t" + (int)size.x + "\t" + (int)size.y + "\tstr-overlay");
            RenderTexture.active = null; Object.DestroyImmediate(png);
        }
        finally { StrDispose(s); }
    }

    private static void StrDispose(StrScene s)
    {
        s.Cam.targetTexture = null; s.Target.Release(); Object.DestroyImmediate(s.Target);
        SceneSingleton<DynamicMap>.i = null; Set(typeof(DynamicMap), "<mapMaximized>k__BackingField", false);
        ModuleServices.Active = null;
        MapUiPointer_ResetFloating();
        Object.DestroyImmediate(s.EventSystemObject); Object.DestroyImmediate(s.Services); Object.DestroyImmediate(s.Canvas); Object.DestroyImmediate(s.Camera);
    }

    private static void MapUiPointer_ResetFloating() => Call(T("Command.Presentation.MapUi.MapUiPointer"), "ResetFloating");

    // Native airbase marks: a friendly blue square at the anchor of every front and a raycast 34 px hit target over it, like the relief's AirbaseHitTarget.
    private static void StrAddIcons(StrScene s)
    {
        StrViewModel vm = s.Hub.Current;
        float scale = Mathf.Abs(s.Image.localScale.x);
        IMapProjection p; ModuleServices.TryGet(out p);
        for (int i = 0; i < vm.FrontCount && i < 4; i++)
        {
            float x, y;
            if (p != null) p.TryProject(vm.Fronts[i].X, vm.Fronts[i].Z, out x, out y); else { x = vm.Fronts[i].X * s.Map.mapDisplayFactor; y = vm.Fronts[i].Z * s.Map.mapDisplayFactor; }
            var go = new GameObject("Airbase mark " + i, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(s.Image, false);
            go.transform.SetSiblingIndex(0);   // under the overlay root, like the native icon layer
            Image img = go.GetComponent<Image>(); img.color = new Color(.35f, .65f, 1f, .9f); img.raycastTarget = true;
            RectTransform r = (RectTransform)go.transform; r.sizeDelta = Vector2.one * 34f / scale; r.localPosition = new Vector3(x, y, 0f);
            s.Icons.Add(img);
            s.IconScreen.Add(RectTransformUtility.WorldToScreenPoint(s.Cam, r.position));
        }
    }

    // ---- the gates -------------------------------------------------------------------------------------------------------------------------------------

    private static void StrGates(StrScene s, string fixture, string pick)
    {
        StrMapOverlay o = s.Overlay;
        StrOverlayPlan plan = o.Plan;
        Rect view = StrScreenRect(s, s.Viewport);
        float reserve = o.ChromeView.BottomReserve;
        Rect inner = new Rect(view.x + 5.5f, view.y + 5.5f + reserve, view.width - 11f, view.height - 11f - reserve);
        // The worst-case board (23 tags in a cluster) has more tags than free places: the placer's documented ceiling lets plates overlap there, so those gates only measure.
        bool crowded = fixture == "full";
        int crowdedMisses = 0;

        Check(!o.Overflowed && !o.InkBuffer.Overflowed && !o.PulseBuffer.Overflowed, "the ink and pulse meshes did not overflow");
        Check((fixture == "rich" || fixture == "full") == (o.PulseBuffer.Count > 0), "the marching axis exists exactly while an operation is ADVANCING (" + fixture + ": " + o.PulseBuffer.Count + " pulse quads)");
        Check(plan.EstimateInkQuads() <= StrMapOverlay.InkCapacity && plan.EstimatePulseQuads() <= StrMapOverlay.PulseCapacity, "the worst-case quads are inside the budgets");
        Check(o.InkBuffer.Count <= plan.EstimateInkQuads(), "the ink never writes more quads than the plan estimated (" + o.InkBuffer.Count + " of " + plan.EstimateInkQuads() + ")");
        Check(!o.WarnedShape, "every shape of the plan was drawn by the real drawer");
        measurements.Add(capture + "\tSTR ink quads\t\t" + o.InkBuffer.Count + " of " + StrMapOverlay.InkCapacity + ", pulse " + o.PulseBuffer.Count + ", tags " + o.ActiveTags + ", rebuild " + o.LastRebuildMs.ToString("0.00") + " ms");

        // Every tag inside the viewport; none meets a 34 px airbase square (its own anchor's, another item's, or a native mark); none meets another tag.
        var anchors = new List<Vector2>();
        foreach (StrMapTag t in o.TagPool)
            if (t.gameObject.activeSelf) anchors.Add(RectTransformUtility.WorldToScreenPoint(s.Cam, s.Image.TransformPoint(new Vector3(t.AnchorLocal.x, t.AnchorLocal.y, 0f))));
        anchors.AddRange(s.IconScreen);
        var boxes = new List<Rect>();
        int shown = 0;
        foreach (StrMapTag t in o.TagPool)
        {
            if (!t.gameObject.activeSelf) continue;
            shown++;
            Rect box = StrScreenRect(s, t.Root); boxes.Add(box);
            Check(box.xMin >= inner.xMin && box.xMax <= inner.xMax && box.yMin >= inner.yMin && box.yMax <= inner.yMax, "tag " + t.Id.Kind + t.Id.Key + " is inside the viewport");
            // A native airbase mark and the tag's own anchor are never covered; in the worst-case board the other items' glyph anchors may be (the placer's documented ceiling).
            foreach (Vector2 a in anchors)
            {
                bool meets = box.Overlaps(new Rect(a.x - 17f, a.y - 17f, 34f, 34f));
                if (crowded) { if (meets) crowdedMisses++; }
                else Check(!meets, "tag " + t.Id.Kind + t.Id.Key + " does not meet a 34 px icon square");
            }
            for (int n = 0; n < boxes.Count - 1; n++) if (!crowded) Check(!box.Overlaps(boxes[n]), "tag " + t.Id.Kind + t.Id.Key + " does not overlap another tag");
        }
        measurements.Add(capture + "\tSTR tags shown\t\t" + shown + " of " + plan.TagCount + " in plan");

        // Raycast: the first hit at each tag's centre is that tag (the card where the card overlaps it); every native mark still wins at its own centre.
        Rect card = o.CardView.Visible ? StrScreenRect(s, o.CardView.Root) : default;
        foreach (StrMapTag t in o.TagPool)
        {
            if (!t.gameObject.activeSelf) continue;
            Vector2 centre = RectTransformUtility.WorldToScreenPoint(s.Cam, t.Root.position);
            GameObject first = StrFirstHit(s, centre, out GameObject top);
            bool underCard = o.CardView.Visible && card.Contains(centre);
            if (first != null)
            {
                bool isTag = first.GetComponentInParent<StrMapTag>() == t, isCard = first.GetComponentInParent<StrMapCardMarker>() != null;
                bool otherTag = first.GetComponentInParent<StrMapTag>() != null && !isTag;
                if (!(crowded && otherTag)) Check(underCard ? isCard : isTag, "RaycastAll at tag " + t.Id.Kind + t.Id.Key + " returns " + (underCard ? "the card" : "that tag") + " first (got " + first.name + ")");
            }
            else measurements.Add(capture + "\tSTR raycast\t\tno GraphicRaycaster result at tag " + t.Id.Kind + t.Id.Key + "; hierarchy top " + (top != null ? top.name : "none"));
            if (top != null)
            {
                bool isTag = top.GetComponentInParent<StrMapTag>() == t, isCard = top.GetComponentInParent<StrMapCardMarker>() != null;
                bool otherTag = top.GetComponentInParent<StrMapTag>() != null && !isTag;
                if (!(crowded && otherTag)) Check(underCard ? isCard : isTag, "the topmost graphic at tag " + t.Id.Kind + t.Id.Key + " is " + (underCard ? "the card" : "that tag") + " (got " + top.name + ")");
            }
        }
        for (int i = 0; i < s.Icons.Count; i++)
        {
            if (o.CardView.Visible && card.Contains(s.IconScreen[i])) continue;   // a floating card covers what is under it, like any window; the guard keeps the map from reacting
            GameObject first = StrFirstHit(s, s.IconScreen[i], out GameObject top);
            if (crowded && top != null && top.GetComponentInParent<StrMapTag>() != null) continue;
            Check(top == s.Icons[i].gameObject, "the native airbase mark " + i + " is still the topmost graphic at its centre (got " + (top != null ? top.name : "none") + ")");
            if (first != null) Check(first == s.Icons[i].gameObject, "RaycastAll at the native airbase mark " + i + " returns the mark first (got " + first.name + ")");
        }

        // The card: registered with the pointer guard, and on its own tag's side of the viewport.
        Type pointer = T("Command.Presentation.MapUi.MapUiPointer");
        foreach (RectTransform chrome in new[] { o.ChromeView.Plate, o.ChromeView.Legend, o.CardView.Root })
        {
            if (!chrome.gameObject.activeInHierarchy) continue;
            Rect r = StrScreenRect(s, chrome);
            Check((bool)Call(pointer, "OverControls", r.center), chrome.name + " claims the pointer (the guard)");
            Check(r.xMin >= view.xMin - 0.5f && r.xMax <= view.xMax + 0.5f && r.yMin >= view.yMin - 0.5f && r.yMax <= view.yMax + 0.5f, chrome.name + " is inside the viewport");
        }
        Check(o.ChromeView.Plate.gameObject.activeInHierarchy, "the layer plate is shown");
        if (pick != null)
        {
            Check(o.CardView.Visible && o.CardPlan.Valid, "a selected item opens its card");
            Check(o.CardView.Root.GetSiblingIndex() == o.CardView.Root.parent.childCount - 1, "the card is the last sibling the harness built (it never raises itself)");
        }
        else Check(!o.CardView.Visible, "with nothing selected there is no card");

        // Passive-widget rule: only tags, the card, the chrome and the synthetic marks take the pointer; the meshes never do.
        foreach (Graphic g in s.Canvas.GetComponentsInChildren<Graphic>(true))
        {
            if (!g.raycastTarget) continue;
            bool allowed = g.GetComponentInParent<StrMapTag>(true) != null || g.GetComponentInParent<StrMapCardMarker>(true) != null || g.name.StartsWith("Airbase mark") || g.name == "Native map viewport" ||
                           g.GetComponentsInParent<Transform>(true).Any(t => t.name == "StrLayerPlate" || t.name == "StrLegend");
            Check(allowed, "unexpected pointer target " + g.name + " under the overlay");
        }

        // Text: at least 11 physical pixels, and inside its box.
        foreach (TMP_Text text in s.Canvas.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue;
            if (text.GetComponentInParent<StrMapTag>() == null && text.GetComponentInParent<StrMapCardMarker>() == null && !text.transform.IsChildOf(o.ChromeView.Plate) && !text.transform.IsChildOf(o.ChromeView.Legend)) continue;
            text.ForceMeshUpdate();
            Vector2 at = RectTransformUtility.WorldToScreenPoint(s.Cam, text.rectTransform.TransformPoint(Vector2.zero));
            float physical = text.fontSize * Vector2.Distance(at, RectTransformUtility.WorldToScreenPoint(s.Cam, text.rectTransform.TransformPoint(Vector2.up)));
            Check(physical >= 10.99f, "text '" + text.text + "' is " + physical.ToString("0.0") + " physical px, under the 11 px floor");
            Rect rect = text.rectTransform.rect;
            if (!text.enableWordWrapping) Check(text.preferredWidth <= rect.width + 1.5f, "text '" + text.text + "' fits its box (" + text.preferredWidth + " > " + rect.width + ")");
            else Check(text.preferredHeight <= rect.height + 1.5f, "wrapped text '" + text.text + "' fits its box (" + text.preferredHeight + " > " + rect.height + ")");
        }
    }

    // ---- interaction gates: pointer guard, click stamp, picker, sibling order, the marching axis, press ids, the layer toggles ----------------------------------

    private static void StrInteractions()
    {
        capture = "str-interactions";
        StrScene s = StrBuild("rich", new Vector2(1280, 720), false, 1f);
        try
        {
            StrTick(s, 3);
            StrMapOverlay o = s.Overlay;
            Type interactions = T("Command.Presentation.MapUi.MfdMapInteractions");
            System.Reflection.PropertyInfo clicked = interactions.GetProperty("ClickedThisFrame", All);

            // A tag click: left button only, stamps the frame so the release does not clear the unit selection, and selects through the hub.
            StrViewModel vm = s.Hub.Current;
            StrItemId id = vm.ProposalCount > 0 ? vm.Proposals[0].Id : StrItemId.None;
            StrMapTag tag = o.Tag(id);
            Check(tag != null, "the first proposal has a tag");
            if (tag != null)
            {
                Call(interactions, "Restore");
                var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                ExecuteEvents.Execute(tag.gameObject, ped, ExecuteEvents.pointerClickHandler);
                Check((bool)clicked.GetValue(null), "a click on a tag stamps the frame (ConsumeClick)");
                Check(s.Hub.Ui.Selected == id, "and selects the item through the hub");
                s.Hub.ClearSelection(); Call(interactions, "Restore");
                var right = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Right };
                ExecuteEvents.Execute(tag.gameObject, right, ExecuteEvents.pointerClickHandler);
                Check(!(bool)clicked.GetValue(null) && s.Hub.Ui.Selected.IsNone, "a right click does neither");
                Check(MapPicker.TryArm("boscali.support", MapPicker.GestureLeft, "TEST"), "the picker arms");
                ExecuteEvents.Execute(tag.gameObject, ped, ExecuteEvents.pointerClickHandler);
                Check(!(bool)clicked.GetValue(null) && s.Hub.Ui.Selected.IsNone, "while an armed pick owns the next click a tag takes none");
                MapPicker.Disarm("boscali.support");
                ExecuteEvents.Execute(tag.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
                Check(s.Hub.Ui.Hover == id, "entering a tag hovers its item");
                ExecuteEvents.Execute(tag.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
                Check(s.Hub.Ui.Hover.IsNone, "leaving it unhovers");
                Call(interactions, "Restore");
            }

            // The card commits through the one command path: a proposal pick goes to the verbs, a refused button sends nothing.
            s.Hub.Select(id, false); StrTick(s, 2);
            Check(o.CardView.Visible && o.CardPlan.Primary.Act == StrCardAct.Pick, "a selected proposal's card has the pick on its primary button");
            StrControls.PressResult pr = o.Controls.Press("map.card.primary");
            Check(pr.Ok && s.Hub.Verbs.Sent.Contains("pick " + vm.Proposals[0].ProposalId), "the primary button runs the pick through the verbs (" + string.Join(",", s.Hub.Verbs.Sent) + ")");
            Check(!o.Controls.Press("map.card.nothing").Ok, "an unknown card id is a refusal");
            StrItemId sortie = StrPick(s, "sortie.offered");
            s.Hub.Select(sortie, false); StrTick(s, 2);
            int before = s.Hub.Verbs.Sent.Count;
            StrControls.PressResult wing = o.Controls.Press("map.card.secondary");
            Check(!wing.Ok && wing.Act.Text.Length >= 8 && s.Hub.Verbs.Sent.Count == before, "the wing button of an offered sortie is refused with its reason and sends nothing");
            Check(o.CardPlan.Reason == wing.Act.Text, "the same words are the card's footer");
            Check(o.Controls.Press("map.card.primary").Ok && s.Hub.Verbs.Sent.Exists(x => x.StartsWith("accept")), "the primary button of an offered sortie accepts it");
            o.Controls.Press("map.card.close"); StrTick(s, 2);
            Check(!o.CardView.Visible && s.Hub.Ui.Selected.IsNone, "close clears the selection and hides the card");
            Check(!o.Controls.Has("map.card.primary"), "a hidden card leaves no control id behind");

            // The layer plate: three toggles that mirror the interface state.
            Check(o.Controls.Press("layer.sorties").Ok, "the SORTIES toggle presses");
            StrTick(s, 2);
            Check(!s.Hub.Ui.ShowSorties && o.TagCount(StrTagKind.Sortie) == 0 && o.TagCount(StrTagKind.Post) == 0, "SORTIES off removes the sortie and post tags");
            Check(o.TagCount(StrTagKind.Front) > 0, "and leaves the fronts");
            o.Controls.Press("layer.sorties"); o.Controls.Press("layer.fronts"); o.Controls.Press("layer.operation"); StrTick(s, 2);
            Check(o.TagCount(StrTagKind.Front) == 0 && o.TagCount(StrTagKind.Proposal) == 0 && o.TagCount(StrTagKind.Effort) == 0, "FRONTS and OPERATION off remove their tags");
            Check(o.ChromeView.Plate.gameObject.activeInHierarchy, "the plate stays so a layer can be turned back on");
            o.Controls.Press("layer.fronts"); o.Controls.Press("layer.operation"); StrTick(s, 2);
            Check(o.TagCount(StrTagKind.Effort) >= 1 && o.TagCount(StrTagKind.Front) >= 1, "turning the layers back on brings the tags back");

            // Sibling stability: sixty ticks with the selection and hover changing never reorder the map viewport's children (the card never raises itself).
            var order = new List<string>();
            foreach (Transform c in s.Viewport) order.Add(c.name);
            var imageOrder = new List<string>();
            foreach (Transform c in s.Image) imageOrder.Add(c.name);
            for (int i = 0; i < 60; i++)
            {
                if (i % 7 == 0) s.Hub.Select(i % 14 == 0 ? id : sortie, false);
                if (i % 5 == 0) s.Hub.Hover(i % 10 == 0 ? id : StrItemId.None);
                if (i == 40) s.Hub.ClearSelection();
                StrTick(s, 1, false);
            }
            var after = new List<string>();
            foreach (Transform c in s.Viewport) after.Add(c.name);
            var imageAfter = new List<string>();
            foreach (Transform c in s.Image) imageAfter.Add(c.name);
            Check(string.Join("|", order) == string.Join("|", after), "the viewport's sibling order is stable over 60 ticks");
            Check(string.Join("|", imageOrder) == string.Join("|", imageAfter), "and so is the map image's");

            // The marching axis: only an ADVANCING operation has a pulse, and its dashes move between two ticks.
            Check(o.PulseBuffer.Count > 0, "an advancing operation has a pulse axis");
            float[] x0 = (float[])o.PulseBuffer.X.Clone();
            for (int i = 0; i < 3; i++) { s.Now += 0.2f; o.Tick(s.Now); }
            float moved = 0f; for (int i = 0; i < o.PulseBuffer.Count * 4; i++) moved += Mathf.Abs(o.PulseBuffer.X[i] - x0[i]);
            Check(moved > 0.01f, "the dashes of the axis march between ticks (" + moved + ")");
            int builds = o.Rebuilds; StrTick(s, 5);
            Check(o.Rebuilds == builds, "an unchanged model and pose rebuild nothing: the gate keeps the ink still");

            // The map closed: nothing is drawn and the pieces hide.
            Set(typeof(DynamicMap), "<mapMaximized>k__BackingField", false);
            o.Tick(s.Now + 1f);
            Check(!o.Visible && !o.ChromeView.Plate.gameObject.activeSelf && !o.CardView.Visible, "a closed map hides the overlay, the plate and the card");
            Set(typeof(DynamicMap), "<mapMaximized>k__BackingField", true);

            // A model that has no ground war draws no tags, and says why on the plate.
            Check(StrPanelText.LayerLine(s.Hub.Current).Length > 0, "the plate line reads");
        }
        finally { StrDispose(s); }
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

/// <summary>
/// The OPS window and its rooms, rendered offline from the production builders over a synthetic busy
/// "game" backdrop at four screen sizes. Asserts the root/room contract: the root draws nothing inside
/// the outline, the window keeps a dimmed margin, each room declares a hero and covers its area, no room
/// sits on the legacy rail columns, rooms do not share a layout signature, labels never overlap, text is
/// never truncated and never below 10 px, and reduced motion opens on the final frame.
/// </summary>
public static class OpsWindowUnityCheck
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly Assembly Mod = typeof(AvScreen).Assembly;
    private static readonly float[] LegacyRails = { 24f, 320f, 1616f };

    private static readonly (string Name, int PixelW, int PixelH, float CanvasW, float CanvasH)[] Screens =
    {
        ("1920x1080", 1920, 1080, 1920f, 1080f),
        ("2560x1080", 2560, 1080, 2560f, 1080f),
        ("1280x720", 1280, 720, 1920f, 1080f),
        ("3840x2160", 3840, 2160, 1920f, 1080f)
    };

    public static int Assertions;
    private static object support;
    private static GameObject backdropCanvas;
    private static readonly Dictionary<string, List<Rect>> signatures = new Dictionary<string, List<Rect>>();
    private static readonly List<string> renders = new List<string>();

    public static string Run(object supportManager, Func<double, object> detachmentFixture, Func<object[]> cyberFixture,
        Func<object[]> stationFixture)
    {
        support = supportManager;
        Type sprites = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Window.OpsSprites", true);
        sprites.GetMethod("Ensure", Static).Invoke(null, null);
        int bytes = (int)sprites.GetProperty("Bytes", Static).GetValue(null);
        Check(bytes <= 6 * 1024 * 1024, "Generated OPS textures must stay under 6 MB, found " + bytes + " bytes.");

        RenderPrimitiveSheet();
        RenderRootOnly();
        CheckBoardDrag();
        CheckDeskMapInput();
        RenderDesk(detachmentFixture);
        RenderCyber(cyberFixture);
        RenderStation(stationFixture);
        RenderImager(stationFixture);
        CheckUniqueness();
        return renders.Count + " OPS window renders (" + string.Join(", ", renders) + "); generated textures " +
               (bytes / 1024) + " KB";
    }

    // ---- Window plumbing ---------------------------------------------------------------------

    private static Component CreateWindow()
    {
        Type type = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Window.OpsWindow", true);
        Func<bool> reduce = () => true;
        var window = (Component)type.GetMethod("Create", Static).Invoke(null, new object[] { reduce });
        ((Behaviour)window).enabled = false;
        var canvas = window.GetComponent<Canvas>();
        canvas.GetComponent<CanvasScaler>().enabled = false;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.transform.position = Vector3.zero;
        canvas.transform.localScale = Vector3.one;
        canvas.transform.rotation = Quaternion.identity;
        return window;
    }

    private static void SizeCanvas(Component window, float w, float h)
    {
        var canvas = window.GetComponent<Canvas>();
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(w, h);
        Call(window, "Layout", w, h);
        EnsureBackdrop(w, h);
    }

    /// <summary>A stand-in for the live game: saturated terrain blobs, a grid, a white front, a top ticker.</summary>
    private static void EnsureBackdrop(float w, float h)
    {
        if (backdropCanvas != null) Object.DestroyImmediate(backdropCanvas);
        backdropCanvas = new GameObject("BusyGame", typeof(RectTransform), typeof(Canvas));
        var canvas = backdropCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 0;
        var rect = (RectTransform)backdropCanvas.transform;
        rect.sizeDelta = new Vector2(w, h);
        rect.position = new Vector3(0f, 0f, 5f);
        var image = new GameObject("Terrain", typeof(RectTransform), typeof(RawImage));
        image.transform.SetParent(rect, false);
        AvKit.Stretch((RectTransform)image.transform);
        image.GetComponent<RawImage>().texture = BusyTexture(480, 270);
        AvKit.Label(rect, "THEATRE WIRE · BRAVO HOLDS NORTH RIDGE · SAM SITE DESTROYED AT 26/-4 · INTERCEPT VECTOR 045",
            new Rect(0f, 0f, w, 36f), Color.white, 14f, FontStyles.Bold, TextAlignmentOptions.Center);
    }

    private static Texture2D busy;

    private static Texture2D BusyTexture(int w, int h)
    {
        if (busy != null) return busy;
        busy = new Texture2D(w, h, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float u = x / (float)w, v = y / (float)h;
            float terrain = Mathf.PerlinNoise(u * 6f, v * 4f);
            var c = new Color(0.16f + 0.2f * terrain, 0.42f + 0.25f * terrain, 0.18f + 0.1f * terrain);
            float blue = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(0.28f, 0.42f)) * 3.2f);
            float red = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(0.72f, 0.58f)) * 3f);
            c = Color.Lerp(c, new Color(0.2f, 0.45f, 1f), blue * 0.6f);
            c = Color.Lerp(c, new Color(1f, 0.25f, 0.2f), red * 0.6f);
            if (x % 40 == 0 || y % 40 == 0) c = Color.Lerp(c, Color.white, 0.25f);
            float front = 0.5f + 0.12f * Mathf.Sin(v * 9f);
            if (Mathf.Abs(u - front) < 0.004f) c = Color.white;
            if (v > 0.93f) c = new Color(0.05f, 0.07f, 0.09f);
            pixels[y * w + x] = c;
        }
        busy.SetPixels(pixels);
        busy.Apply();
        return busy;
    }

    private static Rect WindowRect(Component window)
    {
        object box = window.GetType().GetProperty("Target", Hidden).GetValue(window);
        Type t = box.GetType();
        return new Rect((float)t.GetField("X").GetValue(box), (float)t.GetField("Y").GetValue(box),
            (float)t.GetField("Width").GetValue(box), (float)t.GetField("Height").GetValue(box));
    }

    // ---- Primitive sheet ---------------------------------------------------------------------

    /// <summary>Every primitive in every room's skin, side by side; the skins must differ.</summary>
    private static void RenderPrimitiveSheet()
    {
        const float columnW = 340f, sheetH = 560f;
        var rooms = new (string Style, string Skin, string Surface)[]
        {
            ("DeskStyle", "Dossier", "Paper"), ("StationStyle", "Telemetry", "Surface"),
            ("CyberStyle", "Terminal", "Pane"), ("ImagerStyle", "Symbology", "Pod")
        };
        var go = new GameObject("PrimitiveSheet", typeof(RectTransform), typeof(Canvas));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)go.transform;
        float sheetW = columnW * rooms.Length;
        root.sizeDelta = new Vector2(sheetW, sheetH);
        var inks = new List<Color>();
        var glyphs = new List<Sprite>();
        for (int r = 0; r < rooms.Length; r++)
        {
            Type style = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Views." + rooms[r].Style, true);
            style.GetMethod("Resolve", Static).Invoke(null, null);
            object skin = style.GetMethod(rooms[r].Skin, Static).Invoke(null, null);
            var surface = (Color)style.GetProperty(rooms[r].Surface, Static).GetValue(null);
            float x = r * columnW;
            AvKit.Panel(root, new Rect(x, 0f, columnW, sheetH), surface.WithAlpha(1f));
            inks.Add((Color)skin.GetType().GetField("Ink").GetValue(skin));
            glyphs.Add((Sprite)skin.GetType().GetField("Glyph").GetValue(skin));
            AvKit.Label(root, rooms[r].Style.Replace("Style", "").ToUpperInvariant() + " SKIN", new Rect(x + 16f, -10f, columnW - 32f, 18f),
                inks[r], 12f, FontStyles.Bold);
            Primitive("ArcGauge", root, new Rect(x + 16f, -40f, 110f, 126f), skin, g => Call(g, "Set", 0.62f, "62%", "FITTED"));
            Primitive("PipTrack", root, new Rect(x + 150f, -48f, columnW - 170f, 40f), skin, g => Call(g, "Set", 3, 2, "3 OF 6 · 2 PLANNED"),
                6);
            Primitive("BulletBar", root, new Rect(x + 16f, -186f, columnW - 32f, 28f), skin,
                g => Call(g, "Set", 0.58f, 0.8f, "ENERGY", "580 / 1,000 KJ"));
            Primitive("SegmentBar", root, new Rect(x + 16f, -240f, columnW - 32f, 26f), skin, g => Call(g, "Set", 62, 26, 12));
            object lanes = LaneFixture();
            Primitive("TimelineLanes", root, new Rect(x + 16f, -292f, columnW - 32f, 30f), skin,
                g => Call(g, "Set", lanes, 4, new[] { Color.clear, AvTheme.RailInfo, AvTheme.RailCaution, AvTheme.RailReady, AvTheme.Dim, AvTheme.RailInfo },
                    "EN ROUTE 0:40 · THEN TASK, HOLD"));
            Primitive("Sparkline", root, new Rect(x + 16f, -344f, columnW - 32f, 80f), skin, g =>
            {
                for (int i = 0; i < 60; i++) Call(g, "Push", 40f + 30f * Mathf.Sin(i * 0.35f) + i * 0.4f, 0f, 100f, "HEAT " + (i + 20));
            });
        }
        for (int i = 0; i < inks.Count; i++)
        for (int j = 0; j < i; j++)
            Check(inks[i] != inks[j] || glyphs[i] != glyphs[j], rooms[i].Style + " and " + rooms[j].Style + " skin a primitive identically.");
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.text.Length == 0) continue;
            text.ForceMeshUpdate();
            Check(!text.isTextTruncated, "Primitive sheet text truncated: '" + text.text + "'");
            Check(text.fontSize >= 9.99f, "Primitive sheet text below 10 px: '" + text.text + "'");
        }
        Capture(go, sheetW, sheetH, (int)sheetW, (int)sheetH, "ops-primitives.png");
        renders.Add("ops-primitives.png");
        Object.DestroyImmediate(go);
    }

    private static void Primitive(string name, RectTransform parent, Rect area, object skin, Action<object> set, int count = -1)
    {
        object primitive = Activator.CreateInstance(Mod.GetType("BoscaliSummer.Features.Support.Presentation.Viz." + name, true));
        if (name == "SegmentBar") Call(primitive, "Build", parent, area, skin, AvTheme.RailReady, AvTheme.RailCaution, AvTheme.RailDanger);
        else if (count > 0) Call(primitive, "Build", parent, area, skin, count);
        else Call(primitive, "Build", parent, area, skin);
        set(primitive);
    }

    private static object LaneFixture()
    {
        Type laneType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Layout.LaneSegment", true);
        Array lanes = Array.CreateInstance(laneType, 8);
        Type math = Mod.GetType("BoscaliSummer.Features.Support.Domain.Layout.TimelineMath", true);
        Type state = Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.TeamState", true);
        Type mission = Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.FieldMission", true);
        math.GetMethod("Team", Static).Invoke(null, new object[] { Enum.Parse(state, "EnRoute"), 40f, Enum.Parse(mission, "Seize"), 0, 600f, lanes });
        return lanes;
    }

    // ---- Root --------------------------------------------------------------------------------

    private static void RenderRootOnly()
    {
        Component window = CreateWindow();
        SizeCanvas(window, 1920f, 1080f);
        var canvas = window.GetComponent<Canvas>();
        canvas.enabled = true;
        ((GameObject)Get(window, "content")).SetActive(true);
        SetTween(window, "backdropTween", 1f);
        SetTween(window, "windowTween", 1f);
        Call(window, "ApplyMotion");
        Rect frame = WindowRect(window);
        Check(frame.x >= 40f && frame.y >= 40f && 1920f - frame.xMax >= 40f && 1080f - frame.yMax >= 40f,
            "The window must keep at least 40 px of dimmed game on every side at 1920x1080: " + frame);
        CheckRootOnly(window, 1920f, 1080f);
        Capture(canvas.gameObject, 1920f, 1080f, 1920, 1080, "ops-window-root-1920x1080.png");
        Object.DestroyImmediate(window.gameObject);
    }

    private static void SetTween(Component window, string field, float value)
    {
        FieldInfo info = window.GetType().GetField(field, Hidden);
        object tween = info.GetValue(window);
        tween.GetType().GetField("Value").SetValue(tween, value);
        tween.GetType().GetField("To").SetValue(tween, value);
        info.SetValue(window, tween);
    }

    /// <summary>Every root graphic lies outside the outline, except the backdrop, the hollow shadow and the 1 px outline.</summary>
    private static void CheckRootOnly(Component window, float canvasW, float canvasH)
    {
        Rect frame = WindowRect(window);
        var root = (RectTransform)window.GetType().GetProperty("Root", Hidden).GetValue(window);
        var layer = (RectTransform)window.GetType().GetProperty("RoomLayer", Hidden).GetValue(window);
        var shadow = (Image)window.GetType().GetProperty("ShadowImage", Hidden).GetValue(window);
        Check(!shadow.fillCenter, "The window shadow must be hollow so the root paints nothing under the room.");
        Rect inner = new Rect(frame.x + 1.5f, frame.y + 1.5f, frame.width - 3f, frame.height - 3f);
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            if (!graphic.gameObject.activeInHierarchy || !graphic.enabled) continue;
            if (graphic.transform.IsChildOf(layer)) continue;
            if (graphic == shadow) continue;
            if (graphic.transform.parent == root) continue; // backdrop and vignette, behind everything
            if (graphic.color.a <= 0.001f && graphic.canvasRenderer.cullTransparentMesh) continue;
            Rect r = CanvasRect(graphic.rectTransform, canvasW, canvasH);
            bool inside = r.Overlaps(inner);
            Check(!inside, "The root must draw nothing inside the window outline: " + NodePath(graphic.transform) + " at " + r);
        }
    }

    // ---- Rooms -------------------------------------------------------------------------------

    private static Sprite terrainFixture;
    private static void TerrainFixture(object desk)
    {
        string path = Path.GetFullPath("terrain-fixture.png");
        if (!File.Exists(path)) return;
        if (terrainFixture == null)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Check(texture.LoadImage(File.ReadAllBytes(path)), "The supplied installed-game terrain image must decode.");
            terrainFixture = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }
        object map = desk.GetType().GetProperty("Map", Hidden).GetValue(desk);
        object terrain = map.GetType().GetField("terrain", Hidden).GetValue(map);
        Call(terrain, "SetSource", terrainFixture, new Vector2(81920f, 81920f));
        Call(terrain, "Refresh");
        Check((bool)terrain.GetType().GetProperty("Available", Hidden).GetValue(terrain), "The desk must show the supplied terrain.");
    }

    private static void CheckBoardDrag()
    {
        var go = new GameObject("BoardDragFixture", typeof(RectTransform), typeof(Canvas));
        var events = new GameObject("BoardDragEvents", typeof(EventSystem));
        go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var parent = (RectTransform)go.transform;
        Type type = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Board.BoardSurface", true);
        var rect = new Rect(130f, -90f, 640f, 420f);
        object board = Activator.CreateInstance(type, Hidden, null, new object[] { parent, rect, rect, true, true }, null);
        Canvas.ForceUpdateCanvases();
        RectTransform input = (RectTransform)type.GetProperty("InputLayer", Hidden).GetValue(board);
        object frame = type.GetProperty("Frame", Hidden).GetValue(board);
        float x = (float)frame.GetType().GetField("CentreX").GetValue(frame);
        float z = (float)frame.GetType().GetField("CentreZ").GetValue(frame);
        float mpp = (float)type.GetProperty("MetresPerPixel", Hidden).GetValue(board);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(null, input.TransformPoint(new Vector3(240f, -180f, 0f)));
        Vector2 b = RectTransformUtility.WorldToScreenPoint(null, input.TransformPoint(new Vector3(250f, -185f, 0f)));
        var pointer = new PointerEventData(events.GetComponent<EventSystem>())
        { button = PointerEventData.InputButton.Left, position = b, delta = b - a, dragging = true };
        ExecuteEvents.Execute(input.gameObject, pointer, ExecuteEvents.dragHandler);
        frame = type.GetProperty("Frame", Hidden).GetValue(board);
        float dx = (float)frame.GetType().GetField("CentreX").GetValue(frame) - x;
        float dz = (float)frame.GetType().GetField("CentreZ").GetValue(frame) - z;
        Check(Mathf.Abs(dx + 10f * mpp) < 0.1f && Mathf.Abs(dz - 5f * mpp) < 0.1f,
            "An offset map must pan by the pointer delta, without adding its room offset: " + dx + ", " + dz);
        Type control = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Viz.RoomControl", true);
        var child = new GameObject("DragClick", typeof(RectTransform));
        child.transform.SetParent(input, false);
        Component roomControl = child.AddComponent(control);
        int clicks = 0;
        Call(roomControl, "SetAction", (Action)(() => clicks++));
        Call(roomControl, "OnPointerClick", pointer);
        Check(clicks == 0, "Dragging across a map marker must not select it.");
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(events);
    }

    private static void CheckDeskMapInput()
    {
        var canvasObject = new GameObject("DeskMapInputFixture", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        var events = new GameObject("DeskMapInputEvents", typeof(EventSystem));
        // Batch-mode editor overlay graphics never get a rendered depth (Graphic.depth == -1).
        // Render a real world-space canvas first, matching the screenshot fixtures.
        var cameraObject = new GameObject("DeskMapInputCamera", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 325f;
        camera.aspect = 1000f / 650f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        var target = new RenderTexture(1000, 650, 24);
        camera.targetTexture = target;
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        // This executeMethod runs outside Play Mode; BaseRaycaster.OnEnable is not invoked.
        // Register the fixture exactly as that lifecycle hook does in the running game.
        GraphicRaycaster raycaster = canvasObject.GetComponent<GraphicRaycaster>();
        typeof(BaseRaycaster).GetMethod("OnEnable", Hidden).Invoke(raycaster, null);
        ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(1000f, 650f);
        Type style = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Views.DeskStyle", true);
        style.GetMethod("Resolve", Static).Invoke(null, null);
        Type mapType = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Views.DeskMap", true);
        object map = Activator.CreateInstance(mapType);
        int selected = -1;
        var viewport = new Rect(130f, -90f, 640f, 420f);
        Call(map, "Build", (RectTransform)canvasObject.transform, viewport, viewport, (Action<int>)(i => selected = i));
        Type detachmentType = Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.SpecOpsDetachment", true);
        object detachment = PlanningFixture(detachmentType, out double now);
        Call(map, "Layout", detachment, now, 0, Vector2.zero, false);
        Canvas.ForceUpdateCanvases();
        camera.Render();
        object board = mapType.GetProperty("Board", Hidden).GetValue(map);
        var tags = (Array)mapType.GetField("tags", Hidden).GetValue(map);
        Component label = null;
        for (int i = 0; i < tags.Length; i++)
        {
            object tag = tags.GetValue(i);
            var control = (Component)tag.GetType().GetField("Control", Hidden).GetValue(tag);
            if (!control.gameObject.activeInHierarchy) continue;
            label = control;
            break;
        }
        Check(label != null, "The desk fixture must contain a clickable objective label.");
        var rect = (RectTransform)label.transform;
        var pointer = new PointerEventData(events.GetComponent<EventSystem>())
        {
            button = PointerEventData.InputButton.Left,
            eligibleForClick = true,
            position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center))
        };
        var hits = new List<RaycastResult>();
        events.GetComponent<EventSystem>().RaycastAll(pointer, hits);
        Check(hits.Count > 0 && hits[0].gameObject == label.gameObject,
            "The visible objective label must be the first actual UI raycast hit, above terrain and geographic graphics. " +
            "Pointer " + pointer.position + " screen " + Screen.width + "x" + Screen.height +
            " label depth " + label.GetComponent<Graphic>().depth + " culled " + label.GetComponent<Graphic>().canvasRenderer.cull +
            " hits " + string.Join(", ", hits.ConvertAll(hit => NodePath(hit.gameObject.transform))));
        pointer.pointerCurrentRaycast = hits[0];
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Check(selected >= 0, "A raycast through the visible label must select its objective.");
        float before = (float)board.GetType().GetProperty("MetresPerPixel", Hidden).GetValue(board);
        pointer.scrollDelta = Vector2.up;
        GameObject scrollTarget = ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.scrollHandler);
        float after = (float)board.GetType().GetProperty("MetresPerPixel", Hidden).GetValue(board);
        Check(scrollTarget != null && after < before,
            "Wheel input over an objective label must reach the board and zoom the geographic frame.");

        // Move the real label past the viewport while keeping its rectangle on the screen.
        // Its raycast must be clipped by the same mask that hides its visible graphic.
        rect.anchoredPosition = new Vector2(viewport.x + viewport.width + 20f, viewport.y - 100f);
        Canvas.ForceUpdateCanvases();
        camera.Render();
        pointer.position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        hits.Clear();
        events.GetComponent<EventSystem>().RaycastAll(pointer, hits);
        Check(!hits.Exists(hit => hit.gameObject == label.gameObject),
            "A label outside the terrain viewport must not leave an invisible clickable hit area.");
        typeof(BaseRaycaster).GetMethod("OnDisable", Hidden).Invoke(raycaster, null);
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(events);
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(target);
    }

    private static void RenderDesk(Func<double, object> detachmentFixture)
    {
        Component window = CreateWindow();
        Type deskType = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Views.DeskView", true);
        var log = new[]
        {
            "000:02:20  CHARLIE MOVING OUT · SEIZE · NORTH RIDGE AIRFIELD", "000:01:30  ALPHA SUCCESS · OBSERVATION POST HELD · KERSEY",
            "000:01:30  BRAVO MOVING OUT · SABOTAGE · AIR DEFENCE 26/-4", "000:00:50  ALPHA ON TASK · RECON · KERSEY", "", ""
        };
        object desk = Activator.CreateInstance(deskType, Hidden, null, new[] { support, log }, null);
        Call(desk, "SetHomes", new[] { -6000f, -14000f, 64000f }, new[] { -2000f, 13000f, -62000f },
            new[] { "BOSCALI FIELD", "NORTH RIDGE", "VIGIL CAY NAVAL AIRBASE" }, 3);
        SizeCanvas(window, 1920f, 1080f);
        Present(window, desk);
        TerrainFixture(desk);
        Frontline(desk);

        // Empty: a fresh detachment (ALPHA and BRAVO formed, nothing listed yet).
        Type detachmentType = Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.SpecOpsDetachment", true);
        object empty = Activator.CreateInstance(detachmentType);
        PaintDesk(desk, empty, 10.0);
        RoomChecks(window, desk, "desk-empty", 1920f, 1080f, false);
        Shoot(window, "ops-desk-empty", Screens[0]);

        // Planning: objectives listed, two teams ready, SABOTAGE hovered on an air-defence site.
        object planning = PlanningFixture(detachmentType, out double planNow);
        var bypass = new BepInEx.Configuration.ConfigFile(Path.GetFullPath("desk-bypass.cfg"), false).Bind("Debug", "Bypass", true, "fixture");
        SetField(support, "bypassRequirements", bypass);
        PaintDesk(desk, planning, planNow);
        Call(desk, "Select", 0, 1);
        SetField(desk, "hoverMission", 1);
        PaintDesk(desk, planning, planNow);
        RoomChecks(window, desk, "desk-planning", 1920f, 1080f, true);
        Shoot(window, "ops-desk-planning", Screens[0]);
        SetField(support, "bypassRequirements", null);
        SetField(desk, "hoverMission", -1);

        // Three teams out: ALPHA holds an OP, BRAVO en route, CHARLIE on task; DELTA selected (unformed).
        object detachment = detachmentFixture(0.0);
        double now = 140.0;
        PaintDesk(desk, detachment, now);
        Call(desk, "Select", 3, 0);
        PaintDesk(desk, detachment, now);
        RoomChecks(window, desk, "desk-teams-out", 1920f, 1080f, true);
        CheckClusterFill(desk, detachment);
        foreach (var screen in Screens)
        {
            SizeCanvas(window, screen.CanvasW, screen.CanvasH);
            Present(window, desk);
            TerrainFixture(desk);
            Frontline(desk);
            PaintDesk(desk, detachment, now);
            if (screen.CanvasW != 1920f || screen.CanvasH != 1080f) RoomChecks(window, desk, "desk-teams-out-" + screen.Name, screen.CanvasW, screen.CanvasH, true);
            Shoot(window, "ops-desk-teams-out", screen);
        }
        signatures["desk"] = Sections(desk);
        Object.DestroyImmediate(window.gameObject);
    }

    private static void RenderCyber(Func<object[]> cyberFixture)
    {
        Component window = CreateWindow();
        Type viewType = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Views.CyberView", true);
        var log = new[]
        {
            "000:25:10  INTRUSION DETECTED AT BAS-BRAVO · ISOLATE IT", "000:24:02  CTY-LIMA TAKEN · STAGE 2 CONTROL",
            "000:22:40  BREACH OPEN ON CTY-LIMA · QUIET", "000:20:00  WATCH FLOOR ONLINE", "", ""
        };
        object view = Activator.CreateInstance(viewType, Hidden, null, new[] { support, log }, null);
        SizeCanvas(window, 1920f, 1080f);
        Present(window, view);
        Frontline(view);

        Type networkType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Cyber.CyberNetwork", true);
        object empty = Activator.CreateInstance(networkType);
        PaintCyber(view, empty, 5.0);
        RoomChecks(window, view, "cyber-empty", 1920f, 1080f, false);
        Shoot(window, "ops-cyber-empty", Screens[0]);

        object[] fixture = cyberFixture();
        object network = fixture[0];
        double clock = (double)fixture[1];
        int target = (int)networkType.GetProperty("BreachTarget", Hidden).GetValue(network);
        Call(view, "Select", target, -1);
        PaintCyber(view, network, clock);
        PaintCyber(view, network, clock);
        RoomChecks(window, view, "cyber-breach", 1920f, 1080f, true);
        foreach (var screen in Screens)
        {
            SizeCanvas(window, screen.CanvasW, screen.CanvasH);
            Present(window, view);
            Frontline(view);
            PaintCyber(view, network, clock);
            if (screen.CanvasW != 1920f) RoomChecks(window, view, "cyber-breach-" + screen.Name, screen.CanvasW, screen.CanvasH, true);
            Shoot(window, "ops-cyber-breach", screen);
        }

        Type kind = Mod.GetType("BoscaliSummer.Features.Support.Domain.Cyber.IncidentKind", true);
        int intrusion = (int)Call(network, "Force", Enum.Parse(kind, "Intrusion"), clock);
        Call(network, "Force", Enum.Parse(kind, "Raid"), clock);
        for (int i = 0; i < 20; i++) Call(network, "Tick", clock += 0.25, 0.25f, 1f);
        Call(view, "Select", 2, intrusion);
        PaintCyber(view, network, clock);
        RoomChecks(window, view, "cyber-incident", 1920f, 1080f, true);
        Shoot(window, "ops-cyber-incident", Screens[0]);
        signatures["cyber"] = Sections(view);
        Object.DestroyImmediate(window.gameObject);
    }

    private static object OrbitClock()
    {
        Type clockType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.OrbitClock", true);
        return clockType.GetProperty("Default", BindingFlags.Static | BindingFlags.Public).GetValue(null);
    }

    private static void RenderStation(Func<object[]> stationFixture)
    {
        Component window = CreateWindow();
        Type viewType = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Views.StationView", true);
        object plan = Activator.CreateInstance(Mod.GetType("BoscaliSummer.Features.Support.Presentation.PlatformPlan", true));
        var loop = new[]
        {
            "000:39:34  HARD DOCK · SIG B2", "000:38:02  LIFTOFF CONFIRMED · SIG DOCKING IN 00:20",
            "000:37:10  AOS · BASTION OVERHEAD · LEO PASS", "000:31:44  BROWNOUT · LOADS SHED", "---:--:--  CONSOLE ONLINE · FLIGHT HAS THE ROOM", ""
        };
        object view = Activator.CreateInstance(viewType, Hidden, null, new[] { support, plan, loop, null }, null);
        SizeCanvas(window, 1920f, 1080f);
        Present(window, view);
        object clock = OrbitClock();

        var emptyLoop = new[] { "---:--:--  CONSOLE ONLINE · FLIGHT HAS THE ROOM", "", "", "", "", "" };
        FieldInfo loopField = viewType.GetField("loop", Hidden);
        loopField.SetValue(view, emptyLoop);
        PaintStation(view, null, 0.0, clock);
        RoomChecks(window, view, "station-empty", 1920f, 1080f, false);
        Shoot(window, "ops-station-empty", Screens[0]);
        loopField.SetValue(view, loop);

        object[] fixture = stationFixture();
        object platform = fixture[0];
        double now = (double)fixture[1];
        int free = (int)fixture[2];
        Type kind = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.ModuleKind", true);
        Call(view, "Preview", free, Enum.Parse(kind, "Battery"));
        PaintStation(view, platform, now, clock);
        RoomChecks(window, view, "station-fitted", 1920f, 1080f, true);
        foreach (var screen in Screens)
        {
            SizeCanvas(window, screen.CanvasW, screen.CanvasH);
            Present(window, view);
            PaintStation(view, platform, now, clock);
            if (screen.CanvasW != 1920f) RoomChecks(window, view, "station-fitted-" + screen.Name, screen.CanvasW, screen.CanvasH, true);
            Shoot(window, "ops-station-fitted", screen);
        }
        signatures["station"] = Sections(view);
        Object.DestroyImmediate(window.gameObject);
    }

    private static void PaintStation(object view, object platform, double now, object clock)
    {
        SetField(support, "opsReceived", Time.unscaledTime);
        Call(view, "Paint", platform, now, clock, true);
    }

    private static void RenderImager(Func<object[]> stationFixture)
    {
        Component window = CreateWindow();
        Type viewType = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Views.ImagerView", true);
        object products = Activator.CreateInstance(Mod.GetType("BoscaliSummer.Features.Support.Presentation.PlatformProducts", true));
        object view = Activator.CreateInstance(viewType, Hidden, null, new[] { support, products, null }, null);
        SizeCanvas(window, 1920f, 1080f);
        Present(window, view);
        object clock = OrbitClock();

        object[] fixture = stationFixture();
        object platform = fixture[0];
        double now = (double)fixture[1];
        object state = platform.GetType().GetMethod("State", Hidden).Invoke(platform, new[] { now, clock });
        double subX = (double)state.GetType().GetField("SubX").GetValue(state);
        double subZ = (double)state.GetType().GetField("SubZ").GetValue(state);
        Type position = Mod.GetType("GlobalPosition") ?? typeof(Canvas).Assembly.GetType("GlobalPosition");
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (position != null) break;
            position = assembly.GetType("GlobalPosition");
        }
        object aim = Activator.CreateInstance(position, (float)subX, 0f, (float)subZ);
        Call(view, "Show", aim);
        Call(view, "Fixture", SarScene());
        Call(view, "Paint", platform, state, now, clock, false, true);
        RoomChecks(window, view, "imager-feed", 1920f, 1080f, true);
        Shoot(window, "ops-imager-feed", Screens[0]);

        Call(view, "Fixture", new object[] { null });
        Call(view, "Paint", null, Activator.CreateInstance(state.GetType()), now, clock, false, true);
        RoomChecks(window, view, "imager-no-station", 1920f, 1080f, false);
        Shoot(window, "ops-imager-no-station", Screens[0]);
        signatures["imager"] = Sections(view);
        Object.DestroyImmediate(window.gameObject);
    }

    /// <summary>A stand-in radar scene: speckle, bright built-up blocks, a road and a river.</summary>
    private static Texture2D SarScene()
    {
        const int w = 192, h = 120;
        var scene = new Texture2D(w, h, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color[w * h];
        var random = new System.Random(7);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float speckle = (float)random.NextDouble();
            float ground = 0.22f + 0.18f * Mathf.PerlinNoise(x * 0.05f, y * 0.05f) + 0.12f * speckle;
            bool block = (x / 9) % 3 == 1 && (y / 7) % 3 == 1 && x > 90 && x < 150 && y > 30 && y < 90;
            if (block) ground = 0.75f + 0.25f * speckle;
            if (Mathf.Abs(y - (60 + 18 * Mathf.Sin(x * 0.04f))) < 1.2f) ground = 0.9f;
            if (Mathf.Abs(x - (40 + 10 * Mathf.Sin(y * 0.08f))) < 3f) ground = 0.05f;
            pixels[y * w + x] = new Color(ground, ground, ground);
        }
        scene.SetPixels(pixels);
        scene.Apply();
        return scene;
    }

    private static float cyberTime = 10f;

    private static void PaintCyber(object view, object network, double now)
    {
        SetField(support, "opsReceived", Time.unscaledTime);
        cyberTime += 1.1f;
        Call(view, "Paint", network, now, cyberTime, true);
    }

    /// <summary>Percentile fitting: far home bases must not shrink the objective cluster below 60% of the view.</summary>
    private static void CheckClusterFill(object desk, object detachment)
    {
        object map = desk.GetType().GetProperty("Map", Hidden).GetValue(desk);
        object board = map.GetType().GetProperty("Board", Hidden).GetValue(map);
        var focus = (Rect)board.GetType().GetProperty("Focus", Hidden).GetValue(board);
        int count = (int)detachment.GetType().GetProperty("ObjectiveCount", Hidden).GetValue(detachment);
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        MethodInfo objective = detachment.GetType().GetMethod("Objective", Hidden);
        MethodInfo project = board.GetType().GetMethod("Project", Hidden);
        for (int i = 0; i < count; i++)
        {
            object o = objective.Invoke(detachment, new object[] { i });
            float x = (float)o.GetType().GetField("X").GetValue(o), z = (float)o.GetType().GetField("Z").GetValue(o);
            var p = (Vector2)project.Invoke(board, new object[] { x, z });
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }
        float fill = Mathf.Max((maxX - minX) / focus.width, (maxY - minY) / focus.height);
        Check(fill >= 0.6f, "The objective cluster fills only " + (fill * 100f).ToString("0") + "% of the desk's map focus.");
    }

    private static object PlanningFixture(Type detachmentType, out double now)
    {
        Type kindType = Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.ObjectiveKind", true);
        object detachment = Activator.CreateInstance(detachmentType);
        Call(detachment, "BeginObjectives");
        var fixture = new (string Kind, int Anchor, float X, float Z, int Threat, int Radars, bool Hostile, string Name)[]
        {
            ("Town", 101, 14000f, 6000f, 5, 0, true, "KERSEY"),
            ("AirDefence", 102, 26000f, -4000f, 4, 3, true, "AIR DEFENCE 26/-4"),
            ("Airfield", 103, 21000f, 15000f, 7, 1, true, "NORTH RIDGE AIRFIELD"),
            ("Outpost", 104, -9000f, 22000f, 0, 0, false, "LIGHTHOUSE POINT"),
            ("Town", 105, 34000f, 9000f, 11, 2, true, "PORT SAINT MARIE"),
            ("Airfield", 106, 41000f, -18000f, 9, 2, true, "VIGIL CAY NAVAL AIRB"),
            ("Town", 107, 5000f, -21000f, 1, 0, false, "VALE"),
            ("Outpost", 108, 30000f, 27000f, 3, 0, true, "HILL 402"),
            ("AirDefence", 109, 47000f, 4000f, 6, 4, true, "AIR DEFENCE 47/4")
        };
        foreach (var o in fixture)
            Call(detachment, "ReportObjective", Enum.Parse(kindType, o.Kind), o.Anchor, o.X, o.Z, o.Threat, o.Radars, o.Hostile, o.Name);
        Call(detachment, "EndObjectives");
        now = 30.0;
        return detachment;
    }

    private static void Present(Component window, object room)
    {
        var canvas = window.GetComponent<Canvas>();
        canvas.enabled = true;
        bool shown = (bool)window.GetType().GetMethod("Present", Hidden).Invoke(window, new[] { room, null, null });
        Check(shown, "The window must present a registered room.");
        var group = ((RectTransform)window.GetType().GetProperty("FrameRect", Hidden).GetValue(window)).GetComponent<CanvasGroup>();
        var frame = (RectTransform)window.GetType().GetProperty("FrameRect", Hidden).GetValue(window);
        Check(Mathf.Approximately(group.alpha, 1f) && Mathf.Approximately(frame.localScale.x, 1f),
            "Reduced motion must open on the final frame (alpha " + group.alpha + ", scale " + frame.localScale.x + ").");
        Check(Mathf.Approximately((float)Get(room, "entrance"), 1f), "Reduced motion must hand the room its final entrance state.");
    }

    private static void Frontline(object desk)
    {
        object map = desk.GetType().GetProperty("Map", Hidden).GetValue(desk);
        object board = map.GetType().GetProperty("Board", Hidden).GetValue(map);
        Type pointType = Mod.GetType("BoscaliSummer.Framework.Contracts.FrontlineTracePoint", true);
        int count = 24;
        Array points = Array.CreateInstance(pointType, count);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float z = Mathf.Lerp(-34000f, 36000f, t);
            float x = 8000f + 7000f * Mathf.Sin(t * 5f) + 6000f * t;
            points.SetValue(Activator.CreateInstance(pointType, x, z), i);
        }
        Call(board, "SetFrontline", points, new[] { count }, 1);
        Call(map, "FrontChanged");
    }

    private static void PaintDesk(object desk, object detachment, double now)
    {
        SetField(support, "opsReceived", Time.unscaledTime);
        Call(desk, "Paint", detachment, now, 12.5f, true);
    }

    private static void RoomChecks(Component window, object room, string state, float canvasW, float canvasH, bool populated)
    {
        Canvas.ForceUpdateCanvases();
        var layer = (RectTransform)window.GetType().GetProperty("RoomLayer", Hidden).GetValue(window);
        foreach (TMP_Text text in layer.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!text.gameObject.activeInHierarchy || text.text.Length == 0 || !text.enabled) continue;
            text.ForceMeshUpdate();
            Check(!text.isTextTruncated, state + " text truncated: " + NodePath(text.transform) + " '" + text.text + "'");
            Check(text.fontSize >= 9.99f, state + " text below the 10 px floor: " + NodePath(text.transform) + " '" + text.text + "' at " + text.fontSize);
        }
        Rect frame = WindowRect(window);
        var hero = (Rect)room.GetType().GetProperty("Hero", Hidden).GetValue(room);
        Check(hero.width > 100f && hero.height > 100f && hero.xMax <= frame.width + 0.5f && hero.yMax <= frame.height + 0.5f,
            state + " must declare a hero inside the room: " + hero);
        List<Rect> sections = Sections(room);
        Check(sections.Count >= 3, state + " must declare its sections.");
        float covered = Coverage(sections, frame.width, frame.height);
        float floor = populated ? 0.88f : 0.75f;
        Check(covered >= floor, state + " leaves " + ((1f - covered) * 100f).ToString("0.0") + "% of the room empty (sections cover " +
                               (covered * 100f).ToString("0.0") + "%).");
        if (Mathf.Approximately(canvasW, 1920f))
            foreach (Rect s in sections)
                foreach (float rail in LegacyRails)
                    Check(Mathf.Abs(frame.x + s.x - rail) > 8f, state + " places a section on the legacy rail x " + rail + ": " + s);
        Check(frame.x >= 40f && frame.y >= 39.9f && canvasW - frame.xMax >= 40f && canvasH - frame.yMax >= 39.9f,
            state + " window margin below 40 px: " + frame);
        CheckRootOnly(window, canvasW, canvasH);
        CheckOpaque(layer, frame, state);
        CheckLabels(room, state);
    }

    /// <summary>The room paints its whole interior: an opaque graphic spans the room.</summary>
    private static void CheckOpaque(RectTransform layer, Rect frame, string state)
    {
        bool found = false;
        foreach (Image image in layer.GetComponentsInChildren<Image>(false))
        {
            if (image.color.a < 0.99f) continue;
            Rect r = image.rectTransform.rect;
            if (r.width * image.rectTransform.lossyScale.x >= frame.width - 1f && r.height * image.rectTransform.lossyScale.y >= frame.height - 1f)
            {
                found = true;
                break;
            }
        }
        Check(found, state + " must paint its whole interior with an opaque surface.");
    }

    private static void CheckLabels(object room, string state)
    {
        PropertyInfo mapInfo = room.GetType().GetProperty("Map", Hidden);
        if (mapInfo == null) return;
        object map = mapInfo.GetValue(room);
        var rects = (Rect[])map.GetType().GetProperty("PlacedRects", Hidden).GetValue(map);
        int count = (int)map.GetType().GetProperty("PlacedCount", Hidden).GetValue(map);
        for (int i = 0; i < count; i++)
        for (int j = 0; j < i; j++)
        {
            Rect a = new Rect(rects[i].x, -rects[i].y, rects[i].width, rects[i].height);
            Rect b = new Rect(rects[j].x, -rects[j].y, rects[j].width, rects[j].height);
            Check(!a.Overlaps(b), state + " board labels overlap: " + a + " and " + b);
        }
    }

    private static List<Rect> Sections(object room)
    {
        var list = new List<Rect>();
        foreach (Rect r in (IEnumerable)room.GetType().GetProperty("Sections", Hidden).GetValue(room)) list.Add(r);
        return list;
    }

    private static float Coverage(List<Rect> sections, float w, float h)
    {
        const float cell = 8f;
        int cols = Mathf.CeilToInt(w / cell), rows = Mathf.CeilToInt(h / cell), hit = 0;
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < cols; x++)
        {
            var p = new Vector2(x * cell + cell * 0.5f, y * cell + cell * 0.5f);
            foreach (Rect s in sections)
                if (s.Contains(p)) { hit++; break; }
        }
        return hit / (float)(cols * rows);
    }

    /// <summary>No two rooms share a layout: sorted, 16 px-quantised section rects must differ, and overlap under half.</summary>
    private static void CheckUniqueness()
    {
        var names = new List<string>(signatures.Keys);
        for (int i = 0; i < names.Count; i++)
        for (int j = 0; j < i; j++)
        {
            string a = Signature(signatures[names[i]]), b = Signature(signatures[names[j]]);
            Check(a != b, names[i] + " and " + names[j] + " share a layout signature.");
            var setA = new HashSet<string>(a.Split('|'));
            var setB = new HashSet<string>(b.Split('|'));
            int shared = 0;
            foreach (string s in setA) if (setB.Contains(s)) shared++;
            float jaccard = shared / (float)Mathf.Max(1, setA.Count + setB.Count - shared);
            Check(jaccard < 0.5f, names[i] + " and " + names[j] + " reuse " + (jaccard * 100f).ToString("0") + "% of their section rects.");
        }
    }

    private static string Signature(List<Rect> sections)
    {
        var parts = new List<string>();
        foreach (Rect r in sections)
            parts.Add(Q(r.x) + "," + Q(r.y) + "," + Q(r.width) + "," + Q(r.height));
        parts.Sort(StringComparer.Ordinal);
        return string.Join("|", parts);
    }

    private static int Q(float v) => Mathf.RoundToInt(v / 16f);

    // ---- Capture -----------------------------------------------------------------------------

    private static void Shoot(Component window, string name, (string Name, int PixelW, int PixelH, float CanvasW, float CanvasH) screen)
    {
        var canvas = window.GetComponent<Canvas>();
        string file = name + "-" + screen.Name + ".png";
        Capture(canvas.gameObject, screen.CanvasW, screen.CanvasH, screen.PixelW, screen.PixelH, file);
        renders.Add(file);
    }

    private static void Capture(GameObject canvas, float canvasW, float canvasH, int pixelW, int pixelH, string file)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        var cameraObject = new GameObject("Capture", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = canvasH * 0.5f;
        camera.aspect = canvasW / canvasH;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.backgroundColor = Color.black;
        camera.clearFlags = CameraClearFlags.SolidColor;
        var target = new RenderTexture(pixelW, pixelH, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(pixelW, pixelH, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, pixelW, pixelH), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.GetFullPath(file), image.EncodeToPNG());
        RenderTexture.active = null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(target);
    }

    // ---- Reflection helpers ------------------------------------------------------------------

    private static Rect CanvasRect(RectTransform rect, float canvasW, float canvasH)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (Vector3 c in corners)
        {
            minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
            minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
        }
        return new Rect(minX + canvasW * 0.5f, canvasH * 0.5f - maxY, maxX - minX, maxY - minY);
    }

    private static string NodePath(Transform t)
    {
        var builder = new StringBuilder(t.name);
        for (Transform p = t.parent; p != null && builder.Length < 160; p = p.parent) builder.Insert(0, p.name + "/");
        return builder.ToString();
    }

    private static object Get(object target, string field) => target.GetType().GetField(field, Hidden).GetValue(target);

    private static void SetField(object target, string field, object value) =>
        target.GetType().GetField(field, Hidden).SetValue(target, value);

    private static object Call(object target, string method, params object[] args)
    {
        foreach (MethodInfo info in target.GetType().GetMethods(Hidden))
        {
            if (info.Name != method || info.GetParameters().Length != args.Length) continue;
            return info.Invoke(target, args);
        }
        throw new MissingMethodException(target.GetType().Name, method);
    }

    private static void Check(bool value, string message)
    {
        Assertions++;
        if (!value) throw new Exception(message);
    }
}
#endif

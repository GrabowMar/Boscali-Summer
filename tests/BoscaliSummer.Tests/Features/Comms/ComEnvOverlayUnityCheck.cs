#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// Production floating console and projected markers. The host flag is patched only in this
// editor fixture; no mission, native input capture, weather command or transport is exercised.
public static class ComEnvOverlayUnityCheck
{
    private static readonly Assembly Asm = typeof(AvConsole).Assembly;
    private static readonly List<string> Failures = new List<string>();
    private static int checks, captures;
    private static bool isHost;
    private static MaterialPropertyBlock fixtureProperties;
    private static readonly int[,] Sizes = { { 1920, 1080 }, { 1280, 720 }, { 2560, 1080 } };

    public static void Run()
    {
        if (!EnsureTmpEssentials(Run)) return;
        var harmony = new Harmony("boscalisummer.tests.com-env-overlays");
        try
        {
            Directory.CreateDirectory("renders");
            SetExecutablePath("OverlayPreview.exe");
            InitAvionics();
            new GameObject("Events", typeof(UnityEngine.EventSystems.EventSystem));
            // Rendering UI does not run the weather effects. Their owned property blocks are
            // precreated outside MonoBehaviour field initialization for this editor-only owner.
            fixtureProperties = new MaterialPropertyBlock();
            foreach (string effect in new[] { "TerrainRainDressing", "CanopyShaderDressing" })
                harmony.Patch(T("Modules.Weather.Visuals." + effect).GetConstructor(All & ~BindingFlags.Static, null, Type.EmptyTypes, null),
                    transpiler: new HarmonyMethod(typeof(ComEnvOverlayUnityCheck).GetMethod("PropertiesTranspiler", All)));
            harmony.Patch(T("Core.Game.GameAccess").GetMethod("IsServer", All),
                prefix: new HarmonyMethod(typeof(ComEnvOverlayUnityCheck).GetMethod("HostPrefix", All)));
            foreach (AvThemeId theme in (AvThemeId[])Enum.GetValues(typeof(AvThemeId)))
            {
                AvStyleHost.SetTheme(theme);
                for (int size = 0; size < Sizes.GetLength(0); size++)
                {
                    foreach (string state in new[] { "clear-auto", "storm-all", "eye", "fog" })
                        foreach (bool host in new[] { true, false })
                            ConsoleCase(theme, state, host, Sizes[size, 0], Sizes[size, 1], theme == AvThemeId.Steel);
                    foreach (string state in new[] { "empty", "sparse", "long", "bright", "cluster", "cluster-top", "cluster-bottom", "edge", "behind", "filtered" })
                        MarkerCase(theme, state, Sizes[size, 0], Sizes[size, 1], theme == AvThemeId.Steel);
                }
            }
        }
        catch (Exception e) { Failures.Add("exception: " + e); }
        finally { harmony.UnpatchSelf(); }
        Finish(Failures.Count == 0
            ? "PASS: COMENV OVERLAYS " + checks + " checks, " + captures + " captures"
            : "FAIL (" + Failures.Count + "):\n" + string.Join("\n", Failures), Failures.Count == 0);
    }

    private static bool HostPrefix(ref bool __result) { __result = isHost; return false; }
    private static MaterialPropertyBlock FixtureProperties() => fixtureProperties;
    private static IEnumerable<CodeInstruction> PropertiesTranspiler(IEnumerable<CodeInstruction> code)
    {
        foreach (CodeInstruction instruction in code)
        {
            if (instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo ctor && ctor.DeclaringType == typeof(MaterialPropertyBlock))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = typeof(ComEnvOverlayUnityCheck).GetMethod("FixtureProperties", All);
            }
            yield return instruction;
        }
    }
    private static Type T(string name) => Asm.GetType("BoscaliSummer." + name, true);
    private static object Settings(string module) => Activator.CreateInstance(T("Modules." + module + ".Configuration." + module + "Settings"), All,
        null, new object[] { new ConfigFile(Path.GetFullPath(module + "-overlay.cfg"), false) }, null);
    private static void Check(bool okay, string error) { checks++; if (!okay) Failures.Add(error); }

    private static Camera UiCamera(int width, int height, out RenderTexture rt)
    {
        var camera = new GameObject("UI camera").AddComponent<Camera>();
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = height * .5f;
        camera.transform.position = new Vector3(width * .5f, height * .5f, -100f);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .08f, .09f);
        camera.nearClipPlane = .1f; camera.farClipPlane = 2000f;
        rt = new RenderTexture(width, height, 24); camera.targetTexture = rt;
        return camera;
    }
    private static void CaptureCanvas(Canvas canvas, Camera camera)
    {
        // Overlay projection uses a null camera, so keep the capture canvas on z=0 as
        // the production ScreenSpaceOverlay canvas. Orthographic UI uses a 100-unit plane.
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 100f;
    }

    private static void ConsoleCase(AvThemeId theme, string state, bool host, int width, int height, bool pictures)
    {
        isHost = host;
        string where = "console-" + theme + "-" + width + "x" + height + "-" + state + "-" + (host ? "host" : "client");
        Camera camera = UiCamera(width, height, out RenderTexture rt);
        var services = new GameObject("Console services");
        var manager = (Behaviour)services.AddComponent(T("Modules.Weather.Runtime.WeatherManager")); manager.enabled = false;
        Call(manager, "Configure", Settings("Weather"), null, null);
        byte shown = (byte)(state == "clear-auto" || state == "fog" ? 0 : 6);
        byte sets = (byte)(state == "storm-all" ? 63 : state == "eye" ? 8 : state == "fog" ? 32 : 0);
        object key = Activator.CreateInstance(T("Modules.Weather.Domain.WeatherKey"), All, null,
            new object[] { 42u, 0f, state == "clear-auto", shown, 5f, 60f, sets, (byte)0, state == "eye", 0f, 0f, (byte)(state == "storm-all" ? 7 : 0) }, null);
        Set(manager, "fieldKey", key); Set(manager, "fieldReady", true); Set(manager, "fieldSpan", new Vector2(100000f, 100000f));
        Set(manager, "isManualOverride", state != "clear-auto");
        Set(manager, "forcedRainIntensity", state == "storm-all" ? (object)1f : state == "fog" ? (object)0f : null);
        Call(Get(manager, "field"), "Build", key, 1200f, 50000f, 50000f, 12f, 1f);
        object view = T("Modules.Weather.Presentation.WeatherConsoleWindow").GetMethod("Create", All).Invoke(null, new object[] { manager, services.transform });
        ((Behaviour)view).enabled = false;
        var window = (AvWindow)Get(view, "window");
        CaptureCanvas(window.Root.GetComponentInParent<Canvas>().rootCanvas, camera);
        window.Show(); Call(view, "Refresh");
        for (int i = 0; i < 4; i++) { window.Ticker.TickNow(); window.Body.Relayout(); Canvas.ForceUpdateCanvases(); }
        Gate(window.Root, where, true);
        ScrollRect scroll = window.Root.GetComponentInChildren<ScrollRect>();
        Check(scroll.content.rect.height <= scroll.viewport.rect.height + 1.5f,
            where + ": console requires scrolling while its Rewired mouse is disabled (" + scroll.content.rect.height + " > " + scroll.viewport.rect.height + ")");
        foreach (AvControl control in window.Root.GetComponentsInChildren<AvControl>(true))
        {
            if (control == window.CloseControl) continue;
            bool placementDefault = control == Get(view, "unplaceButton");
            Check(control.Interactable == (host && (!placementDefault || state == "eye")), where + ": wrong authority gate for " + control.name);
        }
        TMP_Text footer = window.Footer.Rect.Find("Text").GetComponent<TMP_Text>();
        Check(footer.text == (host ? "HOST // ALL PLAYERS" : "READ ONLY"), where + ": wrong authority footer " + footer.text);
        foreach (Selectable selectable in window.Root.GetComponentsInChildren<Selectable>(true))
            Check(selectable.navigation.mode == Navigation.Mode.None, where + ": selectable responds to flight navigation " + selectable.name);
        if (pictures) Capture(camera, rt, where);
        window.Hide(); Object.DestroyImmediate(services); Object.DestroyImmediate(camera.gameObject); rt.Release(); Object.DestroyImmediate(rt);
    }

    private static void MarkerCase(AvThemeId theme, string state, int width, int height, bool pictures)
    {
        string where = "markers-" + theme + "-" + width + "x" + height + "-" + state;
        Camera ui = UiCamera(width, height, out RenderTexture rt);
        if (state == "bright") ui.backgroundColor = Color.white;
        var camera = new GameObject("Projection camera").AddComponent<Camera>();
        camera.enabled = false; camera.targetTexture = rt; camera.fieldOfView = 60f;
        camera.transform.position = new Vector3(0f, 0f, -5000f);
        var services = new GameObject("Marker services");
        var manager = (Behaviour)services.AddComponent(T("Modules.Comms.Runtime.CommsManager")); manager.enabled = false;
        object settings = Settings("Comms"); Call(manager, "Configure", settings, null, null);
        object mirrored = Get(manager, "State"); Set(mirrored, "LocalId", 1UL); Set(manager, "<LocalFaction>k__BackingField", 1);
        var view = (Behaviour)services.AddComponent(T("Modules.Comms.Presentation.CommsCockpitMarkers")); view.enabled = false;
        Call(view, "Configure", settings, manager); Call(view, "Build");
        var root = (GameObject)Get(view, "root"); CaptureCanvas(root.GetComponent<Canvas>(), ui);
        Canvas.ForceUpdateCanvases();
        Check(root.GetComponent<CanvasGroup>().blocksRaycasts == false && !root.GetComponent<CanvasGroup>().interactable, where + ": markers capture pointer input");
        object board = Get(mirrored, "Board");
        int count = state == "empty" ? 0 : state == "sparse" ? 3 : 6;
        for (int i = 0; i < count; i++)
        {
            float vx = .22f + i % 3 * .28f, vy = .3f + i / 3 * .36f;
            if (state == "cluster") { vx = .5f + i * .003f; vy = .5f + i * .003f; }
            if (state == "cluster-top") { vx = 1.5f; vy = 1.5f; }
            if (state == "cluster-bottom") { vx = -.5f; vy = -.5f; }
            if (state == "edge") { vx = i % 2 == 0 ? -.5f : 1.5f; vy = .16f + i / 2 * .34f; }
            Vector3 at = camera.ViewportToWorldPoint(new Vector3(vx, vy, state == "behind" ? -6000f : 18000f));
            GlobalPosition global = at.ToGlobalPosition();
            object item = Activator.CreateInstance(T("Modules.Comms.Domain.CommsItem"), true);
            Set(item, "Id", (uint)i + 1); Set(item, "Author", (ulong)i + 1);
            Set(item, "AuthorName", state == "long" || state == "bright" || state.StartsWith("cluster") ? "LONG CALLSIGN WARDEN" : "VIPER " + (i + 1));
            Set(item, "Faction", i % 3 == 0 ? 2 : 1); Set(item, "Style", (byte)(i + 1));
            Set(item, "Points", new[] { Mathf.RoundToInt((float)global.x / 4f), Mathf.RoundToInt((float)global.z / 4f) });
            Set(item, "Height", (float)global.y); Set(item, "Expires", Time.unscaledTime + (state == "filtered" && i < 2 ? -1f : 60f));
            Set(item, "Size", (byte)(state == "long" || state == "bright" || state.StartsWith("cluster") ? 1 : 0));
            Set(item, "Text", state == "long" || state == "bright" || state.StartsWith("cluster") ? "WINCHESTER" : "");
            Call(board, "Add", item, false, null);
        }
        if (state == "filtered") Call(mirrored, "ToggleMute", 3UL);
        Call(view, "Collect"); Call(view, "Render", camera, Vector3.zero); Canvas.ForceUpdateCanvases();
        int actual = ((IList)Get(view, "pings")).Count;
        if (theme == AvThemeId.Steel && state == "sparse")
        {
            var rect = (RectTransform)root.transform;
            var first = ((IList)Get(view, "pings"))[0];
            Vector3 target = new GlobalPosition((float)Get(first, "X"), (float)Get(first, "Height"), (float)Get(first, "Z")).ToLocalPosition();
            File.AppendAllText("geometry.txt", where + ": canvas " + rect.position + " scale " + rect.lossyScale + " rect " + rect.rect +
                " camera " + camera.transform.position + " aspect " + camera.aspect + " target " + target + " screen " + camera.WorldToScreenPoint(target) + "\n");
        }
        Check(actual == (state == "filtered" ? 3 : count), where + ": wrong mirrored ping filtering " + actual);
        Array slots = (Array)Get(view, "markers");
        for (int i = 0; i < actual; i++)
        {
            object marker = slots.GetValue(i);
            object item = ((IList)Get(view, "pings"))[i];
            Vector3 target = new GlobalPosition((float)Get(item, "X"), (float)Get(item, "Height"), (float)Get(item, "Z")).ToLocalPosition();
            Rect screen = ((RectTransform)root.transform).rect;
            object[] projection = { camera, target, screen.width * .5f - 150f, screen.height * .5f - 120f, Vector2.zero, 0f, false };
            Call(view, "Project", projection);
            Check(Vector2.Distance(((RectTransform)Get(marker, "root")).anchoredPosition, (Vector2)projection[4]) < .1f,
                where + ": caption separation moved a projected target glyph");
            if (state == "sparse" || state == "long" || state == "cluster")
                Check(!(bool)projection[6], where + ": on-screen fixture ping was incorrectly edge-clamped");
            TMP_Text caption = (TMP_Text)Get(marker, "label"); caption.ForceMeshUpdate();
            Transform back = caption.transform.parent.Find("Caption backing");
            Color sky = ui.backgroundColor;
            Rgba background = back != null ? back.GetComponent<Image>().color.ToRgba().Over(sky.ToRgba()) : sky.ToRgba();
            float contrast = Rgba.Contrast(caption.color.ToRgba().Over(background), background);
            Check(contrast >= 4.5f, where + ": marker caption contrast " + contrast);
            Rect textBox = LocalTextBounds(caption, (RectTransform)root.transform);
            for (int j = 0; j < actual; j++)
            {
                foreach (string part in new[] { "glyph", "arrow" })
                {
                    var graphic = (Graphic)Get(slots.GetValue(j), part);
                    if (!graphic.gameObject.activeSelf) continue;
                    var corners = new Vector3[4]; graphic.rectTransform.GetWorldCorners(corners);
                    Vector3 a = root.transform.InverseTransformPoint(corners[0]), b = root.transform.InverseTransformPoint(corners[2]);
                    Rect glyph = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                    Check(!textBox.Overlaps(glyph), where + ": caption overlaps fixed " + part);
                }
            }
        }
        Gate((RectTransform)root.transform, where, false);
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) Check(!graphic.raycastTarget, where + ": raycast target " + graphic.name);
        if (pictures) Capture(ui, rt, where);
        Call(view, "ResetForScene"); Object.DestroyImmediate(root); Object.DestroyImmediate(services);
        Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(ui.gameObject); rt.Release(); Object.DestroyImmediate(rt);
    }

    private static void Gate(RectTransform root, string where, bool console)
    {
        var boxes = new List<KeyValuePair<TMP_Text, Rect>>();
        Color ground = console ? AvTheme.Ground : new Color(.06f, .08f, .09f);
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!text.isActiveAndEnabled || string.IsNullOrWhiteSpace(text.text)) continue;
            text.ForceMeshUpdate();
            bool icon = text.name.StartsWith("Icon");
            if (icon) continue;
            Bounds b = text.textBounds; Rect r = text.rectTransform.rect;
            Check(b.size.x <= r.width + 1.5f, where + ": text width overflow '" + text.text + "' (" + b.size.x + " > " + r.width + ")");
            Check(b.size.y <= r.height + 1.5f, where + ": text height overflow '" + text.text + "' (" + b.size.y + " > " + r.height + ")");
            float pixelSize = text.fontSize * text.canvas.scaleFactor;
            Check(pixelSize >= AvTypeScale.Floor - .01f, where + ": physical text size " + pixelSize + " below font floor '" + text.text + "'");
            if (console && text.color.a > .5f)
            {
                Color background = BackgroundOf(text, ground);
                float contrast = Rgba.Contrast(text.color.ToRgba().WithAlpha(1f).Over(background.ToRgba()), background.ToRgba());
                // Disabled host controls are intentionally dim; readable state and footer remain gated.
                AvControl control = text.GetComponentInParent<AvControl>();
                if (control == null || control.Interactable) Check(contrast >= 4.5f, where + ": contrast " + contrast + " '" + text.text + "'");
            }
            Vector3 lo = root.InverseTransformPoint(text.transform.TransformPoint(b.min));
            Vector3 hi = root.InverseTransformPoint(text.transform.TransformPoint(b.max));
            Rect box = Rect.MinMaxRect(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y), Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y));
            Rect bounds = root.rect;
            Check(box.xMin >= bounds.xMin - 2f && box.xMax <= bounds.xMax + 2f && box.yMin >= bounds.yMin - 2f && box.yMax <= bounds.yMax + 2f,
                where + ": text leaves " + (console ? "window" : "screen") + " '" + text.text + "'");
            ScrollRect scroll = text.GetComponentInParent<ScrollRect>();
            if (scroll != null)
            {
                var corners = new Vector3[4]; scroll.viewport.GetWorldCorners(corners);
                Vector3 a = root.InverseTransformPoint(corners[0]), z = root.InverseTransformPoint(corners[2]);
                box = Rect.MinMaxRect(Mathf.Max(box.xMin, a.x), Mathf.Max(box.yMin, a.y), Mathf.Min(box.xMax, z.x), Mathf.Min(box.yMax, z.y));
                if (box.width <= 0f || box.height <= 0f) continue;
            }
            boxes.Add(new KeyValuePair<TMP_Text, Rect>(text, box));
        }
        for (int i = 0; i < boxes.Count; i++) for (int j = i + 1; j < boxes.Count; j++)
        {
            Rect a = boxes[i].Value, b = boxes[j].Value;
            float x = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float y = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            Check(x <= 1.5f || y <= 3f, where + ": captions overlap '" + boxes[i].Key.text + "' / '" + boxes[j].Key.text + "'");
        }
    }

    private static Rect LocalTextBounds(TMP_Text text, RectTransform root)
    {
        Bounds bounds = text.textBounds;
        Vector3 a = root.InverseTransformPoint(text.transform.TransformPoint(bounds.min)), b = root.InverseTransformPoint(text.transform.TransformPoint(bounds.max));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    private static Color BackgroundOf(TMP_Text text, Color ground)
    {
        for (Transform x = text.transform.parent; x != null; x = x.parent)
        {
            foreach (Transform child in x)
            {
                var frame = child.GetComponent<AvFrame>();
                if (frame != null && frame.enabled && frame.Fill && frame.FillColor.a > .35f && child != text.transform)
                {
                    Rgba rgba = frame.FillColor.ToRgba().Over(ground.ToRgba()); return new Color(rgba.R, rgba.G, rgba.B);
                }
            }
            var image = x.GetComponent<Image>();
            if (image != null && image.enabled && image.color.a > .35f) return image.color;
        }
        return ground;
    }
    private static void Capture(Camera camera, RenderTexture rt, string name)
    {
        CapturePng(camera, rt, "renders/" + name + ".png"); captures++;
    }
}
#endif

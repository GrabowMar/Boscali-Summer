#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

public sealed class IssuePreviewCombatHUD : CombatHUD
{
    protected override void Awake() { }
    private void Start() { }
    private void Update() { }
    private void OnDestroy() { }
}

// Production DLL, synthetic native UI shells. No game install changes or live flight claims.
public static class IssueRegressionUnityCheck
{
    private static Type T(string name) => typeof(AvConsole).Assembly.GetType("BoscaliSummer.Modules." + name, true);
    private static object New(string name, params object[] args) => Activator.CreateInstance(T(name), All, null, args, null);

    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;
            SetExecutablePath("IssueRegressionCheck.exe");
            InitAvionics();
            var failures = new List<string>();
            foreach (Action check in new Action[] { Marker, Loadout, MissileSwitch })
                try { check(); } catch (Exception e) { failures.Add(check.Method.Name + ": " + e); }
            if (failures.Count > 0) throw new Exception(string.Join("\n", failures));
            Finish("PASS: #17 final-pose marker scale/heading, #18 live config and same-frame feed stop across board recreation, #19 independent native sets and WMC map selection. Production DLL; synthetic UI, no live flight or multiplayer.\n", true);
        }
        catch (Exception e) { Debug.LogException(e); Finish("FAIL: " + e, false); }
    }

    private static void Marker()
    {
        Camera camera = OrthoCamera("Marker camera", 180f, Color.black);
        var target = new RenderTexture(640, 360, 24); camera.targetTexture = target;
        var root = new GameObject("Map canvas", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
        ((RectTransform)root.transform).sizeDelta = new Vector2(640, 360);
        var go = new GameObject("Native icon", typeof(RectTransform), typeof(Image)); go.transform.SetParent(root.transform, false);
        Image host = go.GetComponent<Image>(); host.rectTransform.sizeDelta = new Vector2(18, 30);
        host.sprite = (Sprite)CallStatic(T("Wing.Presentation.IconFactory"), "Get", "airframe"); host.color = Color.gray;
        object look = CallStatic(T("Wing.Domain.WingMapPresentation"), "Resolve", true, false, true, false, true, true, false);
        Type badges = T("Wing.Presentation.WingMarkerBadge");
        for (int i = 0; i < 12; i++)
        {
            // Repaint samples native sizing; the maximized map then fits/projects the icon.
            host.transform.localScale = Vector3.one * (i % 2 == 0 ? 3f : .3f);
            CallStatic(badges, "Apply", host, look, Color.cyan, "A2 R");
            // Runtime MonoBehaviours do not receive OnEnable in this edit-mode fixture.
            if (i == 0)
            {
                Type pose = typeof(AvConsole).Assembly.GetType("BoscaliSummer.Modules.Wing.Presentation.WingMapBadgePose");
                if (pose != null) Call(go.GetComponentInChildren(pose), "OnEnable");
                foreach (Graphic graphic in go.GetComponentsInChildren<Graphic>())
                    if (graphic.GetType().Name == "WingMapRingGraphic")
                    {
                        // Register the compiled custom Graphic in the editor, as MapOverlayUnityCheck does.
                        CanvasRenderer renderer = graphic.GetComponent<CanvasRenderer>();
                        if (renderer == null) renderer = graphic.gameObject.AddComponent<CanvasRenderer>();
                        typeof(Graphic).GetField("m_CanvasRenderer", All).SetValue(graphic, renderer);
                        typeof(Graphic).GetMethod("OnEnable", All).Invoke(graphic, null);
                    }
            }
            host.transform.localScale = new Vector3(1f, .7f, 1f);
            host.transform.localRotation = Quaternion.Euler(0, 0, i % 3 * 45);
            Canvas.ForceUpdateCanvases();
            foreach (Graphic graphic in go.GetComponentsInChildren<Graphic>())
            {
                if (graphic == host) continue;
                RectTransform rt = graphic.rectTransform;
                Vector2 origin = RectTransformUtility.WorldToScreenPoint(camera, rt.TransformPoint(Vector2.zero));
                Vector2 right = RectTransformUtility.WorldToScreenPoint(camera, rt.TransformPoint(Vector2.right)) - origin;
                Vector2 up = RectTransformUtility.WorldToScreenPoint(camera, rt.TransformPoint(Vector2.up)) - origin;
                Check((right - Vector2.right).sqrMagnitude < .001f && (up - Vector2.up).sqrMagnitude < .001f,
                    "#17 " + graphic.name + " frame " + i + " axes " + right + "/" + up + " after final fitting");
                if (graphic.GetType().Name == "WingMapRingGraphic")
                {
                    Vector2 bottom = RectTransformUtility.WorldToScreenPoint(camera, host.rectTransform.TransformPoint(host.rectTransform.rect.min));
                    Vector2 width = RectTransformUtility.WorldToScreenPoint(camera, host.rectTransform.TransformPoint(new Vector2(9, -15))) - bottom;
                    Vector2 height = RectTransformUtility.WorldToScreenPoint(camera, host.rectTransform.TransformPoint(new Vector2(-9, 15))) - bottom;
                    object geometry = CallStatic(T("Wing.Domain.WingMapBadgeGeometry"), "ForIcon", width.magnitude, height.magnitude);
                    float outer = (float)Get(geometry, "OuterRadiusPixels");
                    Check(Mathf.Abs(rt.rect.width - 2f * (outer + .6f)) < .2f, "#17 ring measures final icon dimensions");
                }
            }
        }
        CapturePng(camera, target, "wing-marker-final-pose.png");
        Call(go.GetComponentInChildren(T("Wing.Presentation.WingMapBadgePose")), "OnDisable");
        Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(target);
    }

    private static void Loadout()
    {
        // Exercise the actual native-set adapter (Cricket/Vagrant symmetry semantics).
        var sets = new[] {
            new HardpointSet { name = "Left outer", hardpoints = new List<Hardpoint> { null }, precludingHardpointSets = new List<byte>() },
            new HardpointSet { name = "Right outer", SymmetryWithPrev = true, hardpoints = new List<Hardpoint> { null }, precludingHardpointSets = new List<byte>() }
        };
        object layout = CallStatic(T("Wing.Domain.WingLoadoutCatalog"), "LayoutOf", (object)sets);
        Check((int)Get(layout, "Stations") == 2, "#19 native symmetric sets remain separate editor stations");
        var keys = new List<string> { "AGM", "AAM" };
        Call(layout, "Normalize", keys); Call(layout, "Pick", keys, 1, "ECM");
        Check(keys[0] == "AGM" && keys[1] == "ECM", "#19 editing right preserves left through the native adapter");
        Camera camera = OrthoCamera("Loadout camera", 200, Color.black);
        var root = new GameObject("Loadout canvas", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
        ((RectTransform)root.transform).sizeDelta = new Vector2(640, 400);
        int selected = -1;
        object ids = New("Wing.Presentation.WmcControls");
        var board = (AvPart)New("Wing.Presentation.WmcStationBoard", (RectTransform)root.transform, ids,
            (Action<int>)(s => selected = s), (Action<int>)(s => { }), (Action<int>)(s => { }));
        Call(board, "SetStations", 2, new List<int> { 1, 1 }, new List<bool> { false, true });
        Call(board, "Bind", 0, "LEFT OUTER · AGM", "100 kg", "ready", true, true, "Left outer");
        Call(board, "Bind", 1, "RIGHT OUTER · ECM", "50 kg", "ready", true, true, "Right outer");
        Call(board, "Rebuild"); board.Place(new AvSlot(20, 20, 600, board.Measure(600)));
        Array boxes = (Array)Get(board, "boxes");
        for (int i = 0; i < 2; i++)
        {
            object box = boxes.GetValue(i);
            AvFrame frame = (AvFrame)Get(box, "Frame");
            frame.GetComponent<AvHit>().Click(null);
            Check(selected == i, "#19 each mirrored map box selects its own station");
        }
        Canvas.ForceUpdateCanvases();
        CapturePng(camera, 640, 400, "wmc-split-pylons.png");
        Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject);
    }

    private static void MissileSwitch()
    {
        var config = new ConfigFile(Path.GetFullPath("missile-switch.cfg"), false) { SaveOnConfigSet = false };
        object settings = New("Hud.Configuration.HudSettings", config);
        var switchEntry = (ConfigEntry<bool>)Get(settings, "PageMissileCam");
        var shell = new GameObject("Native HUD shell"); shell.SetActive(false);
        CombatHUD hud = shell.AddComponent<IssuePreviewCombatHUD>();
        var iconLayer = new GameObject("HUD icon layer", typeof(RectTransform));
        hud.iconLayer = (RectTransform)iconLayer.transform; SceneSingleton<CombatHUD>.i = hud;
        for (int spawn = 0; spawn < 2; spawn++)
        {
            var go = new GameObject("Board shell"); go.SetActive(false);
            object board = go.AddComponent(T("Hud.Presentation.HudBoard"));
            Call(board, "Configure", settings);
            object feed = Get(board, "missileFeed");
            Camera camera = new GameObject("Feed camera", typeof(Camera)).GetComponent<Camera>();
            var picture = new Texture2D(2, 2);
            Set(feed, "cam", camera); Set(feed, "<Texture>k__BackingField", picture);
            Set(feed, "enabledFrame", Time.frameCount); Set(board, "nextRead", float.MaxValue);
            if (spawn > 0)
            {
                Call(board, "Tick");
                Check(T("Hud.Runtime.MissileFeedCamera").GetProperty("Texture", All).GetValue(feed) == null && !camera.enabled,
                    "#18 recreated board preserves OFF before any new option snapshot");
                Set(feed, "<Texture>k__BackingField", picture); camera.enabled = true;
            }
            switchEntry.Value = true; Call(board, "Tick");
            Check(Get(feed, "Texture") == picture, "#18 enabled feed retains its held picture");
            switchEntry.Value = false; Call(board, "Tick");
            Check(T("Hud.Runtime.MissileFeedCamera").GetProperty("Texture", All).GetValue(feed) == null && !camera.enabled,
                "#18 live OFF clears the feed and disables the camera before telemetry throttling");
            Object.DestroyImmediate(go); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(picture);
        }
        SceneSingleton<CombatHUD>.i = null; Object.DestroyImmediate(shell); Object.DestroyImmediate(iconLayer);
    }
}
#endif

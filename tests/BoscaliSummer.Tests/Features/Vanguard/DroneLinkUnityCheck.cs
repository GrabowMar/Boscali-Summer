#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Presentation;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

/// <summary>
/// Renders the VANGUARD drone link surfaces from fixture data: the UAV TERMINAL at 1920 x 1080 (three drones: HAND, WPT, AUTO; one drone;
/// none), the UAV MFD page and the HUD feed card, with the shipped bundle faces, into renders/. Gates every text: fits its box (no spill),
/// 10 px floor. The feed picture is a still (the map fixture) because there is no game camera here. A pass is not visual acceptance: read the PNGs.
/// </summary>
public static class DroneLinkUnityCheck
{
    private static readonly List<string> Failures = new List<string>();
    private static int renders, gated;
    private static Texture2D still;

    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;
            Application.logMessageReceived += (message, _, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Failures.Add("engine " + type + ": " + message);
                if (type == LogType.Warning && message.Contains("font asset") && message.Contains("was not found")) Failures.Add("missing glyph: " + message);
            };
            SetExecutablePath("DroneLinkPreview.exe");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvBundle.ResetForTests();
            Check(File.Exists("avionics-ui.bundle"), "needs the shipped font/icon bundle");
            AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            AvFxDriver.Configure(AvFxTier.Full, false);
            Directory.CreateDirectory("renders");
            still = new Texture2D(2, 2);
            still.LoadImage(File.ReadAllBytes("feed-fixture.png"));

            Terminal("three", Three(), 0);
            Terminal("wpt-linked", Three(), 1);
            Terminal("one", new List<DroneView> { Three()[2] }, 0);
            Terminal("none", new List<DroneView>(), -1);
            Mfd("three", Three(), 0, 896f);
            Mfd("none", new List<DroneView>(), -1, 596f);
            Card("hand", Three(), 0);

            AvBundle.ResetForTests();
            if (Failures.Count > 0) throw new Exception(Failures.Count + " gate failures:\n" + string.Join("\n", Failures.GetRange(0, Math.Min(200, Failures.Count))));
            File.WriteAllText("result.txt", "PASS: UAV terminal, UAV MFD and HUD feed card, " + renders + " renders, " + gated + " gated texts. Fixture data; input, camera and networking untested.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static List<DroneView> Three() => new List<DroneView>
    {
        View(VanguardRole.Drone, "REMORA 1", DroneMode.Hand, new Vector3(1800f, 5200f, 4200f), 92f, 214f, 0.97f, 141f),
        View(VanguardRole.Gunship, "GLAIVE-2S 1", DroneMode.Waypoint, new Vector3(-6400f, 3100f, 11800f), 300f, 64f, 0.78f, 388f),
        View(VanguardRole.Scout, "KESTREL 1", DroneMode.Auto, new Vector3(9100f, 2400f, -15200f), 160f, 41f, 0.31f, 602f),
    };

    private static DroneView View(VanguardRole role, string callsign, DroneMode mode, Vector3 pos, float heading, float speed, float quality, float age)
    {
        var v = new DroneView
        {
            Role = role, Callsign = callsign, Type = DroneLink.Type(role), Job = DroneLink.Job(role), Mode = mode, Position = pos,
            HeadingDeg = heading, SpeedMs = speed, Quality = quality, Age = age, AltM = pos.y,
            RangeM = new Vector2(pos.x, pos.z).magnitude, BearingDeg = DroneLink.Bearing(Vector3.zero, pos),
            Point = pos + new Vector3(2500f, 0f, 3500f),
        };
        v.State = DroneLink.Word(mode) + " · " + AvNum.Fixed(v.RangeM / 1000f, 1) + " KM";
        return v;
    }

    private static DroneLinkService Link(List<DroneView> drones, int linked)
    {
        var go = new GameObject("Link");
        var link = go.AddComponent<DroneLinkService>();
        link.enabled = false;
        link.Fixture(drones, linked, linked >= 0 ? still : null, Vector3.zero, 35f);
        return link;
    }

    private static void Terminal(string name, List<DroneView> drones, int linked)
    {
        renders++;
        DroneLinkService link = Link(drones, linked);
        var canvasObject = new GameObject("TerminalPreview", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvasObject.transform;
        root.sizeDelta = new Vector2(1920f, 1080f);
        DroneTerminalWindow window = DroneTerminalWindow.Make(root, link);
        window.ShowFrame();
        Settle(window.Window.Ticker, root);
        window.Paint();
        Settle(window.Window.Ticker, root);
        Gate(window.Window.Root, "terminal " + name);
        Capture(1920, 1080, 540f, "terminal-" + name);
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(link.gameObject);
    }

    private static void Mfd(string name, List<DroneView> drones, int linked, float height)
    {
        renders++;
        DroneLinkService link = Link(drones, linked);
        var canvasObject = new GameObject("MfdPreview", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvasObject.transform;
        root.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
        var panel = canvasObject.AddComponent<DroneMfdPanel>();
        panel.enabled = false;
        panel.Configure(link, null);
        panel.Build(root, height);
        panel.Refresh();
        AvTicker ticker = root.GetComponentInChildren<AvTicker>();
        Settle(ticker, root);
        foreach (DroneFeedView f in Feeds(panel)) f.Paint(link);
        panel.Refresh();
        Settle(ticker, root);
        Gate(root, "mfd " + name);
        var dump = new System.Text.StringBuilder();
        foreach (Graphic g in root.GetComponentsInChildren<Graphic>(true))
        {
            Rect wr = g.rectTransform.rect;
            Canvas c = g.canvas;
            dump.Append(Where(g.transform, root)).Append(" active=").Append(g.gameObject.activeInHierarchy).Append(" en=").Append(g.enabled)
                .Append(" a=").Append(g.color.a.ToString("0.00")).Append(" pos=").Append(g.rectTransform.position.ToString("0"))
                .Append(" size=").Append(wr.size.ToString("0")).Append(" canvas=").Append(c != null ? c.name + (c.enabled ? "" : "(off)") : "none").AppendLine();
        }
        File.WriteAllText("renders/mfd-" + name + "-dump.txt", dump.ToString());
        Capture((int)AvTokens.PanelWidth * 2, (int)height * 2, height * 0.5f, "mfd-" + name);
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(link.gameObject);
    }

    private static IEnumerable<DroneFeedView> Feeds(DroneMfdPanel panel)
    {
        var f = (DroneFeedView)typeof(DroneMfdPanel).GetField("feed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(panel);
        if (f != null) yield return f;
    }

    private static void Card(string name, List<DroneView> drones, int linked)
    {
        renders++;
        DroneLinkService link = Link(drones, linked);
        var canvasObject = new GameObject("CardPreview", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvasObject.transform;
        root.sizeDelta = new Vector2(440f, 248f);
        var feed = new DroneFeedView(root, "Feed", 12f);
        feed.Place(new AvSlot(4f, 2f, 432f, 243f));
        feed.Paint(link);
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate();
        Gate(root, "card " + name);
        Capture(880, 496, 124f, "card-" + name);
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(link.gameObject);
    }

    private static void Settle(AvTicker ticker, RectTransform root)
    {
        for (int i = 0; i < 6; i++) { ticker?.TickNow(); Canvas.ForceUpdateCanvases(); }
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate();
        // The page-change wipe ends on a 0.3 s unscaled timer that never advances in a batch-mode editor: lift it like the game does.
        foreach (Image cover in root.GetComponentsInChildren<Image>(true)) if (cover.name == "ScanCover") cover.enabled = false;
    }

    private static void Capture(int w, int h, float ortho, string file)
    {
        Camera camera = OrthoCamera("Capture", ortho, new Color(0.05f, 0.06f, 0.07f));
        CapturePng(camera, w, h, Path.GetFullPath("renders/" + file + ".png"));
        Object.DestroyImmediate(camera.gameObject);
    }

    // Every live text fits its box and stays at or above the 10 px floor.
    private static void Gate(RectTransform root, string where)
    {
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text)) continue;
            gated++;
            Rect r = t.rectTransform.rect;
            Vector2 need = t.GetPreferredValues(t.text, t.enableWordWrapping ? r.width : float.PositiveInfinity, float.PositiveInfinity);
            float size = t.enableAutoSizing ? t.fontSize : t.fontSize;
            if (size < 9.95f) Failures.Add(where + ": '" + Short(t.text) + "' below the 10 px floor (" + size.ToString("0.0") + ")");
            if (t.enableAutoSizing) continue; // fitted chrome shrinks to its box
            if (!t.enableWordWrapping && need.x > r.width + 2f) Failures.Add(where + ": '" + Short(t.text) + "' spills its box (" + need.x.ToString("0") + " > " + r.width.ToString("0") + ", " + t.name + ")");
            if (need.y > r.height + 3f && r.height > 0f) Failures.Add(where + ": '" + Short(t.text) + "' overflows its height (" + need.y.ToString("0") + " > " + r.height.ToString("0") + ", " + t.name + ")");
        }
    }

    private static string Where(Transform t, Transform root)
    {
        string p = t.name;
        while (t.parent != null && t.parent != root) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    private static string Short(string s) => s.Length <= 40 ? s.Replace("\n", " / ") : s.Substring(0, 40).Replace("\n", " / ") + "…";
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation;
using BoscaliSummer.Modules.Support.Presentation.Fronts;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

/// <summary>
/// Renders the production OPS front window (shared root, rail and the ORBIT room) from pure fixture data in two states, COLD
/// (a fresh match) and LIVE (readiness 3, ahead, programmes running, birds and tracks), at 1920 x 1080 into renders/. It gates the
/// text the way the other panel checks do (no overflow or truncation, 10 px floor, contrast, no two texts colliding) and fails on
/// any engine error. A pass is not visual acceptance: read the PNGs.
/// </summary>
public static class FrontWindowUnityCheck
{
    private static readonly List<string> Failures = new List<string>();
    private static int gatedTexts;
    private static bool engineError;

    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;
            Application.logMessageReceived += (message, _, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { engineError = true; Failures.Add("engine " + type + ": " + message); }
                if (type == LogType.Warning && message.Contains("font asset") && message.Contains("was not found"))
                    Failures.Add("Shipped font or fallback is missing a displayed glyph: " + message);
            };
            SetExecutablePath("FrontWindowPreview.exe");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvBundle.ResetForTests();
            Check(File.Exists("avionics-ui.bundle"), "The layout check needs the actual shipped font/icon bundle.");
            AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            AvFxDriver.Configure(AvFxTier.Full, false);
            Shader.SetGlobalFloat("_NOA_Now", 1e6f);
            Directory.CreateDirectory("renders");
            FrontMap.TestTexture = MapFixture();
            // Fixture grid shaped like the vanilla one: letters across (A..), digits down (1..), 15 km squares.
            FrontMap.GridSource = (x, z) => ((char)('A' + Mathf.Clamp(Mathf.FloorToInt((x + TheatreW * 0.5f) / 15000f), 0, 25))) + (Mathf.Clamp(Mathf.FloorToInt((TheatreH * 0.5f - z) / 15000f), 0, 98) + 1).ToString();
            CheckAirbaseMapping();
            Render("orbit", "cold", Cold());
            Render("orbit", "live", Live());
            Render("network", "cold", CyberView(false));
            Render("network", "live", CyberView(true));
            Render("shadow", "cold", ShadowView(false));
            Render("shadow", "live", ShadowView(true));
            foreach (bool live in new[] { false, true })
            {
                string name = live ? "live" : "cold";
                RenderMfd("fronts", OpsTab.Fronts, name, PageView(live, false), 896f);
                RenderMfd("perks", OpsTab.Perks, name, PageView(live, false), 896f);
            }
            RenderMfd("perks", OpsTab.Perks, "armed", PageView(true, true), 896f);
            Check(!engineError, "Unity reported an engine error while painting the front window.");
            AvBundle.ResetForTests();
            if (Failures.Count > 0) throw new Exception(Failures.Count + " visual gate failures:\n" + string.Join("\n", Failures.GetRange(0, Math.Min(80, Failures.Count))));
            File.WriteAllText("result.txt", "PASS: OPS front window (ORBIT, NETWORK and SHADOW rooms) COLD and LIVE at 1920x1080 and the OPS MFD page (FRONTS and PERKS: cold, live, armed) at 896, mapped by the production FrontViews / OpsPageViews, with the shipped bundle faces; " + gatedTexts +
                " gated texts (overflow, truncation, 10 px floor, contrast, collisions). Pure fixture data; input, networking and live state untested.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>The shipped terrain2 map picture (copied beside the project by the runner), cropped to the theatre's 2:1.</summary>
    private static Texture MapFixture()
    {
        var src = new Texture2D(2, 2);
        Check(src.LoadImage(File.ReadAllBytes("map-fixture.png")), "map-fixture.png did not load.");
        int w = src.width, h = src.width / 2, y0 = (src.height - h) / 2;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels(src.GetPixels(0, y0, w, h));
        tex.Apply();
        return tex;
    }

    private static void Render(string room, string name, FrontRoomView view)
    {
        var canvasObject = new GameObject("FrontPreview", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvasObject.transform;
        root.sizeDelta = new Vector2(1920f, 1080f);
        Backdrop(root);
        var actions = new NullActions();
        OpsFrontWindow window = OpsFrontWindow.Make(root, actions);
        window.ShowFrame();
        window.Paint(view);
        for (int i = 0; i < 8; i++) { window.Window.Ticker.TickNow(); Canvas.ForceUpdateCanvases(); }
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        window.Paint(view); // second paint: label placement measures real text widths
        for (int i = 0; i < 4; i++) { window.Window.Ticker.TickNow(); Canvas.ForceUpdateCanvases(); }
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        Gate(window.Root, room + " " + name);
        Camera camera = OrthoCamera("Capture", 540f, new Color(0.02f, 0.03f, 0.03f));
        CapturePng(camera, 1920, 1080, Path.GetFullPath("renders/front-" + room + "-" + name + ".png"));
        Object.DestroyImmediate(camera.gameObject);
        Object.DestroyImmediate(canvasObject);
    }

    // ---- The game view behind the popup (so the dimmed backdrop reads) ------------------------------------------------------

    private static void Backdrop(RectTransform root)
    {
        var tex = Terrain(480, 270, 7);
        var go = new GameObject("GameView", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(root, false);
        var img = go.AddComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// <summary>A greyscale value-noise terrain, ridged and shaded: enough to read as a satellite picture.</summary>
    private static Texture2D Terrain(int w, int h, int seed)
    {
        var t = new Texture2D(w, h, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float n = 0f, a = 0.55f, f = 3f;
                for (int o = 0; o < 5; o++) { n += a * Mathf.PerlinNoise(seed * 13.7f + x / (float)w * f, seed * 7.1f + y / (float)h * f); a *= 0.5f; f *= 2.1f; }
                float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(seed + x / (float)w * 6f, y / (float)h * 6f) * 2f - 1f);
                float v = Mathf.Clamp01(0.10f + n * 0.40f + ridge * 0.10f);   // a dim sensor picture: brackets must read on it
                float road = Mathf.Abs(Mathf.Sin((x * 0.021f + y * 0.013f) * 3.1f)) < 0.012f ? 0.22f : 0f;   // faint linear features
                v = Mathf.Clamp01(v + road);
                px[y * w + x] = new Color(v, v, v);
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    // ---- Fixtures: mirrors and inputs, mapped by the production FrontViews ---------------------------------------------------

    private const float TheatreW = 90000f, TheatreH = 60000f, ClockNow = 1000f;
    private static float Wx(float u) => (u - 0.5f) * TheatreW;
    private static float Wz(float v) => (0.5f - v) * TheatreH;

    private static FrontInputs Inputs(bool live) => new FrontInputs
    {
        Allocation = live ? 140 : 20, Now = ClockNow, CostScale = 1f, Faction = "Boscali", Callsign = "VIPER-2",
        Utc = new DateTime(2026, 10, 8, 18, 22, 0, DateTimeKind.Utc), Frame = new MapFrame(TheatreW, TheatreH), Airbases = Bases,
        BirdsDown = 0, BirdPercent = new byte[3], HasBird = new[] { true, live, live }, SpaceLinked = true,
        Family = live ? SpaceFamilyState.Normal : SpaceFamilyState.Degraded, RadarSeconds = live ? 42 : 0, RadarUnavailable = false,
        Teams = live ? 3 : 0, HeldBuilding = live, Price = id => { CallRow r; CallSheet.TryGet(id, out r); return CallSheet.BasePrice(r.Rung); },
    };

    private static readonly AirbaseFix[] Bases =
    {
        new AirbaseFix { X = Wx(0.12f), Z = Wz(0.30f), Name = "Airbase Kestrel Ridge", Side = AirbaseSide.Own },
        new AirbaseFix { X = Wx(0.40f), Z = Wz(0.78f), Name = "Port Halden", Side = AirbaseSide.Own },
        new AirbaseFix { X = Wx(0.55f), Z = Wz(0.18f), Name = "Vance Field", Side = AirbaseSide.Neutral },
        new AirbaseFix { X = Wx(0.86f), Z = Wz(0.40f), Name = "Karsk Forward Base", Side = AirbaseSide.Enemy },
        new AirbaseFix { X = Wx(0.72f), Z = Wz(0.84f), Name = "Dune", Side = AirbaseSide.Enemy },
        new AirbaseFix { X = Wx(0.34f), Z = Wz(0.50f) + 600f, Name = "Under Uplink", Side = AirbaseSide.Own }, // collides with a site: its name must yield
    };

    private static void CheckAirbaseMapping()
    {
        var list = new List<MapAirbaseView>();
        FrontViews.Airbases(list, Bases, new MapFrame(TheatreW, TheatreH));
        Check(list.Count == Bases.Length && list[0].Name == "KESTREL" && list[3].Name == "KARSK" && list[4].Name == "DUNE", "Airbase short names.");
        Check(Mathf.Abs(list[0].U - 0.12f) < 0.001f && Mathf.Abs(list[0].V - 0.30f) < 0.001f && list[3].Side == AirbaseSide.Enemy, "Airbase u/v and side survive the mapping.");
        FrontMap.AxisLabels(FrontMap.GridSource, new Vector2(TheatreW, TheatreH), 6, 4, out string[] c, out string[] r);
        Check(c != null && c[0] == "A" && c[5] == "F" && r[0] == "1" && r[3] == "4", "Axis labels read the grid's own letters across and digits down.");
        FrontMap.AxisLabels((x, z) => "12.4 / -3.1 KM", new Vector2(TheatreW, TheatreH), 6, 4, out c, out r);
        Check(c == null && r == null, "A kilometre-only grid falls back to the numeric labels.");
    }

    private static FrontLogRow LogRow(FrontLogCode code, int a, int b, float ago) => new FrontLogRow { Code = (byte)code, A = (short)a, B = (short)b, Time = ClockNow - ago };

    private static FrontStateData Fronts(bool live)
    {
        var d = new FrontStateData { Protocol = 38, Seq = 1, Now = ClockNow };
        FrontRow s = d.Fronts[0], c = d.Fronts[1], f = d.Fronts[2];
        s.Readiness = (byte)(live ? 3 : 1); s.Superiority = (sbyte)(live ? 34 : 0); s.Budget = (ushort)(live ? 412 : 0); s.Directive = FrontDirective.Recon;
        s.PriorityPct = (byte)(live ? 40 : 33); s.HasFocus = live; s.FocusX = Wx(0.34f); s.FocusZ = Wz(0.50f);
        s.DirectiveLockUntil = live ? 1042f : 0f; s.DirectiveBy = live ? "VIPER-2" : "";
        c.Readiness = (byte)(live ? 2 : 1); c.Superiority = (sbyte)(live ? -6 : 0); c.Budget = (ushort)(live ? 240 : 0); c.Directive = FrontDirective.Balanced; c.PriorityPct = (byte)(live ? 30 : 33);
        c.DirectiveLockUntil = live ? 1030f : 0f; c.DirectiveBy = live ? "VIPER-2" : "";
        f.Readiness = 1; f.Superiority = (sbyte)(live ? -38 : 0); f.Budget = (ushort)(live ? 180 : 0); f.Directive = FrontDirective.Sabotage; f.PriorityPct = (byte)(live ? 30 : 34);
        f.DirectiveLockUntil = live ? 1030f : 0f; f.DirectiveBy = live ? "VIPER-2" : "";
        if (!live)
        {
            s.Log.Add(LogRow(FrontLogCode.Effect, 0, 0, 120f));
            return d;
        }
        s.Queue.Add(new FrontQueueRow { Id = ProgrammeId.LaunchSatellite, Percent = 100, BuildLeft = 130 });
        s.Queue.Add(new FrontQueueRow { Id = ProgrammeId.Readiness, Percent = 20, BuildLeft = 360 });
        c.Queue.Add(new FrontQueueRow { Id = ProgrammeId.DataCenter, Percent = 20, BuildLeft = 180 });
        f.Queue.Add(new FrontQueueRow { Id = ProgrammeId.TrainTeam, Percent = 100, BuildLeft = 70 });
        s.Log.Add(LogRow(FrontLogCode.Directive, (int)FrontDirective.Recon, 0, 400f));
        s.Log.Add(LogRow(FrontLogCode.Counter, 1, 0, 330f));
        s.Log.Add(LogRow(FrontLogCode.Queued, (int)ProgrammeId.LaunchSatellite, 3, 260f));
        s.Log.Add(LogRow(FrontLogCode.Started, (int)ProgrammeId.LaunchSatellite, 240, 200f));
        s.Log.Add(LogRow(FrontLogCode.Effect, 0, 1, 130f));
        s.Log.Add(LogRow(FrontLogCode.Done, (int)ProgrammeId.Readiness, 3, 70f));
        c.Log.Add(LogRow(FrontLogCode.Queued, (int)ProgrammeId.DataCenter, 2, 300f));
        c.Log.Add(LogRow(FrontLogCode.Rebuild, (int)ProgrammeId.EwTruck, 0, 90f));
        f.Log.Add(LogRow(FrontLogCode.Queued, (int)ProgrammeId.TrainTeam, 1, 500f));
        f.Log.Add(LogRow(FrontLogCode.Started, (int)ProgrammeId.TrainTeam, 120, 60f));
        return d;
    }

    private static FrontRoomView Mapped(Front front, bool live)
    {
        var v = new FrontRoomView();
        FrontViews.Common(v, front, Fronts(live), true, Inputs(live));
        return v;
    }

    private static FrontRoomView Cold()
    {
        FrontRoomView v = Mapped(Front.Space, false);
        FrontViews.Orbit(v, Inputs(false));
        v.Sites.Add(Site(0.34f, 0.50f, true, "UPLINK ALPHA", "LIVE"));
        Feed(v.Feed, false);
        return v;
    }

    private static FrontRoomView Live()
    {
        FrontRoomView v = Mapped(Front.Space, true);
        FrontViews.Orbit(v, Inputs(true));
        // The hostile constellation is not mirrored in play; the fixture adds two tracks so the foe cards and tracks are gated too.
        v.Birds.Add(Bird(BirdKind.Radar, false, true, 0.72f, 0.36f, 64f, "KESTREL-9", "HOSTILE RADAR", AvState.Danger));
        v.Birds.Add(Bird(BirdKind.Optical, false, true, 0.84f, 0.62f, 38f, "ORION-3", "HOSTILE OPTICAL", AvState.Caution));
        // One own bird mid-burn so the transfer line, ETA and fuel cost are gated too.
        for (int i = 0; i < v.Birds.Count; i++)
            if (v.Birds[i].Own && v.Birds[i].Kind == BirdKind.Radar)
            {
                OrbitTrackView b = v.Birds[i];
                var g = new GeoBird(0.30f, 0.66f, 100f).Relocate(0f, 0.55f, 0.58f);
                b.FromU = g.FromU; b.FromV = g.FromV; b.TargetU = g.ToU; b.TargetV = g.ToV; b.U = g.U(12f); b.V = g.V(12f);
                b.Relocating = true; b.EtaSeconds = g.Eta(12f); b.BurnCost = g.BurnCost; b.Fuel = g.Fuel;
                v.Birds[i] = b;
            }
        v.Sites.Add(Site(0.34f, 0.50f, true, "UPLINK ALPHA", "LIVE"));
        v.Sites.Add(Site(0.60f, 0.30f, true, "UPLINK BRAVO", "LIVE"));
        v.Sites.Add(Site(0.17f, 0.74f, false, "UPLINK CHARLIE", "DOWN"));
        v.Sites.Add(Site(0.80f, 0.58f, true, "UPLINK DELTA", "LIVE"));
        Feed(v.Feed, true);
        return v;
    }

    private static OrbitSiteView Site(float x, float y, bool live, string name, string state) =>
        new OrbitSiteView { X = x, Y = y, Own = true, Live = live, Name = name, State = state };

    private static OrbitTrackView Bird(BirdKind kind, bool own, bool alive, float u, float vv, float fuel, string callsign, string state, AvState tone) => new OrbitTrackView
    {
        Kind = kind, Own = own, Alive = alive, U = u, V = vv, FromU = u, FromV = vv, TargetU = u, TargetV = vv, Fuel = fuel, Radius = FrontViews.Reach[(int)kind],
        Overhead = Mathf.Abs(u - 0.5f) < 0.2f && Mathf.Abs(vv - 0.5f) < 0.2f, Callsign = callsign, State = state, Tone = tone, Orbit = "GEO 35 786 KM",
    };

    private static void Feed(SpaceFeedView f, bool live)
    {
        f.Source = BirdKind.Optical;
        f.Zoom = 1;
        f.ZoomEnabled = true;
        f.ConstellationMeta = live ? "UPLINKS 3/4 · FAMILY NORMAL" : "UPLINKS 1/1 · FAMILY DEGRADED";
        if (!live)
        {
            f.ImageKind = FeedImageKind.None;
            f.Refusal = "NO PICTURE YET · THE OPTICAL SATELLITE REACHES THE THEATRE IN 4:10";
            f.NoContacts = "NO TRACKS · THE FIRST PASS REVEALS CONTACTS";
            f.NoContactsTone = AvState.Caution;
            f.Status = "OPTICAL · STANDBY";
            f.StatusTone = AvState.Caution;
            f.Pages = 1;
            return;
        }
        f.ImageKind = FeedImageKind.Optical;
        f.Image = Terrain(640, 320, 3);
        f.ImageAspect = 2f;
        f.Refusal = "";
        f.Status = "OPTICAL · GRID 34-11 · 18 KM SWATH · CLOUD 12 % · MID";
        f.StatusTone = AvState.Info;
        f.Page = 0; f.Pages = 2; f.SelectedId = 12; f.SendCount = 2; f.CanConfirm = true; f.CanSend = true;
        string[] title = { "SAM BATTERY", "RADAR SITE", "ARMOUR COLUMN", "SUPPLY TRUCKS", "AIRFIELD", "FUEL DEPOT" };
        string[] cls = { "H", "H", "H", "U", "N", "U" };
        float[] us = { 0.30f, 0.52f, 0.68f, 0.22f, 0.82f, 0.44f }, vs = { 0.62f, 0.34f, 0.70f, 0.28f, 0.50f, 0.80f };
        for (int i = 0; i < 6; i++)
        {
            ProbableClass pc = cls[i] == "H" ? ProbableClass.Hostile : cls[i] == "N" ? ProbableClass.Neutral : ProbableClass.Unknown;
            f.Tiles[i] = new FeedTileView { Present = true, Id = 12 + i, Selected = i == 0, Marked = i == 1 || i == 4, Moving = i == 2 || i == 3, Class = pc, Title = title[i], Percent = (byte)(94 - i * 7) };
            if (i < 5) f.Brackets.Add(new FeedBracketView { Id = 12 + i, U = us[i], V = vs[i], Class = pc, Marked = i == 1, Selected = i == 0, Label = "T" + (12 + i) + " " + title[i].Split(' ')[0] });
        }
    }

    // ---- NETWORK ---------------------------------------------------------------------------------------------------------------

    private static CyberNodeRow CNode(int id, NodeKind kind, float u, float v, bool held = false, bool hopping = false, bool exploit = false) =>
        new CyberNodeRow { Id = id, Kind = kind, X = Wx(u), Z = Wz(v), Held = held, Hopping = hopping, Exploit = exploit };

    private static CyberAnchorRow CAnchor(AnchorKind kind, float u, float v) => new CyberAnchorRow { Kind = kind, Health = AnchorHealth.Live, X = Wx(u), Z = Wz(v) };

    private static CyberStateData CyberMirror(bool live)
    {
        var s = new CyberStateData { Protocol = 38, Seq = 1, Now = ClockNow, Active = true, DataCenterUp = live, IntrusionCap = (byte)(live ? 3 : 1) };
        s.Anchors.Add(CAnchor(AnchorKind.EwTruck, 0.22f, 0.62f));
        if (!live)
        {
            s.Nodes.Add(CNode(1, NodeKind.Radar, 0.30f, 0.55f));
            s.Nodes.Add(CNode(2, NodeKind.SamC2, 0.38f, 0.44f));
            s.Nodes.Add(CNode(3, NodeKind.Relay, 0.47f, 0.36f));
            s.Nodes.Add(CNode(4, NodeKind.Uplink, 0.74f, 0.30f));
            return s;
        }
        s.Anchors.Add(CAnchor(AnchorKind.EwTruck, 0.40f, 0.30f));
        s.Anchors.Add(CAnchor(AnchorKind.DataCenter, 0.12f, 0.80f));
        s.HeldTotal = 2;
        s.Nodes.Add(CNode(1, NodeKind.Radar, 0.30f, 0.55f, held: true));
        s.Nodes.Add(CNode(2, NodeKind.SamC2, 0.38f, 0.44f, held: true, exploit: true));
        s.Nodes.Add(CNode(3, NodeKind.Relay, 0.47f, 0.36f, hopping: true));
        s.Nodes.Add(CNode(4, NodeKind.Radar, 0.46f, 0.22f));
        s.Nodes.Add(CNode(5, NodeKind.Uplink, 0.62f, 0.30f));
        s.Nodes.Add(CNode(6, NodeKind.SamC2, 0.70f, 0.50f));
        s.Nodes.Add(CNode(7, NodeKind.DataCenter, 0.84f, 0.38f));
        s.Nodes.Add(CNode(8, NodeKind.Radar, 0.58f, 0.66f));
        s.Nodes.Add(CNode(9, NodeKind.Relay, 0.74f, 0.72f));
        s.Nodes.Add(CNode(10, NodeKind.Uplink, 0.28f, 0.30f));
        s.Nodes.Add(CNode(11, NodeKind.Relay, 0.66f, 0.38f));
        s.Intrusions.Add(new CyberIntrusionRow { Id = 1, Truck = 0, HopTarget = 3, Own = true, Phase = IntrusionPhase.Hopping, Trace = 71, HopSeconds = 20, HopEndsAt = ClockNow + 7.6f, Held = new[] { 1, 2 }, Operator = "VIPER-2" });
        s.Events.Add(new CyberEventRow { Seq = 1, NodeId = 1, Kind = CyberEventKind.NodeHeld, Node = NodeKind.Radar, Own = true });
        s.Events.Add(new CyberEventRow { Seq = 2, NodeId = 2, Kind = CyberEventKind.NodeHeld, Node = NodeKind.SamC2, Own = true });
        s.Events.Add(new CyberEventRow { Seq = 3, NodeId = 3, Kind = CyberEventKind.HopStarted, Node = NodeKind.Relay, Own = true });
        return s;
    }

    private static FrontRoomView CyberView(bool live)
    {
        FrontRoomView v = Mapped(Front.Cyber, live);
        v.Theatre = "KESTREL SOUND · GRID 30-41 / 07-14";
        FrontViews.Network(v.Network, CyberMirror(live), true, new MapFrame(TheatreW, TheatreH), live ? 2 : 1, ClockNow);
        return v;
    }

    // ---- SHADOW ----------------------------------------------------------------------------------------------------------------

    private static SofTeamRow STeam(int slot, TeamState state, float u, float v, int exposure, int ammo, int odds) =>
        new SofTeamRow { Slot = (byte)slot, State = state, X = Wx(u), Z = Wz(v), Exposure = (byte)exposure, Ammo = (byte)ammo, Odds = (byte)odds };

    private static SofTargetRow STarget(int id, TargetKind kind, AnchorSub sub, float u, float v, bool exploit = false, bool resisted = false) =>
        new SofTargetRow { Id = id, Kind = kind, Sub = sub, X = Wx(u), Z = Wz(v), Exploit = exploit, Resisted = resisted };

    private static SofStateData SofMirror(bool live)
    {
        var s = new SofStateData { Protocol = 38, Seq = 1, Now = ClockNow, Active = true, TeamCap = (byte)(live ? 4 : 2) };
        s.Camps.Add(new SofCampRow { Health = AnchorHealth.Live, X = Wx(0.14f), Z = Wz(0.72f) });
        if (!live) return s;
        s.Camps.Add(new SofCampRow { Health = AnchorHealth.Damaged, X = Wx(0.26f), Z = Wz(0.52f) });
        SofTeamRow a = STeam(0, TeamState.Ready, 0.16f, 0.70f, 12, 80, 78);
        SofTeamRow b = STeam(1, TeamState.OnSite, 0.62f, 0.31f, 31, 64, 71);
        b.Mission = MissionKind.Sabotage; b.TargetId = 2; b.EndsAt = ClockNow + 72f; b.Exploit = true;
        SofTeamRow c = STeam(2, TeamState.Pinned, 0.50f, 0.74f, 96, 22, 32);
        c.Wounded = true; c.Insert = Insertion.Helicopter; c.LiftWaiting = true; c.EndsAt = ClockNow + 100f; c.Mission = MissionKind.Seize; c.TargetId = 7;
        s.Teams.Add(a); s.Teams.Add(b); s.Teams.Add(c);
        s.Targets.Add(STarget(1, TargetKind.Ground, AnchorSub.Uplink, 0.74f, 0.58f));
        s.Targets.Add(STarget(2, TargetKind.Anchor, AnchorSub.Uplink, 0.64f, 0.27f, exploit: true));
        s.Targets.Add(STarget(4, TargetKind.Ground, AnchorSub.Uplink, 0.58f, 0.40f));
        s.Targets.Add(STarget(5, TargetKind.Building, AnchorSub.Uplink, 0.82f, 0.28f));
        s.Targets.Add(STarget(6, TargetKind.Relay, AnchorSub.Uplink, 0.70f, 0.76f));
        s.Targets.Add(STarget(7, TargetKind.Building, AnchorSub.Uplink, 0.46f, 0.82f));
        s.Targets.Add(STarget(8, TargetKind.Ground, AnchorSub.Uplink, 0.88f, 0.62f, resisted: true));
        s.Held.Add(new SofHeldRow { Id = 3, X = Wx(0.34f), Z = Wz(0.34f), Until = ClockNow + 492f });
        s.Held.Add(new SofHeldRow { Id = 9, X = Wx(0.30f), Z = Wz(0.88f), Until = ClockNow + 301f });
        s.Enemies.Add(new SofEnemyRow { X = Wx(0.70f), Z = Wz(0.46f) });
        s.Enemies.Add(new SofEnemyRow { X = Wx(0.55f), Z = Wz(0.62f) });
        s.Enemies.Add(new SofEnemyRow { X = Wx(0.80f), Z = Wz(0.82f) });
        s.TapUntil = ClockNow + 400f; s.TapIntrusions = 2;
        return s;
    }

    private static FrontRoomView ShadowView(bool live)
    {
        FrontRoomView v = Mapped(Front.Sof, live);
        v.Theatre = "KESTREL SOUND · GRID 30-41 / 07-14";
        FrontViews.Shadow(v.Shadow, SofMirror(live), true, live ? CyberMirror(true) : null, new MapFrame(TheatreW, TheatreH), live ? 0 : -1, live ? 4 : 0, ClockNow);
        // The FOB is an OPS programme footprint the mirror does not carry yet; the fixture adds it so the room paints it.
        v.Shadow.HasFob = true; v.Shadow.FobX = 0.22f; v.Shadow.FobY = 0.62f; v.Shadow.FobRadius = 0.16f; v.Shadow.FobName = "FOB ALPHA"; v.Shadow.FobNote = "FOOTPRINT 14 KM · TEAMS RAISE HERE";
        return v;
    }

    // ---- OPS MFD page ----------------------------------------------------------------------------------------------------------

    private static OpsPageView PageView(bool live, bool armed)
    {
        var tiles = new List<CallTile>();
        foreach (CallRow row in CallSheet.Rows)
        {
            bool isArmed = armed && row.Id == SupportActionId.Prsm;
            bool unlocked = live ? row.Rung <= (row.Front == Front.Space ? 3 : row.Front == Front.Cyber ? 2 : 1) : row.Rung <= 1;
            float cooldown = live && row.Id == SupportActionId.Recon ? 12.2f : 0f;
            float balance = live ? 140f : 20f;
            var price = new PriceInputs(false, null, false, 1f, 1f, 1f);
            string unlock = unlocked ? "" : "NEEDS READINESS " + row.Rung;
            tiles.Add(CallsView.Tile(row, CallPricing.Quote(row.Rung, price), unlock, balance, cooldown, isArmed, false, false));
        }
        var v = new OpsPageView { Allocation = live ? 140 : 20 };
        OpsPageViews.Perks(v, tiles);
        OpsPageViews.Cards(v, Fronts(live), true, ClockNow);
        OpsPageViews.Order(v, tiles, AimSource.Map, "GRID 34-11", false, armed ? "ARMED · PRSM STRIKE · PRESS AGAIN OR RIGHT-CLICK MAP" : "", v.Allocation);
        SupportActionId[] favourites = { SupportActionId.Recon, SupportActionId.Prsm, SupportActionId.JtacMark, SupportActionId.Cruise };
        for (int i = 0; i < favourites.Length; i++) foreach (CallTile t in tiles) if (t.Id == favourites[i]) v.Favourites[i] = t;
        return v;
    }

    private static void RenderMfd(string tabName, OpsTab tab, string name, OpsPageView view, float height)
    {
        var canvasObject = new GameObject("MfdPreview", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvasObject.transform;
        root.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
        var panel = canvasObject.AddComponent<CallsPanel>();
        panel.enabled = false;
        panel.BuildForHarness(root, height);
        panel.SelectTab(tab);
        panel.Paint(view);
        for (int i = 0; i < 8; i++) { panel.Ticker.TickNow(); Canvas.ForceUpdateCanvases(); }
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        panel.Paint(view);
        for (int i = 0; i < 4; i++) { panel.Ticker.TickNow(); Canvas.ForceUpdateCanvases(); }
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        string where = "mfd " + tabName + " " + name;
        Gate(panel.ConsoleRoot, where);
        // The page must not run under the footer, nor leave a big empty band above it.
        float lowest = 0f;
        foreach (RectTransform r in panel.ConsoleRoot.GetComponentsInChildren<RectTransform>(false))
        {
            if (!r.gameObject.activeInHierarchy || !(r.name.StartsWith("Front ") || r.name.StartsWith("Perk ") || r.name == "Buttons" || r.name == "Row")) continue;
            if (r.GetComponentInParent<Canvas>() != null && !r.GetComponentInParent<Canvas>().enabled) continue;
            var c = new Vector3[4];
            r.GetWorldCorners(c);
            lowest = Mathf.Min(lowest, panel.ConsoleRoot.InverseTransformPoint(c[0]).y);
        }
        float footerTop = panel.ConsoleRoot.rect.yMin + 76f;
        if (lowest != 0f)
        {
            if (lowest < footerTop - 1f) Fail(where + ": content runs under the footer (" + lowest.ToString("0") + " < " + footerTop.ToString("0") + ")");
            if (lowest - footerTop > 0.12f * height) Fail(where + ": empty band of " + (lowest - footerTop).ToString("0") + " px above the footer");
        }
        Camera camera = OrthoCamera("Capture", height * 0.5f, AvStyleHost.FuiColor("ground", Color.black));
        CapturePng(camera, (int)AvTokens.PanelWidth * 2, (int)height * 2, Path.GetFullPath("renders/mfd-" + tabName + "-" + name + ".png"));
        Object.DestroyImmediate(camera.gameObject);
        Object.DestroyImmediate(canvasObject);
    }

    // ---- Gate (the C2 panel gate, for this window) ---------------------------------------------------------------------------

    private static void Fail(string message) { if (!Failures.Contains(message)) Failures.Add(message); }

    private static void Gate(RectTransform root, string where)
    {
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        var placed = new List<KeyValuePair<TMP_Text, Rect>>(400);
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t)) continue;
            gatedTexts++;
            t.ForceMeshUpdate();
            Rect r = t.rectTransform.rect;
            bool icon = t.name.StartsWith("Icon") || t.GetComponentInParent<AvControl>() == null && t.name.StartsWith("Glyph");
            Bounds b = t.textBounds;
            string tag = where + ": '" + t.text.Replace("\n", " / ") + "' (" + t.name + ")";
            if (!icon)
            {
                if (b.size.x > r.width + 1.5f) Fail(tag + " overflows width " + b.size.x.ToString("0") + " > " + r.width.ToString("0"));
                if (b.size.y > r.height + 1.5f) Fail(tag + " overflows height " + b.size.y.ToString("0") + " > " + r.height.ToString("0"));
                if (t.fontSize < AvTokens.FontMicro - 0.01f) Fail(tag + " below the 10 px floor " + t.fontSize.ToString("0.0"));
                if (t.isTextTruncated) Fail(tag + " is truncated");
            }
            AvControl owner = t.GetComponentInParent<AvControl>();
            bool disabled = owner != null && !owner.Interactable;
            if (!icon && !disabled && t.color.a > 0.5f)
            {
                Color back = BackgroundOf(t, ground);
                float contrast = Rgba.Contrast(t.color.ToRgba().WithAlpha(1f).Over(back.ToRgba()), back.ToRgba());
                if (contrast < 4.5f && !t.text.Contains("<color")) Fail(tag + " contrast " + contrast.ToString("0.00"));
            }
            if (!icon)
            {
                Vector3 lo = root.InverseTransformPoint(t.rectTransform.TransformPoint(b.min));
                Vector3 hi = root.InverseTransformPoint(t.rectTransform.TransformPoint(b.max));
                var ink = Rect.MinMaxRect(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y), Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y));
                if (ink.xMin < root.rect.xMin - 1f || ink.xMax > root.rect.xMax + 1f || ink.yMin < root.rect.yMin - 1f || ink.yMax > root.rect.yMax + 1f)
                    Fail(tag + " outside the window");
                placed.Add(new KeyValuePair<TMP_Text, Rect>(t, ink));
            }
        }
        for (int i = 0; i < placed.Count; i++)
            for (int j = i + 1; j < placed.Count; j++)
            {
                Rect a = placed[i].Value, b = placed[j].Value;
                float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                bool metric = placed[i].Key.name == "Key" && placed[j].Key.name == "Value" || placed[i].Key.name == "Value" && placed[j].Key.name == "Key";
                if (w > 1f && h > 1.5f && !(metric && where.StartsWith("mfd"))) // the kit metric tiles (AvMetric) touch key and value ink boxes by design
                    Fail(where + ": texts overlap '" + placed[i].Key.text.Replace("\n", " / ") + "' (" + placed[i].Key.name + ") and '" + placed[j].Key.text.Replace("\n", " / ") + "' (" + placed[j].Key.name + ")");
            }
    }

    private static Color BackgroundOf(TMP_Text t, Color ground)
    {
        for (Transform x = t.transform.parent; x != null; x = x.parent)
        {
            for (int ci = x.childCount - 1; ci >= 0; ci--)
            {
                Transform child = x.GetChild(ci);
                if (child == t.transform || !child.gameObject.activeInHierarchy) continue;
                var f = child.GetComponent<AvFrame>();
                var img = child.GetComponent<Image>();
                Color c; bool has = false; c = default;
                if (f != null && f.enabled && f.Fill && f.FillColor.a > 0.2f) { c = f.FillColor; has = true; }
                else if (img != null && img.enabled && img.color.a > 0.2f) { c = img.color; has = true; }
                if (!has) continue;
                var corners = new Vector3[4];
                ((RectTransform)child).GetWorldCorners(corners);
                Vector3 centre = t.rectTransform.TransformPoint(t.rectTransform.rect.center);
                if (centre.x >= corners[0].x && centre.x <= corners[2].x && centre.y >= corners[0].y && centre.y <= corners[2].y)
                {
                    Rgba o = c.ToRgba().Over(ground.ToRgba());
                    return new Color(o.R, o.G, o.B);
                }
            }
        }
        return ground;
    }

    // ---- Actions (every press is a no-op here) ---------------------------------------------------------------------------------

    private sealed class NullActions : IFrontActions, ISpaceFeedActions
    {
        public ISpaceFeedActions Orbit => this;
        public void SetDirective(Front front, FrontDirective directive) { }
        public void SetPriority(Front front, int deltaPct) { }
        public void SetFocus(Front front) { }
        public void Queue(Front front, ProgrammeId id) { }
        public void Donate(Front front, ProgrammeId id, int amount) { }
        public void SelectFront(Front front) { }
        public void SelectNode(int nodeId) { }
        public void Hop(int nodeId) { }
        public void Burn(int nodeId) { }
        public void Drop(int nodeId) { }
        public void SelectTeam(int slot) { }
        public void SelectTarget(int targetId) { }
        public void TeamOrder(int slot, int verb) { }
        public void TeamMission(int slot, int missionKind, int targetId) { }
        public void Touch() { }
        public void RelocateBird(int birdKind, float u, float v) { }
        public void SelectEntry(int id) { }
        public void PrevPage() { }
        public void NextPage() { }
        public void Confirm() { }
        public void Send() { }
        public void SetSource(BirdKind source) { }
        public void CycleZoom() { }
    }
}
#endif

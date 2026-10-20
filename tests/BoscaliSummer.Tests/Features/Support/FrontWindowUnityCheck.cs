#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation;
using BoscaliSummer.Modules.Support.Presentation.Fronts;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using OpsSiteRow = BoscaliSummer.Modules.Support.Domain.Ops.OpsSiteRow;
using SiteState = BoscaliSummer.Modules.Support.Domain.Ops.SiteState;
using static UnityCheckHarness;

/// <summary>
/// Renders the production OPS front window (shared root, rail and the ORBIT, NETWORK and SHADOW rooms) from pure fixture data, COLD
/// (a fresh match), LIVE (readiness 3, ahead, programmes running, birds, tracks, nodes, teams) and a set of state variants (relocation,
/// stale picture, locked teams, blocked hops, long names, empty and full rosters), at 1920 x 1080 into renders/; and the OPS MFD page
/// (FRONTS, PERKS, TASKED) at 420, 596 and 896 px. It gates the text the way the other panel checks do (no overflow or truncation,
/// 10 px floor, contrast, no two texts colliding) and fails on any engine error. A pass is not visual acceptance: read the PNGs.
/// </summary>
public static class FrontWindowUnityCheck
{
    private static readonly List<string> Failures = new List<string>();
    private static int gatedTexts, renders, scrolledPages;
    private static bool engineError;

    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;
            Vector2 theatre = BoscaliSummer.Core.Game.TheaterFrame.Resolve(); // the map plate's span: GEO footprints are measured in its ground km, as SupportManager does in game
            GeoBird.Aspect = theatre.y / Mathf.Max(1f, theatre.x);
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
            OpsKit.FallbackGrid = FrontMap.GridSource; // the OPS page and the rooms word grids through OpsKit: same short text as the game's map
            CheckAirbaseMapping();
            RenderOrbit();
            RenderNetwork();
            RenderShadow();
            RenderMfds();
            Check(!engineError, "Unity reported an engine error while painting the front window.");
            AvBundle.ResetForTests();
            if (Failures.Count > 0) throw new Exception(Failures.Count + " visual gate failures:\n" + string.Join("\n", Failures.GetRange(0, Math.Min(400, Failures.Count))));
            File.WriteAllText("result.txt", "PASS: OPS front window (ORBIT, NETWORK and SHADOW rooms: cold, live and state variants) at 1920x1080 and the OPS MFD page (FRONTS, PERKS and TASKED at 420, 596 and 896) mapped by the production FrontViews / OpsPageViews, with the shipped bundle faces; " +
                renders + " renders (" + scrolledPages + " MFD pages scroll at their height and were checked top and bottom), " + gatedTexts + " gated texts (overflow, truncation, 10 px floor, contrast, collisions, scroll reach, pinned strip). Pure fixture data; input, networking and live state untested.");
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

    /// <param name="before">Runs on the freshly shown window, before its first Paint (the rail's funding lock).</param>
    /// <param name="between">Runs after the first paint cycle and before the second one (a room's local mode).</param>
    /// <param name="after">Runs on the painted window once the render is captured.</param>
    private static void Render(string room, string name, FrontRoomView view, Action<OpsFrontWindow> before = null, Action<OpsFrontWindow> between = null, Action<OpsFrontWindow> after = null)
    {
        renders++;
        var canvasObject = new GameObject("FrontPreview", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvasObject.transform;
        root.sizeDelta = new Vector2(1920f, 1080f);
        Backdrop(root);
        var actions = new NullActions();
        OpsFrontWindow window = OpsFrontWindow.Make(root, actions);
        window.ShowFrame();
        before?.Invoke(window);
        window.Paint(view);
        for (int i = 0; i < 8; i++) { window.Window.Ticker.TickNow(); Canvas.ForceUpdateCanvases(); }
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        between?.Invoke(window);
        window.Paint(view); // second paint: label placement measures real text widths
        for (int i = 0; i < 4; i++) { window.Window.Ticker.TickNow(); Canvas.ForceUpdateCanvases(); }
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        Gate(window.Root, room + " " + name);
        Camera camera = OrthoCamera("Capture", 540f, new Color(0.02f, 0.03f, 0.03f));
        CapturePng(camera, 1920, 1080, Path.GetFullPath("renders/front-" + room + "-" + name + ".png"));
        Object.DestroyImmediate(camera.gameObject);
        after?.Invoke(window);
        Object.DestroyImmediate(canvasObject);
    }

    private static OrbitRoom OrbitRoomOf(OpsFrontWindow window)
    {
        FieldInfo f = typeof(OpsFrontWindow).GetField("rooms", BindingFlags.NonPublic | BindingFlags.Instance);
        Check(f != null, "OpsFrontWindow no longer has a 'rooms' array: update the harness.");
        var room = ((IFrontRoom[])f.GetValue(window))[(int)Front.Space] as OrbitRoom;
        Check(room != null, "The ORBIT room is not built.");
        return room;
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

    // The faction's own uplink sites (OPS mirror, world metres): a lone one when cold, an up one and a rebuilding one when live.
    private static OpsSiteRow OwnSite(int slot, SiteState state, float u, float v) => new OpsSiteRow { Slot = (byte)slot, State = state, X = Wx(u), Z = Wz(v) };

    private static FrontInputs Inputs(bool live) => new FrontInputs
    {
        Allocation = live ? 140 : 20, Now = ClockNow, CostScale = 1f, Faction = "Boscali", Callsign = "VIPER-2",
        Utc = new DateTime(2026, 10, 8, 18, 22, 0, DateTimeKind.Utc), Frame = new MapFrame(TheatreW, TheatreH), Airbases = Bases,
        BirdsDown = 0, BirdPercent = new byte[3], HasBird = new[] { true, live, live }, SpaceLinked = true,
        Family = live ? SpaceFamilyState.Normal : SpaceFamilyState.Degraded, RadarSeconds = live ? 42 : 0, RadarUnavailable = false,
        Teams = live ? 3 : 0, HeldBuilding = live, Price = id => { CallRow r; CallSheet.TryGet(id, out r); return CallSheet.BasePrice(r.Rung); },
        Sites = live ? new[] { OwnSite(0, SiteState.Up, 0.30f, 0.58f), OwnSite(1, SiteState.Rebuilding, 0.22f, 0.40f) } : new[] { OwnSite(0, SiteState.Up, 0.34f, 0.50f) },
        Uplinks = live ? CyberMirror(true).Nodes : new List<CyberNodeRow>(),
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

    private static FrontStateData Fronts(bool live, Action<FrontStateData> fix = null)
    {
        var d = new FrontStateData { Protocol = 38, Seq = 1, Now = ClockNow };
        FrontRow s = d.Fronts[0], c = d.Fronts[1], f = d.Fronts[2];
        s.Readiness = (byte)(live ? 3 : 1); s.Superiority = (sbyte)(live ? 34 : 0); s.Budget = (ushort)(live ? 412 : 0); s.Directive = FrontDirective.Recon;
        // The focus pin stands off the first uplink site, so the map shows both.
        s.PriorityPct = (byte)(live ? 40 : 33); s.HasFocus = live; s.FocusX = Wx(0.46f); s.FocusZ = Wz(0.40f);
        s.DirectiveLockUntil = live ? 1042f : 0f; s.DirectiveBy = live ? "VIPER-2" : "";
        c.Readiness = (byte)(live ? 2 : 1); c.Superiority = (sbyte)(live ? -6 : 0); c.Budget = (ushort)(live ? 240 : 0); c.Directive = FrontDirective.Balanced; c.PriorityPct = (byte)(live ? 30 : 33);
        c.DirectiveLockUntil = live ? 1030f : 0f; c.DirectiveBy = live ? "VIPER-2" : "";
        f.Readiness = 1; f.Superiority = (sbyte)(live ? -38 : 0); f.Budget = (ushort)(live ? 180 : 0); f.Directive = FrontDirective.Sabotage; f.PriorityPct = (byte)(live ? 30 : 34);
        f.DirectiveLockUntil = live ? 1030f : 0f; f.DirectiveBy = live ? "VIPER-2" : "";
        if (!live)
        {
            s.Log.Add(LogRow(FrontLogCode.Effect, 0, 0, 120f)); // CYBER and SOF keep an empty log: NO EVENTS LOGGED YET
            fix?.Invoke(d);
            return d;
        }
        // SPACE: a full queue (4/4). The head builds, the second funds, the third waits unfunded and the fourth is funded and waits for the slot.
        s.Queue.Add(new FrontQueueRow { Id = ProgrammeId.LaunchSatellite, Percent = 100, BuildLeft = 130 });
        s.Queue.Add(new FrontQueueRow { Id = ProgrammeId.Readiness, Percent = 20, BuildLeft = 360 });
        s.Queue.Add(new FrontQueueRow { Id = ProgrammeId.UplinkSite, Percent = 0, BuildLeft = 180 });
        s.Queue.Add(new FrontQueueRow { Id = ProgrammeId.Asat, Percent = 100, BuildLeft = 180 });
        // CYBER: the data center funds and the lost EW truck is being rebuilt (35 %), so its Rebuild log row is a current blocker.
        c.Queue.Add(new FrontQueueRow { Id = ProgrammeId.DataCenter, Percent = 20, BuildLeft = 180 });
        c.Queue.Add(new FrontQueueRow { Id = ProgrammeId.EwTruck, Percent = 35, BuildLeft = 200 });
        f.Queue.Add(new FrontQueueRow { Id = ProgrammeId.TrainTeam, Percent = 100, BuildLeft = 70 });
        s.Log.Add(LogRow(FrontLogCode.Directive, (int)FrontDirective.Recon, 0, 400f));
        s.Log.Add(LogRow(FrontLogCode.Counter, 1, 0, 330f));
        s.Log.Add(LogRow(FrontLogCode.Queued, (int)ProgrammeId.LaunchSatellite, 3, 260f));
        s.Log.Add(LogRow(FrontLogCode.Started, (int)ProgrammeId.LaunchSatellite, 240, 200f));
        s.Log.Add(LogRow(FrontLogCode.Effect, 0, 1, 192f)); // 3:12 AGO
        s.Log.Add(LogRow(FrontLogCode.Done, (int)ProgrammeId.Readiness, 3, 70f));
        s.Log.Add(LogRow(FrontLogCode.Failed, (int)ProgrammeId.UplinkSite, (int)FrontOutcome.NoSite, 45f)); // the newest line: a refused programme, funds returned
        c.Log.Add(LogRow(FrontLogCode.Queued, (int)ProgrammeId.DataCenter, 2, 300f));
        c.Log.Add(LogRow(FrontLogCode.Rebuild, (int)ProgrammeId.EwTruck, 0, 90f));
        c.Log.Add(LogRow(FrontLogCode.Started, (int)ProgrammeId.DataCenter, 120, 2f)); // NOW
        f.Log.Add(LogRow(FrontLogCode.Queued, (int)ProgrammeId.TrainTeam, 1, 500f));
        f.Log.Add(LogRow(FrontLogCode.Started, (int)ProgrammeId.TrainTeam, 120, 60f));
        fix?.Invoke(d);
        return d;
    }

    // The state variants of the rail and the cards, applied to a Fronts(live) state.
    private static void SpaceHeadFunding(FrontStateData d) => d.Fronts[0].Queue[0] = new FrontQueueRow { Id = ProgrammeId.LaunchSatellite, Percent = 40, BuildLeft = 240 };

    private static void LongNames(FrontStateData d)
    {
        foreach (FrontRow row in d.Fronts) row.DirectiveBy = "HAWK-1-LONGNAME-16";
        d.PriorityBy = "HAWK-1-LONGNAME-16"; d.PriorityLockUntil = ClockNow + 42f;
        d.Now = ClockNow - 125f; // HOST UPDATE 2:05 AGO
    }

    private static void Shares(FrontStateData d, int space, int cyber, int sof)
    {
        d.Fronts[0].PriorityPct = (byte)space; d.Fronts[1].PriorityPct = (byte)cyber; d.Fronts[2].PriorityPct = (byte)sof;
    }

    private static FrontRoomView Mapped(Front front, bool live, Action<FrontStateData> fix = null)
    {
        var v = new FrontRoomView();
        FrontViews.Common(v, front, Fronts(live, fix), true, Inputs(live));
        return v;
    }

    // ---- ORBIT -----------------------------------------------------------------------------------------------------------------

    /// <summary>The ORBIT room, mapped by the production FrontViews; <paramref name="burning"/> puts the live radar bird mid-burn.</summary>
    private static FrontRoomView OrbitOf(bool live, Action<FrontStateData> fix = null, Func<FrontInputs, FrontInputs> inputs = null, bool burning = true)
    {
        FrontInputs c = Inputs(live);
        if (inputs != null) c = inputs(c);
        var v = new FrontRoomView();
        FrontViews.Common(v, Front.Space, Fronts(live, fix), true, c);
        if (!v.PowerLine.StartsWith("POWER ×", StringComparison.Ordinal)) Fail("the front rail does not carry the power: '" + v.PowerLine + "'");
        FrontViews.Orbit(v, c);
        if (live)
        {
            // The hostile constellation is not mirrored in play; the fixture adds two tracks so the foe cards and tracks are gated too.
            v.Birds.Add(Bird(BirdKind.Radar, false, true, 0.72f, 0.36f, 64f, "KESTREL-9", "HOSTILE RADAR", AvState.Danger));
            v.Birds.Add(Bird(BirdKind.Optical, false, true, 0.84f, 0.62f, 38f, "ORION-3", "HOSTILE OPTICAL", AvState.Caution));
            // One own bird mid-burn so the transfer line, ETA and fuel cost are gated too.
            for (int i = 0; burning && i < v.Birds.Count; i++)
                if (v.Birds[i].Own && v.Birds[i].Kind == BirdKind.Radar)
                {
                    OrbitTrackView b = v.Birds[i];
                    var g = new GeoBird(0.30f, 0.66f, 100f).Relocate(0f, 0.55f, 0.58f);
                    b.FromU = g.FromU; b.FromV = g.FromV; b.TargetU = g.ToU; b.TargetV = g.ToV; b.U = g.U(12f); b.V = g.V(12f);
                    b.Relocating = true; b.EtaSeconds = g.Eta(12f); b.BurnCost = g.BurnCost; b.Fuel = g.Fuel;
                    v.Birds[i] = b;
                }
        }
        Feed(v.Feed, live);
        return v;
    }

    private static OrbitSiteView Site(float x, float y, bool live, string name, string state, string grid, string cost, bool rebuilding = false) =>
        new OrbitSiteView { X = x, Y = y, Own = true, Live = live, Rebuilding = rebuilding, Name = name, State = state, Grid = grid, Cost = cost };

    private static OrbitTrackView Bird(BirdKind kind, bool own, bool alive, float u, float vv, float fuel, string callsign, string state, AvState tone) => new OrbitTrackView
    {
        Kind = kind, Own = own, Alive = alive, U = u, V = vv, FromU = u, FromV = vv, TargetU = u, TargetV = vv, Fuel = fuel, Radius = FrontViews.Reach[(int)kind],
        Overhead = Mathf.Abs(u - 0.5f) < 0.2f && Mathf.Abs(vv - 0.5f) < 0.2f, Callsign = callsign, State = state, Tone = tone,
        Slot = "41.03E", Catalog = "CAT 41732", FootprintKm = FrontViews.Reach[(int)kind] * 90f, Orbit = "SLOT 41.03E · CAT 41732",
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
        f.ImageAge = 34f; f.ImageStale = false;
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
        f.Brief = new FeedBriefView { Has = true, Id = 12, Class = ProbableClass.Hostile, Percent = 94, Moving = false, Marked = false, FixedPoint = false, Title = "HOSTILE", Grid = "C4", Left = 14f };
        f.Log.Add(new FeedLogLine { Stamp = "16:41", Text = "WATCHTOWER BURN · -18 % FUEL · ETA 0:42", Tone = AvState.Caution });
        f.Log.Add(new FeedLogLine { Stamp = "16:37", Text = "MARK T13 · CONFIRMED", Tone = AvState.Ready });
        f.Log.Add(new FeedLogLine { Stamp = "16:33", Text = "OPTICAL LOOK · GRID C4 · 5 CONTACTS", Tone = AvState.Info });
        f.Log.Add(new FeedLogLine { Stamp = "16:20", Text = "UPLINK BRAVO REBUILDING", Tone = AvState.Caution });
        f.Log.Add(new FeedLogLine { Stamp = "15:58", Text = "UPLINK BRAVO DESTROYED", Tone = AvState.Danger });
    }

    private static void RenderOrbit()
    {
        Render("orbit", "cold", OrbitOf(false));
        // Every site state: a lone one standing (cold, above), destroyed, and the live pair (standing and rebuilding) beside revealed enemy sites.
        Render("orbit", "down", OrbitOf(false, inputs: c => { c.Sites = new[] { OwnSite(0, SiteState.Down, 0.34f, 0.50f) }; return c; }));
        Render("orbit", "live", OrbitOf(true), w => w.PriorityLock("HAWK 1", ClockNow + 42f)); // another member holds the funding lock
        Render("orbit", "lockyou", OrbitOf(true), w => w.PriorityLock("VIPER-2", ClockNow + 42f)); // the local pilot holds it

        // Hand-built markers: four own sites of every state and a revealed hostile one (more than the cards show, all on the map).
        FrontRoomView sites = OrbitOf(true);
        sites.Sites.Clear();
        sites.Sites.Add(Site(0.34f, 0.50f, true, "UPLINK ALPHA", "LIVE", "GRID C4", "LOSS DEGRADES SPACE"));
        sites.Sites.Add(Site(0.60f, 0.30f, true, "UPLINK BRAVO", "LIVE", "GRID E3", "LOSS DEGRADES SPACE"));
        sites.Sites.Add(Site(0.17f, 0.74f, false, "UPLINK CHARLIE", "REBUILDING", "GRID B5", "SPACE DEGRADED UNTIL REBUILT", rebuilding: true));
        sites.Sites.Add(Site(0.80f, 0.58f, true, "UPLINK DELTA", "LIVE", "GRID F4", "LOSS DEGRADES SPACE"));
        sites.Sites.Add(new OrbitSiteView { X = 0.72f, Y = 0.30f, Own = false, Live = true, Name = "HOSTILE UPL-05", State = "REVEALED", Grid = "GRID H3", Cost = "STRIKE IT TO DEGRADE THEIR SPACE" });
        Render("orbit", "sites", sites);

        // The sensor: an old picture, a posted package, a cockpit warning.
        FrontRoomView stale = OrbitOf(true);
        stale.Feed.ImageAge = 95f; stale.Feed.ImageStale = true;
        Render("orbit", "stale", stale);
        FrontRoomView posted = OrbitOf(true);
        posted.Feed.Posted = true; posted.Feed.SendCount = 2;
        Render("orbit", "posted", posted);
        FrontRoomView threat = OrbitOf(true);
        threat.Feed.ThreatActive = true; threat.Feed.Threat = "WARNING · MISSILE · RWR SPIKE";
        Render("orbit", "threat", threat);

        // Relocation: a proposed point, the prompt before any click, and a point the fuel cannot reach. Esc ends the proposal (the window's hook).
        Render("orbit", "reloc", OrbitOf(true, burning: false), null, w => OrbitRoomOf(w).Preview((int)BirdKind.Radar, true, 0.62f, 0.40f), w =>
        {
            OrbitRoom room = OrbitRoomOf(w);
            Check(room.CancelMode(), "Esc did not end the relocation proposal.");
            Check(!room.CancelMode(), "Esc ended a relocation that was already over.");
        });
        Render("orbit", "relocprompt", OrbitOf(true, burning: false), null, w => OrbitRoomOf(w).Preview((int)BirdKind.Radar, true, -1f, -1f));
        Render("orbit", "relocfuel", OrbitOf(true, inputs: c => { c.Geo = new[] { default(GeoBird), new GeoBird(0.55f, 0.58f, 8f), default(GeoBird) }; return c; }, burning: false),
            null, w => OrbitRoomOf(w).Preview((int)BirdKind.Radar, true, 0.06f, 0.08f));

        // The rail: few allocation points, a lost satellite, no host yet, long names with a stale snapshot, the share limits, the funding phase.
        Render("orbit", "lowalloc", OrbitOf(true, inputs: c => { c.Allocation = 3; return c; }));
        Render("orbit", "lostbird", OrbitOf(true, inputs: c => { c.BirdsDown = 2; c.BirdPercent = new byte[] { 0, 45, 0 }; return c; }, burning: false));
        var waiting = new FrontRoomView();
        FrontViews.Common(waiting, Front.Space, null, false, Inputs(false));
        FrontViews.Orbit(waiting, Inputs(false));
        Feed(waiting.Feed, false);
        Render("orbit", "waiting", waiting);
        Render("orbit", "longname", OrbitOf(true, LongNames, c => { c.Callsign = "VIPER-2-EXTENDED-CALLSIGN"; return c; }), w => w.PriorityLock("HAWK-1-LONGNAME-16", ClockNow + 42f));
        Render("orbit", "floor", OrbitOf(true, d => Shares(d, 5, 90, 5)));
        Render("orbit", "ceiling", OrbitOf(true, d => Shares(d, 95, 3, 2)));
        Render("orbit", "funding", OrbitOf(true, SpaceHeadFunding));
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
        s.HeldTotal = 4; // two held by YOU, one hopping, one held by an ally
        s.Nodes.Add(CNode(1, NodeKind.Radar, 0.30f, 0.55f, held: true));
        s.Nodes.Add(CNode(2, NodeKind.SamC2, 0.38f, 0.44f, held: true, exploit: true));
        s.Nodes.Add(CNode(3, NodeKind.Relay, 0.47f, 0.36f, hopping: true));
        s.Nodes.Add(CNode(4, NodeKind.Radar, 0.46f, 0.22f, held: true)); // the ally's
        s.Nodes.Add(CNode(5, NodeKind.Uplink, 0.62f, 0.30f));
        s.Nodes.Add(CNode(6, NodeKind.SamC2, 0.70f, 0.50f));
        s.Nodes.Add(CNode(7, NodeKind.DataCenter, 0.84f, 0.38f));
        s.Nodes.Add(CNode(8, NodeKind.Radar, 0.58f, 0.66f));
        s.Nodes.Add(CNode(9, NodeKind.Relay, 0.74f, 0.72f));
        s.Nodes.Add(CNode(10, NodeKind.Uplink, 0.28f, 0.30f));
        s.Nodes.Add(CNode(11, NodeKind.Relay, 0.66f, 0.38f));
        s.Nodes.Add(CNode(21, NodeKind.Uplink, 0.78f, 0.35f)); // the enemy marker on the ORBIT map
        s.Intrusions.Add(new CyberIntrusionRow { Id = 1, Truck = 0, HopTarget = 3, Own = true, Phase = IntrusionPhase.Hopping, Trace = 71, HopSeconds = 20, HopEndsAt = ClockNow + 7.6f, Held = new[] { 1, 2 }, Operator = "VIPER-2" });
        s.Intrusions.Add(new CyberIntrusionRow { Id = 2, Truck = 1, Own = false, Phase = IntrusionPhase.Holding, Trace = 34, Held = new[] { 4 }, Operator = "SABRE-3" });
        s.Events.Add(new CyberEventRow { Seq = 1, NodeId = 1, Kind = CyberEventKind.NodeHeld, Node = NodeKind.Radar, Own = true });
        s.Events.Add(new CyberEventRow { Seq = 2, NodeId = 2, Kind = CyberEventKind.NodeHeld, Node = NodeKind.SamC2, Own = true });
        s.Events.Add(new CyberEventRow { Seq = 3, NodeId = 3, Kind = CyberEventKind.HopStarted, Node = NodeKind.Relay, Own = true });
        s.Events.Add(new CyberEventRow { Seq = 4, NodeId = 5, Kind = CyberEventKind.Released, Node = NodeKind.Uplink, Reason = IntrusionEnd.Burned, Own = false });
        // The other side (protocol 42): two enemy holds on our own net, and the owner of our radar #1 is purging it.
        s.Breaches.Add(new CyberBreachRow { Kind = NodeKind.SamC2, X = Wx(0.18f), Z = Wz(0.70f) });
        s.Breaches.Add(new CyberBreachRow { Kind = NodeKind.Radar, X = Wx(0.10f), Z = Wz(0.58f) });
        CyberNodeRow purged = s.Nodes[0]; purged.Purging = true; s.Nodes[0] = purged;
        return s;
    }

    private static SofTeamRow Hunted(SofTeamRow t) { t.Hunted = true; return t; }

    private static FrontRoomView CyberView(bool live, int selected = int.MinValue, Action<CyberStateData> tweak = null, int allocation = -1)
    {
        FrontRoomView v = Mapped(Front.Cyber, live);
        if (allocation >= 0) v.Allocation = allocation;
        v.Theatre = "KESTREL SOUND · GRID 30-41 / 07-14";
        CyberStateData s = CyberMirror(live);
        tweak?.Invoke(s);
        FrontViews.Network(v.Network, s, true, new MapFrame(TheatreW, TheatreH), selected != int.MinValue ? selected : live ? 2 : 1, ClockNow);
        return v;
    }

    private static void RenderNetwork()
    {
        Render("network", "cold", CyberView(false));
        Render("network", "live", CyberView(true), w => w.PriorityLock("VIPER-2", ClockNow + 42f));
        // A node an ally holds: the owner column, the dashed ring, the dead BURN and DROP rows, the ownership next-line.
        Render("network", "ally", CyberView(true, 4));
        // The only EW truck traced and locked out: the session line, the inspector and the map label all say so.
        Render("network", "blocked", CyberView(false, 1, s => { CyberAnchorRow a = s.Anchors[0]; a.LockUntil = ClockNow + 72f; s.Anchors[0] = a; }));
        // Too few allocation points to start an intrusion: HOP is off and says what it needs.
        Render("network", "poor", CyberView(false, 1, allocation: 2));
        // A hot trace and a full net: 16 nodes (the list scrolls, every map label in use).
        Render("network", "hot", CyberView(true, 2, s =>
        {
            s.Nodes[2] = CNode(3, NodeKind.Relay, 0.47f, 0.36f); // no longer hopping
            s.Intrusions[0] = new CyberIntrusionRow { Id = 1, Truck = 0, HopTarget = 0, Own = true, Phase = IntrusionPhase.Holding, Trace = 88, HopSeconds = 0, HopEndsAt = 0, Held = new[] { 1, 2 }, Operator = "VIPER-2" };
            NodeKind[] kinds = { NodeKind.Radar, NodeKind.SamC2, NodeKind.Relay, NodeKind.Uplink };
            for (int i = 0; i < kinds.Length; i++) s.Nodes.Add(CNode(12 + i, kinds[i], 0.10f + 0.27f * i, i % 2 == 0 ? 0.12f : 0.88f)); // CyberGraph.MaxNodes = 16
        }));
        // A host that has not answered for 30 s while the session is hopping: HOP is blocked, BURN and DROP stay open.
        Render("network", "stale", CyberView(true, 5, s => s.Now = ClockNow - 30f));
        // No EW asset standing, and no host state yet.
        FrontRoomView idle = Mapped(Front.Cyber, false);
        FrontViews.Network(idle.Network, new CyberStateData { Protocol = 38, Seq = 1, Now = ClockNow, Active = false }, true, new MapFrame(TheatreW, TheatreH), 0, ClockNow);
        Render("network", "idle", idle);
        FrontRoomView unknown = Mapped(Front.Cyber, false);
        FrontViews.Network(unknown.Network, null, false, new MapFrame(TheatreW, TheatreH), 0, ClockNow);
        Render("network", "unknown", unknown);
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
        a.Mine = true; a.Controller = "VIPER-2"; // YOU
        SofTeamRow b = STeam(1, TeamState.OnSite, 0.62f, 0.31f, 31, 64, 71);
        b.Mission = MissionKind.Sabotage; b.TargetId = 2; b.EndsAt = ClockNow + 72f; b.Exploit = true;
        b.Controller = "RAVEN-1"; // CTRL RAVEN-1: another operator commands it
        SofTeamRow c = STeam(2, TeamState.Pinned, 0.50f, 0.74f, 96, 22, 32);
        c.Wounded = true; c.Insert = Insertion.Helicopter; c.LiftWaiting = true; c.EndsAt = ClockNow + 100f; c.Mission = MissionKind.Seize; c.TargetId = 7;
        c.Controller = ""; // AUTO: OVERLORD's pool
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
        s.Enemies.Add(new SofEnemyRow { X = Wx(0.55f), Z = Wz(0.62f), Near = true, Hunted = true }); // protocol 42: detected near our sites, our QRF on it
        s.Teams[1] = Hunted(s.Teams[1]); // and a QRF hunts our SOF-2
        s.Enemies.Add(new SofEnemyRow { X = Wx(0.80f), Z = Wz(0.82f) });
        s.TapUntil = ClockNow + 400f; s.TapIntrusions = 2;
        s.Events.Add(new SofEventRow { Seq = 1, Kind = SofEventKind.Raised, Slot = 0 });
        s.Events.Add(new SofEventRow { Seq = 2, Kind = SofEventKind.OnSite, Slot = 1, Mission = MissionKind.Sabotage });
        s.Events.Add(new SofEventRow { Seq = 3, Kind = SofEventKind.Pinned, Slot = 2 });
        return s;
    }

    /// <param name="slot">The picked team (default: the first of the live roster, nothing when cold); <paramref name="target"/> the picked target id (default 4 when live).</param>
    /// <param name="draft">Sets the operator's local draft on the view before it is mapped.</param>
    private static FrontRoomView ShadowView(bool live, int slot = int.MinValue, int target = int.MinValue, Action<SofStateData> tweak = null, Action<ShadowRoomView> draft = null)
    {
        FrontRoomView v = Mapped(Front.Sof, live);
        v.Theatre = "KESTREL SOUND · GRID 30-41 / 07-14";
        SofStateData s = SofMirror(live);
        tweak?.Invoke(s);
        draft?.Invoke(v.Shadow);
        FrontViews.Shadow(v.Shadow, s, true, live ? CyberMirror(true) : null, new MapFrame(TheatreW, TheatreH),
            slot != int.MinValue ? slot : live ? 0 : -1, target != int.MinValue ? target : live ? 4 : 0, ClockNow);
        // The FOB is an OPS programme footprint the mirror does not carry yet; the fixture adds it so the room paints it.
        v.Shadow.HasFob = true; v.Shadow.FobX = 0.22f; v.Shadow.FobY = 0.62f; v.Shadow.FobRadius = 0.16f; v.Shadow.FobName = "FOB ALPHA"; v.Shadow.FobNote = "FOOTPRINT 14 KM · TEAMS RAISE HERE";
        return v;
    }

    /// <summary>The first <paramref name="n"/> targets of mixed kinds, with an exploit one and a resisted one; <paramref name="clustered"/> stacks them all on one map spot.</summary>
    private static void Targets(SofStateData s, int n, bool clustered)
    {
        s.Targets.Clear();
        TargetKind[] kinds = { TargetKind.Ground, TargetKind.Anchor, TargetKind.Building, TargetKind.Relay };
        AnchorSub[] subs = { AnchorSub.Uplink, AnchorSub.EwTruck, AnchorSub.DataCenter, AnchorSub.Camp };
        for (int i = 0; i < n; i++)
        {
            float u = clustered ? 0.60f : 0.18f + 0.64f * ((i * 5) % 9) / 8f, v = clustered ? 0.40f : 0.20f + 0.60f * ((i * 7) % 8) / 7f;
            s.Targets.Add(STarget(i + 1, kinds[i % 4], subs[(i / 4) % 4], u, v, exploit: i == 1, resisted: i == 4));
        }
    }

    private static SofTeamRow Replace(SofStateData s, int slot, Func<SofTeamRow, SofTeamRow> edit)
    {
        int at = s.Teams.FindIndex(t => t.Slot == slot);
        s.Teams[at] = edit(s.Teams[at]);
        return s.Teams[at];
    }

    private static void RenderShadow()
    {
        // A two-slot roster shows two cards: a slot the roster has no row for must not sit unplaced over the title.
        Render("shadow", "cold", ShadowView(false), null, null, w =>
        {
            int cards = 0;
            foreach (RectTransform r in w.Root.GetComponentsInChildren<RectTransform>(false)) if (r.name == "TeamCard" && r.gameObject.activeInHierarchy) cards++;
            Check(cards == 2, "A two-slot roster shows " + cards + " team cards.");
        });
        Render("shadow", "live", ShadowView(true));
        // The locked team picked: another operator commands it, every order control is off.
        Render("shadow", "locked", ShadowView(true, 1, 2));
        // The order composer: an enabled issue, a wrong target kind, a recon point with no target.
        Render("shadow", "draft", ShadowView(true, 0, 2, draft: r => r.DraftMission = MissionKind.Sabotage));
        Render("shadow", "wrongkind", ShadowView(true, 0, 2, draft: r => r.DraftMission = MissionKind.Lase));
        Render("shadow", "recon", ShadowView(true, 0, 0, draft: r =>
        {
            r.HasDraft = true; r.DraftU = 0.4f; r.DraftV = 0.6f; r.DraftId = FrontViews.PointId(Wx(0.4f), Wz(0.6f)); r.DraftMission = MissionKind.Recon;
        }));
        // A pick that left the snapshot: a team (slot 3 prints as D-1, the draft is cleared) and a target.
        Render("shadow", "gone", ShadowView(true, 3, 4, draft: r => r.DraftMission = MissionKind.Recon));
        Render("shadow", "targetgone", ShadowView(true, 0, 99));
        // Target lists of every length; twenty of them stacked on one map spot must all stay reachable.
        foreach (int n in new[] { 0, 1, 8, 9, 20 })
        {
            int count = n;
            Render("shadow", "targets" + n, ShadowView(true, 0, count > 0 ? 1 : 0, s => Targets(s, count, count == 20)));
        }
        // The warning strip: nothing wrong, and one worst team with more behind it.
        Render("shadow", "clear", ShadowView(true, 0, 4, s => Replace(s, 2, t => { t.State = TeamState.Ready; t.Exposure = 8; t.Ammo = 70; t.Wounded = false; t.LiftWaiting = false; t.Mission = MissionKind.None; t.TargetId = 0; return t; })));
        Render("shadow", "more", ShadowView(true, 0, 4, s =>
        {
            Replace(s, 2, t => { t.State = TeamState.Lost; return t; });
            Replace(s, 0, t => { t.Ammo = 10; return t; });
        }));
        // A full roster: four teams, no room for the standing orders.
        Render("shadow", "roster4", ShadowView(true, 0, 4, s =>
        {
            SofTeamRow d = STeam(3, TeamState.Moving, 0.34f, 0.62f, 40, 55, 66);
            d.Mission = MissionKind.Lase; d.TargetId = 1; d.HasDest = true; d.DestX = Wx(0.56f); d.DestZ = Wz(0.52f); d.EndsAt = ClockNow + 90f; d.Controller = "";
            s.Teams.Add(d);
        }));
        // The longest controller name the wire carries (SofWire.MaxController = 16).
        Render("shadow", "longctrl", ShadowView(true, 1, 2, s => Replace(s, 1, t => { t.Controller = "RAVEN-ZULU-ALPHA"; return t; })), w => w.PriorityLock("VIPER-2", ClockNow + 42f));
    }

    // ---- OPS MFD page ----------------------------------------------------------------------------------------------------------

    private sealed class PageOpts
    {
        public bool Live, Armed, Steering, EmptyPosts, NoAim;
        /// <summary>The pilot's allocation (default: 140 live, 20 cold).</summary>
        public float Balance = -1f;
        public SupportActionId? Inspected, Pending, LongLock;
        /// <summary>The armed post (default 7 when armed) and the inspected one (PostGone: it left the board).</summary>
        public int ArmedPost = -1, InspectedPost;
        public Action<FrontStateData> Fix;
    }

    private static FeedPoint[] Points(int n)
    {
        var p = new FeedPoint[n];
        for (int i = 0; i < n; i++) p[i] = new FeedPoint { X = Wx(0.08f + 0.15f * i), Z = Wz(0.14f + 0.16f * i) }; // spread over several grid squares
        return p;
    }

    private static OpsPageView PageView(PageOpts o)
    {
        bool live = o.Live;
        float balance = o.Balance >= 0f ? o.Balance : live ? 140f : 20f;
        var tiles = new List<CallTile>();
        foreach (CallRow row in CallSheet.Rows)
        {
            bool isArmed = o.Armed && row.Id == SupportActionId.Prsm;
            bool unlocked = live ? row.Rung <= (row.Front == Front.Space ? 3 : row.Front == Front.Cyber ? 2 : 1) : row.Rung <= 1;
            float cooldown = live && row.Id == SupportActionId.Recon ? 12.2f : 0f;
            var price = new PriceInputs(false, null, false, 1f, 1f, 1f);
            string unlock = unlocked ? "" : "NEEDS READINESS " + row.Rung;
            if (o.LongLock.HasValue && row.Id == o.LongLock.Value) unlock = "NEEDS STRIKE QUALIFICATION";
            tiles.Add(CallsView.Tile(row, CallPricing.Quote(row.Rung, price), unlock, balance, cooldown, isArmed, o.Pending.HasValue && row.Id == o.Pending.Value, false));
        }
        int allocation = o.Balance >= 0f ? (int)o.Balance : live ? 140 : 20;
        var v = new OpsPageView { Allocation = allocation };
        OpsPageViews.Perks(v, tiles);
        OpsPageViews.Cards(v, Fronts(live, o.Fix), true, ClockNow);
        for (int f = 0; f < v.PowerLine.Length; f++)
            if (!v.PowerLine[f].StartsWith("POWER ×", StringComparison.Ordinal) || !(v.Cards[f].Power ?? "").StartsWith("POWER ×", StringComparison.Ordinal))
                Fail("the OPS page does not carry the power of front " + f + ": '" + v.PowerLine[f] + "'");
        OpsPageViews.Order(v, tiles, o.NoAim ? AimSource.None : AimSource.Map, o.NoAim ? "" : "GRID C4", false,
            o.Armed ? "ARMED · PRSM STRIKE · PRESS AGAIN OR RIGHT-CLICK MAP" : "", allocation, o.Inspected);
        int armedPost = o.ArmedPost >= 0 ? o.ArmedPost : o.Armed ? 7 : 0;
        if (live)
        {
            var posts = new List<FeedPost>
            {
                new FeedPost { CallId = 7, Action = SupportActionId.Prsm, WatchOfficer = true, Points = Points(3), Expires = ClockNow + 95f },
                new FeedPost { CallId = 9, Action = SupportActionId.Artillery, Maker = "VIPER 2", Points = Points(1), Price = 6, Expires = ClockNow + 40f, Launching = true, Claimant = "HAWK 1" },
                new FeedPost { CallId = 11, Action = SupportActionId.Cruise, Maker = "VIPER 2", Points = Points(6), Price = 12, Expires = ClockNow + 180f },
                new FeedPost { CallId = 12, Action = SupportActionId.SabotageStrike, Own = true, Points = Points(2), Expires = ClockNow + 14f },
            };
            if (o.EmptyPosts) posts.Clear();
            OpsPageViews.Tasked(v, posts, true, ClockNow, armedPost, false, o.InspectedPost);
            v.CruiseFlying = true; v.CruiseLegs = 2; v.CruiseRoute = "C4 › E5";
            v.Steering = o.Steering;
        }
        else OpsPageViews.Tasked(v, new List<FeedPost>(), true, ClockNow, 0, false); // nothing posted: NO PACKAGES POSTED
        SupportActionId[] favourites = { SupportActionId.Recon, SupportActionId.Prsm, SupportActionId.JtacMark, SupportActionId.Cruise };
        for (int i = 0; i < favourites.Length; i++) foreach (CallTile t in tiles) if (t.Id == favourites[i]) v.Favourites[i] = t;
        return v;
    }

    private static void RenderMfds()
    {
        foreach (float height in new[] { 420f, 596f, 896f })
        {
            string h = height == 896f ? "" : "-h" + (int)height;
            RenderMfd("fronts", OpsTab.Fronts, "cold" + h, PageView(new PageOpts()), height);
            RenderMfd("fronts", OpsTab.Fronts, "live" + h, PageView(new PageOpts { Live = true }), height);
            RenderMfd("fronts", OpsTab.Fronts, "funding" + h, PageView(new PageOpts { Live = true, Fix = SpaceHeadFunding }), height);
            RenderMfd("fronts", OpsTab.Fronts, "longname" + h, PageView(new PageOpts { Live = true, Fix = LongNames }), height);
            RenderMfd("perks", OpsTab.Perks, "cold" + h, PageView(new PageOpts()), height);
            RenderMfd("perks", OpsTab.Perks, "live" + h, PageView(new PageOpts { Live = true }), height);
            RenderMfd("perks", OpsTab.Perks, "armed" + h, PageView(new PageOpts { Live = true, Armed = true }), height);
            RenderMfd("perks", OpsTab.Perks, "noaim" + h, PageView(new PageOpts { Live = true, Armed = true, NoAim = true }), height); // AIM REQUIRED, FIRE off
            RenderMfd("perks", OpsTab.Perks, "low" + h, PageView(new PageOpts { Live = true, Balance = 5f, LongLock = SupportActionId.SabotageStrike, Pending = SupportActionId.Emp }), height);
            RenderMfd("perks", OpsTab.Perks, "inspect" + h, PageView(new PageOpts { Live = true, Armed = true, Inspected = SupportActionId.ReconTeam }), height); // an inspected perk that is not the armed one
            RenderMfd("tasked", OpsTab.Tasked, "cold" + h, PageView(new PageOpts()), height);
            RenderMfd("tasked", OpsTab.Tasked, "live" + h, PageView(new PageOpts { Live = true }), height);
            RenderMfd("tasked", OpsTab.Tasked, "armed" + h, PageView(new PageOpts { Live = true, Armed = true }), height);
            RenderMfd("tasked", OpsTab.Tasked, "other" + h, PageView(new PageOpts { Live = true, ArmedPost = 7, InspectedPost = 11 }), height); // armed one post, inspecting another
            RenderMfd("tasked", OpsTab.Tasked, "gone" + h, PageView(new PageOpts { Live = true, InspectedPost = OpsPageViews.PostGone }), height);
            RenderMfd("tasked", OpsTab.Tasked, "steer" + h, PageView(new PageOpts { Live = true, Steering = true, InspectedPost = 11 }), height); // CANCEL PICK and its hint
        }
    }

    private static Rect LocalRect(RectTransform root, RectTransform r)
    {
        var c = new Vector3[4];
        r.GetWorldCorners(c);
        Vector3 a = root.InverseTransformPoint(c[0]), b = root.InverseTransformPoint(c[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    /// <summary>The lowest bottom edge (console space, y up) among the page's own content parts, 0 when none shows.</summary>
    private static float LowestContent(RectTransform console)
    {
        float lowest = 0f;
        foreach (RectTransform r in console.GetComponentsInChildren<RectTransform>(false))
        {
            if (!r.gameObject.activeInHierarchy || !(r.name.StartsWith("Front ") || r.name.StartsWith("Perk ") || r.name == "Buttons" || r.name == "Row" || r.name == "BayPlate")) continue;
            Canvas canvas = r.GetComponentInParent<Canvas>();
            if (canvas != null && !canvas.enabled) continue;
            lowest = Mathf.Min(lowest, LocalRect(console, r).yMin);
        }
        return lowest;
    }

    private static void RenderMfd(string tabName, OpsTab tab, string name, OpsPageView view, float height)
    {
        renders++;
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
        RectTransform console = panel.ConsoleRoot;
        var body = (RectTransform)console.Find("Body");
        var viewport = (RectTransform)body.Find("Viewport");
        var scroll = body.GetComponent<ScrollRect>();

        // Does the page scroll at this height? By design FRONTS (Core tier) does at 420, PERKS and TASKED do at 420 and 596; the rest must fit.
        float lowest = LowestContent(console), viewBottom = LocalRect(console, viewport).yMin;
        bool scrolls = lowest != 0f && lowest < viewBottom - 1f;
        if (scrolls) scrolledPages++;
        bool mustFit = tab == OpsTab.Fronts && height >= 596f || tab == OpsTab.Perks && height >= 896f;
        if (mustFit && scrolls) Fail(where + ": the page scrolls at " + height + " px (content ends " + (viewBottom - lowest).ToString("0") + " px below the viewport)");

        // A scrolling page is gated through its viewport: what the mask clips is not on screen (the reach check below proves it all scrolls into view).
        Gate(console, where, scrolls ? viewport : null);

        // The pinned order strip: hidden on FRONTS, docked at the body top elsewhere and never moved by the page scroll.
        Transform stripT = console.Find("OrderStrip");
        Rect strip = default;
        if (tab == OpsTab.Fronts)
        {
            if (stripT != null && stripT.gameObject.activeInHierarchy) Fail(where + ": the OrderStrip shows on FRONTS");
        }
        else if (stripT == null || !stripT.gameObject.activeInHierarchy) Fail(where + ": the OrderStrip is missing");
        else
        {
            strip = LocalRect(console, (RectTransform)stripT);
            float bodyTop = LocalRect(console, body).yMax;
            if (strip.yMax > bodyTop + 0.5f || strip.yMax < bodyTop - 6f) Fail(where + ": the OrderStrip top " + strip.yMax.ToString("0.0") + " is not the body top " + bodyTop.ToString("0.0"));
        }

        if (!scrolls)
        {
            // The page must not run under the footer, nor leave a big empty band above it.
            float footerTop = console.rect.yMin + 76f;
            if (lowest != 0f)
            {
                if (lowest < footerTop - 1f) Fail(where + ": content runs under the footer (" + lowest.ToString("0") + " < " + footerTop.ToString("0") + ")");
                if (lowest - footerTop > 0.12f * height) Fail(where + ": empty band of " + (lowest - footerTop).ToString("0") + " px above the footer");
            }
            if (tab == OpsTab.Fronts && height >= 896f)
            {
                Transform bay = body.Find("BayPlate");
                if (bay == null || !bay.gameObject.activeInHierarchy || ((RectTransform)bay).rect.height < 118f) Fail(where + ": the bay plate is not a full cover at " + height + " px");
            }
        }
        else
        {
            // The last part must scroll fully into view, and the strip must not move with the page.
            CapturePanel(panel, height, "mfd-" + tabName + "-" + name + "-top");
            scroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            float after = LowestContent(console);
            if (after < viewBottom - 1.5f) Fail(where + ": the page does not scroll its last part into view (" + (viewBottom - after).ToString("0") + " px stay below the viewport)");
            if (stripT != null && stripT.gameObject.activeInHierarchy)
            {
                Rect moved = LocalRect(console, (RectTransform)stripT);
                if (Mathf.Abs(moved.yMax - strip.yMax) > 0.5f || Mathf.Abs(moved.yMin - strip.yMin) > 0.5f) Fail(where + ": the OrderStrip moved when the page scrolled to the bottom");
            }
            CapturePanel(panel, height, "mfd-" + tabName + "-" + name + "-bottom");
            scroll.verticalNormalizedPosition = 1f;
            Canvas.ForceUpdateCanvases();
        }
        CapturePanel(panel, height, "mfd-" + tabName + "-" + name);
        Object.DestroyImmediate(canvasObject);
    }

    private static void CapturePanel(CallsPanel panel, float height, string file)
    {
        Camera camera = OrthoCamera("Capture", height * 0.5f, AvStyleHost.FuiColor("ground", Color.black));
        CapturePng(camera, (int)AvTokens.PanelWidth * 2, (int)height * 2, Path.GetFullPath("renders/" + file + ".png"));
        Object.DestroyImmediate(camera.gameObject);
    }

    // ---- Gate (the C2 panel gate, for this window) ---------------------------------------------------------------------------

    private static void Fail(string message) { if (!Failures.Contains(message)) Failures.Add(message); }

    /// <param name="scrolled">A scrolling viewport inside <paramref name="root"/>: texts under it count only where the mask shows them.</param>
    private static void Gate(RectTransform root, string where, RectTransform scrolled = null)
    {
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        Rect clip = scrolled != null ? LocalRect(root, scrolled) : default;
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
                bool under = scrolled != null && t.transform.IsChildOf(scrolled);
                if (ink.xMin < root.rect.xMin - 1f || ink.xMax > root.rect.xMax + 1f) Fail(tag + " outside the window");
                else if (!under && (ink.yMin < root.rect.yMin - 1f || ink.yMax > root.rect.yMax + 1f)) Fail(tag + " outside the window");
                if (under)
                {
                    if (!ink.Overlaps(clip)) continue; // masked away at this scroll position
                    ink = Rect.MinMaxRect(Mathf.Max(ink.xMin, clip.xMin), Mathf.Max(ink.yMin, clip.yMin), Mathf.Min(ink.xMax, clip.xMax), Mathf.Min(ink.yMax, clip.yMax));
                }
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

    // The rooms draw tabs, plates and shapes into one FrontVector mesh per box: the gate reads the colour of the triangle under a text from it.
    private static readonly FieldInfo VecPos = typeof(FrontVector).GetField("pos", BindingFlags.NonPublic | BindingFlags.Instance),
        VecCol = typeof(FrontVector).GetField("col", BindingFlags.NonPublic | BindingFlags.Instance),
        VecIdx = typeof(FrontVector).GetField("idx", BindingFlags.NonPublic | BindingFlags.Instance);

    private static float Side(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

    private static bool VectorFill(FrontVector vec, Vector3 world, out Color fill)
    {
        fill = default;
        var pos = (List<Vector2>)VecPos.GetValue(vec);
        var col = (List<Color32>)VecCol.GetValue(vec);
        var idx = (List<int>)VecIdx.GetValue(vec);
        Vector3 l = vec.rectTransform.InverseTransformPoint(world);
        Rect r = vec.rectTransform.rect;
        var p = new Vector2(l.x - r.xMin, r.yMax - l.y); // back into the mesh's own pixels (origin top-left, y down)
        for (int i = idx.Count - 3; i >= 0; i -= 3) // a later triangle draws on top
        {
            Vector2 a = pos[idx[i]], b = pos[idx[i + 1]], c = pos[idx[i + 2]];
            float d1 = Side(p, a, b), d2 = Side(p, b, c), d3 = Side(p, c, a);
            if ((d1 < 0f || d2 < 0f || d3 < 0f) && (d1 > 0f || d2 > 0f || d3 > 0f)) continue;
            Color32 k = col[idx[i]];
            if (k.a < 51) continue; // under 0.2 alpha it is a tint, not a ground
            fill = k;
            return true;
        }
        return false;
    }

    private static Color BackgroundOf(TMP_Text t, Color ground)
    {
        Vector3 centre = t.rectTransform.TransformPoint(t.textBounds.center); // where the glyphs are, not the middle of a wide right-aligned rect
        for (Transform x = t.transform.parent; x != null; x = x.parent)
        {
            for (int ci = x.childCount - 1; ci >= 0; ci--)
            {
                Transform child = x.GetChild(ci);
                if (child == t.transform || !child.gameObject.activeInHierarchy) continue;
                var f = child.GetComponent<AvFrame>();
                var img = child.GetComponent<Image>();
                var vec = child.GetComponent<FrontVector>();
                Color c; bool has = false; c = default;
                if (vec != null && vec.isActiveAndEnabled) { if (VectorFill(vec, centre, out c)) { Rgba o2 = c.ToRgba().Over(ground.ToRgba()); return new Color(o2.R, o2.G, o2.B); } continue; }
                if (f != null && f.enabled && f.Fill && f.FillColor.a > 0.2f) { c = f.FillColor; has = true; }
                else if (img != null && img.enabled && img.color.a > 0.2f) { c = img.color; has = true; }
                if (!has) continue;
                var corners = new Vector3[4];
                ((RectTransform)child).GetWorldCorners(corners);
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
        public void Purge() { }
        public void Sweep() { }
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

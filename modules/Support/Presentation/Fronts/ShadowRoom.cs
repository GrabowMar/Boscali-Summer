using System.Collections.Generic;
using System;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The SOF front's room (R5, spec 7), laid out as an operations board of paper. The map is the presence / infiltration view as an acetate overlay (grease-pencil
    /// routes, china-marker target circles, hatched exposure areas, push-pin camps; queued camps and FOB as ghosts with their timers, held buildings, revealed
    /// targets, enemy teams and the area RECON point). The column: TEAMS &amp; BASES (camps, FOB, a personnel jacket per team with its live verbs, TRAIN, standing
    /// orders: ShadowBasesPanel.cs) and TARGETS (the field strip, filters, one list of index cards whose aim opens in place as its SEND card, HOSTILES NEAR US with
    /// SWEEP: ShadowTargetsPanel.cs). Two local map picks, one at a time: BUILD CAMP (ground we hold) and PLACE FOB (a held building); a miss says why and stays
    /// armed, Esc ends it, and no marker takes a click meanwhile. Open ground clicked with no pick armed is an area RECON point. Paints from
    /// <see cref="ShadowRoomView"/> only. Everything drawn in paper, stamps, pins and pencil is cosmetic flavour of existing fields: it adds no reading and no input.
    /// </summary>
    internal sealed partial class ShadowRoom : IFrontRoom
    {
        private const float TopH = 480f, FootX = 560f, FootRight = 190f;
        private const int Cols = 12, Rows = 8, MaxTeams = 4, MaxTargets = SofWire.MaxTargets, MaxCamps = CampRules.MaxCamps, MaxGhosts = FrontRules.MaxQueue, MaxHeld = 4, MaxEnemies = 4,
            Missions = 5;
        private static readonly string[] OrderWord = { "PUSH", "HOLD", "EXFIL", "LIFT" };
        private static readonly string[] OrderHelp =
        {
            "Push: move faster and 1.5x as visible. Toggle.",
            "Hold: stay put and let exposure fall twice as fast. Toggle.",
            "Exfil: abandon the mission and go home to the nearest camp or held building.",
            "Lift (the helicopter insertion): the team waits up to " + FrontKit.Clock(SofDesk.LiftWaitSeconds) + " for a pilot's helicopter to land within " + (int)SofRules.LiftPickupMetres +
                " m and carry it; carried, it rolls +20 odds on its next mission. Press again to stop waiting.",
        };

        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private float heroW;
        private FrontMapPlate plate;

        private FrontBox situation;
        private FrontMap.AirbaseLayer airbases;
        private readonly TMP_Text[] teamLabels = new TMP_Text[MaxTeams], targetLabels = new TMP_Text[MaxTargets], campLabels = new TMP_Text[MaxCamps],
            ghostLabels = new TMP_Text[MaxGhosts], heldLabels = new TMP_Text[MaxHeld], enemyMarks = new TMP_Text[MaxEnemies];
        private TMP_Text footLabel;
        private readonly Image[] teamHits = new Image[MaxTeams];
        private readonly int[] hitSlots = new int[MaxTeams], hitTargets = new int[MaxTargets];
        private readonly Image[] targetHits = new Image[MaxTargets];
        private readonly Vector2[] hitAt = new Vector2[MaxTargets];
        // Each team's drawn point this paint (stepped aside off a marker already there); TeamStep clears a 34 px team hit beside a 30 px target or team hit.
        private readonly Vector2[] teamAt = new Vector2[MaxTeams];
        private const float TeamStep = 36f;

        // The team jackets on TEAMS & BASES.
        private readonly Card[] cards = new Card[MaxTeams];

        private ShadowRoomView lastView;
        private FrontRoomView last;
        // A map click is a press and a release that stay together: a pan, a zoom or a drag in between does nothing.
        private bool pressed;
        private Vector2 pressAt, pressCenter;
        private int pressZoom;
        private const float MarkerClear = 22f; // 22 covers the corners of the 34x26 team and 30x30 target hit boxes, so one click never selects a marker and sets a point

        public ShadowRoom(IFrontActions actions, FrontSkin skin)
        {
            this.actions = actions;
            this.skin = skin;
        }

        private static Color Own => AvInk.State(AvState.Ready);

        private void Do(Action a) { actions.Touch(); a(); }

        /// <summary>
        /// Picks a team (local, never an order: the one SEND sends) and drops the note of an earlier cleared pick. From the map or a jacket it opens TEAMS &amp; BASES,
        /// where its jacket is; while TARGETS shows, the column stays there (ruling D-R5) and the team becomes the SEND card's.
        /// </summary>
        private void PickTeam(int slot)
        {
            if (lastView != null) lastView.Reason = "";
            if (!targets.Rect.gameObject.activeSelf) wantTab = 0;
            actions.SelectTeam(slot);
        }

        /// <summary>Picks a target by id, from the map or the list: the same call, and it replaces any RECON point. A map click opens TARGETS, where its card is.</summary>
        private void PickTarget(int id)
        {
            if (lastView != null) { lastView.HasPoint = false; lastView.Reason = ""; }
            wantTab = TargetsTab;
            actions.SelectTarget(id);
        }

        /// <summary>The room's column panels in tab order; the window adds the FRONT tab after them.</summary>
        public IReadOnlyList<ColumnTab> Column => columnTabs;
        /// <summary>The column tab a map pick asked for since the last call, or -1.</summary>
        public int TakeTab() { int t = wantTab; wantTab = -1; return t; }
        private ColumnTab[] columnTabs = new ColumnTab[0];
        private int wantTab = -1;
        private float mapH = TopH;

        /// <summary>The TARGETS column tab's index (TEAMS &amp; BASES is 0).</summary>
        private const int TargetsTab = 1;

        public void Build(RectTransform hero, float w, float h, RectTransform column, float cw, float ch)
        {
            heroW = w; mapH = h;
            BuildSituation(hero);
            BuildBases(column, cw, ch);
            BuildTargets(column, cw, ch);
            columnTabs = new[] { new ColumnTab("TEAMS & BASES", AvIcon.UsersGroup, bases.Rect), new ColumnTab("TARGETS", AvIcon.Crosshair, targets.Rect) };
        }

        /// <summary>A map pick armed or ended: the map and TEAMS &amp; BASES show it now.</summary>
        private void Repaint()
        {
            if (last == null) return;
            PaintSituation(last, last.Shadow);
            PaintBases(last);
        }

        // ---- Paper, ink and pencil -----------------------------------------------------------------------------------------

        /// <summary>
        /// The room's paper and inks: fixed hues (this is a desk, whatever the pilot's scheme). Every ink that sits on paper holds 4.5:1 against it,
        /// so the stamp and tab pairings below are the only ones to use there.
        /// </summary>
        private static class Paper
        {
            public static readonly Color Folder = RoomInk.Rgb(0xC9B684), FolderHi = RoomInk.Rgb(0xDBCB99), FolderEdge = RoomInk.Rgb(0x8A7B52),
                Card = RoomInk.Rgb(0xDDD2AE), CardEdge = RoomInk.Rgb(0xA89C75), Form = RoomInk.Rgb(0xD9D3BF), FormEdge = RoomInk.Rgb(0xA39D86),
                Cream = RoomInk.Rgb(0xF6F0DC), Pen = RoomInk.Typewriter, PenDim = RoomInk.Rgb(0x4E432C), Grease = RoomInk.Grease,
                Red = RoomInk.Rgb(0x7F1A12), Olive = RoomInk.Rgb(0x364216), Amber = RoomInk.Rgb(0x5E3700), Blue = RoomInk.Rgb(0x1B4152), Biro = RoomInk.Rgb(0x1F3F8F),
                BoardInk = RoomInk.BoardInk, Hole = RoomInk.Rgb(0x0C0B08), Khaki = RoomInk.Khaki, TabIdle = RoomInk.Rgb(0xA8966A),
                PhotoPaper = RoomInk.Rgb(0xEFE9D6), PhotoDark = RoomInk.Rgb(0x1E261B), BackTop = RoomInk.Rgb(0x9FB1B6), BackLow = RoomInk.Rgb(0x7E9096),
                Silhouette = RoomInk.Rgb(0x25303A), Gear = RoomInk.Rgb(0x3D4A38), Steel = RoomInk.Rgb(0x7E8791),
                GaugeOk = RoomInk.Rgb(0x9DB84A), GaugeWarn = RoomInk.Rgb(0xE8A93A), GaugeAmmo = RoomInk.Rgb(0x6FA3B8),
                Shadow = RoomInk.Rgba(0f, 0f, 0f, 0.4f), Well = RoomInk.Rgb(0x1B160D, 0.92f);

            /// <summary>Text ink of a state on paper.</summary>
            public static Color Of(AvState s) =>
                s == AvState.Ready ? Olive : s == AvState.Caution ? Amber : s == AvState.Danger ? Red : s == AvState.Info ? Blue : PenDim;

            /// <summary>The folder tab of a state (read with <see cref="TabInk"/>).</summary>
            public static Color TabFill(AvState s) =>
                s == AvState.Ready ? RoomInk.Rgb(0x86A03C) : s == AvState.Caution ? RoomInk.Rgb(0xE0A030) : s == AvState.Danger ? RoomInk.Rgb(0xC23327) : s == AvState.Info ? RoomInk.Rgb(0x6FA3B8) : TabIdle;

            public static Color TabInk(AvState s) => s == AvState.Danger ? Cream : Pen;

            public static Color ExposureText(float pct) => pct < 40f ? Olive : pct < 70f ? Amber : Red;

            public static Color ExposureBar(float pct) => pct < 40f ? GaugeOk : pct < 70f ? GaugeWarn : Grease;
        }

        /// <summary>Hand-drawn marks: grease pencil, china marker, pins, clips, photos. Deterministic (a seed fixes every wobble), allocation-free.</summary>
        private static class Hand
        {
            /// <summary>Smooth noise in -1..1: three sines with seed-shifted phases.</summary>
            public static float Noise(int seed, float t)
            {
                float a = seed * 12.9898f;
                return 0.6f * Mathf.Sin(t * 1.3f + a) + 0.3f * Mathf.Sin(t * 3.1f + a * 1.7f) + 0.1f * Mathf.Sin(t * 7.3f + a * 2.3f);
            }

            public static float Hash(int seed, int i)
            {
                float s = Mathf.Sin(seed * 12.9898f + i * 78.233f) * 43758.5453f;
                return s - Mathf.Floor(s);
            }

            /// <summary>A pencil stroke from a to b: sideways wobble fixed by the seed, pinned at both ends.</summary>
            public static void Stroke(FrontVector g, Vector2 a, Vector2 b, float w, Color k, int seed, float amp = 1.6f)
            {
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 1f) return;
                Vector2 n = new Vector2(-d.y, d.x) / len;
                int seg = Mathf.Max(2, Mathf.CeilToInt(len / 9f));
                Vector2 prev = a;
                for (int i = 1; i <= seg; i++)
                {
                    float t = (float)i / seg;
                    Vector2 p = a + d * t + n * (Noise(seed, t * len * 0.08f) * amp * Mathf.Sin(t * Mathf.PI));
                    g.Line(prev, p, w, k);
                    prev = p;
                }
            }

            /// <summary>A china-marker circle: a little over one turn, the radius drifting out so the end overshoots the start.</summary>
            public static void Circle(FrontVector g, Vector2 c, float r, float w, Color k, int seed)
            {
                int steps = Mathf.Clamp(Mathf.CeilToInt(r * 1.1f), 18, 96);
                float a0 = seed * 0.7f;
                Vector2 prev = default;
                for (int i = 0; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    float a = a0 + t * 1.12f * 2f * Mathf.PI;
                    float rr = r * (1f + 0.045f * Noise(seed + 3, t * 6f) + 0.07f * t);
                    Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    if (i > 0) g.Line(prev, p, w, k);
                    prev = p;
                }
            }

            /// <summary>Diagonal hatching clipped to a circle (one chord per step).</summary>
            public static void Hatch(FrontVector g, Vector2 c, float r, float gap, float w, Color k, bool mirror = false)
            {
                Vector2 n = mirror ? new Vector2(0.7071f, -0.7071f) : new Vector2(0.7071f, 0.7071f), t = mirror ? new Vector2(0.7071f, 0.7071f) : new Vector2(0.7071f, -0.7071f);
                for (float d = -r + gap * 0.5f; d < r; d += gap)
                {
                    float h = Mathf.Sqrt(Mathf.Max(0f, r * r - d * d));
                    g.Line(c + n * d - t * h, c + n * d + t * h, w, k);
                }
            }

            public static void DashRect(FrontVector g, float x, float y, float w, float h, float t, Color k, float dash = 4f, float gap = 3f)
            {
                g.Dashed(new Vector2(x, y), new Vector2(x + w, y), t, k, dash, gap);
                g.Dashed(new Vector2(x + w, y), new Vector2(x + w, y + h), t, k, dash, gap);
                g.Dashed(new Vector2(x + w, y + h), new Vector2(x, y + h), t, k, dash, gap);
                g.Dashed(new Vector2(x, y + h), new Vector2(x, y), t, k, dash, gap);
            }

            /// <summary>A push-pin seen from above: cast shadow, rim, head and a highlight.</summary>
            public static void Pin(FrontVector g, Vector2 p, float r, Color head)
            {
                g.Disc(p + new Vector2(r * 0.5f, r * 0.7f), r, RoomInk.Rgba(0f, 0f, 0f, 0.42f), 14);
                g.Disc(p, r + 1f, RoomInk.Rgba(0f, 0f, 0f, 0.55f), 14);
                g.Disc(p, r, head, 14);
                g.Disc(p + new Vector2(-r * 0.32f, -r * 0.36f), r * 0.34f, RoomInk.Rgba(1f, 1f, 1f, 0.8f), 8);
            }

            /// <summary>A punched hole: the dark board shows through.</summary>
            public static void Hole(FrontVector g, float x, float y, float r = 2.6f)
            {
                g.Disc(new Vector2(x, y), r + 0.9f, RoomInk.Rgba(0f, 0f, 0f, 0.3f), 12);
                g.Disc(new Vector2(x, y), r, Paper.Hole, 12);
            }

            /// <summary>A steel paper clip standing out of a top edge: (x, y) is the middle of its lower end, it rises 19 px.</summary>
            public static void Clip(FrontVector g, float x, float y)
            {
                Color k = Paper.Steel, sh = RoomInk.Rgba(0f, 0f, 0f, 0.4f);
                for (int pass = 0; pass < 2; pass++)
                {
                    float o = pass == 0 ? 1.2f : 0f;
                    Color c = pass == 0 ? sh : k;
                    float x0 = x + o, y0 = y + o;
                    g.Line(new Vector2(x0 - 3.6f, y0), new Vector2(x0 - 3.6f, y0 - 14f), 1.6f, c);
                    ArcTop(g, new Vector2(x0, y0 - 14f), 3.6f, 1.6f, c);
                    g.Line(new Vector2(x0 + 3.6f, y0 - 14f), new Vector2(x0 + 3.6f, y0 + 1f), 1.6f, c);
                    g.Line(new Vector2(x0 + 3.6f, y0 + 1f), new Vector2(x0 + 1.4f, y0 + 3f), 1.6f, c);
                    g.Line(new Vector2(x0 + 1.4f, y0 + 3f), new Vector2(x0 - 1f, y0 + 1f), 1.6f, c);
                    g.Line(new Vector2(x0 - 1f, y0 + 1f), new Vector2(x0 - 1f, y0 - 10f), 1.6f, c);
                    ArcTop(g, new Vector2(x0 + 0.4f, y0 - 10f), 1.4f, 1.5f, c);
                    g.Line(new Vector2(x0 + 1.8f, y0 - 10f), new Vector2(x0 + 1.8f, y0 - 2f), 1.5f, c);
                }
            }

            private static void ArcTop(FrontVector g, Vector2 c, float r, float w, Color k)
            {
                Vector2 prev = c + new Vector2(-r, 0f);
                for (int i = 1; i <= 6; i++)
                {
                    float a = Mathf.PI * (1f - i / 6f);
                    Vector2 p = c + new Vector2(Mathf.Cos(a) * r, -Mathf.Sin(a) * r);
                    g.Line(prev, p, w, k);
                    prev = p;
                }
            }

            public static void HalfDisc(FrontVector g, Vector2 c, float rad, Color k)
            {
                Vector2 prev = c + new Vector2(rad, 0f);
                for (int i = 1; i <= 10; i++)
                {
                    float a = Mathf.PI * i / 10f;
                    Vector2 p = c + new Vector2(Mathf.Cos(a) * rad, -Mathf.Sin(a) * rad);
                    g.Tri(c, prev, p, k);
                    prev = p;
                }
            }

            /// <summary>A folder tab: a trapezoid with slanted shoulders and a thin edge.</summary>
            public static void Tab(FrontVector g, float x, float y, float w, float h, Color fill, Color edge)
            {
                const float sl = 5f;
                g.Quad(new Vector2(x, y + h), new Vector2(x + sl, y), new Vector2(x + w - sl, y), new Vector2(x + w, y + h), fill);
                g.Line(new Vector2(x, y + h), new Vector2(x + sl, y), 1f, edge);
                g.Line(new Vector2(x + sl, y), new Vector2(x + w - sl, y), 1f, edge);
                g.Line(new Vector2(x + w - sl, y), new Vector2(x + w, y + h), 1f, edge);
            }

            /// <summary>An ID photograph: paper border, studio backdrop, head-and-shoulders silhouette in one of four headgears; a head bandage when wounded, a grease cross when lost.</summary>
            public static void Bust(FrontVector g, Rect r, int variant, bool wounded, bool lost)
            {
                g.Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f, Paper.PhotoPaper);
                g.Grad(r.x, r.y, r.width, r.height, Paper.BackTop, Paper.BackTop, Paper.BackLow, Paper.BackLow);
                float cx = r.center.x, bottom = r.yMax, rx = r.width * 0.46f, ry = r.height * 0.34f;
                Color sil = Paper.Silhouette;
                Vector2 hub = new Vector2(cx, bottom), prev = new Vector2(cx - rx, bottom);
                for (int i = 1; i <= 12; i++)
                {
                    float a = Mathf.PI * (1f - i / 12f);
                    Vector2 p = new Vector2(cx + Mathf.Cos(a) * rx, bottom - Mathf.Sin(a) * ry);
                    g.Tri(hub, prev, p, sil);
                    prev = p;
                }
                g.Rect(cx - 2.4f, bottom - ry - 4f, 4.8f, 6f, sil);
                Vector2 head = new Vector2(cx, bottom - ry - 8.5f);
                g.Disc(head, 6.4f, sil, 16);
                Color gear = Paper.Gear;
                switch (variant & 3)
                {
                    case 0: HalfDisc(g, head + new Vector2(0f, -0.6f), 7.2f, gear); g.Rect(head.x - 1f, head.y - 2.6f, 9.5f, 1.8f, gear); break;   // patrol cap and brim
                    case 1: HalfDisc(g, head + new Vector2(1.2f, -1.4f), 8f, gear); g.Disc(head + new Vector2(1.2f, -9f), 1f, gear, 6); break;    // beret
                    case 2: HalfDisc(g, head + new Vector2(0f, -0.4f), 8.4f, gear); g.Rect(head.x - 9f, head.y - 1.8f, 18f, 1.8f, gear); break;   // helmet and rim
                    default: HalfDisc(g, head + new Vector2(0f, -1.2f), 6.4f, gear); g.Rect(head.x - 10f, head.y - 2.6f, 20f, 2f, gear); break;   // boonie, wide brim
                }
                if (wounded)
                {
                    g.Quad(new Vector2(head.x - 6.8f, head.y - 3.4f), new Vector2(head.x + 6.8f, head.y - 5.4f), new Vector2(head.x + 6.8f, head.y - 2.2f), new Vector2(head.x - 6.8f, head.y - 0.2f), Paper.Cream);
                    g.Disc(head + new Vector2(2.6f, -3.2f), 1.2f, Paper.Grease, 6);
                }
                if (lost)
                {
                    Stroke(g, new Vector2(r.xMin + 2f, r.yMin + 2f), new Vector2(r.xMax - 2f, r.yMax - 2f), 2.4f, Paper.Grease, 7, 1f);
                    Stroke(g, new Vector2(r.xMax - 2f, r.yMin + 2f), new Vector2(r.xMin + 2f, r.yMax - 2f), 2.4f, Paper.Grease, 9, 1f);
                }
            }

            /// <summary>An empty photo slot: dashed frame, a dashed head and shoulders.</summary>
            public static void BustGhost(FrontVector g, Rect r, Color k)
            {
                DashRect(g, r.x, r.y, r.width, r.height, 1f, k);
                float cx = r.center.x, bottom = r.yMax - 1f, rx = r.width * 0.42f, ry = r.height * 0.3f;
                Vector2 prev = new Vector2(cx - rx, bottom);
                for (int i = 1; i <= 10; i++)
                {
                    float a = Mathf.PI * (1f - i / 10f);
                    Vector2 p = new Vector2(cx + Mathf.Cos(a) * rx, bottom - Mathf.Sin(a) * ry);
                    if (i % 2 == 1) g.Line(prev, p, 1f, k);
                    prev = p;
                }
                g.Ring(new Vector2(cx, bottom - ry - 8f), 6f, 1f, k, 14, 1f, 1f);
            }

            /// <summary>A segmented gauge on a dark well (twelve cells, the last one part-filled).</summary>
            public static void Gauge(FrontVector g, float x, float y, float w, float h, float frac, Color ink)
            {
                g.Rect(x, y, w, h, Paper.Well);
                const int cells = 12;
                float cw = (w - 2f) / cells;
                frac = Mathf.Clamp01(frac);
                for (int i = 0; i < cells; i++)
                {
                    float f = Mathf.Clamp01(frac * cells - i);
                    if (f <= 0f) break;
                    g.Rect(x + 1f + i * cw, y + 1.5f, Mathf.Max(0.6f, (cw - 1f) * f), h - 3f, ink);
                }
                g.Frame(x, y, w, h, 1f, Paper.Pen);
            }

            /// <summary>
            /// A reconnaissance photograph: night-vision ground with seeded building blocks and tracks, a crosshair and the target marked in china marker.
            /// <paramref name="printed"/>: a real print lies under these marks, so only its paper border and the marks are drawn.
            /// </summary>
            public static void Recon(FrontVector g, Rect r, int seed, TargetKind kind, bool live, bool printed = false)
            {
                if (printed) g.Frame(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f, 2f, Paper.PhotoPaper);
                else
                {
                    g.Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f, Paper.PhotoPaper);
                    g.Rect(r.x, r.y, r.width, r.height, Paper.PhotoDark);
                }
                Vector2 c = r.center;
                if (!live)
                {
                    for (float d = -r.height; d < r.width; d += 7f)
                    {
                        Vector2 a = new Vector2(r.x + d, r.yMax), b = new Vector2(r.x + d + r.height, r.y);
                        if (a.x < r.x) { a = new Vector2(r.x, r.yMax - (r.x - a.x)); }
                        if (b.x > r.xMax) { b = new Vector2(r.xMax, r.y + (b.x - r.xMax)); }
                        if (a.x < r.xMax && b.x > r.x) g.Line(a, b, 1f, RoomInk.Rgba(1f, 1f, 1f, 0.07f));
                    }
                    return;
                }
                for (int i = 0; i < 9 && !printed; i++)
                {
                    float u = Hash(seed, i * 3), v = Hash(seed, i * 3 + 1), s = 0.08f + 0.12f * Hash(seed, i * 3 + 2);
                    float bw = Mathf.Min(r.width * s * 1.4f, r.width), bh = Mathf.Min(r.height * s * 1.2f, r.height);
                    float bx = r.x + (r.width - bw) * u, by = r.y + (r.height - bh) * v;
                    g.Rect(bx, by, bw, bh, RoomInk.Rgba(0.36f, 0.45f, 0.30f, 0.55f));
                }
                if (!printed) g.Line(new Vector2(r.x, r.y + r.height * (0.25f + 0.5f * Hash(seed, 40))), new Vector2(r.xMax, r.y + r.height * (0.25f + 0.5f * Hash(seed, 41))), 1.6f, RoomInk.Rgba(0.62f, 0.7f, 0.5f, 0.4f));
                if (!printed) g.Line(new Vector2(r.x + r.width * (0.2f + 0.6f * Hash(seed, 42)), r.y), new Vector2(r.x + r.width * (0.2f + 0.6f * Hash(seed, 43)), r.yMax), 1.4f, RoomInk.Rgba(0.62f, 0.7f, 0.5f, 0.35f));
                Color cross = RoomInk.Rgba(0.94f, 0.95f, 0.82f, 0.7f);
                g.Line(new Vector2(r.x + 3f, c.y), new Vector2(c.x - 11f, c.y), 1f, cross);
                g.Line(new Vector2(c.x + 11f, c.y), new Vector2(r.xMax - 3f, c.y), 1f, cross);
                g.Line(new Vector2(c.x, r.y + 3f), new Vector2(c.x, c.y - 11f), 1f, cross);
                g.Line(new Vector2(c.x, c.y + 11f), new Vector2(c.x, r.yMax - 3f), 1f, cross);
                Circle(g, c, Mathf.Min(13f, r.height * 0.28f), 1.8f, Paper.Grease, seed);
                TargetGlyph(g, kind, c, 4.5f, Paper.Cream);
                for (int sx = 0; sx < 2; sx++)
                    for (int sy = 0; sy < 2; sy++)
                    {
                        Vector2 q = new Vector2(sx == 0 ? r.x + 3f : r.xMax - 3f, sy == 0 ? r.y + 3f : r.yMax - 3f);
                        float dx = sx == 0 ? 5f : -5f, dy = sy == 0 ? 5f : -5f;
                        g.Line(q, q + new Vector2(dx, 0f), 1f, cross);
                        g.Line(q, q + new Vector2(0f, dy), 1f, cross);
                    }
            }
        }

        private void BuildSituation(RectTransform hero)
        {
            situation = new FrontBox(hero, skin, "Shadow", 0f, 0f, heroW, mapH, "SOF SITUATION · PRESENCE AND INFILTRATION", AvIcon.Eye, FrontLook.Folder).Tagged("FILE 07-S");
            RectTransform b = situation.Rect;
            plate = new FrontMapPlate(b, skin, heroW, mapH, Cols, Rows);
            plate.Tint = RoomInk.Rgba(1f, 0.92f, 0.7f, 1f);
            airbases = new FrontMap.AirbaseLayer(b);
            for (int i = 0; i < MaxTeams; i++) teamLabels[i] = Pool(b, "TeamLabel" + i, 170f, 28f);
            for (int i = 0; i < MaxTargets; i++) targetLabels[i] = Pool(b, "TargetLabel" + i, 170f, 14f);
            for (int i = 0; i < MaxCamps; i++) campLabels[i] = Pool(b, "CampLabel" + i, 170f, 28f);
            for (int i = 0; i < MaxGhosts; i++) ghostLabels[i] = Pool(b, "GhostLabel" + i, 200f, 14f);
            for (int i = 0; i < MaxHeld; i++) heldLabels[i] = Pool(b, "HeldLabel" + i, 170f, 28f);
            for (int i = 0; i < MaxEnemies; i++)
            {
                enemyMarks[i] = FrontKit.Mono(b, "Enemy" + i, 0f, 0f, 18f, 18f, 14f, TextAlignmentOptions.Midline, true);
                enemyMarks[i].text = "?";
                enemyMarks[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < MaxTeams; i++)
            {
                int slot = i;
                Image hit = FrontKit.Solid(b, "TeamHit" + i, 0f, 0f, 34f, 26f, Color.clear);
                hit.raycastTarget = true;
                AvHit.On(hit).Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) Do(() => PickTeam(hitSlots[slot])); };
                hit.gameObject.SetActive(false);
                teamHits[i] = hit;
            }
            for (int i = 0; i < MaxTargets; i++)
            {
                int slot = i;
                Image hit = FrontKit.Solid(b, "TargetHit" + i, 0f, 0f, 30f, 30f, Color.clear);
                hit.raycastTarget = true;
                AvHit.On(hit).Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) Do(() => PickTarget(hitTargets[slot])); };
                hit.gameObject.SetActive(false);
                targetHits[i] = hit;
            }
            // The foot of the plate, right of the cursor readout: what the armed pick wants, or why the last click missed.
            footLabel = FrontKit.Mono(b, "FootLabel", plate.X + FootX, plate.Y + plate.H - 20f, plate.W - FootX - FootRight, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            footLabel.gameObject.SetActive(false);

            skin.Add(() => { foreach (TMP_Text t in enemyMarks) t.color = AvInk.State(AvState.Danger); });
        }

        private static TMP_Text Pool(RectTransform b, string name, float w, float h)
        {
            TMP_Text t = FrontKit.Mono(b, name, 0f, 0f, w, h, 10.5f, TextAlignmentOptions.TopLeft);
            t.gameObject.SetActive(false);
            return t;
        }

        // ---- Kit ------------------------------------------------------------------------------------------------------

        /// <summary>Places a rect by its centre and tilts it (a stamp turns about its middle).</summary>
        private static void Centered(RectTransform rt, float x, float y, float w, float h, float degrees)
        {
            AvLay.Place(rt, x, y, w, h);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
            rt.localRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        /// <summary>
        /// The dark plate a button on paper sits on (the kit's tints wash out on a folder): solid under a live one; under a dark one only a faint well in a dashed
        /// edge, so a refused button never reads as one to press.
        /// </summary>
        private static void HolePlate(FrontVector g, float x, float y, float w, float h, bool live)
        {
            if (live) { g.Rect(x - 1f, y - 1f, w + 2f, h + 2f, Paper.Hole); return; }
            g.Rect(x - 1f, y - 1f, w + 2f, h + 2f, Paper.Hole.WithAlpha(0.3f));
            Hand.DashRect(g, x - 0.5f, y - 0.5f, w + 1f, h + 1f, 1f, Paper.PenDim);
        }

        private AvControl Button(RectTransform p, string label, float x, float y, float w, float h, AvButtonStyle style, Action click, string help)
        {
            AvControl c = AvControl.Make(p, new AvControl.Spec(label, click, style));
            c.Accent = RoomInk.Gold;
            AvLay.Place(c.Rect, x, y, w, h);
            c.SingleLine();
            if (!string.IsNullOrEmpty(help)) c.Help = help;
            return c;
        }

        // ---- Paint -----------------------------------------------------------------------------------------------------

        public void Paint(FrontRoomView v)
        {
            ShadowRoomView s = v.Shadow;
            last = v;
            lastView = s;
            Why(v); // a refusal that comes up while a pick is armed is said at its click (PickAt); a refresh never drops the pick
            PaintSituation(v, s);
            PaintBases(v);
            PaintTargets(v);
        }

        private Vector2 P(float x, float y) => plate.P(x, y);

        /// <summary>
        /// One frame of input. A pan, zoom or inset move repaints only the map, a wheel over the target list only the list; both return false (the column does not
        /// depend on the view). True for a click that stayed put on an unmoved map: the armed pick's (anywhere on the plate), else on open ground a RECON point.
        /// Markers take their clicks through their own hits.
        /// </summary>
        public bool Tick(Vector2 mouse)
        {
            if (last == null) { pressed = false; return false; }
            FrontMapView mv = plate.View;
            Vector2 center = mv.Center;   // the view as the press found it: a press on the inset overview moves it, and that is no click on the map
            int zoomStep = mv.Zoom;
            bool changed = plate.Tick(mouse);
            if (plate.RightClick(mouse, out Vector2 ruv) && pick != PickMode.None) { PickAt(ruv); return true; } // an armed pick places with either button
            float wheel = FrontPointer.Wheel;
            if (Mathf.Abs(wheel) > 0.01f && targets.Rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(targets.Rect, mouse, null))
            {
                int to = Mathf.Clamp(listScroll + (wheel > 0f ? -1 : 1), 0, Math.Max(0, listCount - 1));
                if (to != listScroll && (wheel > 0f || listBelow)) { listScroll = to; PaintTargets(last); }
            }
            if (FrontPointer.Down(0)) { pressed = ToBox(mouse, out pressAt); pressCenter = center; pressZoom = zoomStep; }
            if (pressed && (FrontPointer.Held(1) || FrontPointer.Held(2))) pressed = false;   // a pan gesture is no click
            if (!pressed || !FrontPointer.Up(0)) return MapOnly(changed);
            pressed = false;
            if (!ToBox(mouse, out Vector2 p) || (p - pressAt).sqrMagnitude > FrontMapPlate.ClickSlop * FrontMapPlate.ClickSlop || mv.Zoom != pressZoom || (mv.Center - pressCenter).sqrMagnitude > 1e-8f)
                return MapOnly(changed);
            if (pick != PickMode.None)
            {
                if (!mv.Visible(p, 0f)) return MapOnly(changed);
                PickAt(mv.UV(p));
                return true;
            }
            if (!OnGround(p)) return MapOnly(changed);
            SetPoint(p);
            return true;
        }

        /// <summary>The view moved: the map repaints now, the window repaints nothing else.</summary>
        private bool MapOnly(bool changed)
        {
            if (changed) PaintSituation(last, last.Shadow);
            return false;
        }

        public BoscaliSummer.Core.Ui.CursorShape Cursor(Vector2 mouse) => plate.Cursor(mouse, pick != PickMode.None);

        private bool ToBox(Vector2 screen, out Vector2 p)
        {
            RectTransform box = situation.Rect;
            p = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(box, screen, null, out Vector2 lp)) return false;
            p = new Vector2(lp.x - box.rect.xMin, box.rect.yMax - lp.y);
            return true;
        }

        private bool Near(Vector2 p, float u, float v) => (P(u, v) - p).sqrMagnitude < MarkerClear * MarkerClear;

        /// <summary>Inside the plate, clear of the overlay cards and of every marker.</summary>
        private bool OnGround(Vector2 p)
        {
            ShadowRoomView s = lastView;
            if (s == null || !s.Active || !plate.Shown(p, 12f)) return false;
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present && i < MaxTeams && (teamAt[i] - p).sqrMagnitude < MarkerClear * MarkerClear) return false;
            for (int i = 0; i < s.Targets.Count; i++) if (Near(p, s.Targets[i].X, s.Targets[i].Y)) return false;
            for (int i = 0; i < s.Camps.Count; i++) if (Near(p, s.Camps[i].X, s.Camps[i].Y)) return false;
            for (int i = 0; i < s.Held.Count; i++) if (Near(p, s.Held[i].X, s.Held[i].Y)) return false;
            for (int i = 0; i < s.Enemies.Count; i++) if (Near(p, s.Enemies[i].X, s.Enemies[i].Y)) return false;
            return true;
        }

        /// <summary>Open ground clicked: the aim becomes an area RECON point there (spec 7.1), on TARGETS. It replaces a picked target (one aim at a time).</summary>
        private void SetPoint(Vector2 p)
        {
            Vector2 uv = plate.View.UV(p);
            MapFrame frame = FrontController.Frame();
            float u = Mathf.Clamp01(uv.x), v = Mathf.Clamp01(uv.y);
            lastView.HasPoint = true; lastView.PointU = u; lastView.PointV = v; lastView.Reason = "";
            lastView.PointId = FrontViews.PointId(frame.X(u), frame.Z(v));
            wantTab = TargetsTab;
            actions.Touch();
            actions.SelectTarget(0); // the explicit way to drop a target (ruling D-R5); the view is rebuilt for the point at once
        }

        private static Color Tone(AvState s) =>
            s == AvState.Ready ? Own : s == AvState.Caution ? AvInk.State(AvState.Caution) : s == AvState.Danger ? AvInk.State(AvState.Danger) : s == AvState.Info ? AvInk.Key : AvInk.Dim;

        private static Color ExposureInk(float pct) => pct < 40f ? Own : pct < 70f ? AvInk.State(AvState.Caution) : AvInk.State(AvState.Danger);

        /// <summary>A label plate on the map: a strip of dark label tape with a bright edge and a tick in the marker's colour.</summary>
        private static void Tape(FrontVector g, Rect r, Color accent)
        {
            g.Rect(r.x - 3f, r.y - 1f, r.width + 6f, r.height + 2f, AvInk.Ground.WithAlpha(0.8f));
            g.Rect(r.x - 3f, r.y - 1f, 2f, r.height + 2f, accent);
            g.Rect(r.x - 1f, r.y - 1f, r.width + 4f, 1f, RoomInk.Rgba(1f, 1f, 1f, 0.16f));
        }

        private void PaintSituation(FrontRoomView v, ShadowRoomView s)
        {
            FrontMapView mv = plate.View;
            Vector2 sum = Vector2.zero;
            int na = 0;
            // Centre on our camps and teams, else on our airbases (where the first camp goes); a pan or zoom takes over.
            for (int i = 0; i < s.Camps.Count; i++) if (s.Camps[i].X >= 0f) { sum += new Vector2(s.Camps[i].X, s.Camps[i].Y); na++; }
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present) { sum += new Vector2(s.Teams[i].X, s.Teams[i].Y); na++; }
            if (na == 0) for (int i = 0; i < v.Airbases.Count; i++) if (v.Airbases[i].Side == AirbaseSide.Own) { sum += new Vector2(v.Airbases[i].U, v.Airbases[i].V); na++; }
            if (na > 0) mv.AutoCenter(sum / na);
            plate.Begin();
            FrontVector g = plate.G;
            FrontLabels labels = plate.Labels;
            Color own = Own, amber = AvInk.State(AvState.Caution), danger = AvInk.State(AvState.Danger), key = AvInk.Key, dim = AvInk.Dim, foe = AvInk.Hostile;
            Color grease = Paper.Grease;
            bool hits = pick == PickMode.None; // an armed pick owns the click: no marker takes one
            if (!hits) labels.Take(new Rect(plate.X + FootX, plate.Y + plate.H - 24f, plate.W - FootX - FootRight, 24f)); // the foot words
            FrontMap.AirbaseLayer.Glyphs(g, v.Airbases, P);
            PaintPickRings(g, v);

            // Camps: push-pins (a queued one is a ghost below).
            for (int i = 0; i < s.Camps.Count; i++)
            {
                SofCampView c = s.Camps[i];
                if (c.State == InfraState.Building || c.X < 0f) continue;
                Vector2 p = P(c.X, c.Y);
                Color k = c.Health == AnchorHealth.Live ? own : c.Health == AnchorHealth.Damaged ? amber : danger;
                Hand.Pin(g, p, 7f, k);
                if (c.Health == AnchorHealth.Down) FrontKit.Cross(g, p, 9f, danger);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
            }
            // Queued camps and a queued FOB with a point: a dashed pin with its timer (words below, where there is room).
            for (int i = 0; i < s.Ghosts.Count; i++)
            {
                Vector2 p = P(s.Ghosts[i].U, s.Ghosts[i].V);
                g.Disc(p, 9f, AvInk.Ground.WithAlpha(0.6f), 18);
                g.Ring(p, 9f, 1.6f, own.WithAlpha(0.9f), 24, 3f, 2f);
                g.Disc(p, 3f, own.WithAlpha(0.6f), 10);
                labels.Take(new Rect(p.x - 12f, p.y - 12f, 24f, 24f));
            }

            // Revealed targets: one hit region each (the list beside the map reaches every one, even when a cluster stacks them here),
            // each ringed twice in china marker (a resisted one in faded ink, an exploit one with an amber third ring).
            // One hit per spot, the picked target's first: in a cluster (the buildings of an airbase) only one takes the map's click, the list reaches the rest
            // and a zoom pulls them apart.
            int placed = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < MaxTargets; i++)
                {
                    if (i >= s.Targets.Count) { if (pass == 0) Show(targetHits[i], false); continue; }
                    if (s.Targets[i].Selected != (pass == 0)) continue;
                    Vector2 p = P(s.Targets[i].X, s.Targets[i].Y);
                    bool on = hits && plate.Shown(p, 10f);
                    for (int j = 0; j < placed && on; j++) on = Mathf.Abs(hitAt[j].x - p.x) >= 30f || Mathf.Abs(hitAt[j].y - p.y) >= 30f;
                    Show(targetHits[i], on);
                    if (!on) continue;
                    hitTargets[i] = s.Targets[i].Id;
                    hitAt[placed++] = p;
                    AvLay.Place(targetHits[i].rectTransform, p.x - 15f, p.y - 15f, 30f, 30f);
                }
            for (int i = 0; i < s.Targets.Count; i++)
            {
                SofTargetView t = s.Targets[i];
                Vector2 p = P(t.X, t.Y);
                Color k = t.Resisted ? dim : foe;
                g.Disc(p, 11f, AvInk.Ground.WithAlpha(0.85f), 18);
                TargetGlyph(g, t.Kind, p, 7f, k);
                if (t.Resisted) g.Line(p + new Vector2(-9f, 9f), p + new Vector2(9f, -9f), 1.6f, dim);
                Color mark = t.Resisted ? dim.WithAlpha(0.8f) : grease;
                Hand.Circle(g, p, 12f, 1.9f, mark, t.Id * 5 + 1);
                Hand.Circle(g, p, 16.5f, 1.7f, mark.WithAlpha(0.85f), t.Id * 5 + 3);
                if (t.Exploit) Hand.Circle(g, p, 21f, 1.5f, amber, t.Id * 5 + 4);
                if (t.Selected)
                {
                    FrontKit.Brackets(g, p, 19f, AvInk.Select);
                    g.Ring(p, 27f, 1.2f, AvInk.Select.WithAlpha(0.7f), 36, 1f, 1f); // locate ring: the list pick shows where it is
                }
                labels.Take(new Rect(p.x - 18f, p.y - 18f, 36f, 36f));
            }

            // The area RECON point the operator clicked.
            if (s.HasPoint)
            {
                Vector2 p = P(s.PointU, s.PointV);
                g.Ring(p, 12f, 1.6f, AvInk.Select, 28);
                FrontKit.Cross(g, p, 6f, AvInk.Select);
            }

            // Revealed enemy teams.
            for (int i = 0; i < MaxEnemies; i++)
            {
                if (i >= s.Enemies.Count) { enemyMarks[i].gameObject.SetActive(false); continue; }
                Vector2 p = P(s.Enemies[i].X, s.Enemies[i].Y);
                g.Quad(p + new Vector2(0f, -11f), p + new Vector2(11f, 0f), p + new Vector2(0f, 11f), p + new Vector2(-11f, 0f), foe.WithAlpha(0.22f));
                g.Polyline(new[] { p + new Vector2(0f, -11f), p + new Vector2(11f, 0f), p + new Vector2(0f, 11f), p + new Vector2(-11f, 0f) }, 1.6f, foe, true);
                enemyMarks[i].gameObject.SetActive(true);
                enemyMarks[i].text = i < s.EnemyMark.Count ? s.EnemyMark[i] : "?";
                AvLay.Place(enemyMarks[i].rectTransform, p.x - 9f, p.y - 9f, 18f, 18f);
                labels.Take(new Rect(p.x - 13f, p.y - 13f, 26f, 26f));
            }

            // Held buildings.
            for (int i = 0; i < MaxHeld; i++)
            {
                if (i >= s.Held.Count) { heldLabels[i].gameObject.SetActive(false); continue; }
                Vector2 p = P(s.Held[i].X, s.Held[i].Y);
                g.Disc(p, 14f, own.WithAlpha(0.16f), 20);
                g.Rect(p.x - 7f, p.y - 4f, 14f, 11f, own.WithAlpha(0.5f));
                g.Frame(p.x - 7f, p.y - 4f, 14f, 11f, 1.4f, own);
                g.Line(p + new Vector2(0f, -4f), p + new Vector2(0f, -12f), 1.4f, own);
                g.Tri(p + new Vector2(0f, -12f), p + new Vector2(8f, -9f), p + new Vector2(0f, -6f), own);
                labels.Take(new Rect(p.x - 14f, p.y - 15f, 28f, 28f));
            }

            // Where each team's glyph and click sit: its point, stepped aside while an earlier team or a target marker is already there (two teams at one camp,
            // a team on its target), so every marker keeps its own glyph and click; a tick runs back to where it really stands.
            for (int i = 0; i < s.Teams.Count && i < MaxTeams; i++)
            {
                if (!s.Teams[i].Present) continue;
                Vector2 p = P(s.Teams[i].X, s.Teams[i].Y);
                for (int step = 0; step < MaxTeams + 1 && Taken(s, i, p); step++) p.x += TeamStep;
                teamAt[i] = p;
            }
            // Teams: exposure areas (hatched), routes in pencil, then glyphs.
            for (int i = 0; i < s.Teams.Count && i < MaxTeams; i++)
            {
                SofTeamView t = s.Teams[i];
                if (!t.Present) continue;
                Vector2 p = P(t.X, t.Y);
                float er = 14f + t.Exposure / 100f * 56f;
                Color ek = ExposureInk(t.Exposure);
                g.Ring(p, 70f, 1f, ek.WithAlpha(0.18f), 56, 1f, 1f);
                Hand.Hatch(g, p, er, 5f, 1.1f, ek.WithAlpha(0.45f));
                if (t.Exposure >= 70) Hand.Hatch(g, p, er, 5f, 1f, ek.WithAlpha(0.3f), true);   // heavier cross-hatching once a team is close to pinned
                g.Ring(p, er, 1.6f, ek.WithAlpha(0.9f), 56);
                if (t.HasDest)
                {
                    // A grease-pencil route: the picked team's in grease red, the others' in black pencil with a pale halo so it reads on dark ground.
                    Vector2 d = P(t.DestX, t.DestY), u = (d - p).normalized, nrm = new Vector2(-u.y, u.x);
                    int seed = 17 + t.Slot * 29;
                    Vector2 a0 = p + u * 12f, a1 = d - u * 3f;
                    if (t.Selected) Hand.Stroke(g, a0, a1, 3.2f, grease, seed, 2.2f);
                    else
                    {
                        Hand.Stroke(g, a0, a1, 5.8f, RoomInk.Rgba(0.96f, 0.93f, 0.82f, 0.5f), seed, 2.2f);
                        Hand.Stroke(g, a0, a1, 3.4f, RoomInk.Rgba(0.05f, 0.04f, 0.03f, 0.97f), seed, 2.2f);
                    }
                    Color pen = t.Selected ? grease : RoomInk.Rgba(0.07f, 0.06f, 0.04f, 0.96f);
                    Vector2 h1 = d - u * 11f + nrm * 5.5f, h2 = d - u * 11f - nrm * 5.5f;
                    if (!t.Selected) { g.Line(d, h1, 4f, RoomInk.Rgba(0.96f, 0.93f, 0.82f, 0.55f)); g.Line(d, h2, 4f, RoomInk.Rgba(0.96f, 0.93f, 0.82f, 0.55f)); }
                    g.Line(d, h1, 2.2f, pen);
                    g.Line(d, h2, 2.2f, pen);
                    if (!t.Selected) Hand.Circle(g, d + u * 3f, 7f, 4f, RoomInk.Rgba(0.96f, 0.93f, 0.82f, 0.5f), seed + 2);
                    Hand.Circle(g, d + u * 3f, 7f, t.Selected ? 2f : 2.2f, pen, seed + 2);
                    labels.Take(new Rect(d.x - 9f, d.y - 9f, 18f, 18f));
                }
                if (t.Lasing)
                {
                    Vector2 tg = P(t.TargetX, t.TargetY);
                    g.Dashed(p, tg, 1.4f, key, 3f, 3f);
                    g.Ring(tg, 18f, 1.4f, key, 30);
                }
            }
            for (int i = 0; i < MaxTeams; i++)
            {
                if (i >= s.Teams.Count || !s.Teams[i].Present) { teamLabels[i].gameObject.SetActive(false); teamHits[i].gameObject.SetActive(false); continue; }
                SofTeamView t = s.Teams[i];
                Vector2 p = teamAt[i], at = P(t.X, t.Y);
                Color k = Tone(t.Tone);
                if ((p - at).sqrMagnitude > 4f) { g.Line(at, p, 1f, k.WithAlpha(0.6f)); g.Disc(at, 2f, k.WithAlpha(0.8f), 8); }
                g.Rect(p.x - 10f, p.y - 7f, 20f, 14f, AvInk.Ground.WithAlpha(0.9f));
                g.Frame(p.x - 10f, p.y - 7f, 20f, 14f, 1.8f, k);
                g.Line(p + new Vector2(-6f, -4f), p + new Vector2(6f, 4f), 1.6f, k);
                g.Line(p + new Vector2(-6f, 4f), p + new Vector2(6f, -4f), 1.6f, k);
                if (t.Helicopter) { g.Line(p + new Vector2(-8f, -11f), p + new Vector2(8f, -11f), 1.6f, k); g.Line(p + new Vector2(0f, -11f), p + new Vector2(0f, -7f), 1.4f, k); }
                if (t.Wounded) g.Disc(p + new Vector2(11f, -8f), 3f, danger, 8);
                if (t.Selected) FrontKit.Brackets(g, p, 17f, AvInk.Select);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
                Image hit = teamHits[i];
                hit.gameObject.SetActive(hits && plate.Shown(p, 10f));
                hitSlots[i] = t.Slot;
                AvLay.Place(hit.rectTransform, p.x - 17f, p.y - 13f, 34f, 26f);
            }

            // Labels last, once every glyph has claimed its space.
            for (int i = 0; i < MaxTeams; i++)
            {
                if (i >= s.Teams.Count || !s.Teams[i].Present) continue;
                SofTeamView t = s.Teams[i];
                TMP_Text lt = teamLabels[i];
                if (!plate.Shown(teamAt[i], 12f)) { lt.gameObject.SetActive(false); continue; }
                lt.gameObject.SetActive(true);
                lt.text = C2Kit.Tint("<b>" + t.Callsign + "</b> " + t.Codename + " · " + t.State, Tone(t.Tone)) + "\n" + C2Kit.Tint("EXPOSURE " + t.Exposure + " %", ExposureInk(t.Exposure));
                lt.color = AvInk.Ink;
                Tape(g, labels.Place(lt, teamAt[i], 14f, false), Tone(t.Tone));
            }
            int cl = 0;
            for (int i = 0; i < s.Camps.Count && cl < MaxCamps; i++)
            {
                SofCampView c = s.Camps[i];
                if (c.State == InfraState.Building || c.X < 0f || !plate.Shown(P(c.X, c.Y), 12f)) continue;
                TMP_Text lt = campLabels[cl++];
                lt.gameObject.SetActive(true);
                Color k = c.Health == AnchorHealth.Live ? own : c.Health == AnchorHealth.Damaged ? amber : danger;
                lt.text = C2Kit.Tint("<b>" + c.Name + "</b>", k) + "\n" + C2Kit.Tint(c.Health == AnchorHealth.Down ? "LOST" : c.Health == AnchorHealth.Damaged ? "DAMAGED" : "UP", dim);
                lt.color = AvInk.Ink;
                Tape(g, labels.Place(lt, P(c.X, c.Y), 14f, false), k);
            }
            for (int i = cl; i < MaxCamps; i++) campLabels[i].gameObject.SetActive(false);
            // Ghost words: "CAMP-2 · ASHWOOD · 0:51" where there is room (their own pool: a camp label never doubles as one).
            int gl = 0;
            for (int i = 0; i < s.Ghosts.Count && gl < MaxGhosts; i++)
            {
                Vector2 p = P(s.Ghosts[i].U, s.Ghosts[i].V);
                if (!plate.Shown(p, 12f)) continue;
                TMP_Text lt = ghostLabels[gl];
                lt.gameObject.SetActive(true);
                FrontKit.Set(lt, s.Ghosts[i].Label, own);
                Rect spot = labels.Place(lt, p, 14f, true, true);
                if (spot.width <= 0f) { lt.gameObject.SetActive(false); continue; }
                Tape(g, spot, own);
                gl++;
            }
            for (int i = gl; i < MaxGhosts; i++) ghostLabels[i].gameObject.SetActive(false);
            for (int i = 0; i < MaxHeld && i < s.Held.Count; i++)
            {
                SofHeldView hv = s.Held[i];
                TMP_Text lt = heldLabels[i];
                if (!plate.Shown(P(hv.X, hv.Y), 12f)) { lt.gameObject.SetActive(false); continue; }
                lt.gameObject.SetActive(true);
                lt.text = C2Kit.Tint("<b>" + hv.Name + "</b>", own) + "\n" + C2Kit.Tint("HELD · " + FrontKit.Clock(hv.SecondsLeft), amber);
                lt.color = AvInk.Ink;
                Tape(g, labels.Place(lt, P(hv.X, hv.Y), 14f, false), own);
            }
            for (int i = 0; i < MaxTargets; i++)
            {
                if (i >= s.Targets.Count) { targetLabels[i].gameObject.SetActive(false); continue; }
                SofTargetView t = s.Targets[i];
                TMP_Text lt = targetLabels[i];
                if (!plate.Shown(P(t.X, t.Y), 12f)) { lt.gameObject.SetActive(false); continue; }
                lt.gameObject.SetActive(true);
                lt.text = C2Kit.Tint("<b>" + t.Name + "</b>" + (t.Exploit ? " · EXPLOIT" : t.Resisted ? " · RESISTED" : ""), t.Resisted ? dim : t.Exploit ? amber : foe);
                lt.color = AvInk.Ink;
                // Only the picked target must be named on the map; the rest take a clear spot or stay unlabelled (the list beside the map names every one, a cluster of twenty included).
                Rect spot = labels.Place(lt, P(t.X, t.Y), 17f, true, !t.Selected);
                if (spot.width <= 0f) lt.gameObject.SetActive(false); else Tape(g, spot, t.Resisted ? dim : t.Exploit ? amber : foe);
            }
            airbases.Names(g, v.Airbases, P, mv, (t, a) => labels.Place(t, a, 9f, true, true));

            // The foot: what the armed pick wants, or why the last click missed.
            if (footLabel.gameObject.activeSelf == hits) footLabel.gameObject.SetActive(!hits);
            if (!hits) FrontKit.Set(footLabel, FrontKit.Fit(footLabel, PickWords(true), 10.5f), pickMiss.Length > 0 ? danger : amber);

            int up = 0;
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present) up++;
            for (int i = 0; i < s.Camps.Count; i++) if (s.Camps[i].X >= 0f) plate.Dot(s.Camps[i].X, s.Camps[i].Y, own);
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present) plate.Dot(s.Teams[i].X, s.Teams[i].Y, Tone(s.Teams[i].Tone));
            for (int i = 0; i < s.Targets.Count; i++) plate.Dot(s.Targets[i].X, s.Targets[i].Y, foe);
            plate.End();

            situation.SetMeta(s.Active ? up + " TEAMS OUT · " + s.Targets.Count + " TARGETS REVEALED · " + s.Enemies.Count + " ENEMY TEAMS" : s.Meta, s.Enemies.Count > 0 ? FrontKit.Tone(AvState.Danger) : dim);
        }

        /// <summary>A team drawn at <paramref name="p"/> would sit on a target marker or on an earlier team's drawn point (their hits would overlap).</summary>
        private bool Taken(ShadowRoomView s, int team, Vector2 p)
        {
            for (int j = 0; j < team; j++) if (s.Teams[j].Present && Mathf.Abs(teamAt[j].x - p.x) < TeamStep && Mathf.Abs(teamAt[j].y - p.y) < 28f) return true;
            foreach (SofTargetView t in s.Targets) { Vector2 q = P(t.X, t.Y); if (Mathf.Abs(q.x - p.x) < 33f && Mathf.Abs(q.y - p.y) < 29f) return true; }
            return false;
        }

        private static void TargetGlyph(FrontVector g, TargetKind kind, Vector2 p, float r, Color k)
        {
            switch (kind)
            {
                case TargetKind.Ground:
                    g.Polyline(new[] { p + new Vector2(-r, -r * 0.7f), p + new Vector2(r, -r * 0.7f), p + new Vector2(0f, r) }, 1.6f, k, true);
                    break;
                case TargetKind.Anchor:
                    g.Polyline(new[] { p + new Vector2(0f, -r), p + new Vector2(r, 0f), p + new Vector2(0f, r), p + new Vector2(-r, 0f) }, 1.6f, k, true);
                    g.Disc(p, 2f, k, 8);
                    break;
                case TargetKind.Relay:
                    g.Ring(p, r, 1.6f, k, 20);
                    g.Disc(p, 2.2f, k, 8);
                    break;
                default:
                    g.Frame(p.x - r * 0.85f, p.y - r * 0.85f, r * 1.7f, r * 1.7f, 1.6f, k);
                    break;
            }
        }

        // ---- Cards and stamps -----------------------------------------------------------------------------------------------

        /// <summary>A rubber stamp: a double border with cut corners and a tracked word in a tone, tilted a few degrees about its middle. Built once; Set only changes the word and the ink.</summary>
        private sealed class Stamp
        {
            private readonly RectTransform root;
            private readonly AvFrame outer, inner;
            private readonly TMP_Text word;
            private readonly float w, h, tilt;

            public Stamp(RectTransform p, float x, float y, float w, float h, float tilt, float size = 10.5f)
            {
                this.w = w; this.h = h; this.tilt = tilt;
                root = AvLay.Child(p, "Stamp");
                outer = FrontKit.Panel(root, "Outer", 0f, 0f, w, h, 3f);
                outer.Stroke = 1.8f;
                inner = FrontKit.Panel(root, "Inner", 3.5f, 3.5f, w - 7f, h - 7f, 1.5f);
                inner.Stroke = 0.8f;
                inner.Fill = false;
                word = FrontKit.Mono(root, "Word", 0f, 0f, w, h, size, TextAlignmentOptions.Midline, true, 2f);
                Place(x, y);
            }

            public void Place(float x, float y) => Centered(root, x, y, w, h, tilt);

            public void Set(string text, Color ink)
            {
                bool on = !string.IsNullOrEmpty(text);
                if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
                if (!on) return;
                FrontKit.Set(word, text, ink);
                outer.Paint(ink.WithAlpha(0.07f), ink.WithAlpha(0.92f));
                inner.Paint(Color.clear, ink.WithAlpha(0.6f));
            }
        }

        /// <summary>
        /// One team card on TEAMS &amp; BASES. A team on the roster is a personnel jacket: a folder tab in the state's colour (staggered by slot), an ID photograph with a
        /// head-and-shoulders silhouette, the callsign large with its codename and controller, the mission (with its odds while afield) and clock, exposure and ammo as
        /// gauges, the state as a rubber stamp, and a verb row: its home (ruling D-R4), PUSH / HOLD / EXFIL / LIFT on dark plates, lit as the host would take them, and
        /// what they mean now (LIFT is the helicopter insertion, ruling D-R2). A queued TRAIN is an empty jacket in training with its bar and CHIP IN. A click on a
        /// jacket picks the team (the TARGETS missions send it); it sends no order.
        /// </summary>
        private sealed class Card
        {
            public const float TabH = 13f, BodyH = 88f, FullH = TabH + BodyH;
            private const float TabW = 46f, TabStep = 50f, InfoX = 62f, VerbY = TabH + 64f, VerbX = 120f, VerbW = 58f, VerbGap = 4f, ChipW = 3f * VerbW + 2f * VerbGap;
            private readonly RectTransform root;
            private readonly AvHit hit;
            private readonly FrontVector gfx;
            private readonly TMP_Text tabText, call, code, flags, control, mission, eta, expKey, expVal, ammoKey, ammoVal, caption, remark, home, note;
            private readonly AvControl[] verbs = new AvControl[4];
            private readonly AvControl chip;
            private readonly Stamp stamp;
            private readonly float x, w;
            private bool shown;
            private int mode = -1; // 1 = a team's jacket, 2 = a queued TRAIN in training
            private int slot;
            private ushort row;

            public Card(ShadowRoom room, RectTransform p, float x, float w)
            {
                this.x = x; this.w = w;
                root = AvLay.Child(p, "TeamCard");
                AvLay.Place(root, x, 0f, w, FullH);
                AvFrame hitFrame = FrontKit.Panel(root, "Hit", 0f, 0f, w, FullH);
                hitFrame.Paint(Color.clear, Color.clear);
                hit = AvHit.On(hitFrame);
                hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left && mode == 1) room.Do(() => room.PickTeam(slot)); };
                gfx = FrontVector.Add(root, "Jacket");
                AvLay.Place(gfx.rectTransform, 0f, 0f, w, FullH);
                tabText = FrontKit.Mono(root, "TabText", 10f, 0f, TabW, TabH, 10f, TextAlignmentOptions.Midline, true, 0.5f);
                remark = FrontKit.Mono(root, "Remark", 10f + 3 * TabStep + TabW + 8f, 0f, w - (10f + 3 * TabStep + TabW + 8f) - 8f, TabH, 10f, TextAlignmentOptions.MidlineRight);
                call = FrontKit.Cond(root, "Call", InfoX, TabH + 1f, 34f, 21f, 16f, TextAlignmentOptions.MidlineLeft);
                code = FrontKit.Mono(root, "Code", InfoX + 33f, TabH + 4f, 56f, 15f, 11f, TextAlignmentOptions.MidlineLeft, true);
                flags = FrontKit.Mono(root, "Flags", InfoX + 92f, TabH + 4f, w - InfoX - 92f - 8f - 84f, 15f, 10f, TextAlignmentOptions.MidlineLeft);
                control = FrontKit.Mono(root, "Control", w - 8f - 84f, TabH + 4f, 84f, 15f, 11f, TextAlignmentOptions.MidlineRight, true);
                mission = FrontKit.Mono(root, "Mission", InfoX, TabH + 23f, w - InfoX - 8f - 92f, 14f, 10.5f, TextAlignmentOptions.MidlineLeft);
                eta = FrontKit.Mono(root, "Eta", w - 8f - 88f, TabH + 23f, 88f, 14f, 10.5f, TextAlignmentOptions.MidlineRight);
                expKey = FrontKit.Mono(root, "ExpKey", InfoX, TabH + 36f, 54f, 14f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
                expVal = FrontKit.Mono(root, "ExpVal", 206f, TabH + 36f, 40f, 14f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                ammoKey = FrontKit.Mono(root, "AmmoKey", InfoX, TabH + 49.5f, 54f, 14f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
                ammoVal = FrontKit.Mono(root, "AmmoVal", 206f, TabH + 49.5f, 40f, 14f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                caption = FrontKit.Mono(root, "Caption", 3f, TabH + 47f, 56f, 14f, 10f, TextAlignmentOptions.Midline, true);
                stamp = new Stamp(root, w - 8f - 112f, TabH + 38.5f, 112f, 22f, -3f);
                home = FrontKit.Mono(root, "Home", 8f, VerbY, VerbX - 12f, 22f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
                for (int i = 0; i < verbs.Length; i++)
                {
                    int verb = i;
                    verbs[i] = room.Button(root, OrderWord[i], VerbX + i * (VerbW + VerbGap), VerbY, VerbW, 22f, i == 2 ? AvButtonStyle.Danger : AvButtonStyle.Default,
                        () => room.Do(() => room.actions.TeamOrder(slot, verb)), OrderHelp[i]);
                }
                chip = room.Button(root, "CHIP IN", VerbX, VerbY, ChipW, 22f, AvButtonStyle.Default, () => { if (room.last != null) FrontPanels.Donate(room.actions, Front.Sof, room.last, row); },
                    "Optional: speed the training up with your allocation. Front funds pay for it anyway.");
                float nx = VerbX + 4f * (VerbW + VerbGap) + 2f;
                note = FrontKit.Mono(root, "Note", nx, VerbY, w - nx - 8f, 22f, 10.5f, TextAlignmentOptions.MidlineLeft);
                root.gameObject.SetActive(false); // a card with no team or training row to show stays off until a paint needs it
            }

            private void Layout(int m, float y)
            {
                if (!shown) { root.gameObject.SetActive(true); shown = true; }
                AvLay.Place(root, x, y, w, FullH);
                if (m == mode) return;
                mode = m;
                bool team = m == 1;
                hit.Interactable = team;
                flags.gameObject.SetActive(team); control.gameObject.SetActive(team); eta.gameObject.SetActive(team);
                ammoKey.gameObject.SetActive(team); ammoVal.gameObject.SetActive(team); caption.gameObject.SetActive(team); remark.gameObject.SetActive(team); home.gameObject.SetActive(team);
                foreach (AvControl b in verbs) b.Rect.gameObject.SetActive(team);
                if (team && chip.Rect.gameObject.activeSelf) chip.Rect.gameObject.SetActive(false);
            }

            /// <summary>Takes the card out of the stack; returns the y the next card starts at.</summary>
            public float Hide(float y)
            {
                if (shown) { root.gameObject.SetActive(false); shown = false; mode = -1; }
                return y;
            }

            // Cosmetic flavour: a typed margin note per stamp word, beside the folder tab (it carries no reading of its own).
            private static string Remark(string stampWord)
            {
                switch (stampWord)
                {
                    case "STANDBY": return "// AWAITING TASKING";
                    case "LIFT ASKED": return "// BIRD INBOUND";
                    case "TASKED": case "MOVING": return "// ON THE MARCH";
                    case "ON SITE": return "// WORKING THE OBJECTIVE";
                    case "PINNED": return "// NEEDS COVER";
                    case "LOST": return "// NO CONTACT";
                    case "EXFIL": return "// HEADING HOME";
                    case "RAISING": return "// IN TRAINING";
                    case "WIA": return "// LICKING WOUNDS";
                    default: return "";
                }
            }

            /// <summary>Paints <paramref name="t"/> at <paramref name="y"/> and returns the y below it.</summary>
            public float Paint(FrontRoomView fv, in SofTeamView t, float y)
            {
                FrontVector g = gfx;
                g.Clear();
                slot = t.Slot;
                float tabX = 10f + slot * TabStep;
                AvLay.Place(tabText.rectTransform, tabX, 0f, TabW, TabH);
                AvText.Set(tabText, "TAB " + (slot + 1));
                AvText.Set(call, t.Callsign);
                AvText.Set(code, t.Codename);
                if (t.Training)
                {
                    // A team on its way: an empty jacket (dashed), its bar, CHIP IN on a dark plate while the funding bar fills.
                    Layout(2, y);
                    row = t.Row;
                    Color ghost = Paper.Khaki.WithAlpha(0.55f);
                    g.Rect(0f, TabH, w - 2f, BodyH, Paper.Khaki.WithAlpha(0.09f));   // a tint (alpha under 0.2), not a ground
                    Hand.DashRect(g, 0.5f, TabH + 0.5f, w - 2f, BodyH - 1f, 1f, ghost);
                    g.Line(new Vector2(tabX, TabH), new Vector2(tabX + 5f, 0.5f), 1f, ghost);
                    g.Dashed(new Vector2(tabX + 5f, 0.5f), new Vector2(tabX + TabW - 5f, 0.5f), 1f, ghost, 4f, 3f);
                    g.Line(new Vector2(tabX + TabW - 5f, 0.5f), new Vector2(tabX + TabW, TabH), 1f, ghost);
                    Hand.BustGhost(g, new Rect(11f, TabH + 5f, 42f, 38f), ghost);
                    Hand.Gauge(g, 118f, TabH + 39f, 84f, 7f, t.Progress, t.Building ? Paper.GaugeWarn : Paper.GaugeAmmo);
                    int ap = FrontPanels.QueueRow(fv, t.Row, out FrontQueueView q) ? FrontPanels.ChipAp(q) : 0;
                    bool offer = ap > 0;
                    if (chip.Rect.gameObject.activeSelf != offer) chip.Rect.gameObject.SetActive(offer);
                    if (offer)
                    {
                        HolePlate(g, VerbX, VerbY, ChipW, 22f, fv.Allocation >= ap);
                        OpsKit.Label(chip, "CHIP IN · " + FrontPanels.Pays(ap));
                        OpsKit.Enable(chip, fv.Allocation >= ap);
                    }
                    g.Flush();
                    tabText.color = Paper.BoardInk; call.color = Paper.BoardInk; code.color = Paper.BoardInk;
                    FrontKit.Set(mission, FrontKit.Fit(mission, "NEW TEAM · " + t.State, 10.5f), RoomInk.Gold);
                    FrontKit.Set(expKey, t.Building ? "TRAINING" : "FUNDED", Paper.BoardInk);
                    FrontKit.Set(expVal, Mathf.RoundToInt(Mathf.Clamp01(t.Progress) * 100f) + " %", Paper.BoardInk);
                    FrontKit.Set(note, FrontKit.Fit(note, t.Building ? "RAISES AT A CAMP WITH ROOM" : "CHIP IN SPEEDS IT UP", 10.5f), Paper.BoardInk);
                    stamp.Set("IN TRAINING", Paper.BoardInk);
                    return y + FullH + 4f;
                }
                Layout(1, y);
                bool sel = t.Selected;
                Color fill = Paper.TabFill(t.Tone);
                Color paper = sel ? Paper.FolderHi : Paper.Folder;
                g.Rect(2f, TabH + 2f, w - 2f, BodyH, Paper.Shadow);
                g.Rect(0f, TabH, w - 2f, BodyH, paper);
                g.Frame(0f, TabH, w - 2f, BodyH, sel ? 2f : 1f, sel ? Paper.Pen : Paper.FolderEdge);
                g.Rect(sel ? 2f : 1f, TabH + (sel ? 2f : 1f), 4f, BodyH - (sel ? 4f : 2f), fill);   // the state's colour runs down the spine
                Hand.Tab(g, tabX, 0f, TabW, TabH + 1f, fill, Paper.FolderEdge);
                var photo = new Rect(11f, TabH + 5f, 42f, 38f);
                Hand.Bust(g, photo, slot, t.Wounded, t.Stamp == "LOST");
                Hand.Gauge(g, 118f, TabH + 39f, 84f, 7f, t.Exposure / 100f, Paper.ExposureBar(t.Exposure));
                Hand.Gauge(g, 118f, TabH + 52.5f, 84f, 7f, t.Ammo / 100f, t.Ammo < SofDesk.AmmoPerMission ? Paper.Grease : Paper.GaugeAmmo);
                if (sel) Hand.Clip(g, 49f, TabH + 19f);   // the picked jacket has its photograph clipped on
                // The verbs: dark plates under the switches (the kit's translucent tints would wash out on a folder), lit as the host would take them.
                for (int i = 0; i < verbs.Length; i++)
                {
                    bool on = (t.VerbOn & (1 << i)) != 0;
                    HolePlate(g, VerbX + i * (VerbW + VerbGap), VerbY, VerbW, 22f, on);
                    verbs[i].Interactable = on;
                    verbs[i].Latched = (t.VerbOnLatched & (1 << i)) != 0;
                }
                g.Flush();

                tabText.color = Paper.TabInk(t.Tone);
                call.color = Paper.Pen; code.color = Paper.PenDim;
                FrontKit.Set(remark, Remark(t.Stamp), Paper.BoardInk);
                FrontKit.Set(caption, t.Helicopter ? "HELO" : "GROUND", Paper.PenDim);
                // Flags read "GROUND · PUSH · WIA": the first word is the caption under the photo, the rest sit beside the codename.
                int cut = t.Flags.IndexOf(" · ", StringComparison.Ordinal);
                FrontKit.Set(flags, cut > 0 ? C2Kit.FitTo(flags, t.Flags.Substring(cut + 3), w - InfoX - 92f - 8f - 84f) : "", Paper.PenDim);
                FrontKit.Set(control, C2Kit.FitTo(control, t.Control, 84f), t.Locked ? Paper.Amber : t.Control == "AUTO" ? Paper.PenDim : Paper.Olive);
                FrontKit.Set(mission, FrontKit.Fit(mission, t.Mission, 10.5f), Paper.Pen);
                FrontKit.Set(eta, C2Kit.FitTo(eta, t.Eta, 88f), Paper.PenDim);
                FrontKit.Set(expKey, "EXPOSURE", Paper.PenDim);
                FrontKit.Set(ammoKey, "AMMO", Paper.PenDim);
                FrontKit.Set(expVal, t.Exposure + " %", Paper.ExposureText(t.Exposure));
                FrontKit.Set(ammoVal, t.Ammo + " %", t.Ammo < SofDesk.AmmoPerMission ? Paper.Red : Paper.Pen);
                FrontKit.Set(home, FrontKit.Fit(home, t.Home, 10.5f), Paper.Pen);
                FrontKit.Set(note, FrontKit.Fit(note, t.VerbNote, 10.5f), t.Locked ? Paper.Amber : Paper.PenDim);
                stamp.Set(t.Stamp, Paper.Of(t.Tone));
                return y + FullH + 4f;
            }
        }
    }
}

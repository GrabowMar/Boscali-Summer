using System;
using System.Globalization;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Presentation.C2;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The SOF front's room (R5), laid out as an operations board of paper. Top: the presence / infiltration map as an acetate overlay (grease-pencil
    /// routes, china-marker target circles, hatched exposure areas, push-pin camps; own camps, the operation footprint, held buildings, revealed targets
    /// and enemy teams). Bottom, left to right: the TEAM ROSTER (a personnel jacket per raised team with a folder tab, ID photo, bars and a rubber
    /// state stamp, one line per empty slot, the worst warning as an alert slip, the events kept across refreshes typed on teleprinter paper), the TARGET
    /// LIST (an index card per mirrored target, whatever the map clusters, and the picked target's package) and the TASKING ORDER (a fragmentary-order
    /// form: SITUATION = aim, MISSION = the five rows, EXECUTION = team and estimate, SERVICE SUPPORT = cost and ammo, COMMAND AND SIGNAL = authority and
    /// ISSUE, then movement with EXFIL apart). Paints from <see cref="ShadowRoomView"/> only; picking a mission only drafts, ISSUE sends once.
    /// Everything drawn in paper, stamps, pins and pencil is cosmetic flavour of existing fields: it adds no reading and no input.
    /// </summary>
    internal sealed class ShadowRoom : IFrontRoom
    {
        private const float TopH = 480f, Gap = 8f, ColW = 270f, RosterW = 390f, ListW = 352f;
        private const int Cols = 12, Rows = 8, MaxTeams = 4, MaxTargets = SofWire.MaxTargets, MaxCamps = 3, MaxHeld = 4, MaxEnemies = 4, Missions = 5, LogLines = ShadowRoomView.MaxHistory;
        private static readonly string[] OrderWord = { "PUSH", "HOLD", "EXFIL", "LIFT" };
        private static readonly string[] OrderHelp =
        {
            "Push: move faster and 1.5x as visible. Toggle.",
            "Hold: stay put and let exposure fall twice as fast. Toggle.",
            "Exfil: abandon the mission and go home.",
            "Lift: call a helicopter to pick the team up where it stands.",
        };

        // Fixed standing orders, worded from the rule constants so they cannot drift from the host.
        private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;
        private static readonly string[] Sop =
        {
            "SOP 1 · FOOT " + (SofRules.SpeedMetresPerSecond * 3.6f).ToString("0", Inv) + " KM/H · PUSH " + SofRules.PushSpeedFactor.ToString("0.#", Inv) + "x FASTER, " + SofRules.PushExposureFactor.ToString("0.#", Inv) + "x AS VISIBLE",
            "SOP 2 · ENEMY WITHIN " + (SofRules.ExposureRadius / 1000f).ToString("0.#", Inv) + " KM RAISES EXPOSURE · " + SofRules.PinExposure.ToString("0", Inv) + " % = PINNED",
            "SOP 3 · EACH MISSION SPENDS " + SofDesk.AmmoPerMission.ToString("0", Inv) + " % AMMO · CAMPS REFILL IT",
        };

        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private float heroW, colX;
        private FrontMapPlate plate;

        private FrontBox situation, roster, targets, composer;
        private FrontVector side, listG, rosterG, formG;
        private FrontMap.AirbaseLayer airbases;
        private readonly TMP_Text[] teamLabels = new TMP_Text[MaxTeams], targetLabels = new TMP_Text[MaxTargets], campLabels = new TMP_Text[MaxCamps],
            heldLabels = new TMP_Text[MaxHeld], enemyMarks = new TMP_Text[MaxEnemies];
        private TMP_Text fobLabel;
        private readonly Image[] teamHits = new Image[MaxTeams];
        private readonly int[] hitSlots = new int[MaxTeams], hitTargets = new int[MaxTargets];
        private readonly Image[] targetHits = new Image[MaxTargets];

        private TMP_Text opHead, opName, opNote, teamHead, teamCount, teamNote, heldHead, revHead, tapHead, tapLine, keyHead;
        private readonly TMP_Text[] heldName = new TMP_Text[MaxHeld], heldTime = new TMP_Text[MaxHeld], revKey = new TMP_Text[5], revVal = new TMP_Text[5], keyText = new TMP_Text[6];

        // Roster.
        private const float StripY = 28f, CardsTop = 62f, OrdersH = 14f + 3f * 15f, LineH = 15f;
        private Image warnRail, warnPlate;
        private TMP_Text warnText, logHead, orderHead;
        private AvControl warnButton;
        private readonly Card[] cards = new Card[MaxTeams];
        private readonly TMP_Text[] logLine = new TMP_Text[LogLines], orderLine = new TMP_Text[Sop.Length];

        // Target list.
        private const float RowH = 26f, ListTop = 28f, FileMin = 87f, FileTabH = 15f;
        private float fileY, fileH;
        private readonly Image[] rowHit = new Image[MaxTargets];
        private readonly int[] rowId = new int[MaxTargets];
        private readonly TMP_Text[] rowName = new TMP_Text[MaxTargets], rowDetail = new TMP_Text[MaxTargets], rowTag = new TMP_Text[MaxTargets];
        private TMP_Text fileTab, fileA, fileB, fileC, fileD, fileNote, listEmpty, listHint;
        private Stamp fileStamp;
        private static readonly string ReconHint = "RECON A GROUND POINT · IT REVEALS " + (SofRules.ReconRadius / 1000f).ToString("0.#", Inv) + " KM FOR "
            + FrontKit.Clock(SofRules.ReconRevealSeconds);

        // Composer.
        private static readonly float[] FormRules = { 89f, 226f, 256f, 286.5f };
        private const float SheetX = 6f, SheetY = 26f, SheetBottom = 349f, FormL = 22f, FormV = 144f;
        private readonly AvControl[] mission = new AvControl[Missions], order = new AvControl[4];
        private readonly TMP_Text[] missionA = new TMP_Text[Missions], missionB = new TMP_Text[Missions], paraKey = new TMP_Text[5];
        private TMP_Text formTitle, formSub, teamLine, aimLine, aimNote, quoteLine, costLine, quoteNote, authLeft, authRight, issueNote, exfilNote, draftMark;
        private AvControl issue;
        private Stamp eyesStamp;
        private string operatorCall = "", dtg = "";
        private bool signed;

        private int selectedSlot = -1, selectedTarget;
        private ShadowRoomView lastView;
        private bool pressed;
        private Vector2 pressAt;
        private const float ClickSlop = 5f, MarkerClear = 22f; // 22 covers the corners of the 34x26 team and 30x30 target hit boxes, so one click never selects a marker and drafts a point
        private readonly MissionKind[] missionKind = new MissionKind[Missions];
        private readonly string[] helpShown = new string[Missions];

        public ShadowRoom(IFrontActions actions, FrontSkin skin)
        {
            this.actions = actions;
            this.skin = skin;
        }

        private static Color Own => AvInk.State(AvState.Ready);

        private void Do(Action a) { actions.Touch(); a(); }

        /// <summary>Picks a team (local, never an order) and drops the note of an earlier cleared pick.</summary>
        private void PickTeam(int slot)
        {
            if (lastView != null) lastView.Reason = "";
            actions.SelectTeam(slot);
        }

        /// <summary>Picks a target by id, from the map or the list: the same call, and it replaces any RECON point.</summary>
        private void PickTarget(int id)
        {
            if (lastView != null) { lastView.HasDraft = false; lastView.Reason = ""; }
            actions.SelectTarget(id);
        }

        /// <summary>Drafts (or un-drafts) a mission for the picked team. Nothing is sent; the controller repaints with the new quote.</summary>
        private void Draft(int slot)
        {
            if (lastView == null || selectedSlot < 0) return;
            lastView.DraftMission = lastView.DraftMission == missionKind[slot] ? MissionKind.None : missionKind[slot];
            lastView.Reason = "";
            actions.SelectTeam(selectedSlot);
        }

        public void Build(RectTransform hero, float w, float h)
        {
            heroW = w;
            colX = w - 12f - ColW;
            BuildSituation(hero);
            float y = TopH + Gap, bh = h - y, cx = RosterW + Gap + ListW + Gap;
            BuildRoster(hero, 0f, y, RosterW, bh);
            BuildTargets(hero, RosterW + Gap, y, ListW, bh);
            BuildComposer(hero, cx, y, w - cx, bh);
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
                BoardInk = RoomInk.Rgb(0xA89F78), Hole = RoomInk.Rgb(0x0C0B08), Khaki = RoomInk.Khaki, TabIdle = RoomInk.Rgb(0xA8966A),
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

            /// <summary>A reconnaissance photograph: night-vision ground with seeded building blocks and tracks, a crosshair and the target marked in china marker.</summary>
            public static void Recon(FrontVector g, Rect r, int seed, TargetKind kind, bool live)
            {
                g.Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f, Paper.PhotoPaper);
                g.Rect(r.x, r.y, r.width, r.height, Paper.PhotoDark);
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
                for (int i = 0; i < 9; i++)
                {
                    float u = Hash(seed, i * 3), v = Hash(seed, i * 3 + 1), s = 0.08f + 0.12f * Hash(seed, i * 3 + 2);
                    float bw = Mathf.Min(r.width * s * 1.4f, r.width), bh = Mathf.Min(r.height * s * 1.2f, r.height);
                    float bx = r.x + (r.width - bw) * u, by = r.y + (r.height - bh) * v;
                    g.Rect(bx, by, bw, bh, RoomInk.Rgba(0.36f, 0.45f, 0.30f, 0.55f));
                }
                g.Line(new Vector2(r.x, r.y + r.height * (0.25f + 0.5f * Hash(seed, 40))), new Vector2(r.xMax, r.y + r.height * (0.25f + 0.5f * Hash(seed, 41))), 1.6f, RoomInk.Rgba(0.62f, 0.7f, 0.5f, 0.4f));
                g.Line(new Vector2(r.x + r.width * (0.2f + 0.6f * Hash(seed, 42)), r.y), new Vector2(r.x + r.width * (0.2f + 0.6f * Hash(seed, 43)), r.yMax), 1.4f, RoomInk.Rgba(0.62f, 0.7f, 0.5f, 0.35f));
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

        // ---- Situation -----------------------------------------------------------------------------------------------

        private TMP_Text Head(RectTransform b, string name, float x, float y, string text)
        {
            TMP_Text t = FrontKit.Mono(b, name, x, y, ColW, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 1.5f);
            t.text = text;
            return t;
        }

        private const float KeyX = 14f, KeyY = 395f;
        private static readonly Rect HeldCard = new Rect(8f, 29f, 282f, 168f), KeyCard = new Rect(8f, 392f, 410f, 58f);
        private Rect OpCard => new Rect(colX - 6f, 29f, ColW + 12f, 162f);
        private Rect QrfCard => new Rect(colX - 6f, 197f, ColW + 12f, 40f);
        private const float SweepW = 92f;
        private AvControl sweep;
        private TMP_Text qrfLine;

        private void BuildSituation(RectTransform hero)
        {
            situation = new FrontBox(hero, skin, "Shadow", 0f, 0f, heroW, TopH, "SOF SITUATION · PRESENCE AND INFILTRATION", AvIcon.Eye, FrontLook.Folder).Tagged("FILE 07-S");
            RectTransform b = situation.Rect;
            plate = new FrontMapPlate(b, skin, heroW, TopH, Cols, Rows);
            side = FrontVector.Add(b, "Side");   // overlay notes on the map
            AvLay.Place(side.rectTransform, 0f, 0f, heroW, TopH);
            plate.Labels.Fixed(HeldCard);
            plate.Labels.Fixed(KeyCard);
            plate.Labels.Fixed(OpCard);
            plate.Labels.Fixed(QrfCard);
            airbases = new FrontMap.AirbaseLayer(b);
            fobLabel = FrontKit.Mono(b, "FobLabel", 0f, 0f, 190f, 14f, 10.5f, TextAlignmentOptions.TopLeft);
            for (int i = 0; i < MaxTeams; i++) teamLabels[i] = Pool(b, "TeamLabel" + i, 170f, 28f);
            for (int i = 0; i < MaxTargets; i++) targetLabels[i] = Pool(b, "TargetLabel" + i, 170f, 14f);
            for (int i = 0; i < MaxCamps; i++) campLabels[i] = Pool(b, "CampLabel" + i, 170f, 28f);
            for (int i = 0; i < MaxHeld; i++) heldLabels[i] = Pool(b, "HeldLabel" + i, 170f, 28f);
            for (int i = 0; i < MaxEnemies; i++)
            {
                enemyMarks[i] = FrontKit.Mono(b, "Enemy" + i, 0f, 0f, 18f, 18f, 14f, TextAlignmentOptions.Midline, true);
                enemyMarks[i].text = "?";
                enemyMarks[i].gameObject.SetActive(false);
            }
            fobLabel.gameObject.SetActive(false);
            qrfLine = FrontKit.Mono(b, "QrfLine", colX, 204f, ColW - SweepW - 6f, 26f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            sweep = Button(b, "SWEEP", colX + ColW - SweepW, 204f, SweepW, 26f, AvButtonStyle.Danger, () => Do(() => actions.Sweep()),
                "Send a QRF after every hostile team detected near our sites: it reaches them " + (int)SofRules.SweepSeconds + " s after the press, costs " + SofRules.SweepCost +
                " CR and hunts them for " + (int)SofRules.HuntSeconds + " s (their exposure climbs), then " + (int)SofRules.SweepCooldownSeconds + " s to refit.");
            sweep.Interactable = false;
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

            opHead = Head(b, "OpHead", colX, 34f, "OPERATION FOOTPRINT");
            opName = FrontKit.Mono(b, "OpName", colX, 52f, ColW, 18f, 12f, TextAlignmentOptions.MidlineLeft, true);
            opNote = FrontKit.Mono(b, "OpNote", colX, 70f, ColW, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            teamHead = Head(b, "TeamHead", colX, 96f, "TEAMS IN THE FIELD");
            teamCount = FrontKit.Mono(b, "TeamCount", colX, 112f, 120f, 36f, 24f, TextAlignmentOptions.MidlineLeft, true);
            teamNote = FrontKit.Mono(b, "TeamNote", colX + 110f, 114f, ColW - 110f, 30f, 10.5f, TextAlignmentOptions.MidlineRight);
            tapHead = Head(b, "TapHead", colX, 152f, "SIGNAL TAP");
            tapLine = FrontKit.Mono(b, "TapLine", colX, 170f, ColW, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            heldHead = Head(b, "HeldHead", 16f, 34f, "HELD BUILDINGS");
            for (int i = 0; i < MaxHeld; i++)
            {
                heldName[i] = FrontKit.Mono(b, "HeldName" + i, 16f, 52f + i * 16f, 170f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                heldTime[i] = FrontKit.Mono(b, "HeldTime" + i, 186f, 52f + i * 16f, 96f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true);
            }
            revHead = Head(b, "RevHead", 16f, 124f, "REVEALED");
            string[] rk = { "GROUND", "ANCHORS", "BUILDINGS", "RELAYS", "ENEMY TEAMS" };
            for (int i = 0; i < 5; i++)
            {
                revKey[i] = FrontKit.Mono(b, "RevKey" + i, 16f + (i % 2) * 135f, 142f + (i / 2) * 16f, 100f, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
                revKey[i].text = rk[i];
                revVal[i] = FrontKit.Mono(b, "RevVal" + i, 16f + (i % 2) * 135f + 80f, 142f + (i / 2) * 16f, 45f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true);
            }
            keyHead = Head(b, "KeyHead", KeyX + 4f, KeyY, "KEY");
            string[] words = { "TEAM", "ROUTE", "EXPOSURE", "CAMP", "TARGET", "ENEMY TEAM" };
            for (int i = 0; i < 6; i++)
            {
                keyText[i] = FrontKit.Mono(b, "Key" + i, KeyX + 26f + (i % 3) * 130f, KeyY + 18f + (i / 3) * 17f, 100f, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
                keyText[i].text = words[i];
            }
            skin.Add(() =>
            {
                // The footprint and held notes are paper pinned to the map; the key stays a dark acetate sticker.
                opHead.color = Paper.PenDim; teamHead.color = Paper.PenDim; heldHead.color = Paper.PenDim; revHead.color = Paper.PenDim; tapHead.color = Paper.PenDim;
                keyHead.color = AvInk.Dim;
                foreach (TMP_Text t in revKey) t.color = Paper.PenDim;
                foreach (TMP_Text t in keyText) t.color = AvInk.Dim;
                foreach (TMP_Text t in enemyMarks) t.color = AvInk.State(AvState.Danger);
            });
        }

        private static TMP_Text Pool(RectTransform b, string name, float w, float h)
        {
            TMP_Text t = FrontKit.Mono(b, name, 0f, 0f, w, h, 10.5f, TextAlignmentOptions.TopLeft);
            t.gameObject.SetActive(false);
            return t;
        }

        // ---- Roster ----------------------------------------------------------------------------------------------------

        private void BuildRoster(RectTransform hero, float x, float y, float w, float h)
        {
            roster = new FrontBox(hero, skin, "Roster", x, y, w, h, "TEAM ROSTER", AvIcon.UsersGroup, FrontLook.Folder).Tagged("FILE 07-A");
            RectTransform b = roster.Rect;
            // The alert slip: a card with a margin rail in the state's colour.
            FrontKit.Solid(b, "WarnBack", 8f, StripY, w - 16f, 28f, Paper.Card);
            warnRail = FrontKit.Solid(b, "WarnRail", 8f, StripY, 4f, 28f, Color.clear);
            warnText = FrontKit.Mono(b, "WarnText", 20f, StripY, w - 38f, 28f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            warnPlate = FrontKit.Solid(b, "WarnPlate", w - 129f, StripY + 1f, 114f, 26f, Paper.Hole);   // a dark plate under the button: the kit's translucent tints would wash out on a card
            warnButton = Button(b, "SELECT TEAM", w - 128f, StripY + 2f, 112f, 24f, AvButtonStyle.Default, () => Do(() => { if (lastView != null && lastView.WarnSlot >= 0) PickTeam(lastView.WarnSlot); }),
                "Select the team the warning names. This sends no order.");
            rosterG = FrontVector.Add(b, "LogPaper");   // under the log lines, over nothing else
            AvLay.Place(rosterG.rectTransform, 0f, 0f, w, h);
            for (int i = 0; i < MaxTeams; i++)
            {
                int slot = i;
                cards[i] = new Card(b, skin, slot, 8f, w - 16f, () => Do(() => PickTeam(slot)));
            }
            logHead = FrontKit.Mono(b, "LogHead", 22f, 0f, w - 44f, 14f, 10f, TextAlignmentOptions.MidlineLeft, true, 1.5f);
            logHead.text = "SIGNAL LOG · TELEPRINTER 07";
            for (int i = 0; i < LogLines; i++)
            {
                logLine[i] = FrontKit.Mono(b, "Log" + i, 24f, 0f, w - 48f, LineH, 10.5f, TextAlignmentOptions.MidlineLeft);
                logLine[i].gameObject.SetActive(false);
            }
            logHead.gameObject.SetActive(false);
            orderHead = Head(b, "OrderHead", 18f, h - 6f - OrdersH, "STANDING ORDERS");
            AvLay.Place(orderHead.rectTransform, 18f, h - 6f - OrdersH, ColW, 14f);
            for (int i = 0; i < orderLine.Length; i++)
            {
                orderLine[i] = FrontKit.Mono(b, "Sop" + i, 18f, h - 6f - OrdersH + 15f + i * LineH, w - 36f, LineH, 10f, TextAlignmentOptions.MidlineLeft);
                orderLine[i].text = Sop[i];
            }
            skin.Add(() =>
            {
                logHead.color = Paper.PenDim; orderHead.color = Paper.PenDim;
                foreach (TMP_Text t in orderLine) t.color = Paper.PenDim;
            });
        }

        private void PaintRoster(ShadowRoomView s)
        {
            int up = 0;
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present) up++;
            Color wk = Tone(s.WarnTone);
            roster.SetMeta(s.Active ? up + " / " + s.TeamCap + " TEAMS" : s.Meta, s.WarnSlot >= 0 ? wk : AvInk.Dim);

            // The worst standing warning as an alert slip, with SELECT TEAM; it sends no order.
            bool offer = s.WarnSlot >= 0;
            warnRail.color = Paper.TabFill(s.WarnTone);
            FrontKit.Set(warnText, C2Kit.FitTo(warnText, s.WarnText, offer ? 236f : 346f), Paper.Of(s.WarnTone));
            warnButton.Rect.gameObject.SetActive(offer);
            warnPlate.gameObject.SetActive(offer);
            if (offer)
            {
                bool already = s.WarnSlot == s.SelectedSlot;
                warnButton.Label = already ? "SELECTED" : "SELECT TEAM";
                warnButton.Interactable = !already;
            }

            // Free slots are one line each, or unfilled jackets when that still leaves the log its lines and the orders their foot.
            int vacant = 0;
            for (int i = 0; i < s.Teams.Count && i < MaxTeams; i++) if (!s.Teams[i].Present) vacant++;
            float compact = CardsTop + up * (Card.FullH + 4f) + vacant * (Card.EmptyH + 3f);
            bool vacantCards = vacant > 0 && compact + vacant * (Card.FullH + 4f - Card.EmptyH - 3f) <= roster.Height - 6f - OrdersH - 4f - LineH * (1 + Math.Min(s.History.Count, LogLines));
            float y = CardsTop;
            for (int i = 0; i < MaxTeams; i++) y = i < s.Teams.Count ? cards[i].Paint(s.Teams[i], y, vacantCards) : cards[i].Hide(y);

            // The events the host reported (newest first) take what the cards leave, ahead of the fixed standing orders at the foot.
            float foot = roster.Height - 6f, ordTop = foot - OrdersH, logTop = y + 4f + LineH;
            bool quiet = s.History.Count == 0;
            int want = quiet ? 1 : Math.Min(s.History.Count, LogLines), fitWith = (int)((ordTop - 9f - logTop) / LineH);
            bool orders = y + 6f <= ordTop - 5f && fitWith >= want;
            int n = Math.Min(want, Math.Max(0, orders ? fitWith : (int)((foot - logTop) / LineH)));
            logHead.gameObject.SetActive(n > 0);
            AvLay.Place(logHead.rectTransform, 22f, y + 3f, roster.Width - 44f, 14f);

            // Teleprinter paper: a strip with tractor holes down both edges and a perforation under the head.
            FrontVector g = rosterG;
            g.Clear();
            if (n > 0)
            {
                float top = y + 1f, bottom = logTop + n * LineH + 2f, pw = roster.Width - 16f;
                g.Rect(11f, top + 2f, pw, bottom - top, Paper.Shadow);
                g.Rect(8f, top, pw, bottom - top, Paper.Card);
                g.Frame(8f, top, pw, bottom - top, 1f, Paper.CardEdge);
                for (float hy = top + 7f; hy < bottom - 3f; hy += 11f) { Hand.Hole(g, 14f, hy, 2.2f); Hand.Hole(g, roster.Width - 14f, hy, 2.2f); }
                g.Dashed(new Vector2(20f, y + 18f), new Vector2(roster.Width - 20f, y + 18f), 1f, Paper.CardEdge, 3f, 3f);
            }
            if (orders)
            {
                // The standing orders: a laminated card at the foot, pinned.
                float ptop = ordTop - 5f, ph = OrdersH + 9f, pw = roster.Width - 16f;
                g.Rect(11f, ptop + 2f, pw, ph, Paper.Shadow);
                g.Rect(8f, ptop, pw, ph, Paper.Card);
                g.Frame(8f, ptop, pw, ph, 1f, Paper.CardEdge);
                Hand.Pin(g, new Vector2(roster.Width - 20f, ptop + 9f), 3.8f, Paper.Grease);
            }
            g.Flush();
            for (int i = 0; i < LogLines; i++)
            {
                bool on = i < n;
                if (logLine[i].gameObject.activeSelf != on) logLine[i].gameObject.SetActive(on);
                if (!on) continue;
                AvLay.Place(logLine[i].rectTransform, 24f, y + 4f + LineH + i * LineH, roster.Width - 48f, LineH);
                if (quiet) { FrontKit.Set(logLine[i], C2Kit.Tint("#--", Paper.PenDim) + " " + C2Kit.Tint("NO TRAFFIC · THE LINE IS QUIET", Paper.PenDim), Paper.Pen); continue; }
                FrontLogLine l = s.History[s.History.Count - 1 - i];
                // Typed: the serial number in faded ribbon, the line in the ribbon of its tone.
                FrontKit.Set(logLine[i], C2Kit.Tint(l.Stamp, Paper.PenDim) + " " + C2Kit.Tint(C2Kit.FitTo(logLine[i], l.Text, roster.Width - 48f - 34f), Paper.Of(l.Tone)), Paper.Pen);
            }
            orderHead.gameObject.SetActive(orders);
            foreach (TMP_Text t in orderLine) t.gameObject.SetActive(orders);
        }

        // ---- Target list -------------------------------------------------------------------------------------------------

        private void BuildTargets(RectTransform hero, float x, float y, float w, float h)
        {
            targets = new FrontBox(hero, skin, "Targets", x, y, w, h, "TARGET LIST", AvIcon.Crosshair, FrontLook.Folder).Tagged("FILE 07-T");
            RectTransform b = targets.Rect;
            listG = FrontVector.Add(b, "ListGfx");
            AvLay.Place(listG.rectTransform, 0f, 0f, w, h);
            float cw = (w - 24f) / 2f;
            for (int i = 0; i < MaxTargets; i++)
            {
                int slot = i;
                float rx = 8f + (i % 2) * (cw + 8f), ry = ListTop + (i / 2) * 28f;
                Image hit = FrontKit.Solid(b, "RowHit" + i, rx, ry, cw, RowH, Color.clear);
                hit.raycastTarget = true;
                AvHit.On(hit).Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) Do(() => PickTarget(rowId[slot])); };
                hit.gameObject.SetActive(false);
                rowHit[i] = hit;
                rowName[i] = FrontKit.Mono(b, "RowName" + i, rx + 38f, ry, cw - 42f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
                rowDetail[i] = FrontKit.Mono(b, "RowDetail" + i, rx + 38f, ry + 14f, 50f, 12f, 10f, TextAlignmentOptions.MidlineLeft);
                rowTag[i] = FrontKit.Mono(b, "RowTag" + i, rx + 88f, ry + 14f, cw - 92f, 12f, 10f, TextAlignmentOptions.MidlineRight, true);
                rowName[i].gameObject.SetActive(false);
                rowDetail[i].gameObject.SetActive(false);
                rowTag[i].gameObject.SetActive(false);
            }
            fileTab = FrontKit.Mono(b, "FileTab", 14f, 0f, 92f, 15f, 10f, TextAlignmentOptions.Midline, true, 1f);
            fileA = FrontKit.Mono(b, "FileA", 14f, 0f, w - 28f, 16f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
            fileB = FrontKit.Mono(b, "FileB", 14f, 0f, w - 28f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft);
            fileC = FrontKit.Mono(b, "FileC", 14f, 0f, w - 28f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft);
            fileD = FrontKit.Mono(b, "FileD", 14f, 0f, w - 28f, 14f, 10f, TextAlignmentOptions.MidlineLeft);
            fileNote = FrontKit.Mono(b, "FileNote", 14f, 0f, w - 28f, 28f, 10f, TextAlignmentOptions.TopLeft);
            fileNote.enableWordWrapping = true;
            fileStamp = new Stamp(b, 0f, 0f, 92f, 22f, 6f);
            // The empty list: a card tray with its label slip, and what would fill it.
            listEmpty = FrontKit.Mono(b, "ListEmpty", 14f, ListTop + 70f, w - 28f, 18f, 12f, TextAlignmentOptions.Midline, true, 2f);
            listHint = FrontKit.Mono(b, "ListHint", 14f, ListTop + 94f, w - 28f, 16f, 10.5f, TextAlignmentOptions.Midline);
            listEmpty.text = "NO TARGETS REVEALED";
            listEmpty.color = Paper.Pen;
        }

        /// <summary>Cosmetic flavour: a two-line analyst note per target kind, worded from the best mission of that kind (no reading of its own).</summary>
        private static string AnalystNote(TargetKind kind, bool exploit, bool resisted)
        {
            string note = kind == TargetKind.Ground ? "PATROL ROUTE UNCONFIRMED. LASE FROM A RIDGE OR OBSERVE."
                : kind == TargetKind.Anchor ? "HARDENED ANCHOR. SABOTAGE FROM THE FLANK, NOT THE ROAD."
                : kind == TargetKind.Building ? "SEIZE AND HOLD. EXPECT A COUNTERATTACK WITHIN MINUTES."
                : "NETWORK TAP POINT. KEEP THE TEAM QUIET AND ON SITE.";
            return resisted ? "SPACE BEATS SOF HERE. LEAVE IT TO THE SATELLITES." : exploit ? note + " SOFT TARGET." : note;
        }

        private void PaintTargets(ShadowRoomView s)
        {
            targets.SetMeta(s.Targets.Count + " REVEALED", s.Targets.Count > 0 ? FrontKit.Tone(AvState.Danger) : AvInk.Dim);
            FrontVector g = listG;
            g.Clear();
            int n = s.Targets.Count;
            // A tray of at least four rows: the cards grow while the list is short, shrink as it fills, and the picked target's package takes what is left.
            int rows = Math.Max(4, (n + 1) / 2);
            float pitch = Mathf.Clamp(Mathf.Floor((targets.Height - ListTop - 6f - FileMin - 8f) / rows), 28f, 44f);
            bool roomy = pitch >= 34f;
            float ch = pitch - (roomy ? 4f : 1f), cw = (targets.Width - 24f) / 2f;
            fileY = ListTop + rows * pitch + 2f;
            fileH = targets.Height - 6f - fileY;
            Color grease = Paper.Grease;
            int clipAt = -1;
            for (int i = 0; i < MaxTargets && i < rows * 2; i++)
            {
                float rx = 8f + (i % 2) * (cw + 8f), ry = ListTop + (i / 2) * pitch;
                if (i >= n)
                {
                    g.Rect(rx, ry, cw, ch, Paper.Khaki.WithAlpha(0.08f)); // a tint (alpha under 0.2), not a ground
                    Hand.DashRect(g, rx + 0.5f, ry + 0.5f, cw - 1f, ch - 1f, 1f, Paper.Khaki.WithAlpha(0.5f));
                }
            }
            for (int i = 0; i < MaxTargets; i++)
            {
                bool on = i < n;
                if (rowHit[i].gameObject.activeSelf != on) { rowHit[i].gameObject.SetActive(on); rowName[i].gameObject.SetActive(on); rowDetail[i].gameObject.SetActive(on); rowTag[i].gameObject.SetActive(on); }
                if (!on) continue;
                SofTargetView t = s.Targets[i];
                rowId[i] = t.Id;
                float rx = 8f + (i % 2) * (cw + 8f), ry = ListTop + (i / 2) * pitch;
                bool sel = t.Selected;
                // An index card: shadow, cream stock, faint ruling under both lines, a red margin, a punched hole.
                g.Rect(rx + 1.5f, ry + 2f, cw, ch, Paper.Shadow);
                g.Rect(rx, ry, cw, ch, Paper.Card);
                float ty = roomy ? ry + (ch - 28f) * 0.5f : ry + 0.5f;
                if (roomy)
                {
                    g.Rect(rx + 3f, ty + 14.4f, cw - 6f, 1f, RoomInk.Rgba(0.35f, 0.5f, 0.68f, 0.17f)); // ruling under both lines: alpha under 0.2, a tint and not a ground
                    g.Rect(rx + 3f, ty + 27.2f, cw - 6f, 1f, RoomInk.Rgba(0.35f, 0.5f, 0.68f, 0.17f));
                }
                g.Rect(rx + 34f, ry + 1f, 1f, ch - 2f, RoomInk.Rgba(0.78f, 0.25f, 0.2f, 0.3f));
                g.Frame(rx, ry, cw, ch, sel ? 2f : 1f, sel ? Paper.Pen : Paper.CardEdge);
                Hand.Hole(g, rx + 6.5f, ry + 6.5f, 2.3f);
                AvLay.Place(rowHit[i].rectTransform, rx, ry, cw, ch);
                float nw = cw - 42f, ly = ty + (roomy ? 14f : 13.5f);
                AvLay.Place(rowName[i].rectTransform, rx + 38f, ty, nw, 14.5f);
                AvLay.Place(rowDetail[i].rectTransform, rx + 38f, ly, 50f, 13f);
                AvLay.Place(rowTag[i].rectTransform, rx + 88f, ly, cw - 92f, 13f);
                // The kind glyph in typewriter ink, circled twice in china marker (a resisted one is struck through and circled in faded ink).
                Vector2 gp = new Vector2(rx + 21f, ry + ch * 0.5f);
                float ro = Mathf.Min(13f, ch * 0.5f - 1.5f);
                Color mark = t.Resisted ? Paper.PenDim : grease;
                Hand.Circle(g, gp, ro, 1.8f, mark, t.Id * 5 + 1);
                Hand.Circle(g, gp, ro - 3.2f, 1.4f, mark, t.Id * 5 + 3);
                TargetGlyph(g, t.Kind, gp, 5f, Paper.Pen);
                if (t.Resisted) g.Line(gp + new Vector2(-8f, 7f), gp + new Vector2(8f, -7f), 1.6f, Paper.Pen);
                if (t.Exploit) g.Disc(gp + new Vector2(ro * 0.72f, -ro * 0.72f), 2.8f, Paper.Amber, 8);
                if (sel) clipAt = i;
                FrontKit.Set(rowName[i], C2Kit.FitTo(rowName[i], t.Name, nw), Paper.Pen);
                // Detail is "D2 · LASE": the grid ref on the left, the best mission (or EXPLOIT / RESISTED) typed as a tag on the right.
                int cut = t.Detail.IndexOf(" · ", StringComparison.Ordinal);
                string grid = cut > 0 ? t.Detail.Substring(0, cut) : t.Detail, tag = cut > 0 ? t.Detail.Substring(cut + 3) : "";
                FrontKit.Set(rowDetail[i], "GRID " + grid, Paper.PenDim);
                FrontKit.Set(rowTag[i], C2Kit.FitTo(rowTag[i], tag, cw - 92f), t.Exploit ? Paper.Amber : t.Resisted ? Paper.PenDim : Paper.Blue);
            }
            if (clipAt >= 0)
            {
                float rx = 8f + (clipAt % 2) * (cw + 8f), ry = ListTop + (clipAt / 2) * pitch;
                Hand.Clip(g, rx + cw - 9f, ry + 9f);
            }

            bool none = n == 0;
            if (listEmpty.gameObject.activeSelf != none) { listEmpty.gameObject.SetActive(none); listHint.gameObject.SetActive(none); }
            if (none)
            {
                float mid = ListTop + rows * pitch * 0.5f;
                AvLay.Place(listEmpty.rectTransform, 14f, mid - 22f, targets.Width - 28f, 18f);
                AvLay.Place(listHint.rectTransform, 14f, mid + 6f, targets.Width - 28f, 16f);
                float sw = 224f;
                g.Rect((targets.Width - sw) * 0.5f + 2f, mid - 26f, sw, 26f, Paper.Shadow);
                g.Rect((targets.Width - sw) * 0.5f, mid - 28f, sw, 26f, Paper.Card);
                g.Frame((targets.Width - sw) * 0.5f, mid - 28f, sw, 26f, 1f, Paper.CardEdge);
                Hand.Pin(g, new Vector2((targets.Width - sw) * 0.5f + 10f, mid - 15f), 3.6f, Paper.Grease);
                bool teamUp = false;
                for (int i = 0; i < s.Teams.Count; i++) teamUp |= s.Teams[i].Present;
                FrontKit.Set(listHint, C2Kit.FitTo(listHint, !s.Active ? "WAITING FOR THE HOST" : teamUp ? ReconHint : "TRAIN A TEAM, THEN RECON GROUND", targets.Width - 28f),
                    s.Active ? FrontKit.Tone(AvState.Info) : AvInk.Dim);
            }

            PaintFile(g, s);
            g.Flush();
        }

        /// <summary>
        /// The picked target's package: a manila folder with its tab, a reconnaissance photograph and the four typed lines. A short list leaves it room: the photograph grows,
        /// the long lines go under it and a rubber stamp and an analyst note join in.
        /// </summary>
        private void PaintFile(FrontVector g, ShadowRoomView s)
        {
            float fx = 8f, fw = targets.Width - 16f, bodyY = fileY + FileTabH, bodyH = fileH - FileTabH;
            bool filed = s.SelectedTargetId != 0 || s.HasDraft;
            SofTargetView pick = default;
            bool havePick = false;
            for (int i = 0; i < s.Targets.Count; i++) if (s.Targets[i].Selected) { pick = s.Targets[i]; havePick = true; }
            g.Rect(fx + 2f, bodyY + 2f, fw, bodyH, Paper.Shadow);
            g.Rect(fx, bodyY, fw, bodyH, Paper.Folder);
            g.Frame(fx, bodyY, fw, bodyH, 1f, Paper.FolderEdge);
            Hand.Tab(g, fx + 8f, fileY, 96f, FileTabH + 1f, filed ? Paper.FolderHi : Paper.TabIdle, Paper.FolderEdge);
            bool wide = bodyH >= 125f, big = bodyH >= 160f;
            float pw = wide ? 112f : 58f, ph = wide ? 88f : Mathf.Min(bodyH - 14f, 46f);
            var photo = new Rect(fx + 10f, bodyY + 8f, pw, ph);
            Hand.Recon(g, photo, havePick ? pick.Id : 3, havePick ? pick.Kind : TargetKind.Ground, havePick);
            if (!havePick && filed) Hand.Circle(g, photo.center, Mathf.Min(13f, ph * 0.28f), 1.8f, Paper.Grease, 5); // a bare ground point: just the mark on a blank print
            float tx = photo.xMax + 12f, tw = fx + fw - tx - 6f;
            float cy = wide ? photo.yMax + 8f : bodyY + 37f, cx = wide ? fx + 12f : tx, cw = wide ? fw - 24f : tw;
            AvLay.Place(fileTab.rectTransform, fx + 8f, fileY, 96f, FileTabH);
            AvLay.Place(fileA.rectTransform, tx, bodyY + 6f, tw, 16f);
            AvLay.Place(fileB.rectTransform, tx, bodyY + 22f, tw, 15f);
            AvLay.Place(fileC.rectTransform, cx, cy, cw, 15f);
            AvLay.Place(fileD.rectTransform, cx, cy + 15f, cw, 14f);
            FrontKit.Set(fileTab, s.FileTab, Paper.Pen);
            FrontKit.Set(fileA, C2Kit.FitTo(fileA, s.FileA, tw), filed ? Paper.Pen : Paper.PenDim);
            FrontKit.Set(fileB, C2Kit.FitTo(fileB, s.FileB, tw), Paper.Blue);
            FrontKit.Set(fileC, C2Kit.FitTo(fileC, s.FileC, cw), Paper.PenDim);
            FrontKit.Set(fileD, C2Kit.FitTo(fileD, s.FileD, cw), Paper.Amber);
            bool note = big && havePick;
            if (fileNote.gameObject.activeSelf != note) fileNote.gameObject.SetActive(note);
            if (note)
            {
                float ny = cy + 38f;
                for (int i = 0; i < 3; i++) g.Rect(fx + 10f, ny + 12.5f + i * 13.5f, fw - 20f, 1f, RoomInk.Rgba(0.35f, 0.5f, 0.68f, 0.17f)); // ruled notes: alpha under 0.2, a tint and not a ground
                AvLay.Place(fileNote.rectTransform, fx + 12f, ny, fw - 24f, 28f);
                FrontKit.Set(fileNote, AnalystNote(pick.Kind, pick.Exploit, pick.Resisted), Paper.PenDim);
            }
            // A stamp on a package that is exploitable or resisted (the list tags carry the same words).
            if (wide && havePick && (pick.Exploit || pick.Resisted))
            {
                fileStamp.Place(fx + fw - 108f, bodyY + 46f);
                fileStamp.Set(pick.Resisted ? "RESISTED" : "EXPLOIT", pick.Resisted ? Paper.Red : Paper.Amber);
            }
            else fileStamp.Set("", Paper.Pen);
        }

        // ---- Composer ---------------------------------------------------------------------------------------------------

        private void BuildComposer(RectTransform hero, float x, float y, float w, float h)
        {
            composer = new FrontBox(hero, skin, "Composer", x, y, w, h, "TASKING ORDER", AvIcon.Clipboard, FrontLook.Folder).Tagged("FRAGO 03");
            RectTransform b = composer.Rect;
            formG = FrontVector.Add(b, "FormGfx");
            AvLay.Place(formG.rectTransform, 0f, 0f, w, h);
            // The "DRAFT" watermark (ornament, not for reading) sits on the sheet, under every line and control.
            draftMark = FrontKit.Cond(b, "GlyphDraft", 0f, 0f, 340f, 130f, 112f, TextAlignmentOptions.Midline);
            draftMark.text = "DRAFT";
            draftMark.characterSpacing = 10f;
            draftMark.color = Paper.Pen.WithAlpha(0.08f);
            Centered(draftMark.rectTransform, SheetX + 28f, 130f, 340f, 130f, 14f);

            float tw = w - FormL - 8f, vw = w - FormV - 14f;
            formTitle = FrontKit.Mono(b, "FormTitle", FormL, 29f, 286f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 0.5f);
            formSub = FrontKit.Mono(b, "FormSub", FormL, 43f, 286f, 14f, 10f, TextAlignmentOptions.MidlineLeft);
            eyesStamp = new Stamp(b, w - 98f, 30f, 88f, 22f, -4f);
            string[] keys = { "1. SITUATION", "2. MISSION", "3. EXECUTION", "4. SERVICE SUPPORT", "5. COMMAND & SIGNAL" };
            float[] ky = { 61f, 91f, 228f, 258f, 288f };
            for (int i = 0; i < 5; i++)
            {
                paraKey[i] = FrontKit.Mono(b, "Para" + i, FormL, ky[i], 124f, 14f, 10f, TextAlignmentOptions.MidlineLeft, true, 0.3f);
                paraKey[i].text = keys[i];
            }
            aimLine = FrontKit.Mono(b, "AimLine", FormV, 61f, vw, 14f, 10f, TextAlignmentOptions.MidlineLeft, true);
            aimNote = FrontKit.Mono(b, "AimNote", FormV, 74.5f, vw, 14f, 10f, TextAlignmentOptions.MidlineLeft);
            float bw = 98f;
            for (int i = 0; i < Missions; i++)
            {
                int slot = i;
                float ry = 105f + i * 24f;
                mission[i] = Button(b, "MISSION", FormL, ry, bw, 22f, AvButtonStyle.Default, () => Do(() => Draft(slot)), "");
                missionA[i] = FrontKit.Mono(b, "MissionA" + i, 126f, ry, 102f, 22f, 10f, TextAlignmentOptions.MidlineLeft, true);
                missionB[i] = FrontKit.Mono(b, "MissionB" + i, 230f, ry, w - 230f - 10f, 22f, 10f, TextAlignmentOptions.MidlineLeft);
            }
            teamLine = FrontKit.Mono(b, "TeamLine", FormV, 228f, vw, 14f, 10f, TextAlignmentOptions.MidlineLeft, true);
            quoteLine = FrontKit.Mono(b, "QuoteLine", FormV, 241.5f, vw, 14f, 10f, TextAlignmentOptions.MidlineLeft, true);
            costLine = FrontKit.Mono(b, "CostLine", FormV, 258f, vw, 14f, 10f, TextAlignmentOptions.MidlineLeft);
            quoteNote = FrontKit.Mono(b, "QuoteNote", FormV, 271.5f, vw, 14f, 10f, TextAlignmentOptions.MidlineLeft);
            authLeft = FrontKit.Mono(b, "AuthLeft", 150f, 288f, 80f, 14f, 10f, TextAlignmentOptions.MidlineLeft, true);
            authRight = FrontKit.Mono(b, "AuthRight", w - 14f - 100f, 288f, 100f, 14f, 10f, TextAlignmentOptions.MidlineRight);
            issue = Button(b, "ISSUE ORDER", FormL, 303f, tw - 6f, 28f, AvButtonStyle.Primary,
                () => Do(() => { if (lastView != null && lastView.CanIssue) actions.TeamMission(selectedSlot, (int)lastView.DraftMission, MissionAim(lastView.DraftMission)); }),
                "Sends the drafted mission once. Picking a mission or a target only drafts it.");
            issueNote = FrontKit.Mono(b, "IssueNote", FormL, 333f, tw - 6f, 14f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            // Movement: the toggles and the lift on the left, EXFIL apart on the right, on the dark board under the sheet.
            float[] ox = { 8f, 96f, 284f, 184f };
            float[] ow = { 84f, 84f, w - 292f, 84f };
            for (int i = 0; i < 4; i++)
            {
                int verb = i;
                order[i] = Button(b, OrderWord[i], ox[i], 355f, ow[i], 26f, i == 2 ? AvButtonStyle.Danger : AvButtonStyle.Default, () => Do(() => actions.TeamOrder(selectedSlot, verb)), OrderHelp[i]);
            }
            exfilNote = FrontKit.Mono(b, "ExfilNote", 10f, 384f, w - 20f, 14f, 10f, TextAlignmentOptions.MidlineLeft);
            skin.Add(() =>
            {
                foreach (TMP_Text t in paraKey) t.color = Paper.Pen;
                formTitle.color = Paper.Pen; formSub.color = Paper.PenDim; aimNote.color = Paper.PenDim; costLine.color = Paper.Pen; quoteNote.color = Paper.PenDim;
                authLeft.color = Paper.Pen; authRight.color = Paper.PenDim;
            });
        }

        /// <summary>Places a rect by its centre and tilts it (the stamp and the watermark turn about their middle).</summary>
        private static void Centered(RectTransform rt, float x, float y, float w, float h, float degrees)
        {
            AvLay.Place(rt, x, y, w, h);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
            rt.localRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        private AvControl Button(RectTransform p, string label, float x, float y, float w, float h, AvButtonStyle style, Action click, string help)
        {
            AvControl c = AvControl.Make(p, new AvControl.Spec(label, click, style));
            AvLay.Place(c.Rect, x, y, w, h);
            c.SingleLine();
            if (!string.IsNullOrEmpty(help)) c.Help = help;
            return c;
        }

        private void PaintComposer(ShadowRoomView s)
        {
            SofTeamView? sel = null;
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present && s.Teams[i].Slot == s.SelectedSlot) sel = s.Teams[i];
            composer.SetMeta(sel.HasValue ? sel.Value.Callsign + " " + sel.Value.Codename + " · " + sel.Value.State : "NO TEAM SELECTED", sel.HasValue ? Tone(sel.Value.Tone) : AvInk.Dim);
            float vw = composer.Width - FormV - 14f;
            bool drafted = s.DraftMission != MissionKind.None;
            SofMissionView dm = default;
            for (int i = 0; i < Missions; i++)
            {
                bool on = i < s.Missions.Count;
                mission[i].Rect.gameObject.SetActive(on);
                missionA[i].gameObject.SetActive(on);
                missionB[i].gameObject.SetActive(on);
                if (!on) continue;
                SofMissionView m = s.Missions[i];
                if (drafted && m.Kind == s.DraftMission) dm = m;
                missionKind[i] = m.Kind;
                mission[i].Label = m.Name;
                mission[i].Interactable = m.Pick;
                mission[i].Latched = m.Drafted;
                if (helpShown[i] != m.Note) { helpShown[i] = m.Note; if (!string.IsNullOrEmpty(m.Note)) mission[i].Help = m.Note; }
                FrontKit.Set(missionA[i], m.Cost + " · " + m.Odds, m.Allowed ? Paper.Pen : Paper.PenDim);
                FrontKit.Set(missionB[i], C2Kit.FitTo(missionB[i], m.Exploit ? "EXPLOIT · " + m.Note : m.Note, composer.Width - 230f - 10f), m.Allowed ? (m.Exploit ? Paper.Amber : Paper.PenDim) : Paper.Amber);
            }

            // Header: the operation name is the host's own (the same team, mission and target always give the same two words).
            string op = drafted ? FrontViews.OpName(sel.HasValue ? sel.Value.Slot : 0, s.DraftMission, s.SelectedTargetId) : "________";
            FrontKit.Set(formTitle, C2Kit.FitTo(formTitle, "FRAGMENTARY ORDER 03 · OP " + op, 286f), Paper.Pen);
            string to = sel.HasValue ? SofRules.Callsign(sel.Value.Slot) + " " + sel.Value.Codename : "________";
            FrontKit.Set(formSub, C2Kit.FitTo(formSub, "TO " + to + (operatorCall.Length > 0 ? " · FM " + operatorCall : ""), 286f), Paper.PenDim);
            eyesStamp.Set("EYES ONLY", Paper.Red);

            // 1 SITUATION: the aim and its grid.
            FrontKit.Set(aimLine, C2Kit.FitTo(aimLine, s.AimLine, vw), Paper.Of(s.AimTone));
            FrontKit.Set(aimNote, C2Kit.FitTo(aimNote, s.AimNote, vw), Paper.PenDim);
            // 3 EXECUTION: the team and the estimate.
            FrontKit.Set(teamLine, C2Kit.FitTo(teamLine, s.TeamLine, vw), Paper.Of(s.TeamTone));
            FrontKit.Set(quoteLine, C2Kit.FitTo(quoteLine, s.QuoteLine, vw), Paper.Of(s.QuoteTone));
            // 4 SERVICE SUPPORT: what the drafted mission costs and what the team has left (derived from the quote and the roster; no new reading).
            bool lowAmmo = sel.HasValue && sel.Value.Ammo < SofDesk.AmmoPerMission;
            string cost = "COST " + (drafted ? dm.Cost : "--") + " · AMMO " + (sel.HasValue ? sel.Value.Ammo + " %" : "--") + " · SPENDS " + SofDesk.AmmoPerMission.ToString("0", Inv) + " %";
            FrontKit.Set(costLine, C2Kit.FitTo(costLine, cost, vw), lowAmmo ? Paper.Red : Paper.Pen);
            FrontKit.Set(quoteNote, C2Kit.FitTo(quoteNote, s.QuoteNote, vw), Paper.PenDim);
            // 5 COMMAND AND SIGNAL: the authority line (signed once the order is ready) and ISSUE.
            FrontKit.Set(authLeft, C2Kit.FitTo(authLeft, "AUTH " + operatorCall, 80f), Paper.Pen);
            FrontKit.Set(authRight, "DTG " + (dtg.Length > 0 ? dtg : "--"), Paper.PenDim);
            signed = s.CanIssue;
            issue.Label = s.IssueLabel;
            issue.Interactable = s.CanIssue;
            FrontKit.Set(issueNote, C2Kit.FitTo(issueNote, s.IssueNote, composer.Width - FormL - 14f), Paper.Of(s.IssueTone));
            for (int i = 0; i < 4; i++)
            {
                order[i].Interactable = s.OrderOn[i];
                order[i].Latched = s.OrderLatched[i];
            }
            order[2].Label = s.ExfilLabel;
            FrontKit.Set(exfilNote, C2Kit.FitTo(exfilNote, s.ExfilNote, composer.Width - 20f), s.OrderOn[2] ? FrontKit.Tone(AvState.Danger) : AvInk.Dim);
            if (draftMark.gameObject.activeSelf == drafted) draftMark.gameObject.SetActive(!drafted);
            PaintForm();
        }

        /// <summary>The sheet itself: shadow, paper, binder holes, the header rule, a blank for the signature (a biro scrawl once the order can be issued).</summary>
        private void PaintForm()
        {
            FrontVector g = formG;
            g.Clear();
            float w = composer.Width, sw = w - SheetX * 2f, sh = SheetBottom - SheetY;
            g.Rect(SheetX + 3f, SheetY + 3f, sw, sh, Paper.Shadow);
            g.Rect(SheetX, SheetY, sw, sh, Paper.Form);
            g.Frame(SheetX, SheetY, sw, sh, 1f, Paper.FormEdge);
            for (int i = 0; i < 3; i++) Hand.Hole(g, SheetX + 7f, 78f + i * 108f, 3.2f);
            // A staple in the corner, and dark plates under the switches: the kit's translucent tints would wash out on paper.
            g.Line(new Vector2(SheetX + 3f, SheetY + 6f), new Vector2(SheetX + 12f, SheetY + 3f), 2.6f, RoomInk.Rgba(0f, 0f, 0f, 0.35f));
            g.Line(new Vector2(SheetX + 3f, SheetY + 5f), new Vector2(SheetX + 12f, SheetY + 2f), 2.2f, Paper.Steel);
            for (int i = 0; i < Missions; i++) g.Rect(FormL - 0.5f, 104.5f + i * 24f, 99f, 23f, Paper.Hole);
            g.Rect(FormL - 1f, 302f, w - FormL - 8f - 4f, 30f, Paper.Hole);
            g.Rect(FormL - 4f, 57f, w - FormL * 2f + 6f, 1.6f, Paper.Pen.WithAlpha(0.85f));
            g.Rect(FormL - 4f, 59.6f, w - FormL * 2f + 6f, 0.8f, Paper.Pen.WithAlpha(0.6f));
            // Faint dotted rules between the paragraphs (alpha under 0.2: a tint, never a ground under a line of text).
            foreach (float ry in FormRules) g.Dashed(new Vector2(FormL - 4f, ry), new Vector2(w - FormL + 2f, ry), 1f, Paper.Pen.WithAlpha(0.18f), 2f, 3f);
            // The blank: where the authority signs.
            float bx = 234f, bw = w - 14f - 100f - bx - 4f, by = 300f;
            g.Rect(bx, by, bw, 1f, Paper.Pen.WithAlpha(0.8f));
            if (signed)
            {
                Vector2 p = new Vector2(bx + 3f, by - 2f);
                for (int i = 1; i <= 16; i++)
                {
                    float t = i / 16f;
                    Vector2 q = new Vector2(bx + 3f + t * (bw - 8f), by - 5f - Mathf.Abs(Hand.Noise(41, i * 1.1f)) * 7f + (i % 2 == 0 ? 2f : 0f));
                    g.Line(p, q, 1.2f, Paper.Biro);
                    p = q;
                }
            }
            g.Flush();
        }

        // ---- Paint -----------------------------------------------------------------------------------------------------

        public void Paint(FrontRoomView v)
        {
            ShadowRoomView s = v.Shadow;
            lastView = s;
            selectedSlot = s.SelectedSlot;
            selectedTarget = s.SelectedTargetId;
            operatorCall = v.Callsign ?? "";
            dtg = v.Dtg ?? "";
            PaintSituation(v, s);
            PaintRoster(s);
            PaintTargets(s);
            PaintComposer(s);
        }

        private Vector2 P(float x, float y) => plate.P(x, y);

        /// <summary>RECON order target: the draft point's id when one is set, else the picked target.</summary>
        private int MissionAim(MissionKind kind) => kind == MissionKind.Recon && lastView != null && lastView.HasDraft ? lastView.DraftId : selectedTarget;

        public bool CancelMode() => false; // a draft lives on the view, Esc closes as before

        public bool Tick(Vector2 mouse)
        {
            bool changed = plate.Tick(mouse);
            // A left click on empty ground (not a drag, a marker or an overlay) drafts a RECON point; it counts on release, like the map's own clicks.
            if (FrontPointer.Down(0)) pressed = !changed && ToBox(mouse, out pressAt);
            else if (changed) pressed = false;
            else if (pressed && FrontPointer.Up(0))
            {
                pressed = false;
                if (ToBox(mouse, out Vector2 p) && (p - pressAt).sqrMagnitude <= ClickSlop * ClickSlop && OnGround(p)) { SetDraft(p); changed = true; }
            }
            return changed;
        }

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
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present && Near(p, s.Teams[i].X, s.Teams[i].Y)) return false;
            for (int i = 0; i < s.Targets.Count; i++) if (Near(p, s.Targets[i].X, s.Targets[i].Y)) return false;
            for (int i = 0; i < s.Camps.Count; i++) if (Near(p, s.Camps[i].X, s.Camps[i].Y)) return false;
            for (int i = 0; i < s.Held.Count; i++) if (Near(p, s.Held[i].X, s.Held[i].Y)) return false;
            for (int i = 0; i < s.Enemies.Count; i++) if (Near(p, s.Enemies[i].X, s.Enemies[i].Y)) return false;
            return !s.HasFob || !Near(p, s.FobX, s.FobY);
        }

        private void SetDraft(Vector2 p)
        {
            Vector2 uv = plate.View.UV(p), span = BoscaliSummer.Core.Game.TheaterFrame.Resolve();
            var frame = new MapFrame(span.x, span.y);
            float u = Mathf.Clamp01(uv.x), v = Mathf.Clamp01(uv.y);
            lastView.HasDraft = true; lastView.DraftU = u; lastView.DraftV = v; lastView.Reason = "";
            lastView.DraftId = FrontViews.PointId(frame.X(u), frame.Z(v));
            if (selectedTarget != 0) actions.SelectTarget(selectedTarget); // one aim at a time: the point replaces the picked target
            actions.Touch();
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
            if (s.HasFob) { sum += new Vector2(s.FobX, s.FobY); na++; }
            for (int i = 0; i < s.Camps.Count; i++) { sum += new Vector2(s.Camps[i].X, s.Camps[i].Y); na++; }
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present) { sum += new Vector2(s.Teams[i].X, s.Teams[i].Y); na++; }
            if (na > 0) mv.AutoCenter(sum / na);
            plate.Begin();
            FrontVector g = plate.G;
            FrontLabels labels = plate.Labels;
            Color own = Own, amber = AvInk.State(AvState.Caution), danger = AvInk.State(AvState.Danger), key = AvInk.Key, dim = AvInk.Dim, foe = AvInk.Hostile;
            Color grease = Paper.Grease;
            FrontMap.AirbaseLayer.Glyphs(g, v.Airbases, P);

            // Operation footprint: drawn round the FOB in marker pencil.
            if (s.HasFob)
            {
                Vector2 f = P(s.FobX, s.FobY);
                float r = s.FobRadius * mv.Scale;
                g.Disc(f, r, own.WithAlpha(0.05f), 64);
                Hand.Circle(g, f, r, 2.2f, own.WithAlpha(0.75f), 11);
                g.Ring(f, r * 0.55f, 1f, own.WithAlpha(0.3f), 56, 1f, 1f);
                g.Line(f + new Vector2(0f, 8f), f + new Vector2(0f, -14f), 1.6f, own);
                g.Tri(f + new Vector2(0f, -14f), f + new Vector2(10f, -10f), f + new Vector2(0f, -6f), own);
                labels.Take(new Rect(f.x - 12f, f.y - 16f, 24f, 26f));
            }

            // Camps: push-pins.
            for (int i = 0; i < MaxCamps; i++)
            {
                if (i >= s.Camps.Count) { campLabels[i].gameObject.SetActive(false); continue; }
                SofCampView c = s.Camps[i];
                Vector2 p = P(c.X, c.Y);
                Color k = c.Health == AnchorHealth.Live ? own : c.Health == AnchorHealth.Damaged ? amber : danger;
                Hand.Pin(g, p, 7f, k);
                if (c.Health == AnchorHealth.Down) FrontKit.Cross(g, p, 9f, danger);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
            }

            // Revealed targets: one hit region each (the list beside the map reaches every one, even when a cluster stacks them here),
            // each ringed twice in china marker (a resisted one in faded ink, an exploit one with an amber third ring).
            for (int i = 0; i < MaxTargets; i++) if (i >= s.Targets.Count) targetHits[i].gameObject.SetActive(false);
            for (int i = 0; i < s.Targets.Count; i++)
            {
                SofTargetView t = s.Targets[i];
                Vector2 p = P(t.X, t.Y);
                if (i < MaxTargets && !plate.Shown(p, 10f)) targetHits[i].gameObject.SetActive(false);
                else if (i < MaxTargets)
                {
                    targetHits[i].gameObject.SetActive(true);
                    hitTargets[i] = t.Id;
                    AvLay.Place(targetHits[i].rectTransform, p.x - 15f, p.y - 15f, 30f, 30f);
                }
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

            // The RECON point the operator clicked.
            if (s.HasDraft)
            {
                Vector2 p = P(s.DraftU, s.DraftV);
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
                Vector2 p = P(t.X, t.Y);
                Color k = Tone(t.Tone);
                g.Rect(p.x - 10f, p.y - 7f, 20f, 14f, AvInk.Ground.WithAlpha(0.9f));
                g.Frame(p.x - 10f, p.y - 7f, 20f, 14f, 1.8f, k);
                g.Line(p + new Vector2(-6f, -4f), p + new Vector2(6f, 4f), 1.6f, k);
                g.Line(p + new Vector2(-6f, 4f), p + new Vector2(6f, -4f), 1.6f, k);
                if (t.Helicopter) { g.Line(p + new Vector2(-8f, -11f), p + new Vector2(8f, -11f), 1.6f, k); g.Line(p + new Vector2(0f, -11f), p + new Vector2(0f, -7f), 1.4f, k); }
                if (t.Wounded) g.Disc(p + new Vector2(11f, -8f), 3f, danger, 8);
                if (t.Selected) FrontKit.Brackets(g, p, 17f, AvInk.Select);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
                Image hit = teamHits[i];
                hit.gameObject.SetActive(plate.Shown(p, 10f));
                hitSlots[i] = t.Slot;
                AvLay.Place(hit.rectTransform, p.x - 17f, p.y - 13f, 34f, 26f);
            }

            // Labels last, once every glyph has claimed its space.
            if (s.HasFob && plate.Shown(P(s.FobX, s.FobY), 12f))
            {
                fobLabel.gameObject.SetActive(true);
                FrontKit.Set(fobLabel, s.FobName, own);
                Tape(g, labels.Place(fobLabel, P(s.FobX, s.FobY), 12f, true), own);
            }
            else fobLabel.gameObject.SetActive(false);
            for (int i = 0; i < MaxTeams; i++)
            {
                if (i >= s.Teams.Count || !s.Teams[i].Present) continue;
                SofTeamView t = s.Teams[i];
                TMP_Text lt = teamLabels[i];
                if (!plate.Shown(P(t.X, t.Y), 12f)) { lt.gameObject.SetActive(false); continue; }
                lt.gameObject.SetActive(true);
                lt.text = C2Kit.Tint("<b>" + t.Callsign + "</b> " + t.Codename + " · " + t.State, Tone(t.Tone)) + "\n" + C2Kit.Tint("EXPOSURE " + t.Exposure + " %", ExposureInk(t.Exposure));
                lt.color = AvInk.Ink;
                Tape(g, labels.Place(lt, P(t.X, t.Y), 14f, false), Tone(t.Tone));
            }
            for (int i = 0; i < MaxCamps && i < s.Camps.Count; i++)
            {
                SofCampView c = s.Camps[i];
                TMP_Text lt = campLabels[i];
                if (!plate.Shown(P(c.X, c.Y), 12f)) { lt.gameObject.SetActive(false); continue; }
                lt.gameObject.SetActive(true);
                Color k = c.Health == AnchorHealth.Live ? own : c.Health == AnchorHealth.Damaged ? amber : danger;
                lt.text = C2Kit.Tint("<b>" + c.Name + "</b>", k) + "\n" + C2Kit.Tint(c.Health == AnchorHealth.Down ? "DOWN · RESTORE " + c.Rebuild + " %" : c.Health == AnchorHealth.Damaged ? "DAMAGED" : "LIVE", dim);
                lt.color = AvInk.Ink;
                Tape(g, labels.Place(lt, P(c.X, c.Y), 14f, false), k);
            }
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

            int up = 0;
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present) up++;
            if (s.HasFob) plate.Dot(s.FobX, s.FobY, own);
            for (int i = 0; i < s.Camps.Count; i++) plate.Dot(s.Camps[i].X, s.Camps[i].Y, own);
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present) plate.Dot(s.Teams[i].X, s.Teams[i].Y, Tone(s.Teams[i].Tone));
            for (int i = 0; i < s.Targets.Count; i++) plate.Dot(s.Targets[i].X, s.Targets[i].Y, foe);
            plate.End();

            situation.SetMeta(s.Active ? up + " TEAMS OUT · " + s.Targets.Count + " TARGETS REVEALED · " + s.Enemies.Count + " ENEMY TEAMS" : s.Meta, s.Enemies.Count > 0 ? FrontKit.Tone(AvState.Danger) : dim);
            side.Clear();
            PaintSide(side, s, up);
            side.Flush();
        }

        /// <summary>A paper note pinned to the map: shadow, manila, a thin edge and a grease-red pin in the top corner.</summary>
        private static void Note(FrontVector g, Rect r)
        {
            g.Rect(r.x + 3f, r.y + 3f, r.width, r.height, Paper.Shadow);
            g.Rect(r.x, r.y, r.width, r.height, Paper.Folder);
            g.Frame(r.x, r.y, r.width, r.height, 1f, Paper.FolderEdge);
            Hand.Pin(g, new Vector2(r.xMax - 13f, r.y + 11f), 4.6f, Paper.Grease);
        }

        private void PaintSide(FrontVector g, ShadowRoomView s, int up)
        {
            Note(g, OpCard);
            Note(g, HeldCard);
            Note(g, QrfCard);
            FrontKit.Set(qrfLine, C2Kit.FitTo(qrfLine, s.QrfLine, ColW - SweepW - 6f), s.QrfTone == AvState.Danger ? Paper.Red : Paper.PenDim);
            sweep.Label = s.SweepLabel;
            sweep.Interactable = s.CanSweep;
            FrontMapPlate.Card(g, KeyCard.x, KeyCard.y, KeyCard.width, KeyCard.height);
            FrontKit.Set(opName, s.HasFob ? s.FobName : "NO OPERATION FOOTPRINT", s.HasFob ? Paper.Olive : Paper.PenDim);
            FrontKit.Set(opNote, C2Kit.FitTo(opNote, s.FobNote, ColW), Paper.PenDim);
            FrontKit.Set(teamCount, up + " / " + s.TeamCap, up > 0 ? Paper.Olive : Paper.PenDim);
            int worst = 0;
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present) worst = Math.Max(worst, s.Teams[i].Exposure);
            FrontKit.Set(teamNote, up == 0 ? "NONE OUT" : "WORST EXPOSURE " + worst + " %", up == 0 ? Paper.PenDim : Paper.ExposureText(worst));
            for (int i = 0; i < MaxHeld; i++)
            {
                bool on = i < s.Held.Count;
                FrontKit.Set(heldName[i], on ? C2Kit.FitTo(heldName[i], s.Held[i].Name, 166f) : (i == 0 ? "NONE HELD" : ""), on ? Paper.Pen : Paper.PenDim);
                FrontKit.Set(heldTime[i], on ? FrontKit.Clock(s.Held[i].SecondsLeft) : "", Paper.Amber);
            }
            int[] kinds = new int[4];
            for (int i = 0; i < s.Targets.Count; i++) kinds[(int)s.Targets[i].Kind]++;
            for (int i = 0; i < 4; i++) FrontKit.Set(revVal[i], kinds[i].ToString(), kinds[i] > 0 ? Paper.Pen : Paper.PenDim);
            FrontKit.Set(revVal[4], s.Enemies.Count.ToString(), s.Enemies.Count > 0 ? Paper.Red : Paper.PenDim);
            FrontKit.Set(tapLine, C2Kit.FitTo(tapLine, s.TapLine, ColW), Paper.PenDim);

            // The key shows the map's own marks: infantry box, pencil route, hatched exposure, push-pin, double-ringed target, enemy diamond.
            Color[] ink = { Own, Own, AvInk.State(AvState.Caution), Own, AvInk.Hostile, AvInk.Hostile };
            for (int i = 0; i < 6; i++)
            {
                Vector2 p = new Vector2(KeyX + 12f + (i % 3) * 130f, KeyY + 26f + (i / 3) * 17f);
                switch (i)
                {
                    case 0: g.Frame(p.x - 8f, p.y - 5f, 16f, 10f, 1.6f, ink[i]); g.Line(p + new Vector2(-4f, -3f), p + new Vector2(4f, 3f), 1.2f, ink[i]); g.Line(p + new Vector2(-4f, 3f), p + new Vector2(4f, -3f), 1.2f, ink[i]); break;
                    case 1:
                        Hand.Stroke(g, p + new Vector2(-9f, 1f), p + new Vector2(8f, -1f), 2.2f, Paper.Grease, 5, 1.6f);
                        g.Line(p + new Vector2(8f, -1f), p + new Vector2(2f, -5f), 1.8f, Paper.Grease); g.Line(p + new Vector2(8f, -1f), p + new Vector2(2f, 3f), 1.8f, Paper.Grease); break;
                    case 2: Hand.Hatch(g, p, 7f, 3.2f, 1f, ink[i].WithAlpha(0.7f)); g.Ring(p, 7f, 1.4f, ink[i], 18); break;
                    case 3: Hand.Pin(g, p, 5.2f, ink[i]); break;
                    case 4:
                        Hand.Circle(g, p, 7.5f, 1.5f, Paper.Grease, 1); Hand.Circle(g, p, 5f, 1.2f, Paper.Grease, 3);
                        TargetGlyph(g, TargetKind.Building, p, 2.6f, ink[i]); break;
                    default: g.Polyline(new[] { p + new Vector2(0f, -7f), p + new Vector2(7f, 0f), p + new Vector2(0f, 7f), p + new Vector2(-7f, 0f) }, 1.4f, ink[i], true); break;
                }
            }
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
        /// One roster slot. A raised team is a personnel jacket: a folder tab in the state's colour (staggered by slot), an ID photograph with a head-and-shoulders
        /// silhouette, the callsign large with its codename, controller, mission and clock, exposure and ammo as gauges and the state as a rubber stamp; a free slot
        /// is one line or an empty jacket. The whole jacket selects the team.
        /// </summary>
        private sealed class Card
        {
            public const float TabH = 13f, BodyH = 64f, FullH = TabH + BodyH, EmptyH = 22f;
            private const float TabW = 46f, TabStep = 50f, InfoX = 62f;
            private readonly RectTransform root, full;
            private readonly AvFrame hitFrame;
            private readonly AvHit hit;
            private readonly FrontVector gfx;
            private readonly TMP_Text tabText, call, code, flags, control, mission, eta, expKey, expVal, ammoKey, ammoVal, caption, remark, empty;
            private readonly Stamp stamp;
            private readonly float x, w;
            private readonly int slot;
            private bool shown;
            private int mode = -1; // 1 = jacket, 0 = free slot line, 2 = free slot as an empty jacket (when the roster has room)

            public Card(RectTransform p, FrontSkin skin, int slot, float x, float w, Action click)
            {
                this.slot = slot; this.x = x; this.w = w;
                root = AvLay.Child(p, "TeamCard");
                AvLay.Place(root, x, 0f, w, FullH);
                hitFrame = FrontKit.Panel(root, "Hit", 0f, 0f, w, FullH);
                hitFrame.Paint(Color.clear, Color.clear);
                hit = AvHit.On(hitFrame);
                hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) click(); };
                gfx = FrontVector.Add(root, "Jacket");
                AvLay.Place(gfx.rectTransform, 0f, 0f, w, FullH);
                empty = FrontKit.Mono(root, "Empty", 12f, 0f, w - 24f, EmptyH, 10.5f, TextAlignmentOptions.MidlineLeft);
                empty.text = "SLOT " + (slot + 1) + " · EMPTY · TRAIN TEAM IN PROGRAMMES";
                empty.color = AvInk.Dim;
                full = AvLay.Child(root, "Full");
                AvLay.Place(full, 0f, 0f, w, FullH);
                float tabX = 10f + slot * TabStep;
                tabText = FrontKit.Mono(full, "TabText", tabX, 0f, TabW, TabH, 10f, TextAlignmentOptions.Midline, true, 0.5f);
                tabText.text = "TAB " + (slot + 1);
                remark = FrontKit.Mono(full, "Remark", 10f + 3 * TabStep + TabW + 8f, 0f, w - (10f + 3 * TabStep + TabW + 8f) - 8f, TabH, 10f, TextAlignmentOptions.MidlineRight);
                call = FrontKit.Cond(full, "Call", InfoX, TabH + 1f, 34f, 21f, 16f, TextAlignmentOptions.MidlineLeft);
                call.text = SofRules.Callsign(slot);
                code = FrontKit.Mono(full, "Code", InfoX + 33f, TabH + 4f, 56f, 15f, 11f, TextAlignmentOptions.MidlineLeft, true);
                code.text = FrontViews.TeamCodename(slot);
                flags = FrontKit.Mono(full, "Flags", InfoX + 92f, TabH + 4f, w - InfoX - 92f - 8f - 84f, 15f, 10f, TextAlignmentOptions.MidlineLeft);
                control = FrontKit.Mono(full, "Control", w - 8f - 84f, TabH + 4f, 84f, 15f, 11f, TextAlignmentOptions.MidlineRight, true);
                mission = FrontKit.Mono(full, "Mission", InfoX, TabH + 23f, 222f, 14f, 10.5f, TextAlignmentOptions.MidlineLeft);
                eta = FrontKit.Mono(full, "Eta", w - 8f - 82f, TabH + 23f, 82f, 14f, 10.5f, TextAlignmentOptions.MidlineRight);
                expKey = FrontKit.Mono(full, "ExpKey", InfoX, TabH + 36f, 54f, 14f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
                expVal = FrontKit.Mono(full, "ExpVal", 206f, TabH + 36f, 36f, 14f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                ammoKey = FrontKit.Mono(full, "AmmoKey", InfoX, TabH + 49.5f, 54f, 14f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
                ammoVal = FrontKit.Mono(full, "AmmoVal", 206f, TabH + 49.5f, 36f, 14f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                caption = FrontKit.Mono(full, "Caption", 3f, TabH + 47f, 56f, 14f, 10f, TextAlignmentOptions.Midline, true);
                stamp = new Stamp(full, w - 8f - 112f, TabH + 40.5f, 112f, 22f, -3f);
                expKey.text = "EXPOSURE"; ammoKey.text = "AMMO";
                root.gameObject.SetActive(false); // a slot the roster has no row for (TeamCap below 4) must not sit unplaced at the top: the first Paint shows it
            }

            private void Layout(int m, float y)
            {
                if (!shown) { root.gameObject.SetActive(true); shown = true; }
                bool isFull = m != 0;
                AvLay.Place(root, x, y, w, isFull ? FullH : EmptyH);
                if (m == mode) return;
                mode = m;
                full.gameObject.SetActive(isFull);
                empty.gameObject.SetActive(!isFull);
                hit.Interactable = m == 1;
                // A vacant jacket keeps the tab, the slot's own callsign and a note but no readings: no gauges, no controller, no clock.
                bool team = m == 1;
                flags.gameObject.SetActive(team); control.gameObject.SetActive(team); eta.gameObject.SetActive(team);
                expKey.gameObject.SetActive(m != 0); expVal.gameObject.SetActive(team); ammoKey.gameObject.SetActive(m != 0); ammoVal.gameObject.SetActive(team);
                caption.gameObject.SetActive(team); remark.gameObject.SetActive(team);
                if (m == 2) mission.text = "NOT RAISED · TRAIN TEAM IN PROGRAMMES";
            }

            /// <summary>Takes the slot out of the stack; returns the y the next card starts at.</summary>
            public float Hide(float y)
            {
                if (shown) { root.gameObject.SetActive(false); shown = false; }
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

            /// <summary>Paints the slot at <paramref name="y"/> and returns the y below it.</summary>
            public float Paint(in SofTeamView t, float y, bool vacantCard)
            {
                FrontVector g = gfx;
                g.Clear();
                float tabX = 10f + slot * TabStep;
                if (!t.Present)
                {
                    Layout(vacantCard ? 2 : 0, y);
                    Color ghost = Paper.Khaki.WithAlpha(0.55f);
                    if (!vacantCard)
                    {
                        Hand.DashRect(g, 0.5f, 0.5f, w - 1f, EmptyH - 1f, 1f, ghost);
                        g.Flush();
                        return y + EmptyH + 3f;
                    }
                    g.Rect(0f, TabH, w - 2f, BodyH, Paper.Khaki.WithAlpha(0.09f));   // a tint (alpha under 0.2), not a ground
                    Hand.DashRect(g, 0.5f, TabH + 0.5f, w - 2f, BodyH - 1f, 1f, ghost);
                    g.Line(new Vector2(tabX, TabH), new Vector2(tabX + 5f, 0.5f), 1f, ghost);
                    g.Dashed(new Vector2(tabX + 5f, 0.5f), new Vector2(tabX + TabW - 5f, 0.5f), 1f, ghost, 4f, 3f);
                    g.Line(new Vector2(tabX + TabW - 5f, 0.5f), new Vector2(tabX + TabW, TabH), 1f, ghost);
                    Hand.BustGhost(g, new Rect(11f, TabH + 5f, 42f, 38f), ghost);
                    Hand.DashRect(g, 118f, TabH + 39f, 84f, 7f, 1f, ghost, 3f, 2f);
                    Hand.DashRect(g, 118f, TabH + 52.5f, 84f, 7f, 1f, ghost, 3f, 2f);
                    g.Flush();
                    tabText.color = Paper.BoardInk; call.color = Paper.BoardInk; code.color = Paper.BoardInk; mission.color = Paper.BoardInk;
                    FrontKit.Set(expKey, "EXPOSURE", Paper.BoardInk);
                    FrontKit.Set(ammoKey, "AMMO", Paper.BoardInk);
                    mission.rectTransform.sizeDelta = new Vector2(w - InfoX - 8f, 14f);
                    stamp.Set("VACANT", Paper.BoardInk);
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
                g.Flush();

                tabText.color = Paper.TabInk(t.Tone);
                call.color = Paper.Pen; code.color = Paper.PenDim;
                FrontKit.Set(remark, Remark(t.Stamp), Paper.BoardInk);
                FrontKit.Set(caption, t.Helicopter ? "HELO" : "GROUND", Paper.PenDim);
                // Flags read "GROUND · PUSH · WIA": the first word is the caption under the photo, the rest sit beside the codename.
                int cut = t.Flags.IndexOf(" · ", StringComparison.Ordinal);
                FrontKit.Set(flags, cut > 0 ? C2Kit.FitTo(flags, t.Flags.Substring(cut + 3), w - InfoX - 92f - 8f - 84f) : "", Paper.PenDim);
                FrontKit.Set(control, C2Kit.FitTo(control, t.Control, 84f), t.Locked ? Paper.Amber : t.Control == "AUTO" ? Paper.PenDim : Paper.Olive);
                mission.rectTransform.sizeDelta = new Vector2(222f, 14f);
                FrontKit.Set(mission, C2Kit.FitTo(mission, t.Mission, 222f), Paper.Pen);
                FrontKit.Set(eta, C2Kit.FitTo(eta, t.Eta, 82f), Paper.PenDim);
                FrontKit.Set(expKey, "EXPOSURE", Paper.PenDim);
                FrontKit.Set(ammoKey, "AMMO", Paper.PenDim);
                FrontKit.Set(expVal, t.Exposure + " %", Paper.ExposureText(t.Exposure));
                FrontKit.Set(ammoVal, t.Ammo + " %", t.Ammo < SofDesk.AmmoPerMission ? Paper.Red : Paper.Pen);
                stamp.Set(t.Stamp, Paper.Of(t.Tone));
                return y + FullH + 4f;
            }
        }
    }
}

using System;
using System.Globalization;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Visuals;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The ORBIT room's DOWNLINK tab (spec 5.3): a sensor station around the live feed. A row of birds on station and the chosen bird's modes (EO / IR /
    /// LOW LIGHT, SAR, STRIKE); the picture with its burn-in (bird and mode, LIVE clock, frame and GSD, slant and elevation, look size and grid, sun and
    /// shadow, cloud), a crosshair, a north arrow with the shadow's fall, the zoom scale, an inset of the look inside the footprint, the host-revealed boxes
    /// (the lock red with a short track tail) and the packet bar with the link meter; under it the lock card (target, the held airbase it threatens, the
    /// reveal's age), one SEND TO TASKED, MARK SPOT and RELEASE, and the link. Every bird switch, new lock or new pick plays the acquire sequence, client
    /// only: a wide frame of the footprint with the look glowing, a slew into the look with motion streaks, then the picture resolves band by band from
    /// coarse blocks to the link's final block while PKT n / 256 runs. Wheel zooms 1..16 (eased), a left drag pans the look, a click on a box locks it.
    /// Two instances: one docked as the column tab, one enlarged over the map (ENLARGE / DOCK, Esc docks). Per-frame work stays in <see cref="DownlinkPane.Tick"/>.
    /// </summary>
    internal sealed partial class OrbitRoom
    {
        private static readonly AvIcon[] KindIcon = { AvIcon.Camera, AvIcon.RadarDish, AvIcon.Target };
        private static readonly DownlinkMode[] OpticalModes = { DownlinkMode.Eo, DownlinkMode.Ir, DownlinkMode.LowLight },
            RadarModes = { DownlinkMode.Sar }, KineticModes = { DownlinkMode.Strike };
        private static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        // The acquire sequence belongs to the room (the docked and the enlarged pane play the same one): when it began, and what the feed showed last frame.
        private float acqAt = -100f;
        private DownlinkMode seenMode;
        private int seenSel, seenPkt = SpaceFeedRules.ResolvePackets;
        private readonly Blocks coarse = new Blocks("BoscaliDownlinkCoarse"), fine = new Blocks("BoscaliDownlinkFine");

#if !PUBLIC_RELEASE
        /// <summary>TEST ONLY (the sim's downlink verb): every acquire step this many times longer, so a capture can land inside one.</summary>
        internal static float AcquireSlow = 1f;
        private static float Slow => Mathf.Clamp(AcquireSlow, 1f, 20f);
#else
        private const float Slow = 1f;
#endif

        /// <summary>Where the acquire sequence stands (0 WIDE, 1 SLEW, 2 RESOLVE, 3 done): the GUI check reads it.</summary>
        internal int AcquirePhase => last != null && last.Feed != null ? SpaceFeedRules.AcquirePhase(Time.unscaledTime - acqAt, last.Feed.Link, Slow, out _) : 3;

        /// <summary>The DOWNLINK picture on screen (the enlarged one first): its centre in screen pixels, for the GUI check's wheel and drag.</summary>
        internal bool DownlinkAt(out Vector2 at)
        {
            DownlinkPane p = enlarged.Visible ? enlarged : docked.Visible ? docked : null;
            at = p != null ? p.ScreenCentre : default;
            return p != null;
        }

        /// <summary>The window closed: the resolve's two small targets go back to the budget (made again on the next look).</summary>
        internal void ReleaseDownlink() { coarse.Release(); fine.Release(); }

        private static bool HasPicture(SpaceFeedView f) => f != null && f.Image != null && f.ImageKind != FeedImageKind.None && f.Refusal.Length == 0;

        /// <summary>
        /// Starts the acquire sequence when the feed changed under the pane: another bird, a new lock or a new pick (the feed restarted its resolve on its
        /// own) replays it from the wide frame; another mode of the same bird or a release only resolves again.
        /// </summary>
        private void WatchAcquire(SpaceFeedView f, float wall)
        {
            bool restarted = f.Packets < seenPkt;
            if (f.Mode != seenMode || f.SelectedId != seenSel || restarted)
                acqAt = SpaceFeedRules.AcquireFull(seenMode, f.Mode, seenSel, f.SelectedId, restarted) ? wall
                    : wall - (SpaceFeedRules.AcquireWide + SpaceFeedRules.AcquireSlew) * Slow;
            seenMode = f.Mode; seenSel = f.SelectedId; seenPkt = f.Packets;
        }

        /// <summary>A bird button: the map rings it too, and the feed switches to it (its own modes: EO for OPTICAL, SAR, STRIKE).</summary>
        private void PickBird(int kind)
        {
            Select(kind);
            Do(() => Feed.SetSource((BirdKind)kind));
        }

        /// <summary>The DOWNLINK look box on the map (spec 5.1): amber, where the feed points, with a glow so a 2 km look still reads on a 300 km theatre.</summary>
        private void DrawLook(FrontVector g, SpaceFeedView f, FrontMapView mv)
        {
            MapFrame fr = FrontController.Frame();
            Vector2 c = mv.P(f.LookU, f.LookV);
            float w = Mathf.Max(14f, f.LookKm * 1000f / fr.W * mv.Scale), h = Mathf.Max(9f, f.LookKm * 1000f / Mathf.Max(0.1f, f.ImageAspect) / fr.H * (mv.H / mv.VH));
            Color k = RoomInk.Telemetry;
            for (int i = 3; i >= 1; i--) g.Frame(c.x - w * 0.5f - i * 3f, c.y - h * 0.5f - i * 3f, w + i * 6f, h + i * 6f, 2.5f, k.WithAlpha(0.09f * (4 - i)));
            g.Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h, k.WithAlpha(0.14f));
            g.Frame(c.x - w * 0.5f, c.y - h * 0.5f, w, h, 1.8f, k);
            plate.Labels.Take(new Rect(c.x - w * 0.5f - 10f, c.y - h * 0.5f - 10f, w + 20f, h + 20f));
        }

        private static float TextW(string s, float size) => (s ?? "").Length * size * 0.6f;

        /// <summary>mission seconds as HH:MM:SS (MET); cosmetic read-out of the mission clock, capped at 99 hours.</summary>
        private static string Met(float seconds)
        {
            int t = Mathf.Clamp(Mathf.FloorToInt(seconds), 0, 99 * 3600 + 3599);
            return (t / 3600).ToString("00", CultureInfo.InvariantCulture) + ":" + (t / 60 % 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (t % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private static string Km(float km) => km < 10f ? km.ToString("0.0", CultureInfo.InvariantCulture) : Mathf.RoundToInt(km).ToString(CultureInfo.InvariantCulture);

        private static Color LinkInk(int link) => link >= 4 ? Mc.Go : link == 3 ? RoomInk.Telemetry : Mc.NoGo;

        /// <summary>The 8-point direction after "SHADOW " in the sun line, or -1 (sun down, unknown).</summary>
        private static int ShadowPoint(string sun)
        {
            int at = string.IsNullOrEmpty(sun) ? -1 : sun.IndexOf("SHADOW ", StringComparison.Ordinal);
            if (at < 0) return -1;
            string p = sun.Substring(at + 7).Trim();
            for (int i = 0; i < Points.Length; i++) if (p == Points[i]) return i;
            return -1;
        }

        /// <summary>The held airbase nearest a point (world metres) and how far it is.</summary>
        private static bool Nearest(FrontRoomView v, float x, float z, out string name, out float km)
        {
            name = ""; km = 0f;
            float best = float.MaxValue;
            MapFrame fr = FrontController.Frame();
            foreach (MapAirbaseView a in v.Airbases)
            {
                if (a.Side != AirbaseSide.Own) continue;
                float dx = fr.X(a.U) - x, dz = fr.Z(a.V) - z, d = dx * dx + dz * dz;
                if (d < best) { best = d; name = (a.Name ?? "").ToUpperInvariant(); }
            }
            if (best == float.MaxValue) return false;
            km = Mathf.Sqrt(best) / 1000f;
            return true;
        }

        /// <summary>One DOWNLINK pane (docked as the column tab, or enlarged over the map with its controls on a strip at the right).</summary>
        private sealed class DownlinkPane
        {
            private const float Bar = 22f, HitHalf = 18f, Strip = 300f, CardH = 194f, LinkH = 58f, WellX = 124f, BlitEvery = 0.1f, SendEvery = 0.05f,
                ZoomStep = 1.35f, ZoomEase = 0.08f;
            private const int Coarse = 16, Bands = 8;

            private readonly OrbitRoom room;
            public readonly FrontBox Box;
            private readonly float pw, ph, cardX, cardY, cardW;
            private readonly RectTransform pic, zoomer;
            private readonly Image back;
            private readonly RawImage wideMap, baseImg, fineImg, insetMap;
            private readonly FrontVector panel, standby, hud, fx;
            private readonly FeedBracket[] boxes = new FeedBracket[SpaceFeedView.MaxBrackets];
            private readonly AvControl[] birds = new AvControl[3], modes = new AvControl[3];
            private readonly DownlinkMode[] modeOf = new DownlinkMode[3];
            private readonly AvControl send, markSpot, release;
            private readonly TMP_Text zoomWord, refusal, acqWord, pktWord, linkWord, tl, tl2, tr, tr2, bl, bl2, br, br2, north, zTop, zBot, zCur, strikeWord,
                lockHead, lockAge, lockWhat, lockThreat, lockStare, sendNote, words, linkVal, linkNote;
            private readonly Rect inset;

            // What the layers show (Tick keeps it current every frame; Paint at the window's 5 Hz).
            private int phase = 3, shownPkt = -1, fxPkt = -1, shownLink = -1;
            private bool live, noBird, standbyDrawn, wasVisible, wideOk, zoomerFull = true;
            private float u0, v0, uw = 1f, vh = 1f, nextBlit, s0 = 1f;
            private Rect wideUv, zoomRect;
            private Vector2 wideLook, wideSize;
            private float wideRadius;
            private Texture mapTex;
            private string who = "";

            // Input: the eased wheel zoom and the drag pan, sent to the feed at most 20 times a second.
            private bool easing, dragging;
            private float zoomShown = 1f, zoomTarget = 1f, nextZoomSend, nextPanSend;
            private Vector2 dragLast, panAcc;

            // The lock: where its box sits, and its last positions on the ground (the STARE tail).
            private readonly Vector2[] tail = new Vector2[4];
            private int tailN, tailId;
            private bool lockShown;
            private Vector2 lockAt;
            private string grid = "";
            private float gridU = -1f, gridV = -1f;

            public DownlinkPane(OrbitRoom room, RectTransform parent, float w, float h, bool big)
            {
                this.room = room;
                Box = new FrontBox(parent, room.skin, big ? "DownlinkBig" : "Downlink", 0f, 0f, w, h, "DOWNLINK · SENSOR STATION", AvIcon.Camera, FrontLook.Console).Tagged("INCO");
                RectTransform b = Box.Rect;
                pw = big ? w - 24f - Strip : w - 16f;
                ph = big ? h - 36f : 460f;
                float px = 8f, py = big ? 28f : 88f, cx = big ? w - 8f - Strip : 8f, cw = big ? Strip : w - 16f;
                panel = FrontVector.Add(b, "Panel");
                AvLay.Place(panel.rectTransform, 0f, 0f, w, h);

                // Birds on station, the ENLARGE / DOCK switch, the chosen bird's modes.
                _ = big ? Tool(b, "DOCK", AvIcon.Minimize, cx, 28f, cw, 26f, room.ToggleCamera, "Dock the DOWNLINK back in the column (Esc does the same).")
                    : Tool(b, "ENLARGE", AvIcon.Maximize, w - 8f - 130f, 28f, 130f, 26f, room.ToggleCamera, "Give the DOWNLINK the whole room over the map. DOCK or Esc brings it back.");
                for (int k = 0; k < birds.Length; k++)
                {
                    int kind = k;
                    birds[k] = Tool(b, FrontViews.BirdCallsign(k) + " · " + KindTag[k], KindIcon[k], big ? cx : 8f + k * 154f, big ? 60f + k * 30f : 28f, big ? cw : 150f, 26f,
                        () => room.PickBird(kind), "");
                }
                float my = big ? 152f : 58f, mw = big ? (cw - 12f) / 3f : 88f;
                for (int i = 0; i < modes.Length; i++)
                {
                    int slot = i;
                    modes[i] = Tool(b, "EO", AvIcon.None, cx + i * (mw + 6f), my, mw, 24f, () => room.Do(() => room.Feed.SetMode(modeOf[slot])), "");
                }
                float zx = 8f + 3f * (mw + 6f) + 4f;
                zoomWord = big ? FrontKit.Mono(b, "ZoomWord", cx + 2f, 182f, cw - 4f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft)
                    : FrontKit.Mono(b, "ZoomWord", zx, 58f, w - 8f - zx, 24f, 10.5f, TextAlignmentOptions.MidlineRight);

                // The picture: ground, the wide frame's map, the zoomer (the coarse and the resolved layer), the test pattern, the inset, the burn-in layer,
                // the boxes, the per-frame effects, the words. It clips everything it holds (the window's hit test does not clip: see PaintBoxes).
                pic = AvLay.Child(b, "Picture");
                AvLay.Place(pic, px, py, pw, ph);
                pic.gameObject.AddComponent<RectMask2D>();
                back = AvLay.Solid(pic, "Back", Color.clear);
                AvLay.Fill(back.rectTransform);
                back.raycastTarget = true; // a miss never reaches the map behind
                wideMap = Raw(pic, "WideMap");
                AvLay.Fill(wideMap.rectTransform);
                zoomer = AvLay.Child(pic, "Zoomer");
                AvLay.Place(zoomer, 0f, 0f, pw, ph);
                baseImg = Raw(zoomer, "Base");
                AvLay.Fill(baseImg.rectTransform);
                fineImg = Raw(zoomer, "Resolved");
                AvLay.Fill(fineImg.rectTransform);
                standby = Vec(pic, "Standby", pw, ph);
                float iw = big ? 176f : 132f, ih = Mathf.Round(iw * 0.62f);
                inset = new Rect(pw - 10f - iw, ph - Bar - 46f - ih, iw, ih);
                insetMap = Raw(pic, "Inset");
                AvLay.Place(insetMap.rectTransform, inset.x, inset.y, inset.width, inset.height);
                hud = Vec(pic, "Hud", pw, ph);
                for (int i = 0; i < boxes.Length; i++) boxes[i] = new FeedBracket(pic, i, id => room.Do(() => room.Feed.SelectEntry(id)));
                fx = Vec(pic, "Fx", pw, ph);
                tl = Line(pic, "Tl", 10.5f, true); tl2 = Line(pic, "Tl2", 10f, false);
                tr = Line(pic, "Tr", 10.5f, true); tr2 = Line(pic, "Tr2", 10f, false);
                bl = Line(pic, "Bl", 10.5f, true); bl2 = Line(pic, "Bl2", 10f, false);
                br = Line(pic, "Br", 10.5f, true); br2 = Line(pic, "Br2", 10f, false);
                north = FrontKit.Mono(pic, "North", 23f, 51f, 14f, 14f, 10.5f, TextAlignmentOptions.Midline, true);
                north.text = "N";
                zTop = FrontKit.Mono(pic, "ZoomTop", 0f, 0f, 36f, 14f, 10f, TextAlignmentOptions.MidlineRight);
                zBot = FrontKit.Mono(pic, "ZoomBottom", 0f, 0f, 36f, 14f, 10f, TextAlignmentOptions.MidlineRight);
                zCur = FrontKit.Mono(pic, "ZoomNow", 0f, 0f, 44f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                acqWord = FrontKit.Mono(pic, "Acquire", pw * 0.5f - 200f, 48f, 400f, 18f, 11.5f, TextAlignmentOptions.Midline, true, 2f);
                strikeWord = FrontKit.Mono(pic, "Strike", 0f, 72f, 10f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
                refusal = FrontKit.Mono(pic, "Refusal", 20f, ph * 0.5f - 12f, pw - 40f, 24f, 11.5f, TextAlignmentOptions.Midline, true);
                pktWord = FrontKit.Mono(pic, "Packets", 8f, ph - Bar + 2f, WellX - 12f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
                linkWord = FrontKit.Mono(pic, "LinkWord", pw - 166f, ph - Bar + 2f, 160f, 18f, 10.5f, TextAlignmentOptions.MidlineRight);

                // The lock card and the link, under the picture (docked) or on the strip (enlarged).
                cardX = cx; cardY = big ? 206f : 556f; cardW = cw;
                float cy = cardY;
                lockHead = FrontKit.Mono(b, "LockHead", cx + 10f, cy + 4f, cw * 0.5f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
                lockAge = FrontKit.Mono(b, "LockAge", cx + cw * 0.4f, cy + 4f, cw * 0.6f - 10f, 18f, 10.5f, TextAlignmentOptions.MidlineRight);
                lockWhat = FrontKit.Mono(b, "LockWhat", cx + 10f, cy + 26f, cw - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                lockThreat = FrontKit.Mono(b, "LockThreat", cx + 10f, cy + 44f, cw - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                lockStare = FrontKit.Mono(b, "LockStare", cx + 10f, cy + 62f, cw - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                send = Tool(b, "SEND TO TASKED", AvIcon.Target, cx + 8f, cy + 84f, cw - 16f, 40f, () => room.Do(() => room.Feed.SendToTasked()), "");
                send.SetStyle(AvButtonStyle.Danger);
                sendNote = FrontKit.Mono(b, "SendNote", cx + 10f, cy + 126f, cw - 20f, 14f, 10f, TextAlignmentOptions.Midline);
                float hw = (cw - 24f) * 0.5f;
                markSpot = Tool(b, "MARK SPOT", AvIcon.MapPin, cx + 8f, cy + 144f, hw, 24f, () => room.Do(() => room.Feed.Confirm()),
                    "MARK the locked target without posting it: it stays on every pilot's map as a fixed point after its reveal lapses.");
                release = Tool(b, "RELEASE", AvIcon.LockOpen, cx + 16f + hw, cy + 144f, hw, 24f, () => room.Do(() => room.Feed.SelectEntry(0)), "Drop the lock: the camera stops following it.");
                words = FrontKit.Mono(b, "Words", cx + 10f, cy + 172f, cw - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                float ly = cy + CardH + 8f;
                TMP_Text linkHead = FrontKit.Mono(b, "LinkHead", cx + 10f, ly + 4f, cw * 0.3f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
                linkHead.text = "LINK";
                linkVal = FrontKit.Mono(b, "LinkVal", cx + cw * 0.25f, ly + 4f, cw * 0.75f - 10f, 18f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                linkNote = FrontKit.Mono(b, "LinkNote", cx + 10f, ly + 38f, cw - 20f, 16f, 10f, TextAlignmentOptions.MidlineLeft);
                TMP_Text hint = big ? FrontKit.Mono(b, "Hint", cx + 2f, ly + LinkH + 10f, cw - 4f, 48f, 10.5f, TextAlignmentOptions.TopLeft)
                    : FrontKit.Mono(b, "Hint", 8f, h - 30f, w - 16f, 16f, 10.5f, TextAlignmentOptions.Midline);
                hint.enableWordWrapping = big;
                hint.text = big ? "WHEEL ZOOM 1–16X · DRAG PANS INSIDE THE FOOTPRINT · CLICK A BOX LOCKS · DOCK OR ESC PUTS IT BACK IN THE COLUMN"
                    : "WHEEL ZOOM · DRAG PANS · CLICK A BOX LOCKS · ENLARGE FILLS THE ROOM";
                room.skin.Add(() =>
                {
                    back.color = Mc.PlateInk;
                    hint.color = Mc.Steel; linkHead.color = Own; north.color = RoomInk.Engrave;
                    DrawPanel(Mathf.Max(1, shownLink));
                });
            }

            public bool Visible => Box.Rect.gameObject.activeInHierarchy;

            /// <summary>The picture's centre in screen pixels (the in-game GUI check aims its wheel and drag there).</summary>
            public Vector2 ScreenCentre => RectTransformUtility.WorldToScreenPoint(null, pic.TransformPoint(pic.rect.center));

            private static AvControl Tool(RectTransform p, string label, AvIcon icon, float x, float y, float w, float h, Action click, string help)
            {
                AvControl c = AvControl.Make(p, new AvControl.Spec(label, click, AvButtonStyle.Default, icon));
                c.Accent = RoomInk.Ice;
                AvLay.Place(c.Rect, x, y, w, h);
                c.SingleLine();
                c.Help = help;
                return c;
            }

            private static RawImage Raw(RectTransform p, string name)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(p, false);
                RawImage r = go.AddComponent<RawImage>();
                r.raycastTarget = false;
                r.enabled = false;
                return r;
            }

            private static FrontVector Vec(RectTransform p, string name, float w, float h)
            {
                FrontVector v = FrontVector.Add(p, name);
                AvLay.Place(v.rectTransform, 0f, 0f, w, h);
                return v;
            }

            private static TMP_Text Line(RectTransform p, string name, float size, bool strong) =>
                FrontKit.Mono(p, name, 0f, 0f, 10f, 17f, size, TextAlignmentOptions.MidlineLeft, strong);

            private static void Show(Behaviour b, bool on) { if (b.enabled != on) b.enabled = on; }

            private static void Help(AvControl c, string s) { if (c.Help != s) c.Help = s; }

            private bool Local(Vector2 mouse, out Vector2 p)
            {
                p = default;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(pic, mouse, null, out Vector2 lp)) return false;
                p = new Vector2(lp.x - pic.rect.xMin, pic.rect.yMax - lp.y);
                return true;
            }

            private void Crop(SpaceFeedView f) => SpaceFeedRules.CoverCrop(pw / ph, f.ImageAspect, out u0, out v0, out uw, out vh);

            /// <summary>Picture pixels per metre on the ground (the look's width over the shown part of the picture).</summary>
            private float PxPerMetre(SpaceFeedView f) => pw / Mathf.Max(1f, uw * f.LookKm * 1000f);

            // ---- Per frame ------------------------------------------------------------------------------------------------

            /// <summary>One frame: input over the picture and the acquire sequence's layers. It repaints nothing else (the window paints at 5 Hz).</summary>
            public void Tick(Vector2 mouse, float wall)
            {
                if (!Visible) { wasVisible = false; dragging = false; easing = false; panAcc = Vector2.zero; return; }
                FrontRoomView v = room.last;
                SpaceFeedView f = v?.Feed;
                if (f == null) return;
                if (!wasVisible) { wasVisible = true; Paint(v); } // just shown: no stale frame for the rest of the window's paint period
                Input(f, mouse, wall);
                live = !noBird && HasPicture(f);
                Animate(f, wall, false);
            }

            private void Input(SpaceFeedView f, Vector2 mouse, float wall)
            {
                bool inside = Local(mouse, out Vector2 p) && p.x >= 0f && p.x <= pw && p.y >= 0f && p.y <= ph - Bar;
                ISpaceFeedActions feed = room.Feed;
                // Wheel: an eased zoom about the look centre.
                float wheel = FrontPointer.Wheel;
                if (inside && live && f.ZoomEnabled && Mathf.Abs(wheel) > 0.01f)
                {
                    if (!easing) zoomShown = zoomTarget = f.Zoom;
                    zoomTarget = SpaceFeedRules.ClampZoom(zoomTarget * Mathf.Pow(ZoomStep, wheel));
                    easing = true;
                    room.actions.Touch();
                }
                if (easing)
                {
                    zoomShown = Mathf.Lerp(zoomShown, zoomTarget, 1f - Mathf.Exp(-Time.unscaledDeltaTime / ZoomEase));
                    bool done = Mathf.Abs(zoomShown - zoomTarget) <= 0.005f * zoomTarget;
                    if (done) zoomShown = zoomTarget;
                    if (done || wall >= nextZoomSend) { feed?.SetZoom(zoomShown); nextZoomSend = wall + SendEvery; }
                    if (done) easing = false;
                }
                // Left drag: the look slides inside the imaged window (the feed clamps it; a STARE lock keeps it centred).
                if (FrontPointer.Down(0)) { dragging = inside && live; dragLast = p; }
                if (dragging && FrontPointer.Held(0)) { if (Local(mouse, out Vector2 q)) { panAcc += q - dragLast; dragLast = q; } }
                else dragging = false;
                if (panAcc.sqrMagnitude > 0.01f && (wall >= nextPanSend || !dragging))
                {
                    float mpp = uw * f.LookKm * 1000f / pw;
                    if (mpp > 0f && feed != null) { feed.Pan(-panAcc.x * mpp, panAcc.y * mpp); room.actions.Touch(); } // drag the ground: right moves the look west, down moves it north
                    panAcc = Vector2.zero;
                    nextPanSend = wall + SendEvery;
                }
            }

            /// <summary>
            /// The picture's layers for this frame: the coarse layer and the resolved band (point-filtered blocks of the live picture, re-blitted ten times a
            /// second), the zoomer's rectangle during the slew, the wide frame's map, the packet counter and the effects layer while the sequence runs.
            /// </summary>
            private void Animate(SpaceFeedView f, float wall, bool force)
            {
                Crop(f);
                int was = phase;
                float k = 1f;
                phase = live ? SpaceFeedRules.AcquirePhase(wall - room.acqAt, f.Link, Slow, out k) : 3;
                bool changed = phase != was;
                Texture src = live ? f.Image : null;
                bool blit = force || changed || wall >= nextBlit;
                if (blit) nextBlit = wall + BlitEvery;
                // IR and LOW LIGHT come out of the post in the link's blocks already; EO, SAR and STRIKE get them here.
                Texture end = src == null || src == f.PostTexture ? src : room.fine.Of(src, SpaceFeedRules.ResolveBands(f.Link), blit);
                Texture start = src != null && phase < 3 ? room.coarse.Of(src, Coarse, blit) : null;
                Rect crop = new Rect(u0, v0, uw, vh);
                Show(baseImg, src != null);
                if (src != null)
                {
                    Texture t = phase < 3 ? start : end;
                    if (baseImg.texture != t) baseImg.texture = t;
                    if (baseImg.uvRect != crop) baseImg.uvRect = crop;
                }
                // The resolve: whole bands from the top take the final look.
                float done = phase == 2 ? Mathf.Floor(k * Bands) / Bands : 0f;
                bool band = src != null && done > 0f;
                Show(fineImg, band);
                if (band)
                {
                    if (fineImg.texture != end) fineImg.texture = end;
                    RectTransform rt = fineImg.rectTransform;
                    rt.anchorMin = new Vector2(0f, 1f - done);
                    fineImg.uvRect = new Rect(u0, v0 + vh * (1f - done), uw, vh * done);
                }
                // The slew: the picture grows from the look box in the wide frame to the whole pane, exponentially (a zoom, not a slide).
                if (src != null && phase <= 1 && wideOk)
                {
                    float e = phase == 0 ? 0f : k * k * (3f - 2f * k), s = s0 * Mathf.Pow(1f / s0, e);
                    Vector2 c = Vector2.Lerp(new Vector2(wideLook.x * pw, wideLook.y * ph), new Vector2(pw * 0.5f, ph * 0.5f), e);
                    zoomRect = new Rect(c.x - pw * s * 0.5f, c.y - ph * s * 0.5f, pw * s, ph * s);
                    AvLay.Place(zoomer, zoomRect.x, zoomRect.y, zoomRect.width, zoomRect.height);
                    zoomerFull = false;
                }
                else if (!zoomerFull) { AvLay.Place(zoomer, 0f, 0f, pw, ph); zoomerFull = true; }
                bool wide = src != null && phase <= 1 && wideOk && mapTex != null;
                Show(wideMap, wide);
                if (wide)
                {
                    if (wideMap.texture != mapTex) wideMap.texture = mapTex;
                    wideMap.uvRect = wideUv;
                    wideMap.color = RoomInk.Rgba(0.62f, 0.78f, 1f, phase == 0 ? 1f : 1f - k);
                }
                if (changed)
                {
                    who = FrontViews.BirdCallsign((int)f.Source); // the sequence starts between paints, on the bird just picked
                    FrontKit.Set(acqWord, phase == 0 ? "ACQUIRE · " + who + " · WIDE" : phase == 1 ? "SLEW" : phase == 2 ? "RESOLVE · LINK " + f.Link + " / 5" : "", RoomInk.Telemetry);
                    if (room.last != null) PaintBoxes(f); // the boxes wait for the resolve
                }
                int pkt = !live || phase < 2 ? 0 : phase == 2 ? Mathf.RoundToInt(k * SpaceFeedRules.ResolvePackets) : f.Packets;
                if (pkt != shownPkt) { shownPkt = pkt; FrontKit.Set(pktWord, "PKT " + pkt + " / " + f.PacketsTotal, pkt >= f.PacketsTotal ? Mc.Go : RoomInk.Engrave); }
                if (phase < 3 || changed || pkt != fxPkt) DrawFx(f, k, pkt);
            }

            /// <summary>The effects layer: the packet bar's fill, and while the sequence runs the wide frame, the slew's streaks or the resolve's scan line.</summary>
            private void DrawFx(SpaceFeedView f, float k, int pkt)
            {
                fxPkt = pkt;
                fx.Clear();
                float y = ph - Bar, frac = Mathf.Clamp01(pkt / (float)Mathf.Max(1, f.PacketsTotal));
                if (frac > 0f) fx.Rect(WellX + 1f, y + 9f, (pw - WellX - 236f - 2f) * frac, 4f, frac >= 1f ? Mc.Go : Own);
                if (live && phase <= 1) Wide(phase == 0 ? 1f : 1f - k, k);
                if (live && phase == 1) Streaks(k);
                if (live && phase == 2)
                {
                    float ys = k * ph, yb = Mathf.Floor(k * Bands) / Bands * ph;
                    Color ice = RoomInk.Ice;
                    fx.Grad(0f, ys - 22f, pw, 22f, ice.WithAlpha(0f), ice.WithAlpha(0f), ice.WithAlpha(0.22f), ice.WithAlpha(0.22f));
                    fx.Rect(0f, ys - 1f, pw, 1.6f, ice.WithAlpha(0.9f));
                    if (yb > 0f) fx.Rect(0f, yb - 0.5f, pw, 1f, RoomInk.Engrave.WithAlpha(0.3f));
                }
                fx.Flush();
            }

            /// <summary>The wide frame: the bird's whole footprint over the map, its nadir, and the look box glowing where the feed will point.</summary>
            private void Wide(float a, float k)
            {
                if (mapTex == null)
                {
                    fx.Rect(0f, 0f, pw, ph, Mc.PlateInk.WithAlpha(0.92f * a));
                    for (float gx = 40f; gx < pw; gx += 40f) fx.Line(new Vector2(gx, 0f), new Vector2(gx, ph), 1f, Own.WithAlpha(0.1f * a));
                    for (float gy = 40f; gy < ph; gy += 40f) fx.Line(new Vector2(0f, gy), new Vector2(pw, gy), 1f, Own.WithAlpha(0.1f * a));
                }
                fx.Rect(0f, 0f, pw, ph, RoomInk.Plate.WithAlpha(0.22f * a));
                Vector2 c = new Vector2(pw * 0.5f, ph * 0.5f);
                float r = wideRadius * pw;
                fx.Disc(c, r, Own.WithAlpha(0.08f * a), 72);
                fx.Ring(c, r, 1.8f, Own.WithAlpha(0.9f * a), 96);
                fx.Ring(c, r + 7f, 1f, Own.WithAlpha(0.35f * a), 96, 1f, 1f);
                Sat(fx, c, Own.WithAlpha(a), true, 0.9f);
                Rect z = zoomRect;
                Vector2 lc = z.center;
                Color amber = RoomInk.Telemetry;
                fx.Line(new Vector2(0f, lc.y), new Vector2(pw, lc.y), 1f, amber.WithAlpha(0.35f * a));
                fx.Line(new Vector2(lc.x, 0f), new Vector2(lc.x, ph), 1f, amber.WithAlpha(0.35f * a));
                float pulse = 0.55f + 0.45f * Mathf.Sin(k * Mathf.PI * 3f);
                for (int i = 3; i >= 1; i--) fx.Frame(z.x - i * 3.5f, z.y - i * 3.5f, z.width + i * 7f, z.height + i * 7f, 3f, amber.WithAlpha(0.13f * (4 - i) * pulse * a));
                fx.Frame(z.x, z.y, z.width, z.height, 2f, amber.WithAlpha(a));
            }

            /// <summary>The slew's motion streaks: lines rushing out from the centre, fading as the picture arrives.</summary>
            private void Streaks(float k)
            {
                Vector2 c = new Vector2(pw * 0.5f, ph * 0.5f);
                float diag = Mathf.Sqrt(pw * pw + ph * ph) * 0.5f, a = 0.5f * (1f - k) + 0.08f;
                for (int i = 0; i < 32; i++)
                {
                    float ang = i * (2f * Mathf.PI / 32f) + (i * 0.618f % 1f) * 0.15f, jitter = (i * 7 % 5) / 5f;
                    Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    float r1 = diag * (0.1f + 0.75f * k + 0.08f * jitter), r2 = r1 + diag * (0.08f + 0.24f * k);
                    fx.Line(c + d * r1, c + d * r2, i % 3 == 0 ? 1.6f : 1f, RoomInk.Engrave.WithAlpha(a * (i % 2 == 0 ? 1f : 0.55f)));
                }
            }

            // ---- Paint (the window's refresh) ---------------------------------------------------------------------------

            public void Paint(FrontRoomView v)
            {
                SpaceFeedView f = v.Feed;
                if (f == null) return;
                // Birds on station. A feed whose bird is not up moves to one that is (a picture beats an empty panel).
                int firstUp = -1;
                bool sourceUp = false;
                for (int k = 0; k < birds.Length; k++)
                {
                    bool up = k < v.Fleet.Count && v.Fleet[k].Up;
                    if (up && firstUp < 0) firstUp = k;
                    sourceUp |= up && k == (int)f.Source;
                    OpsKit.Enable(birds[k], up);
                    birds[k].Latched = up && k == (int)f.Source;
                    string name = FrontViews.BirdCallsign(k);
                    Help(birds[k], up ? "Show " + name + "'s picture (" + C2Orbit.BirdName((BirdKind)k) + ")." : name + " is not on station: launch it on SATELLITES.");
                }
                noBird = firstUp < 0;
                if (!sourceUp && firstUp >= 0) room.Feed?.SetSource((BirdKind)firstUp);
                who = FrontViews.BirdCallsign((int)f.Source);

                DownlinkMode[] list = f.Source == BirdKind.Radar ? RadarModes : f.Source == BirdKind.Kinetic ? KineticModes : OpticalModes;
                for (int i = 0; i < modes.Length; i++)
                {
                    bool on = i < list.Length;
                    if (modes[i].Rect.gameObject.activeSelf != on) modes[i].Rect.gameObject.SetActive(on);
                    if (!on) continue;
                    modeOf[i] = list[i];
                    OpsKit.Label(modes[i], SpaceFeedRules.ModeWord(list[i]));
                    Help(modes[i], ModeHelp(list[i]));
                    modes[i].Latched = f.Mode == list[i];
                    OpsKit.Enable(modes[i], sourceUp);
                }

                if (f.ThreatActive) Box.SetMeta(C2Words.Fit(f.Threat, 58), FrontKit.Tone(AvState.Danger)); // a cockpit warning outranks the station line
                else Box.SetMeta(f.Status, f.StatusTone == AvState.Danger || f.StatusTone == AvState.Caution ? FrontKit.Tone(f.StatusTone) : AvInk.Dim);

                live = !noBird && HasPicture(f);
                string why = noBird ? "NO BIRD ON STATION · LAUNCH ONE ON SATELLITES" : live ? "" : f.Refusal.Length > 0 ? f.Refusal : "NO PICTURE YET · STAND BY";
                FrontKit.Set(refusal, why.Length > 0 ? FrontKit.Fit(refusal, why, 11.5f) : "", noBird ? Mc.Steel : FrontKit.Tone(AvState.Caution));
                DrawStandby(!live);
                Crop(f);
                float zoom = easing ? zoomShown : f.Zoom;
                bool stare = f.Source == BirdKind.Optical && f.Zoom > 1.01f && f.Brief.Has;
                string zw = !live ? "" : f.Source == BirdKind.Radar ? "SAR SCENE " + Km(f.LookKm) + " KM · FIXED"
                    : "ZOOM " + SpaceFeedRules.ZoomWord(zoom) + " · LOOK " + Km(f.LookKm) + " KM" + (stare ? " · STARE" : "");
                FrontKit.Set(zoomWord, FrontKit.Fit(zoomWord, zw, 10.5f), stare ? Mc.Go : Mc.Steel);

                // The footprint around the look: the wide frame (picture-sized) and the inset (its own size) share the mapping.
                wideOk = FootView(v, f, pw, ph, out wideUv, out wideLook, out wideSize, out wideRadius);
                s0 = Mathf.Clamp(wideSize.x, 0.12f, 1f);
                bool insetOk = FootView(v, f, inset.width, inset.height, out Rect iuv, out Vector2 il, out Vector2 isz, out float ir) && live;
                Show(insetMap, insetOk && mapTex != null);
                if (insetOk && mapTex != null)
                {
                    if (insetMap.texture != mapTex) insetMap.texture = mapTex;
                    insetMap.uvRect = iuv;
                    insetMap.color = RoomInk.Rgba(0.62f, 0.78f, 1f, 1f);
                }

                PaintLock(v, f);
                PaintBoxes(f);
                hud.Clear();
                PaintBurn(v, f);
                bool on2 = live;
                if (north.gameObject.activeSelf != on2) north.gameObject.SetActive(on2);
                if (live)
                {
                    Crosshair(f);
                    Compass(f);
                    ZoomScale(f, zoom);
                    LockMarks(f);
                }
                else { zTop.text = ""; zBot.text = ""; zCur.text = ""; }
                Strike(f);
                if (insetOk) Inset(il, isz, ir);
                PacketBar(v, f);
                hud.Flush();
                PaintLink(v, f);
                Animate(f, Time.unscaledTime, true);
            }

            private static string ModeHelp(DownlinkMode m) =>
                m == DownlinkMode.Eo ? "EO: the daylight picture. It refuses at night (use IR or LOW LIGHT)."
                : m == DownlinkMode.Ir ? "IR: white-hot, day and night. Contacts and impacts glow."
                : m == DownlinkMode.LowLight ? "LOW LIGHT: green phosphor with grain, for dusk and night."
                : m == DownlinkMode.Sar ? "SAR: the RADAR bird's strip from its last RECON PASS, through any weather."
                : "STRIKE: the KINETIC bird watches its rod's impact: the ellipse, the rod clock, and a hold on the hit.";

            /// <summary>
            /// The bird's footprint as a frame of <paramref name="w"/> x <paramref name="h"/> centred on its nadir (1.15 footprint radii across): the theatre
            /// picture's crop for it, the look's centre and size in that frame (0..1, y down) and the footprint radius as a share of the width.
            /// </summary>
            private bool FootView(FrontRoomView v, SpaceFeedView f, float w, float h, out Rect uv, out Vector2 look, out Vector2 size, out float radius)
            {
                uv = default; look = size = default; radius = 0f;
                if (!f.HasLook || !room.TryBird((int)f.Source, out OrbitTrackView b) || !b.Alive || b.Radius <= 0f) return false;
                MapFrame fr = FrontController.Frame();
                float hwM = b.Radius * fr.W * 1.15f, hhM = hwM * h / Mathf.Max(1f, w), hwU = hwM / fr.W, hhV = hhM / fr.H;
                look = new Vector2(Mathf.Clamp01(0.5f + (f.LookU - b.U) / (2f * hwU)), Mathf.Clamp01(0.5f + (f.LookV - b.V) / (2f * hhV)));
                float lw = f.LookKm * 1000f;
                size = new Vector2(lw / (2f * hwM), lw / Mathf.Max(0.1f, f.ImageAspect) / (2f * hhM));
                radius = 0.5f / 1.15f;
                mapTex = FrontMap.Source(out Texture tex, out Rect src) ? tex : null;
                uv = new Rect(src.x + (b.U - hwU) * src.width, src.y + (1f - (b.V + hhV)) * src.height, 2f * hwU * src.width, 2f * hhV * src.height);
                return true;
            }

            /// <summary>
            /// The host-revealed boxes once the picture resolves. A box whose 36 px click area would reach past the picture (the toolbar above, the strip to the
            /// right) is not shown: the window's hit test does not clip.
            /// </summary>
            private void PaintBoxes(SpaceFeedView f)
            {
                lockShown = false;
                int shown = 0;
                if (live && phase >= 2)
                    for (int i = 0; i < f.Brackets.Count && shown < boxes.Length; i++)
                    {
                        FeedBracketView b = f.Brackets[i];
                        if (!SpaceFeedRules.CropPoint(b.U, b.V, u0, v0, uw, vh, out float ax, out float ay)) continue;
                        float x = ax * pw, y = (1f - ay) * ph;
                        if (x < HitHalf || x > pw - HitHalf || y < HitHalf || y > ph - Bar - 8f) continue;
                        boxes[shown++].Paint(b, x, y, pw);
                        if (b.Selected) { lockShown = true; lockAt = new Vector2(x, y); }
                    }
                for (int i = shown; i < boxes.Length; i++) boxes[i].Hide();
            }

            /// <summary>One burn-in line on its dark plate; returns the plate's left edge.</summary>
            private float Burn(TMP_Text t, string s, Color ink, float x, float y, float size, bool right)
            {
                if (string.IsNullOrEmpty(s)) { if (t.text.Length > 0) t.text = ""; return x; }
                float w = TextW(s, size) + 6f, plate = w + 10f, px = right ? x - plate : x;
                hud.Rect(px, y, plate, 17f, RoomInk.Plate.WithAlpha(0.74f));
                hud.Rect(right ? px + plate - 2f : px, y, 2f, 17f, ink.WithAlpha(0.85f));
                FrontKit.Set(t, s, ink);
                AvLay.Place(t.rectTransform, px + 5f, y, w, 17f);
                return px;
            }

            private void PaintBurn(FrontRoomView v, SpaceFeedView f)
            {
                Color ink = RoomInk.Engrave, dim = Mc.Steel;
                string mode = f.Mode == DownlinkMode.Ir ? "IR WHITE-HOT" : f.Mode == DownlinkMode.Sar ? "SAR STRIP" : f.Mode == DownlinkMode.Strike ? "STRIKE VIEW" : SpaceFeedRules.ModeWord(f.Mode);
                Burn(tl, who + " / " + C2Orbit.BirdName(f.Source) + " / " + mode, ink, 8f, 6f, 10.5f, false);
                string t1 = f.Telemetry[1], t2 = f.Telemetry[2];
                Burn(tl2, !live ? "NO FRAME · STANDBY" : t1.Length > 0 && t2.Length > 0 ? t1 + " · " + t2 : t1 + t2, dim, 8f, 25f, 10f, false);
                string stamp = "T+" + Met(v.Now);
                float lx = Burn(tr, live ? "LIVE " + stamp : "STBY " + stamp, live ? Mc.Go : RoomInk.Telemetry, pw - 8f, 6f, 10.5f, true);
                if (live) hud.Disc(new Vector2(lx - 8f, 14.5f), 4f, Mc.Go, 14);
                else hud.Ring(new Vector2(lx - 8f, 14.5f), 3.6f, 1.4f, RoomInk.Telemetry, 14);
                int frame = Mathf.FloorToInt(Mathf.Max(0f, v.Now) * 8f) % 1000000;
                Burn(tr2, live ? "FRM " + frame.ToString("000000", CultureInfo.InvariantCulture) + (f.Telemetry[0].Length > 0 ? " · " + f.Telemetry[0] : "") : "", dim, pw - 8f, 25f, 10f, true);
                if (!live) { Burn(bl, "", ink, 0f, 0f, 10f, false); Burn(bl2, "", ink, 0f, 0f, 10f, false); Burn(br, "", ink, 0f, 0f, 10f, false); Burn(br2, "", ink, 0f, 0f, 10f, false); return; }
                if (Mathf.Abs(f.LookU - gridU) > 1e-4f || Mathf.Abs(f.LookV - gridV) > 1e-4f)
                {
                    gridU = f.LookU; gridV = f.LookV;
                    MapFrame fr = FrontController.Frame();
                    grid = OpsKit.Grid(fr.X(f.LookU), fr.Z(f.LookV));
                }
                float yb = ph - Bar - 40f;
                Burn(bl, "LOOK " + Km(f.LookKm) + " × " + Km(f.LookKm / Mathf.Max(0.1f, f.ImageAspect)) + " KM", ink, 8f, yb, 10.5f, false);
                Burn(bl2, "GRID " + grid, dim, 8f, yb + 18f, 10f, false);
                Burn(br, f.Telemetry[3], ink, pw - 8f, yb, 10.5f, true);
                Burn(br2, f.Telemetry[4], dim, pw - 8f, yb + 18f, 10f, true);
            }

            private void Crosshair(SpaceFeedView f)
            {
                Vector2 c = new Vector2(pw * 0.5f, ph * 0.5f);
                Color ink = RoomInk.Engrave.WithAlpha(0.9f), dark = RoomInk.Plate.WithAlpha(0.6f);
                for (int i = 0; i < 4; i++)
                {
                    Vector2 d = i == 0 ? Vector2.right : i == 1 ? Vector2.left : i == 2 ? Vector2.up : Vector2.down, n = new Vector2(-d.y, d.x);
                    hud.Line(c + d * 16f, c + d * 50f, 3.4f, dark);
                    hud.Line(c + d * 16f, c + d * 50f, 1.3f, ink);
                    hud.Line(c + d * 33f - n * 4f, c + d * 33f + n * 4f, 1.2f, ink);
                }
                Color mark = f.SelectedId > 0 ? Mc.NoGo : RoomInk.Ice;
                hud.Diamond(c, 8f, dark);
                hud.Diamond(c, 6f, mark);
                hud.Diamond(c, 4f, dark);
            }

            /// <summary>The north arrow (the picture is north-up: the imager's up is world north) and, in amber, the way shadows fall.</summary>
            private void Compass(SpaceFeedView f)
            {
                Vector2 c = new Vector2(30f, 88f);
                const float r = 20f;
                hud.Disc(c, r + 2f, RoomInk.Plate.WithAlpha(0.6f), 32);
                hud.Ring(c, r, 1.2f, RoomInk.Engrave.WithAlpha(0.7f), 40);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 45f * Mathf.Deg2Rad;
                    Vector2 d = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
                    hud.Line(c + d * (r - (i % 2 == 0 ? 5f : 3f)), c + d * r, i % 2 == 0 ? 1.6f : 1f, RoomInk.Engrave.WithAlpha(0.8f));
                }
                hud.Tri(c + new Vector2(0f, -r + 3f), c + new Vector2(4.5f, 0f), c + new Vector2(-4.5f, 0f), Mc.NoGo);
                hud.Tri(c + new Vector2(0f, r - 3f), c + new Vector2(4.5f, 0f), c + new Vector2(-4.5f, 0f), RoomInk.Engrave.WithAlpha(0.45f));
                int sh = ShadowPoint(f.Telemetry[3]);
                if (sh < 0) return;
                float sa = sh * 45f * Mathf.Deg2Rad;
                Vector2 sd = new Vector2(Mathf.Sin(sa), -Mathf.Cos(sa)), sn = new Vector2(-sd.y, sd.x), tip = c + sd * (r - 4f);
                hud.Line(c, tip - sd * 4f, 2f, RoomInk.Telemetry);
                hud.Tri(tip, tip - sd * 6f + sn * 3.5f, tip - sd * 6f - sn * 3.5f, RoomInk.Telemetry);
            }

            /// <summary>The zoom scale up the right edge: 1X at the foot, 16X at the top (log steps), the marker at the zoom now.</summary>
            private void ZoomScale(SpaceFeedView f, float zoom)
            {
                float x = pw - 22f, top = 60f, bottom = inset.y - 22f;
                if (!f.ZoomEnabled || bottom - top < 40f) { zTop.text = ""; zBot.text = ""; zCur.text = ""; return; }
                Color ice = RoomInk.Ice;
                hud.Rect(x, top, 6f, bottom - top, RoomInk.Plate.WithAlpha(0.6f));
                hud.Frame(x, top, 6f, bottom - top, 1f, ice.WithAlpha(0.8f));
                for (float z = 1f; z <= SpaceFeedRules.MaxZoom; z *= 2f)
                {
                    float y = bottom - Mathf.Log(z, 2f) / 4f * (bottom - top);
                    hud.Line(new Vector2(x - 5f, y), new Vector2(x, y), 1.2f, ice.WithAlpha(0.8f));
                }
                float zy = bottom - Mathf.Log(SpaceFeedRules.ClampZoom(zoom), 2f) / 4f * (bottom - top);
                hud.Rect(x - 1f, zy - 2f, 8f, 4f, ice);
                hud.Tri(new Vector2(x - 3f, zy), new Vector2(x - 11f, zy - 5f), new Vector2(x - 11f, zy + 5f), ice);
                hud.Rect(x - 62f, zy - 8f, 48f, 16f, RoomInk.Plate.WithAlpha(0.74f));
                AvLay.Place(zTop.rectTransform, x - 40f, top - 7f, 36f, 14f);
                AvLay.Place(zBot.rectTransform, x - 40f, bottom - 7f, 36f, 14f);
                AvLay.Place(zCur.rectTransform, x - 62f, zy - 8f, 46f, 16f);
                bool nearTop = zy - top < 16f, nearBottom = bottom - zy < 16f;
                FrontKit.Set(zTop, nearTop ? "" : "16X", Mc.Steel);
                FrontKit.Set(zBot, nearBottom ? "" : "1X", Mc.Steel);
                FrontKit.Set(zCur, SpaceFeedRules.ZoomWord(zoom), ice);
            }

            /// <summary>The lock's red glow and, under STARE, its last positions on the ground drawn relative to the box (the camera keeps the box centred).</summary>
            private void LockMarks(SpaceFeedView f)
            {
                if (!lockShown) return;
                Color red = Mc.NoGo;
                hud.Frame(lockAt.x - 16f, lockAt.y - 16f, 32f, 32f, 2f, red.WithAlpha(0.35f));
                hud.Frame(lockAt.x - 21f, lockAt.y - 21f, 42f, 42f, 1.4f, red.WithAlpha(0.15f));
                if (tailN < 2) return;
                float ppm = PxPerMetre(f);
                Vector2 now = tail[tailN - 1], prev = lockAt;
                for (int i = tailN - 2; i >= 0; i--)
                {
                    Vector2 d = tail[i] - now, p = lockAt + new Vector2(d.x, -d.y) * ppm;
                    if ((p - lockAt).sqrMagnitude < 4f) continue; // a stationary target leaves no tail
                    hud.Dashed(prev, p, 1.2f, red.WithAlpha(0.45f), 3f, 3f);
                    hud.Disc(p, 2.8f, red.WithAlpha(0.25f + 0.45f * (i + 1) / (tailN - 1)), 10);
                    prev = p;
                }
            }

            /// <summary>The STRIKE view: the rod's impact ellipse (dashed red), the rod clock, and HOLD · IMPACT after it lands.</summary>
            private void Strike(SpaceFeedView f)
            {
                if (!live || f.Mode != DownlinkMode.Strike) { if (strikeWord.text.Length > 0) strikeWord.text = ""; return; }
                FeedStrikeView s = f.Strike;
                Color red = Mc.NoGo;
                string word = !s.Has ? "NO ROD IN FLIGHT · WATCHING THE KINETIC NADIR" : s.Held ? "HOLD · IMPACT" : "ROD INBOUND · IMPACT IN " + FrontKit.Clock(s.Eta);
                if (s.Has && s.OnPicture && SpaceFeedRules.CropPoint(s.U, s.V, u0, v0, uw, vh, out float ax, out float ay))
                {
                    Vector2 c = new Vector2(ax * pw, (1f - ay) * ph);
                    float r = Mathf.Max(12f, s.Radius * PxPerMetre(f));
                    if (s.Held) EllDisc(hud, c, r, r * 0.82f, red.WithAlpha(0.14f));
                    EllRing(hud, c, r, r * 0.82f, 1.8f, red, 2);
                    EllRing(hud, c, r * 1.25f, r * 1.03f, 1f, red.WithAlpha(0.4f), 3);
                    FrontKit.Cross(hud, c, 5f, red);
                }
                else if (s.Has) word += " · OFF PICTURE";
                float tw = TextW(word, 11.5f) + 6f, x = pw * 0.5f - tw * 0.5f;
                hud.Rect(x - 34f, 70f, tw + 42f, 22f, RoomInk.Plate.WithAlpha(0.8f));
                hud.Frame(x - 34f, 70f, tw + 42f, 22f, 1f, (s.Has ? red : Mc.Steel).WithAlpha(0.7f));
                // The rod clock: a ring with a hand sweeping each second until impact.
                Vector2 rc = new Vector2(x - 18f, 81f);
                hud.Ring(rc, 8f, 1.3f, s.Has ? red : Mc.Steel, 28);
                float ha = (s.Has && !s.Held ? s.Eta % 1f : 0f) * 2f * Mathf.PI;
                hud.Line(rc, rc + new Vector2(Mathf.Sin(ha), -Mathf.Cos(ha)) * 6f, 1.6f, s.Has ? red : Mc.Steel);
                AvLay.Place(strikeWord.rectTransform, x, 72f, tw, 18f);
                FrontKit.Set(strikeWord, word, s.Has ? red : Mc.Steel);
            }

            /// <summary>The inset: the footprint around the bird's nadir and the look box inside it, so the operator never loses where the feed points.</summary>
            private void Inset(Vector2 look, Vector2 size, float radius)
            {
                Rect r = inset;
                if (mapTex == null) hud.Rect(r.x, r.y, r.width, r.height, Mc.PlateInk.WithAlpha(0.92f));
                hud.Rect(r.x, r.y, r.width, r.height, RoomInk.Plate.WithAlpha(0.25f));
                hud.Frame(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f, 1.2f, RoomInk.Ice.WithAlpha(0.85f));
                Vector2 c = r.center;
                hud.Ring(c, radius * r.width, 1.2f, Own.WithAlpha(0.9f), 48);
                hud.Disc(c, 1.8f, Own, 8);
                Vector2 lp = new Vector2(r.x + look.x * r.width, r.y + look.y * r.height);
                float lw = Mathf.Max(6f, size.x * r.width), lh = Mathf.Max(4f, size.y * r.height);
                Color amber = RoomInk.Telemetry;
                hud.Line(new Vector2(r.x, lp.y), new Vector2(r.xMax, lp.y), 1f, amber.WithAlpha(0.35f));
                hud.Line(new Vector2(lp.x, r.y), new Vector2(lp.x, r.yMax), 1f, amber.WithAlpha(0.35f));
                hud.Frame(lp.x - lw * 0.5f, lp.y - lh * 0.5f, lw, lh, 1.5f, amber);
            }

            private void PacketBar(FrontRoomView v, SpaceFeedView f)
            {
                float y = ph - Bar, ww = pw - WellX - 236f, sx = pw - 228f;
                hud.Rect(0f, y, pw, Bar, RoomInk.Plate.WithAlpha(0.9f));
                hud.Rect(0f, y, pw, 1f, RoomInk.Bezel.WithAlpha(0.8f));
                hud.Rect(WellX, y + 8f, ww, 6f, Mc.PlateInk);
                hud.Frame(WellX, y + 8f, ww, 6f, 1f, RoomInk.Bezel.WithAlpha(0.6f));
                for (int i = 0; i < 5; i++)
                {
                    hud.Rect(sx + i * 12f, y + 7f, 10f, 8f, i < f.Link ? LinkInk(f.Link) : Mc.PlateInk);
                    hud.Frame(sx + i * 12f, y + 7f, 10f, 8f, 1f, RoomInk.Bezel.WithAlpha(0.6f));
                }
                FrontKit.Set(linkWord, "LINK " + f.Link + "/5 · PADS " + v.PadsUp + "/" + FrontRules.PadCap, LinkInk(f.Link));
            }

            private void PaintLock(FrontRoomView v, SpaceFeedView f)
            {
                FeedBriefView b = f.Brief;
                Color red = Mc.NoGo, steel = Mc.Steel;
                string what, threat = "", stare = "", age = "";
                Color whatInk = RoomInk.Engrave, stareInk = steel;
                if (b.Has)
                {
                    Vector2 at = new Vector2(b.X, b.Z);
                    if (b.Id != tailId) { tailId = b.Id; tailN = 0; }
                    if (tailN == 0 || (at - tail[tailN - 1]).sqrMagnitude > 4f)
                    {
                        if (tailN == tail.Length) { Array.Copy(tail, 1, tail, 0, tail.Length - 1); tailN--; }
                        tail[tailN++] = at;
                    }
                    FrontKit.Set(lockHead, (b.FixedPoint ? "MARK · " : "LOCK · ") + C2Orbit.TrackId(b.Id), b.FixedPoint ? RoomInk.Telemetry : red);
                    age = b.FixedPoint ? "HELD " + FrontKit.Clock(Mathf.Max(0f, b.Left)) + " MORE" : "REVEALED " + FrontKit.Clock(Mathf.Max(0f, SpaceContacts.RevealSeconds - b.Left)) + " AGO";
                    what = b.Title + (b.Percent > 0 && b.Percent <= 100 ? " " + b.Percent + " %" : "") + (b.Moving ? " · MOVING" : "") + (b.Marked ? " · MARKED" : "") + " · GRID " + b.Grid;
                    whatInk = b.FixedPoint ? RoomInk.Engrave : FeedInk.Class(b.Class);
                    threat = Nearest(v, b.X, b.Z, out string name, out float km)
                        ? (b.Class == ProbableClass.Friendly ? "NEAR " : "THREATENS ") + name + " · " + Km(km) + " KM" : "NO HELD AIRBASE ON THE MAP";
                    if (f.Source == BirdKind.Optical) { stare = f.Zoom > 1.01f ? "STARE · THE CAMERA FOLLOWS IT" : "WHEEL IN TO STARE: THE CAMERA FOLLOWS A LOCK"; stareInk = f.Zoom > 1.01f ? Mc.Go : steel; }
                }
                else
                {
                    tailN = 0; tailId = 0;
                    FrontKit.Set(lockHead, "NO LOCK", steel);
                    what = f.NoContacts.Length > 0 ? f.NoContacts : !live ? "" : f.Brackets.Count > 0 ? "CLICK A BOX ON THE PICTURE TO LOCK IT" : "NO BOX IN VIEW · ONLY HOST-REVEALED CONTACTS GET ONE";
                    whatInk = steel;
                    if (f.SendCount > 0) stare = f.SendCount + (f.SendCount == 1 ? " MARK HELD" : " MARKS HELD") + " · SEND TO TASKED POSTS " + (f.SendCount == 1 ? "IT" : "THEM");
                }
                FrontKit.Set(lockAge, age, steel);
                FrontKit.Set(lockWhat, FrontKit.Fit(lockWhat, what, 10.5f), whatInk);
                FrontKit.Set(lockThreat, FrontKit.Fit(lockThreat, threat, 10.5f), RoomInk.Telemetry);
                FrontKit.Set(lockStare, FrontKit.Fit(lockStare, stare, 10.5f), stareInk);
                bool chain = b.Has && !b.Marked && !b.FixedPoint;
                OpsKit.Enable(send, f.CanConfirm || f.CanSend);
                Help(send, chain ? "One press: MARK the lock, then post it to TASKED as a strike any pilot can claim." : f.CanSend ? f.SendHelp : f.ConfirmHelp);
                string note = chain ? "ONE PRESS: MARK IT, THEN A STRIKE POST ANY PILOT CAN CLAIM"
                    : f.CanSend ? "POSTS " + f.SendCount + (f.SendCount == 1 ? " MARK" : " MARKS") + " AS ONE STRIKE ANY PILOT CAN CLAIM" : "LOCK A BOX FIRST";
                FrontKit.Set(sendNote, FrontKit.Fit(sendNote, note, 10f), f.CanConfirm || f.CanSend ? RoomInk.Engrave : steel);
                OpsKit.Label(markSpot, f.ConfirmFull ? "MARKS FULL" : "MARK SPOT");
                OpsKit.Enable(markSpot, f.CanConfirm);
                OpsKit.Enable(release, f.SelectedId > 0);
                FrontKit.Set(words, FrontKit.Fit(words, f.Words ?? "", 10.5f), FrontKit.Tone(f.WordsTone));
            }

            private void PaintLink(FrontRoomView v, SpaceFeedView f)
            {
                if (f.Link != shownLink) { shownLink = f.Link; DrawPanel(shownLink); }
                FrontKit.Set(linkVal, "QUALITY " + f.Link + " / 5 · PADS " + v.PadsUp + " / " + FrontRules.PadCap + " UP", LinkInk(f.Link));
                string note = f.Link >= 5 ? "TWO PADS: THE FASTEST, SHARPEST PICTURE" : v.PadsUp >= 1 ? "ONE PAD: SLOWER, BLOCKIER PICTURE · A SECOND PAD SHARPENS IT"
                    : "NO PAD: THE SLOWEST, BLOCKIEST PICTURE";
                FrontKit.Set(linkNote, FrontKit.Fit(linkNote, note, 10f), Mc.Steel);
            }

            /// <summary>The lock card's and the link's slabs, and the link's five segments.</summary>
            private void DrawPanel(int link)
            {
                panel.Clear();
                Slab(panel, cardX, cardY, cardW, CardH, Mc.NoGo);
                float ly = cardY + CardH + 8f, sw = (cardW - 20f - 16f) / 5f;
                Slab(panel, cardX, ly, cardW, LinkH, Own);
                for (int i = 0; i < 5; i++)
                {
                    float x = cardX + 10f + i * (sw + 4f);
                    panel.Rect(x, ly + 26f, sw, 8f, i < link ? LinkInk(link) : Mc.PlateInk);
                    panel.Frame(x, ly + 26f, sw, 8f, 1f, RoomInk.Bezel.WithAlpha(0.6f));
                }
                panel.Flush();
            }

            /// <summary>No picture: a dead-air test pattern. It marks no swath and no target, so it never passes for coverage; the words in the middle say why.</summary>
            private void DrawStandby(bool on)
            {
                if (on == standbyDrawn) return;
                standbyDrawn = on;
                standby.Clear();
                if (on)
                {
                    Color k = AvInk.Key;
                    Vector2 c = new Vector2(pw * 0.5f, ph * 0.5f);
                    for (float x = 0f; x <= pw; x += 40f) standby.Line(new Vector2(x, 0f), new Vector2(x, ph), 1f, k.WithAlpha(0.10f));
                    for (float y = 0f; y <= ph; y += 40f) standby.Line(new Vector2(0f, y), new Vector2(pw, y), 1f, k.WithAlpha(0.10f));
                    for (int i = 1; i <= 4; i++) standby.Ring(c, 38f * i, 1.2f, k.WithAlpha(0.28f), 72, i % 2 == 0 ? 1f : 0f, 1f);
                    standby.Line(new Vector2(0f, c.y), new Vector2(pw, c.y), 1f, k.WithAlpha(0.3f));
                    standby.Line(new Vector2(c.x, 0f), new Vector2(c.x, ph), 1f, k.WithAlpha(0.3f));
                }
                standby.Flush();
            }
        }

        /// <summary>
        /// A point-filtered 160x100 target that shows a picture in blocks of n post pixels (of 320 across): the resolve's coarse layer and, on a weak link, the
        /// final look of EO / SAR / STRIKE (IR and LOW LIGHT carry their blocks from the post). Counted in <see cref="FxRtPool"/>; a refused target shows the
        /// picture itself instead (no resolve effect, never black).
        /// </summary>
        private sealed class Blocks
        {
            private const int W = DownlinkPost.Width / 2, H = DownlinkPost.Height / 2;
            private readonly string name;
            private RenderTexture rt;
            private bool refused;

            public Blocks(string name) { this.name = name; }

            /// <summary><paramref name="src"/> in blocks of <paramref name="block"/> post pixels (1 is the picture itself), re-drawn when <paramref name="blit"/>.</summary>
            public Texture Of(Texture src, int block, bool blit)
            {
                if (src == null || block <= 1 || !Ensure()) return src;
                if (!blit) return rt;
                RenderTexture previous = RenderTexture.active;
                if (block <= 2) Graphics.Blit(src, rt);
                else
                {
                    RenderTexture tmp = RenderTexture.GetTemporary(Mathf.Max(1, DownlinkPost.Width / block), Mathf.Max(1, DownlinkPost.Height / block), 0, RenderTextureFormat.ARGB32);
                    tmp.filterMode = FilterMode.Point;
                    Graphics.Blit(src, tmp);
                    Graphics.Blit(tmp, rt);
                    RenderTexture.ReleaseTemporary(tmp);
                }
                RenderTexture.active = previous;
                return rt;
            }

            private bool Ensure()
            {
                if (rt != null) return true;
                if (refused) return false;
                rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                rt.Create();
                if (FxRtPool.Own(rt)) return true;
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                rt = null;
                refused = true;
                return false;
            }

            public void Release()
            {
                refused = false;
                if (rt == null) return;
                FxRtPool.Disown(rt);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                rt = null;
            }
        }
    }
}

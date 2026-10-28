using System;
using System.Text;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Vanguard.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// The UAV TERMINAL (after Arma 3's): a near-full-screen <see cref="AvWindow"/> with the OPS front window's input hold
    /// (<see cref="WindowInput"/>: the pilot keeps flying on stick and keyboard, the mouse drives the window). Assets on the link on the
    /// left (click = link), the linked drone's live feed as the hero with its asset card under it, the TACTICAL PLOT on the right
    /// (north up round your aircraft: click a marker to link it, click open ground to send the linked drone there) over the link log,
    /// and the action bar (PREV, NEXT, SEND TO TARGET, BACK ON TASK, SENSOR, ZOOM, HUD FEED). On the feed: left-click turns the drone that way,
    /// right-click on the ground sends it there, the wheel trims height. Every press goes to <see cref="DroneLinkService"/>.
    /// </summary>
    internal sealed class DroneTerminalWindow : MonoBehaviour
    {
        private const float W = 1560f, H = 900f, Pad = 10f, TitleH = 30f, StripH = 18f, BarH = 44f, FooterH = AvGridTokens.Footer;
        private const float LeftW = 330f, HeroW = 840f, Gap = 10f, HeadH = 22f, PlotSize = 350f, NetH = 112f;
        private const int SortOrder = 30008, Rows = 9, Markers = 16;

        private DroneLinkService link;
        private AvWindow window;
        private readonly WindowInput input = new WindowInput();
        private DroneFeedView feed;
        private AvList list;
        private TMP_Text stripLeft, stripRight, assetsHead, budget, net, cardHead, orders, plotHead, plotHint, logHead, log;
        private readonly TMP_Text[] keys = new TMP_Text[13], values = new TMP_Text[13];
        private float listY, leftBottom;
        private AvControl prev, next, toTarget, auto, look, zoom, hud;
        private RectTransform plot;
        private AvVector plotInk;
        private readonly TMP_Text[] markerLabels = new TMP_Text[Markers];
        private readonly TMP_Text[] ringLabels = new TMP_Text[3];
        private readonly Vector2[] markerAt = new Vector2[Markers], labelAt = new Vector2[Markers];
        private float plotScale = 1f, nextPaint;
        private Vector3 plotCentre;
        private readonly StringBuilder text = new StringBuilder(512);

        public bool IsOpen => window != null && window.Visible;

        internal static DroneTerminalWindow Create(Transform parent, DroneLinkService link)
        {
            if (Application.isBatchMode) return null;
            return Make(parent, link);
        }

        /// <summary>The offline harness builds the same window (batch mode included).</summary>
        internal static DroneTerminalWindow Make(Transform parent, DroneLinkService link)
        {
            var go = new GameObject("BoscaliUavTerminal", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<DroneTerminalWindow>();
            view.link = link;
            view.Build(go.transform);
            return view;
        }

        private void Build(Transform uiRoot)
        {
            window = AvWindow.Build(uiRoot, "UavTerminal", "UAV TERMINAL · VANGUARD DATA LINK", W, H, SortOrder);
            // Solid: the window's blur/frost layer drew over the body plate and the HUD and MFDs behind showed through as ghosts.
            window.Opaque = true;
            window.Closed += OnClosed;
            window.CloseControl.Help = "Close the terminal (Esc). Stick and keyboard keep flying the aircraft; the mouse drives this window.";
            window.Footer.Set("Stick and keyboard keep flying your aircraft; the mouse drives the terminal. Esc closes it.", AvState.Inert);
            RectTransform r = window.Root;
            Image body = AvLay.Solid(r, "Body", AvInk.Ground.WithAlpha(0.98f));
            AvLay.Place(body.rectTransform, 0f, TitleH, W, H - TitleH - FooterH);
            body.rectTransform.SetAsFirstSibling();
            body.raycastTarget = true;

            float sy = TitleH + 2f;
            Image strip = AvLay.Solid(r, "Strip", AvInk.State(AvState.Caution).WithAlpha(0.22f));
            AvLay.Place(strip.rectTransform, Pad, sy, W - 2f * Pad, StripH);
            stripLeft = Mono(r, "StripLeft", Pad + 8f, sy, 760f, StripH, TextAlignmentOptions.MidlineLeft);
            stripRight = Mono(r, "StripRight", W - Pad - 768f, sy, 760f, StripH, TextAlignmentOptions.MidlineRight);

            float top = sy + StripH + 8f, bottom = H - FooterH - BarH - 8f;
            float lx = Pad, hx = lx + LeftW + Gap, rx = hx + HeroW + Gap, rw = W - Pad - rx;

            // Assets on the link.
            assetsHead = Head(r, "AssetsHead", lx, top, LeftW);
            list = new AvList(r, window.Ticker, Rows, BindRow) { RowClicked = i => { link.Link(i); Paint(); } };
            listY = top + HeadH + 4f;
            leftBottom = bottom - NetH - 8f;
            budget = AvText.Make(r, "Budget", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            Head(r, "NetHead", lx, bottom - NetH, LeftW).text = "DATA LINK";
            net = AvText.Make(r, "Net", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft);
            AvLay.Place(net, lx + 4f, bottom - NetH + HeadH + 4f, LeftW - 8f, NetH - HeadH - 4f);
            PlaceList();

            // Hero feed + asset card.
            float feedH = Mathf.Round(HeroW * 9f / 16f);
            feed = new DroneFeedView(r, "Feed", 15f);
            feed.Place(new AvSlot(hx, top, HeroW, feedH));
            // Click-to-steer: left-click turns the drone there (above or below the cross climbs or dives), right-click on the ground sends it
            // there to circle, the wheel trims height.
            Image pad = AvLay.Solid(feed.Rect, "SteerPad", Color.clear);
            AvLay.Fill(pad.rectTransform);
            pad.raycastTarget = true;
            SteerPad steer = pad.gameObject.AddComponent<SteerPad>();
            steer.Click = FeedClick;
            steer.Wheel = d => { link.Climb(d > 0f ? 150f : -150f); Paint(); };
            float cy = top + feedH + 8f, cellW = HeroW / 5f;
            cardHead = Head(r, "CardHead", hx, cy, HeroW);
            string[] names = { "TYPE", "JOB", "ORDER", "LINK", "TIME ALOFT", "ALTITUDE", "SPEED", "HEADING", "RANGE / BRG", "COMMAND", "TASK", "SYSTEMS", "POSITION" };
            for (int i = 0; i < keys.Length; i++)
            {
                // Row three: TASK spans three cells, then SYSTEMS and POSITION.
                int col = i < 10 ? i % 5 : i == 10 ? 0 : i - 8, span = i == 10 ? 3 : 1;
                float x = hx + col * cellW, y = cy + HeadH + 6f + (i < 10 ? i / 5 : 2) * 46f;
                keys[i] = AvText.Make(r, "Key " + i, AvTextRole.Micro, names[i]);
                AvLay.Place(keys[i], x + 4f, y, cellW * span - 8f, 14f);
                values[i] = AvText.Make(r, "Value " + i, AvTextRole.DataStrong, "—");
                AvText.Fit(values[i], false);
                AvLay.Place(values[i], x + 4f, y + 14f, cellW * span - 8f, 26f);
            }
            float oy = cy + HeadH + 6f + 3f * 46f + 4f;
            orders = AvText.Make(r, "Orders", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            AvLay.Place(orders, hx + 4f, oy, HeroW - 8f, bottom - oy);

            // Tactical plot + link log.
            plotHead = Head(r, "PlotHead", rx, top, rw);
            plot = AvLay.Child(r, "Plot");
            AvLay.Place(plot, rx, top + HeadH + 4f, rw, PlotSize);
            plot.gameObject.AddComponent<RectMask2D>();
            Image plotBack = AvLay.Solid(plot, "Back", AvInk.Surface.WithAlpha(0.9f));
            AvLay.Fill(plotBack.rectTransform);
            plotBack.raycastTarget = true;
            AvHit.On(plotBack).Click = PlotClick;
            plotInk = AvVector.Create(plot, "Ink", 900);
            for (int i = 0; i < Markers; i++)
            {
                markerLabels[i] = AvText.Make(plot, "Marker " + i, AvTextRole.Micro, "");
                markerLabels[i].raycastTarget = false;
                markerLabels[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < ringLabels.Length; i++)
            {
                ringLabels[i] = AvText.Make(plot, "Ring " + i, AvTextRole.Micro, "");
                ringLabels[i].raycastTarget = false;
            }
            float py = top + HeadH + 4f + PlotSize + 4f;
            plotHint = AvText.Make(r, "PlotHint", AvTextRole.Micro, "CLICK A MARKER TO LINK IT · CLICK OPEN GROUND TO SEND THE LINKED DRONE THERE", TextAlignmentOptions.TopLeft, true);
            AvLay.Place(plotHint, rx, py, rw, 30f);
            logHead = Head(r, "LogHead", rx, py + 34f, rw);
            log = AvText.Make(r, "Log", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft, true);
            AvLay.Place(log, rx + 4f, py + 34f + HeadH + 4f, rw - 8f, bottom - (py + 34f + HeadH + 4f));

            // Action bar.
            float by = H - FooterH - BarH - 2f, bw = (W - 2f * Pad - 6f * 8f) / 7f;
            prev = Button(r, 0, by, bw, "PREV", AvIcon.ChevronLeft, () => link.Cycle(-1), "Link the previous drone. Every drone keeps flying its last order.");
            next = Button(r, 1, by, bw, "NEXT", AvIcon.ChevronRight, () => link.Cycle(1), "Link the next drone. Every drone keeps flying its last order.");
            toTarget = Button(r, 2, by, bw, "SEND TO TARGET", AvIcon.Focus2, link.SendToTarget, "Send the linked drone to your selected target: it flies there and circles it.");
            auto = Button(r, 3, by, bw, "BACK ON TASK", AvIcon.Refresh, link.Release, "Clear the drone's order: it goes back to its own job.");
            look = Button(r, 4, by, bw, "SENSOR", AvIcon.Eye, link.ToggleLook, "Switch the sensor between the daylight camera (EO) and white-hot infrared (IR).");
            zoom = Button(r, 5, by, bw, "ZOOM", AvIcon.Focus2, link.CycleZoom, "Step the camera zoom: 1X, 3X, 7X.");
            hud = Button(r, 6, by, bw, "HUD FEED", AvIcon.Camera, link.ToggleFeed, "Show the linked drone's feed card on your HUD in flight (it is always on while you hand-fly).");
        }

        private AvControl Button(RectTransform r, int slot, float y, float w, string label, AvIcon icon, Action click, string help)
        {
            AvControl c = AvControl.Make(r, new AvControl.Spec(label, () => { click(); Paint(); }, AvButtonStyle.Default, icon));
            AvLay.Place(c.Rect, Pad + slot * (w + 8f), y, w, BarH - 6f);
            c.SingleLine();
            c.Help = help;
            return c;
        }

        private static TMP_Text Mono(RectTransform r, string name, float x, float y, float w, float h, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(r, name, AvTextRole.Micro, "", align);
            t.characterSpacing = 1.5f;
            AvLay.Place(t, x, y, w, h);
            return t;
        }

        private static TMP_Text Head(RectTransform r, string name, float x, float y, float w)
        {
            TMP_Text t = AvText.Make(r, name, AvTextRole.Head, "");
            AvText.Fit(t, false);
            AvLay.Place(t, x, y, w, HeadH);
            Image rule = AvLay.Solid(r, name + " Rule", AvInk.Hairline);
            AvLay.Place(rule.rectTransform, x, y + HeadH, w, 1f);
            rule.raycastTarget = false;
            return t;
        }

        // ---- open / close ------------------------------------------------------------------------------------------------

        public void Open()
        {
            if (window == null || IsOpen) return;
            try
            {
                window.Show();
                input.Take();
                AvUiSound.Play(AvUiCue.Engage);
                Paint();
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError("UAV terminal open failed: " + e);
                Abort();
            }
        }

        public void Close() { if (IsOpen) window.Hide(); }

        private void OnClosed()
        {
            input.GiveBack();
            AvUiSound.Play(AvUiCue.Release);
        }

        private void Abort()
        {
            try { if (IsOpen) window.Hide(); } catch (Exception) { /* the input guard below must run whatever the window did */ }
            input.Force();
        }

        private void OnDisable() => input.Force();

        private void OnDestroy()
        {
            if (IsOpen) { try { window.Hide(); } catch (Exception) { } }
            input.Force();
        }

        private void Update()
        {
            input.Tick();
            if (!IsOpen) return;
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
                if (!link.HasAircraft) { Close(); return; } // dead, ejected or in the menu
                input.Keep();
                input.Pointer(window.Root);
                feed.Paint(link);
                if (Time.unscaledTime >= nextPaint) Paint();
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError("UAV terminal fault, closing: " + e);
                Abort();
            }
        }

        // ---- paint (5 Hz, and after every press) --------------------------------------------------------------------------

        internal void Paint()
        {
            nextPaint = Time.unscaledTime + 0.2f;
            DroneView v = link.Linked;
            AvText.Set(stripLeft, "VANGUARD C2 // UAV DATA LINK 16X // " + link.Count + " ON NET // LINK LIMIT " + AvNum.Fixed(DroneLink.MaxRangeM / 1000f, 0) + " KM");
            AvText.Set(stripRight, "OWN HDG " + Deg(link.SelfHeading) + " // MSN T+" + DroneLink.Clock(Time.timeSinceLevelLoad) + " // SECRET // REL CREW");
            AvText.Set(assetsHead, "ASSETS ON LINK · " + link.Count);
            list.SetCount(link.Count);
            if (v != null) list.Reveal(link.LinkedIndex);
            PlaceList();
            AvText.Set(budget, Budget());
            AvText.Set(net, NetLines());
            Card(v);
            toTarget.Interactable = link.Count > 0 && link.CanStrike;
            auto.Interactable = v != null && v.Mode != DroneMode.Auto;
            prev.Interactable = next.Interactable = link.Count > 1;
            look.Label = "SENSOR · " + link.LookWord;
            zoom.Label = "ZOOM · " + link.ZoomWord;
            hud.Label = link.FeedShown ? "HUD FEED · ON" : "HUD FEED · OFF";
            hud.Latched = link.FeedShown;
            Plot();
            AvText.Set(logHead, "LINK LOG");
            text.Clear();
            for (int i = link.Log.Count - 1; i >= 0; i--) text.Append(link.Log[i]).Append('\n');
            AvText.Set(log, text.Length > 0 ? text.ToString() : "NO TRAFFIC YET");
        }

        private void BindRow(int index, AvRow row)
        {
            if (index < 0 || index >= link.Count) { row.Set("—", "", ""); return; }
            DroneView v = link[index];
            bool linked = index == link.LinkedIndex;
            AvState state = v.Quality <= 0f ? AvState.Danger : v.Mode == DroneMode.Hand ? AvState.Caution : linked ? AvState.Info : AvState.Ready;
            row.Set(v.Callsign + (linked ? "  [LINK]" : ""), v.Job + " · " + DroneLink.Word(v.Mode) + " · " + Bars(v.Quality),
                AvNum.Fixed(v.RangeM / 1000f, 1) + " KM", state);
            row.Help = "Link " + v.Callsign + ": the feed, the card and the stick follow the link.";
        }

        // Flavour for the link panel: band and crypto are fixed words, the channel count and own ship are live.
        private string NetLines() =>
            "BAND      KU · VNG-16X MESH\nCRYPTO    KEY " + DroneLink.Clock(Time.timeSinceLevelLoad / 60f) + " · VALID\nUPLINK    " + link.Count +
            " OF 8 CHANNELS OPEN\nTERMINAL  " + (link.HasAircraft ? "OWN SHIP · HDG " + Deg(link.SelfHeading) : "NO OWN SHIP");

        private string Budget()
        {
            if (!link.HasAircraft) return "NO AIRCRAFT. The terminal needs your own aircraft in the air.";
            if (link.Count == 0) return "NO DRONE AIRBORNE.\nLaunch a VANGUARD drone (MALD, REMORA, HAWC-X, GLAIVE, KESTREL, HAMILCAR) and it joins the link. AEGIS, ORCA and the ALE-X kite are not on the link.";
            int hands = 0, wpts = 0;
            for (int i = 0; i < link.Count; i++) { if (link[i].Mode == DroneMode.Hand) hands++; else if (link[i].Mode == DroneMode.Waypoint) wpts++; }
            return "ON TASK " + (link.Count - hands - wpts) + " · HAND " + hands + " · WPT " + wpts +
                "\nOrders stay with their drone: link another and the last one keeps flying its order. Past " + AvNum.Fixed(DroneLink.MaxRangeM / 1000f, 0) +
                " KM the link drops and the drone goes back to its job.";
        }

        private void Card(DroneView v)
        {
            AvText.Set(cardHead, v != null ? "SELECTED ASSET · " + v.Callsign : "SELECTED ASSET · NONE");
            if (v == null)
            {
                for (int i = 0; i < values.Length; i++) AvText.Set(values[i], "—");
                AvText.Set(orders, link.Count > 0 ? "Pick an asset on the left or a marker on the plot to link it." : "");
                return;
            }
            AvText.Set(values[0], v.Type);
            AvText.Set(values[1], v.Job);
            AvText.Set(values[2], DroneLink.Word(v.Mode));
            AvText.Set(values[3], AvNum.Fixed(v.Quality * 100f, 0) + "%  " + Bars(v.Quality));
            AvText.Set(values[4], DroneLink.Clock(v.Age));
            AvText.Set(values[5], AvNum.Fixed(v.AltM, 0) + " M");
            AvText.Set(values[6], AvNum.Fixed(v.SpeedMs * 1.944f, 0) + " KT");
            AvText.Set(values[7], Deg(v.HeadingDeg));
            AvText.Set(values[8], AvNum.Fixed(v.RangeM / 1000f, 1) + " KM / " + Deg(v.BearingDeg));
            AvText.Set(values[9], v.Mode == DroneMode.Waypoint ? AvNum.Fixed(Flat(v.Position, v.Point) / 1000f, 1) + " KM OUT"
                : v.Mode == DroneMode.Hand ? "CRS " + Deg(v.Point.x) + " · " + AvNum.Fixed(v.Point.y, 0) + " M" : "—");
            AvText.Set(values[10], DroneLink.Task(v.Role));
            AvText.Set(values[11], v.Mode == DroneMode.Auto ? "ON TASK" : v.Role == VanguardRole.Drone ? "HOLD · RAM FUSE LIVE" : "AUTO HOLD");
            Vector3 d = v.Position - link.SelfPosition;
            AvText.Set(values[12], "E " + Signed(d.x) + " · N " + Signed(d.z));
            values[11].color = AvInk.State(v.Mode == DroneMode.Auto ? AvState.Ready : AvState.Caution);
            values[2].color = AvInk.State(v.Mode == DroneMode.Hand ? AvState.Caution : v.Mode == DroneMode.Waypoint ? AvState.Info : AvState.Ready);
            values[3].color = AvInk.State(v.Quality <= 0f ? AvState.Danger : v.Quality < 0.5f ? AvState.Caution : AvState.Ready);
            AvText.Set(orders, v.Mode == DroneMode.Hand
                ? "HAND: it holds the course and height you gave it. LEFT-CLICK THE FEED to turn it there (above or below the cross climbs or dives), WHEEL trims height, RIGHT-CLICK the ground to send it there. BACK ON TASK resumes its own job."
                : v.Mode == DroneMode.Waypoint
                    ? "WAYPOINT: it flies to the point and circles it at " + AvNum.Fixed(DroneLink.OrbitRadiusM / 1000f, 1) + " KM. RIGHT-CLICK the feed or click the plot to move the point, LEFT-CLICK the feed to fly it by hand. BACK ON TASK resumes its own job."
                    : "ON TASK: it flies its own job. RIGHT-CLICK the ground in the feed (or click the plot) to send it there, LEFT-CLICK the feed to turn it that way by hand.");
        }

        // ---- tactical plot -----------------------------------------------------------------------------------------------

        private void Plot()
        {
            float size = plot.rect.width > 1f ? plot.rect.width : PlotSize, half = size * 0.5f, margin = 18f;
            float farthest = 0f;
            for (int i = 0; i < link.Count; i++) farthest = Mathf.Max(farthest, link[i].RangeM / 1000f);
            float rangeKm = DroneLink.PlotRangeKm(farthest);
            plotScale = (half - margin) / (rangeKm * 1000f);
            plotCentre = link.SelfPosition;
            AvText.Set(plotHead, "TACTICAL PLOT · NORTH UP · " + AvNum.Fixed(rangeKm, 0) + " KM");

            AvQuadBuffer b = plotInk.Buffer;
            b.Clear();
            Rgba hair = AvInk.Hairline.ToRgba(), dim = AvInk.Dim.WithAlpha(0.5f).ToRgba(), ink = AvInk.Ink.ToRgba();
            float c = half, ph = PlotSize;
            AvStrokes.Line(b, margin * 0.5f, c, size - margin * 0.5f, c, 1f, hair);
            AvStrokes.Line(b, c, margin * 0.5f, c, ph - margin * 0.5f, 1f, hair);
            for (int i = 0; i < 3; i++)
            {
                float km = rangeKm * (i + 1) / 3f, rr = km * 1000f * plotScale;
                AvStrokes.Ring(b, c, c, rr, 72, 1f, i == 2 ? dim : hair);
                AvText.Set(ringLabels[i], AvNum.Fixed(km, km < 10f ? 1 : 0) + " KM");
                AvLay.Place(ringLabels[i], c + rr * 0.7071f + 2f, ph - (c + rr * 0.7071f) - 7f, 60f, 14f);
            }
            if (rangeKm * 1000f >= DroneLink.MaxRangeM - 1f)
                AvStrokes.DashedRing(b, c, c, DroneLink.MaxRangeM * plotScale, 48, 0.5f, 1.4f, AvInk.State(AvState.Danger).WithAlpha(0.7f).ToRgba());
            Glyph(b, c, c, link.SelfHeading, 9f, AvInk.Friendly.ToRgba()); // own aircraft

            int shown = 0;
            for (int i = 0; i < link.Count && shown < Markers; i++, shown++)
            {
                DroneView v = link[i];
                Vector2 p = ToPlot(v.Position);
                markerAt[i] = p;
                bool linked = i == link.LinkedIndex;
                Rgba tone = AvInk.State(v.Quality <= 0f ? AvState.Danger : v.Mode == DroneMode.Hand ? AvState.Caution
                    : v.Mode == DroneMode.Waypoint ? AvState.Info : AvState.Ready).ToRgba();
                if (v.Mode == DroneMode.Waypoint)
                {
                    Vector2 w = ToPlot(v.Point);
                    AvStrokes.DashedLine(b, p.x, p.y, w.x, w.y, 4f, 3f, 1f, tone);
                    AvStrokes.Cross(b, w.x, w.y, 5f, 1.4f, tone);
                    AvStrokes.Ring(b, w.x, w.y, Mathf.Max(3f, DroneLink.OrbitRadiusM * plotScale), 32, 1f, tone);
                }
                if (v.Mode == DroneMode.Hand)
                {
                    float cr = v.Point.x * Mathf.Deg2Rad; // the commanded course, ahead of the heading stub
                    AvStrokes.DashedLine(b, p.x, p.y, p.x + Mathf.Sin(cr) * 44f, p.y + Mathf.Cos(cr) * 44f, 4f, 3f, 1f, tone);
                }
                float rad = v.HeadingDeg * Mathf.Deg2Rad;
                AvStrokes.Line(b, p.x, p.y, p.x + Mathf.Sin(rad) * 12f, p.y + Mathf.Cos(rad) * 12f, 1.2f, tone);
                AvStrokes.Diamond(b, p.x, p.y, linked ? 6f : 4.5f, linked ? 2f : 1.4f, tone);
                if (linked)
                {
                    AvStrokes.Bracket(b, p.x - 11f, p.y - 11f, 22f, 22f, 6f, 1.4f, ink);
                    AvStrokes.DashedLine(b, c, c, p.x, p.y, 3f, 4f, 1f, dim);
                }
                TMP_Text label = markerLabels[shown];
                if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
                AvText.Set(label, v.Callsign);
                label.color = linked ? AvInk.Select : AvInk.Ink;
                // Drones flying together used to print their callsigns on top of each other: step a label down past the ones placed.
                Vector2 at = new Vector2(p.x + 9f, ph - p.y - 7f);
                for (int k = 0; k < shown; k++)
                    if (Mathf.Abs(labelAt[k].x - at.x) < 110f && Mathf.Abs(labelAt[k].y - at.y) < 14f) { at.y = labelAt[k].y + 14f; k = -1; }
                labelAt[shown] = at;
                AvLay.Place(label, at.x, at.y, 110f, 14f);
            }
            for (int i = shown; i < Markers; i++) if (markerLabels[i].gameObject.activeSelf) markerLabels[i].gameObject.SetActive(false);
            plotInk.Commit();
        }

        private static void Glyph(AvQuadBuffer b, float x, float y, float headingDeg, float r, Rgba c)
        {
            float a = (90f - headingDeg) * Mathf.Deg2Rad; // compass to plot angle (y up)
            Vector2 f = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), s = new Vector2(-f.y, f.x);
            Vector2 tip = new Vector2(x, y) + f * r, l = new Vector2(x, y) - f * r * 0.7f + s * r * 0.7f, rt = new Vector2(x, y) - f * r * 0.7f - s * r * 0.7f;
            AvStrokes.Line(b, l.x, l.y, tip.x, tip.y, 1.6f, c);
            AvStrokes.Line(b, rt.x, rt.y, tip.x, tip.y, 1.6f, c);
            AvStrokes.Line(b, l.x, l.y, rt.x, rt.y, 1.6f, c);
        }

        private Vector2 ToPlot(Vector3 global)
        {
            float half = PlotSize * 0.5f;
            return new Vector2(half + (global.x - plotCentre.x) * plotScale, half + (global.z - plotCentre.z) * plotScale);
        }

        private void PlotClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(plot, e.position, null, out Vector2 local)) return;
            var p = new Vector2(local.x, PlotSize + local.y); // top-left pivot, y down -> the plot's y-up frame
            int best = -1;
            float bestD = 16f;
            for (int i = 0; i < Mathf.Min(link.Count, Markers); i++)
            {
                float d = Vector2.Distance(p, markerAt[i]);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0) link.Link(best);
            else if (link.Linked != null && plotScale > 0f)
                link.SendTo(new Vector3(plotCentre.x + (p.x - PlotSize * 0.5f) / plotScale, 0f, plotCentre.z + (p.y - PlotSize * 0.5f) / plotScale));
            Paint();
        }

        // Left = turn that way (HAND), right = go to the ground there (WPT).
        private void FeedClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Middle) return;
            RectTransform rt = feed.Rect;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, null, out Vector2 local)) return;
            Rect box = rt.rect;
            var at = new Vector2((local.x - box.xMin) / box.width, (local.y - box.yMin) / box.height);
            if (e.button == PointerEventData.InputButton.Left) link.SteerAt(at);
            else if (!link.SteerGround(at)) AvUiSound.Play(AvUiCue.Caution); // open sky: nothing to fly to
            Paint();
        }

        /// <summary>The feed's click and wheel (AvHit has no wheel).</summary>
        private sealed class SteerPad : MonoBehaviour, IPointerClickHandler, IScrollHandler
        {
            public Action<PointerEventData> Click;
            public Action<float> Wheel;
            public void OnPointerClick(PointerEventData e) => Click?.Invoke(e);
            public void OnScroll(PointerEventData e) { if (e.scrollDelta.y != 0f) Wheel?.Invoke(e.scrollDelta.y); }
        }

        private static string Signed(float metres) => (metres > -50f ? "+" : "-") + AvNum.Fixed(Mathf.Abs(metres) / 1000f, 1);      // under 50 m rounds to 0.0: no "-0.0"

        private void PlaceList()
        {
            float listH = list.Measure(LeftW);
            list.Place(new AvSlot(Pad, listY, LeftW, listH));
            float by = listY + listH + 8f;
            AvLay.Place(budget, Pad, by, LeftW, Mathf.Max(20f, leftBottom - by));
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static string Deg(float deg) => DroneFeedView.Three(deg) + "°";

        private static string Bars(float quality)
        {
            return "SIG " + DroneLink.Bars(quality) + "/5";
        }
    }
}

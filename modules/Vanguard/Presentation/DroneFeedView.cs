using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// The linked drone's video as every drone view shows it (UAV terminal hero, UAV MFD, HUD feed card): the feed picture under
    /// analogue static that thickens as the link weakens, bursts on every switch and fills the screen with no signal, and a sensor-ball
    /// overlay as dense as the MISSILE CAM page (2026-10-20): REC lamp, callsign, REMORA stores, order and lens mode, a numbered heading
    /// tape, scrolling ALT and SPD tapes, horizon bars where the stabilised lens puts the horizon, a gimbal box (azimuth off the nose,
    /// elevation), the designation boxed through the lens (snapping solid with a ring when the ball locks on it), OWN and WPT marks,
    /// link quality and latency, sensor, zoom, field of view, SPI slant range and grid, a frame counter, and the commanded course while hand-flown. The big
    /// (terminal) view prints the long lines; the cards keep one line a corner. Cosmetic, client-local.
    /// </summary>
    internal sealed class DroneFeedView : AvPart
    {
        private const float BurstSeconds = 0.6f, LockSnap = 0.5f, BigWidth = 600f;
        private const float HaloPx = 2.4f;
        private static readonly Rgba Halo = new Rgba(0f, 0f, 0f, 0.6f);

        private readonly Image back;
        private readonly RawImage picture, snow;
        private readonly AvVector ink;
        private readonly TMP_Text topLeft, topRight, heading, alt, speed, bottomLeft, bottomRight, centre, gimbal, markName, markRange, ownLabel, wptLabel;
        private readonly TMP_Text[] numerals = new TMP_Text[3];
        private readonly Image recLamp;
        private readonly float type;
        private float w = 320f, h = 180f, pad = 8f, line = 15f, boxW = 60f, topBand = 60f, bottomBand = 20f;
        private bool big;
        private bool wasTracking;
        private float lockAt = -9f;
        private static Texture2D staticTile;

        /// <param name="textSize">Point size of the overlay text (the terminal's hero uses a larger one than the cards).</param>
        public DroneFeedView(RectTransform parent, string name, float textSize)
        {
            type = textSize;
            Rect = AvLay.Child(parent, name);
            Rect.gameObject.AddComponent<RectMask2D>();
            back = AvLay.Solid(Rect, "Back", Color.black);
            AvLay.Fill(back.rectTransform);
            back.raycastTarget = false;
            picture = Raw("Picture");
            snow = Raw("Static");
            snow.texture = Static();
            ink = AvVector.Create(Rect, "Symbology", 700);
            recLamp = AvLay.Solid(Rect, "Rec", Color.red);
            recLamp.raycastTarget = false;
            topLeft = Text("TopLeft", TextAlignmentOptions.TopLeft);
            topRight = Text("TopRight", TextAlignmentOptions.TopRight);
            heading = Text("Heading", TextAlignmentOptions.Center);
            for (int i = 0; i < numerals.Length; i++) numerals[i] = Text("Numeral " + i, TextAlignmentOptions.Center);
            alt = Text("Alt", TextAlignmentOptions.MidlineLeft);
            speed = Text("Speed", TextAlignmentOptions.MidlineRight);
            bottomLeft = Text("BottomLeft", TextAlignmentOptions.BottomLeft);
            bottomRight = Text("BottomRight", TextAlignmentOptions.BottomRight);
            gimbal = Text("Gimbal", TextAlignmentOptions.MidlineLeft);
            markName = Text("MarkName", TextAlignmentOptions.Bottom);
            markRange = Text("MarkRange", TextAlignmentOptions.Top);
            ownLabel = Text("Own", TextAlignmentOptions.MidlineLeft);
            wptLabel = Text("Wpt", TextAlignmentOptions.MidlineLeft);
            centre = Text("Centre", TextAlignmentOptions.Center);
            centre.fontStyle = FontStyles.Bold;
        }

        private RawImage Raw(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            RawImage image = go.AddComponent<RawImage>();
            image.raycastTarget = false;
            AvLay.Fill(image.rectTransform);
            return image;
        }

        private TMP_Text Text(string name, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(Rect, name, AvTextRole.DataSmall, "", align);
            AvText.Size(t, type);
            t.raycastTarget = false;
            return t;
        }

        // Grey snow, built once: the grain tile is single-channel and would draw red in a UI image.
        private static Texture2D Static()
        {
            if (staticTile != null) return staticTile;
            const int size = 96;
            staticTile = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "BoscaliDroneStatic", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var rng = new System.Random(7);
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) { byte g = (byte)rng.Next(256); px[i] = new Color32(g, g, g, 255); }
            staticTile.SetPixels32(px);
            staticTile.Apply(false, true);
            return staticTile;
        }

        public override float Measure(float width) => Mathf.Round(width * 9f / 16f);

        /// <summary>The overlay's ink; null = the avionics white. The HUD card sets the HUD theme's colour so it reads like the rest of the HUD.</summary>
        public Color? Ink;

        private float TapeRow => big ? pad + line * 2f + 2f : pad;     // the card has no corner words over its tape

        public override void Place(AvSlot s)
        {
            base.Place(s);
            w = s.W;
            h = s.H;
            big = w >= BigWidth;
            pad = Mathf.Round(h * 0.035f) + 4f;
            line = type + 4f;
            boxW = type * 4.6f;
            int lines = big ? 2 : 1;
            topBand = TapeRow + line + type * 1.5f;
            bottomBand = pad + line * lines + 4f;
            AvLay.Place(recLamp.rectTransform, pad, pad + 3f, type * 0.6f, type * 0.6f);
            AvLay.Place(topLeft, pad + type, pad, w * 0.5f, line * 2f);
            AvLay.Place(topRight, w * 0.5f - pad, pad, w * 0.5f, line * 2f);
            AvLay.Place(heading, w * 0.5f - type * 1.7f, TapeRow, type * 3.4f, line);
            AvLay.Place(alt, pad + type * 1.3f, h * 0.5f - line - 2f, boxW, line * 2f + 4f);
            AvLay.Place(speed, w - pad - type * 1.3f - boxW, h * 0.5f - line - 2f, boxW, line * 2f + 4f);
            AvLay.Place(bottomLeft, pad + type * 3.6f, h - pad - line * lines, w * 0.5f, line * lines);
            AvLay.Place(bottomRight, w * 0.5f - pad, h - pad - line * lines, w * 0.5f, line * lines);
            AvLay.Place(gimbal, w * 0.5f + type * 3f, h - bottomBand - type * 3.1f, type * 5f, line * 2f);
            AvLay.Place(centre, 0f, h * 0.5f + h * 0.12f, w, line * 1.6f);
        }

        /// <summary>Every frame the view is on screen: asks for the feed and repaints picture, static and symbology.</summary>
        public void Paint(DroneLinkService link)
        {
            link.Want();
            DroneView v = link.Linked;
            Texture tex = link.Feed;
            if (picture.texture != tex) picture.texture = tex;
            picture.enabled = tex != null;

            float now = Time.unscaledTime;
            float quality = v != null ? v.Quality : 0f;
            // Static says "weak link" without burying the picture and the words (a full-screen snow storm read as noise, user 2026-10-20).
            float amount = v == null ? (link.Lost ? 0.25f : 0.45f) : Mathf.Pow(1f - quality, 1.5f) * 0.4f;
            if (v != null && link.SinceSwitch < BurstSeconds) amount = Mathf.Max(amount, 0.6f * (1f - link.SinceSwitch / BurstSeconds));
            if (v != null && quality <= 0f) amount = 0.45f;
            picture.color = v != null && quality <= 0f ? new Color(0.45f, 0.45f, 0.45f, 1f) : Color.white;
            snow.enabled = amount > 0.02f;
            if (snow.enabled)
            {
                snow.color = new Color(1f, 1f, 1f, Mathf.Clamp01(amount));
                snow.uvRect = new Rect(Random.value, Random.value, w / 160f, h / 160f);
            }

            bool tracking = v != null && link.Tracking;
            if (tracking && !wasTracking) lockAt = now;
            wasTracking = tracking;

            Color ink0 = Ink ?? AvInk.Ink, dim = Ink.HasValue ? Ink.Value.WithAlpha(0.6f) : AvInk.Dim;
            Color tone = v == null ? AvInk.State(AvState.Inert) : v.Mode == DroneMode.Hand ? AvInk.State(AvState.Caution)
                : v.Mode == DroneMode.Waypoint ? AvInk.State(AvState.Info) : AvInk.State(AvState.Ready);
            bool blink = ((int)(now * 1.25f) & 1) == 0;
            recLamp.enabled = v != null && blink;
            recLamp.color = AvInk.State(AvState.Danger);

            Words(link, v, quality);
            topLeft.color = heading.color = alt.color = speed.color = bottomLeft.color = bottomRight.color = gimbal.color = ink0;
            topRight.color = tone;
            centre.color = link.Lost || (v != null && quality <= 0f) ? AvInk.State(AvState.Danger) : ink0;
            Draw(link, v, quality, now, ink0, dim, tone);
        }

        private void Words(DroneLinkService link, DroneView v, float quality)
        {
            if (v == null)
            {
                AvText.Set(topLeft, big ? "UAV LINK" : "");
                AvText.Set(topRight, "");
                AvText.Set(heading, ""); AvText.Set(alt, ""); AvText.Set(speed, ""); AvText.Set(gimbal, "");
                AvText.Set(bottomLeft, "LINK —");
                AvText.Set(bottomRight, link.LookWord + " · " + link.ZoomWord);
                AvText.Set(centre, link.Lost ? "SIGNAL LOST · " + link.LostName : link.Count > 0 ? "NO DRONE LINKED" : "NO DRONE AIRBORNE");
                return;
            }
            string second = link.Stores(out RemoraView stores)
                ? "AAM " + RemoraArcher.Left(stores.Fired) + "/" + RemoraArcher.Rounds + " · FLR " + stores.Flares + " · " + Phase(stores.Phase)
                : "SYS " + (v.Mode == DroneMode.Auto ? "ON TASK" : "HOLD");
            // The card's header already names the drone and its order: the small view keeps its corners empty.
            AvText.Set(topLeft, big ? "REC  " + v.Callsign + " · " + v.Job + "\n" + second : "");
            AvText.Set(topRight, !big ? "" : DroneLink.Word(v.Mode) + " · " + link.LensWord + "\n" +
                (big ? "FRM " + (link.FrameNumber % 1000000).ToString("000000") + " · " : "") + "T+" + DroneLink.Clock(v.Age));
            AvText.Set(heading, Three(v.HeadingDeg));
            AvText.Set(alt, v.Mode == DroneMode.Hand && Mathf.Abs(v.Point.y - v.AltM) > 30f
                ? (v.Point.y > v.AltM ? "ALT CLB" : "ALT DSC") + "\n" + AvNum.Fixed(v.AltM, 0) + " M"       // climbing or diving to the commanded height
                : "ALT\n" + AvNum.Fixed(v.AltM, 0) + " M");
            AvText.Set(speed, "SPD\n" + AvNum.Fixed(v.SpeedMs * 1.944f, 0) + " KT");
            string spi = link.HasSpi ? "SPI " + AvNum.Fixed(link.SpiSlantM / 1000f, 1) + " KM" : "SPI —";
            if (big)
            {
                AvText.Set(bottomLeft, "LINK " + AvNum.Fixed(quality * 100f, 0) + "% · SIG " + DroneLink.Bars(quality) + "/5 · LAT " + DroneLink.LatencyMs(quality) + " MS\n" +
                    "RNG " + AvNum.Fixed(v.RangeM / 1000f, 1) + " KM · BRG " + Three(v.BearingDeg) + " · KU-16X");
                AvText.Set(bottomRight, link.LookWord + " · " + link.ZoomWord + " · FOV " + AvNum.Fixed(link.FieldOfView, 0) + "° · " + link.LensWord + "\n" +
                    spi + (link.HasSpi ? " · GRID " + DroneLink.Grid(link.SpiGlobal.x, link.SpiGlobal.z) : ""));
                AvText.Set(gimbal, "AZ " + Signed(link.LensAzimuth) + "\nEL " + Signed(link.LensElevation));
            }
            else
            {
                AvText.Set(bottomLeft, "LINK " + AvNum.Fixed(quality * 100f, 0) + "% · " + AvNum.Fixed(v.RangeM / 1000f, 1) + " KM");
                AvText.Set(bottomRight, link.LookWord + " · " + link.ZoomWord);
                AvText.Set(gimbal, "");
            }
            AvText.Set(centre, quality <= 0f ? "NO SIGNAL · RETURNING TO TASK" : link.SinceSwitch < BurstSeconds ? "ACQUIRING LINK · " + v.Callsign : "");
        }

        private static string Phase(RemoraPhase phase) => phase == RemoraPhase.Engage ? "ENGAGE" : phase == RemoraPhase.Ram ? "RAM" : "ESCORT";

        private static string Three(float deg) => AvNum.Fixed(Mathf.Repeat(deg, 360f), 0).PadLeft(3, '0');

        private static string Signed(float deg) => (deg < -0.5f ? "-" : "+") + AvNum.Fixed(Mathf.Abs(deg), 0).PadLeft(3, '0');

        // A light stroke over a wider dark one, a little longer at both ends: the symbology reads on bright haze as on dark sea.
        private static void Outlined(AvQuadBuffer b, float ax, float ay, float bx, float by, float width, Rgba c)
        {
            float dx = bx - ax, dy = by - ay, len = Mathf.Sqrt(dx * dx + dy * dy), e = len > 1e-3f ? HaloPx * 0.5f / len : 0f;
            AvStrokes.Line(b, ax - dx * e, ay - dy * e, bx + dx * e, by + dy * e, width + HaloPx, Halo);
            AvStrokes.Line(b, ax, ay, bx, by, width, c);
        }

        // Symbology in the view's own pixels, y up from the bottom (AvVector's frame).
        private void Draw(DroneLinkService link, DroneView v, float quality, float now, Color ink0, Color dim, Color tone)
        {
            AvQuadBuffer b = ink.Buffer;
            b.Clear();
            // Scrims under the text bands (they draw below the words): white symbology stays legible over a bright cloud deck.
            Rgba shade = new Rgba(0f, 0f, 0f, 0.58f);      // 0.4 washed out over a bright sky in game (2026-10-20)
            AvStrokes.Fill(b, 0f, h - topBand, w, topBand, shade);
            AvStrokes.Fill(b, 0f, 0f, w, bottomBand, shade);
            // SIGNAL LOST / NO SIGNAL on a dark band: red words over static were unreadable (in game 2026-10-20).
            if (!string.IsNullOrEmpty(centre.text)) AvStrokes.Fill(b, 0f, h * 0.38f - line * 1.6f, w, line * 1.6f, new Rgba(0f, 0f, 0f, 0.7f));
            Rgba a = ink0.WithAlpha(0.85f).ToRgba(), faint = dim.WithAlpha(0.6f).ToRgba();
            float cx = w * 0.5f, cy = h * 0.5f, lw = Mathf.Max(1f, h / 240f), corner = h * 0.07f;
            if (big) // the card's own frame (FeedCorner) has the corners
            {
                AvStrokes.Bracket(b, 3f, 3f, w - 6f, h - 6f, corner, lw + HaloPx, Halo);
                AvStrokes.Bracket(b, 3f, 3f, w - 6f, h - 6f, corner, lw, faint);
            }
            if (v != null)
            {
                Reticle(b, cx, cy, lw, a);
                HeadingTape(v.HeadingDeg, v.Mode == DroneMode.Hand ? v.Point.x : float.NaN, lw, a, faint, tone.ToRgba());
                // The card keeps the readings and drops the ladders and the gimbal box: a small picture with a big one's furniture was clutter.
                Tape(b, true, v.AltM, 1000f, 100f, 5, lw, a, faint);
                Tape(b, false, v.SpeedMs * 1.944f, 120f, 10f, 5, lw, a, faint);
                Horizon(b, link, lw, a);
                if (big) Gimbal(b, link, lw, faint, a);
                Marks(b, link, v, now, lw, a, tone);
            }
            else
            {
                foreach (TMP_Text n in numerals) Hide(n);
                Hide(markName); Hide(markRange); Hide(ownLabel); Hide(wptLabel);
            }
            // Link bars beside the bottom-left words.
            int bars = v != null ? DroneLink.Bars(quality) : 0;
            float bx = pad, by = pad * 0.5f + 2f, bw = Mathf.Max(2f, type * 0.28f);
            Rgba lit = tone.ToRgba(), off = dim.WithAlpha(0.35f).ToRgba();
            for (int i = 0; i < 5; i++)
                AvStrokes.Fill(b, bx + i * (bw + 2f), by, bw, type * (0.3f + 0.14f * i), i < bars ? lit : off);
            ink.Commit();
        }

        private static void Reticle(AvQuadBuffer b, float cx, float cy, float lw, Rgba a)
        {
            float arm = cy * 0.14f, gap = cy * 0.05f;
            Outlined(b, cx - gap - arm, cy, cx - gap, cy, lw * 1.4f, a);
            Outlined(b, cx + gap, cy, cx + gap + arm, cy, lw * 1.4f, a);
            Outlined(b, cx, cy - gap - arm, cx, cy - gap, lw * 1.4f, a);
            Outlined(b, cx, cy + gap, cx, cy + gap + arm * 0.6f, lw * 1.4f, a);
            AvStrokes.Fill(b, cx - lw - 1.2f, cy - lw - 1.2f, 2f * lw + 2.4f, 2f * lw + 2.4f, Halo);
            AvStrokes.Fill(b, cx - lw, cy - lw, 2f * lw, 2f * lw, a);
        }

        // A tick every 10 degrees within 40 either side, numerals on the 30s (N E S W on the cardinals) clear of the boxed heading,
        // a caret under the box.
        private void HeadingTape(float headingDeg, float commandDeg, float lw, Rgba a, Rgba faint, Rgba tone)
        {
            if (!float.IsNaN(commandDeg))
            {
                // HAND: the commanded course as a caret on the tape (pinned to the tape's end while the turn is wider than it).
                float off = Mathf.Clamp(Mathf.DeltaAngle(headingDeg, commandDeg), -40f, 40f);
                float x = w * 0.5f + off / 40f * w * 0.22f, y = h - TapeRow - line - type * 0.95f;
                AvStrokes.Chevron(ink.Buffer, x, y, type * 0.55f, 90f, lw * 2f + HaloPx, Halo);
                AvStrokes.Chevron(ink.Buffer, x, y, type * 0.55f, 90f, lw * 2f, tone);
            }
            AvQuadBuffer b = ink.Buffer;
            float cx = w * 0.5f, span = w * 0.22f, row = TapeRow, ticksTop = h - row - line, boxHalf = type * 1.7f;
            float deg = Mathf.Repeat(headingDeg, 360f);
            AvStrokes.Fill(b, cx - boxHalf, ticksTop, boxHalf * 2f, line, new Rgba(0f, 0f, 0f, 0.55f));
            AvStrokes.Bracket(b, cx - boxHalf, ticksTop, boxHalf * 2f, line, line * 0.5f, lw, a);
            int used = 0;
            for (int d = -40; d <= 40; d += 10)
            {
                float tick = Mathf.Ceil((deg + d) / 10f) * 10f - deg;
                if (tick > 40f) continue;
                float x = cx + tick / 40f * span;
                int at = Mathf.RoundToInt(Mathf.Repeat(deg + tick, 360f)) % 360;
                bool major = at % 30 == 0;
                Outlined(b, x, ticksTop, x, ticksTop - (major ? type * 0.6f : type * 0.3f), lw, faint);
                if (!major || used >= numerals.Length || Mathf.Abs(x - cx) < boxHalf + type * 1.7f) continue;
                TMP_Text n = numerals[used++];
                if (!n.gameObject.activeSelf) n.gameObject.SetActive(true);
                AvText.Set(n, at == 0 ? "N" : at == 90 ? "E" : at == 180 ? "S" : at == 270 ? "W" : (at / 10).ToString("00"));
                n.color = Ink.HasValue ? Ink.Value.WithAlpha(0.6f) : AvInk.Dim;
                AvLay.Place(n, x - type * 1.2f, row, type * 2.4f, line);
            }
            for (int i = used; i < numerals.Length; i++) Hide(numerals[i]);
            AvStrokes.Chevron(b, cx, ticksTop - type * 0.95f, type * 0.35f, 90f, lw + HaloPx, Halo);
            AvStrokes.Chevron(b, cx, ticksTop - type * 0.95f, type * 0.35f, 90f, lw, a);
        }

        private static void Hide(TMP_Text t)
        {
            if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
        }

        // A scrolling ladder on one side (left = ALT in metres, right = SPD in knots) beside the boxed reading: a tick every
        // <paramref name="minor"/>, a long one every <paramref name="majorEvery"/> ticks, <paramref name="window"/> units over the tape.
        private void Tape(AvQuadBuffer b, bool left, float value, float window, float minor, int majorEvery, float lw, Rgba a, Rgba faint)
        {
            float tapeH = h * 0.4f, cy = h * 0.5f, perUnit = tapeH / window;
            float edge = left ? pad : w - pad, dir = left ? 1f : -1f;
            if (big)
            for (float k = Mathf.Ceil((value - window * 0.5f) / minor); k * minor <= value + window * 0.5f; k++)
            {
                float y = cy + (k * minor - value) * perUnit;
                bool major = Mathf.RoundToInt(k) % majorEvery == 0;
                Outlined(b, edge, y, edge + dir * (major ? type * 0.9f : type * 0.45f), y, lw, major ? a : faint);
            }
            // The reading's box, with a pointer into the tape.
            float bw = boxW + type * 0.4f, bh = line * 2f + 4f, by = cy - line - 2f, bx = left ? pad + type * 1.1f : w - pad - type * 1.1f - bw;
            AvStrokes.Fill(b, bx, by, bw, bh, new Rgba(0f, 0f, 0f, 0.45f));
            AvStrokes.Bracket(b, bx, by, bw, bh, type * 0.6f, lw, a);
            Outlined(b, left ? bx : bx + bw, cy, left ? bx - type * 0.5f : bx + bw + type * 0.5f, cy, lw * 1.4f, a);
        }

        // Where the stabilised lens puts the horizon: two bars at the sides, clear of the tapes, when it is in the picture.
        private void Horizon(AvQuadBuffer b, DroneLinkService link, float lw, Rgba a)
        {
            float y = h * 0.5f + DroneLink.HorizonLift(link.LensElevation, link.FieldOfView) * h * 0.5f;
            if (y < bottomBand + type || y > h - topBand - type) return;
            float inner = pad + type * 1.5f + boxW + type * 0.8f, len = type * 2.6f, drop = type * 0.4f;
            Outlined(b, inner, y, inner + len, y, lw * 1.2f, a);
            Outlined(b, inner + len, y, inner + len, y - drop, lw * 1.2f, a);
            Outlined(b, w - inner, y, w - inner - len, y, lw * 1.2f, a);
            Outlined(b, w - inner - len, y, w - inner - len, y - drop, lw * 1.2f, a);
        }

        // The gimbal box: the ball's field of regard (azimuth -180..180 across, elevation 0 at the top to -90 at the bottom), the nose
        // tick and the lens cross.
        private void Gimbal(AvQuadBuffer b, DroneLinkService link, float lw, Rgba faint, Rgba a)
        {
            float gw = type * 5f, gh = type * 2.4f, gx = w * 0.5f - gw * 0.5f, gy = bottomBand + type * 0.7f;
            AvStrokes.Fill(b, gx, gy, gw, gh, new Rgba(0f, 0f, 0f, 0.4f));
            AvStrokes.Bracket(b, gx, gy, gw, gh, type * 0.5f, lw, faint);
            Outlined(b, gx + gw * 0.5f, gy + gh, gx + gw * 0.5f, gy + gh - type * 0.35f, lw, faint);
            float px = gx + (Mathf.Clamp(link.LensAzimuth, -180f, 180f) + 180f) / 360f * gw;
            float py = gy + gh - Mathf.Clamp01(-link.LensElevation / 90f) * gh;
            AvStrokes.Cross(b, px, py, type * 0.35f, lw * 1.3f, a);
        }

        // The designation through the lens (dashed ring until the ball tracks it, then solid red with a snap ring), OWN and the WPT point.
        private void Marks(AvQuadBuffer b, DroneLinkService link, DroneView v, float now, float lw, Rgba a, Color tone)
        {
            if (link.MarkInView(out Vector2 vp, out string name, out float rangeM))
            {
                float x = vp.x * w, y = vp.y * h;
                float half = Mathf.Clamp(h * 0.05f * 8000f / Mathf.Max(rangeM, 1000f), h * 0.035f, h * 0.1f);
                bool locked = link.Tracking;
                Color hotColour = locked ? AvInk.State(AvState.Danger) : AvInk.Ink;
                float width = locked ? lw * 1.8f : lw;
                AvStrokes.Bracket(b, x - half, y - half, half * 2f, half * 2f, half * 0.45f, width + HaloPx, Halo);
                AvStrokes.Bracket(b, x - half, y - half, half * 2f, half * 2f, half * 0.45f, width, hotColour.ToRgba());
                if (!locked) AvStrokes.DashedRing(b, x, y, half * 1.25f, 16, 0.5f, lw, a);
                float u = (now - lockAt) / LockSnap;
                if (locked && u >= 0f && u < 1f) AvStrokes.Ring(b, x, y, half * (1.2f + 1.4f * (1f - u)), 32, lw, hotColour.WithAlpha(1f - u).ToRgba());
                Label(b, markName, name, x - type * 6f, y + half + 2f, type * 12f, hotColour);
                Label(b, markRange, AvNum.Fixed(rangeM / 1000f, 1) + " KM" + (locked ? " · TRK" : ""), x - type * 6f, y - half - 2f - line, type * 12f, AvInk.Ink);
            }
            else { Hide(markName); Hide(markRange); }

            Aircraft own = link.Own;
            if (own != null && link.Project(own.transform.position, out Vector2 op) && Inside(op))
            {
                float x = op.x * w, y = op.y * h;
                AvStrokes.Diamond(b, x, y, type * 0.45f, lw + HaloPx, Halo);
                AvStrokes.Diamond(b, x, y, type * 0.45f, lw, AvInk.Friendly.ToRgba());
                Label(b, ownLabel, "OWN", x + type * 0.7f, y - line * 0.5f, type * 3f, AvInk.Friendly);
            }
            else Hide(ownLabel);

            if (v.Mode == DroneMode.Waypoint)
            {
                Vector3 point = new GlobalPosition(v.Point).ToLocalPosition();
                point.y = Datum.LocalSeaY;
                // The ball stares at the point, so it sits on the cross: mark it only off the centre (WPT printed over the reticle).
                if (link.Project(point, out Vector2 wp) && Inside(wp) && (Mathf.Abs(wp.x - 0.5f) * w > type * 2.5f || Mathf.Abs(wp.y - 0.5f) * h > type * 2.5f))
                {
                    float x = wp.x * w, y = wp.y * h;
                    AvStrokes.Cross(b, x, y, type * 0.5f, lw + HaloPx, Halo);
                    AvStrokes.Cross(b, x, y, type * 0.5f, lw, tone.ToRgba());
                    Label(b, wptLabel, "WPT", x + type * 0.7f, y - line * 0.5f, type * 3f, AvInk.State(AvState.Info));
                    return;
                }
            }
            Hide(wptLabel);
        }

        private bool Inside(Vector2 vp) => vp.x > 0.02f && vp.x < 0.98f && vp.y * h > bottomBand && vp.y * h < h - topBand;

        // A one-line label with its box's bottom-left corner at (x, y) in the ink frame (y up), kept inside the picture, on a dark
        // scrim the width of its words (over a bright cloud deck the bare words vanished, in-game 2026-10-20).
        private void Label(AvQuadBuffer b, TMP_Text t, string text, float x, float y, float width, Color colour)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            AvText.Set(t, text);
            t.color = colour;
            x = Mathf.Clamp(x, 2f, w - width - 2f);
            y = Mathf.Clamp(y, bottomBand, h - topBand - line);
            AvLay.Place(t, x, h - y - line, width, line);
            float words = Mathf.Min(width, t.GetPreferredValues(text, width, line).x) + 6f;
            float left = t.alignment == TextAlignmentOptions.MidlineLeft ? x - 3f : x + (width - words) * 0.5f;
            AvStrokes.Fill(b, left, y, words, line, new Rgba(0f, 0f, 0f, 0.5f));
        }
    }
}

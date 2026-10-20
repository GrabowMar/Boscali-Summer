using BoscaliSummer.Modules.Vanguard.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// The linked drone's video as every drone view shows it (UAV terminal hero, UAV MFD, HUD feed card): the feed picture under
    /// analogue static that thickens as the link weakens, bursts on every switch and fills the screen with no signal; a stabilised
    /// reticle, corner brackets and heading tape; REC lamp, callsign, order, time aloft, HDG / ALT / SPD, link bars, range and bearing,
    /// sensor mode and zoom; and the centre word (ACQUIRING LINK, SIGNAL LOST, NO SIGNAL, NO DRONE LINKED). Cosmetic, client-local.
    /// </summary>
    internal sealed class DroneFeedView : AvPart
    {
        private const float BurstSeconds = 0.6f;
        private readonly Image back;
        private readonly RawImage picture, snow;
        private readonly AvVector ink;
        private readonly TMP_Text topLeft, topRight, heading, alt, speed, bottomLeft, bottomRight, centre;
        private readonly Image recLamp;
        private readonly float type;
        private float w = 320f, h = 180f, pad = 8f, line = 15f;
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
            ink = AvVector.Create(Rect, "Symbology", 200);
            recLamp = AvLay.Solid(Rect, "Rec", Color.red);
            recLamp.raycastTarget = false;
            topLeft = Text("TopLeft", TextAlignmentOptions.TopLeft);
            topRight = Text("TopRight", TextAlignmentOptions.TopRight);
            heading = Text("Heading", TextAlignmentOptions.Top);
            alt = Text("Alt", TextAlignmentOptions.MidlineLeft);
            speed = Text("Speed", TextAlignmentOptions.MidlineRight);
            bottomLeft = Text("BottomLeft", TextAlignmentOptions.BottomLeft);
            bottomRight = Text("BottomRight", TextAlignmentOptions.BottomRight);
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

        public override void Place(AvSlot s)
        {
            base.Place(s);
            w = s.W;
            h = s.H;
            pad = Mathf.Round(h * 0.035f) + 4f;
            line = type + 4f;
            AvLay.Place(recLamp.rectTransform, pad, pad + 3f, type * 0.6f, type * 0.6f);
            AvLay.Place(topLeft, pad + type, pad, w * 0.6f, line * 2f);
            AvLay.Place(topRight, w * 0.4f - pad, pad, w * 0.6f, line * 2f);
            AvLay.Place(heading, w * 0.3f, pad + line * 2.2f, w * 0.4f, line);
            AvLay.Place(alt, pad, h * 0.5f - line, w * 0.3f, line * 2f);
            AvLay.Place(speed, w * 0.7f - pad, h * 0.5f - line, w * 0.3f, line * 2f);
            AvLay.Place(bottomLeft, pad + type * 3.6f, h - pad - line, w * 0.62f, line);
            AvLay.Place(bottomRight, w * 0.5f - pad, h - pad - line, w * 0.5f, line);
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
            float amount = v == null ? (link.Lost ? 0.3f : 0.92f) : Mathf.Pow(1f - quality, 1.5f) * 0.85f;
            if (v != null && link.SinceSwitch < BurstSeconds) amount = Mathf.Max(amount, 1f - link.SinceSwitch / BurstSeconds);
            if (v != null && quality <= 0f) amount = 0.95f;
            snow.enabled = amount > 0.02f;
            if (snow.enabled)
            {
                snow.color = new Color(1f, 1f, 1f, Mathf.Clamp01(amount));
                snow.uvRect = new Rect(Random.value, Random.value, w / 160f, h / 160f);
            }

            Color ink0 = AvInk.Ink, dim = AvInk.Dim;
            Color tone = v == null ? AvInk.State(AvState.Inert) : v.Mode == DroneMode.Hand ? AvInk.State(AvState.Caution)
                : v.Mode == DroneMode.Waypoint ? AvInk.State(AvState.Info) : AvInk.State(AvState.Ready);
            bool blink = ((int)(now * 1.25f) & 1) == 0;
            recLamp.enabled = v != null && blink;
            recLamp.color = AvInk.State(AvState.Danger);

            Words(link, v, quality);
            topLeft.color = heading.color = alt.color = speed.color = bottomLeft.color = bottomRight.color = ink0;
            topRight.color = tone;
            centre.color = link.Lost || (v != null && quality <= 0f) ? AvInk.State(AvState.Danger) : ink0;
            Draw(v, quality, ink0, dim, tone);
        }

        private void Words(DroneLinkService link, DroneView v, float quality)
        {
            if (v == null)
            {
                AvText.Set(topLeft, "UAV LINK");
                AvText.Set(topRight, "");
                AvText.Set(heading, ""); AvText.Set(alt, ""); AvText.Set(speed, "");
                AvText.Set(bottomLeft, "LINK —");
                AvText.Set(bottomRight, link.LookWord + " · " + link.ZoomWord);
                AvText.Set(centre, link.Lost ? "SIGNAL LOST · " + link.LostName : link.Count > 0 ? "NO DRONE LINKED" : "NO DRONE AIRBORNE");
                return;
            }
            AvText.Set(topLeft, "REC  " + v.Callsign + " · " + v.Job);
            AvText.Set(topRight, DroneLink.Word(v.Mode) + "\nT+" + DroneLink.Clock(v.Age));
            AvText.Set(heading, "HDG " + AvNum.Fixed(Mathf.Repeat(v.HeadingDeg, 360f), 0).PadLeft(3, '0'));
            AvText.Set(alt, "ALT\n" + AvNum.Fixed(v.AltM, 0) + " M");
            AvText.Set(speed, "SPD\n" + AvNum.Fixed(v.SpeedMs * 1.944f, 0) + " KT");
            AvText.Set(bottomLeft, "LINK " + AvNum.Fixed(quality * 100f, 0) + "% · " + AvNum.Fixed(v.RangeM / 1000f, 1) + " KM · BRG " +
                AvNum.Fixed(v.BearingDeg, 0).PadLeft(3, '0'));
            AvText.Set(bottomRight, link.LookWord + " · " + link.ZoomWord);
            AvText.Set(centre, quality <= 0f ? "NO SIGNAL · RETURNING TO TASK" : link.SinceSwitch < BurstSeconds ? "ACQUIRING LINK · " + v.Callsign : "");
        }

        private const float HaloPx = 2.4f;
        private static readonly Rgba Halo = new Rgba(0f, 0f, 0f, 0.6f);

        // A light stroke over a wider dark one, a little longer at both ends: the symbology reads on bright haze as on dark sea.
        private static void Outlined(AvQuadBuffer b, float ax, float ay, float bx, float by, float width, Rgba c)
        {
            float dx = bx - ax, dy = by - ay, len = Mathf.Sqrt(dx * dx + dy * dy), e = len > 1e-3f ? HaloPx * 0.5f / len : 0f;
            AvStrokes.Line(b, ax - dx * e, ay - dy * e, bx + dx * e, by + dy * e, width + HaloPx, Halo);
            AvStrokes.Line(b, ax, ay, bx, by, width, c);
        }

        // Symbology in the view's own pixels, y up from the bottom (AvVector's frame).
        private void Draw(DroneView v, float quality, Color ink0, Color dim, Color tone)
        {
            AvQuadBuffer b = ink.Buffer;
            b.Clear();
            // Scrims under the text bands (they draw below the words): white symbology stays legible over a bright cloud deck.
            Rgba shade = new Rgba(0f, 0f, 0f, 0.4f);
            float topBand = pad + line * 3.3f, bottomBand = pad + line * 1.2f, box = pad + type * 4.8f;
            AvStrokes.Fill(b, 0f, h - topBand, w, topBand, shade);
            AvStrokes.Fill(b, 0f, 0f, w, bottomBand, shade);
            AvStrokes.Fill(b, 0f, h * 0.5f - line - 2f, box, line * 2f + 4f, shade);
            AvStrokes.Fill(b, w - box, h * 0.5f - line - 2f, box, line * 2f + 4f, shade);
            Rgba a = ink0.WithAlpha(0.85f).ToRgba(), faint = dim.WithAlpha(0.6f).ToRgba();
            float cx = w * 0.5f, cy = h * 0.5f, arm = h * 0.07f, gap = h * 0.025f, lw = Mathf.Max(1f, h / 240f);
            float inset = Mathf.Round(h * 0.06f), corner = h * 0.08f;
            AvStrokes.Bracket(b, inset, inset, w - 2f * inset, h - 2f * inset, corner, lw + HaloPx, Halo);
            AvStrokes.Bracket(b, inset, inset, w - 2f * inset, h - 2f * inset, corner, lw, faint);
            if (v != null)
            {
                Outlined(b, cx - gap - arm, cy, cx - gap, cy, lw * 1.4f, a);
                Outlined(b, cx + gap, cy, cx + gap + arm, cy, lw * 1.4f, a);
                Outlined(b, cx, cy - gap - arm, cx, cy - gap, lw * 1.4f, a);
                Outlined(b, cx, cy + gap, cx, cy + gap + arm * 0.6f, lw * 1.4f, a);
                AvStrokes.Fill(b, cx - lw - 1.2f, cy - lw - 1.2f, 2f * lw + 2.4f, 2f * lw + 2.4f, Halo);
                AvStrokes.Fill(b, cx - lw, cy - lw, 2f * lw, 2f * lw, a);
                // Heading tape: a tick every 10 degrees within 40 either side, a caret under the centre.
                float tapeY = h - pad - line * 1.3f, span = w * 0.22f;
                for (int d = -40; d <= 40; d += 10)
                {
                    float deg = Mathf.Repeat(v.HeadingDeg, 360f);
                    float tick = Mathf.Ceil((deg + d) / 10f) * 10f - deg;
                    if (tick > 40f) continue;
                    float x = cx + tick / 40f * span;
                    bool major = Mathf.RoundToInt(Mathf.Repeat(deg + tick, 360f)) % 30 == 0;
                    Outlined(b, x, tapeY, x, tapeY - (major ? type * 0.7f : type * 0.35f), lw, faint);
                }
                AvStrokes.Chevron(b, cx, tapeY - type * 0.95f, type * 0.35f, 90f, lw + HaloPx, Halo);
                AvStrokes.Chevron(b, cx, tapeY - type * 0.95f, type * 0.35f, 90f, lw, a);
            }
            // Link bars beside the bottom-left words.
            int bars = v != null ? DroneLink.Bars(quality) : 0;
            float bx = inset * 0.5f + 6f, by = inset * 0.5f + 4f, bw = Mathf.Max(2f, type * 0.28f);
            Rgba lit = tone.ToRgba(), off = dim.WithAlpha(0.35f).ToRgba();
            for (int i = 0; i < 5; i++)
                AvStrokes.Fill(b, bx + i * (bw + 2f), by, bw, type * (0.3f + 0.14f * i), i < bars ? lit : off);
            ink.Commit();
        }
    }
}

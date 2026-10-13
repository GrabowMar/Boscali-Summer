using BoscaliSummer.Modules.DynamicOperations.Domain;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Ui;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.DynamicOperations.Runtime
{
    /// <summary>
    /// One contract's cockpit marker in the visor's own ink: corner brackets on the point (a
    /// double chevron along the bearing while the target is off screen), a dashed area ring
    /// sized and faded with vanilla's formulas, and a 45 degree stub up to a hairline shelf
    /// that carries the title above and the detail below; the shelf thickens from its root as
    /// the hold progresses. Everything is vector strokes and text over a feathered shade: no
    /// plate and no sprite of the game's (its objective sprites are white on opaque black and
    /// paint a black square without the game's additive material). The label glides and nudges
    /// aside when it would overlap a neighbour. One instance per pool slot, fixed for the
    /// scene: contracts rotate through slots, never the other way.
    /// </summary>
    internal sealed class ContractMarker
    {
        private const float LabelOffsetX = 28f;
        private const float LabelOffsetY = 22f;
        private const float LabelWidth = 620f;
        private const float DetailSizeFactor = 0.66f;
        /// <summary>Clear space between the two lines, as a share of the text size: the shelf runs through its middle.</summary>
        private const float GapFactor = 0.16f;
        private const float HeightFactor = 1f + DetailSizeFactor + GapFactor;
        private const float DetailShare = (DetailSizeFactor + GapFactor * 0.5f) / HeightFactor;
        private const float Bracket = 11f;
        private const float Stroke = 1.25f;
        private const float ShadeMargin = 60f;
        private const float ShadeAlpha = 0.3f;
        private const float RingInk = 0.85f;
        private const float MinRingPixels = 16f;
        private const float MaxRingPixels = 2600f;
        private static readonly Rgba White = new Rgba(1f, 1f, 1f, 1f);

        private readonly RectTransform root;
        private readonly RectTransform ring;
        private readonly RectTransform label;
        private readonly AvVector ringStrokes;
        private readonly AvVector strokes;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI detail;

        private string titleCache = string.Empty;
        private float labelWidth = LabelWidth;
        private string detailCache;
        private int detailCard = -1;
        private int detailMetres = int.MinValue;
        private int detailSeconds = int.MinValue;
        private bool detailMetric;
        private bool detailInside;
        private Color detailTone;
        private float textSize;
        private float labelScale;
        private float labelX, labelY;
        private float targetX, targetY;
        private float nudgeX, nudgeY;
        private float ringPixels;
        private float bearing, progress;
        private bool offScreen;
        private bool flipped;
        private bool snap;

        internal ContractMarker(RectTransform parent, string name, VanillaHudStyle.CockpitStyle style)
        {
            textSize = VanillaHudStyle.OverlayTextSize;

            root = Child(parent, name);
            root.anchoredPosition = Vector2.zero;

            ring = Child(root, name + " Ring");
            ringStrokes = AvVector.Create(ring, "Strokes", 200);
            ring.gameObject.SetActive(false);

            strokes = AvVector.Create(root, name + " Strokes", 24);

            label = Child(root, name + " Label");
            label.pivot = new Vector2(0f, DetailShare);
            label.sizeDelta = new Vector2(LabelWidth, textSize * HeightFactor);
            label.anchoredPosition = new Vector2(LabelOffsetX, LabelOffsetY);
            labelScale = style.LabelScale > 0.01f ? style.LabelScale : 0.5f;
            label.localScale = new Vector3(labelScale, labelScale, 1f);
            Image shade = HudSprites.Child(label, name + " Shade", HudSprites.Feather, sliced: true);
            shade.rectTransform.offsetMin = new Vector2(-ShadeMargin, -ShadeMargin);
            shade.rectTransform.offsetMax = new Vector2(ShadeMargin, ShadeMargin);
            shade.color = HudSprites.ShadeTint.WithAlpha(ShadeAlpha);
            title = Text(label, name + " Title", style, textSize);
            detail = Text(label, name + " Detail", style, textSize * DetailSizeFactor);
            detail.rectTransform.anchorMin = detail.rectTransform.anchorMax = new Vector2(0f, 0f);
            detail.rectTransform.pivot = new Vector2(0f, 0f);

            labelX = targetX = LabelOffsetX;
            labelY = targetY = LabelOffsetY;
            SetVisible(false);
        }

        /// <summary>Is this slot currently drawing a contract?</summary>
        internal bool Visible => root.gameObject.activeSelf;

        /// <summary>The label's resting point: vanilla's anti-overlap tests these, not the drifting points.</summary>
        internal Vector2 LabelAnchor => root.anchoredPosition + new Vector2(targetX, targetY);

        internal void SetVisible(bool visible)
        {
            if (root.gameObject.activeSelf == visible) return;
            root.gameObject.SetActive(visible);
            if (!visible) return;
            snap = true;
            nudgeX = 0f;
            nudgeY = 0f;
        }

        /// <summary>The projected point, in canvas space: the brackets stay glued here.</summary>
        internal void Place(float x, float y) => root.anchoredPosition = new Vector2(x, y);

        /// <summary>
        /// Brackets on a point inside the frame; a chevron along the bearing from the screen
        /// centre to the (pre-clamp) point while the marker is pinned to the frame edge.
        /// </summary>
        internal void SetPointer(float bearingDegrees, bool clamped)
        {
            bearing = bearingDegrees;
            offScreen = clamped;
        }

        /// <summary>
        /// The area ring: full diameter from the angular scale, vanilla's fade, and roll-locked
        /// to the camera so the dashes hold still against the world. The ring is dropped while
        /// the marker is clamped to the frame edge. Its strokes are rebuilt only when its size
        /// moves by half a unit; the fade is a tint.
        /// </summary>
        internal void SetRing(float radius, float range, float canvasHeight, Camera camera, bool clamped, Color ink)
        {
            float alpha = clamped ? 0f : OperationMarkerCopy.RingAlpha(radius, range);
            float pixels = alpha <= 0.0001f
                ? 0f
                : 2f * radius * ContractMarkerMath.PixelsPerMetre(canvasHeight, camera.fieldOfView, range);
            bool show = pixels >= MinRingPixels && pixels <= MaxRingPixels;
            if (ring.gameObject.activeSelf != show) ring.gameObject.SetActive(show);
            if (!show) return;
            if (Mathf.Abs(pixels - ringPixels) > 0.5f)
            {
                ringPixels = pixels;
                float r = pixels * 0.5f;
                AvQuadBuffer b = ringStrokes.Buffer;
                b.Clear();
                AvStrokes.DashedRing(b, 0f, 0f, r, r < 60f ? 24 : r < 240f ? 48 : 96, 0.5f, Stroke, White);
                AvStrokes.Line(b, r + 3f, 0f, r + 10f, 0f, Stroke, White);
                AvStrokes.Line(b, -r - 3f, 0f, -r - 10f, 0f, Stroke, White);
                AvStrokes.Line(b, 0f, r + 3f, 0f, r + 10f, Stroke, White);
                AvStrokes.Line(b, 0f, -r - 3f, 0f, -r - 10f, Stroke, White);
                ringStrokes.Commit();
            }
            ringStrokes.color = ink.WithAlpha(alpha * RingInk);
            ring.localRotation = Quaternion.Euler(0f, 0f, -camera.transform.eulerAngles.z);
        }

        /// <summary>
        /// Title and detail, cached until they change. The label hangs up and to the right of
        /// the point and flips left when it would leave the frame. Text size is the player's
        /// vanilla overlay size; the title is the theme's ink, the strokes and the detail line
        /// take the contract's tone.
        /// </summary>
        internal void SetLabel(ContractCard card, float distance, bool metric, float x, float halfWidth,
            bool clamped, Color ink, Color tone)
        {
            bool changed = false;
            string titleText = card.TitleLine;
            if (titleCache != titleText)
            {
                titleCache = titleText;
                title.text = titleText;
                title.ForceMeshUpdate();
                changed = true;
            }

            // The detail line carries a live distance and clock, so it is rebuilt only when one
            // of its inputs actually moved: a step of the displayed range, a second of clock, a
            // new tone or a new contract.
            int metres = OperationMarkerCopy.DistanceStep(distance, metric);
            bool inside = card.Inside(distance);
            int seconds = Mathf.CeilToInt(card.Seconds);
            if (detailCache == null || detailCard != card.Id || detailMetric != metric ||
                detailTone != tone || metres != detailMetres || inside != detailInside || seconds != detailSeconds)
            {
                detailCard = card.Id;
                detailMetric = metric;
                detailTone = tone;
                detailInside = inside;
                detailMetres = metres;
                detailSeconds = seconds;
                detailCache = card.Detail(distance, metric);
                detail.text = detailCache;
                detail.ForceMeshUpdate();
                changed = true;
            }

            title.color = ink;
            detail.color = tone;
            strokes.color = tone;
            progress = inside ? Mathf.Clamp01(card.Progress) : 0f;

            if (changed) labelWidth = Mathf.Max(title.preferredWidth, detail.preferredWidth);
            float width = labelWidth;
            float visual = width * labelScale;
            if (!Mathf.Approximately(width, label.sizeDelta.x))
                label.sizeDelta = new Vector2(width, label.sizeDelta.y);
            bool rightEdge = clamped && x >= halfWidth - 0.5f;
            bool wantFlip = rightEdge || x + LabelOffsetX + visual > halfWidth;
            if (wantFlip != flipped)
            {
                flipped = wantFlip;
                label.pivot = new Vector2(wantFlip ? 1f : 0f, DetailShare);
                snap = true;
            }
            targetX = wantFlip ? -LabelOffsetX : LabelOffsetX;
            targetY = LabelOffsetY;
        }

        /// <summary>One anti-overlap step against another label: dx,dy = other − this.</summary>
        internal void Nudge(float dx, float dy, float deltaTime) =>
            ContractMarkerLook.Step(ref nudgeX, ref nudgeY, dx, dy, deltaTime);

        /// <summary>The vanilla nudge decay, for a frame with no near neighbour to push against.</summary>
        internal void DecayNudge()
        {
            nudgeX *= ContractMarkerLook.NudgeDecay;
            nudgeY *= ContractMarkerLook.NudgeDecay;
        }

        /// <summary>Glide the label a fifth of the way toward its anchor plus nudge, and draw the strokes that tie it to the point.</summary>
        internal void GlideLabel()
        {
            if (snap)
            {
                labelX = targetX + nudgeX;
                labelY = targetY + nudgeY;
                snap = false;
            }
            else
            {
                labelX = ContractMarkerLook.Glide(labelX, targetX + nudgeX);
                labelY = ContractMarkerLook.Glide(labelY, targetY + nudgeY);
            }
            label.anchoredPosition = new Vector2(labelX, labelY);
            Draw();
        }

        /// <summary>
        /// The point's mark, the stub and the shelf, in the root's own units from the point (y up).
        /// The buffer's hash makes a frame with nothing moved a no-op.
        /// </summary>
        private void Draw()
        {
            AvQuadBuffer b = strokes.Buffer;
            b.Clear();
            if (offScreen)
            {
                float rad = bearing * Mathf.Deg2Rad;
                AvStrokes.Chevron(b, 0f, 0f, 12f, bearing, Stroke + 0.5f, White);
                AvStrokes.Chevron(b, -Mathf.Cos(rad) * 7f, -Mathf.Sin(rad) * 7f, 12f, bearing, Stroke, White);
            }
            else
            {
                AvStrokes.Bracket(b, -Bracket, -Bracket, Bracket * 2f, Bracket * 2f, 5f, Stroke, White);
                AvStrokes.Fill(b, -1f, -1f, 2f, 2f, White);
            }

            float side = flipped ? -1f : 1f;
            float shelf = labelWidth * labelScale;
            float from = labelX - side * 4f, to = labelX + side * shelf;
            AvStrokes.Line(b, side * Bracket, Bracket, from, labelY, Stroke, White);
            AvStrokes.Line(b, from, labelY, to, labelY, Stroke, White);
            AvStrokes.Fill(b, to - 1.5f, labelY - 1.5f, 3f, 3f, White);
            if (progress > 0f) AvStrokes.Line(b, from, labelY, from + (to - from) * progress, labelY, 3f, White);
            strokes.Commit();
        }

        /// <summary>The player changed the overlay text size: follow it live.</summary>
        internal void SetTextSize(float size)
        {
            if (size <= 0f || Mathf.Approximately(size, textSize)) return;
            textSize = size;
            title.fontSize = size;
            detail.fontSize = size * DetailSizeFactor;
            title.rectTransform.sizeDelta = new Vector2(LabelWidth, size);
            detail.rectTransform.sizeDelta = new Vector2(LabelWidth, size * DetailSizeFactor);
            label.sizeDelta = new Vector2(label.sizeDelta.x, size * HeightFactor);
        }

        private static TextMeshProUGUI Text(RectTransform parent, string name, VanillaHudStyle.CockpitStyle style,
            float size)
        {
            TextMeshProUGUI label = HudText.Make(parent, name, style.Font, style.FontMaterial,
                TextAlignmentOptions.MidlineLeft, size);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(LabelWidth, size);
            rect.anchoredPosition = Vector2.zero;
            return label;
        }

        private static RectTransform Child(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }
    }
}

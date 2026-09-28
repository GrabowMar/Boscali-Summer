using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    /// <summary>
    /// Small local kit v2 parts (built from <c>AvPart</c>/<c>AvFrame</c>/<c>AvText</c>/<c>AvLay</c>)
    /// that the SQD console's five pages share. Kit v2 has no ready-made equivalent for a wrapped
    /// prose line, a label/value pair or a portrait-with-fallback, so these are built locally per
    /// the P2 brief ("build it locally in your module from kit primitives") rather than invented
    /// inside the shared kit.
    /// </summary>
    internal sealed class AvTextBlock : AvPart
    {
        private readonly TMP_Text text;

        public AvTextBlock(RectTransform parent, AvTextRole role = AvTextRole.ProseSmall)
        {
            Rect = AvLay.Child(parent, "Text");
            text = AvText.Make(Rect, "Text", role, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public Color Color { set => text.color = value; }

        public void Set(string value) { string v = value ?? ""; if (text.text != v) text.text = v; }

        public override float Measure(float width) => Mathf.Max(14f, AvText.Height(text, width));

        public override void Place(AvSlot slot) { base.Place(slot); AvLay.Fill(text.rectTransform); }

        public override void Restyle() =>
            text.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
    }

    /// <summary>
    /// One field: a caps label at left, a value at right. When either side does not fit its
    /// share of the line the field stacks (label above, value below and wrapped) instead of
    /// shrinking or clipping. Replaces the v1 FormRow.
    /// </summary>
    internal sealed class AvKeyValue : AvPart
    {
        private const float Line = 20f;
        private readonly TMP_Text key, value;
        private AvState state = AvState.Inert;
        private bool stacked;

        public AvKeyValue(RectTransform parent, string keyText)
        {
            Rect = AvLay.Child(parent, "Field " + keyText);
            key = AvText.Make(Rect, "Key", AvTextRole.Label, keyText ?? "");
            value = AvText.Make(Rect, "Value", AvTextRole.Data, "—", TextAlignmentOptions.MidlineRight);
            Restyle();
        }

        public void Set(string v, AvState st = AvState.Inert)
        {
            string body = string.IsNullOrEmpty(v) ? "—" : v;
            if (value.text != body) value.text = body;
            if (st != state) { state = st; Restyle(); }
        }

        private bool NeedsStack(float width)
        {
            float kw = width * 0.46f;
            return AvText.Width(value) > width - kw - 2f || AvText.Width(key) > kw - 2f;
        }

        public override float Measure(float width)
        {
            stacked = NeedsStack(width);
            return stacked ? Line + Mathf.Max(Line - 4f, AvText.Height(value, width)) + 4f : AvGridTokens.RowDense;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            stacked = NeedsStack(s.W);
            value.enableWordWrapping = stacked;
            if (stacked)
            {
                AvLay.Place(key.rectTransform, 0f, 0f, s.W, Line);
                AvLay.Place(value.rectTransform, 0f, Line, s.W, Mathf.Max(0f, s.H - Line));
                value.alignment = TextAlignmentOptions.TopRight;
            }
            else
            {
                float kw = s.W * 0.46f;
                AvLay.Place(key.rectTransform, 0f, 0f, kw, s.H);
                AvLay.Place(value.rectTransform, kw, 0f, s.W - kw, s.H);
                value.alignment = TextAlignmentOptions.MidlineRight;
            }
        }

        public override void Restyle()
        {
            key.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            value.color = state == AvState.Inert
                ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-value").Color, AvTheme.TextPrimary)
                : AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-value " + AvStates.Class(state)).Color, AvTheme.TextPrimary);
        }
    }

    /// <summary>A small read-only stat: caption over a mono value. Used for compact tile rows.</summary>
    internal sealed class AvStatTile : AvPart
    {
        private readonly AvFrame frame;
        private readonly TMP_Text caption, value;

        public AvStatTile(RectTransform parent, string captionText)
        {
            Rect = AvLay.Child(parent, "Tile " + captionText);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(5f)); AvLay.Fill(frame.rectTransform);
            caption = AvText.Make(Rect, "Caption", AvTextRole.Micro, captionText ?? "");
            value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "—");
            AvText.Fit(caption, false); AvText.Fit(value, false);
            Restyle();
        }

        public void Set(string v) { string body = string.IsNullOrEmpty(v) ? "—" : v; if (value.text != body) value.text = body; }

        public override float Measure(float width) => AvGridTokens.ToolCell;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(caption.rectTransform, 6f, 4f, s.W - 12f, 14f);
            AvLay.Place(value.rectTransform, 6f, 19f, s.W - 12f, s.H - 23f);
        }

        public override void Restyle()
        {
            frame.Paint(AvStyleHost.Resolve(AvStyleHost.FuiStyle("card inert").Background, AvTheme.SurfaceInert),
                AvStyleHost.Resolve(AvStyleHost.FuiStyle("card inert").Border, AvTheme.Hairline));
            caption.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-caption").Color, AvTheme.Dim);
            value.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-value").Color, AvTheme.TextPrimary);
        }
    }

    /// <summary>A framed portrait box: an <see cref="Image"/> with a NO VISUAL fallback word.</summary>
    internal sealed class AvPortrait : AvPart
    {
        private readonly AvFrame frame;
        private readonly Image image;
        private readonly TMP_Text fallback;

        public AvPortrait(RectTransform parent, string name, string fallbackWord = "NO VISUAL")
        {
            Rect = AvLay.Child(parent, "Portrait " + name);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(4f)); AvLay.Fill(frame.rectTransform);
            var go = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            image = go.AddComponent<Image>();
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false;
            fallback = AvText.Make(Rect, "Fallback", AvTextRole.Micro, fallbackWord, TextAlignmentOptions.Center, true);
            Restyle();
        }

        public void Set(Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;
            fallback.gameObject.SetActive(sprite == null);
        }

        public override float Measure(float width) => AvGridTokens.Metric;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place((RectTransform)image.transform, 3f, 3f, s.W - 6f, s.H - 6f);
            AvLay.Place(fallback.rectTransform, 4f, 4f, s.W - 8f, s.H - 8f);
        }

        public override void Restyle()
        {
            frame.Paint(AvStyleHost.Resolve(AvStyleHost.FuiStyle("card inert").Background, AvTheme.SurfaceInert),
                AvStyleHost.Resolve(AvStyleHost.FuiStyle("card inert").Border, AvTheme.Hairline));
            fallback.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-caption").Color, AvTheme.Dim);
        }
    }

    /// <summary>
    /// The SQD screen's own mounting geometry: how tall the bezel's spare glass lets the panel
    /// grow, and nudging the built panel back inside its canvas. Both are pure <c>RectTransform</c>
    /// arithmetic with no v1 kit dependency; copied locally because the v1 screen and kit helpers
    /// are the very v1 entry points this slice retires (kit gap: consider promoting these two to a
    /// neutral kit v2 helper — nothing about them is v1-specific).
    /// </summary>
    internal static class SqdScreenMount
    {
        public static float ResolveHeight(RectTransform parent, float min, float max)
        {
            if (max < min) max = min;
            if (parent == null) return min;

            float available = parent.rect.height;
            RectTransform cursor = parent;
            for (int i = 0; i < 4 && available <= 1f && cursor != null; i++)
            {
                cursor = cursor.parent as RectTransform;
                if (cursor != null) available = cursor.rect.height;
            }

            if (available <= 1f) return min;
            return Mathf.Clamp(Mathf.Floor(available), min, max);
        }

        public static void ClampIntoCanvas(RectTransform panel, float margin = 8f)
        {
            if (panel == null) return;
            Canvas canvas = panel.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRt = canvas.rootCanvas.transform as RectTransform;
            if (canvasRt == null || panel.parent == null) return;

            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = canvasRt.InverseTransformPoint(corners[i]);
                if (local.x < minX) minX = local.x;
                if (local.x > maxX) maxX = local.x;
                if (local.y < minY) minY = local.y;
                if (local.y > maxY) maxY = local.y;
            }

            Rect bounds = canvasRt.rect;
            float dx = 0f;
            if (minX < bounds.xMin + margin) dx = bounds.xMin + margin - minX;
            else if (maxX > bounds.xMax - margin) dx = bounds.xMax - margin - maxX;

            float dy = 0f;
            if (maxY > bounds.yMax - margin) dy = bounds.yMax - margin - maxY;
            else if (minY < bounds.yMin + margin) dy = bounds.yMin + margin - minY;

            if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f)) return;

            Vector3 world = canvasRt.TransformVector(new Vector3(dx, dy, 0f));
            Vector3 local2 = panel.parent.InverseTransformVector(world);
            panel.anchoredPosition += new Vector2(local2.x, local2.y);
        }
    }
}

using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NOAvionics
{
    public sealed class AvSlider : AvPart
    {
        private readonly TMP_Text label, value;
        private readonly AvGaugeGraphic track;
        private readonly Func<float> get;
        private readonly Action<float> set;
        private readonly Func<string> text;

        public AvSlider(RectTransform parent, string labelText, Func<float> get01, Action<float> set01, Func<string> valueText)
        {
            Rect = AvLay.Child(parent, "Slider " + labelText);
            get = get01; set = set01; text = valueText;
            label = AvText.Make(Rect, "Label", AvTextRole.Label, labelText, TextAlignmentOptions.MidlineLeft, true);
            value = AvText.Make(Rect, "Value", AvTextRole.Data, "", TextAlignmentOptions.MidlineRight);
            AvText.Fit(value, false); // long values shrink into their 40% box, never spill
            var go = new GameObject("Track", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            track = go.AddComponent<AvGaugeGraphic>();
            track.Shape = AvGaugeShape.Segments; track.Segments = 20; track.SegmentGap = 2f;
            track.ValueMarker = true;
            track.raycastPadding = new Vector4(0f, -8f, 0f, -8f);
            var drag = go.AddComponent<Drag>();
            drag.Owner = this;
            track.raycastTarget = true;
            Refresh();
            Restyle();
        }

        public void Refresh()
        {
            track.Value = get?.Invoke() ?? 0f;
            string v = text?.Invoke() ?? AvNum.Percent(track.Value);
            if (value.text != v) value.text = v;
        }

        private void Pointer(PointerEventData e)
        {
            RectTransform r = (RectTransform)track.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(r, e.position, e.pressEventCamera, out Vector2 local)) return;
            set?.Invoke(Mathf.Clamp01((local.x - r.rect.xMin) / Mathf.Max(1f, r.rect.width)));
            Refresh();
        }

        public override float Measure(float width) => Mathf.Max(AvGridTokens.Row + 18f, AvText.Height(label, width * 0.6f - 8f) + 26f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(label.rectTransform, 0f, 0f, Mathf.Max(0f, s.W * 0.6f - 8f), s.H - 26f);
            AvLay.Place(value.rectTransform, s.W * 0.6f, 0f, s.W * 0.4f, 20f);
            AvLay.Place((RectTransform)track.transform, 0f, s.H - 16f, s.W, 8f);
        }

        public override void Restyle()
        {
            label.color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            value.color = AvStyleHost.FuiInk("row-value", AvTheme.TextPrimary);
            track.Track = AvStyleHost.FuiFill("gauge-track", AvTheme.Hairline);
            track.FillColor = AvStyleHost.FuiColor("select", AvTheme.Accent);
            track.FillEnd = Color.Lerp(track.FillColor, AvTheme.TextPrimary, 0.35f);
            track.SetVerticesDirty();
        }

        private sealed class Drag : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public AvSlider Owner;
            public void OnPointerDown(PointerEventData e) { if (e.button != PointerEventData.InputButton.Left) return; Owner.Pointer(e); AvUiSound.Play(AvUiCue.Press); }
            public void OnDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Owner.Pointer(e); }
        }
    }
}

using System;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    internal static class FeedInk
    {
        internal static Color Class(ProbableClass c) =>
            c == ProbableClass.Hostile ? OpsInk.Rail(AvState.Danger)
            : c == ProbableClass.Friendly ? OpsInk.Rail(AvState.Ready)
            : c == ProbableClass.Neutral ? OpsInk.Rail(AvState.Info) : OpsInk.Rail(AvState.Caution);
    }

    /// <summary>A contact bracket over the picture: a 36 px click target around a small outline and a label.</summary>
    internal sealed class FeedBracket
    {
        private const float Hit = 36f, Box = 22f;
        private readonly RectTransform root;
        private readonly AvFrame outline;
        private readonly TMP_Text label;
        private readonly Image hitArea;
        private int id;
        private bool selected, marked;
        private ProbableClass cls;

        public FeedBracket(RectTransform parent, int index, Action<int> select)
        {
            root = AvLay.Child(parent, "Bracket " + index);
            AvLay.Place(root, 0f, 0f, Hit, Hit);
            hitArea = AvLay.Solid(root, "Hit", Color.clear);
            AvLay.Fill(hitArea.rectTransform);
            hitArea.raycastTarget = true;
            AvHit hit = hitArea.gameObject.AddComponent<AvHit>();
            hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) select(id); };
            outline = AvFrame.Add(root, "Outline", default(AvChamfer));
            outline.Fill = false;
            outline.Stroke = 1.4f;
            outline.Bracket = 5f;
            outline.raycastTarget = false;
            AvLay.Place(outline.rectTransform, (Hit - Box) * 0.5f, (Hit - Box) * 0.5f, Box, Box);
            label = OpsText.Line(root, "Label", AvTextRole.Micro, TextAlignmentOptions.Top);
            label.fontSizeMin = AvTokens.FontMicro;
            AvLay.Place(label.rectTransform, -30f, Hit - 6f, Hit + 60f, 14f);
            label.enableWordWrapping = false;
            root.gameObject.SetActive(false);
        }

        public void Hide() { if (root.gameObject.activeSelf) root.gameObject.SetActive(false); }

        public void Paint(in FeedBracketView b, float x, float y)
        {
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            id = b.Id; selected = b.Selected; marked = b.Marked; cls = b.Class;
            AvLay.Place(root, x - Hit * 0.5f, y - Hit * 0.5f, Hit, Hit);
            AvText.Set(label, b.Label);
            Restyle();
        }

        public void Restyle()
        {
            if (outline == null) return;
            Color ink = selected ? AvInk.Select : marked ? OpsInk.Rail(AvState.Ready) : FeedInk.Class(cls);
            outline.StrokeColor = ink;
            outline.BracketColor = ink;
            outline.Stroke = selected || marked ? 2.2f : 1.4f;
            outline.SetVerticesDirty();
            label.color = selected ? AvInk.Select : AvInk.Ink;
        }
    }

    /// <summary>The sensor picture: the satellite image cropped to the part's box, the host-revealed contact brackets and a status line.</summary>
    internal sealed class OpsFeedPicture : AvPart
    {
        private const float StatusH = 18f;
        private readonly Image back, statusBack;
        private readonly RawImage image;
        private readonly TMP_Text refusal, status;
        private readonly FeedBracket[] brackets = new FeedBracket[SpaceFeedView.MaxBrackets];
        private AvState statusTone = AvState.Info;

        public OpsFeedPicture(RectTransform parent, Action<int> select)
        {
            Rect = AvLay.Child(parent, "Picture");
            Rect.gameObject.AddComponent<RectMask2D>();
            back = AvLay.Solid(Rect, "Back", Color.black);
            AvLay.Fill(back.rectTransform);
            back.raycastTarget = true; // a miss never reaches the map behind
            var go = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            image = go.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.enabled = false;
            AvLay.Fill(image.rectTransform);
            refusal = AvText.Make(Rect, "Refusal", AvTextRole.Prose, "", TextAlignmentOptions.Center, true);
            AvLay.Fill(refusal.rectTransform, 12f);
            statusBack = AvLay.Solid(Rect, "StatusBack", Color.clear);
            statusBack.raycastTarget = false;
            status = C2Kit.Mono(Rect, "Status", 10f, TextAlignmentOptions.MidlineLeft);
            for (int i = 0; i < brackets.Length; i++) brackets[i] = new FeedBracket(Rect, i, select);
            Restyle();
        }

        public override float Measure(float width) => Mathf.Round(width * 0.62f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(statusBack.rectTransform, 0f, s.H - StatusH, s.W, StatusH);
            AvLay.Place(status, 6f, s.H - StatusH, s.W - 12f, StatusH);
        }

        public void Paint(SpaceFeedView v)
        {
            bool show = v.Image != null && v.ImageKind != FeedImageKind.None && v.Refusal.Length == 0;
            if (image.enabled != show) image.enabled = show;
            AvText.Set(refusal, show ? "" : v.Refusal);
            AvText.Set(status, v.Status ?? "");
            if (statusTone != v.StatusTone) { statusTone = v.StatusTone; Restyle(); }
            if (!show) { foreach (FeedBracket b in brackets) b.Hide(); return; }
            if (image.texture != v.Image) image.texture = v.Image;
            Rect area = Rect.rect;
            float aspect = area.height > 1f ? area.width / area.height : 1.6f;
            SpaceFeedRules.CoverCrop(aspect, v.ImageAspect, out float u0, out float v0, out float uw, out float vh);
            image.uvRect = new Rect(u0, v0, uw, vh);
            int shown = 0;
            for (int i = 0; i < v.Brackets.Count && shown < brackets.Length; i++)
            {
                FeedBracketView b = v.Brackets[i];
                if (!SpaceFeedRules.CropPoint(b.U, b.V, u0, v0, uw, vh, out float ax, out float ay)) continue;
                brackets[shown++].Paint(b, ax * area.width, (1f - ay) * area.height);
            }
            for (int i = shown; i < brackets.Length; i++) brackets[i].Hide();
        }

        public override void Restyle()
        {
            back.color = AvStyleHost.FuiColor("ground", Color.black);
            refusal.color = OpsInk.Word(AvState.Caution);
            statusBack.color = OpsInk.A(AvStyleHost.FuiColor("ground", Color.black), 0.72f);
            status.color = OpsInk.Word(statusTone == AvState.Inert ? AvState.Info : statusTone);
            foreach (FeedBracket b in brackets) b?.Restyle();
        }
    }
}

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

    /// <summary>A contact bracket over the DOWNLINK picture: a 36 px click target around a small outline and a label (the lock wears red).</summary>
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

        public void Paint(in FeedBracketView b, float x, float y, float pictureW)
        {
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            id = b.Id; selected = b.Selected; marked = b.Marked; cls = b.Class;
            AvLay.Place(root, x - Hit * 0.5f, y - Hit * 0.5f, Hit, Hit);
            // The name stays centred under its bracket but slides inside the picture near an edge instead of being clipped by the mask.
            float lw = Hit + 60f, left = x - Hit * 0.5f;
            AvLay.Place(label.rectTransform, Mathf.Clamp(-30f, 2f - left, Mathf.Max(2f - left, pictureW - 2f - lw - left)), Hit - 6f, lw, 14f);
            AvText.Set(label, b.Label);
            Restyle();
        }

        public void Restyle()
        {
            if (outline == null) return;
            Color ink = selected ? OpsInk.Rail(AvState.Danger) : marked ? OpsInk.Rail(AvState.Ready) : FeedInk.Class(cls); // the lock is red
            outline.StrokeColor = ink;
            outline.BracketColor = ink;
            outline.Stroke = selected || marked ? 2.2f : 1.4f;
            outline.SetVerticesDirty();
            label.color = selected ? OpsInk.Word(AvState.Danger) : AvInk.Ink;
        }
    }
}

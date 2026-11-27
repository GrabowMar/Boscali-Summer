using System;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The empty roster shows the current requisition and the actions available to this pilot.</summary>
    internal sealed class WmcRequisitionCard : AvPart
    {
        private const float Padding = 14f, Gap = 8f, IconSize = 50f, ButtonHeight = 42f;
        private readonly AvFrame frame;
        private readonly Image icon;
        private readonly TMP_Text title, price, detail;
        private float placedWidth;

        public AvControl Call { get; }
        public AvControl Supply { get; }

        public WmcRequisitionCard(RectTransform parent, Action call, Action supply)
        {
            Rect = AvLay.Child(parent, "RequisitionCard");
            frame = WmcDraw.FillFrame(Rect, AvChamfer.Diagonal(6f));
            frame.raycastTarget = false;
            icon = AvLay.Solid(Rect, "AirframeIcon", Color.white);
            icon.preserveAspect = true;
            icon.enabled = false;
            title = AvText.Make(Rect, "Airframe", AvTextRole.Headline, "", TextAlignmentOptions.TopLeft, true);
            price = AvText.Make(Rect, "Quote", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight, true);
            AvText.Size(price, AvTypeScale.Of(AvTextRole.Title).Size);
            detail = AvText.Make(Rect, "LaunchFieldOrBlocker", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
            AvText.Size(detail, 16f);
            Call = AvControl.Make(Rect, new AvControl.Spec("CALL", call, AvButtonStyle.Primary));
            Call.SetLabelRole(AvTextRole.Title);
            Call.Help = "Call one wingman using your current supply selection.";
            Call.gameObject.SetActive(false);
            Supply = AvControl.Make(Rect, new AvControl.Spec("OPEN SUPPLY", supply, AvButtonStyle.Primary));
            Supply.SetLabelRole(AvTextRole.Title);
            Supply.Help = "View available aircraft, pilots, bases and equipment.";
            Restyle();
        }

        public void Set(string airframe, string quote, string locationOrReason, Sprite silhouette, bool ready)
        {
            bool changed = AvText.Set(title, airframe);
            changed |= AvText.Set(price, quote);
            changed |= AvText.Set(detail, locationOrReason);
            if (icon.sprite != silhouette)
            {
                icon.sprite = silhouette;
                icon.enabled = silhouette != null;
                changed = true;
            }
            if (Call.gameObject.activeSelf != ready)
            {
                Call.gameObject.SetActive(ready);
                Supply.SetStyle(ready ? AvButtonStyle.Default : AvButtonStyle.Primary);
                changed = true;
            }
            if (!changed) return;
            if (placedWidth > 0f) Layout(placedWidth, true);
            Changed();
        }

        public override float Measure(float width) => Layout(width, false);

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            placedWidth = slot.W;
            Layout(slot.W, true);
        }

        private float Layout(float width, bool place)
        {
            float inner = Mathf.Max(1f, width - 2f * Padding);
            bool showIcon = icon.sprite != null && inner >= 300f;
            float inset = showIcon ? IconSize + Gap : 0f;
            float textWidth = Mathf.Max(1f, inner - inset);
            float priceWidth = Mathf.Min(AvText.Width(price) + 2f, textWidth * 0.45f);
            bool hasQuote = price.text.Length > 0;
            bool stackQuote = hasQuote && textWidth < 190f;
            float titleWidth = hasQuote && !stackQuote ? textWidth - priceWidth - Gap : textWidth;
            float titleHeight = Mathf.Max(28f, AvText.Height(title, titleWidth));
            float quoteHeight = hasQuote ? Mathf.Max(22f, AvText.Height(price, stackQuote ? textWidth : priceWidth)) : 0f;
            float headingHeight = stackQuote ? titleHeight + quoteHeight : Mathf.Max(titleHeight, quoteHeight);
            float detailHeight = Mathf.Max(20f, AvText.Height(detail, textWidth));
            float summaryHeight = Mathf.Max(showIcon ? IconSize : 0f, headingHeight + 3f + detailHeight);
            float actionY = Padding + summaryHeight + 12f;
            float buttonWidth = Call.gameObject.activeSelf ? (inner - Gap) * 0.5f : inner;
            float buttonHeight = Mathf.Max(ButtonHeight, Supply.PreferredHeight(buttonWidth));
            if (place)
            {
                icon.enabled = showIcon;
                if (showIcon) AvLay.Place(icon.rectTransform, Padding, Padding + (summaryHeight - IconSize) * 0.5f, IconSize, IconSize);
                float left = Padding + inset;
                AvLay.Place(title.rectTransform, left, Padding, titleWidth, titleHeight);
                AvLay.Place(price.rectTransform, stackQuote ? left : width - Padding - priceWidth, stackQuote ? Padding + titleHeight : Padding,
                    stackQuote ? textWidth : priceWidth, stackQuote ? quoteHeight : headingHeight);
                AvLay.Place(detail.rectTransform, left, Padding + headingHeight + 3f, textWidth, detailHeight);
                if (Call.gameObject.activeSelf) AvLay.Place(Call.Rect, Padding, actionY, buttonWidth, buttonHeight);
                AvLay.Place(Supply.Rect, width - Padding - buttonWidth, actionY, buttonWidth, buttonHeight);
            }
            return actionY + buttonHeight + Padding;
        }

        public override void Restyle()
        {
            WmcDraw.PaintCard(frame, "card", AvTheme.Surface);
            title.color = AvInk.Ink;
            price.color = AvInk.State(AvState.Ready);
            icon.color = AvInk.Ink;
            detail.color = AvInk.Dim;
            Call.Restyle();
            Supply.Restyle();
        }
    }
}

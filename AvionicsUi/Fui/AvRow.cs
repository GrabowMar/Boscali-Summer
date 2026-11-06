using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>List row: state rail · name / sub (wraps) · mono value · up to 3 trailing controls.</summary>
    public sealed class AvRow : AvPart
    {
        private const float PadX = 10f, PadY = 6f, ValueW = 88f, TrailW = 56f, ColumnGap = 8f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text name, sub, value;
        private readonly AvHit hit;
        private RawImage badge;
        private const float BadgeW = 22f;
        private readonly List<AvControl> trailing = new List<AvControl>(3);
        private AvState state = AvState.Info;
        private bool hover, armed, badgeSlot, interactable = true;
        private AvSlot lastSlot;
        private bool placed;
        private int styledLook = -1, styledGen = -1, styledTrailing = -1;

        public AvRow(RectTransform parent, Action onClick = null)
        {
            Rect = AvLay.Child(parent, "Row");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer)); AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            name = AvText.Make(Rect, "Name", AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
            sub = AvText.Make(Rect, "Sub", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "", TextAlignmentOptions.TopRight);
            AvText.Fit(value, false); // the value column is fixed: long words shrink, never spill
            if (onClick != null)
            {
                hit = AvHit.On(frame);
                hit.Hover = h => { hover = h; Restyle(); };
                hit.Click = e => onClick();
            }
            Restyle();
        }

        /// <summary>Hover help shown in the console footer.</summary>
        public string Help { set { frame.raycastTarget = true; AvHelpTip.Attach(frame.gameObject, value, name); } }

        public bool Armed { get => armed; set { if (armed == value) return; armed = value; Restyle(); } }
        public bool Interactable
        {
            get => interactable;
            set { if (interactable == value) return; interactable = value; if (hit != null) hit.Interactable = value; Restyle(); }
        }

        public void Set(string n, string s, string v, AvState st = AvState.Info)
        {
            bool grew = false;
            if (name.text != (n ?? "")) { name.text = n ?? ""; grew = true; }
            if (sub.text != (s ?? "")) { sub.text = s ?? ""; grew = true; }
            if (value.text != (v ?? ""))
            {
                // The value column only narrows the text when it appears or disappears.
                grew |= value.text.Length == 0 || string.IsNullOrEmpty(v);
                value.text = v ?? "";
            }
            if (st != state) { state = st; Restyle(); }
            if (!grew) return;
            // Re-arrange inside the current slot right away: a value that appears without changing the row's
            // height would otherwise never be placed (the flow only re-lays when the height moves).
            if (placed) Place(lastSlot);
            Changed();
        }

        public AvControl AddTrailing(AvControl.Spec spec)
        {
            if (trailing.Count >= 3) return null;
            AvControl c = AvControl.Make(Rect, spec);
            trailing.Add(c);
            LayoutChanged();
            return c;
        }

        private float BadgeSpace => BadgeSlot || (badge != null && badge.enabled) ? BadgeW + 6f : 0f;

        /// <summary>Reserve the badge slot even without an image, so mixed rows align their names.</summary>
        public bool BadgeSlot
        {
            get => badgeSlot;
            set { if (badgeSlot == value) return; float before = BadgeSpace; badgeSlot = value; if (before != BadgeSpace) LayoutChanged(); }
        }

        /// <summary>Optional small image before the name (station logos, portraits). Null hides it.</summary>
        public void SetBadge(Texture texture)
        {
            float before = BadgeSpace;
            bool appears = texture != null && (badge == null || !badge.enabled);
            if (texture != null && badge == null)
            {
                badge = new GameObject("Badge", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<RawImage>();
                badge.transform.SetParent(Rect, false);
                badge.raycastTarget = false;
            }
            if (badge != null) { badge.texture = texture; badge.enabled = texture != null; }
            if (before != BadgeSpace) LayoutChanged();
            else if (appears && placed) Place(lastSlot);
        }

        private void LayoutChanged()
        {
            if (placed) Place(lastSlot);
            Changed();
        }

        private float InlineTextWidth(float width) => width - 2f * PadX - 4f - BadgeSpace -
            (value.text.Length > 0 ? ValueW + ColumnGap : trailing.Count > 0 ? ColumnGap : 0f) - InlineActionsWidth;

        private static float TrailingWidth(AvControl control) => Mathf.Max(TrailW, control.PreferredWidth);

        private float InlineActionsWidth
        {
            get { float width = 0f; foreach (AvControl control in trailing) width += TrailingWidth(control) + 4f; return width; }
        }

        private bool Stacked(float width) => trailing.Count > 0 && InlineTextWidth(width) < 80f;

        private float TextWidth(float width) => Mathf.Max(1f, Stacked(width)
            ? width - 2f * PadX - 4f - BadgeSpace - (value.text.Length > 0 ? ValueW + ColumnGap : 0f)
            : InlineTextWidth(width));

        private float MainHeight(float textWidth)
        {
            float h = AvText.Height(name, textWidth) + (sub.text.Length > 0 ? 2f + AvText.Height(sub, textWidth) : 0f);
            if (value.text.Length > 0) h = Mathf.Max(h, AvText.Height(value, ValueW));
            return Mathf.Max(h, BadgeSpace > 0f ? BadgeW : 0f);
        }

        private float ActionWidth(float width) => AvFlowMath.ColumnWidth(Mathf.Max(0f, width - 2f * PadX - 4f), trailing.Count, 4f);

        private float ActionsHeight(float width, bool stacked)
        {
            float h = 24f;
            foreach (AvControl control in trailing) h = Mathf.Max(h, control.PreferredHeight(stacked ? width : TrailingWidth(control)));
            return trailing.Count > 0 ? h : 0f;
        }

        public override float Measure(float width)
        {
            bool stacked = Stacked(width);
            float main = MainHeight(TextWidth(width)), actions = ActionsHeight(ActionWidth(width), stacked);
            float h = stacked ? main + ColumnGap + actions : Mathf.Max(main, actions);
            return Mathf.Max(AvGridTokens.Row, h + 2f * PadY);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            lastSlot = s; placed = true;
            bool stacked = Stacked(s.W);
            float w = TextWidth(s.W), nh = AvText.Height(name, w), main = MainHeight(w);
            float actionW = ActionWidth(s.W), actionH = ActionsHeight(actionW, stacked);
            AvLay.Place(rail.rectTransform, 0f, 0f, 2f, s.H);
            float x0 = PadX + 4f + BadgeSpace;
            if (badge != null && badge.enabled) AvLay.Place(badge.rectTransform, PadX + 4f,
                stacked ? PadY + (main - BadgeW) * 0.5f : (s.H - BadgeW) * 0.5f, BadgeW, BadgeW);
            AvLay.Place(name.rectTransform, x0, PadY, w, nh);
            AvLay.Place(sub.rectTransform, x0, PadY + nh + 2f, w, AvText.Height(sub, w));
            float x = s.W - PadX;
            if (stacked)
            {
                for (int i = 0; i < trailing.Count; i++) AvLay.Place(trailing[i].Rect,
                    PadX + 4f + i * (actionW + 4f), PadY + main + ColumnGap, actionW, actionH);
            }
            else
            {
                for (int i = trailing.Count - 1; i >= 0; i--)
                {
                    float controlW = TrailingWidth(trailing[i]);
                    x -= controlW; AvLay.Place(trailing[i].Rect, x, (s.H - actionH) * 0.5f, controlW, actionH); x -= 4f;
                }
            }
            if (value.text.Length > 0) AvLay.Place(value.rectTransform, x - ValueW, PadY, ValueW, AvText.Height(value, ValueW));
        }

        public override void Restyle()
        {
            // A pooled list row is restyled on every rebind; skip when neither the sheet nor the row's look inputs moved.
            int look = (interactable ? 0 : 1) | (armed ? 2 : 0) | (hover ? 4 : 0) | ((int)state << 3);
            if (look == styledLook && styledGen == AvStyleHost.FuiGeneration && styledTrailing == trailing.Count) return;
            styledLook = look; styledGen = AvStyleHost.FuiGeneration; styledTrailing = trailing.Count;
            string st = !interactable ? "disabled" : armed ? "armed" : hover ? "hover" : null;
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state), st);
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            rail.color = AvStyleHost.Resolve(r.Rail, AvTheme.RailInfo);
            name.color = !interactable ? AvInk.Muted : AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            sub.color = !interactable ? AvInk.Muted : AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            value.color = !interactable ? AvInk.Muted : AvStyleHost.FuiInk("row-value " + AvStates.Class(state), AvTheme.TextPrimary);
            foreach (AvControl c in trailing) c.Restyle();
        }
    }
}

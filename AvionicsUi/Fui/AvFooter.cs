using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// Console footer: the console's help strip, at a fixed height (a hover must never move the page above it).
    /// Idle, a state-coloured SYS tag leads the status line; under it the page's standing orders show one at a time (SOP, with
    /// pips; they turn over on the slow tick); the right end carries the ambient status column (<see cref="AvAmbient"/>).
    /// Hovering a control with help swaps in a TIP tag, the control's own name and its help sentence, in the same layout.
    /// Text shrinks toward the 11 px floor; an order that still cannot fit waits for a turn with more room.
    /// A compact strip (under 36 px) keeps one line: tag and text.
    /// </summary>
    public sealed class AvFooter : AvPart
    {
        private const float TagW = 34f, StatusW = 124f, Top = 7f, Bottom = 6f, Indent = TagW + 2f * AvGridTokens.Pad;
        private const int Dwell = 16;   // slow ticks (0.5 s each) an order stays up

        private readonly Image back;
        private readonly AvFooterGraphic art;
        private readonly AvFrame tag;
        private readonly RectTransform badge;
        private readonly TMP_Text text, name, order, sop, tagText;
        private readonly TMP_Text[] status = new TMP_Text[AvAmbient.Lines];
        private AvState state;
        private string baseValue = "", baseText = "", hint, hintName = "";
        private string[] orders;
        private int orderIndex, orderTicks;
        private bool statusShown = true, statusAvailable;
        private float width = AvTokens.PanelWidth, height = AvGridTokens.Footer;

        public AvFooter(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Footer");
            back = AvLay.Solid(Rect, "Back", Color.clear); AvLay.Fill(back.rectTransform);
            AvSurfaceGradient.Apply(back, Color.white, new Color(.72f, .77f, .79f, 1f));
            var artGo = new GameObject("Art", typeof(RectTransform), typeof(CanvasRenderer));
            artGo.transform.SetParent(Rect, false);
            AvLay.Fill((RectTransform)artGo.transform);
            art = artGo.AddComponent<AvFooterGraphic>();
            art.raycastTarget = false;
            // The badge sits in its own container so the frame is not read as the backing of the footer text beside it.
            badge = AvLay.Child(Rect, "TipBadge");
            tag = AvFrame.Add(badge, "TipTag", AvChamfer.Diagonal(4f)); tag.Stroke = 0f; AvLay.Fill(tag.rectTransform);
            tagText = AvText.Make(badge, "TipWord", AvTextRole.Micro, "SYS", TextAlignmentOptions.Center);
            AvText.Fit(tagText, false);
            AvLay.Fill(tagText.rectTransform);
            name = AvText.Make(Rect, "TipName", AvTextRole.Label);
            AvText.Fit(name, false);
            text = AvText.Make(Rect, "Text", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
            AvText.Fit(text, true);
            sop = AvText.Make(Rect, "OrdersTag", AvTextRole.Micro, "SOP");
            AvText.Fit(sop, false);
            order = AvText.Make(Rect, "Order", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            for (int i = 0; i < status.Length; i++)
            {
                status[i] = AvText.Make(Rect, "Status" + i, AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                AvText.Fit(status[i], false);
                PinRight(status[i].rectTransform, AvGridTokens.Pad, 9f + i * 19f, StatusW, 18f);
            }
            ShowStatus(false);
            Arrange();
            Restyle();
        }

        public void Set(string value, AvState s = AvState.Inert)
        {
            string next = value ?? "";
            if (baseValue == next && state == s) return;
            baseValue = next; baseText = AvStates.Glyph(s) + next; state = s;
            if (hint == null && AvText.Set(text, baseText)) Arrange();
            Restyle();
        }

        /// <summary>Hover help: <paramref name="value"/>, headed by the control's own name when given; null restores the idle strip.</summary>
        public void SetHint(string value, string controlName = null)
        {
            string next = string.IsNullOrEmpty(value) || !AvConsoleLook.HoverHelp ? null : value;
            string nextName = next == null ? "" : Clean(controlName);
            if (hint == next && hintName == nextName) return;
            hint = next; hintName = nextName;
            AvText.Set(text, hint ?? baseText);
            AvText.Set(name, hintName);
            Tick();
            Arrange();
            Restyle();
        }

        /// <summary>The current page's standing orders (null = none); the strip shows them one at a time, from the first.</summary>
        public void SetOrders(string[] lines)
        {
            if (lines != null && lines.Length == 0) lines = null;
            if (ReferenceEquals(lines, orders)) return;
            orders = lines; orderIndex = 0; orderTicks = 0;
            Arrange();
        }

        /// <summary>Refreshes the idle status column and turns the standing orders over; the owning console or window calls it on its slow tick.</summary>
        public void Tick()
        {
            bool any = false, turned = false;
            // The clock column stays up under hover help too: the strip keeps one layout, only its words change.
            for (int i = 0; i < status.Length; i++)
            {
                string line = AvAmbient.Line(i);
                AvText.Set(status[i], line);
                any |= line.Length > 0;
            }
            if (hint == null)
            {
                if (orders != null && orders.Length > 1 && ++orderTicks >= Dwell)
                {
                    orderTicks = 0;
                    orderIndex = (orderIndex + 1) % orders.Length;
                    turned = true;
                }
            }
            if (ShowStatus(any) | turned) Arrange();
        }

        // True when the column's visibility changed (the text column width with it).
        private bool ShowStatus(bool on)
        {
            statusAvailable = on;
            // Keep the reading/help column usable on compact consoles; ambient facts yield first.
            on &= AvConsoleLook.Clock && width - (Indent + AvGridTokens.Pad + StatusW + 14f) >= 160f && height >= AvGridTokens.Footer;
            if (on == statusShown) return false;
            statusShown = on;
            foreach (TMP_Text line in status) line.enabled = on;
            return true;
        }

        /// <summary>Re-reads <see cref="AvConsoleLook"/> (clock column, standing orders, hover help).</summary>
        public void Relook()
        {
            if (!AvConsoleLook.HoverHelp && hint != null) SetHint(null);
            ShowStatus(statusAvailable);
            Arrange();
        }

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            width = slot.W; height = slot.H;
            bool compact = height < 36f;
            text.enableWordWrapping = !compact;
            text.alignment = compact ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.TopLeft;
            ShowStatus(statusAvailable);
            Arrange();
        }

        // Tag, then the control's name when hovering; the text block beside the tag; the standing order under it, with its tag and
        // pips in the gutter under the badge. Measurements are cached per text, so a re-arrange is cheap.
        private void Arrange()
        {
            bool compact = height < 36f, hovering = hint != null;
            float right = AvGridTokens.Pad + (statusShown ? StatusW + 14f : 0f);
            float colW = Mathf.Max(0f, width - Indent - right);
            float tagH = Mathf.Min(16f, Mathf.Max(0f, height));
            AvLay.Place(badge, AvGridTokens.Pad, compact ? (height - tagH) * .5f : Top, TagW, tagH);

            bool named = hovering && !compact && hintName.Length > 0;
            name.enabled = named;
            float y = compact ? 0f : Top - 1f;
            if (named) { AvLay.Place(name.rectTransform, Indent, Top, colW, 16f); y = Top + 19f; }
            float room = compact ? height : Mathf.Max(0f, height - y - Bottom);

            // Idle: the status takes what it needs and the order gets the rest, if one fits there.
            float textH = room;
            bool showOrder = false;
            if (!compact && !hovering && orders != null && AvConsoleLook.Orders)
            {
                float statusH = baseText.Length > 0 ? Mathf.Min(AvText.Height(text, colW), room) : 0f;
                float oy = y + statusH + (statusH > 0f ? 3f : 0f), oroom = height - Bottom - oy;
                for (int k = 0; k < orders.Length && !showOrder && oroom >= 14f; k++)
                {
                    int i = (orderIndex + k) % orders.Length;
                    AvText.Set(order, orders[i]);
                    if (AvText.Height(order, colW) <= oroom + 1f) { orderIndex = i; showOrder = true; }
                }
                if (showOrder)
                {
                    textH = statusH;
                    AvLay.Place(order.rectTransform, Indent, oy, colW, oroom);
                    float gy = Mathf.Max(oy + 1f, Top + 20f);   // under the badge even when there is no status line
                    AvLay.Place(sop.rectTransform, AvGridTokens.Pad, gy, TagW, 13f);
                    art.SetPips(orders.Length, orderIndex, AvGridTokens.Pad, gy + 16f, TagW);
                }
            }
            order.enabled = sop.enabled = showOrder;
            if (!showOrder) art.SetPips(0, 0, 0f, 0f, 0f);
            AvLay.Place(text.rectTransform, Indent, y, colW, compact ? height : textH);
            art.SetLayout(TagW, statusShown ? right - 7f : -1f);
        }

        private static string Clean(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            string s = label.Replace('\n', ' ').Trim();
            return s.StartsWith("//") ? s.Substring(2).TrimStart() : s;
        }

        private static void PinRight(RectTransform t, float right, float top, float w, float h)
        {
            t.anchorMin = t.anchorMax = t.pivot = new Vector2(1f, 1f);
            t.anchoredPosition = new Vector2(-right, -top);
            t.sizeDelta = new Vector2(w, h);
        }

        public override float Measure(float width) => AvGridTokens.Footer;

        public override void Restyle()
        {
            AvStyle s = AvStyleHost.FuiStyle("footer " + AvStates.Class(state));
            back.color = AvStyleHost.Resolve(s.Background, AvTheme.SurfaceInert);
            text.color = AvStyleHost.Resolve(hint != null ? AvStyleHost.FuiStyle("title").Color : s.Color, AvTheme.Dim);
            // Help reads in the info colour, the status in its state's, so a glance tells which one the strip is showing.
            Color slab = AvStyleHost.FuiFill("slab " + (hint != null ? "info" : state == AvState.Inert ? "ready" : AvStates.Class(state)), AvTheme.Accent);
            tag.Paint(slab, Color.clear);
            tagText.color = AvStyleHost.FuiInk("slab", Color.black);
            AvText.Set(tagText, hint != null ? "TIP" : "SYS");
            name.color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
            order.color = AvStyleHost.FuiInk("page-index", AvTheme.Dim);
            sop.color = AvStyleHost.FuiInk("decor", AvTheme.Disabled);
            art.Paint(slab, AvStyleHost.FuiFill("decor", AvTheme.Hairline), AvStyleHost.FuiFill("slab info", AvTheme.RailInfo));
            status[0].color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
            for (int i = 1; i < status.Length; i++) status[i].color = AvStyleHost.FuiInk("page-index", AvTheme.Dim);
        }
    }

    /// <summary>The footer's line art in one mesh: the tag's accent on the top edge, the status column rule and the standing-order pips.</summary>
    public sealed class AvFooterGraphic : MaskableGraphic
    {
        private const float PipW = 5f, PipH = 2f, PipGap = 2f;
        private Color accent = Color.white, rule = Color.white, pipOn = Color.white;
        private float tagW, columnRight = -1f, pipX, pipTop, pipRoom;
        private int pips, pipIndex;

        public void Paint(Color accentColor, Color ruleColor, Color pipColor)
        {
            if (accentColor == accent && ruleColor == rule && pipColor == pipOn) return;
            accent = accentColor; rule = ruleColor; pipOn = pipColor;
            SetVerticesDirty();
        }

        /// <param name="tagWidth">Width of the tag whose accent runs along the top edge.</param>
        /// <param name="columnRightEdge">Distance of the status column rule from the right edge; negative hides it.</param>
        public void SetLayout(float tagWidth, float columnRightEdge)
        {
            if (Mathf.Approximately(tagWidth, tagW) && Mathf.Approximately(columnRightEdge, columnRight)) return;
            tagW = tagWidth; columnRight = columnRightEdge;
            SetVerticesDirty();
        }

        public void SetPips(int count, int index, float x, float top, float room)
        {
            if (count == pips && index == pipIndex && Mathf.Approximately(x, pipX) && Mathf.Approximately(top, pipTop) && Mathf.Approximately(room, pipRoom)) return;
            pips = count; pipIndex = index; pipX = x; pipTop = top; pipRoom = room;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width < 40f || r.height < 4f) return;
            float x0 = r.xMin + AvGridTokens.Pad;
            AvMesh.Quad(vh, x0, r.yMax - 2f, x0 + tagW, r.yMax, accent);
            if (columnRight >= 0f && r.height > 24f)
            {
                float x = r.xMax - columnRight;
                AvMesh.Quad(vh, x, r.yMin + 9f, x + 1f, r.yMax - 9f, rule);
            }
            if (pips < 2) return;
            int shown = Mathf.Min(pips, Mathf.FloorToInt((pipRoom + PipGap) / (PipW + PipGap)));
            if (shown < 2) return;
            float y1 = r.yMax - pipTop, y0 = y1 - PipH;
            Color off = rule;
            off.a *= 0.4f;   // the frame hue sits close to the info hue in several schemes: brightness carries the "which one"
            for (int i = 0; i < shown; i++)
            {
                float px = r.xMin + pipX + i * (PipW + PipGap);
                AvMesh.Quad(vh, px, y0, px + PipW, y1, i == pipIndex % shown ? pipOn : off);
            }
        }
    }
}

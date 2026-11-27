using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// Console footer: the console's help strip, at a fixed height (a hover must never move the page above it).
    /// Idle, a state-coloured SYS tag leads the status line; under it the page's standing orders show one at a time (SOP, with
    /// pips; they turn over on the slow tick); a slim bottom line carries ambient status (<see cref="AvAmbient"/>).
    /// Hovering a control with help swaps in a TIP tag, the control's own name and its help sentence, in the same layout.
    /// Text shrinks toward the 11 px floor; an order that still cannot fit waits for a turn with more room.
    /// A compact strip (under 36 px) keeps one line: tag and text.
    /// </summary>
    public sealed class AvFooter : AvPart
    {
        private const float TagW = 28f, StatusH = 17f, Top = 6f, Bottom = 5f, Indent = TagW + 2f * AvGridTokens.Pad;
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
        private bool statusShown = true, statusAvailable, orderOnly;
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
            sop = AvText.Make(Rect, "OrdersTag", AvTextRole.Micro, "TIP");
            AvText.Fit(sop, false);
            order = AvText.Make(Rect, "Order", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            for (int i = 0; i < status.Length; i++)
            {
                status[i] = AvText.Make(Rect, "Status" + i, AvTextRole.DataSmall, "",
                    i == 0 ? TextAlignmentOptions.MidlineLeft : i == status.Length - 1 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Center);
                AvText.Fit(status[i], false);
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
            ShowStatus(statusAvailable);
            Arrange();   // Help borrows the telemetry line without changing the footer's outer height.
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

        /// <summary>Refreshes the idle telemetry line and turns the standing orders over; the owning console or window calls it on its slow tick.</summary>
        public void Tick()
        {
            bool any = false, turned = false;
            // Keep the values fresh while help temporarily borrows their space.
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

        // True when the telemetry line changes the available reading height.
        private bool ShowStatus(bool on)
        {
            statusAvailable = on;
            // Keep the reading/help column usable on compact consoles; ambient facts yield first.
            on &= hint == null && AvConsoleLook.Clock && width >= 300f && height >= AvGridTokens.Footer;
            if (on == statusShown) return false;
            statusShown = on;
            foreach (TMP_Text line in status) line.enabled = on;
            return true;
        }

        /// <summary>Re-reads <see cref="AvConsoleLook"/> (telemetry line, standing orders, hover help).</summary>
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
            float right = AvGridTokens.Pad;
            float colW = Mathf.Max(0f, width - Indent - right);
            float readingHeight = height - (statusShown ? StatusH : 0f);
            if (statusShown)
            {
                float statusW = (width - 2f * AvGridTokens.Pad) / status.Length;
                for (int i = 0; i < status.Length; i++)
                    AvLay.Place(status[i].rectTransform, AvGridTokens.Pad + i * statusW, height - StatusH, statusW, StatusH);
            }
            float tagH = Mathf.Min(16f, Mathf.Max(0f, height));
            AvLay.Place(badge, AvGridTokens.Pad, compact ? (height - tagH) * .5f : Top, TagW, tagH);

            bool named = hovering && !compact && hintName.Length > 0;
            name.enabled = named;
            float y = compact ? 0f : Top - 1f;
            if (named) { AvLay.Place(name.rectTransform, Indent, Top, colW, 14f); y = Top + 15f; }
            float room = compact ? height : Mathf.Max(0f, readingHeight - y - Bottom);

            // Idle: the status takes what it needs and the order gets the rest, if one fits there.
            float textH = room;
            bool showOrder = false;
            if (!compact && !hovering && orders != null && AvConsoleLook.Orders)
            {
                float statusH = baseText.Length > 0 ? Mathf.Min(AvText.Height(text, colW), room) : 0f;
                float oy = y + statusH + (statusH > 0f ? 3f : 0f), oroom = readingHeight - Bottom - oy;
                for (int k = 0; k < orders.Length && !showOrder && oroom >= 14f; k++)
                {
                    int i = (orderIndex + k) % orders.Length;
                    if (string.IsNullOrWhiteSpace(orders[i])) continue;
                    AvText.Set(order, orders[i]);
                    if (AvText.Height(order, colW) <= oroom + 1f) { orderIndex = i; showOrder = true; }
                }
                if (showOrder)
                {
                    textH = statusH;
                    AvLay.Place(order.rectTransform, Indent, oy, colW, oroom);
                    float gy = Mathf.Max(oy + 1f, Top + 20f);   // under the badge even when there is no status line
                    AvLay.Place(sop.rectTransform, AvGridTokens.Pad, gy, TagW, 16f);
                    art.SetPips(orders.Length, orderIndex, AvGridTokens.Pad, Mathf.Min(gy + 17f, readingHeight - 3f), TagW);
                }
            }
            order.enabled = showOrder;
            bool nextOrderOnly = showOrder && baseText.Length == 0;
            bool showBadge = hovering || showOrder || baseValue.Length > 0;
            badge.gameObject.SetActive(showBadge);
            sop.enabled = showOrder && !nextOrderOnly;
            if (!showOrder) art.SetPips(0, 0, 0f, 0f, 0f);
            AvLay.Place(text.rectTransform, Indent, y, colW, compact ? height : textH);
            art.SetLayout(showBadge ? TagW : 0f, statusShown ? readingHeight : -1f);
            // Without a status line the order occupies its place, so its tag belongs there too.
            if (orderOnly != nextOrderOnly) { orderOnly = nextOrderOnly; Restyle(); }
        }

        private static string Clean(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            string s = label.Replace('\n', ' ').Trim();
            return s.StartsWith("//") ? s.Substring(2).TrimStart() : s;
        }

        public override float Measure(float width) => AvGridTokens.Footer;

        public override void Restyle()
        {
            AvStyle s = AvStyleHost.FuiStyle("footer " + AvStates.Class(state));
            // Help and status are reading planes; underlying map labels must not ghost through them.
            back.color = AvStyleHost.Resolve(s.Background, AvTheme.SurfaceInert).WithAlpha(1f);
            text.color = AvStyleHost.Resolve(hint != null ? AvStyleHost.FuiStyle("title").Color : s.Color, AvTheme.Dim);
            // Help reads in the info colour, the status in its state's, so a glance tells which one the strip is showing.
            bool tip = hint != null || orderOnly;
            Color slab = AvStyleHost.FuiFill("slab " + (tip ? "info" : state == AvState.Inert ? "ready" : AvStates.Class(state)), AvTheme.Accent);
            tag.Paint(slab.WithAlpha(.16f), Color.clear);
            tagText.color = slab;
            AvText.Set(tagText, tip ? "TIP" : "SYS");
            name.color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
            order.color = AvStyleHost.FuiInk("page-index", AvTheme.Dim);
            sop.color = AvStyleHost.FuiInk("section-caption", AvTheme.Disabled);   // readable: it labels the line beside it
            art.Paint(slab, AvStyleHost.FuiFill("decor", AvTheme.Hairline), AvStyleHost.FuiFill("slab info", AvTheme.RailInfo));
            status[0].color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
            for (int i = 1; i < status.Length; i++) status[i].color = AvStyleHost.FuiInk("page-index", AvTheme.Dim);
        }
    }

    /// <summary>The footer's line art in one mesh: accent, telemetry divider and standing-order pips.</summary>
    public sealed class AvFooterGraphic : MaskableGraphic
    {
        private const float PipW = 5f, PipH = 2f, PipGap = 2f;
        private Color accent = Color.white, rule = Color.white, pipOn = Color.white;
        private float tagW, telemetryTop = -1f, pipX, pipTop, pipRoom;
        private int pips, pipIndex;

        public void Paint(Color accentColor, Color ruleColor, Color pipColor)
        {
            if (accentColor == accent && ruleColor == rule && pipColor == pipOn) return;
            accent = accentColor; rule = ruleColor; pipOn = pipColor;
            SetVerticesDirty();
        }

        /// <param name="tagWidth">Width of the tag whose accent runs along the top edge.</param>
        /// <param name="telemetryTop">Telemetry line's top measured from the footer top; negative hides it.</param>
        public void SetLayout(float tagWidth, float telemetryTop)
        {
            if (Mathf.Approximately(tagWidth, tagW) && Mathf.Approximately(this.telemetryTop, telemetryTop)) return;
            tagW = tagWidth; this.telemetryTop = telemetryTop;
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
            if (telemetryTop >= 0f && r.height > 24f)
            {
                float y = r.yMax - telemetryTop;
                AvMesh.Quad(vh, x0, y, r.xMax - AvGridTokens.Pad, y + .65f, rule);
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

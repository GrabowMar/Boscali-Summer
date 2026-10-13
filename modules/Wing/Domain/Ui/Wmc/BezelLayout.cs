using System;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>The bezel's fixed geometry (spec bezel v2 §3): the synced <c>AvScreen</c> chrome with no metric row (data bar 62,
    /// tabs 34, status 56 and three 8 px gaps) leaves H − 176 for a page; TACTICAL stacks a scope row, the flight list and ORDERS.</summary>
    internal static class BezelLayout
    {
        public const float Chrome = 176f;
        public const int BaseRows = 3;
        public const int InboundMax = 4;
        public const float PopupRowPitch = 32f, PopupPad = 8f;
        public const int PopupMaxRows = 7;

        public static float Body(float panelHeight) => panelHeight - Chrome;

        /// <summary>The toolkit popup's height for <paramref name="entries"/> rows (seven at most: it pages beyond).</summary>
        public static float PopupHeight(int entries) =>
            PopupRowPitch * (entries <= 0 ? 1 : entries > PopupMaxRows ? PopupMaxRows : entries) + PopupPad;

        /// <summary>Where a popup for a row goes, as a depth from the body's top: below the row when it fits, else above, else after
        /// scrolling the row up (<paramref name="scroll"/> &gt; 0, at most <paramref name="maxScroll"/>) so it fits below, else after
        /// scrolling it down (<paramref name="scroll"/> &lt; 0, at most <paramref name="backScroll"/>: the page's offset) so it fits
        /// above — never over its row and never past the body (the page layer sits under the chrome). A last resort, when nothing fits,
        /// keeps it in the body.</summary>
        public static float PopupPlace(float rowDepth, float rowH, float popupH, float bodyH, float maxScroll, out float scroll,
            float backScroll = 0f)
        {
            scroll = 0f;
            if (rowDepth + rowH + popupH <= bodyH) return rowDepth + rowH;
            if (rowDepth - popupH >= 0f) return rowDepth - popupH;
            float need = rowDepth + rowH + popupH - bodyH;
            if (need <= maxScroll && rowH + popupH <= bodyH)
            {
                scroll = need;
                return rowDepth - need + rowH;
            }
            // Review R5: a short page cannot scroll a middle row far enough up; scrolled back, the row sits under the popup instead.
            float back = popupH - rowDepth;
            if (back <= backScroll && popupH + rowH <= bodyH)
            {
                scroll = -back;
                return 0f;
            }
            float room = bodyH - (rowDepth + rowH) >= rowDepth ? rowDepth + rowH : rowDepth - popupH;
            return Math.Max(0f, Math.Min(room, bodyH - popupH));
        }

        /// <summary>INBOUND rows drawn (the last says "+n MORE" beyond <see cref="InboundMax"/>).</summary>
        public static int InboundRows(int inbound) => inbound <= 0 ? 0 : inbound > InboundMax ? InboundMax : inbound;
    }
}

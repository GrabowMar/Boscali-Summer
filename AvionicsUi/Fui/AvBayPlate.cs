using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// What a console page shows below its last control when it is shorter than its viewport: an equipment bay
    /// cover carrying the page's standing orders (usage notes in the unit's own voice) and a data plate. Every
    /// mark on it is fixed text or fiction, never a reading, so it cannot be mistaken for live data.
    /// </summary>
    public sealed class AvBayPlate
    {
        public const float MinHeight = 54f;
        private const float Strip = 42f, Hatch = 6f, FullFrom = 118f, Pad = 10f, BarcodeW = 64f;

        private readonly RectTransform rect;
        private readonly AvBayGraphic art;
        private readonly TMP_Text stencil, caption, head, body, plate1, plate2;
        private readonly string id, serial;
        private float lastTop = -1f, lastHeight = -1f;
        private int lastPage = -1;
        private string[] lastOrders;

        public AvBayPlate(RectTransform parent, string id)
        {
            this.id = id ?? "";
            serial = AvPortalMath.Serial(id).Substring(3);
            rect = AvLay.Child(parent, "BayPlate");
            var go = new GameObject("Art", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(rect, false);
            AvLay.Fill((RectTransform)go.transform);
            art = go.AddComponent<AvBayGraphic>();
            art.raycastTarget = false;
            art.Seed = System.Convert.ToUInt32(serial, 16);
            stencil = AvText.Make(rect, "Stencil", AvTextRole.Headline, this.id, TextAlignmentOptions.Center);
            caption = AvText.Make(rect, "Caption", AvTextRole.Micro, "THIS BAY INTENTIONALLY LEFT BLANK", TextAlignmentOptions.Center);
            head = AvText.Make(rect, "OrdersHead", AvTextRole.Micro);
            body = AvText.Make(rect, "Orders", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            plate1 = AvText.Make(rect, "Plate1", AvTextRole.Micro);
            plate2 = AvText.Make(rect, "Plate2", AvTextRole.Micro);
            AvText.Fit(plate1, false); AvText.Fit(plate2, false);
            rect.gameObject.SetActive(false);
            Restyle();
        }

        public void Hide()
        {
            lastHeight = -1f;
            if (rect.gameObject.activeSelf) rect.gameObject.SetActive(false);
        }

        /// <summary>Covers <paramref name="available"/> px below <paramref name="top"/>; too little room hides the plate.</summary>
        public void Set(float x, float top, float width, float available, int page, string[] orders)
        {
            if (available < MinHeight) { Hide(); return; }
            if (Mathf.Abs(top - lastTop) < 0.5f && Mathf.Abs(available - lastHeight) < 0.5f && page == lastPage &&
                ReferenceEquals(orders, lastOrders)) return;
            lastTop = top; lastHeight = available; lastPage = page; lastOrders = orders;
            if (!rect.gameObject.activeSelf) rect.gameObject.SetActive(true);

            // Short gap: the data plate alone, seated on the bottom edge. Enough room: the whole bay cover.
            bool full = available >= FullFrom;
            float h = full ? available : MinHeight;
            AvLay.Place(rect, x, top + available - h, width, h);
            art.StripHeight = Strip; art.HatchHeight = full ? Hatch : 0f; art.Fasteners = full;
            art.SetVerticesDirty();

            float inner = width - 2f * Pad;
            string pageCode = (page + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            AvLay.Place(plate1.rectTransform, Pad, h - Strip + 5f, inner - BarcodeW, 16f);
            AvLay.Place(plate2.rectTransform, Pad, h - Strip + 21f, inner - BarcodeW, 16f);
            plate1.text = id + " DISPLAY PROCESSOR · MFD-480";
            plate2.text = "P/N 480-" + serial + "-" + pageCode + " · " + AvAmbient.Maker + " · MOD C";

            float y = Hatch + 8f, zoneBottom = h - Strip - 6f;
            bool showOrders = false;
            if (full && orders != null && orders.Length > 0)
            {
                float room = zoneBottom - y - 18f, bodyHeight = 0f;
                int count = orders.Length;
                for (; count > 0; count--)
                {
                    body.text = Compose(orders, count);
                    bodyHeight = AvText.Height(body, inner);
                    if (bodyHeight <= room) break;
                }
                if (count > 0)
                {
                    showOrders = true;
                    head.text = "STANDING ORDERS · " + id + "/" + pageCode;
                    AvLay.Place(head.rectTransform, Pad, y, inner, 16f);
                    AvLay.Place(body.rectTransform, Pad, y + 18f, inner, bodyHeight);
                    y += 18f + bodyHeight + 8f;
                }
            }
            head.enabled = body.enabled = showOrders;

            float zone = zoneBottom - y;
            bool showStencil = full && zone >= 84f;
            stencil.enabled = caption.enabled = showStencil;
            if (!showStencil) return;
            float size = Mathf.Clamp(zone * 0.36f, 26f, 72f), line = size * 1.4f;   // TMP bounds run about 1.3 em tall
            float sy = y + (zone - line - 18f) * 0.5f;
            stencil.fontSize = size;
            AvLay.Place(stencil.rectTransform, Pad, sy, inner, line);
            AvLay.Place(caption.rectTransform, Pad, sy + line + 2f, inner, 16f);
        }

        private static string Compose(string[] orders, int count)
        {
            var sb = new System.Text.StringBuilder(160);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append((i + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture)).Append("  ").Append(orders[i]);
            }
            return sb.ToString();
        }

        public void Restyle()
        {
            Color ink = AvStyleHost.FuiInk("decor", AvTheme.Disabled);
            art.color = AvStyleHost.FuiFill("decor", AvTheme.Hairline);
            head.color = AvStyleHost.FuiInk("section-title", AvTheme.RailInfo);
            body.color = plate1.color = AvStyleHost.FuiInk("page-index", AvTheme.Dim);
            plate2.color = caption.color = ink;
            stencil.color = ink.WithAlpha(0.16f);
        }
    }

    /// <summary>The bay cover's line art: frame, hazard band, fasteners, the data plate rule and its part barcode. Static.</summary>
    public sealed class AvBayGraphic : MaskableGraphic
    {
        public float StripHeight = 42f, HatchHeight = 6f;
        public bool Fasteners = true;
        public uint Seed = 1u;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width < 40f || r.height < StripHeight) return;
            Color c = color, soft = color;
            soft.a *= 0.7f;
            AvMesh.Outline(vh, r, c);
            if (HatchHeight > 0f)
                foreach (AvV2[] stripe in AvPortalMath.HazardStripes(r.xMin + 1f, r.yMax - 1f - HatchHeight, r.xMax - 1f, r.yMax - 1f, 8f))
                    AvMesh.Fan(vh, stripe, soft);
            float rule = r.yMin + StripHeight;
            if (r.height > StripHeight + 4f) AvMesh.Quad(vh, r.xMin + 1f, rule, r.xMax - 1f, rule + 0.65f, c);
            if (Fasteners)
            {
                Cross(vh, r.xMin + 9f, r.yMax - HatchHeight - 9f, c); Cross(vh, r.xMax - 9f, r.yMax - HatchHeight - 9f, c);
                Cross(vh, r.xMin + 9f, rule + 9f, c); Cross(vh, r.xMax - 9f, rule + 9f, c);
            }
            // Part barcode: bars from the console serial, so each console keeps its own.
            float x = r.xMax - 10f - 52f, x1 = r.xMax - 10f, y0 = r.yMin + 9f, y1 = r.yMin + StripHeight - 9f;
            uint h = Seed | 1u;
            while (x < x1)
            {
                h = h * 1664525u + 1013904223u;
                float w = 1f + ((h >> 28) & 3u) * 0.75f;
                if (((h >> 20) & 3u) != 0u) AvMesh.Quad(vh, x, y0, Mathf.Min(x + w, x1), y1, soft);
                x += w + 1f;
            }
        }

        private static void Cross(VertexHelper vh, float cx, float cy, Color c)
        {
            AvMesh.Quad(vh, cx - 3f, cy - 0.5f, cx + 3f, cy + 0.5f, c);
            AvMesh.Quad(vh, cx - 0.5f, cy - 3f, cx + 0.5f, cy + 3f, c);
        }
    }
}

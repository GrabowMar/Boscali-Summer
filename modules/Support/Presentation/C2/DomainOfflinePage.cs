using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>
    /// The [3] NET and [4] SOF tabs: a domain whose own backend is not online yet. One empty map box that says so, and the real
    /// CALL rows of that family (NET: FLARE BARRAGE and EMP; SOF: JTAC LASE with UNLASE, and FORTIFY), bound to the
    /// <see cref="CallsController"/> exactly as the CAP page binds them (<see cref="CapPage.Bind"/> / <see cref="CapPage.PaintRow"/>).
    /// The page owns no policy and no state beyond its rows.
    /// </summary>
    internal sealed class DomainOfflinePage
    {
        private const float Gap = 6f;
        private readonly CallFamily family;
        private readonly float width;
        private readonly Action<AvPart> register;
        private readonly C2Box map, callsBox;
        private readonly List<C2Row> rows = new List<C2Row>(4);
        private readonly List<SupportActionId> ids = new List<SupportActionId>(4);
        private readonly List<Action> pinActions = new List<Action>(4);
        private readonly List<Image> gridLines = new List<Image>(40);
        private readonly TMP_Text headline, detail;

        /// <summary>The footer words of this page (the refusal that points at the working surface).</summary>
        public string Words { get; }

        public DomainOfflinePage(RectTransform parent, float width, float height, CallFamily family, CallsController calls, Action<AvPart> register)
        {
            this.width = width;
            this.family = family;
            this.register = register ?? (_ => { });
            bool net = family == CallFamily.Cyber;
            bool full = height >= 560f;
            Words = net ? "NEGATIVE: CYBER OFFLINE — USE CAP FOR CYBER CALLS" : "NEGATIVE: SOF OFFLINE — USE JTAC LASE FROM CAP";

            for (int i = 0; i < CallSheet.Rows.Count; i++)
                if (CallSheet.Rows[i].Family == family) ids.Add(CallSheet.Rows[i].Id);
            int n = ids.Count;
            float rowH = full ? 40f : 26f, gap = full ? Gap : 4f;
            float callsBody = n * (rowH + 2f) + 2f;
            float callsH = C2Box.HeaderH + callsBody;
            float mapBody = Mathf.Max(60f, height - gap * 3f - callsH - C2Box.HeaderH);

            map = Make(new C2Box(parent, net ? "INTRUSION MAP" : "AO / TEAMS"));
            map.BodyHeight = mapBody;
            map.SetMeta(net ? "0 NODES · 0 IMPLANTS" : "0 TEAMS · 0 POSTS");
            map.Place(new AvSlot(0f, gap, width, C2Box.HeaderH + mapBody));
            Grid(map.Body, width - 2f, mapBody - 2f);
            headline = C2Kit.Mono(map.Body, "Headline", 12f, TextAlignmentOptions.Center, true, 2f);
            detail = C2Kit.Mono(map.Body, "Detail", 10f, TextAlignmentOptions.Center);
            OpsText.Set(headline, net ? "NO EW ASSETS ONLINE" : "NO TEAMS RAISED");
            OpsText.Set(detail, net ? "> cyber domain not online — arrives with the EW/CYBER milestone"
                : "> SOF domain not online — arrives with the SOF milestone");
            C2Kit.Place(headline, 4f, mapBody * 0.5f - 22f, width - 10f, 18f);
            C2Kit.Place(detail, 4f, mapBody * 0.5f - 2f, width - 10f, 16f);

            float y = gap + C2Box.HeaderH + mapBody + gap;
            callsBox = Make(new C2Box(parent, net ? "CYBER CALLS" : "SOF CALLS"));
            callsBox.BodyHeight = callsBody;
            callsBox.SetMeta("LIVE · SAME AUTHORITY AS CAP");
            callsBox.Place(new AvSlot(0f, y, width, callsH));
            for (int i = 0; i < n; i++)
            {
                C2Row row = Make(new C2Row(callsBox.Body, rowH, full));
                row.Place(new AvSlot(1f, 1f + i * (rowH + 2f), width - 4f, rowH));
                pinActions.Add(CapPage.Bind(row, calls, ids[i]));
                rows.Add(row);
            }
            Restyle();
        }

        private T Make<T>(T part) where T : AvPart
        {
            register(part);
            return part;
        }

        private void Grid(RectTransform body, float w, float h)
        {
            const float Cell = 40f;
            for (float x = Cell; x < w - 2f; x += Cell) gridLines.Add(Line(body, x, 0f, 1f, h));
            for (float y = Cell; y < h - 2f; y += Cell) gridLines.Add(Line(body, 0f, y, w, 1f));
        }

        private static Image Line(RectTransform body, float x, float y, float w, float h)
        {
            Image img = AvLay.Solid(body, "Grid", Color.clear);
            img.raycastTarget = false;
            AvLay.Place(img.rectTransform, x, y, w, h);
            return img;
        }

        /// <summary>Paints the call rows from the same tiles the CAP page paints.</summary>
        public void Paint(CapView v)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                CallTile tile = default;
                bool found = false;
                for (int k = 0; k < v.Tiles.Count && !found; k++)
                    if (v.Tiles[k].Id == ids[i]) { tile = v.Tiles[k]; found = true; }
                if (!found) continue;
                CapPage.PaintRow(rows[i], tile, CapPage.IndexOf(ids[i]), CapPage.IsPinned(v, ids[i]), pinActions[i]);
            }
        }

        public void Restyle()
        {
            foreach (Image g in gridLines) if (g != null) g.color = OpsInk.Hairline;
            if (headline != null) headline.color = OpsInk.Muted;
            if (detail != null) detail.color = OpsInk.Dim;
        }
    }
}

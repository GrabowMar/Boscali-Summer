using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>What every C2 terminal page shares: the height class, the part registry (so a restyle reaches every part) and the page console.</summary>
    internal abstract class C2PageBase
    {
        protected readonly float width;
        protected readonly bool full;
        protected readonly Action<AvPart> register;
        protected C2ConsoleView console;

        protected C2PageBase(float width, float height, Action<AvPart> register)
        {
            this.width = width;
            this.register = register ?? (_ => { });
            full = height >= 560f;
        }

        protected T Make<T>(T part) where T : AvPart
        {
            register(part);
            return part;
        }
    }

    /// <summary>
    /// The scaffold of a C2 map page (NET, SOF): the console, the map box with its grid and fallback headline, the label declutter and projection fit, the
    /// OPERATION box, the CALL rows, and a debounced press. The pages add their own layers on top of the map and their own boxes between the map and the OPS box.
    /// </summary>
    internal abstract class C2MapPage : C2PageBase
    {
        protected C2Box map;
        protected OpsBox ops;
        protected float mapW, mapH;
        protected TMP_Text headline, detail;
        protected readonly CyberMapProjection projection = new CyberMapProjection();
        protected readonly List<MapPoint> fit = new List<MapPoint>(48);
        protected readonly List<Rect> placed = new List<Rect>(48);
        protected int paintedSeq = -1, paintedSecond = -1, paintedOps = -1;
        private readonly float buttonH;
        private readonly C2CallRows callRows;
        private Image[] gridLines;
        private float nextPress;

        protected C2MapPage(float width, float height, Action<AvPart> register, float buttonH, CallFamily family) : base(width, height, register)
        {
            this.buttonH = buttonH;
            callRows = new C2CallRows(family);
        }

        protected void BuildConsole(RectTransform parent, int lines, float y, float height)
        {
            console = Make(new C2ConsoleView(parent, lines));
            console.Place(new AvSlot(0f, y, width, height));
        }

        /// <summary>The map box and its grid. Its own layers follow, then <see cref="BuildHeadline"/> on top.</summary>
        protected void BuildMap(RectTransform parent, string title, string meta, float y, float body)
        {
            map = Make(new C2Box(parent, title));
            map.BodyHeight = body;
            map.SetMeta(meta);
            map.Place(new AvSlot(0f, y, width, C2Box.HeaderH + body));
            mapW = width - 2f; mapH = body - 2f;
            gridLines = BuildGrid(map.Body);
        }

        /// <summary>The centred NO LINK / nothing-standing words, created last so they sit above every map layer.</summary>
        protected void BuildHeadline()
        {
            headline = C2Kit.Mono(map.Body, "Headline", 12f, TextAlignmentOptions.Center, true, 2f);
            detail = C2Kit.Mono(map.Body, "Detail", 10f, TextAlignmentOptions.Center);
            C2Kit.Place(headline, 4f, mapH * 0.5f - 22f, mapW - 8f, 18f);
            C2Kit.Place(detail, 4f, mapH * 0.5f - 2f, mapW - 8f, 16f);
        }

        protected void BuildOps(RectTransform parent, float y, OpDomain domain, OpKind[] kinds, OpsBoxActions actions)
        {
            ops = new OpsBox(parent, width, full, domain, kinds, actions, register);
            ops.Place(new AvSlot(0f, y, width, OpsBox.HeightFor(full)));
        }

        protected float CallsBody(float rowH) => callRows.BodyHeight(rowH);

        protected void BuildCalls(RectTransform parent, string title, float y, float rowH, CallsController calls) =>
            callRows.Build(parent, title, y, width, rowH, full, calls, register);

        protected void PaintCalls(CapView v) => callRows.Paint(v);

        protected static int OpsSeq(CapView v) => v.OpsKnown && v.Ops != null ? v.Ops.Seq : -2;

        /// <summary>Greedy label declutter (see <see cref="MapDeclutter"/>): the nearest free spot inside the map, so two map labels never share pixels.</summary>
        protected Vector2 Free(float x, float y, float w, float h) => MapDeclutter.Free(placed, x, y, w, h, mapW, mapH);

        /// <summary>A press is a request, never an effect: the host judges it. A 0.35 s debounce keeps a double click from sending twice.</summary>
        protected void Press(Action send)
        {
            if (Time.unscaledTime < nextPress) return;
            nextPress = Time.unscaledTime + 0.35f;
            send?.Invoke();
        }

        protected AvControl Button(RectTransform parent, string label, AvButtonStyle style, float x, float y, float w, Action click)
        {
            AvControl c = AvControl.Make(parent, new AvControl.Spec(label, click, style));
            c.SingleLine();
            AvLay.Place(c.Rect, x, y, w, buttonH);
            return c;
        }

        /// <summary>Re-colours the shared parts and forces the next paint to redraw with the new palette.</summary>
        protected void RestyleMap()
        {
            if (gridLines != null) foreach (Image g in gridLines) if (g != null) g.color = OpsInk.Hairline;
            if (headline != null) headline.color = OpsInk.Muted;
            if (detail != null) detail.color = OpsInk.Dim;
            ops?.Restyle();
            paintedSeq = paintedOps = -1;
        }

        protected static AvLineGraphic Line(RectTransform body, string name, float thickness)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(body, false);
            var g = go.AddComponent<AvLineGraphic>();
            g.raycastTarget = false;
            g.FillUnder = false;
            g.Thickness = thickness;
            AvLay.Fill(g.rectTransform);
            g.SetPoints(null, null, 0);
            return g;
        }

        /// <summary>A hidden, non-clickable solid (a map dot or a selection ring); the page places and tints it per paint.</summary>
        protected static Image Dot(RectTransform body, string name)
        {
            Image img = AvLay.Solid(body, name, Color.clear);
            img.raycastTarget = false;
            img.gameObject.SetActive(false);
            return img;
        }

        protected static TMP_Text Tag(RectTransform body, string name)
        {
            TMP_Text t = C2Kit.Mono(body, name, 10f, TextAlignmentOptions.MidlineLeft, true);
            t.gameObject.SetActive(false);
            return t;
        }

        private Image[] BuildGrid(RectTransform body)
        {
            const float Cell = 40f;
            var list = new List<Image>(40);
            for (float x = Cell; x < mapW - 2f; x += Cell) list.Add(GridLine(body, x, 0f, 1f, mapH));
            for (float yy = Cell; yy < mapH - 2f; yy += Cell) list.Add(GridLine(body, 0f, yy, mapW, 1f));
            return list.ToArray();
        }

        private static Image GridLine(RectTransform body, float x, float y, float w, float h)
        {
            Image img = AvLay.Solid(body, "Grid", Color.clear);
            img.raycastTarget = false;
            AvLay.Place(img.rectTransform, x, y, w, h);
            return img;
        }
    }

    /// <summary>The CALL rows at the foot of a map page (NET: CYBER, SOF: SOF), bound exactly as the CAP page binds them.</summary>
    internal sealed class C2CallRows
    {
        private readonly List<SupportActionId> ids = new List<SupportActionId>(3);
        private readonly List<C2Row> rows = new List<C2Row>(3);
        private readonly List<Action> pinActions = new List<Action>(3);
        private readonly List<CapPage.RowMemo> memos = new List<CapPage.RowMemo>(3);

        public C2CallRows(CallFamily family)
        {
            for (int i = 0; i < CallSheet.Rows.Count; i++) if (CallSheet.Rows[i].Family == family) ids.Add(CallSheet.Rows[i].Id);
        }

        public float BodyHeight(float rowH) => ids.Count * (rowH + 2f) + 2f;

        public void Build(RectTransform parent, string title, float y, float width, float rowH, bool full, CallsController calls, Action<AvPart> register)
        {
            float body = BodyHeight(rowH);
            var box = new C2Box(parent, title);
            register(box);
            box.BodyHeight = body;
            box.SetMeta("LIVE · SAME AUTHORITY AS CAP");
            box.Place(new AvSlot(0f, y, width, C2Box.HeaderH + body));
            for (int i = 0; i < ids.Count; i++)
            {
                var row = new C2Row(box.Body, rowH, full);
                register(row);
                row.Place(new AvSlot(1f, 1f + i * (rowH + 2f), width - 4f, rowH));
                pinActions.Add(CapPage.Bind(row, calls, ids[i]));
                rows.Add(row);
                memos.Add(default);
            }
        }

        public void Paint(CapView v)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                CallTile tile = default;
                bool found = false;
                for (int k = 0; k < v.Tiles.Count && !found; k++)
                    if (v.Tiles[k].Id == ids[i]) { tile = v.Tiles[k]; found = true; }
                if (!found) continue;
                bool pinned = CapPage.IsPinned(v, ids[i]);
                CapPage.RowMemo memo = memos[i];
                bool changed = memo.Changed(tile, pinned);
                memos[i] = memo;
                if (changed) CapPage.PaintRow(rows[i], tile, CapPage.IndexOf(ids[i]), pinned, pinActions[i]);
            }
        }
    }
}

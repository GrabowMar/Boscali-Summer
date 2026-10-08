using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The visible window onto the theatre: the plate (box pixels) covers the whole area it is given, so the theatre picture is scaled to COVER
    /// it (aspect kept, the overflowing axis cropped) and then zoomed 1x..3x. <see cref="P"/> is the one projection every marker uses and
    /// <see cref="ImageUv"/> is the crop of the same view, so a marker always lands on the pixel it describes.
    /// </summary>
    internal sealed class FrontMapView
    {
        public const int MaxZoom = 3;
        public readonly float X, Y, W, H;
        public int Zoom = 1;
        public Vector2 Center = new Vector2(0.5f, 0.5f);
        /// <summary>The operator moved or zoomed the view: automatic centring stops.</summary>
        public bool Touched;
        private bool centered;

        /// <summary>The visible part of the theatre in 0..1 units.</summary>
        public float VW { get; private set; }
        public float VH { get; private set; }

        public FrontMapView(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; Refresh(); }

        /// <summary>Plate pixels per theatre width: multiply a fraction of the theatre's width (a reach, a radius) by it.</summary>
        public float Scale => W / VW;

        public void Refresh()
        {
            float theatre = FrontMap.Aspect, plate = W / H;
            float bw = plate > theatre ? 1f : plate / theatre, bh = plate > theatre ? theatre / plate : 1f;
            VW = bw / Zoom; VH = bh / Zoom;
            Center.x = VW >= 1f ? 0.5f : Mathf.Clamp(Center.x, VW * 0.5f, 1f - VW * 0.5f);
            Center.y = VH >= 1f ? 0.5f : Mathf.Clamp(Center.y, VH * 0.5f, 1f - VH * 0.5f);
        }

        public Vector2 P(float u, float v) => new Vector2(X + (u - Center.x + VW * 0.5f) / VW * W, Y + (v - Center.y + VH * 0.5f) / VH * H);

        public Vector2 UV(Vector2 p) => new Vector2(Center.x - VW * 0.5f + (p.x - X) / W * VW, Center.y - VH * 0.5f + (p.y - Y) / H * VH);

        /// <summary>Whether a projected point (plus a margin for its glyph) is inside the plate.</summary>
        public bool Visible(Vector2 p, float pad = 8f) => p.x >= X + pad && p.x <= X + W - pad && p.y >= Y + pad && p.y <= Y + H - pad;

        /// <summary>The crop of the theatre picture (<paramref name="src"/> is the whole theatre in texture uv) that the view shows.</summary>
        public Rect ImageUv(Rect src)
        {
            float u0 = Center.x - VW * 0.5f, v0 = Center.y - VH * 0.5f;
            return new Rect(src.x + u0 * src.width, src.y + (1f - v0 - VH) * src.height, VW * src.width, VH * src.height);
        }

        /// <summary>Wheel zoom: one step in or out, the theatre point under <paramref name="p"/> stays under it.</summary>
        public bool ZoomAt(Vector2 p, int dir)
        {
            int z = Mathf.Clamp(Zoom + dir, 1, MaxZoom);
            if (z == Zoom) return false;
            Vector2 uv = UV(p);
            Zoom = z;
            Refresh();
            Center = new Vector2(uv.x - ((p.x - X) / W - 0.5f) * VW, uv.y - ((p.y - Y) / H - 0.5f) * VH);
            Refresh();
            Touched = true;
            return true;
        }

        public void Pan(Vector2 d)
        {
            Center -= new Vector2(d.x / W * VW, d.y / H * VH);
            Refresh();
            Touched = true;
        }

        public void CenterOn(Vector2 uv) { Center = uv; Refresh(); Touched = true; }

        /// <summary>Centres once on the area of interest (until the operator takes the view).</summary>
        public void AutoCenter(Vector2 uv)
        {
            if (Touched || centered) return;
            centered = true;
            Center = uv;
            Refresh();
        }
    }

    /// <summary>
    /// The theatre plate every front room draws on: the real vanilla map image (DynamicMap.mapImage's sprite), a clipped vector layer in box
    /// pixels, the grid, and the plate chrome (vignette, scale bar in km, north arrow, grid readout under the cursor, inset overview map with
    /// the view rectangle). Wheel zooms around the cursor, right or middle drag pans, a click on the inset recentres.
    /// </summary>
    internal sealed class FrontMapPlate
    {
        private const float MiniW = 168f, RowH = 24f, Edge = 70f;
        private static readonly float[] NiceKm = { 1f, 2f, 5f, 10f, 20f, 25f, 50f, 100f };

        public readonly FrontMapView View;
        /// <summary>Box-pixel vector layer, clipped to the plate.</summary>
        public readonly FrontVector G;
        public readonly FrontLabels Labels;

        private readonly RectTransform box;
        private readonly RawImage image, mini;
        private readonly FrontVector chrome;
        private readonly TMP_Text[] colLabels, rowLabels;
        private readonly TMP_Text scaleLabel, northLabel, readout;
        private readonly string[] colNames, rowNames;
        private readonly int cols, rows;
        private readonly Rect miniRect;
        private readonly List<Vector2> dotPos = new List<Vector2>(16);
        private readonly List<Color> dotInk = new List<Color>(16);
        private bool dragging;
        private Vector2 lastBox;
        private string idle;

        public FrontMapPlate(RectTransform body, FrontSkin skin, float boxW, float boxH, int cols, int rows)
        {
            box = body;
            this.cols = cols; this.rows = rows;
            float x = 2f, y = FrontKit.HeaderH + 2f, w = boxW - 4f, h = boxH - y - 2f;
            View = new FrontMapView(x, y, w, h);
            Labels = new FrontLabels(x, y, w, h);

            RectTransform clip = AvLay.Child(body, "MapClip");
            AvLay.Place(clip, x, y, w, h);
            clip.gameObject.AddComponent<RectMask2D>();
            var go = new GameObject("MapImage", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(clip, false);
            image = go.AddComponent<RawImage>();
            image.raycastTarget = false;
            AvLay.Place(image.rectTransform, 0f, 0f, w, h);
            G = FrontVector.Add(clip, "Map");
            AvLay.Place(G.rectTransform, -x, -y, boxW, boxH);   // box pixels inside the clipped plate

            float mh = Mathf.Round(MiniW / Mathf.Max(0.5f, FrontMap.Aspect));
            miniRect = new Rect(x + w - 8f - MiniW, y + h - 8f - mh, MiniW, mh);
            var mg = new GameObject("MapInset", typeof(RectTransform), typeof(CanvasRenderer));
            mg.transform.SetParent(body, false);
            mini = mg.AddComponent<RawImage>();
            mini.raycastTarget = false;
            AvLay.Place(mini.rectTransform, miniRect.x, miniRect.y, miniRect.width, miniRect.height);
            chrome = FrontVector.Add(body, "MapChrome");
            AvLay.Place(chrome.rectTransform, 0f, 0f, boxW, boxH);

            FrontMap.AxisLabels(FrontMap.GridSource, BoscaliSummer.Core.Game.TheaterFrame.Resolve(), cols, rows, out colNames, out rowNames);
            colLabels = new TMP_Text[cols]; rowLabels = new TMP_Text[rows];
            for (int i = 0; i < cols; i++) colLabels[i] = GridLabel(body, "Col" + i);
            for (int j = 0; j < rows; j++) rowLabels[j] = GridLabel(body, "Row" + j);
            float by = y + h - RowH + 4f;
            scaleLabel = FrontKit.Mono(body, "ScaleLabel", 0f, by, 56f, 16f, 10f, TextAlignmentOptions.MidlineLeft, true);
            northLabel = FrontKit.Mono(body, "North", x + 224f, by, 14f, 16f, 10.5f, TextAlignmentOptions.Midline, true);
            northLabel.text = "N";
            readout = FrontKit.Mono(body, "Readout", x + 246f, by, 300f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            skin.Add(() =>
            {
                foreach (TMP_Text t in colLabels) t.color = AvInk.Muted;
                foreach (TMP_Text t in rowLabels) t.color = AvInk.Muted;
                scaleLabel.color = AvInk.Ink; northLabel.color = AvInk.Ink;
            });
            Hover(null);
        }

        public float X => View.X;
        public float Y => View.Y;
        public float W => View.W;
        public float H => View.H;
        public Rect Plate => new Rect(View.X, View.Y, View.W, View.H);

        public Vector2 P(float u, float v) => View.P(u, v);

        /// <summary>A marker the operator can see: inside the plate and not under an overlay card.</summary>
        public bool Shown(Vector2 p, float pad = 12f) => View.Visible(p, pad) && !Labels.Blocked(new Rect(p.x - 8f, p.y - 8f, 16f, 16f));

        /// <summary>A dot for the inset overview (own forces, the area of interest).</summary>
        public void Dot(float u, float v, Color k) { dotPos.Add(new Vector2(u, v)); dotInk.Add(k); }

        private static TMP_Text GridLabel(RectTransform p, string name)
        {
            TMP_Text t = FrontKit.Mono(p, "Grid" + name, 0f, 0f, 24f, 13f, 10f, TextAlignmentOptions.MidlineLeft);
            t.gameObject.SetActive(false);
            return t;
        }

        /// <summary>Soft card behind an overlay on the map: translucent ground, hairline frame.</summary>
        public static void Card(FrontVector g, float x, float y, float w, float h)
        {
            g.Rect(x, y, w, h, AvInk.Ground.WithAlpha(0.74f));
            g.Frame(x, y, w, h, 1f, AvInk.Hairline);
            g.Rect(x, y, 3f, h, AvInk.Select.WithAlpha(0.8f));
        }

        // ---- Paint ---------------------------------------------------------------------------------------------------

        /// <summary>Starts a paint: image crop, scrim, grid, vignette and the grid labels. The room draws its markers into <see cref="G"/> next.</summary>
        public void Begin()
        {
            FrontMapView v = View;
            v.Refresh();
            G.Clear(); chrome.Clear(); Labels.Reset();
            dotPos.Clear(); dotInk.Clear();
            bool has = FrontMap.Source(out Texture tex, out Rect uv);
            image.enabled = mini.enabled = has;
            if (has)
            {
                image.texture = tex; image.uvRect = v.ImageUv(uv); image.color = new Color(0.9f, 0.94f, 0.92f, 1f);
                mini.texture = tex; mini.uvRect = uv; mini.color = new Color(0.8f, 0.85f, 0.83f, 1f);
            }
            // A light scrim keeps markers legible over bright terrain without hiding it; with no image it is the old opaque ground.
            G.Rect(v.X, v.Y, v.W, v.H, AvInk.Ground.WithAlpha(has ? 0.16f : 0.9f));
            Color hair = AvInk.Hairline, frame = AvInk.Frame;
            for (int i = 1; i < cols; i++)
            {
                float gx = v.P((float)i / cols, 0f).x;
                if (gx > v.X && gx < v.X + v.W) G.Line(new Vector2(gx, v.Y), new Vector2(gx, v.Y + v.H), i % 5 == 0 ? 1.2f : 1f, (i % 5 == 0 ? frame : hair).WithAlpha(0.3f));
            }
            for (int j = 1; j < rows; j++)
            {
                float gy = v.P(0f, (float)j / rows).y;
                if (gy > v.Y && gy < v.Y + v.H) G.Line(new Vector2(v.X, gy), new Vector2(v.X + v.W, gy), j % 4 == 0 ? 1.2f : 1f, (j % 4 == 0 ? frame : hair).WithAlpha(0.3f));
            }
            for (int i = 0; i <= cols * 5; i++)
            {
                float tx = v.P(i / (5f * cols), 0f).x, th = i % 5 == 0 ? 6f : 3f;
                if (tx < v.X || tx > v.X + v.W) continue;
                G.Line(new Vector2(tx, v.Y), new Vector2(tx, v.Y + th), 1f, frame.WithAlpha(0.8f));
            }
            // Vignette: the plate edges fall off into the ground colour.
            Color c0 = AvInk.Ground.WithAlpha(0.55f), c1 = AvInk.Ground.WithAlpha(0f);
            G.Grad(v.X, v.Y, Edge, v.H, c0, c1, c1, c0);
            G.Grad(v.X + v.W - Edge, v.Y, Edge, v.H, c1, c0, c0, c1);
            G.Grad(v.X, v.Y, v.W, Edge * 0.6f, c0, c0, c1, c1);
            G.Grad(v.X, v.Y + v.H - Edge * 0.6f, v.W, Edge * 0.6f, c1, c1, c0, c0);
            G.Rect(v.X, v.Y + v.H - RowH, v.W, RowH, AvInk.Ground.WithAlpha(0.5f));

            // Grid names ride the view: columns along the top edge, rows down the left edge.
            for (int i = 0; i < cols; i++)
            {
                float lx = v.P((float)i / cols, 0f).x + 4f;
                bool on = lx >= v.X + 2f && lx <= v.X + v.W - 28f && !Labels.Blocked(new Rect(lx - 2f, v.Y, 22f, 16f));
                colLabels[i].gameObject.SetActive(on);
                if (!on) continue;
                colLabels[i].text = colNames != null ? colNames[i] : (30 + i).ToString();
                AvLay.Place(colLabels[i].rectTransform, lx, v.Y + 2f, 24f, 13f);
                Labels.Take(new Rect(lx - 2f, v.Y, 22f, 16f));
            }
            for (int j = 0; j < rows; j++)
            {
                float ly = v.P(0f, (float)j / rows).y + 2f;
                bool on = ly >= v.Y + 16f && ly <= v.Y + v.H - RowH - 14f && !Labels.Blocked(new Rect(v.X + 2f, ly - 2f, 22f, 15f));
                rowLabels[j].gameObject.SetActive(on);
                if (!on) continue;
                rowLabels[j].text = rowNames != null ? rowNames[j] : (6 + j).ToString("00");
                AvLay.Place(rowLabels[j].rectTransform, v.X + 4f, ly, 24f, 13f);
                Labels.Take(new Rect(v.X + 2f, ly - 2f, 22f, 15f));
            }
            Labels.Take(new Rect(v.X, v.Y + v.H - RowH, 560f, RowH));
            Labels.Take(new Rect(miniRect.x - 4f, miniRect.y - 4f, miniRect.width + 8f, miniRect.height + 8f));
        }

        /// <summary>Finishes a paint: frame, scale bar, north arrow, inset, then pushes both layers.</summary>
        public void End()
        {
            FrontMapView v = View;
            G.Frame(v.X, v.Y, v.W, v.H, 1.6f, AvInk.Frame);
            Vector2 span = BoscaliSummer.Core.Game.TheaterFrame.Resolve();
            float kmPerPx = Mathf.Max(0.0001f, span.x / 1000f / v.Scale), km = NiceKm[0];
            foreach (float n in NiceKm) if (n / kmPerPx <= 130f) km = n;
            float len = km / kmPerPx, sx = v.X + 12f, sy = v.Y + v.H - 9f;
            Color ink = AvInk.Ink, back = AvInk.Ground.WithAlpha(0.85f);
            chrome.Line(new Vector2(sx - 1f, sy), new Vector2(sx + len + 1f, sy), 4f, back);
            chrome.Line(new Vector2(sx, sy), new Vector2(sx + len, sy), 1.6f, ink);
            for (int i = 0; i <= 2; i++) chrome.Line(new Vector2(sx + len * i / 2f, sy - (i == 1 ? 3f : 6f)), new Vector2(sx + len * i / 2f, sy), 1.4f, ink);
            AvLay.Place(scaleLabel.rectTransform, sx + len + 8f, v.Y + v.H - RowH + 4f, 56f, 16f);
            AvText.Set(scaleLabel, km.ToString("0") + " KM");
            float nx = v.X + 214f, ny = v.Y + v.H - 4f;
            chrome.Tri(new Vector2(nx, ny - 16f), new Vector2(nx + 5f, ny - 3f), new Vector2(nx - 5f, ny - 3f), ink);
            chrome.Line(new Vector2(nx, ny - 3f), new Vector2(nx, ny - 8f), 1.4f, back);

            // Inset: the whole theatre, the view rectangle, the area-of-interest dots.
            Rect m = miniRect;
            chrome.Frame(m.x - 1f, m.y - 1f, m.width + 2f, m.height + 2f, 1.4f, AvInk.Frame);
            float u0 = v.Center.x - v.VW * 0.5f, v0 = v.Center.y - v.VH * 0.5f;
            var r = new Rect(m.x + u0 * m.width, m.y + v0 * m.height, v.VW * m.width, v.VH * m.height);
            chrome.Rect(r.x, r.y, r.width, r.height, AvInk.Key.WithAlpha(0.1f));
            chrome.Frame(r.x, r.y, r.width, r.height, 1.4f, AvInk.Key);
            for (int i = 0; i < dotPos.Count; i++) chrome.Disc(new Vector2(m.x + dotPos[i].x * m.width, m.y + dotPos[i].y * m.height), 2.2f, dotInk[i], 8);
            G.Flush();
            chrome.Flush();
        }

        // ---- Input (polled by the window: the Rewired mouse is off, the event system hears nothing) --------------------

        private bool ToBox(Vector2 screen, out Vector2 p)
        {
            p = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(box, screen, null, out Vector2 lp)) return false;
            p = new Vector2(lp.x - box.rect.xMin, box.rect.yMax - lp.y);
            return true;
        }

        /// <summary>One frame of pointer input; true when the view moved and the room must repaint.</summary>
        public bool Tick(Vector2 mouse)
        {
            if (!box.gameObject.activeInHierarchy || !ToBox(mouse, out Vector2 p)) return false;
            FrontMapView v = View;
            bool inPlate = p.x >= v.X && p.x <= v.X + v.W && p.y >= v.Y && p.y <= v.Y + v.H;
            bool inMini = miniRect.Contains(p);
            bool changed = false;
            Hover(inPlate ? v.UV(p) : (Vector2?)null);
            if (inMini && Input.GetMouseButton(0))
            {
                Vector2 uv = new Vector2((p.x - miniRect.x) / miniRect.width, (p.y - miniRect.y) / miniRect.height);
                v.CenterOn(uv);
                return true;
            }
            float wheel = Input.mouseScrollDelta.y;
            if (inPlate && Mathf.Abs(wheel) > 0.01f) changed |= v.ZoomAt(p, wheel > 0f ? 1 : -1);
            bool down = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (down && (dragging || inPlate))
            {
                if (dragging && (p - lastBox).sqrMagnitude > 0.25f) { v.Pan(p - lastBox); changed = true; }
                dragging = true; lastBox = p;
            }
            else dragging = false;
            if (changed) Hover(inPlate ? v.UV(p) : (Vector2?)null);
            return changed;
        }

        private void Hover(Vector2? uv)
        {
            string text;
            if (uv.HasValue)
            {
                Vector2 span = BoscaliSummer.Core.Game.TheaterFrame.Resolve();
                float xm = (uv.Value.x - 0.5f) * span.x, zm = (0.5f - uv.Value.y) * span.y;
                string grid = FrontMap.GridSource(xm, zm);
                text = View.Zoom + "X · GRID " + grid + " · " + Mathf.Abs(xm / 1000f).ToString("0.0") + " KM " + (xm >= 0f ? "E" : "W") + " " + Mathf.Abs(zm / 1000f).ToString("0.0") + " KM " + (zm >= 0f ? "N" : "S");
            }
            else text = View.Zoom + "X · WHEEL ZOOM · RIGHT-DRAG PAN";
            if (text == idle) return;
            idle = text;
            FrontKit.Set(readout, text, uv.HasValue ? AvInk.Ink : AvInk.Dim);
        }
    }

    /// <summary>The theatre picture source and the pure helpers around it.</summary>
    internal static class FrontMap
    {
        /// <summary>Offline harness fixture: used when there is no live DynamicMap sprite. Whole texture = whole theatre.</summary>
        internal static Texture TestTexture;

        /// <summary>Grid naming for the plate edges: the vanilla grid live; the harness swaps in a fixture grid.</summary>
        internal static System.Func<float, float, string> GridSource = OpsKit.Grid;

        /// <summary>The theatre's width over height, from the same span the views project with.</summary>
        public static float Aspect { get { Vector2 s = BoscaliSummer.Core.Game.TheaterFrame.Resolve(); return s.x / Mathf.Max(1f, s.y); } }

        internal static bool Source(out Texture tex, out Rect uv)
        {
            tex = null; uv = new Rect(0f, 0f, 1f, 1f);
            try
            {
                DynamicMap dm = SceneSingleton<DynamicMap>.i;
                Image im = dm != null && dm.mapImage != null ? dm.mapImage.GetComponent<Image>() : null;
                Sprite sp = im != null ? im.sprite : null;
                if (sp != null && sp.texture != null)
                {
                    Rect r = sp.textureRect;
                    tex = sp.texture;
                    uv = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
                    return true;
                }
            }
            catch (System.Exception) { /* no live map: fall through to the fixture */ }
            tex = TestTexture;
            return tex != null;
        }

        /// <summary>
        /// The vanilla grid's own column and row names for a cols x rows plate over the theatre (cell centres through <paramref name="grid"/>,
        /// which is OpsKit.Grid live). Both arrays are null when the grid is unavailable (it answers in kilometres) or its squares are not a
        /// letters-plus-digits name, and the caller keeps its numeric labels.
        /// </summary>
        public static void AxisLabels(System.Func<float, float, string> grid, Vector2 span, int cols, int rows, out string[] colNames, out string[] rowNames)
        {
            colNames = rowNames = null;
            var c = new string[cols][]; var r = new string[rows][];
            for (int i = 0; i < cols; i++) if ((c[i] = Split(grid((i + 0.5f) / cols * span.x - span.x * 0.5f, 0f))) == null) return;
            for (int j = 0; j < rows; j++) if ((r[j] = Split(grid(0f, span.y * 0.5f - (j + 0.5f) / rows * span.y))) == null) return;
            // Columns vary in one part of the name (letters or digits), rows in the other; take whichever part actually changes.
            int cp = c[0][0] != c[cols - 1][0] ? 0 : 1, rp = r[0][1] != r[rows - 1][1] ? 1 : 0;
            if (cp == rp) return;
            colNames = new string[cols]; rowNames = new string[rows];
            for (int i = 0; i < cols; i++) colNames[i] = c[i][cp];
            for (int j = 0; j < rows; j++) rowNames[j] = r[j][rp];
        }

        private static string[] Split(string s)
        {
            if (string.IsNullOrEmpty(s) || s.EndsWith("KM", System.StringComparison.Ordinal)) return null;
            int k = 0;
            while (k < s.Length && char.IsLetter(s[k])) k++;
            string letters = s.Substring(0, k), digits = s.Substring(k).Trim('-', ' ', '_', '/');
            if (letters.Length == 0 || digits.Length == 0) return null;
            for (int i = 0; i < digits.Length; i++) if (!char.IsDigit(digits[i])) return null;
            return new[] { letters, digits };
        }

        /// <summary>Airbase marks and names on a theatre plate; owned by the room, one label pool each.</summary>
        public sealed class AirbaseLayer
        {
            private readonly TMP_Text[] names = new TMP_Text[FrontRoomView.MaxAirbases];

            public AirbaseLayer(RectTransform body)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    names[i] = FrontKit.Mono(body, "AirbaseLabel" + i, 0f, 0f, 90f, 14f, 9.5f, TextAlignmentOptions.TopLeft);
                    names[i].gameObject.SetActive(false);
                }
            }

            public static Color Ink(AirbaseSide s) => s == AirbaseSide.Own ? AvInk.State(AvState.Ready) : s == AirbaseSide.Enemy ? AvInk.Hostile : AvInk.Dim;

            /// <summary>The glyphs; draw before the room's own markers so they sit underneath.</summary>
            public static void Glyphs(FrontVector g, List<MapAirbaseView> list, System.Func<float, float, Vector2> p)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    Vector2 c = p(list[i].U, list[i].V);
                    Color k = Ink(list[i].Side);
                    g.Diamond(c, 6.5f, AvInk.Ground);
                    g.Diamond(c, 4.5f, k.WithAlpha(0.45f));
                    g.Line(c + new Vector2(-4f, 0f), c + new Vector2(4f, 0f), 1.6f, k);
                    g.Line(c + new Vector2(0f, -4f), c + new Vector2(0f, 4f), 1.6f, k);
                }
            }

            /// <summary>The names, last: <paramref name="place"/> returns an empty rect when no clear spot is left, and then the name is dropped.</summary>
            public void Names(FrontVector g, List<MapAirbaseView> list, System.Func<float, float, Vector2> p, FrontMapView view, System.Func<TMP_Text, Vector2, Rect> place)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    TMP_Text t = names[i];
                    if (i >= list.Count) { t.gameObject.SetActive(false); continue; }
                    Vector2 a = p(list[i].U, list[i].V);
                    if (!view.Visible(a, 10f)) { t.gameObject.SetActive(false); continue; }
                    t.gameObject.SetActive(true);
                    FrontKit.Set(t, list[i].Name, Ink(list[i].Side));
                    Rect r = place(t, a);
                    if (r.width <= 0f) { t.gameObject.SetActive(false); continue; }
                    Plate(g, r);
                }
            }
        }

        public static void Plate(FrontVector g, Rect r) => g.Rect(r.x - 2f, r.y - 1f, r.width + 4f, r.height + 2f, AvInk.Ground.WithAlpha(0.72f));
    }

    /// <summary>First-fit label placement beside an anchor on a map: the first spot clear of every taken rect and inside the map.</summary>
    internal sealed class FrontLabels
    {
        private readonly Rect map;
        private readonly List<Rect> fixedRects = new List<Rect>(32), taken = new List<Rect>(96);

        public FrontLabels(float mapX, float mapY, float mapW, float mapH) { map = new Rect(mapX, mapY, mapW, mapH); }

        /// <summary>A permanent obstacle (an overlay card).</summary>
        public void Fixed(Rect r) => fixedRects.Add(r);

        public void Reset() { taken.Clear(); taken.AddRange(fixedRects); }

        /// <summary>Whether a rect touches an overlay card (those never give way).</summary>
        public bool Blocked(Rect r)
        {
            foreach (Rect o in fixedRects) if (r.Overlaps(o)) return true;
            return false;
        }

        public void Take(Rect r) => taken.Add(r);

        /// <summary>With <paramref name="optional"/> a spot must be fully clear; otherwise nothing is placed and an empty rect comes back.</summary>
        public Rect Place(TMP_Text t, Vector2 anchor, float off, bool single, bool optional = false)
        {
            t.ForceMeshUpdate();
            float lw = Mathf.Ceil(t.GetPreferredValues(t.text, 400f, 100f).x) + 4f, lh = single ? 14f : 28f;
            Vector2[] cands =
            {
                new Vector2(off, -lh - 2f), new Vector2(off, 4f), new Vector2(-off - lw, -lh - 2f), new Vector2(-off - lw, 4f),
                new Vector2(-lw * 0.5f, -lh - off), new Vector2(-lw * 0.5f, off), new Vector2(off, -lh * 0.5f), new Vector2(-off - lw, -lh * 0.5f),
            };
            Rect best = default;
            bool found = false;
            float bestScore = float.MaxValue;
            for (int i = 0; i < cands.Length; i++)
            {
                var r = new Rect(anchor.x + cands[i].x, anchor.y + cands[i].y, lw, lh);
                if (r.xMin < map.xMin + 2f || r.xMax > map.xMax - 2f || r.yMin < map.yMin + 2f || r.yMax > map.yMax - 2f) continue;
                float score = 0f;
                foreach (Rect o in fixedRects) { float ox = Mathf.Min(r.xMax, o.xMax) - Mathf.Max(r.xMin, o.xMin), oy = Mathf.Min(r.yMax, o.yMax) - Mathf.Max(r.yMin, o.yMin); if (ox > 0f && oy > 0f) score += ox * oy * 50f; }
                foreach (Rect o in taken) { float ox = Mathf.Min(r.xMax, o.xMax) - Mathf.Max(r.xMin, o.xMin), oy = Mathf.Min(r.yMax, o.yMax) - Mathf.Max(r.yMin, o.yMin); if (ox > 0f && oy > 0f) score += ox * oy; }
                if (score < bestScore) { bestScore = score; best = r; found = true; if (score <= 0f) break; }
            }
            if (optional && (!found || bestScore > 0f)) return default;
            if (!found) best = new Rect(Mathf.Clamp(anchor.x + off, map.xMin + 2f, map.xMax - lw - 2f), Mathf.Clamp(anchor.y - lh - 2f, map.yMin + 2f, map.yMax - lh - 2f), lw, lh);
            t.alignment = TextAlignmentOptions.TopLeft;
            AvLay.Place(t.rectTransform, best.x, best.y, lw, lh);
            taken.Add(best);
            return best;
        }
    }
}

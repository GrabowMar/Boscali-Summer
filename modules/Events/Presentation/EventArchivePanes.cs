using NOAvionics;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>One labelled reading in the archive detail grid.</summary>
    internal readonly struct DetailFigure
    {
        public readonly string Key, Value, Note;
        public readonly AvState State;

        public DetailFigure(string key, string value, string note = null, AvState state = AvState.Inert)
        {
            Key = key;
            Value = value;
            Note = note;
            State = state;
        }
    }

    /// <summary>
    /// A framed, independently scrolling pane hosting its own <see cref="AvFlow"/>. The archive window's body
    /// is one flow; the two panes are how it shows an index and a detail side by side without either one
    /// making the whole window taller than the screen.
    /// </summary>
    internal sealed class ScrollPane
    {
        private readonly AvFrame frame;
        private readonly AvScrollView scrollView;

        public RectTransform Root { get; }
        public RectTransform Content { get; }
        public AvFlow Flow { get; }

        public ScrollPane(RectTransform parent, AvTicker ticker, float width)
        {
            Root = AvLay.Child(parent, "Pane");
            frame = AvFrame.Add(Root, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(frame.rectTransform);
            frame.raycastTarget = true;   // wheel input anywhere over the pane reaches its scroll rect
            scrollView = new AvScrollView(Root, true);
            Content = scrollView.Content;
            Flow = new AvFlow(Content, ticker, width);
            Restyle();
        }

        public void Place(float x, float y, float w, float h)
        {
            AvLay.Place(Root, x, y, w, h);
            scrollView.Place(w, h);
            Flow.ViewportHeight = h;   // a growing part (the dossier's poster) takes the pane's spare height
        }

        public void ScrollToTop() => scrollView.Scroll.verticalNormalizedPosition = 1f;

        public void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card inert");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            scrollView.Restyle();
        }
    }

    /// <summary>The index pane (about 40%) and the detail pane beside it, at a fixed height.</summary>
    internal sealed class ArchivePanesPart : AvPart
    {
        private const float PaneGap = 10f;
        private readonly float height, leftWidth, rightWidth;

        public ScrollPane Left { get; }
        public ScrollPane Right { get; }

        public ArchivePanesPart(RectTransform parent, AvTicker ticker, float innerWidth, float paneHeight)
        {
            Rect = AvLay.Child(parent, "Panes");
            height = paneHeight;
            // The list's two pager buttons and full record range need at least 288 px inside its padding.
            leftWidth = Mathf.Max(330f, Mathf.Floor(innerWidth * 0.38f));
            rightWidth = innerWidth - leftWidth - PaneGap;
            Left = new ScrollPane(Rect, ticker, leftWidth);
            Right = new ScrollPane(Rect, ticker, rightWidth);
        }

        public void Relayout()
        {
            Left.Flow.Relayout();
            Right.Flow.Relayout();
        }

        public override float Measure(float width) => height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            Left.Place(0f, 0f, leftWidth, s.H);
            Right.Place(leftWidth + PaneGap, 0f, rightWidth, s.H);
        }

        public override void Restyle()
        {
            Left.Restyle();
            Right.Restyle();
        }
    }

    /// <summary>
    /// The archive's detail pane: kicker, title, poster or model view, a grid of figures, the prose and (for a
    /// superevent) its timed orders. One arrangement routine serves Measure and Place, and every setter calls
    /// Changed(), so a long dossier grows the pane's scrollable height instead of running under anything.
    /// </summary>
    internal sealed class EventDetailPart : AvPart
    {
        private const int MaxFigures = 6;
        private const float PosterHeight = 172f, ModelHeight = 250f, FigureGap = 6f;

        private enum Media { None, Poster, Model }

        private sealed class Cell
        {
            public readonly AvFrame Frame;
            public readonly TMP_Text Key, Value, Note;
            public AvState State;

            public Cell(RectTransform parent)
            {
                Frame = AvFrame.Add(parent, "Cell", AvChamfer.Diagonal(4f));
                Key = AvText.Make(parent, "CellKey", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft);
                Value = AvText.Make(parent, "CellValue", AvTextRole.DataStrong, "", TextAlignmentOptions.TopLeft, true);
                Note = AvText.Make(parent, "CellNote", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft);
                AvText.Fit(Key, false);
                AvText.Fit(Value, true);
                AvText.Fit(Note, false);
            }

            public void SetActive(bool on)
            {
                Frame.gameObject.SetActive(on);
                Key.gameObject.SetActive(on);
                Value.gameObject.SetActive(on);
                Note.gameObject.SetActive(on);
            }
        }

        private readonly TMP_Text kicker, title, body, ordersKey, orders, previewNote;
        private readonly Image mediaBack, rule;
        private readonly EventsMfdPanel.EventPlateArt plate;
        private readonly RawImage modelView;
        private readonly AvControl rotateLeft, rotateRight;
        private readonly Cell[] cells = new Cell[MaxFigures];
        private Media media = Media.None;
        private int figureCount;
        private bool previewAvailable = true;

        public Action<float> Rotate;

        public EventDetailPart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Detail");
            kicker = AvText.Make(Rect, "Kicker", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
            title = AvText.Make(Rect, "Title", AvTextRole.Title, "", TextAlignmentOptions.TopLeft, true);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            mediaBack = AvLay.Solid(Rect, "MediaBack", Color.clear);
            plate = new EventsMfdPanel.EventPlateArt(Rect, 34f);
            var modelGo = new GameObject("Model", typeof(RectTransform), typeof(CanvasRenderer));
            modelGo.transform.SetParent(Rect, false);
            modelView = modelGo.AddComponent<RawImage>();
            modelView.raycastTarget = false;
            previewNote = AvText.Make(Rect, "PreviewNote", AvTextRole.Micro, "MODEL PREVIEW UNAVAILABLE", TextAlignmentOptions.Center);
            rotateLeft = AvControl.Make(Rect, new AvControl.Spec("", () => Rotate?.Invoke(-30f), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
            rotateRight = AvControl.Make(Rect, new AvControl.Spec("", () => Rotate?.Invoke(30f), AvButtonStyle.Quiet, AvIcon.ChevronRight));
            rotateLeft.Help = "Turn the model 30 degrees left.";
            rotateRight.Help = "Turn the model 30 degrees right.";
            for (int i = 0; i < cells.Length; i++) cells[i] = new Cell(Rect);
            body = AvText.Make(Rect, "Body", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
            ordersKey = AvText.Make(Rect, "OrdersKey", AvTextRole.Micro, "TIMED ORDERS", TextAlignmentOptions.TopLeft);
            orders = AvText.Make(Rect, "Orders", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft, true);
            ApplyVisibility();
            Restyle();
        }

        private void SetFigures(IList<DetailFigure> figures)
        {
            figureCount = Mathf.Min(figures != null ? figures.Count : 0, MaxFigures);
            for (int i = 0; i < cells.Length; i++)
            {
                bool on = i < figureCount;
                cells[i].SetActive(on);
                if (!on) continue;
                cells[i].Key.text = figures[i].Key ?? "";
                cells[i].Value.text = figures[i].Value ?? "";
                cells[i].Note.text = figures[i].Note ?? "";
                cells[i].State = figures[i].State;
            }
        }

        private void ApplyVisibility()
        {
            plate.Root.gameObject.SetActive(media == Media.Poster);
            mediaBack.gameObject.SetActive(media == Media.Model);
            modelView.gameObject.SetActive(media == Media.Model);
            rotateLeft.gameObject.SetActive(media == Media.Model && previewAvailable);
            rotateRight.gameObject.SetActive(media == Media.Model && previewAvailable);
            previewNote.gameObject.SetActive(media == Media.Model && !previewAvailable);
            bool hasOrders = orders.text.Length > 0;
            ordersKey.gameObject.SetActive(hasOrders);
            orders.gameObject.SetActive(hasOrders);
        }

        private void Apply(string kick, string headline, string text, string timedOrders)
        {
            kicker.text = kick ?? "";
            title.text = headline ?? "";
            body.text = text ?? "";
            orders.text = timedOrders ?? "";
            ApplyVisibility();
            Restyle();
            Changed();
        }

        public void ShowEmpty(string headline, string note)
        {
            media = Media.None;
            SetFigures(null);
            Apply("", headline, note, null);
        }

        public void ShowDoc(string code, string headline, string text)
        {
            media = Media.None;
            SetFigures(null);
            Apply(code, headline, text, null);
        }

        public void ShowEvent(string kick, string headline, Sprite art, AvIcon glyph, IList<DetailFigure> figures,
            string flavorText, string timedOrders)
        {
            media = Media.Poster;
            plate.Bind(art, glyph, AvStyleHost.FuiColor("ink-dim", AvTheme.Dim));
            SetFigures(figures);
            Apply(kick, headline, flavorText, timedOrders);
        }

        public RawImage ShowAircraft(string kick, string headline, IList<DetailFigure> figures, string description, string note)
        {
            media = Media.Model;
            previewAvailable = true;
            SetFigures(figures);
            Apply(kick, headline, string.IsNullOrEmpty(note) ? description : description + "\n\n" + note, null);
            return modelView;
        }

        public void SetPreviewAvailable(bool available)
        {
            if (previewAvailable == available) return;
            previewAvailable = available;
            ApplyVisibility();
        }

        public override float Measure(float width) => Arrange(width, false);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            Arrange(s.W, true, Mathf.Max(0f, s.H - Arrange(s.W, false)));
        }

        private float Arrange(float w, bool place, float extra = 0f)
        {
            float y = 0f;
            float kh = kicker.text.Length > 0 ? AvText.Height(kicker, w) : 0f;
            if (place) AvLay.Place(kicker.rectTransform, 0f, y, w, kh);
            if (kh > 0f) y += kh + 2f;
            float th = AvText.Height(title, w);
            if (place) AvLay.Place(title.rectTransform, 0f, y, w, th);
            y += th + 8f;
            if (place) AvLay.Place(rule.rectTransform, 0f, y, w, 1f);
            y += 1f + 10f;

            if (figureCount > 0)
            {
                int cols = Mathf.Min(w < 500f ? 2 : 3, figureCount);
                float cw = (w - (cols - 1) * FigureGap) / cols;
                for (int first = 0; first < figureCount; first += cols)
                {
                    float rowH = 46f;
                    for (int i = first; i < Mathf.Min(first + cols, figureCount); i++)
                    {
                        Cell cell = cells[i];
                        float valueH = Mathf.Max(20f, AvText.Height(cell.Value, cw - 20f));
                        rowH = Mathf.Max(rowH, 6f + 15f + valueH +
                            (cell.Note.text.Length > 0 ? 2f + 15f : 0f) + 6f);
                    }
                    if (place)
                        for (int i = first; i < Mathf.Min(first + cols, figureCount); i++)
                        {
                            float cx = (i - first) * (cw + FigureGap);
                            Cell cell = cells[i];
                            float valueH = Mathf.Max(20f, AvText.Height(cell.Value, cw - 20f));
                            AvLay.Place(cell.Frame.rectTransform, cx, y, cw, rowH);
                            AvLay.Place(cell.Key.rectTransform, cx + 10f, y + 6f, cw - 20f, 15f);
                            AvLay.Place(cell.Value.rectTransform, cx + 10f, y + 21f, cw - 20f, valueH);
                            AvLay.Place(cell.Note.rectTransform, cx + 10f, y + 23f + valueH, cw - 20f,
                                cell.Note.text.Length > 0 ? 15f : 0f);
                        }
                    y += rowH + (first + cols < figureCount ? FigureGap : 12f);
                }
            }

            if (media != Media.None)
            {
                float mh = media == Media.Model ? ModelHeight : PosterHeight;
                // Spare pane height goes to the media, but never so much that the art is sliced (poster 1.4:1, model 1.6:1).
                if (place && extra > 0f) mh += Mathf.Clamp((media == Media.Model ? w / 1.6f : w / 1.4f) - mh, 0f, extra);
                if (place)
                {
                    if (media == Media.Poster)
                    {
                        AvLay.Place(plate.Root, 0f, y, w, mh);
                        plate.Layout(w, mh);
                    }
                    else
                    {
                        AvLay.Place(mediaBack.rectTransform, 0f, y, w, mh);
                        AvLay.Place(modelView.rectTransform, 0f, y, w, mh);
                        modelView.uvRect = EventsMfdPanel.CropUv(new Rect(0f, 0f, 1f, 1f), w, mh);
                        AvLay.Place(previewNote.rectTransform, 8f, y + mh * 0.5f - 8f, w - 16f, 16f);
                        AvLay.Place(rotateLeft.Rect, w - 8f - 62f - 4f, y + mh - 8f - 26f, 30f, 26f);
                        AvLay.Place(rotateRight.Rect, w - 8f - 30f, y + mh - 8f - 26f, 30f, 26f);
                    }
                }
                y += mh + 12f;
            }

            if (body.text.Length > 0)
            {
                float bh = AvText.Height(body, w);
                if (place) AvLay.Place(body.rectTransform, 0f, y, w, bh);
                y += bh + 12f;
            }

            if (orders.text.Length > 0)
            {
                float kh2 = AvText.Height(ordersKey, w);
                if (place) AvLay.Place(ordersKey.rectTransform, 0f, y, w, kh2);
                y += kh2 + 2f;
                float oh = AvText.Height(orders, w);
                if (place) AvLay.Place(orders.rectTransform, 0f, y, w, oh);
                y += oh + 12f;
            }
            return Mathf.Max(1f, y - 12f);
        }

        public override void Restyle()
        {
            kicker.color = AvStyleHost.FuiColor("key", AvTheme.RailInfo);
            title.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            rule.color = AvStyleHost.FuiColor("hairline", AvTheme.Hairline);
            mediaBack.color = AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
            modelView.color = Color.white;
            previewNote.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
            body.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            ordersKey.color = AvStyleHost.FuiColor("key", AvTheme.RailInfo);
            orders.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
            AvStyle card = AvStyleHost.FuiStyle("card inert");
            foreach (Cell cell in cells)
            {
                cell.Frame.Paint(AvStyleHost.Resolve(card.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(card.Border, AvTheme.Hairline));
                cell.Key.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                cell.Value.color = cell.State == AvState.Inert
                    ? AvStyleHost.FuiColor("ink", AvTheme.TextPrimary) : EventsMfdPanel.TextColor(cell.State);
                cell.Note.color = EventsMfdPanel.TextColor(cell.State);
            }
            plate.Restyle();
            rotateLeft.Restyle();
            rotateRight.Restyle();
        }
    }
}

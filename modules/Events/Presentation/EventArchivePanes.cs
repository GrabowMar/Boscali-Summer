using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// A framed, independently scrolling pane hosting its own <see cref="AvFlow"/>. The archive window's body
    /// is one flow; the two panes are how it shows an index and a record side by side without either one
    /// making the whole window taller than the screen.
    /// </summary>
    internal sealed class ScrollPane
    {
        private readonly AvFrame frame;
        private readonly AvScrollView scrollView;
        private float viewHeight;

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
            viewHeight = h;
            Flow.ViewportHeight = h;   // a growing part (the card's plate) takes the pane's spare height
        }

        public void ScrollToTop() => scrollView.Scroll.verticalNormalizedPosition = 1f;

        /// <summary>Brings content offset <paramref name="y"/> to the top of the pane, as far as the content allows.</summary>
        public void ScrollTo(float y)
        {
            float travel = Flow.ContentHeight - viewHeight;
            scrollView.Scroll.verticalNormalizedPosition = travel > 1f ? 1f - Mathf.Clamp01(y / travel) : 1f;
        }

        public void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card inert");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            scrollView.Restyle();
        }
    }

    /// <summary>The index pane and the record pane beside it, at a fixed height.</summary>
    internal sealed class ArchivePanesPart : AvPart
    {
        private const float PaneGap = 8f;
        private readonly float height, leftWidth, rightWidth;

        public ScrollPane Left { get; }
        public ScrollPane Right { get; }

        public ArchivePanesPart(RectTransform parent, AvTicker ticker, float innerWidth, float paneHeight)
        {
            Rect = AvLay.Child(parent, "Panes");
            height = paneHeight;
            // The index's pager line (two buttons and the record range) needs about 280 px inside its padding.
            leftWidth = Mathf.Clamp(Mathf.Floor(innerWidth * 0.27f), 304f, 372f);
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

    /// <summary>Small builders the archive's parts share.</summary>
    internal static class ArchiveUi
    {
        public static AvQuadGraphic Quads(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var quads = go.AddComponent<AvQuadGraphic>();
            quads.raycastTarget = false;
            return quads;
        }

        /// <summary>Four corner brackets just inside a w x h plate, the way a printed plate is registered.</summary>
        public static void Brackets(AvQuadGraphic quads, float w, float h, Color c, float inset = 6f, float arm = 16f)
        {
            const float t = 2f;
            float x1 = w - inset, y1 = h - inset;
            quads.Add(inset, inset, arm, t, c); quads.Add(inset, inset, t, arm, c);
            quads.Add(x1 - arm, inset, arm, t, c); quads.Add(x1 - t, inset, t, arm, c);
            quads.Add(inset, y1 - t, arm, t, c); quads.Add(inset, y1 - arm, t, arm, c);
            quads.Add(x1 - arm, y1 - t, arm, t, c); quads.Add(x1 - t, y1 - arm, t, arm, c);
        }

        public static string Two(int n) => AvNum.Fixed(n, 0).PadLeft(2, '0');
    }

    /// <summary>
    /// The library's header band: the issuing section's block, the title, who issued it and under which
    /// reference, how many records the shelves hold, and a classification strip along the bottom edge. All of it
    /// is fixed text in the section's voice except the holdings count, which is the real number of records.
    /// </summary>
    internal sealed class LibraryHeaderPart : AvPart
    {
        private const string Notice = "RESTRICTED  ·  AIRCREW REFERENCE  ·  THIS COPY IS HELD AT YOUR STATION; NOTHING ON ITS SHELVES IS TRANSMITTED";
        private const string ShortNotice = "RESTRICTED  ·  AIRCREW REFERENCE  ·  LOCAL COPY, NOT TRANSMITTED";

        private readonly AvFrame frame, crestBox;
        private readonly Image strip;
        private readonly TMP_Text crest, title, issuer, holdings, reference, notice;
        private readonly bool compact;

        public LibraryHeaderPart(RectTransform parent, bool compact)
        {
            this.compact = compact;
            Rect = AvLay.Child(parent, "LibraryHeader");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(frame.rectTransform);
            crestBox = AvFrame.Add(Rect, "CrestBox", AvChamfer.Diagonal(4f));
            crestBox.Bracket = 5f;
            crest = AvText.Make(Rect, "Crest", AvTextRole.Head, "AIS", TextAlignmentOptions.Center);
            title = AvText.Make(Rect, "Title", AvTextRole.Title, "RECOGNITION & REFERENCE LIBRARY");
            issuer = AvText.Make(Rect, "Issuer", AvTextRole.Micro,
                "ISSUED BY AIR INTELLIGENCE SECTION, EVENT DIRECTORATE  ·  FOR USE IN AIRCREW BRIEFING ROOMS");
            holdings = AvText.Make(Rect, "Holdings", AvTextRole.Head, "", TextAlignmentOptions.MidlineRight);
            reference = AvText.Make(Rect, "Reference", AvTextRole.Micro,
                "EVD PUB 2-14  ·  EDITION 3  ·  AMENDMENT LIST 07", TextAlignmentOptions.MidlineRight);
            strip = AvLay.Solid(Rect, "Classification", Color.clear);
            notice = AvText.Make(strip.rectTransform, "Notice", AvTextRole.Micro, Notice, TextAlignmentOptions.Center);
            AvLay.Fill(notice.rectTransform);
            AvHelpTip.Attach(frame.gameObject,
                "The field archive is a reference library. It is local to your station: opening or reading a record sends nothing to the host.");
            frame.raycastTarget = true;
            Restyle();
        }

        /// <summary>The real number of records on the shelves; a dash until any are held.</summary>
        public void SetHoldings(int records, int shelves) =>
            holdings.text = "HOLDINGS  " + (records > 0 ? AvNum.Thousands(records) : "—") + " RECORDS  ·  " +
                AvNum.Fixed(shelves, 0) + " SHELVES";

        public override float Measure(float width) => compact ? 47f : 66f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            const float stripH = 16f;
            float top = s.H - stripH - 1f, x = 62f;
            AvLay.Place(crestBox.rectTransform, 8f, 6f, 44f, top - 11f);
            AvLay.Place(crest.rectTransform, 8f, 6f, 44f, top - 11f);
            float rightW = Mathf.Max(AvText.Width(holdings), compact ? 0f : AvText.Width(reference)) + 2f;
            float leftW = s.W - x - rightW - 26f;
            issuer.gameObject.SetActive(!compact && AvText.Width(issuer) <= leftW);
            reference.gameObject.SetActive(!compact);
            if (compact)
            {
                AvLay.Place(title.rectTransform, x, 4f, leftW, top - 6f);
                AvLay.Place(holdings.rectTransform, s.W - 10f - rightW, 4f, rightW, top - 6f);
            }
            else
            {
                AvLay.Place(title.rectTransform, x, 5f, leftW, 24f);
                AvLay.Place(issuer.rectTransform, x, 29f, leftW, 15f);
                AvLay.Place(holdings.rectTransform, s.W - 10f - rightW, 7f, rightW, 20f);
                AvLay.Place(reference.rectTransform, s.W - 10f - rightW, 29f, rightW, 15f);
            }
            AvLay.Place(strip.rectTransform, 1f, top, s.W - 2f, stripH);
            notice.text = notice.GetPreferredValues(Notice, 100000f, 0f).x <= s.W - 24f ? Notice : ShortNotice;
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            crestBox.Paint(AvInk.Inert, AvInk.Frame);
            crestBox.BracketColor = AvInk.Key;
            crest.color = AvInk.Key;
            title.color = AvInk.Ink;
            issuer.color = AvInk.Key;
            holdings.color = AvInk.Ink;
            reference.color = AvInk.Dim;
            strip.color = EventsMfdPanel.SlabBack(AvState.Caution);
            notice.color = EventsMfdPanel.SlabInk();
        }
    }

    /// <summary>
    /// The shelves as index tabs: a drawer number, the shelf name and how many records it holds. The open shelf
    /// stands proud of the rest. Counts are real; an empty shelf reads a dash.
    /// </summary>
    internal sealed class ShelfTabsPart : AvPart
    {
        private const float Gap = 4f;

        private sealed class Tab
        {
            public RectTransform Root;
            public AvFrame Frame;
            public Image Rail;
            public TMP_Text Drawer, Name, Count, Icon;
            public AvHelpTip Tip;
            public bool Hover;
        }

        private readonly Tab[] tabs = new Tab[ArchiveShelf.Count];
        private readonly Image baseline;
        private readonly bool compact;
        private int selected = -1;

        /// <summary>The shelf the pilot clicked.</summary>
        public Action<int> Picked;

        public ShelfTabsPart(RectTransform parent, bool compact)
        {
            this.compact = compact;
            Rect = AvLay.Child(parent, "ShelfTabs");
            baseline = AvLay.Solid(Rect, "Baseline", Color.clear);
            for (int i = 0; i < tabs.Length; i++)
            {
                int shelf = i;
                var tab = new Tab { Root = AvLay.Child(Rect, "Shelf " + ArchiveShelf.Names[i]) };
                tab.Frame = AvFrame.Add(tab.Root, "Frame", new AvChamfer { TL = 7f, TR = 7f });
                AvLay.Fill(tab.Frame.rectTransform);
                tab.Rail = AvLay.Solid(tab.Root, "Rail", Color.clear);
                tab.Drawer = AvText.Make(tab.Root, "Drawer", AvTextRole.Micro, ArchiveUi.Two(i + 1));
                tab.Name = AvText.Make(tab.Root, "Name", AvTextRole.Head, ArchiveShelf.Names[i]);
                AvText.Fit(tab.Name, false);
                tab.Icon = AvIcons.Make(tab.Root, ArchiveShelf.Icons[i], AvGridTokens.IconInline, Color.white);
                tab.Count = AvText.Make(tab.Root, "Count", AvTextRole.DataSmall, "—", TextAlignmentOptions.MidlineRight);
                AvHit hit = AvHit.On(tab.Frame);
                hit.Hover = h => { tab.Hover = h; Restyle(); };
                hit.Click = e => Picked?.Invoke(shelf);
                tab.Tip = AvHelpTip.Attach(tab.Frame.gameObject, ArchiveShelf.Help[i]);
                tabs[i] = tab;
            }
            Restyle();
        }

        public void Select(int shelf)
        {
            if (selected == shelf) return;
            selected = shelf;
            Restyle();
        }

        public void SetCount(int shelf, int records)
        {
            tabs[shelf].Count.text = records > 0 ? AvNum.Fixed(records, 0) : "—";
            tabs[shelf].Tip.Text = ArchiveShelf.Help[shelf] + (records > 0
                ? " " + AvNum.Fixed(records, 0) + " records held."
                : " No records are held: the game's encyclopedia has not issued any to this station.");
        }

        public override float Measure(float width) => compact ? 38f : 43f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = (s.W - (tabs.Length - 1) * Gap) / tabs.Length, h = s.H - 1f;
            for (int i = 0; i < tabs.Length; i++)
            {
                Tab tab = tabs[i];
                AvLay.Place(tab.Root, i * (w + Gap), 0f, w, h);
                AvLay.Place(tab.Rail.rectTransform, 7f, 0f, w - 14f, 2f);
                tab.Drawer.gameObject.SetActive(!compact);
                if (compact)
                {
                    AvLay.Place(tab.Name.rectTransform, 6f, 3f, w - 12f, 16f);
                    AvLay.Place(tab.Icon.rectTransform, 6f, 20f, 14f, 14f);
                    AvLay.Place(tab.Count.rectTransform, w - 6f - 44f, 19f, 44f, 16f);
                }
                else
                {
                    AvLay.Place(tab.Drawer.rectTransform, 8f, 5f, 18f, 15f);
                    AvLay.Place(tab.Name.rectTransform, 28f, 3f, w - 34f, 19f);
                    AvLay.Place(tab.Icon.rectTransform, 8f, 23f, 14f, 14f);
                    AvLay.Place(tab.Count.rectTransform, w - 8f - 70f, 22f, 70f, 17f);
                }
            }
            AvLay.Place(baseline.rectTransform, 0f, s.H - 1f, s.W, 1f);
        }

        public override void Restyle()
        {
            baseline.color = AvInk.Frame;
            for (int i = 0; i < tabs.Length; i++)
            {
                Tab tab = tabs[i];
                bool open = i == selected;
                AvStyle style = AvStyleHost.FuiStyle("tab", open ? "latched" : tab.Hover ? "hover" : null);
                tab.Frame.Paint(AvStyleHost.Resolve(style.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(style.Border, AvTheme.Hairline));
                tab.Rail.color = style.Rail.HasValue ? AvStyleHost.Resolve(style.Rail, Color.clear) : Color.clear;
                Color ink = AvStyleHost.Resolve(style.Color, AvTheme.Dim);
                tab.Name.color = ink;
                tab.Icon.color = ink;
                tab.Count.color = open ? AvInk.Ink : AvInk.Dim;
                tab.Drawer.color = AvInk.Key;
            }
        }
    }

    /// <summary>The index pane's heading: which drawer is open, how it is filed, and the three column captions.</summary>
    internal sealed class IndexHeadPart : AvPart
    {
        private readonly TMP_Text drawer, count, colCode, colName, colTag;
        private readonly Image rule;

        public IndexHeadPart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "IndexHead");
            drawer = AvText.Make(Rect, "Drawer", AvTextRole.Head, "");
            AvText.Fit(drawer, false);
            count = AvText.Make(Rect, "Count", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            colCode = AvText.Make(Rect, "ColCode", AvTextRole.Micro, "");
            colName = AvText.Make(Rect, "ColName", AvTextRole.Micro, "");
            colTag = AvText.Make(Rect, "ColTag", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            Restyle();
        }

        public void Set(string drawerText, string countText, string code, string name, string tag)
        {
            drawer.text = drawerText;
            count.text = countText;
            colCode.text = code;
            colName.text = name;
            colTag.text = tag;
        }

        public override float Measure(float width) => 40f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float countW = AvText.Width(count) + 2f;
            AvLay.Place(drawer.rectTransform, 2f, 0f, s.W - countW - 10f, 20f);
            AvLay.Place(count.rectTransform, s.W - countW - 2f, 0f, countW, 20f);
            AvLay.Place(colCode.rectTransform, ArchiveIndexRow.BoxX, 21f, ArchiveIndexRow.BoxW, 15f);
            AvLay.Place(colName.rectTransform, ArchiveIndexRow.NameX, 21f, s.W - ArchiveIndexRow.NameX - ArchiveIndexRow.TagW - 12f, 15f);
            AvLay.Place(colTag.rectTransform, s.W - 8f - ArchiveIndexRow.TagW, 21f, ArchiveIndexRow.TagW, 15f);
            AvLay.Place(rule.rectTransform, 0f, s.H - 1f, s.W, 1f);
        }

        public override void Restyle()
        {
            drawer.color = AvInk.Key;
            count.color = AvInk.Dim;
            colCode.color = colName.color = colTag.color = AvInk.Dim;
            rule.color = AvInk.Hairline;
        }
    }

    /// <summary>
    /// One line of the index: the boxed designation, the record's name and its class or tier tag, and (while
    /// the drawer is short enough to afford it) a second line of filing detail. The open record is armed
    /// (accent fill, edge and rail); the name wraps instead of being cut.
    /// </summary>
    internal sealed class ArchiveIndexRow : AvPart
    {
        internal const float BoxX = 8f, BoxW = 64f, NameX = BoxX + BoxW + 8f, TagW = 62f;

        private readonly AvFrame frame, box;
        private readonly Image rail;
        private readonly TMP_Text code, name, sub, tag;
        private readonly AvHelpTip tip;
        private AvState state = AvState.Inert;
        private bool hover, chosen;

        /// <summary>The record this pooled row currently shows.</summary>
        public int Item;

        public ArchiveIndexRow(RectTransform parent, Action<int> onClick)
        {
            Rect = AvLay.Child(parent, "IndexRow");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            box = AvFrame.Add(Rect, "CodeBox", AvChamfer.Diagonal(3f));
            code = AvText.Make(Rect, "Code", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
            AvText.Fit(code, false);
            name = AvText.Make(Rect, "Name", AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
            sub = AvText.Make(Rect, "Sub", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
            tag = AvText.Make(Rect, "Tag", AvTextRole.Micro, "", TextAlignmentOptions.TopRight);
            AvHit hit = AvHit.On(frame);
            hit.Hover = h => { hover = h; Restyle(); };
            hit.Click = e => onClick?.Invoke(Item);
            tip = AvHelpTip.Attach(frame.gameObject, "");
            Restyle();
        }

        public void Set(int item, string codeText, string nameText, string subText, string tagText, AvState tint, bool open)
        {
            Item = item;
            code.text = codeText ?? "";
            name.text = nameText ?? "";
            sub.text = subText ?? "";
            tag.text = tagText ?? "";
            state = tint;
            chosen = open;
            tip.Text = "Open the " + (nameText ?? "").ToLowerInvariant() + " record. The up and down arrow keys step through the index.";
            Restyle();
        }

        private float NameWidth(float width) => width - NameX - TagW - 14f;

        private float SubHeight(float width) => sub.text.Length > 0 ? 1f + AvText.Height(sub, width - NameX - 10f) : 0f;

        public override float Measure(float width) =>
            Mathf.Max(30f, AvText.Height(name, NameWidth(width)) + SubHeight(width) + 13f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float nameH = AvText.Height(name, NameWidth(s.W)), subH = SubHeight(s.W), boxY = subH > 0f ? (s.H - 20f) * 0.5f : 5f;
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(box.rectTransform, BoxX, boxY, BoxW, 20f);
            AvLay.Place(code.rectTransform, BoxX + 3f, boxY, BoxW - 6f, 20f);
            AvLay.Place(name.rectTransform, NameX, 7f, NameWidth(s.W), nameH);
            AvLay.Place(sub.rectTransform, NameX, 8f + nameH, s.W - NameX - 10f, Mathf.Max(0f, subH - 1f));
            AvLay.Place(tag.rectTransform, s.W - 8f - TagW, 8f, TagW, 15f);
        }

        public override void Restyle()
        {
            AvStyle style = AvStyleHost.FuiStyle("row " + AvStates.Class(state), chosen ? "armed" : hover ? "hover" : null);
            frame.Paint(AvStyleHost.Resolve(style.Background, AvTheme.SurfaceInert),
                style.Border.HasValue ? AvStyleHost.Resolve(style.Border, Color.clear) : Color.clear);
            rail.color = AvStyleHost.Resolve(style.Rail, AvTheme.RailInfo);
            box.Paint(AvInk.Ground, chosen ? AvInk.Select : AvInk.Frame);
            code.color = AvInk.Ink;
            name.color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            sub.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            tag.color = state == AvState.Info ? AvInk.Dim : EventsMfdPanel.TextColor(state);
        }
    }

    /// <summary>
    /// What the index pane carries under its list: the drawer's standing figures (on a unit shelf, the card that
    /// leads the shelf in each figure; in the event catalog, how the dossiers divide) and, seated on the pane's
    /// bottom edge, a note of what the drawer holds. Every row is counted or read from the shelf itself.
    /// </summary>
    internal sealed class IndexFootPart : AvPart
    {
        internal const int MaxRows = 8;
        private const float CaptionH = 21f, RowH = 22f;

        private sealed class Row
        {
            public RectTransform Root;
            public AvFrame Frame;
            public TMP_Text Label, Code, Value;
            public AvHelpTip Tip;
            public bool Hover;
            public int Item = -1;
        }

        private readonly TMP_Text caption, note;
        private readonly Image rule, noteRule;
        private readonly Row[] rows = new Row[MaxRows];
        private int count;

        /// <summary>The record a clicked leader row points at.</summary>
        public Action<int> Picked;

        public IndexFootPart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "IndexFoot");
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            caption = AvText.Make(Rect, "Caption", AvTextRole.Micro, "");
            for (int i = 0; i < MaxRows; i++)
            {
                var row = new Row { Root = AvLay.Child(Rect, "Leader " + i) };
                row.Frame = AvFrame.Add(row.Root, "Frame", default(AvChamfer));
                AvLay.Fill(row.Frame.rectTransform);
                row.Frame.Stroke = 0f;
                row.Label = AvText.Make(row.Root, "Label", AvTextRole.Micro, "");
                row.Code = AvText.Make(row.Root, "Code", AvTextRole.DataSmall, "");
                AvText.Fit(row.Code, false);
                row.Value = AvText.Make(row.Root, "Value", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                AvHit hit = AvHit.On(row.Frame);
                hit.Hover = h => { row.Hover = h; Restyle(); };
                hit.Click = e => { if (row.Item >= 0) Picked?.Invoke(row.Item); };
                row.Tip = AvHelpTip.Attach(row.Frame.gameObject, "");
                rows[i] = row;
            }
            noteRule = AvLay.Solid(Rect, "NoteRule", Color.clear);
            note = AvText.Make(Rect, "Note", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public void Begin(string captionText, string noteText)
        {
            count = 0;
            caption.text = captionText;
            note.text = noteText;
        }

        /// <param name="item">The record the row opens, or -1 for a row that only states a count.</param>
        public void Add(string label, string code, string value, int item, string help)
        {
            if (count >= MaxRows) return;
            Row row = rows[count++];
            row.Label.text = label;
            row.Code.text = code;
            row.Value.text = value;
            row.Item = item;
            row.Tip.Text = help;
        }

        public void End()
        {
            for (int i = 0; i < MaxRows; i++) rows[i].Root.gameObject.SetActive(i < count);
            caption.gameObject.SetActive(count > 0);
            rule.gameObject.SetActive(count > 0);
            Restyle();
            Changed();
        }

        private float RowsHeight => count > 0 ? 8f + CaptionH + count * RowH : 0f;

        public override float Measure(float width) => RowsHeight + 10f + AvText.Height(note, width - 4f) + 2f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(rule.rectTransform, 0f, 4f, s.W, 1f);
            AvLay.Place(caption.rectTransform, 2f, 10f, s.W - 4f, 16f);
            for (int i = 0; i < count; i++)
            {
                Row row = rows[i];
                AvLay.Place(row.Root, 0f, 8f + CaptionH + i * RowH, s.W, RowH - 2f);
                AvLay.Place(row.Label.rectTransform, 6f, 2f, 92f, 16f);
                AvLay.Place(row.Code.rectTransform, 100f, 1f, 64f, 18f);
                AvLay.Place(row.Value.rectTransform, 166f, 1f, s.W - 172f, 18f);
            }
            // The drawer note sits on the pane's bottom edge, the way a label plate sits on a drawer front.
            float noteH = AvText.Height(note, s.W - 4f);
            AvLay.Place(noteRule.rectTransform, 0f, s.H - noteH - 9f, s.W, 1f);
            AvLay.Place(note.rectTransform, 2f, s.H - noteH - 2f, s.W - 4f, noteH);
        }

        public override void Restyle()
        {
            rule.color = AvInk.Hairline;
            noteRule.color = AvInk.Hairline;
            caption.color = AvInk.Key;
            note.color = AvInk.Dim;
            foreach (Row row in rows)
            {
                bool lit = row.Hover && row.Item >= 0;
                AvStyle style = AvStyleHost.FuiStyle("row", lit ? "hover" : null);
                row.Frame.Paint(lit ? AvStyleHost.Resolve(style.Background, AvTheme.SurfaceInert) : Color.clear, Color.clear);
                row.Label.color = AvInk.Dim;
                row.Code.color = AvInk.Key;
                row.Value.color = AvInk.Ink;
            }
        }
    }
}

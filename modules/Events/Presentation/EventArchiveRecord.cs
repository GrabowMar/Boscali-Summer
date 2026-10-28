using NOAvionics;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// The head of every record: the boxed designation, the title, one line of filing detail and, where the
    /// pane is wide enough, the kind of record and its place in the drawer. A widget its record part places.
    /// </summary>
    internal sealed class RecordHead
    {
        private const float BoxH = 44f;
        private readonly AvFrame box;
        private readonly TMP_Text code, title, sub, stamp, serial;
        private readonly Image rule, cap;
        private AvState tint = AvState.Info;

        public RecordHead(RectTransform parent)
        {
            box = AvFrame.Add(parent, "HeadBox", AvChamfer.Diagonal(5f));
            box.Bracket = 6f;
            code = AvText.Make(parent, "HeadCode", AvTextRole.Display, "", TextAlignmentOptions.Center);
            AvText.Fit(code, false);
            title = AvText.Make(parent, "HeadTitle", AvTextRole.Headline, "", TextAlignmentOptions.TopLeft, true);
            sub = AvText.Make(parent, "HeadSub", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
            stamp = AvText.Make(parent, "HeadStamp", AvTextRole.Head, "", TextAlignmentOptions.TopRight);
            serial = AvText.Make(parent, "HeadSerial", AvTextRole.DataSmall, "", TextAlignmentOptions.TopRight);
            rule = AvLay.Solid(parent, "HeadRule", Color.clear);
            cap = AvLay.Solid(parent, "HeadCap", Color.clear);
            Restyle();
        }

        public void Set(string codeText, string titleText, string subText, string stampText, string serialText,
            AvState state = AvState.Info)
        {
            code.text = codeText ?? "";
            title.text = titleText ?? "";
            sub.text = subText ?? "";
            stamp.text = stampText ?? "";
            serial.text = serialText ?? "";
            tint = state;
            Restyle();
        }

        /// <summary>Lays the head out from <paramref name="y"/> and returns the height it took, its rule included.</summary>
        public float Arrange(float y, float w, bool place)
        {
            bool side = w >= 520f && stamp.text.Length > 0;
            float sideW = side ? Mathf.Max(AvText.Width(stamp), AvText.Width(serial)) + 2f : 0f;
            float boxW = Mathf.Clamp(AvText.Width(code) + 24f, 64f, Mathf.Min(156f, w * 0.3f));
            float tx = boxW + 12f, tw = w - tx - (side ? sideW + 14f : 0f);
            float th = AvText.Height(title, tw), sh = sub.text.Length > 0 ? AvText.Height(sub, tw) : 0f;
            float block = Mathf.Max(BoxH, th - 4f + sh);
            if (place)
            {
                AvLay.Place(box.rectTransform, 0f, y, boxW, BoxH);
                AvLay.Place(code.rectTransform, 6f, y, boxW - 12f, BoxH);
                AvLay.Place(title.rectTransform, tx, y - 4f, tw, th);
                AvLay.Place(sub.rectTransform, tx, y - 4f + th, tw, sh);
                stamp.gameObject.SetActive(side);
                serial.gameObject.SetActive(side);
                if (side)
                {
                    AvLay.Place(stamp.rectTransform, w - sideW, y + 2f, sideW, 18f);
                    AvLay.Place(serial.rectTransform, w - sideW, y + 22f, sideW, 16f);
                }
                AvLay.Place(rule.rectTransform, 0f, y + block + 7f, w, 1f);
                AvLay.Place(cap.rectTransform, 0f, y + block + 6f, boxW, 2f);
            }
            return block + 8f;
        }

        public void Restyle()
        {
            Color accent = tint == AvState.Info || tint == AvState.Inert ? AvInk.Key : AvInk.State(tint);
            box.Paint(AvInk.Inert, accent);
            box.BracketColor = accent;
            code.color = AvInk.Ink;
            title.color = AvInk.Ink;
            sub.color = AvInk.Key;
            stamp.color = AvInk.Dim;
            serial.color = AvInk.Dim;
            rule.color = AvInk.Hairline;
            cap.color = accent;
        }
    }

    /// <summary>
    /// A block of comparative figures: label, value (mono, right-aligned), unit, and under them a bar scaled
    /// against the largest value on the same shelf with the card's rank beside it. A figure the definition does
    /// not establish prints a dash and draws an empty track, never a zero-length reading.
    /// </summary>
    internal sealed class FigureBlock
    {
        internal const int Max = 8;
        private const float CaptionH = 21f, RowH = 28f, RowGap = 8f, ColGap = 18f, ValueW = 64f, UnitW = 36f;

        private readonly TMP_Text caption, hint;
        private readonly TMP_Text[] labels = new TMP_Text[Max], values = new TMP_Text[Max], units = new TMP_Text[Max],
            ranks = new TMP_Text[Max];
        private readonly AvQuadGraphic bars;
        private readonly float[] share = new float[Max], barX = new float[Max], barY = new float[Max];
        private readonly AvState fill;
        private float barW;
        private int count;
        private bool painted;

        public FigureBlock(RectTransform parent, string name, AvState barState)
        {
            fill = barState;
            bars = ArchiveUi.Quads(parent, name + "Bars");
            caption = AvText.Make(parent, name + "Caption", AvTextRole.Micro, "");
            hint = AvText.Make(parent, name + "Hint", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
            for (int i = 0; i < Max; i++)
            {
                labels[i] = AvText.Make(parent, name + "Label " + i, AvTextRole.Micro, "");
                values[i] = AvText.Make(parent, name + "Value " + i, AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
                units[i] = AvText.Make(parent, name + "Unit " + i, AvTextRole.Micro, "");
                ranks[i] = AvText.Make(parent, name + "Rank " + i, AvTextRole.Micro, "");
            }
            Restyle();
        }

        public void Begin(string captionText, string hintText)
        {
            count = 0;
            caption.text = captionText;
            hint.text = hintText;
        }

        /// <param name="fraction">The bar, 0..1; negative when the figure is not established.</param>
        public void Add(string label, string value, string unit, string rank, float fraction)
        {
            if (count >= Max) return;
            labels[count].text = label;
            values[count].text = value;
            units[count].text = unit;
            ranks[count].text = rank;
            share[count] = fraction;
            count++;
        }

        public void End()
        {
            bool any = count > 0;
            caption.gameObject.SetActive(any);
            hint.gameObject.SetActive(any);
            bars.gameObject.SetActive(any);
            for (int i = 0; i < Max; i++)
            {
                bool on = i < count;
                labels[i].gameObject.SetActive(on);
                values[i].gameObject.SetActive(on);
                units[i].gameObject.SetActive(on);
                ranks[i].gameObject.SetActive(on);
            }
            if (painted) Paint();
        }

        public float Measure(int cols)
        {
            if (count == 0) return 0f;
            int rows = (count + cols - 1) / cols;
            return CaptionH + rows * RowH + (rows - 1) * RowGap;
        }

        public void Place(float y, float w, int cols)
        {
            if (count == 0) return;
            float cw = (w - (cols - 1) * ColGap) / cols, h = Measure(cols);
            float captionW = AvText.Width(caption) + 2f, hintW = AvText.Width(hint) + 2f;
            AvLay.Place(caption.rectTransform, 0f, y, captionW, 16f);
            hint.gameObject.SetActive(hint.text.Length > 0 && captionW + 20f + hintW <= w);
            AvLay.Place(hint.rectTransform, w - hintW, y, hintW, 16f);
            AvLay.Place(bars.rectTransform, 0f, y, w, h);
            barW = cw - UnitW - 4f;
            for (int i = 0; i < count; i++)
            {
                float cx = (i % cols) * (cw + ColGap), cy = y + CaptionH + (i / cols) * (RowH + RowGap);
                AvLay.Place(labels[i].rectTransform, cx, cy, cw - ValueW - UnitW - 8f, 16f);
                AvLay.Place(values[i].rectTransform, cx + cw - UnitW - 4f - ValueW, cy - 1f, ValueW, 18f);
                AvLay.Place(units[i].rectTransform, cx + cw - UnitW, cy, UnitW, 16f);
                AvLay.Place(ranks[i].rectTransform, cx + cw - UnitW, cy + 14f, UnitW, 14f);
                barX[i] = cx;
                barY[i] = cy - y + 20f;
            }
            painted = true;
            Paint();
        }

        private void Paint()
        {
            Color track = AvInk.Hairline, ink = AvInk.State(fill);
            bars.Begin();
            // The caption's rule runs from the caption to the hint; the bars sit on their own tracks.
            for (int i = 0; i < count; i++)
            {
                bars.Add(barX[i], barY[i], barW, 5f, track);
                if (share[i] > 0f) bars.Add(barX[i], barY[i], Mathf.Max(2f, barW * Mathf.Clamp01(share[i])), 5f, ink);
            }
            bars.End();
        }

        public void Restyle()
        {
            caption.color = AvInk.Key;
            hint.color = AvInk.Dim;
            for (int i = 0; i < Max; i++)
            {
                labels[i].color = AvInk.Dim;
                values[i].color = AvInk.Ink;
                units[i].color = AvInk.Dim;
                ranks[i].color = AvInk.Dim;
            }
            if (painted) Paint();
        }
    }

    /// <summary>
    /// A record's cross-references: boxed designation and name of other records worth pulling, as clickable
    /// chips in whole rows. The record part decides how many rows its spare height can carry.
    /// </summary>
    internal sealed class RefStrip
    {
        internal const int Max = 12;
        internal const float RowPitch = ChipH + Gap;
        private const float CaptionH = 21f, ChipH = 30f, Gap = 6f, BoxW = 60f;

        private sealed class Chip
        {
            public RectTransform Root;
            public AvFrame Frame, Box;
            public TMP_Text Code, Name;
            public AvHelpTip Tip;
            public bool Hover;
            public int Item;
        }

        private readonly TMP_Text caption;
        private readonly Chip[] chips = new Chip[Max];
        private int count;

        /// <summary>The record a clicked chip points at.</summary>
        public Action<int> Picked;

        public RefStrip(RectTransform parent)
        {
            caption = AvText.Make(parent, "RefCaption", AvTextRole.Micro, "");
            for (int i = 0; i < Max; i++)
            {
                var chip = new Chip { Root = AvLay.Child(parent, "Ref " + i) };
                chip.Frame = AvFrame.Add(chip.Root, "Frame", default(AvChamfer));
                AvLay.Fill(chip.Frame.rectTransform);
                chip.Box = AvFrame.Add(chip.Root, "CodeBox", AvChamfer.Diagonal(3f));
                chip.Code = AvText.Make(chip.Root, "Code", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
                AvText.Fit(chip.Code, false);
                chip.Name = AvText.Make(chip.Root, "Name", AvTextRole.Label, "", TextAlignmentOptions.MidlineLeft);
                AvText.Fit(chip.Name, true);
                AvHit hit = AvHit.On(chip.Frame);
                hit.Hover = h => { chip.Hover = h; Restyle(); };
                hit.Click = e => Picked?.Invoke(chip.Item);
                chip.Tip = AvHelpTip.Attach(chip.Frame.gameObject, "");
                chip.Root.gameObject.SetActive(false);
                chips[i] = chip;
            }
            Restyle();
        }

        public void Begin(string captionText)
        {
            count = 0;
            caption.text = captionText;
        }

        public void Add(int item, string code, string name)
        {
            if (count >= Max) return;
            Chip chip = chips[count++];
            chip.Item = item;
            chip.Code.text = code;
            chip.Name.text = name;
            chip.Tip.Text = "Open the " + name.ToLowerInvariant() + " record.";
        }

        public static int Columns(float w) => w >= 840f ? 3 : w >= 520f ? 2 : 1;

        public int RowsFor(float w) => count == 0 ? 0 : (count + Columns(w) - 1) / Columns(w);

        public static float Height(int rows) => rows <= 0 ? 0f : CaptionH + rows * ChipH + (rows - 1) * Gap;

        /// <summary>Shows the first <paramref name="rows"/> rows from <paramref name="y"/>; zero rows hides the strip.</summary>
        public void Place(float y, float w, int rows)
        {
            int cols = Columns(w), shown = Mathf.Min(count, rows * cols);
            caption.gameObject.SetActive(shown > 0);
            AvLay.Place(caption.rectTransform, 0f, y, w, 16f);
            float cw = (w - (cols - 1) * Gap) / cols;
            for (int i = 0; i < Max; i++)
            {
                Chip chip = chips[i];
                bool on = i < shown;
                chip.Root.gameObject.SetActive(on);
                if (!on) continue;
                AvLay.Place(chip.Root, (i % cols) * (cw + Gap), y + CaptionH + (i / cols) * (ChipH + Gap), cw, ChipH);
                AvLay.Place(chip.Box.rectTransform, 6f, 5f, BoxW, 20f);
                AvLay.Place(chip.Code.rectTransform, 9f, 5f, BoxW - 6f, 20f);
                AvLay.Place(chip.Name.rectTransform, BoxW + 14f, 1f, cw - BoxW - 22f, ChipH - 2f);
            }
        }

        public void Restyle()
        {
            caption.color = AvInk.Key;
            foreach (Chip chip in chips)
            {
                AvStyle style = AvStyleHost.FuiStyle("row", chip.Hover ? "hover" : null);
                chip.Frame.Paint(AvStyleHost.Resolve(style.Background, AvTheme.SurfaceInert),
                    style.Border.HasValue ? AvStyleHost.Resolve(style.Border, Color.clear) : AvInk.Hairline);
                chip.Box.Paint(AvInk.Ground, AvInk.Frame);
                chip.Code.color = AvInk.Ink;
                chip.Name.color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            }
        }
    }

    /// <summary>
    /// A unit's recognition card. The hero is the recognition plate: the model drawn orthographically inside
    /// corner brackets, with a scale bar measured off the render, the LEN / SPAN / HGT the definition files,
    /// the unit's map symbol and four standard views. Under it sit the comparative figures, the role
    /// weighting, the analyst's notes, cross-references and the file line. One arrangement routine serves
    /// Measure and Place; spare pane height goes to the plate, then to more cross-reference rows.
    /// </summary>
    internal sealed class UnitCardPart : AvPart
    {
        private const float ToolbarH = 24f, DimsW = 112f, DimsH = 51f;

        private static readonly string[] ViewNames = { "PORT", "PLAN", "HEAD-ON", "3-QTR" };

        private static readonly string[] ViewHelp =
        {
            "PORT: the port-side profile, nose to the left.",
            "PLAN: the planform from directly above, nose to the left.",
            "HEAD-ON: the head-on view, as it closes on you.",
            "3-QTR: the three-quarter view off the port bow, from above.",
        };

        private static readonly float[] NiceMetres = { 0.1f, 0.2f, 0.5f, 1f, 2f, 5f, 10f, 20f, 50f, 100f, 200f, 500f };

        private readonly RecordHead head;
        private readonly AvFrame plateFrame, symbolFrame;
        private readonly Image plateBack, symbol, notesRule, fileRule;
        private readonly RawImage modelView;
        private readonly AvQuadGraphic marks;
        private readonly TMP_Text plateCaption, scaleText, dims, plateNote, symbolCaption, viewNote, notesKey, reported,
            notes, fileLine;
        private readonly AvControl[] views = new AvControl[4];
        private readonly AvControl turnLeft, turnRight;
        private readonly FigureBlock figures, roles;
        private readonly RefStrip refs;
        private readonly List<int> related = new List<int>(32);
        private EventAircraftPreview preview;
        private bool hasModel, placed;
        private AvSlot last;
        private float plateW, plateH;

        /// <summary>The record a cross-reference chip asks for.</summary>
        public Action<int> OpenRecord { set => refs.Picked = value; }

        /// <summary>The height of the pane the card sits in: a short pane keeps the plate low enough to show the figures under it.</summary>
        public float ViewHeight = 2000f;

        /// <summary>Where the model viewer draws.</summary>
        public RawImage ModelView => modelView;

        public UnitCardPart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "UnitCard");
            head = new RecordHead(Rect);
            plateFrame = AvFrame.Add(Rect, "Plate", AvChamfer.Diagonal(8f));
            plateBack = AvLay.Solid(Rect, "PlateBack", Color.clear);
            var modelGo = new GameObject("Model", typeof(RectTransform), typeof(CanvasRenderer));
            modelGo.transform.SetParent(Rect, false);
            modelView = modelGo.AddComponent<RawImage>();
            modelView.raycastTarget = false;
            marks = ArchiveUi.Quads(Rect, "PlateMarks");
            plateCaption = AvText.Make(Rect, "PlateCaption", AvTextRole.Micro, "");
            scaleText = AvText.Make(Rect, "ScaleText", AvTextRole.DataSmall, "");
            dims = AvText.Make(Rect, "Dimensions", AvTextRole.DataSmall, "", TextAlignmentOptions.TopRight);
            plateNote = AvText.Make(Rect, "PlateNote", AvTextRole.Micro,
                "NO PLATE HELD  ·  NO MODEL COULD BE DRAWN FOR THIS CARD", TextAlignmentOptions.Center, true);
            symbolFrame = AvFrame.Add(Rect, "SymbolBox", AvChamfer.Diagonal(4f));
            symbol = AvLay.Solid(Rect, "Symbol", Color.white);
            symbol.preserveAspect = true;
            symbolCaption = AvText.Make(Rect, "SymbolCaption", AvTextRole.Micro, "MAP SYMBOL", TextAlignmentOptions.MidlineRight);
            for (int i = 0; i < views.Length; i++)
            {
                var view = (EventAircraftPreview.View)i;
                views[i] = AvControl.Make(Rect, new AvControl.Spec(ViewNames[i], () => Look(view), AvButtonStyle.Quiet));
                views[i].SingleLine();
            }
            turnLeft = AvControl.Make(Rect, new AvControl.Spec("", () => Turn(-30f), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
            turnRight = AvControl.Make(Rect, new AvControl.Spec("", () => Turn(30f), AvButtonStyle.Quiet, AvIcon.ChevronRight));
            viewNote = AvText.Make(Rect, "ViewNote", AvTextRole.Micro,
                "SCALE BAR MEASURED OFF THE MODEL  ·  DIMENSIONS AS FILED", TextAlignmentOptions.MidlineRight);
            figures = new FigureBlock(Rect, "Figure", AvState.Ready);
            roles = new FigureBlock(Rect, "Role", AvState.Info);
            notesKey = AvText.Make(Rect, "NotesKey", AvTextRole.Micro, "ANALYST NOTES");
            reported = AvText.Make(Rect, "Reported", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
            notesRule = AvLay.Solid(Rect, "NotesRule", Color.clear);
            notes = AvText.Make(Rect, "Notes", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
            refs = new RefStrip(Rect);
            fileRule = AvLay.Solid(Rect, "FileRule", Color.clear);
            fileLine = AvText.Make(Rect, "FileLine", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
            ApplyModelState();
            Restyle();
        }

        public void Bind(ArchiveLibrary library, int shelf, int item)
        {
            ArchiveRecord card = library.Card(shelf, item);
            int count = library.Count(shelf);
            string role = ArchiveLibrary.RoleName(card.Role);
            string place = ArchiveUi.Two(item + 1) + " OF " + ArchiveUi.Two(count);
            head.Set(card.Code, card.Name.ToUpperInvariant(),
                ArchiveShelf.Names[shelf] + "  ·  PRIMARY ROLE " + role +
                    (card.Detail.Length > 0 ? "  ·  GUIDANCE " + card.Detail : ""),
                "RECOGNITION CARD", "CARD " + place);

            string second = shelf == ArchiveShelf.Aircraft ? "SPAN" : shelf == ArchiveShelf.Naval ? "BEAM" : "WIDTH";
            dims.text = Dimension("LEN", card.Length) + "\n" + Dimension(second, card.Span) + "\n" + Dimension("HGT", card.Height);

            figures.Begin(shelf == ArchiveShelf.Ordnance ? "ROUND DATA" : shelf == ArchiveShelf.Structures ? "SITE DATA" : "PERFORMANCE",
                "BAR: SHARE OF THE LARGEST ON THIS SHELF  ·  N/N: RANK ON THIS SHELF");
            for (int i = 0; i < card.Figures.Length; i++)
            {
                ArchiveFigure f = card.Figures[i];
                figures.Add(f.Label, f.Text, f.Unit, library.Rank(shelf, i, f.Value), f.Known ? library.Share(shelf, i, f.Value) : -1f);
            }
            figures.End();

            // The weighting is the definition's own; a card that files none (a truck, a hangar) carries no block.
            roles.Begin("ROLE WEIGHTING", "AS THE DEFINITION FILES IT, 0 TO 1");
            if (card.Role >= 0)
                for (int i = 0; i < card.Weights.Length; i++)
                {
                    float weight = card.Weights[i];
                    roles.Add(ArchiveLibrary.RoleNames[i], weight > 0f ? AvNum.Fixed(weight, 2) : "—", "", "",
                        weight > 0f ? Mathf.Clamp01(weight) : -1f);
                }
            roles.End();

            reported.text = card.Reported.Length > 0 ? "REPORTED AS " + card.Reported.ToUpperInvariant() : "";
            notes.text = card.Notes.Length > 0 ? card.Notes : "No analyst notes are filed against this card.";
            symbol.sprite = card.MapIcon;

            refs.Begin("SAME PRIMARY ROLE ON THIS SHELF  ·  " + role);
            library.SameRole(shelf, item, related);
            foreach (int other in related)
            {
                ArchiveRecord peer = library.Card(shelf, other);
                refs.Add(other, peer.Code, peer.Name.ToUpperInvariant());
            }
            fileLine.text = "FILE " + ArchiveShelf.Names[shelf] + "/" + card.Code + "  ·  KEY " +
                (card.Key.Length > 0 ? card.Key : "—") + "  ·  SOURCE: GAME ENCYCLOPEDIA DEFINITION  ·  CARD " + place;
            ApplyModelState();
            Rebind();
        }

        /// <summary>Hands the card its model viewer once the record's prefab has (or has not) loaded.</summary>
        public void SetPreview(EventAircraftPreview viewer, bool available)
        {
            preview = viewer;
            hasModel = available && viewer != null;
            ApplyModelState();
            if (!placed) return;
            if (hasModel) preview.SetAspect((plateW - 2f) / Mathf.Max(1f, plateH - 2f));
            PaintMarks();
        }

        private static string Dimension(string label, float metres) =>
            label.PadRight(6) + (metres > 0f ? AvNum.Fixed(metres, 1) : "—").PadLeft(6) + (metres > 0f ? " M" : "  ");

        private void Look(EventAircraftPreview.View view)
        {
            if (!hasModel) return;
            preview.Look(view);
            PaintMarks();
        }

        private void Turn(float degrees)
        {
            if (!hasModel) return;
            preview.Rotate(degrees);
            PaintMarks();
        }

        private void ApplyModelState()
        {
            modelView.gameObject.SetActive(hasModel);
            plateNote.gameObject.SetActive(!hasModel);
            bool hasSymbol = symbol.sprite != null;
            symbolFrame.gameObject.SetActive(hasSymbol);
            symbol.gameObject.SetActive(hasSymbol);
            symbolCaption.gameObject.SetActive(hasSymbol);
            for (int i = 0; i < views.Length; i++)
            {
                views[i].Interactable = hasModel;
                views[i].Help = hasModel ? ViewHelp[i] : "No model is held for this card, so there is no view to set.";
            }
            turnLeft.Interactable = turnRight.Interactable = hasModel;
            turnLeft.Help = hasModel ? "Turn the model 30 degrees to port." : "No model is held for this card.";
            turnRight.Help = hasModel ? "Turn the model 30 degrees to starboard." : "No model is held for this card.";
        }

        /// <summary>Re-arranges inside the current slot at once, then lets the flow re-measure (the height may not have moved).</summary>
        private void Rebind()
        {
            Restyle();
            if (placed) Place(last);
            Changed();
        }

        public override float Measure(float width) => Arrange(width, false, 0f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            last = s;
            placed = true;
            Arrange(s.W, true, s.H);
        }

        private float Arrange(float w, bool place, float slotHeight)
        {
            float y = head.Arrange(0f, w, place) + 10f;
            int cols = w >= 840f ? 4 : w >= 560f ? 3 : 2;
            float figH = figures.Measure(cols), roleH = roles.Measure(cols);
            float notesH = 21f + AvText.Height(notes, w - 14f);
            int allRows = refs.RowsFor(w);
            float fileH = 8f + AvText.Height(fileLine, w);
            float plateMin = Mathf.Clamp(Mathf.Round(Mathf.Min(w * 0.34f, ViewHeight * 0.42f)), 170f, 300f);
            float natural = y + plateMin + 4f + ToolbarH + 12f + figH + 12f + (roleH > 0f ? roleH + 12f : 0f) + notesH + 12f +
                (allRows > 0 ? RefStrip.Height(1) + 12f : 0f) + fileH;
            if (!place) return natural;

            // Spare height: the plate first (to half its width), then whole cross-reference rows, then the plate again.
            float extra = Mathf.Max(0f, slotHeight - natural);
            float grown = Mathf.Min(extra, Mathf.Max(0f, Mathf.Round(w * 0.5f) - plateMin));
            extra -= grown;
            int rows = allRows > 0 ? 1 : 0;
            while (rows < allRows && extra >= RefStrip.RowPitch) { rows++; extra -= RefStrip.RowPitch; }
            plateW = w;
            plateH = plateMin + grown + extra;

            bool hasSymbol = symbol.sprite != null;
            AvLay.Place(plateFrame.rectTransform, 0f, y, w, plateH);
            AvLay.Place(plateBack.rectTransform, 1f, y + 1f, w - 2f, plateH - 2f);
            AvLay.Place(modelView.rectTransform, 1f, y + 1f, w - 2f, plateH - 2f);
            AvLay.Place(marks.rectTransform, 0f, y, w, plateH);
            AvLay.Place(plateCaption.rectTransform, 16f, y + 12f, w - 32f - (hasSymbol ? 62f : 0f), 16f);
            AvLay.Place(plateNote.rectTransform, 24f, y + plateH * 0.5f - 16f, w - 48f, 32f);
            AvLay.Place(scaleText.rectTransform, 18f, y + plateH - 40f, 110f, 16f);
            AvLay.Place(dims.rectTransform, w - 18f - DimsW, y + plateH - 14f - DimsH, DimsW, DimsH);
            AvLay.Place(symbolFrame.rectTransform, w - 14f - 46f, y + 12f, 46f, 46f);
            AvLay.Place(symbol.rectTransform, w - 14f - 40f, y + 18f, 34f, 34f);
            AvLay.Place(symbolCaption.rectTransform, w - 14f - 96f, y + 60f, 96f, 15f);
            if (hasModel) preview.SetAspect((w - 2f) / Mathf.Max(1f, plateH - 2f));
            PaintMarks();
            y += plateH + 4f;

            float buttonW = Mathf.Min(68f, (w - 3f * 4f - 8f - 2f * 30f - 4f) / 4f), x = 0f;
            for (int i = 0; i < views.Length; i++)
            {
                AvLay.Place(views[i].Rect, x, y, buttonW, ToolbarH);
                x += buttonW + 4f;
            }
            x += 4f;
            AvLay.Place(turnLeft.Rect, x, y, 30f, ToolbarH);
            AvLay.Place(turnRight.Rect, x + 34f, y, 30f, ToolbarH);
            x += 64f;
            float viewNoteW = AvText.Width(viewNote) + 2f;
            viewNote.gameObject.SetActive(x + 16f + viewNoteW <= w);
            AvLay.Place(viewNote.rectTransform, w - viewNoteW, y + 4f, viewNoteW, 16f);
            y += ToolbarH + 12f;

            figures.Place(y, w, cols);
            y += figH + 12f;
            if (roleH > 0f)
            {
                roles.Place(y, w, cols);
                y += roleH + 12f;
            }

            float keyW = AvText.Width(notesKey) + 2f, reportedW = AvText.Width(reported) + 2f;
            AvLay.Place(notesKey.rectTransform, 0f, y, keyW, 16f);
            reported.gameObject.SetActive(reported.text.Length > 0 && keyW + 16f + reportedW <= w);
            AvLay.Place(reported.rectTransform, w - reportedW, y, reportedW, 16f);
            AvLay.Place(notesRule.rectTransform, 0f, y + 21f, 2f, notesH - 21f);
            AvLay.Place(notes.rectTransform, 14f, y + 21f, w - 14f, notesH - 21f);
            y += notesH + 12f;

            refs.Place(y, w, rows);
            if (rows > 0) y += RefStrip.Height(rows) + 12f;
            AvLay.Place(fileRule.rectTransform, 0f, y, w, 1f);
            AvLay.Place(fileLine.rectTransform, 0f, y + 8f, w, fileH - 8f);
            return natural;
        }

        /// <summary>The plate's line work: corner brackets, the backing under its readouts and the scale bar off the last render.</summary>
        private void PaintMarks()
        {
            if (!placed) return;
            Color ink = AvInk.Ink, backing = AvInk.Ground.WithAlpha(0.62f);
            marks.Begin();
            ArchiveUi.Brackets(marks, plateW, plateH, AvInk.Frame);
            marks.Add(plateW - 18f - DimsW - 8f, plateH - 14f - DimsH - 5f, DimsW + 16f, DimsH + 10f, backing);
            if (symbol.sprite != null) marks.Add(plateW - 14f - 100f, 8f, 104f, 70f, backing);

            float span = hasModel ? preview.SpanMetres : 0f;
            float metresPerPixel = span > 0f ? span / Mathf.Max(1f, plateH - 2f) : 0f;
            float nice = 0f;
            if (metresPerPixel > 0f)
                foreach (float candidate in NiceMetres)
                    if (candidate / metresPerPixel <= plateW * 0.26f) nice = candidate;
            scaleText.gameObject.SetActive(nice > 0f);
            if (nice > 0f)
            {
                float length = nice / metresPerPixel, x = 18f, baseY = plateH - 20f;
                marks.Add(x - 8f, baseY - 25f, Mathf.Max(length, 64f) + 16f, 36f, backing);
                marks.Add(x, baseY, length, 2f, ink);
                marks.Add(x, baseY - 5f, 1.5f, 7f, ink);
                marks.Add(x + length - 1.5f, baseY - 5f, 1.5f, 7f, ink);
                marks.Add(x + length * 0.5f - 0.75f, baseY - 3f, 1.5f, 5f, ink);
                scaleText.text = AvNum.Fixed(nice, nice < 1f ? 1 : 0) + " M";
            }
            marks.End();

            plateCaption.text = !hasModel ? "RECOGNITION PLATE"
                : "RECOGNITION PLATE  ·  VIEW " + preview.ViewName + (plateW >= 560f ? "  ·  ORTHOGRAPHIC" : "");
            EventAircraftPreview.View? current = hasModel ? preview.Preset : null;
            for (int i = 0; i < views.Length; i++)
                views[i].Latched = current.HasValue && (int)current.Value == i;
        }

        public override void Restyle()
        {
            head.Restyle();
            AvStyle c = AvStyleHost.FuiStyle("card inert");
            plateFrame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvInk.Frame);
            plateBack.color = AvInk.Inert.WithAlpha(1f);
            modelView.color = Color.white;
            plateCaption.color = AvInk.Key;
            scaleText.color = AvInk.Ink;
            dims.color = AvInk.Ink;
            plateNote.color = AvInk.Dim;
            symbolFrame.Paint(AvInk.Ground.WithAlpha(0.62f), AvInk.Hairline);
            symbol.color = AvInk.Ink;
            symbolCaption.color = AvInk.Dim;
            viewNote.color = AvInk.Dim;
            foreach (AvControl view in views) view.Restyle();
            turnLeft.Restyle();
            turnRight.Restyle();
            figures.Restyle();
            roles.Restyle();
            notesKey.color = AvInk.Key;
            reported.color = AvInk.Dim;
            notesRule.color = AvInk.Key;
            notes.color = AvInk.Ink;
            refs.Restyle();
            fileRule.color = AvInk.Hairline;
            fileLine.color = AvInk.Dim;
            PaintMarks();
        }
    }
}

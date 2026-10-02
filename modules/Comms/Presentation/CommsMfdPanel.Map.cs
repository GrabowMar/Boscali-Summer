using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Comms.Domain;
using BoscaliSummer.Modules.Comms.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private static readonly CommsTool[] ToolRow =
        {
            CommsTool.Ping, CommsTool.Pen, CommsTool.Line, CommsTool.Arrow, CommsTool.Circle, CommsTool.Box,
            CommsTool.Sticker, CommsTool.Label, CommsTool.Eraser, CommsTool.Measure,
        };

        private static readonly AvIcon[] ToolIcons =
        {
            AvIcon.MapPin, AvIcon.Pencil, AvIcon.Line, AvIcon.ArrowUpRight, AvIcon.Circle, AvIcon.Square,
            AvIcon.Sticker, AvIcon.Typography, AvIcon.Eraser, AvIcon.Ruler2,
        };

        private static readonly AvIcon[] PingIcons =
            { AvIcon.MapPin, AvIcon.AlertTriangle, AvIcon.Radar2, AvIcon.Target, AvIcon.Shield, AvIcon.Flag };

        private static readonly AvIcon[] StickerIcons =
        {
            AvIcon.Star, AvIcon.Heart, AvIcon.MoodSmile, AvIcon.Skull, AvIcon.Flame, AvIcon.Crown,
            AvIcon.Bolt, AvIcon.Flag, AvIcon.QuestionMark, AvIcon.AlertTriangle, AvIcon.Eye, AvIcon.Coffee,
        };

        private static readonly string[] ToolTips =
        {
            "Ping: click the map to drop the selected ping. Middle-click does the same without arming anything.",
            "Pen: drag on the map to draw freehand. Holding the draw key does this without opening COM.",
            "Line: drag from start to end.",
            "Arrow: drag from the tail to the tip — an attack axis or a route.",
            "Circle: drag from the centre out to the edge — a threat ring or a CAP station.",
            "Box: drag corner to corner — a kill box or an area to avoid.",
            "Sticker: click the map to place the selected sticker.",
            "Text: type the words in the field below, press PLACE, then click the map where they belong.",
            "Erase: click one of your own marks to remove it (the host can erase any mark).",
            "Ruler: drag for range and true bearing. Only you see it.",
        };

        private const int BoardRows = 12;
        private const int MinBoardRows = 2;

        // Everything on the MAP page except its mark list: part heights, gaps and padding, biased a few px high
        // so the list never overshoots into a scrollbar.
        private const float MapFixedHeight = 473f;

        // The YOUR MARKS header and ring row, gaps included.
        private const float MapRingsHeight = 107f;

        private AvControl[] toolButtons;
        private AvControl[] pingButtons;
        private PenRow penRow;
        private AvControl[] stickerButtons;
        private AvField labelField;
        private AvSegmented channelStrip;
        private AvControl clearAllButton;
        private AvGauge[] markRings;
        private AvSection marksSection;
        private AvSection boardSection;
        private AvRow[] boardRows;
        private readonly CommsItem[] boardBound = new CommsItem[BoardRows];
        private AvHazardBar boardBar;
        private readonly int[] markCounts = new int[4];

        private void ResetMap()
        {
            toolButtons = null;
            pingButtons = null;
            penRow = null;
            stickerButtons = null;
            labelField = null;
            channelStrip = null;
            clearAllButton = null;
            markRings = null;
            marksSection = null;
            boardSection = null;
            boardRows = null;
            for (int i = 0; i < boardBound.Length; i++) boardBound[i] = null;
            boardBar = null;
        }

        private void BuildMapPage(AvFlow p)
        {
            // Tools lead the page with no header of their own: the tab already says MAP and the chip says which tool is armed.
            var toolSpecs = new AvControl.Spec[ToolRow.Length + 2];
            for (int i = 0; i < ToolRow.Length; i++)
            {
                CommsTool tool = ToolRow[i];
                toolSpecs[i] = new AvControl.Spec(ToolLabel(tool), () => comms.ToggleTool(tool), AvButtonStyle.Default, ToolIcons[i]);
            }
            toolSpecs[ToolRow.Length] = new AvControl.Spec("UNDO", () => comms.Undo(), AvButtonStyle.Quiet, AvIcon.ArrowBackUp);
            toolSpecs[ToolRow.Length + 1] = new AvControl.Spec("OFF", () =>
            {
                comms.SetTool(CommsTool.None);
                comms.ClearMeasure();
            }, AvButtonStyle.Quiet, AvIcon.X);
            var toolHelps = new string[toolSpecs.Length];
            Array.Copy(ToolTips, toolHelps, ToolRow.Length);
            toolHelps[ToolRow.Length] = "Undo: take back your most recent mark.";
            toolHelps[ToolRow.Length + 1] = "Off: put the tool down and give the map back its normal clicks. ESC does the same.";
            toolButtons = ButtonGrid(p, toolSpecs, 6, toolHelps);

            var inks = new Color[CommsCatalog.Pens.Length];
            for (int i = 0; i < inks.Length; i++) inks[i] = CommsMesh.Ink(i);
            penRow = p.Add(new PenRow(p.Content, inks, ink => comms.PenInk = ink,
                CommsCatalog.PenWidthNames, w => comms.PenWidth = w));
            for (int i = 0; i < penRow.Swatches.Length; i++)
                penRow.Swatches[i].Help = CommsCatalog.Pens[i].Name + " ink, for the pen and the shapes.";
            for (int i = 0; i < penRow.Widths.Length; i++) penRow.Widths[i].Help = "Stroke width for the pen and shapes. " + HoldHint();

            // Pings and stickers are one group of markers: one header, three rows of six.
            p.Section(AvIcon.MapPin, "MARKERS", QuickPingNote());
            var pingSpecs = new AvControl.Spec[CommsCatalog.PalettePings];
            var pingHelps = new string[pingSpecs.Length];
            for (int i = 0; i < pingSpecs.Length; i++)
            {
                int kind = i;
                PingKind ping = CommsCatalog.Pings[i];
                pingSpecs[i] = new AvControl.Spec(ping.Code, () =>
                {
                    comms.PingKind = kind;
                    comms.SetTool(CommsTool.Ping);
                }, ToneStyle(ping.Tone), PingIcons[i]);
                pingHelps[i] = ping.Code + " ping: " + ping.Phrase + ". Pick it, then click the map.";
            }
            pingButtons = ButtonGrid(p, pingSpecs, pingSpecs.Length, pingHelps);

            var stickerSpecs = new AvControl.Spec[CommsCatalog.Stickers.Length];
            var stickerHelps = new string[stickerSpecs.Length];
            for (int i = 0; i < stickerSpecs.Length; i++)
            {
                int kind = i;
                StickerKind sticker = CommsCatalog.Stickers[i];
                stickerSpecs[i] = new AvControl.Spec(sticker.Name, () =>
                {
                    comms.StickerKind = kind;
                    comms.SetTool(CommsTool.Sticker);
                }, AvButtonStyle.Default, StickerIcons[i]);
                stickerHelps[i] = sticker.Name + " sticker. Pick it, then click the map.";
            }
            stickerButtons = ButtonGrid(p, stickerSpecs, 6, stickerHelps);

            // Label field and its PLACE key share one line; who sees a post and the clear keys share the next.
            labelField = new AvField(p.Content, "TEXT: FARP HERE, CAP EAST…", CommsText.MaxLabel, _ => ArmLabel());
            Tip(labelField, "Words for the map, up to " + CommsText.MaxLabel + " characters. Press PLACE, then click where they go.");
            var placeRow = new AvButtons(p.Content, new[] { new AvControl.Spec("PLACE", ArmLabel, AvButtonStyle.Primary, AvIcon.MapPin) });
            placeRow.Controls[0].Help = "Arm the text label, then click the map where it belongs.";
            p.Row(labelField, placeRow);

            channelStrip = AvSegmented.Strip(p.Content, new[] { "TEAM", "ALL" },
                () => comms.Channel == CommsChannel.All ? 1 : 0,
                index =>
                {
                    if ((comms.Channel == CommsChannel.All) != (index == 1)) comms.ToggleChannel();
                });
            channelStrip.Options[0].Help = "Send to TEAM: only your side sees your marks, calls, polls and games.";
            channelStrip.Options[1].Help = "Send to ALL: every player sees them, including the other side. The host can switch this channel off.";
            var clearRow = new AvButtons(p.Content,
                new[]
                {
                    new AvControl.Spec("CLEAR MINE", () => comms.ClearMine(), AvButtonStyle.Danger, AvIcon.Eraser),
                    new AvControl.Spec("CLEAR MAP", () => comms.ClearAll(), AvButtonStyle.Danger, AvIcon.Eraser),
                });
            clearAllButton = clearRow.Controls[1];
            clearRow.Controls[0].Help = "Remove everything you have put on the map.";
            clearAllButton.Help = "Host only: wipe every mark from the shared map.";
            p.Row(channelStrip, clearRow);

            // How much of each personal budget is in use: the oldest mark of a kind retires when its ring is full.
            marksSection = p.Section(AvIcon.Stack2, "YOUR MARKS", "OLDEST FADES FIRST");
            markRings = new[]
            {
                new AvGauge(p.Content, "PINGS", AvGaugeShape.Ring, 56f),
                new AvGauge(p.Content, "DRAWN", AvGaugeShape.Ring, 56f),
                new AvGauge(p.Content, "STICKERS", AvGaugeShape.Ring, 56f),
                new AvGauge(p.Content, "LABELS", AvGaugeShape.Ring, 56f),
            };
            markRings[0].Help = "Pings you have up, out of " + CommsBoard.AuthorBudget(CommsItemKind.Ping) + ". A new one retires your oldest.";
            markRings[1].Help = "Pen strokes and shapes you have up, out of " + CommsBoard.AuthorBudget(CommsItemKind.Stroke) + ". A new one retires your oldest.";
            markRings[2].Help = "Stickers you have up, out of " + CommsBoard.AuthorBudget(CommsItemKind.Sticker) + ". A new one retires your oldest.";
            markRings[3].Help = "Text labels you have up, out of " + CommsBoard.AuthorBudget(CommsItemKind.Label) + ". A new one retires your oldest.";
            p.Row(markRings);

            // The list is what fills the page: as many marks as fit, newest first. Click one to flash it on the map.
            boardSection = p.Section(AvIcon.Map2, "ON THE MAP", "");
            boardRows = new AvRow[BoardRows];
            for (int i = 0; i < BoardRows; i++)
            {
                int row = i;
                boardRows[i] = p.Add(new AvRow(p.Content, () => FlashBoardRow(row)));
            }
            boardBar = p.Add(new AvHazardBar(p.Content, "BOARD"), 1f);
            boardBar.Help = "How full the shared board is for your audience. At the ceiling the oldest mark is retired to make room.";
        }

        private void ArmLabel()
        {
            comms.LabelText = labelField != null ? labelField.Text : comms.LabelText;
            comms.SetTool(CommsTool.Label);
        }

        private void RefreshMap()
        {
            if (toolButtons == null) return;
            for (int i = 0; i < ToolRow.Length; i++) toolButtons[i].Latched = comms.Tool == ToolRow[i];
            for (int i = 0; i < pingButtons.Length; i++)
                pingButtons[i].Latched = comms.PingKind == i && comms.Tool == CommsTool.Ping;
            for (int i = 0; i < penRow.Swatches.Length; i++) penRow.Swatches[i].Selected = comms.PenInk == i;
            for (int i = 0; i < penRow.Widths.Length; i++) penRow.Widths[i].Latched = comms.PenWidth == i;
            for (int i = 0; i < stickerButtons.Length; i++)
                stickerButtons[i].Latched = comms.StickerKind == i && comms.Tool == CommsTool.Sticker;

            channelStrip.Refresh();
            clearAllButton.Interactable = comms.IsHost;

            // ---- personal budgets and the board list, in one pass over the board
            CommsBoard board = comms.State.Board;
            IReadOnlyList<CommsItem> items = board.Items;
            float now = Time.unscaledTime;
            ulong me = comms.LocalId;
            for (int i = 0; i < markCounts.Length; i++) markCounts[i] = 0;
            for (int i = 0; i < items.Count; i++)
            {
                CommsItem item = items[i];
                if (item.Author != me) continue;
                if (item.Kind == CommsItemKind.Ping) { if (!item.IsCall) markCounts[0]++; }
                else if (item.Kind == CommsItemKind.Stroke) markCounts[1]++;
                else if (item.Kind == CommsItemKind.Sticker) markCounts[2]++;
                else markCounts[3]++;
            }
            SetRing(markRings[0], markCounts[0], CommsBoard.AuthorBudget(CommsItemKind.Ping));
            SetRing(markRings[1], markCounts[1], CommsBoard.AuthorBudget(CommsItemKind.Stroke));
            SetRing(markRings[2], markCounts[2], CommsBoard.AuthorBudget(CommsItemKind.Sticker));
            SetRing(markRings[3], markCounts[3], CommsBoard.AuthorBudget(CommsItemKind.Label));

            // A short console drops the budget rings (their numbers stay in the tips) so the mark list keeps its rows.
            float viewport = console.Page(TabMap).ViewportHeight;
            bool compact = viewport > 0f && viewport < MapFixedHeight + 4f * 33f;
            Show(marksSection, !compact);
            for (int i = 0; i < markRings.Length; i++) Show(markRings[i], !compact);
            int fit = FitRows(console.Page(TabMap), MapFixedHeight - (compact ? MapRingsHeight : 0f), BoardRows, MinBoardRows);
            int shown = 0;
            for (int i = items.Count - 1; i >= 0 && shown < fit; i--)
            {
                CommsItem item = items[i];
                if (comms.State.IsMuted(item.Author)) continue;
                boardBound[shown] = item;
                boardRows[shown].Set(Who(item.Author, item.AuthorName) + " · " + ItemName(item), null,
                    CommsText.Countdown(item.Expires - now), ItemState(item));
                boardRows[shown].Help = "Flash this mark on the map.";
                Show(boardRows[shown], true);
                shown++;
            }
            if (shown == 0)
            {
                boardBound[0] = null;
                boardRows[0].Set("NOTHING ON THE MAP YET · PICK A TOOL AND CLICK THE MAP", null, "", AvState.Inert);
                boardRows[0].Help = null;
                Show(boardRows[0], true);
                shown = 1;
            }
            for (int i = shown; i < BoardRows; i++)
            {
                boardBound[i] = null;
                Show(boardRows[i], false);
            }
            boardSection.SetCaption(AvNum.Fixed(board.Count, 0) + " MARKS · CLICK TO FLASH");
            float load = board.Count / (float)CommsBoard.MaxPerAudience;
            boardBar.Set(load, AvNum.Fixed(board.Count, 0) + " / " + AvNum.Fixed(CommsBoard.MaxPerAudience, 0),
                load > 0.85f ? AvState.Caution : AvState.Info);
        }

        private static void SetRing(AvGauge ring, int count, int budget)
        {
            float fill = budget <= 0 ? 0f : Mathf.Clamp01(count / (float)budget);
            ring.Set(fill, AvNum.Fixed(count, 0) + "/" + AvNum.Fixed(budget, 0), fill >= 1f ? AvState.Caution : AvState.Info);
        }

        private static string ItemName(CommsItem item)
        {
            switch (item.Kind)
            {
                case CommsItemKind.Ping:
                    return CommsCatalog.ValidPing(item.Style) ? CommsCatalog.Pings[item.Style].Code + " PING" : "PING";
                case CommsItemKind.Sticker:
                    return CommsCatalog.ValidSticker(item.Style) ? CommsCatalog.Stickers[item.Style].Name + " STICKER" : "STICKER";
                case CommsItemKind.Label:
                    return "“" + item.Text + "”";
                default:
                    return "DRAWING";
            }
        }

        private static AvState ItemState(CommsItem item) =>
            item.Kind == CommsItemKind.Ping && CommsCatalog.ValidPing(item.Style)
                ? ToneState(CommsCatalog.Pings[item.Style].Tone)
                : AvState.Info;

        private void FlashBoardRow(int row)
        {
            CommsItem item = row >= 0 && row < boardBound.Length ? boardBound[row] : null;
            if (item == null || item.Points == null || item.Points.Length < 2) return;
            comms.Highlight(item.X, item.Z);
            comms.State.SetNotice("FLASHING " + CommsText.Grid(item.X, item.Z) + " ON THE MAP", false, Time.unscaledTime);
        }

        /// <summary>A hostile tone (enemy, SAM, spike) reads as the kit's danger style; the icon and code still say which.</summary>
        private static AvButtonStyle ToneStyle(CommsTone tone) => tone == CommsTone.Danger ? AvButtonStyle.Danger : AvButtonStyle.Default;

        private string QuickPingNote()
        {
            KeyCode key = settings.QuickPingKey.Value;
            return key == KeyCode.None ? "CLICK THE MAP" : KeyName(key) + " DROPS IT";
        }

        private string HoldHint()
        {
            KeyCode hold = settings.DrawHoldKey.Value;
            string draw = hold == KeyCode.None ? "" : "Hold " + KeyName(hold) + " and drag on the map to draw at any time. ";
            return draw + "Your marks fade on their own; the host sets how long they last.";
        }

        private static string ToolLabel(CommsTool tool)
        {
            switch (tool)
            {
                case CommsTool.Label: return "TEXT";
                case CommsTool.Eraser: return "ERASE";
                case CommsTool.Measure: return "RULER";
                default: return tool.ToString().ToUpperInvariant();
            }
        }

        /// <summary>
        /// The pen swatches (data: a fixed ink) plus the width choices, in one hand-laid line —
        /// the kit has no mixed-width row primitive, so this composes AvFrame + AvControl itself
        /// (spec: "build it locally in your module from kit primitives").
        /// </summary>
        private sealed class PenRow : AvPart
        {
            private const float SwatchW = 34f, Gap = 4f, SectionGap = 8f;

            public readonly PenSwatch[] Swatches;
            public readonly AvControl[] Widths;

            public PenRow(RectTransform parent, Color[] inks, Action<int> pickInk, string[] widthNames, Action<int> pickWidth)
            {
                Rect = AvLay.Child(parent, "Pen Row");
                Swatches = new PenSwatch[inks.Length];
                for (int i = 0; i < inks.Length; i++)
                {
                    int ink = i;
                    Swatches[i] = new PenSwatch(Rect, inks[i]);
                    Swatches[i].Clicked += () => pickInk(ink);
                }
                Widths = new AvControl[widthNames.Length];
                for (int i = 0; i < widthNames.Length; i++)
                {
                    int w = i;
                    Widths[i] = AvControl.Make(Rect, new AvControl.Spec(widthNames[i], () => pickWidth(w)));
                }
            }

            public override float Measure(float width) => AvGridTokens.RowDense;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float x = 0f;
                for (int i = 0; i < Swatches.Length; i++)
                {
                    Swatches[i].Place(new AvSlot(x, 0f, SwatchW, s.H));
                    x += SwatchW + Gap;
                }
                x += SectionGap - Gap;
                float remain = Mathf.Max(0f, s.W - x);
                float ow = AvFlowMath.ColumnWidth(remain, Widths.Length, Gap);
                for (int i = 0; i < Widths.Length; i++) AvLay.Place(Widths[i].Rect, x + i * (ow + Gap), 0f, ow, s.H);
            }

            public override void Restyle()
            {
                foreach (PenSwatch s in Swatches) s.Restyle();
                foreach (AvControl w in Widths) w.Restyle();
            }
        }

        /// <summary>One clickable ink swatch: an AvFrame filled with the pen's own colour (data), a select stroke.</summary>
        private sealed class PenSwatch : AvPart
        {
            private readonly AvFrame frame;
            private readonly Color ink;
            private bool selected;

            public event Action Clicked;

            public PenSwatch(RectTransform parent, Color inkColor)
            {
                ink = inkColor;
                Rect = AvLay.Child(parent, "Pen Swatch");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(3f));
                AvLay.Fill(frame.rectTransform);
                AvHit hit = AvHit.On(frame);
                hit.Click = _ => Clicked?.Invoke();
                Restyle();
            }

            public bool Selected { get => selected; set { selected = value; Restyle(); } }

            /// <summary>Hover help shown in the console footer.</summary>
            public string Help { set => AvHelpTip.Attach(frame.gameObject, value); }

            public override float Measure(float width) => AvGridTokens.RowDense;

            public override void Restyle()
            {
                Color stroke = selected
                    ? AvStyleHost.FuiColor("select", AvTheme.Accent)
                    : AvStyleHost.Resolve(AvStyleHost.FuiStyle("card-bracket").Background, AvTheme.Frame);
                frame.Paint(ink, stroke);
            }
        }
    }
}

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
            CommsTool.Label, CommsTool.Eraser, CommsTool.Measure,
        };

        private static readonly AvIcon[] ToolIcons =
        {
            AvIcon.MapPin, AvIcon.Pencil, AvIcon.Line, AvIcon.ArrowUpRight, AvIcon.Circle, AvIcon.Square,
            AvIcon.Typography, AvIcon.Eraser, AvIcon.Ruler2,
        };

        private static readonly AvIcon[] PingIcons =
            { AvIcon.MapPin, AvIcon.AlertTriangle, AvIcon.Radar2, AvIcon.Target, AvIcon.Shield, AvIcon.Flag };

        private static readonly string[] ToolTips =
        {
            "Ping: click the map to drop the selected ping. Middle-click does the same without arming anything.",
            "Pen: drag on the map to draw freehand. Holding the draw key does this without opening COM.",
            "Line: drag from start to end.",
            "Arrow: drag from the tail to the tip — an attack axis or a route.",
            "Circle: drag from the centre out to the edge — a threat ring or a CAP station.",
            "Box: drag corner to corner — a kill box or an area to avoid.",
            "Text: type the words in the field below, press PLACE, then click the map where they belong.",
            "Erase: click one of your own marks to remove it (the host can erase any mark).",
            "Ruler: drag for range and true bearing. Only you see it.",
        };

        private const int BoardRows = 16;
        private const int MinBoardRows = 2;

        private AvSection plotSection;
        private AvControl[] toolButtons;
        private AvControl[] pingButtons;
        private InkDeck penRow;
        private AvField labelField;
        private AvSegmented channelStrip;
        private AvControl clearAllButton;
        private TallyPart tally;
        private AvSection boardSection;
        private NetRow[] boardRows;
        private readonly CommsItem[] boardBound = new CommsItem[BoardRows];
        private readonly int[] markCounts = new int[3];

        private void ResetMap()
        {
            plotSection = null;
            toolButtons = null;
            pingButtons = null;
            penRow = null;
            labelField = null;
            channelStrip = null;
            clearAllButton = null;
            tally = null;
            boardSection = null;
            boardRows = null;
            for (int i = 0; i < boardBound.Length; i++) boardBound[i] = null;
        }

        private void BuildMapPage(AvFlow p)
        {
            // Work runs top to bottom: pick a tool, pick the ink, drop a mark, then read what is on the map.
            plotSection = p.Section(AvIcon.Pencil, "PLOT", "MAP FREE");
            var toolSpecs = new AvControl.Spec[ToolRow.Length + 2];
            for (int i = 0; i < ToolRow.Length; i++)
            {
                CommsTool tool = ToolRow[i];
                toolSpecs[i] = new AvControl.Spec(ToolLabel(tool), () => comms.ToggleTool(tool), AvButtonStyle.Default, ToolIcons[i]);
            }
            toolSpecs[ToolRow.Length] = new AvControl.Spec("UNDO", () => comms.Undo(), AvButtonStyle.Quiet, AvIcon.ArrowBackUp);
            toolSpecs[ToolRow.Length + 1] = new AvControl.Spec("TOOL OFF", () =>
            {
                comms.SetTool(CommsTool.None);
                comms.ClearMeasure();
            }, AvButtonStyle.Quiet, AvIcon.X);
            var toolHelps = new string[toolSpecs.Length];
            Array.Copy(ToolTips, toolHelps, ToolRow.Length);
            toolHelps[ToolRow.Length] = "Undo: take back your most recent mark.";
            toolHelps[ToolRow.Length + 1] = "Tool off: put the tool down and give the map back its normal clicks. ESC does the same.";
            // Six columns: the six drawing keys fill the first line; the second line is four keys and a double-width OFF.
            var spans = new int[toolSpecs.Length];
            for (int i = 0; i < spans.Length; i++) spans[i] = 1;
            spans[spans.Length - 1] = 2;
            KeyGrid deck = p.Add(new KeyGrid(p.Content, 6, toolSpecs, spans));
            toolButtons = deck.Controls;
            for (int i = 0; i < toolButtons.Length; i++) toolButtons[i].Help = toolHelps[i];

            var inks = new Color[CommsCatalog.Pens.Length];
            for (int i = 0; i < inks.Length; i++) inks[i] = CommsMesh.Ink(i);
            penRow = p.Add(new InkDeck(p.Content, inks, ink => comms.PenInk = ink,
                CommsCatalog.PenWidthNames, w => comms.PenWidth = w));
            for (int i = 0; i < penRow.Swatches.Length; i++)
                penRow.Swatches[i].Help = CommsCatalog.Pens[i].Name + " ink, for the pen and the shapes.";
            for (int i = 0; i < penRow.Widths.Length; i++) penRow.Widths[i].Help = "Stroke width for the pen and shapes. " + HoldHint();

            // Markers, the text label and the audience of the next post sit under one header.
            p.Section(AvIcon.MapPin, "MARKERS", "CLICK THE MAP");
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

            // Label field and its PLACE key share one line.
            labelField = new AvField(p.Content, "TEXT: FARP HERE, CAP EAST…", CommsText.MaxLabel, _ => ArmLabel());
            Tip(labelField, "Words for the map, up to " + CommsText.MaxLabel + " characters. Press PLACE, then click where they go.");
            var placeRow = new AvButtons(p.Content, new[] { new AvControl.Spec("PLACE", ArmLabel, AvButtonStyle.Primary, AvIcon.MapPin) });
            placeRow.Controls[0].Help = "Arm the text label, then click the map where it belongs.";
            p.Row(labelField, placeRow);

            channelStrip = new AvSegmented(p.Content, "NEXT POST GOES TO", new[] { "TEAM", "ALL" },
                () => comms.Channel == CommsChannel.All ? 1 : 0,
                index =>
                {
                    if ((comms.Channel == CommsChannel.All) != (index == 1)) comms.ToggleChannel();
                });
            channelStrip.Options[0].Help = "Send to TEAM: only your side sees your marks and calls.";
            channelStrip.Options[1].Help = "Send to ALL: every player sees them, including the other side. The host can switch this channel off.";
            p.Add(channelStrip);

            // The shared plot: what each of you may hold, and everything this net can see, newest first. The list takes the
            // height the page has left; click a mark to flash it on the map.
            boardSection = p.Section(AvIcon.Map2, "SHARED PLOT", "");
            tally = p.Add(new TallyPart(p.Content, "MY PINGS", "MY DRAWN", "MY LABELS", "BOARD"));
            tally.Help(0, "Pings you have up, out of " + CommsBoard.AuthorBudget(CommsItemKind.Ping) + ". A new one retires your oldest.");
            tally.Help(1, "Pen strokes and shapes you have up, out of " + CommsBoard.AuthorBudget(CommsItemKind.Stroke) + ". A new one retires your oldest.");
            tally.Help(2, "Text labels you have up, out of " + CommsBoard.AuthorBudget(CommsItemKind.Label) + ". A new one retires your oldest.");
            tally.Help(3, "How full the shared board is for your audience. At the ceiling the oldest mark is retired to make room.");
            boardRows = new NetRow[BoardRows];
            for (int i = 0; i < BoardRows; i++)
            {
                int row = i;
                boardRows[i] = p.Add(new NetRow(p.Content, AvTextRole.Head, 56f, 124f, 58f, () => FlashBoardRow(row)));
            }

            var clearRow = new AvButtons(p.Content,
                new[]
                {
                    new AvControl.Spec("CLEAR MINE", () => comms.ClearMine(), AvButtonStyle.Danger, AvIcon.Eraser),
                    new AvControl.Spec("CLEAR MAP", () => comms.ClearAll(), AvButtonStyle.Danger, AvIcon.Eraser),
                });
            clearAllButton = clearRow.Controls[1];
            clearRow.Controls[0].Help = "Remove everything you have put on the map.";
            clearAllButton.Help = "Host only: wipe every mark from the shared map.";
            p.Add(clearRow);

            KeyCode hold = settings.DrawHoldKey.Value;
            console.Orders(TabMap,
                "Arm a tool, then click or drag on the map. ESC puts it down.",
                "Click a mark in the shared plot to flash it on the map.",
                "Marks fade on their own; a full budget retires your oldest first.",
                hold != KeyCode.None
                    ? "Hold " + KeyName(hold) + " and drag on the map to draw without opening this page."
                    : "Pen, line, arrow, circle and box all take the ink and line weight set above.");
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
            plotSection.SetCaption(comms.Tool == CommsTool.None ? "MAP FREE"
                : "ARMED · " + ToolName(comms.Tool) +
                  (comms.Tool == CommsTool.Ping && CommsCatalog.ValidPing(comms.PingKind) ? " · " + CommsCatalog.Pings[comms.PingKind].Code : ""));
            penRow.SetKeys(
                "INK · " + (CommsCatalog.ValidPen(comms.PenInk) ? CommsCatalog.Pens[comms.PenInk].Name : "—"),
                "LINE · " + (CommsCatalog.ValidWidth(comms.PenWidth) ? CommsCatalog.PenWidthNames[comms.PenWidth] : "—"));

            channelStrip.Refresh();
            clearAllButton.Interactable = comms.IsHost;

            // ---- personal budgets and the board list, in one pass over the board
            CommsBoard board = comms.State.Board;
            IReadOnlyList<CommsItem> items = board.Items;
            float now = Time.unscaledTime;
            ulong me = comms.LocalId;
            int eligible = 0;
            for (int i = 0; i < markCounts.Length; i++) markCounts[i] = 0;
            for (int i = 0; i < items.Count; i++)
            {
                CommsItem item = items[i];
                if (item.Kind != CommsItemKind.Sticker && !comms.State.IsMuted(item.Author)) eligible++;
                if (item.Author != me) continue;
                if (item.Kind == CommsItemKind.Ping) { if (!item.IsCall) markCounts[0]++; }
                else if (item.Kind == CommsItemKind.Stroke) markCounts[1]++;
                else if (item.Kind == CommsItemKind.Label) markCounts[2]++;
            }
            SetTally(0, markCounts[0], CommsBoard.AuthorBudget(CommsItemKind.Ping));
            SetTally(1, markCounts[1], CommsBoard.AuthorBudget(CommsItemKind.Stroke));
            SetTally(2, markCounts[2], CommsBoard.AuthorBudget(CommsItemKind.Label));
            float load = board.Count / (float)CommsBoard.MaxPerAudience;
            tally.Set(3, AvNum.Fixed(board.Count, 0) + "/" + AvNum.Fixed(CommsBoard.MaxPerAudience, 0), load,
                load > 0.85f ? AvState.Caution : AvState.Info);

            // The list shows as many marks as the page can hold: a short console gets fewer rows instead of a scrollbar.
            AvFlow page = console.Page(TabMap);
            float width = page.Inner;
            float room = page.ViewportHeight <= 0f ? float.MaxValue : page.ViewportHeight - FixedHeight(page, boardRows);
            float used = 0f;
            int shown = 0;
            for (int i = items.Count - 1; i >= 0 && shown < BoardRows; i--)
            {
                CommsItem item = items[i];
                if (item.Kind == CommsItemKind.Sticker || comms.State.IsMuted(item.Author)) continue;
                NetRow row = boardRows[shown];
                string left = CommsText.Countdown(item.Expires - now);
                row.Set(ItemTag(item), Who(item.Author, item.AuthorName), ItemWords(item), left, ItemState(item));
                float h = row.Measure(width) + AvGridTokens.Gap;
                if (shown >= MinBoardRows && used + h > room) break;
                used += h;
                boardBound[shown] = item;
                row.Help = "Flash this mark on the map. It fades in " + left + ".";
                Show(row, true);
                shown++;
            }
            if (shown == 0)
            {
                boardBound[0] = null;
                boardRows[0].Set("", "", "PLOT CLEAR · NO MARKS HELD. ARM A TOOL, THEN CLICK THE MAP.", "", AvState.Inert);
                boardRows[0].Help = null;
                Show(boardRows[0], true);
                shown = 1;
            }
            for (int i = shown; i < BoardRows; i++)
            {
                boardBound[i] = null;
                Show(boardRows[i], false);
            }
            boardSection.SetCaption(eligible == 0 ? "NOTHING HELD"
                : (shown < eligible ? "NEWEST " + AvNum.Fixed(shown, 0) + " OF " + AvNum.Fixed(eligible, 0)
                                    : AvNum.Fixed(eligible, 0) + (eligible == 1 ? " MARK" : " MARKS")) + " · FADE TIMER AT RIGHT");
        }

        private void SetTally(int cell, int count, int budget)
        {
            float fill = budget <= 0 ? 0f : Mathf.Clamp01(count / (float)budget);
            tally.Set(cell, AvNum.Fixed(count, 0) + "/" + AvNum.Fixed(budget, 0), fill, fill >= 1f ? AvState.Caution : AvState.Info);
        }

        /// <summary>The kind stencilled on a ledger line: the ping's own code, or what sort of mark it is.</summary>
        private static string ItemTag(CommsItem item)
        {
            switch (item.Kind)
            {
                case CommsItemKind.Ping:
                    return CommsCatalog.ValidPing(item.Style) ? CommsCatalog.Pings[item.Style].Code : "PING";
                case CommsItemKind.Label:
                    return "TEXT";
                default:
                    return "DRAW";
            }
        }

        /// <summary>What the mark says and where it is: the phrase, the label's words or the ink, then its grid.</summary>
        private static string ItemWords(CommsItem item)
        {
            string at = CommsText.Grid(item.X, item.Z);
            switch (item.Kind)
            {
                case CommsItemKind.Ping:
                    return (CommsCatalog.ValidPing(item.Style) ? CommsCatalog.Pings[item.Style].Phrase : "PING") + " · " + at;
                case CommsItemKind.Label:
                    return "“" + item.Text + "” · " + at;
                default:
                    return (CommsCatalog.ValidPen(item.Style) ? CommsCatalog.Pens[item.Style].Name + " " : "") + "DRAWING · " + at;
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
    }
}

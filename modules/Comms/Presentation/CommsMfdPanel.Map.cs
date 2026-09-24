using BoscaliSummer.Features.Comms.Domain;
using BoscaliSummer.Features.Comms.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private const float TallButton = 42f;
        private const float RowButton = 28f;

        private static readonly CommsTool[] ToolRow =
        {
            CommsTool.Ping, CommsTool.Pen, CommsTool.Line, CommsTool.Arrow, CommsTool.Circle, CommsTool.Box,
            CommsTool.Sticker, CommsTool.Label, CommsTool.Eraser, CommsTool.Measure,
        };

        private static readonly string[] ToolGlyphs =
            { "ping", "pen", "line", "arrow", "circle", "box", "sticker", "text", "eraser", "measure" };

        private static readonly string[] ToolTips =
        {
            "Click the map to drop the selected ping. Middle-click does the same without arming anything.",
            "Freehand pen: drag on the map. Holding the draw key does this without opening COM.",
            "Straight line: drag from start to end.",
            "Arrow: drag from the tail to the tip — an attack axis or a route.",
            "Circle: drag from the centre out to the edge — a threat ring or a CAP station.",
            "Box: drag corner to corner — a kill box or an area to avoid.",
            "Sticker: click the map to place the selected sticker.",
            "Text label: type the words below, then click the map where they belong.",
            "Eraser: click one of your own marks to remove it (the host can erase any mark).",
            "Ruler: drag for range and true bearing. Only you see it.",
        };

        private AvButton[] toolButtons;
        private AvButton[] pingButtons;
        private AvButton[] penButtons;
        private AvButton[] widthButtons;
        private AvButton[] stickerButtons;
        private AvButton channelButton;
        private AvButton clearAllButton;
        private TMP_InputField labelField;
        private TMP_Text penNote;

        private void ResetMap()
        {
            toolButtons = null;
            pingButtons = null;
            penButtons = null;
            widthButtons = null;
            stickerButtons = null;
            channelButton = null;
            clearAllButton = null;
            labelField = null;
            penNote = null;
        }

        private void BuildMapPage(GameObject page)
        {
            const float build = HeadingHeight * 6 + TallButton * 5 + Gap * 2 + RowButton * 3 + SectionGap * 6 + 40f;
            RectTransform parent = PageBody(page, build, out float x, out float y, out float width);

            Heading(parent, x, ref y, width, "TOOLS", "LEFT CLICK OR DRAG ON THE MAP · ESC ENDS");
            toolButtons = new AvButton[ToolRow.Length + 2];
            for (int i = 0; i < ToolRow.Length; i++)
            {
                int row = i / 6, column = i % 6;
                CommsTool tool = ToolRow[i];
                toolButtons[i] = IconButton(parent, Cell(x, y - row * (TallButton + Gap), width, TallButton, 6, column),
                    ToolGlyphs[i], ToolLabel(tool), AvTheme.RailInfo, () => comms.ToggleTool(tool), ToolTips[i], out _);
            }
            toolButtons[ToolRow.Length] = IconButton(parent, Cell(x, y - (TallButton + Gap), width, TallButton, 6, 4),
                "undo", "UNDO", AvTheme.RailInfo, () => comms.Undo(), "Take back your most recent mark.", out _);
            toolButtons[ToolRow.Length + 1] = IconButton(parent, Cell(x, y - (TallButton + Gap), width, TallButton, 6, 5),
                "off", "OFF", AvTheme.Dim, () => { comms.SetTool(CommsTool.None); comms.ClearMeasure(); },
                "Put the tool down and give the map back its normal clicks.", out _);
            y -= TallButton * 2 + Gap + SectionGap;

            Heading(parent, x, ref y, width, "PING", QuickPingNote());
            pingButtons = new AvButton[CommsCatalog.PalettePings];
            for (int i = 0; i < pingButtons.Length; i++)
            {
                int kind = i;
                PingKind ping = CommsCatalog.Pings[i];
                pingButtons[i] = IconButton(parent, Cell(x, y, width, TallButton, pingButtons.Length, i), ping.Glyph, ping.Code,
                    ToneColour(ping.Tone), () => { comms.PingKind = kind; comms.SetTool(CommsTool.Ping); },
                    ping.Phrase + ". Pick it, then click the map.", out _);
            }
            y -= TallButton + SectionGap;

            penNote = Heading(parent, x, ref y, width, "PEN", "");
            int pens = CommsCatalog.Pens.Length;
            float swatchWidth = 34f;
            penButtons = new AvButton[pens];
            for (int i = 0; i < pens; i++)
            {
                int ink = i;
                var area = new Rect(x + i * (swatchWidth + Gap), y, swatchWidth, RowButton - 2f);
                penButtons[i] = Button(parent, area, "", () => comms.PenInk = ink, CommsCatalog.Pens[i].Name + " ink.");
                Image chip = AvKit.Panel((RectTransform)penButtons[i].transform, new Rect(6f, -6f, swatchWidth - 12f, RowButton - 14f),
                    CommsMesh.Ink(i));
                chip.raycastTarget = false;
            }
            float widthsX = x + pens * (swatchWidth + Gap) + SectionGap;
            float widthsWidth = x + width - widthsX;
            widthButtons = new AvButton[CommsCatalog.PenWidths.Length];
            for (int i = 0; i < widthButtons.Length; i++)
            {
                int w = i;
                widthButtons[i] = Button(parent, Cell(widthsX, y, widthsWidth, RowButton - 2f, widthButtons.Length, i),
                    CommsCatalog.PenWidthNames[i], () => comms.PenWidth = w, "Stroke width for the pen and shapes.");
            }
            y -= RowButton + SectionGap;

            Heading(parent, x, ref y, width, "STICKERS", "FOR FUN — AND FOR POINTING");
            stickerButtons = new AvButton[CommsCatalog.Stickers.Length];
            for (int i = 0; i < stickerButtons.Length; i++)
            {
                int kind = i;
                int row = i / 6, column = i % 6;
                StickerKind sticker = CommsCatalog.Stickers[i];
                stickerButtons[i] = IconButton(parent, Cell(x, y - row * (TallButton + Gap), width, TallButton, 6, column),
                    sticker.Glyph, sticker.Name, ToneColour(sticker.Tone),
                    () => { comms.StickerKind = kind; comms.SetTool(CommsTool.Sticker); },
                    sticker.Name + " sticker. Pick it, then click the map.", out _);
            }
            y -= TallButton * 2 + Gap + SectionGap;

            Heading(parent, x, ref y, width, "TEXT LABEL", "UP TO " + CommsText.MaxLabel + " CHARACTERS");
            labelField = AvKit.InputField(parent, new Rect(x, y, width - 96f, RowButton), CommsText.MaxLabel,
                value => comms.LabelText = value, placeholderText: "FARP HERE, CAP EAST…",
                tooltip: "Words for the map. Press PLACE, then click where they go.");
            Button(parent, new Rect(x + width - 90f, y, 90f, RowButton), "PLACE", () =>
            {
                comms.LabelText = labelField != null ? labelField.text : comms.LabelText;
                comms.SetTool(CommsTool.Label);
            }, "Arm the label, then click the map.", AvButtonStyle.Primary);
            y -= RowButton + SectionGap;

            Heading(parent, x, ref y, width, "SEND TO", "TEAM IS YOUR SIDE ONLY");
            channelButton = Button(parent, Cell(x, y, width, RowButton, 3, 0), "", () => comms.ToggleChannel(),
                "Who sees what you post next: your team only, or every player including the other side.");
            Button(parent, Cell(x, y, width, RowButton, 3, 1), "CLEAR MINE", () => comms.ClearMine(),
                "Remove everything you have put on the map.", AvButtonStyle.Danger);
            clearAllButton = Button(parent, Cell(x, y, width, RowButton, 3, 2), "CLEAR MAP", () => comms.ClearAll(),
                "Host only: wipe every mark from the shared map.", AvButtonStyle.Danger);
            y -= RowButton + SectionGap;

            AvStyled.Label(parent, new Rect(x, y, width, 34f), HoldHint(), "hint");
        }

        private void RefreshMap()
        {
            if (toolButtons == null) return;
            for (int i = 0; i < ToolRow.Length; i++) toolButtons[i].SetLatched(comms.Tool == ToolRow[i]);
            for (int i = 0; i < pingButtons.Length; i++)
                pingButtons[i].SetLatched(comms.PingKind == i && comms.Tool == CommsTool.Ping);
            for (int i = 0; i < penButtons.Length; i++) penButtons[i].SetLatched(comms.PenInk == i);
            for (int i = 0; i < widthButtons.Length; i++) widthButtons[i].SetLatched(comms.PenWidth == i);
            for (int i = 0; i < stickerButtons.Length; i++)
                stickerButtons[i].SetLatched(comms.StickerKind == i && comms.Tool == CommsTool.Sticker);

            bool team = comms.Channel == CommsChannel.Team;
            channelButton.SetText(team ? "TEAM ONLY" : "ALL PLAYERS");
            channelButton.SetLatched(!team);
            clearAllButton.SetEnabled(comms.IsHost);
            if (penNote != null)
                penNote.text = CommsCatalog.Pens[Mathf.Clamp(comms.PenInk, 0, CommsCatalog.Pens.Length - 1)].Name + " · " +
                               CommsCatalog.PenWidthNames[Mathf.Clamp(comms.PenWidth, 0, CommsCatalog.PenWidthNames.Length - 1)];
        }

        private string QuickPingNote()
        {
            KeyCode key = settings.QuickPingKey.Value;
            return key == KeyCode.None ? "PICK A TYPE, CLICK THE MAP" : KeyName(key) + " DROPS IT ANYWHERE";
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
    }
}

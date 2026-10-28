using NOAvionics;
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

        private AvSection plotSection;
        private AvControl[] toolButtons;
        private AvControl[] pingButtons;
        private InkDeck penRow;
        private AvField labelField;
        private AvControl clearAllButton;

        private void ResetMap()
        {
            plotSection = null;
            toolButtons = null;
            pingButtons = null;
            penRow = null;
            labelField = null;
            clearAllButton = null;
        }

        private void BuildMapPage(AvFlow p)
        {
            // Top to bottom: pick a tool and its ink, or a marker, then click the map. The map itself is the list of marks.
            plotSection = p.Section(AvIcon.Pencil, "PLOT", "MAP FREE");
            var toolSpecs = new AvControl.Spec[ToolRow.Length + 2];
            var toolHelps = new string[toolSpecs.Length];
            for (int i = 0; i < ToolRow.Length; i++)
            {
                CommsTool tool = ToolRow[i];
                toolSpecs[i] = new AvControl.Spec(ToolLabel(tool), () => comms.ToggleTool(tool), AvButtonStyle.Default, ToolIcons[i]);
                toolHelps[i] = ToolTips[i];
            }
            toolSpecs[ToolRow.Length] = new AvControl.Spec("UNDO", () => comms.Undo(), AvButtonStyle.Quiet, AvIcon.ArrowBackUp);
            toolHelps[ToolRow.Length] = "Undo: take back your most recent mark.";
            toolSpecs[ToolRow.Length + 1] = new AvControl.Spec("TOOL OFF", () =>
            {
                comms.SetTool(CommsTool.None);
                comms.ClearMeasure();
            }, AvButtonStyle.Quiet, AvIcon.X);
            toolHelps[ToolRow.Length + 1] = "Tool off: give the map back its normal clicks. ESC does the same.";
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

            labelField = new AvField(p.Content, "TEXT: FARP HERE, CAP EAST…", CommsText.MaxLabel, _ => ArmLabel());
            Tip(labelField, "Words for the map, up to " + CommsText.MaxLabel + " characters. Press PLACE, then click where they go.");
            var placeRow = new AvButtons(p.Content, new[] { new AvControl.Spec("PLACE", ArmLabel, AvButtonStyle.Primary, AvIcon.MapPin) });
            placeRow.Controls[0].Help = "Arm the text label, then click the map where it belongs.";
            p.Row(labelField, placeRow);

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

            console.Orders(TabMap,
                "Arm a tool, then click or drag on the map. ESC puts it down.",
                HoldHint());
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
            clearAllButton.Interactable = comms.IsHost;
        }

        /// <summary>A hostile tone (enemy, SAM, spike) reads as the kit's danger style; the icon and code still say which.</summary>
        private static AvButtonStyle ToneStyle(CommsTone tone) => tone == CommsTone.Danger ? AvButtonStyle.Danger : AvButtonStyle.Default;

        private string HoldHint()
        {
            KeyCode hold = settings.DrawHoldKey.Value;
            string draw = hold == KeyCode.None ? "" : "Hold " + KeyName(hold) + " and drag on the map to draw at any time. ";
            return draw + "Marks fade on their own; the host sets how long they last.";
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

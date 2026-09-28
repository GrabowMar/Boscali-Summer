using System;
using BoscaliSummer.Features.Comms.Domain;
using BoscaliSummer.Features.Comms.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Comms.Presentation
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

        private AvControl[] toolButtons;
        private AvControl[] pingButtons;
        private PenRow penRow;
        private AvSection penSection;
        private AvControl[] stickerButtons;
        private AvField labelField;
        private AvControl channelButton;
        private AvControl clearAllButton;

        private void ResetMap()
        {
            toolButtons = null;
            pingButtons = null;
            penRow = null;
            penSection = null;
            stickerButtons = null;
            labelField = null;
            channelButton = null;
            clearAllButton = null;
        }

        private void BuildMapPage(AvFlow p)
        {
            p.Section(AvIcon.Pencil, "TOOLS", "LEFT CLICK OR DRAG ON THE MAP · ESC ENDS");
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
            toolButtons = ButtonGrid(p, toolSpecs, 6);

            p.Section(AvIcon.MapPin, "PING", QuickPingNote());
            var pingSpecs = new AvControl.Spec[CommsCatalog.PalettePings];
            for (int i = 0; i < pingSpecs.Length; i++)
            {
                int kind = i;
                PingKind ping = CommsCatalog.Pings[i];
                pingSpecs[i] = new AvControl.Spec(ping.Code, () =>
                {
                    comms.PingKind = kind;
                    comms.SetTool(CommsTool.Ping);
                }, AvButtonStyle.Default, PingIcons[i]);
            }
            pingButtons = ButtonGrid(p, pingSpecs, pingSpecs.Length);

            penSection = p.Section(AvIcon.Pencil, "PEN", "");
            var inks = new Color[CommsCatalog.Pens.Length];
            for (int i = 0; i < inks.Length; i++) inks[i] = CommsMesh.Ink(i);
            penRow = p.Add(new PenRow(p.Content, inks, ink => comms.PenInk = ink,
                CommsCatalog.PenWidthNames, w => comms.PenWidth = w));

            p.Section(AvIcon.Sticker, "STICKERS", "FOR FUN — AND FOR POINTING");
            var stickerSpecs = new AvControl.Spec[CommsCatalog.Stickers.Length];
            for (int i = 0; i < stickerSpecs.Length; i++)
            {
                int kind = i;
                StickerKind sticker = CommsCatalog.Stickers[i];
                stickerSpecs[i] = new AvControl.Spec(sticker.Name, () =>
                {
                    comms.StickerKind = kind;
                    comms.SetTool(CommsTool.Sticker);
                }, AvButtonStyle.Default, StickerIcons[i]);
            }
            stickerButtons = ButtonGrid(p, stickerSpecs, 6);

            p.Section(AvIcon.Typography, "TEXT LABEL", "UP TO " + CommsText.MaxLabel + " CHARACTERS");
            labelField = p.Add(new AvField(p.Content, "FARP HERE, CAP EAST…", CommsText.MaxLabel, _ => ArmLabel()));
            p.Buttons(new AvControl.Spec("PLACE", ArmLabel, AvButtonStyle.Primary, AvIcon.MapPin));

            p.Section(AvIcon.Message2, "SEND TO", "TEAM IS YOUR SIDE ONLY");
            AvButtons sendRow = p.Buttons(
                new AvControl.Spec("", () => comms.ToggleChannel()),
                new AvControl.Spec("CLEAR MINE", () => comms.ClearMine(), AvButtonStyle.Danger, AvIcon.Eraser),
                new AvControl.Spec("CLEAR MAP", () => comms.ClearAll(), AvButtonStyle.Danger, AvIcon.Eraser));
            channelButton = sendRow.Controls[0];
            clearAllButton = sendRow.Controls[2];

            p.Add(new AvNote(p.Content, HoldHint()));
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

            bool team = comms.Channel == CommsChannel.Team;
            channelButton.Label = team ? "TEAM ONLY" : "ALL PLAYERS";
            channelButton.Latched = !team;
            clearAllButton.Interactable = comms.IsHost;
            penSection?.SetCaption(CommsCatalog.Pens[Mathf.Clamp(comms.PenInk, 0, CommsCatalog.Pens.Length - 1)].Name + " · " +
                CommsCatalog.PenWidthNames[Mathf.Clamp(comms.PenWidth, 0, CommsCatalog.PenWidthNames.Length - 1)]);
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

using System;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal sealed partial class SettingsMfdPanel
    {
        /// <summary>
        /// The common HUD element's own rows, in the COCKPIT page. Everything here is client-local
        /// presentation and applies on the board's next tick, so the pilot sees the change while
        /// flying. The feeds are listed from whatever modules declared one, not from a list kept here.
        /// </summary>
        private void BuildHudRows(AvFlow flow, int page, IHudBoard board)
        {
            Func<bool> on = () => board != null && board.Enabled;
            Func<string> off = () => "Turn the common HUD element on first.";

            flow.Section(AvIcon.Eye, "HUD ELEMENT", "STATUS STACK");
            Toggle(flow, page, "HUD ELEMENT",
                "Draw the one cockpit HUD element every feature shares for status lines and notices.",
                () => on(), v => { if (board != null) board.Enabled = v; });

            Stepper(flow, page, "SIZE",
                () => board != null ? HudLayout.ScaleName(board.ScaleStep) : "--",
                d => { if (board != null) board.ScaleStep = HudLayout.Cycle(board.ScaleStep, HudLayout.ScaleCount, d); },
                () => board != null, () => board != null,
                "Text size as a multiple of the game's own overlay text size option.",
                on, off);

            Stepper(flow, page, "OPACITY",
                () => board != null ? HudLayout.OpacityName(board.OpacityStep) : "--",
                d => { if (board != null) board.OpacityStep = HudLayout.Cycle(board.OpacityStep, HudLayout.OpacityCount, d); },
                () => board != null, () => board != null,
                "How solid the element reads over a bright sky. OFF hides it without unloading it.",
                on, off);

            Stepper(flow, page, "CONTRAST",
                () => HudLayout.ContrastName(board?.Contrast ?? 1),
                d => { if (board != null) board.Contrast = HudLayout.Cycle(board.Contrast, 3, d); },
                () => true, () => true, "CLEAR floating ink, GLASS backing, or SOLID for bright sky.", on, off);

            Stepper(flow, page, "MAX LINES",
                () => board != null ? AvNum.Fixed(board.MaxRows, 0) : "--",
                d => { if (board != null) board.MaxRows = Mathf.Clamp(board.MaxRows + d, HudLayout.MinRows, HudLayout.MaxRows); },
                () => board != null && board.MaxRows > HudLayout.MinRows,
                () => board != null && board.MaxRows < HudLayout.MaxRows,
                "How many lines the element may show at once. Two are kept for live notices.",
                on, off);

            Toggle(flow, page, "DETAIL LINES", "Supporting text and progress gauges; off uses compact single-line rows.",
                () => board != null && board.ShowDetails, v => { if (board != null) board.ShowDetails = v; }, on, off);

            flow.Section(AvIcon.Message2, "NOTICES", "TRANSIENT ALERTS");
            Toggle(flow, page, "NOTICES",
                "Show transient notices, including ace hunt and mission alerts.",
                () => board != null && board.NoticesEnabled,
                v => { if (board != null) board.NoticesEnabled = v; },
                on, off);

            Stepper(flow, page, "NOTICE TIME",
                () => board != null ? AvNum.Seconds(board.NoticeSeconds, 0) : "--",
                d => { if (board != null) board.NoticeSeconds = Mathf.Clamp(board.NoticeSeconds + d, HudLayout.MinNoticeSeconds, HudLayout.MaxNoticeSeconds); },
                () => board != null && board.NoticeSeconds > HudLayout.MinNoticeSeconds,
                () => board != null && board.NoticeSeconds < HudLayout.MaxNoticeSeconds,
                "How long a transient notice stays up.",
                () => on() && board.NoticesEnabled, () => "Turn notices on first.");

            flow.Section(AvIcon.Maximize, "PLACEMENT", "SAFE AREA");
            HudOffset(flow, page, "HORIZONTAL", () => board?.OffsetX ?? 0,
                v => { if (board != null) board.OffsetX = v; }, -600, on, off);
            HudOffset(flow, page, "VERTICAL", () => board?.OffsetY ?? 0,
                v => { if (board != null) board.OffsetY = v; }, -600, on, off);
            flow.Buttons(new AvControl.Spec("RESET STATUS LAYOUT", () =>
            { board?.ResetLayout(); Echo("Status layout restored. Feed preferences kept."); Changed(); }))
                .Controls[0].Help = "Restore the status layout. Feed preferences are kept.";

            flow.Section(AvIcon.ListDetails, "FEEDS", board == null ? "UNAVAILABLE" : "PER FEATURE");
            if (board == null)
            {
                Toggle(flow, page, "FEEDS",
                    "The common HUD element is not installed in this session.",
                    () => false, v => { }, () => false, () => "HUD element unavailable.");
                return;
            }

            int feeds = Mathf.Min(board.Channels.Count, HudLayout.MaxChannels);
            for (int i = 0; i < feeds; i++)
            {
                IHudChannel feed = board.Channels[i];
                Toggle(flow, page, feed.Label,
                    "Show this feed on the common HUD element. Switching it off hides its lines " +
                    "and changes nothing about how the feature itself runs.",
                    () => feed.Enabled, v => { if (feed.Enabled != v) feed.Toggle(); },
                    on, off);
            }
        }

        private void HudOffset(AvFlow flow, int page, string label, Func<int> read,
            Action<int> write, int min, Func<bool> enabled, Func<string> reason)
        {
            Stepper(flow, page, label, () => AvNum.Fixed(read(), 0) + " px",
                d => write(Mathf.Clamp(read() + d * 20, min, 600)),
                () => read() > min, () => read() < 600,
                "Adjust by 20 reference pixels. The complete overlay is clamped inside the safe area.", enabled, reason);
        }
    }
}

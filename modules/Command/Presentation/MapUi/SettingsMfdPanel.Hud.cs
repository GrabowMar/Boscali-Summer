using NOAvionics;
using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal sealed partial class SettingsMfdPanel
    {
        /// <summary>
        /// The common HUD element's own controls, in the COCKPIT page: its switches join the page's toggle grid,
        /// its levels are dials, its feeds are a grid of toggles, and a live sample of the element grows at the
        /// foot. Everything here is client-local presentation and applies on the board's next tick, so the pilot
        /// sees the change while flying. The feeds are listed from whatever modules declared one, not from a
        /// list kept here.
        /// </summary>
        private void BuildHudRows(AvFlow flow, int page, IHudBoard board, AvCellGrid switches)
        {
            Func<bool> on = () => board != null && board.Enabled;
            Func<string> off = () => "Turn the common HUD element on first.";

            ToggleCell(flow, switches, page, "HUD ELEMENT",
                "Draw the one cockpit HUD element every feature shares for status lines and notices.",
                () => on(), v => { if (board != null) board.Enabled = v; });
            ToggleCell(flow, switches, page, "DETAIL LINES",
                "Supporting text and progress gauges; off uses compact single-line rows.",
                () => board != null && board.ShowDetails, v => { if (board != null) board.ShowDetails = v; }, on, off);

            AvCell notices = ToggleCell(flow, null, page, "NOTICES",
                "Show transient notices, including ace hunt and mission alerts.",
                () => board != null && board.NoticesEnabled,
                v => { if (board != null) board.NoticesEnabled = v; },
                on, off);
            var reset = new AvButtons(flow.Content, new[]
            {
                new AvControl.Spec("RESET STATUS LAYOUT", () =>
                    { board?.ResetLayout(); Echo("Status layout restored. Feed preferences kept."); Changed(); },
                    AvButtonStyle.Default, AvIcon.Refresh),
            });
            reset.Controls[0].Help = "Restore the status layout. Feed preferences are kept.";
            flow.Row(notices, reset);

            var look = flow.Add(new SetRingRow(flow.Content));
            Ring(flow, look, page, "SIZE",
                () => board != null ? HudLayout.ScaleName(board.ScaleStep) : "--",
                () => board != null ? HudLayout.ClampScale(board.ScaleStep) / (float)(HudLayout.ScaleCount - 1) : 0f,
                d => { if (board != null) board.ScaleStep = HudLayout.Cycle(board.ScaleStep, HudLayout.ScaleCount, d); },
                () => board != null, () => board != null,
                "Text size as a multiple of the game's own overlay text size option.", on, off);
            Ring(flow, look, page, "OPACITY",
                () => board != null ? HudLayout.OpacityName(board.OpacityStep) : "--",
                () => board != null ? HudLayout.Opacity(board.OpacityStep) : 0f,
                d => { if (board != null) board.OpacityStep = HudLayout.Cycle(board.OpacityStep, HudLayout.OpacityCount, d); },
                () => board != null, () => board != null,
                "How solid the element reads over a bright sky. OFF hides it without unloading it.", on, off);
            Ring(flow, look, page, "CONTRAST",
                () => HudLayout.ContrastName(board?.Contrast ?? 1),
                () => Mathf.Clamp(board?.Contrast ?? 1, 0, 2) / 2f,
                d => { if (board != null) board.Contrast = HudLayout.Cycle(board.Contrast, 3, d); },
                () => true, () => true, "CLEAR floating ink, GLASS backing, or SOLID for bright sky.", on, off);
            Ring(flow, look, page, "MAX LINES",
                () => board != null ? AvNum.Fixed(board.MaxRows, 0) : "--",
                () => board != null ? (board.MaxRows - HudLayout.MinRows) / (float)(HudLayout.MaxRows - HudLayout.MinRows) : 0f,
                d => { if (board != null) board.MaxRows = Mathf.Clamp(board.MaxRows + d, HudLayout.MinRows, HudLayout.MaxRows); },
                () => board != null && board.MaxRows > HudLayout.MinRows,
                () => board != null && board.MaxRows < HudLayout.MaxRows,
                "How many lines the element may show at once. Two are kept for live notices.", on, off);

            var place = flow.Add(new SetRingRow(flow.Content));
            Ring(flow, place, page, "NOTICE TIME",
                () => board != null ? AvNum.Seconds(board.NoticeSeconds, 0) : "--",
                () => board != null
                    ? (board.NoticeSeconds - HudLayout.MinNoticeSeconds) / (HudLayout.MaxNoticeSeconds - HudLayout.MinNoticeSeconds)
                    : 0f,
                d => { if (board != null) board.NoticeSeconds = Mathf.Clamp(board.NoticeSeconds + d, HudLayout.MinNoticeSeconds, HudLayout.MaxNoticeSeconds); },
                () => board != null && board.NoticeSeconds > HudLayout.MinNoticeSeconds,
                () => board != null && board.NoticeSeconds < HudLayout.MaxNoticeSeconds,
                "How long a transient notice stays up.",
                () => on() && board.NoticesEnabled, () => "Turn notices on first.");
            HudOffset(flow, place, page, "HORIZONTAL", () => board?.OffsetX ?? 0,
                v => { if (board != null) board.OffsetX = v; }, -600, on, off);
            HudOffset(flow, place, page, "VERTICAL", () => board?.OffsetY ?? 0,
                v => { if (board != null) board.OffsetY = v; }, -600, on, off);

            if (board == null)
            {
                flow.Section(AvIcon.ListDetails, "FEEDS", "UNAVAILABLE");
                ToggleCell(flow, flow.Grid(2), page, "FEEDS",
                    "The common HUD element is not installed in this session.",
                    () => false, v => { }, () => false, () => "HUD element unavailable.");
            }
            else if (board.Channels.Count > 0)
            {
                // Only features that declared a feed appear; a session with none has nothing to configure here.
                flow.Section(AvIcon.ListDetails, "FEEDS", "WHAT THE ELEMENT SHOWS");
                AvCellGrid feedGrid = flow.Grid(2);
                int feeds = Mathf.Min(board.Channels.Count, HudLayout.MaxChannels);
                for (int i = 0; i < feeds; i++)
                {
                    IHudChannel feed = board.Channels[i];
                    ToggleCell(flow, feedGrid, page, feed.Label,
                        "Show this feed on the common HUD element. Switching it off hides its lines " +
                        "and changes nothing about how the feature itself runs.",
                        () => feed.Enabled, v => { if (feed.Enabled != v) feed.Toggle(); },
                        on, off);
                }
            }

            var sample = flow.Add(new SetHudPreview(flow.Content, board), 1f);
            sample.Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, sample.Refresh);
        }

        private void HudOffset(AvFlow flow, SetRingRow row, int page, string label, Func<int> read,
            Action<int> write, int min, Func<bool> enabled, Func<string> reason)
        {
            Ring(flow, row, page, label, () => AvNum.Fixed(read(), 0) + " px",
                () => (read() - min) / (float)(600 - min),
                d => write(Mathf.Clamp(read() + d * 20, min, 600)),
                () => read() > min, () => read() < 600,
                "Adjust by 20 reference pixels. The complete overlay is clamped inside the safe area.", enabled, reason,
                () => AvNum.Fixed(read(), 0));
        }
    }
}

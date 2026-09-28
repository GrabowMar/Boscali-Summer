using System;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal sealed partial class SettingsMfdPanel
    {
        private void BuildHudPage(RectTransform parent, Rect body)
        {
            ModServices.TryGet(out IHudBoard board);
            int feeds = board == null ? 1 : Mathf.Min(board.Channels.Count, HudLayout.MaxChannels);
            parent = Page(4, parent, body, HudSettingRows + feeds, 1, out var area);
            Heading(parent, ref area, "01", "STATUS & NOTICES", "LOCAL DISPLAY");
            BuildHudRows(parent, ref area, board);
        }

        private void HudOffset(RectTransform parent, ref Rect area, string label, Func<int> read,
            Action<int> write, int min, Func<bool> enabled, Func<string> reason)
        {
            Stepper(parent, TakeRow(ref area), label, () => read() + " px",
                d => write(Mathf.Clamp(read() + d * 20, min, 600)),
                () => read() > min, () => read() < 600,
                "Adjust by 20 reference pixels. The complete overlay is clamped inside the safe area.", enabled, reason);
        }
    }
}

using BoscaliSummer.Framework.Features;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal sealed partial class SettingsMfdPanel
    {
        private void BuildPerformancePage(RectTransform parent, Rect body)
        {
            var rows = clientSettings?.Rows;
            int count = rows?.Count ?? 0;
            int sections = 0;
            string previous = null;
            for (int i = 0; i < count; i++)
            {
                if (rows[i].Section == previous) continue;
                previous = rows[i].Section;
                sections++;
            }
            if (sections == 0) sections = 1;
            parent = Page(5, parent, body, count, sections, out var area);
            if (count == 0)
            {
                Heading(parent, ref area, "01", "PERFORMANCE", "UNAVAILABLE");
                AvStyled.Label(parent, TakeRow(ref area),
                    "NO CLIENT PERFORMANCE CONTROLS INSTALLED", "body",
                    align: TextAlignmentOptions.MidlineLeft);
                return;
            }

            previous = null;
            int section = 0;
            for (int i = 0; i < count; i++)
            {
                ClientSettingToggle row = rows[i];
                if (row.Section != previous)
                {
                    previous = row.Section;
                    section++;
                    Heading(parent, ref area, section.ToString("00"), previous, "NO RESTART");
                }
                Toggle(parent, TakeRow(ref area), row.Label, row.Help,
                    () => row.Value, row.Set);
            }
        }
    }
}

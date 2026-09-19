using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPACE › ENEMY ACTIVITY — the other side's stations. Tracked platforms are live (orbit,
    /// size from the silhouette the host discloses, overhead or away, manoeuvres); the
    /// counterspace desk is a set of stubs marked COMING SOON so the room shows where the
    /// feature is going without pretending it works.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float CounterspaceNoticeHeight = 104f;

        private readonly OpsRow[] foreignRows = new OpsRow[SpaceOperations.MaximumForeign];
        private TMP_Text foreignNote;

        private void ResetEnemyPage()
        {
            for (int i = 0; i < foreignRows.Length; i++) foreignRows[i] = null;
            foreignNote = null;
        }

        private void BuildEnemyPage(RectTransform root, Rect body)
        {
            float height = HeaderHeight + foreignRows.Length * CompactRowHeight + SectionGap +
                           HeaderHeight + CounterspaceNoticeHeight + SectionGap;
            float rowHeight = CompactRowHeight;
            RectTransform parent = BeginSub(root, body, height, out float x, out float y, out float width);

            foreignNote = Header(parent, x, ref y, width, "TRACKED PLATFORMS", "");
            for (int i = 0; i < foreignRows.Length; i++)
            {
                foreignRows[i] = Row(parent, x, y, width, true, "UNK", "TRACK " + (i + 1), null, null, null,
                                     height: rowHeight);
                y -= rowHeight;
            }
            y -= SectionGap;

            Header(parent, x, ref y, width, "COUNTERSPACE", "COMING SOON · NO ACTIONS AVAILABLE");
            AvStyled.Label(parent, new Rect(x, y, width, 18f), "TRACKING ONLY", "row-name");
            TMP_Text notice = Wrapped(AvStyled.Label(parent, new Rect(x, y - 28f, width, 68f),
                "Enemy orbit and silhouette are host-reported; fitted modules are not disclosed. " +
                "ASAT warnings, signal interception, debris alerts and orbital defence are not implemented yet.", "row-sub"));
            notice.maxVisibleLines = 4;
            notice.color = AvTheme.Dim;
        }

        private void RefreshEnemyPage(double now, in OrbitClock clock)
        {
            if (foreignNote == null) return;
            IReadOnlyList<ForeignPlatform> tracks = support.Space.Foreign;
            int overhead = 0;
            for (int i = 0; i < foreignRows.Length; i++)
            {
                OpsRow row = foreignRows[i];
                if (i >= tracks.Count)
                {
                    row.Code.text = "—";
                    row.Name.text = "EMPTY TRACK " + (i + 1);
                    Paint(row, Tone.Locked, "NO HOST-REPORTED PLATFORM");
                    continue;
                }

                ForeignPlatform track = tracks[i];
                OrbitRegime orbit = OrbitRegimes.Get(track.Regime);
                OrbitState state = track.State(now, clock);
                int modules = track.ModuleCount;
                string size = modules <= 3 ? "LIGHT" : modules <= 7 ? "MEDIUM" : "HEAVY";
                row.Code.text = "UNK";
                row.Name.text = "UNKNOWN STATION " + (i + 1) + " · " + orbit.Code;

                Tone tone;
                string status;
                if (state.Phase == OrbitPhase.Hold)
                {
                    tone = Tone.Pending;
                    status = "MANOEUVRING · REACQUIRE " + PlatformWords.Clock(state.TimeToPass);
                }
                else if (state.InPass)
                {
                    overhead++;
                    tone = Tone.Danger;
                    status = "OVERHEAD · LOS " + PlatformWords.Clock(state.TimeToPassEnd) + " · " + modules + " MODULES (" + size + ")";
                }
                else
                {
                    tone = Tone.Armed;
                    status = "AWAY · AOS " + PlatformWords.Clock(state.TimeToPass) + " · " + modules + " MODULES (" + size + ")";
                }
                Paint(row, tone, status);
            }
            foreignNote.text = tracks.Count + "/" + foreignRows.Length +
                               (tracks.Count == 1 ? " TRACK" : " TRACKS") +
                               (overhead > 0 ? " · " + overhead + " OVERHEAD"
                                             : tracks.Count == 0 ? " · SKY CLEAR" : "");
        }
    }
}

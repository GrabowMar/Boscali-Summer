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
        private const float StubHeight = 50f;

        private static readonly string[][] Stubs =
        {
            new[] { "ASA", "ASAT WARNING", "Direct-ascent launches against our station, with a time to intercept." },
            new[] { "SIG", "SIGNALS INTERCEPT", "What the enemy station is tasking, heard through our SIGINT array." },
            new[] { "DBR", "DEBRIS WATCH", "Conjunction alerts and debris fields after a kill." },
            new[] { "DEF", "ORBITAL DEFENCE", "Decoys, manoeuvres and shielding postures against counterspace fire." }
        };

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
                           HeaderHeight + Stubs.Length * (StubHeight + 6f) + SectionGap;
            RectTransform parent = BeginSub(root, body, height, out float x, out float y, out float width);

            foreignNote = Header(parent, x, ref y, width, "TRACKED PLATFORMS", "");
            for (int i = 0; i < foreignRows.Length; i++)
            {
                foreignRows[i] = Row(parent, x, y, width, true, "UNK", "TRACK " + (i + 1), null, null, null);
                y -= CompactRowHeight;
            }
            y -= SectionGap;

            Header(parent, x, ref y, width, "COUNTERSPACE DESK", "COMING SOON");
            for (int i = 0; i < Stubs.Length; i++)
            {
                var area = new Rect(x, y, width, StubHeight);
                AvStyled.Box(parent, area, "card inert");
                AvStyled.Rail(parent, area, "locked");
                AvKit.Label(parent, Stubs[i][0], new Rect(x + 10f, y - 6f, 34f, 16f), AvTheme.Dim, AvTokens.FontLead,
                    FontStyles.Bold);
                SingleLine(AvStyled.Label(parent, new Rect(x + 50f, y - 6f, width - 170f, 16f), Stubs[i][1], "row-name"))
                    .color = AvTheme.Dim;
                AvStyled.Label(parent, new Rect(x + 50f, y - 25f, width - 60f, 20f), Stubs[i][2], "row-sub").color =
                    AvTheme.Disabled;
                var stamp = new Rect(x + width - 112f, y - 6f, 102f, 16f);
                AvStyled.Box(parent, stamp, "stamp warn");
                AvStyled.Label(parent, stamp, "COMING SOON", "stamp warn", align: TextAlignmentOptions.Center);
                y -= StubHeight + 6f;
            }
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
                    row.Name.text = "TRACK " + (i + 1);
                    Paint(row, Tone.Locked, "NO TRACK · SKY CLEAR ON THIS SLOT");
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
            foreignNote.text = tracks.Count == 0 ? "NO TRACKS"
                : tracks.Count + " TRACK" + (tracks.Count == 1 ? "" : "S") + (overhead > 0 ? " · " + overhead + " OVERHEAD" : "");
        }
    }
}

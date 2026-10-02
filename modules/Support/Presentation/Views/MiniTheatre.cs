using BoscaliSummer.Modules.Support.Domain.SpecOps;
using BoscaliSummer.Modules.Support.Presentation.Board;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Presentation.Window;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    /// <summary>
    /// The compact SPEC OPS theatre on OPS › SPEC OPS › STATUS: objectives by relation, owned airbases,
    /// deployed teams as lettered plates on their objectives and charged posts' sectors, fitted with the
    /// same percentile rule as the desk so a far home base cannot shrink the cluster.
    /// </summary>
    internal sealed class MiniTheatre
    {
        private const int Objectives = SpecOpsDetachment.ObjectiveSlots;
        private const int Teams = SpecOpsDetachment.TeamCount;
        private const int Homes = 8;

        private BoardSurface board;
        private BoardSectorOverlay sectors;
        private readonly Image[] marks = new Image[Objectives];
        private readonly Image[] homes = new Image[Homes];
        private readonly Image[] plates = new Image[Teams];
        private readonly TMP_Text[] letters = new TMP_Text[Teams];
        private readonly float[] fitX = new float[Objectives + Homes];
        private readonly float[] fitZ = new float[Objectives + Homes];
        private readonly float[] homeX = new float[Homes];
        private readonly float[] homeZ = new float[Homes];
        private int homeCount;
        private TMP_Text empty;

        public void Build(RectTransform parent, Rect view)
        {
            OpsSprites.Ensure();
            Chrome.Panel(parent, view, AvTheme.Ground);
            Image contours = Chrome.Panel(parent, view, AvTheme.RailInfo.WithAlpha(0.09f), OpsSprites.Contours);
            contours.type = Image.Type.Tiled;
            contours.raycastTarget = false;
            Chrome.Outline(parent, view, AvTheme.Hairline);
            RoomPaint.Brackets(parent, view, 8f, AvTheme.RailInfo);
            board = new BoardSurface(parent, view, view, false);
            sectors = new BoardSectorOverlay(parent, board, AvTheme.Hairline, AvTheme.RailReady, AvTheme.RailInfo);
            for (int i = 0; i < Homes; i++)
            {
                homes[i] = Chrome.Panel(parent, new Rect(0f, 0f, 7f, 7f), AvTheme.RailInfo);
                homes[i].enabled = false;
            }
            for (int i = 0; i < Objectives; i++)
            {
                marks[i] = Chrome.Panel(parent, new Rect(0f, 0f, 10f, 10f), AvTheme.Dim, OpsSprites.Diamond);
                marks[i].type = Image.Type.Simple;
                marks[i].enabled = false;
            }
            for (int i = 0; i < Teams; i++)
            {
                plates[i] = Chrome.Panel(parent, new Rect(0f, 0f, 16f, 16f), AvTheme.RailInfo, AvSprites.Control);
                letters[i] = OpsText.Plot(plates[i].rectTransform, FieldWords.Callsign(i).Substring(0, 1), new Rect(0f, 0f, 16f, 16f),
                    AvTheme.TextInk, AvTextRole.Micro, TextAlignmentOptions.Center);
                Chrome.Stretch(letters[i].rectTransform);
                plates[i].gameObject.SetActive(false);
            }
            empty = OpsText.Plot(parent, "", new Rect(view.x + 8f, view.y - view.height * 0.5f + 8f, view.width - 16f, 16f), AvTheme.Dim,
                AvTextRole.Label, TextAlignmentOptions.Center);
            Image topOsd = Chrome.Panel(parent, new Rect(view.x + 1f, view.y - 1f, view.width - 2f, 16f),
                AvTheme.SurfaceInert.WithAlpha(0.88f));
            topOsd.raycastTarget = false;
            Image bottomOsd = Chrome.Panel(parent, new Rect(view.x + 1f, view.y - view.height + 17f,
                view.width - 2f, 16f), AvTheme.SurfaceInert.WithAlpha(0.88f));
            bottomOsd.raycastTarget = false;
            OpsText.Plot(parent, "// FIELD PLOT", new Rect(view.x + 8f, view.y - 1f,
                view.width - 16f, 16f), AvTheme.RailInfo, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            OpsText.Plot(parent, "TEAM · REAL SITE · SECTOR", new Rect(view.x + 8f,
                view.y - view.height + 17f, view.width - 16f, 16f), AvTheme.Dim, AvTextRole.Micro,
                TextAlignmentOptions.MidlineLeft);
        }

        public void SetHomes(float[] xs, float[] zs, int count)
        {
            homeCount = Mathf.Clamp(count, 0, Homes);
            for (int i = 0; i < homeCount; i++)
            {
                homeX[i] = xs[i];
                homeZ[i] = zs[i];
            }
        }

        public void Paint(SpecOpsDetachment detachment, double now, string emptyText)
        {
            int count = detachment != null && detachment.Enabled ? detachment.ObjectiveCount : 0;
            int points = 0;
            for (int i = 0; i < count; i++)
            {
                fitX[points] = detachment.Objective(i).X;
                fitZ[points++] = detachment.Objective(i).Z;
            }
            for (int i = 0; i < homeCount; i++)
            {
                fitX[points] = homeX[i];
                fitZ[points++] = homeZ[i];
            }
            board.Fit(fitX, fitZ, points, 16000f, points >= 8 ? 10f : 0f, 12f);
            sectors.Begin();
            for (int i = 0; i < count; i++)
            {
                FieldObjective o = detachment.Objective(i);
                sectors.Add(o.X, o.Z, detachment.ControlsSector(o.X, o.Z, now));
            }
            for (int i = 0; i < homeCount; i++)
                sectors.Add(homeX[i], homeZ[i], detachment != null && detachment.ControlsSector(homeX[i], homeZ[i], now));
            if (detachment != null && detachment.Enabled)
                for (int t = 0; t < Teams; t++)
                {
                    FieldTeam team = detachment.Team(t);
                    if (team.Deployed) sectors.Add(team.X, team.Z, detachment.ControlsSector(team.X, team.Z, now));
                }
            sectors.Paint();
            string text = count > 0 ? "" : emptyText ?? "";
            if (empty.text != text) empty.text = text;
            for (int i = 0; i < Homes; i++)
            {
                Vector2 p = i < homeCount ? board.Project(homeX[i], homeZ[i]) : default;
                bool show = count > 0 && i < homeCount && board.InView(p);
                homes[i].enabled = show;
                if (show) Lines.Centre(homes[i].rectTransform, p.x, p.y, 7f);
            }
            for (int i = 0; i < Objectives; i++)
            {
                bool show = i < count;
                marks[i].enabled = show;
                if (!show) continue;
                FieldObjective o = detachment.Objective(i);
                Vector2 p = board.Project(o.X, o.Z);
                Lines.Centre(marks[i].rectTransform, p.x, p.y, 10f);
                int team = detachment.TeamOn(o.Anchor);
                bool held = team >= 0 && detachment.Team(team).State == TeamState.Holding;
                marks[i].color = held ? FieldTones.Post(detachment.Team(team).Mission) : o.Hostile ? AvTheme.RailDanger : AvTheme.Dim;
            }
            for (int t = 0; t < Teams; t++)
            {
                FieldTeam team = detachment != null && detachment.Enabled ? detachment.Team(t) : default;
                bool deployed = count > 0 && team.Deployed;
                if (plates[t].gameObject.activeSelf != deployed) plates[t].gameObject.SetActive(deployed);
                if (!deployed) continue;
                Vector2 p = board.Project(team.X, team.Z);
                Lines.Centre(plates[t].rectTransform, p.x + 9f + t * 2f, p.y + 9f, 16f, 16f);
                plates[t].color = FieldTones.State(team.State);
            }
        }
    }
}

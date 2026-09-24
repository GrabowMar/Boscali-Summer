using System;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Features.Support.Presentation.Board;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>
    /// Clipped terrain map using the game's map imagery and the shared geographic frame. Objectives,
    /// labels, team routes and held-post coverage stay registered while panning or zooming. All
    /// overlays are pooled at build and the mission order still targets the host's stable anchor.
    /// </summary>
    internal sealed class DeskMap
    {
        private const int Objectives = SpecOpsDetachment.ObjectiveSlots;
        private const int Teams = SpecOpsDetachment.TeamCount;
        public const int Homes = 8;
        private const int Labels = Objectives + Homes;
        private const int Strokes = 360;
        private const int GridLines = 40;
        private const float MinimumSpan = 18000f;
        private const float FitPadding = 36f;
        private const float StrokeStep = 9f;
        private static readonly float[] GridSteps = { 1000f, 2000f, 5000f, 10000f, 20000f, 50000f };

        private sealed class Token
        {
            public RoomControl Control;
            public Image Disc, Glyph, Threat, Badge, Ring, Pin;
            public int Anchor;
        }

        private sealed class Tag
        {
            public Image Paper, Leader;
            public RoomControl Control;
            public TMP_Text Name, Sub;
            public string ShownName, ShownSub;
            public float Width;
        }

        private sealed class Team
        {
            public Image Route, Token, Pulse, Reach;
            public TMP_Text Letter;
        }

        public BoardSurface Board { get; private set; }

        private RectTransform surface;
        private CanvasGroup surfaceGroup;
        private BoardTerrain terrain;
        private TMP_Text terrainStatus;
        private RectTransform layer;
        private readonly Image[] gridX = new Image[GridLines];
        private readonly Image[] gridZ = new Image[GridLines];
        private Image scaleBar;
        private TMP_Text scaleText;
        private Image scalePaper;
        private readonly Image[] front = new Image[Strokes];
        private readonly Token[] tokens = new Token[Objectives];
        private readonly Tag[] tags = new Tag[Labels];
        private readonly Image[] homes = new Image[Homes];
        private readonly Team[] teams = new Team[Teams];
        private Image leader;
        private Image previewRoute;
        private Image previewPaper;
        private TMP_Text previewText;

        private readonly float[] homeX = new float[Homes];
        private readonly float[] homeZ = new float[Homes];
        private readonly string[] homeName = new string[Homes];
        private int homeCount;

        private readonly float[] fitX = new float[Objectives + Homes + Teams];
        private readonly float[] fitZ = new float[Objectives + Homes + Teams];
        private readonly Vector2[] anchors = new Vector2[Labels];
        private readonly Vector2[] sizes = new Vector2[Labels];
        private readonly int[] priorities = new int[Labels];
        private readonly float[] radii = new float[Labels];
        private readonly PlacedLabel[] placed = new PlacedLabel[Labels];
        private readonly int[] labelSlot = new int[Labels];
        private readonly Rect[] obstacleRects = new Rect[8];
        private int obstacleCount;
        private int frontRevision = -1;
        private int shownRevision = -1;
        private Action<int> select;

        /// <summary>Placed label rects (AvKit), for the harness overlap check.</summary>
        internal Rect[] PlacedRects { get; } = new Rect[Labels];
        internal int PlacedCount { get; private set; }

        public void Build(RectTransform room, Rect view, Rect focus, Action<int> onSelect)
        {
            select = onSelect;
            OpsSprites.Ensure();

            var surfaceObject = new GameObject("Table", typeof(RectTransform), typeof(CanvasGroup));
            surface = (RectTransform)surfaceObject.transform;
            surface.SetParent(room, false);
            AvKit.Place(surface, view);
            surface.pivot = new Vector2(0.5f, 0.5f);
            surface.anchoredPosition = new Vector2(view.x + view.width * 0.5f, view.y - view.height * 0.5f);
            surfaceGroup = surfaceObject.GetComponent<CanvasGroup>();
            var local = new Rect(0f, 0f, view.width, view.height);
            AvKit.Panel(surface, local, DeskStyle.Map);

            Board = new BoardSurface(room, view, focus, true, true);
            var layerObject = new GameObject("Pieces", typeof(RectTransform));
            layer = (RectTransform)layerObject.transform;
            layer.SetParent(Board.InputLayer, false);
            // Geographic overlays use room coordinates inside the clipped viewport.
            AvKit.Place(layer, new Rect(-view.x, -view.y, view.x + view.width, view.height - view.y));
            terrain = new BoardTerrain(layer, Board);
            for (int i = 0; i < GridLines; i++)
            {
                gridX[i] = AvKit.Rule(layer, new Rect(view.x, view.y, 1f, view.height), DeskStyle.MapInk.WithAlpha(0.13f));
                gridZ[i] = AvKit.Rule(layer, new Rect(view.x, view.y, view.width, 1f), DeskStyle.MapInk.WithAlpha(0.13f));
                gridX[i].enabled = gridZ[i].enabled = false;
            }
            for (int i = 0; i < Strokes; i++)
            {
                front[i] = Lines.Make(layer, DeskStyle.MapInk.WithAlpha(0.9f), null, "Front");
                front[i].enabled = false;
            }
            for (int i = 0; i < Homes; i++)
            {
                homes[i] = AvKit.Panel(layer, new Rect(0f, 0f, 12f, 12f), AvTheme.RailInfo);
                homes[i].gameObject.SetActive(false);
            }
            for (int t = 0; t < Teams; t++) teams[t] = BuildTeamUnderlay();
            for (int i = 0; i < Objectives; i++) tokens[i] = BuildToken(i);
            for (int t = 0; t < Teams; t++) BuildTeamToken(teams[t], t);
            leader = Lines.Make(layer, DeskStyle.Ink.WithAlpha(0.85f), null, "FolderLeader");
            leader.enabled = false;
            previewRoute = Lines.Make(layer, DeskStyle.Ink, OpsSprites.Dash, "Preview");
            previewRoute.enabled = false;
            for (int i = 0; i < Labels; i++) tags[i] = BuildTag(i);
            previewPaper = AvKit.Panel(layer, new Rect(0f, 0f, 10f, 22f), DeskStyle.Paper);
            previewText = DeskStyle.Body(previewPaper.rectTransform, new Rect(6f, 0f, 10f, 22f), DeskStyle.TypewriterSmall,
                DeskStyle.Ink, TextAlignmentOptions.Center);
            AvKit.Stretch(previewText.rectTransform);
            previewPaper.gameObject.SetActive(false);

            scalePaper = AvKit.Panel(room, new Rect(focus.x + 8f, focus.y - focus.height + 34f, 150f, 26f), DeskStyle.Paper);
            scaleBar = AvKit.Rule(scalePaper.rectTransform, new Rect(10f, -17f, 60f, 3f), DeskStyle.Ink);
            scaleText = DeskStyle.Body(scalePaper.rectTransform, new Rect(76f, 0f, 70f, 26f), DeskStyle.TypewriterSmall, DeskStyle.Ink);
            terrainStatus = DeskStyle.Body(room, new Rect(focus.x + 170f, focus.y - focus.height + 34f, focus.width - 394f, 26f), 10f, DeskStyle.Ink);
            MapButton(room, new Rect(focus.x + focus.width - 204f, focus.y - focus.height + 36f, 84f, 30f), "FIT / F", () => Board.ResetFraming());
            MapButton(room, new Rect(focus.x + focus.width - 112f, focus.y - focus.height + 36f, 48f, 30f), "−", () => Zoom(1f / 1.4f));
            MapButton(room, new Rect(focus.x + focus.width - 56f, focus.y - focus.height + 36f, 48f, 30f), "+", () => Zoom(1.4f));
        }

        private void Zoom(float factor) => Board.ZoomAt(new Vector2(Board.View.x + Board.View.width * 0.5f,
            Board.View.y - Board.View.height * 0.5f), factor);

        private static void MapButton(RectTransform parent, Rect area, string text, Action click)
        {
            RoomControl control = RoomControl.Create(parent, area, click, "MapControl");
            Image fill = AvKit.Panel(control.Rect, new Rect(0f, 0f, area.width, area.height), DeskStyle.Paper);
            AvKit.Outline(control.Rect, new Rect(0f, 0f, area.width, area.height), DeskStyle.Khaki);
            DeskStyle.Title(control.Rect, text, new Rect(0f, 0f, area.width, area.height), 12f, DeskStyle.Ink, TextAlignmentOptions.Center);
            control.Changed = c => fill.color = c.Hovered ? DeskStyle.Tape : DeskStyle.Paper;
        }

        private Token BuildToken(int slot)
        {
            var token = new Token();
            token.Threat = AvKit.Panel(layer, new Rect(0f, 0f, 10f, 10f), DeskStyle.Stamp, OpsSprites.Ring);
            token.Threat.type = Image.Type.Simple;
            token.Ring = AvKit.Panel(layer, new Rect(0f, 0f, 10f, 10f), DeskStyle.Ink, OpsSprites.Ring);
            token.Ring.type = Image.Type.Simple;
            int captured = slot;
            token.Control = RoomControl.Create(layer, new Rect(0f, 0f, DeskStyle.TokenSize, DeskStyle.TokenSize),
                () => select?.Invoke(captured), "Objective");
            RectTransform host = token.Control.Rect;
            token.Disc = AvKit.Panel(host, new Rect(0f, 0f, DeskStyle.TokenSize, DeskStyle.TokenSize), DeskStyle.Paper, OpsSprites.Dot);
            token.Disc.type = Image.Type.Simple;
            AvKit.Stretch(token.Disc.rectTransform);
            token.Glyph = AvKit.Panel(host, new Rect(4f, -4f, DeskStyle.TokenSize - 8f, DeskStyle.TokenSize - 8f), DeskStyle.Stamp);
            token.Badge = AvKit.Panel(host, new Rect(DeskStyle.TokenSize - 10f, 6f, 14f, 14f), AvTheme.RailReady, OpsSprites.Dot);
            token.Badge.type = Image.Type.Simple;
            Image tick = AvKit.Panel(token.Badge.rectTransform, new Rect(1f, -1f, 12f, 12f), DeskStyle.Paper);
            tick.sprite = OpsSprites.Glyph(OpsSprites.G.Check);
            token.Pin = AvKit.Panel(host, new Rect(DeskStyle.TokenSize * 0.5f - 7f, 20f, 14f, 14f), DeskStyle.Stamp);
            token.Pin.sprite = OpsSprites.Glyph(OpsSprites.G.Pin);
            token.Control.Changed = c => token.Disc.color = c.Hovered ? DeskStyle.Tape : DeskStyle.Paper;
            SetVisible(token, false);
            return token;
        }

        private Team BuildTeamUnderlay()
        {
            var team = new Team
            {
                Reach = AvKit.Panel(layer, new Rect(0f, 0f, 10f, 10f), AvTheme.RailReady, OpsSprites.DottedRing),
                Route = Lines.Make(layer, AvTheme.RailInfo, OpsSprites.Dash, "Route")
            };
            team.Reach.type = Image.Type.Simple;
            team.Reach.gameObject.SetActive(false);
            team.Route.enabled = false;
            return team;
        }

        private void BuildTeamToken(Team team, int index)
        {
            team.Pulse = AvKit.Panel(layer, new Rect(0f, 0f, 10f, 10f), AvTheme.RailCaution, OpsSprites.Ring);
            team.Pulse.type = Image.Type.Simple;
            team.Token = AvKit.Panel(layer, new Rect(0f, 0f, 22f, 22f), DeskStyle.Ink, OpsSprites.Dot);
            team.Token.type = Image.Type.Simple;
            team.Letter = AvKit.Label(team.Token.rectTransform, FieldWords.Callsign(index).Substring(0, 1),
                new Rect(0f, 0f, 22f, 22f), DeskStyle.Paper, 12f, FontStyles.Bold, TextAlignmentOptions.Center);
            AvKit.Stretch(team.Letter.rectTransform);
            team.Pulse.gameObject.SetActive(false);
            team.Token.gameObject.SetActive(false);
        }

        private Tag BuildTag(int index)
        {
            var tag = new Tag
            {
                Leader = Lines.Make(layer, DeskStyle.Ink.WithAlpha(0.8f), null, "Leader"),
                Control = RoomControl.Create(layer, new Rect(0f, 0f, 100f, 30f),
                    () => { if (labelSlot[index] >= 0) select?.Invoke(labelSlot[index]); }, "ObjectiveLabel")
            };
            tag.Paper = AvKit.Panel(tag.Control.Rect, new Rect(0f, 0f, 100f, 30f), DeskStyle.Paper);
            AvKit.Stretch(tag.Paper.rectTransform);
            tag.Leader.enabled = false;
            tag.Name = AvKit.Label(tag.Paper.rectTransform, "", new Rect(6f, -2f, 90f, 15f), DeskStyle.Ink, 11f,
                FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            tag.Name.characterSpacing = 2f;
            tag.Sub = DeskStyle.Body(tag.Paper.rectTransform, new Rect(6f, -15f, 90f, 13f), 10f, DeskStyle.Khaki);
            tag.Control.gameObject.SetActive(false);
            return tag;
        }

        // ---- Data -------------------------------------------------------------------------------

        public void SetHomes(float[] xs, float[] zs, string[] names, int count)
        {
            homeCount = Math.Max(0, Math.Min(count, Homes));
            for (int i = 0; i < homeCount; i++)
            {
                homeX[i] = xs[i];
                homeZ[i] = zs[i];
                homeName[i] = names != null && i < names.Length ? names[i] : null;
            }
        }

        /// <summary>Panels lying over the map (AvKit rects); labels keep off them. At most eight.</summary>
        public void SetObstacles(params Rect[] rects)
        {
            obstacleCount = Math.Min(rects.Length, obstacleRects.Length);
            for (int i = 0; i < obstacleCount; i++) obstacleRects[i] = rects[i];
            Board.SetObstacles(obstacleRects, obstacleCount);
        }

        /// <summary>The nearest listed home to a point, or false when none is known.</summary>
        public bool NearestHome(float x, float z, out float hx, out float hz)
        {
            hx = hz = 0f;
            float best = float.MaxValue;
            for (int i = 0; i < homeCount; i++)
            {
                float d = (homeX[i] - x) * (homeX[i] - x) + (homeZ[i] - z) * (homeZ[i] - z);
                if (d >= best) continue;
                best = d;
                hx = homeX[i];
                hz = homeZ[i];
            }
            return best < float.MaxValue;
        }

        // ---- Layout (on data or frame change) -----------------------------------------------------

        /// <summary>Frame the cluster (unless the player took over) and reposition every piece.</summary>
        public void Layout(SpecOpsDetachment detachment, double now, int selected, Vector2 folderEdge, bool folderOpen)
        {
            int count = detachment != null && detachment.Enabled ? detachment.ObjectiveCount : 0;
            int points = 0;
            for (int i = 0; i < count; i++)
            {
                FieldObjective o = detachment.Objective(i);
                fitX[points] = o.X;
                fitZ[points++] = o.Z;
            }
            // Frame every objective and deployed team. Rear-area bases remain on the map, but
            // must not shrink the operational area to a tiny cluster. With no operations yet,
            // home bases provide orientation instead.
            if (detachment != null && detachment.Enabled)
                for (int i = 0; i < Teams; i++)
                {
                    FieldTeam team = detachment.Team(i);
                    if (!team.Deployed) continue;
                    fitX[points] = team.X;
                    fitZ[points++] = team.Z;
                }
            if (points == 0)
            for (int i = 0; i < homeCount; i++)
            {
                fitX[points] = homeX[i];
                fitZ[points++] = homeZ[i];
            }
            Board.Fit(fitX, fitZ, points, MinimumSpan, 0f, FitPadding);
            terrain.Refresh();
            DeskStyle.Type(terrainStatus, terrain.Available ? "TERRAIN / LIVE THEATER · DRAG TO PAN" : "TERRAIN UNAVAILABLE · OBJECTIVES / GRID ONLY");
            PaintGrid();
            if (frontRevision != Board.Revision) PaintFront();

            for (int i = 0; i < Homes; i++)
            {
                bool show = i < homeCount;
                if (homes[i].gameObject.activeSelf != show) homes[i].gameObject.SetActive(show);
                if (!show) continue;
                Vector2 p = Board.Project(homeX[i], homeZ[i]);
                Lines.Centre(homes[i].rectTransform, p.x, p.y, 12f);
                homes[i].rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
            }

            Board.ClearMarkers();
            for (int i = 0; i < Objectives; i++)
            {
                Token token = tokens[i];
                bool show = i < count;
                SetVisible(token, show);
                if (!show) continue;
                FieldObjective o = detachment.Objective(i);
                token.Anchor = o.Anchor;
                Vector2 p = Board.Project(o.X, o.Z);
                Lines.Centre(token.Control.Rect, p.x, p.y, DeskStyle.TokenSize);
                Board.AddMarker(i, p, DeskStyle.TokenSize * 0.7f);
                int holder = detachment.TeamOn(o.Anchor);
                bool held = holder >= 0 && detachment.Team(holder).State == TeamState.Holding;
                token.Glyph.sprite = OpsSprites.Glyph(Glyph(o.Kind));
                token.Glyph.color = held ? FieldTones.Post(detachment.Team(holder).Mission)
                    : o.Hostile ? DeskStyle.Stamp : DeskStyle.Khaki;
                float threatDiameter = Mathf.Max(DeskStyle.TokenSize + 12f, Board.Pixels(FieldCatalog.ThreatRadius) * 2f);
                token.Threat.gameObject.SetActive(o.Hostile && o.Threat > 0);
                Lines.Centre(token.Threat.rectTransform, p.x, p.y, threatDiameter);
                token.Threat.color = DeskStyle.Stamp.WithAlpha(Mathf.Clamp01(0.35f + o.Threat * 0.06f));
                token.Badge.gameObject.SetActive(detachment.Scouted(o.Anchor, now));
                bool isSelected = i == selected;
                token.Ring.gameObject.SetActive(isSelected);
                token.Pin.gameObject.SetActive(isSelected);
                Lines.Centre(token.Ring.rectTransform, p.x, p.y, DeskStyle.TokenSize * 1.9f);
                token.Control.WithTooltip(o.Name + " — " + FieldWords.Kind(o.Kind) + ", " +
                                          (o.Hostile ? "hostile" : "neutral") + ", " + o.Threat + " ground unit(s) within 2 km, " +
                                          o.Radars + " radar(s). Click to brief it.");
            }

            PlaceTags(detachment, now, count, selected);
            PaintTeams(detachment, now, false);
            leader.enabled = false;
            shownRevision = Board.Revision;
        }

        /// <summary>True when the player zoomed or panned since the last layout.</summary>
        public bool FrameChanged => shownRevision != Board.Revision;

        private void PlaceTags(SpecOpsDetachment detachment, double now, int count, int selected)
        {
            int n = 0;
            for (int i = 0; i < count && n < Labels; i++)
            {
                FieldObjective o = detachment.Objective(i);
                int team = detachment.TeamOn(o.Anchor);
                string sub = team >= 0
                    ? FieldWords.Callsign(team) + " " + FieldWords.State(detachment.Team(team).State)
                    : FieldWords.KindCode(o.Kind) + " · " + FieldWords.Threat(o.Threat) +
                      (detachment.Scouted(o.Anchor, now) ? " · SCOUTED" : "");
                Write(tags[n], PlaceNames.Shorten(o.Name, 16), sub);
                anchors[n] = Board.Project(o.X, o.Z);
                sizes[n] = new Vector2(tags[n].Width, 30f);
                priorities[n] = i == selected ? 1000 : (team >= 0 ? 400 : 100) + o.Threat * 4 + (o.Hostile ? 50 : 0);
                radii[n] = i == selected ? DeskStyle.TokenSize : DeskStyle.TokenSize * 0.5f + 3f;
                labelSlot[n] = i;
                n++;
            }
            for (int i = 0; i < homeCount && n < Labels; i++)
            {
                Write(tags[n], PlaceNames.Shorten(string.IsNullOrEmpty(homeName[i]) ? "HOME BASE" : homeName[i], 14), "HOME BASE");
                anchors[n] = Board.Project(homeX[i], homeZ[i]);
                sizes[n] = new Vector2(tags[n].Width, 30f);
                priorities[n] = 20;
                radii[n] = 9f;
                labelSlot[n] = -1 - i;
                n++;
            }
            Board.PlaceLabels(anchors, sizes, priorities, radii, n, placed);
            PlacedCount = 0;
            for (int i = 0; i < Labels; i++)
            {
                Tag tag = tags[i];
                bool show = i < n && placed[i].Visible && Board.InView(anchors[i]);
                if (tag.Control.gameObject.activeSelf != show) tag.Control.gameObject.SetActive(show);
                if (!show)
                {
                    tag.Leader.enabled = false;
                    continue;
                }
                PlacedLabel p = placed[i];
                var rect = new Rect(p.X, p.Y, p.Width, p.Height);
                AvKit.Place(tag.Control.Rect, rect);
                tag.Control.SetEnabled(labelSlot[i] >= 0);
                tag.Control.WithTooltip(labelSlot[i] >= 0 ? "Select objective / " + tag.ShownName : tag.ShownName);
                PlacedRects[PlacedCount++] = rect;
                if (p.Leader)
                {
                    float cx = Mathf.Clamp(anchors[i].x, rect.x, rect.x + rect.width);
                    float cy = Mathf.Clamp(anchors[i].y, rect.y - rect.height, rect.y);
                    Lines.Set(tag.Leader, anchors[i].x, anchors[i].y, cx, cy, 1f);
                }
                else tag.Leader.enabled = false;
                bool hot = labelSlot[i] == selected;
                tag.Paper.color = hot ? DeskStyle.Tape : DeskStyle.Paper;
            }
        }

        private static void Write(Tag tag, string name, string sub)
        {
            if (tag.ShownName == name && tag.ShownSub == sub) return;
            tag.ShownName = name;
            tag.ShownSub = sub;
            tag.Name.text = name;
            DeskStyle.Type(tag.Sub, sub);
            float nameWidth = tag.Name.GetPreferredValues(name).x;
            float subWidth = tag.Sub.GetPreferredValues(tag.Sub.text).x;
            tag.Width = Mathf.Ceil(Mathf.Max(nameWidth, subWidth)) + 14f;
            tag.Name.rectTransform.sizeDelta = new Vector2(tag.Width - 10f, 15f);
            tag.Sub.rectTransform.sizeDelta = new Vector2(tag.Width - 10f, 13f);
        }

        private void PaintGrid()
        {
            float mpp = Board.MetresPerPixel;
            float step = GridSteps[GridSteps.Length - 1];
            for (int i = 0; i < GridSteps.Length; i++)
            {
                if (GridSteps[i] / mpp < 96f) continue;
                step = GridSteps[i];
                break;
            }
            Rect view = Board.View;
            Board.Unproject(new Vector2(view.x, view.y), out float left, out float top);
            float first = Mathf.Ceil(left / step) * step;
            for (int i = 0; i < GridLines; i++)
            {
                Vector2 p = Board.Project(first + i * step, 0f);
                bool show = p.x < view.x + view.width;
                gridX[i].enabled = show;
                if (show) AvKit.Place(gridX[i].rectTransform, new Rect(p.x, view.y, 1f, view.height));
            }
            float firstZ = Mathf.Floor(top / step) * step;
            for (int i = 0; i < GridLines; i++)
            {
                Vector2 p = Board.Project(0f, firstZ - i * step);
                bool show = p.y > view.y - view.height;
                gridZ[i].enabled = show;
                if (show) AvKit.Place(gridZ[i].rectTransform, new Rect(view.x, p.y, view.width, 1f));
            }
            float length = step / mpp;
            scaleBar.rectTransform.sizeDelta = new Vector2(Mathf.Min(length, 60f), 3f);
            DeskStyle.Type(scaleText, (length > 60f ? "GRID " : "") + Mathf.RoundToInt(step / 1000f) + " KM");
        }

        private void PaintFront()
        {
            frontRevision = Board.Revision;
            int used = 0;
            int offset = 0;
            for (int t = 0; t < Board.TraceCount && used < Strokes; t++)
            {
                int length = Board.TraceLengths[t];
                Vector2 last = default;
                bool have = false;
                for (int k = 0; k < length && used < Strokes; k++)
                {
                    FrontlineTracePointView(Board, offset + k, out Vector2 p);
                    if (!have) { last = p; have = true; continue; }
                    bool final = k == length - 1;
                    if ((p - last).sqrMagnitude < StrokeStep * StrokeStep && !final) continue;
                    if (Board.InView(p, 40f) || Board.InView(last, 40f))
                        Lines.Set(front[used++], last.x, last.y, p.x, p.y, 3f);
                    last = p;
                }
                offset += length;
            }
            for (int i = used; i < Strokes; i++) front[i].enabled = false;
        }

        private static void FrontlineTracePointView(BoardSurface board, int index, out Vector2 p) =>
            p = board.Project(board.TracePoints[index].X, board.TracePoints[index].Z);

        /// <summary>Force the front to redraw after new traces arrived.</summary>
        public void FrontChanged() => frontRevision = -1;

        // ---- Motion (every frame) ---------------------------------------------------------------

        /// <summary>Team tokens travel along their routes; the on-task ring pulses.</summary>
        public void Animate(SpecOpsDetachment detachment, double now, float time) => PaintTeams(detachment, now, true, time);

        private void PaintTeams(SpecOpsDetachment detachment, double now, bool motionOnly, float time = 0f)
        {
            for (int t = 0; t < Teams; t++)
            {
                Team team = teams[t];
                FieldTeam value = detachment != null && detachment.Enabled ? detachment.Team(t) : default;
                bool deployed = value.Deployed;
                if (team.Token.gameObject.activeSelf != deployed) team.Token.gameObject.SetActive(deployed);
                bool hasHome = NearestHome(value.X, value.Z, out float hx, out float hz);
                Vector2 target = Board.Project(value.X, value.Z);
                Vector2 home = hasHome ? Board.Project(hx, hz) : target;
                if (!deployed)
                {
                    team.Route.enabled = false;
                    if (team.Pulse.gameObject.activeSelf) team.Pulse.gameObject.SetActive(false);
                    if (team.Reach.gameObject.activeSelf) team.Reach.gameObject.SetActive(false);
                    continue;
                }
                float progress = value.State == TeamState.EnRoute ? detachment.Progress(t, now) : 1f;
                Vector2 at = Vector2.Lerp(home, target, progress) + new Vector2(10f + t * 3f, 10f);
                Lines.Centre(team.Token.rectTransform, at.x, at.y, 22f);
                team.Token.color = value.State == TeamState.Holding ? FieldTones.Post(value.Mission) : DeskStyle.Ink;
                bool pulse = value.State == TeamState.OnTask;
                if (team.Pulse.gameObject.activeSelf != pulse) team.Pulse.gameObject.SetActive(pulse);
                if (pulse)
                {
                    float phase = Mathf.Repeat(time * 0.8f, 1f);
                    Lines.Centre(team.Pulse.rectTransform, target.x, target.y, DeskStyle.TokenSize + 40f * phase);
                    team.Pulse.color = AvTheme.RailCaution.WithAlpha(1f - phase);
                }
                if (motionOnly) continue;
                bool route = value.State != TeamState.Holding && hasHome;
                if (route) Lines.Set(team.Route, home.x, home.y, target.x, target.y, 3f);
                else team.Route.enabled = false;
                bool reach = value.State == TeamState.Holding;
                if (team.Reach.gameObject.activeSelf != reach) team.Reach.gameObject.SetActive(reach);
                if (reach)
                {
                    Lines.Centre(team.Reach.rectTransform, target.x, target.y, Board.Pixels(FieldCatalog.PostReach(value.Mission)) * 2f);
                    team.Reach.color = FieldTones.Post(value.Mission).WithAlpha(0.8f);
                }
            }
        }

        // ---- Hover preview ----------------------------------------------------------------------

        /// <summary>A mission sheet under the pointer: its route and timing drawn on the table.</summary>
        public void Preview(bool show, float x, float z, string timing)
        {
            if (!show || !NearestHome(x, z, out float hx, out float hz))
            {
                previewRoute.enabled = false;
                if (previewPaper.gameObject.activeSelf) previewPaper.gameObject.SetActive(false);
                return;
            }
            Vector2 a = Board.Project(hx, hz);
            Vector2 b = Board.Project(x, z);
            Lines.Set(previewRoute, a.x, a.y, b.x, b.y, 3f);
            DeskStyle.Type(previewText, timing);
            float width = previewText.GetPreferredValues(previewText.text).x + 16f;
            Vector2 mid = (a + b) * 0.5f;
            AvKit.Place(previewPaper.rectTransform, new Rect(mid.x - width * 0.5f, mid.y + 30f, width, 22f));
            if (!previewPaper.gameObject.activeSelf) previewPaper.gameObject.SetActive(true);
        }

        // ---- Entrance ---------------------------------------------------------------------------

        /// <summary>The map background settles; input and geographic overlays retain their final geometry.</summary>
        public void Settle(float p)
        {
            float eased = Domain.Layout.Motion.EaseOutCubic(p);
            float scale = Mathf.Lerp(1.03f, 1f, eased);
            surface.localScale = new Vector3(scale, scale, 1f);
            surfaceGroup.alpha = Mathf.Lerp(0.4f, 1f, eased);
        }

        private static void SetVisible(Token token, bool show)
        {
            if (token.Control.gameObject.activeSelf != show) token.Control.gameObject.SetActive(show);
            if (token.Threat.gameObject.activeSelf && !show) token.Threat.gameObject.SetActive(false);
            if (token.Ring.gameObject.activeSelf && !show) token.Ring.gameObject.SetActive(false);
        }

        public static int Glyph(ObjectiveKind kind)
        {
            switch (kind)
            {
                case ObjectiveKind.Airfield: return OpsSprites.G.ObjAirfield;
                case ObjectiveKind.Outpost: return OpsSprites.G.ObjOutpost;
                case ObjectiveKind.Town: return OpsSprites.G.ObjTown;
                case ObjectiveKind.AirDefence: return OpsSprites.G.ObjAirDefence;
                default: return OpsSprites.G.Pin;
            }
        }
    }
}

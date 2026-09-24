using System;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Board
{
    /// <summary>
    /// The shared geographic engine under the CYBER netmap and the SPEC OPS briefing table:
    /// fit (percentile-trimmed, so a far home base cannot shrink the cluster), wheel zoom about the
    /// cursor, left-drag pan, F to fit again, world ↔ room projection, marker hit tests, collision-free
    /// label placement and the frontline traces. It creates exactly one invisible input rect and never
    /// a visible graphic: each room renders its own map on top of it in its own language.
    ///
    /// <para>Coordinates handed to rooms are <see cref="AvKit"/> coordinates in the parent (Y negative
    /// downward). The pure maths underneath (<see cref="BoardFit"/>, <see cref="LabelPlacer"/>) is
    /// Y-down; the conversion happens only here.</para>
    /// </summary>
    internal sealed class BoardSurface
    {
        public const int MarkerLimit = 48;
        private const float ZoomStep = 1.18f;
        private const float MinimumMetresPerPixel = 8f;
        private const float FrontlineInterval = 1f;
        private const float DefaultMapHalf = 40960f;

        private readonly Rect view;
        private readonly Rect focus;
        private readonly RectTransform input;
        private readonly Vector2[] markerAt = new Vector2[MarkerLimit];
        private readonly float[] markerRadius = new float[MarkerLimit];
        private readonly int[] markerId = new int[MarkerLimit];
        private readonly LabelRequest[] requests = new LabelRequest[LabelPlacer.Maximum];
        private readonly Box[] obstacles = new Box[8];
        private int markerCount;
        private int obstacleCount;
        private BoardFrame frame;
        private float mapHalf = DefaultMapHalf;
        private float nextFrontline;

        /// <summary>Frontline polylines from Command's control field (global X/Z), filled at most once a second.</summary>
        public readonly FrontlineTracePoint[] TracePoints = new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
        public readonly int[] TraceLengths = new int[FrontlineTraceLimits.MaximumTraces];
        private readonly float[] tracePressure = new float[FrontlineTraceLimits.MaximumTraces];

        /// <summary>Number of frontline traces in <see cref="TracePoints"/>; 0 when the contract is absent.</summary>
        public int TraceCount { get; private set; }

        /// <summary>Bumped whenever the frame changes, so a room repositions its markers only then.</summary>
        public int Revision { get; private set; }

        /// <summary>True once the player zoomed or panned; automatic fitting stops until F.</summary>
        public bool UserFramed { get; private set; }

        public BoardFrame Frame => frame;
        public Rect View => view;
        public Rect Focus => focus;
        public float MetresPerPixel => frame.MetresPerPixel > 0f ? frame.MetresPerPixel : 1f;
        public float MapHalf => mapHalf;

        /// <summary>A click on the board that hit no marker: local position and button.</summary>
        public Action<Vector2, PointerEventData.InputButton> Clicked;

        /// <summary>
        /// <paramref name="view"/> is the whole drawable area; <paramref name="focus"/> the part of it
        /// no panel covers, which fitting frames. Both in the parent's AvKit coordinates.
        /// </summary>
        public BoardSurface(RectTransform parent, Rect view, Rect focus, bool interactive, bool clip = false)
        {
            this.view = view;
            this.focus = focus;
            frame.MetresPerPixel = 100f;
            if (!interactive) return;
            var go = new GameObject("BoardInput", typeof(RectTransform), typeof(Image));
            input = (RectTransform)go.transform;
            input.SetParent(parent, false);
            AvKit.Place(input, view);
            Image hit = go.GetComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            hit.canvasRenderer.cullTransparentMesh = true;
            go.AddComponent<BoardInput>().Board = this;
            // A board smaller than its room clips its pieces (and their hit areas) to the view.
            if (clip) go.AddComponent<RectMask2D>();
        }

        /// <summary>The input rect; rooms parent clickable markers here so wheel and drag still reach the board.</summary>
        public RectTransform InputLayer => input;

        public void SetMapHalf(float half)
        {
            if (half > 1000f && !float.IsNaN(half)) mapHalf = half;
        }

        // ---- Framing ----------------------------------------------------------------------------

        /// <summary>Frame the points in the focus rect unless the player has taken over the view.</summary>
        public void Fit(float[] xs, float[] zs, int count, float minSpan, float trimPercent, float padding)
        {
            if (UserFramed) return;
            BoardFrame next = BoardFit.Fit(xs, zs, count, focus.width, focus.height, padding, minSpan, trimPercent);
            if (count <= 0) next = new BoardFrame { CentreX = 0f, CentreZ = 0f, MetresPerPixel = mapHalf * 2f / Mathf.Max(1f, focus.height) };
            next.MetresPerPixel = Mathf.Clamp(next.MetresPerPixel, MinimumMetresPerPixel, MaximumMetresPerPixel);
            // The fitted centre belongs to the focus rect; shift it to the view's centre.
            float dx = (focus.x + focus.width * 0.5f) - (view.x + view.width * 0.5f);
            float dy = (focus.y - focus.height * 0.5f) - (view.y - view.height * 0.5f);
            next.CentreX -= dx * next.MetresPerPixel;
            next.CentreZ -= dy * next.MetresPerPixel;
            SetFrame(next);
        }

        /// <summary>F: forget the player's framing; the next <see cref="Fit"/> frames the cluster again.</summary>
        public void ResetFraming()
        {
            if (!UserFramed) return;
            UserFramed = false;
            Revision++;
        }

        public void ZoomAt(Vector2 local, float factor)
        {
            ToView(local, out float sx, out float sy);
            UserFramed = true;
            SetFrame(BoardFit.Zoom(frame, factor, sx, sy, view.width, view.height, MinimumMetresPerPixel, MaximumMetresPerPixel));
        }

        public void PanBy(Vector2 delta)
        {
            UserFramed = true;
            SetFrame(BoardFit.Pan(frame, delta.x, -delta.y, view.width, view.height, mapHalf));
        }

        private float MaximumMetresPerPixel => mapHalf * 2.4f / Mathf.Max(1f, Mathf.Min(view.width, view.height));

        private void SetFrame(BoardFrame next)
        {
            if (Mathf.Abs(next.CentreX - frame.CentreX) < 0.5f && Mathf.Abs(next.CentreZ - frame.CentreZ) < 0.5f &&
                Mathf.Abs(next.MetresPerPixel - frame.MetresPerPixel) < 0.0005f * frame.MetresPerPixel) return;
            frame = next;
            Revision++;
        }

        // ---- Projection -------------------------------------------------------------------------

        /// <summary>World X/Z to the parent's AvKit coordinates.</summary>
        public Vector2 Project(float worldX, float worldZ)
        {
            BoardFit.Project(frame, worldX, worldZ, view.width, view.height, out float sx, out float sy);
            return new Vector2(view.x + sx, view.y - sy);
        }

        public void Unproject(Vector2 local, out float worldX, out float worldZ)
        {
            ToView(local, out float sx, out float sy);
            BoardFit.Unproject(frame, sx, sy, view.width, view.height, out worldX, out worldZ);
        }

        /// <summary>Metres to pixels at the current zoom.</summary>
        public float Pixels(float metres) => metres / MetresPerPixel;

        public bool InView(Vector2 local, float margin = 0f) =>
            local.x >= view.x - margin && local.x <= view.x + view.width + margin &&
            local.y <= view.y + margin && local.y >= view.y - view.height - margin;

        private void ToView(Vector2 local, out float sx, out float sy)
        {
            sx = local.x - view.x;
            sy = view.y - local.y;
        }

        // ---- Hit tests --------------------------------------------------------------------------

        public void ClearMarkers() => markerCount = 0;

        public void AddMarker(int id, Vector2 local, float radius)
        {
            if (markerCount >= MarkerLimit) return;
            markerId[markerCount] = id;
            markerAt[markerCount] = local;
            markerRadius[markerCount] = radius;
            markerCount++;
        }

        /// <summary>The id of the nearest registered marker within its radius of <paramref name="local"/>, or -1.</summary>
        public int Hit(Vector2 local)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < markerCount; i++)
            {
                float d = (markerAt[i] - local).sqrMagnitude;
                if (d > markerRadius[i] * markerRadius[i] || d >= bestDistance) continue;
                bestDistance = d;
                best = markerId[i];
            }
            return best;
        }

        // ---- Labels -----------------------------------------------------------------------------

        /// <summary>Areas labels must avoid (a panel lying over the map), in AvKit coordinates.</summary>
        public void SetObstacles(Rect[] rects, int count)
        {
            obstacleCount = 0;
            for (int i = 0; i < count && i < obstacles.Length && rects != null; i++)
            {
                Rect r = rects[i];
                obstacles[obstacleCount++] = new Box(r.x - view.x, view.y - r.y, r.width, r.height);
            }
        }

        /// <summary>
        /// Place up to <see cref="LabelPlacer.Maximum"/> labels around anchors given in AvKit
        /// coordinates. <paramref name="into"/> receives AvKit top-left positions and leader flags.
        /// Returns how many are visible.
        /// </summary>
        public int PlaceLabels(Vector2[] anchors, Vector2[] sizes, int[] priorities, float[] markerRadii, int count,
            PlacedLabel[] into)
        {
            count = Math.Min(count, LabelPlacer.Maximum);
            for (int i = 0; i < count; i++)
            {
                ToView(anchors[i], out float sx, out float sy);
                requests[i] = new LabelRequest(sx, sy, sizes[i].x, sizes[i].y, priorities[i], markerRadii[i]);
                into[i] = default;
            }
            int placed = LabelPlacer.Place(requests, count, new Box(0f, 0f, view.width, view.height), scratch,
                obstacles, obstacleCount);
            for (int k = 0; k < placed; k++)
            {
                PlacedLabel p = scratch[k];
                if (p.Index < 0 || p.Index >= count) continue;
                p.X += view.x;
                p.Y = view.y - p.Y;
                into[p.Index] = p;
            }
            return placed;
        }

        private readonly PlacedLabel[] scratch = new PlacedLabel[LabelPlacer.Maximum];

        // ---- Frontline --------------------------------------------------------------------------

        /// <summary>Re-read the frontline at most once a second. True when new traces were copied.</summary>
        public bool RefreshFrontline(int factionId, float time)
        {
            if (time < nextFrontline) return false;
            nextFrontline = time + FrontlineInterval;
            if (!ModServices.TryGet(out ITerritoryIngress territory) || territory == null)
            {
                bool had = TraceCount > 0;
                TraceCount = 0;
                return had;
            }
            TraceCount = Math.Max(0, territory.CopyFrontlineTraces(factionId, TracePoints, TraceLengths, tracePressure));
            return true;
        }

        /// <summary>Offline fixtures and tests: supply traces directly.</summary>
        internal void SetFrontline(FrontlineTracePoint[] points, int[] lengths, int traces)
        {
            int offset = 0;
            TraceCount = 0;
            for (int t = 0; t < traces && t < TraceLengths.Length; t++)
            {
                int length = lengths[t];
                if (offset + length > TracePoints.Length) break;
                Array.Copy(points, offset, TracePoints, offset, length);
                TraceLengths[t] = length;
                offset += length;
                TraceCount++;
            }
        }

        // ---- Input ------------------------------------------------------------------------------

        private sealed class BoardInput : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler,
            IEndDragHandler, IPointerClickHandler
        {
            public BoardSurface Board;

            public void OnScroll(PointerEventData eventData)
            {
                if (!Local(eventData, out Vector2 local)) return;
                float wheel = eventData.scrollDelta.y;
                if (Mathf.Abs(wheel) < 0.01f) return;
                Board.ZoomAt(local, wheel > 0f ? ZoomStep : 1f / ZoomStep);
            }

            public void OnBeginDrag(PointerEventData eventData) { }

            public void OnDrag(PointerEventData eventData)
            {
                if (eventData.button != PointerEventData.InputButton.Left) return;
                if (!Local(eventData, out Vector2 now)) return;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,
                        eventData.position - eventData.delta, eventData.pressEventCamera, out Vector2 before)) return;
                // 'now' is in room coordinates; 'before' is local to the input rect.
                // Mixing them made each drag include the entire map's layout offset.
                Board.PanBy(now - new Vector2(Board.view.x + before.x, Board.view.y + before.y));
            }

            public void OnEndDrag(PointerEventData eventData) { }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.dragging || !eventData.eligibleForClick) return;
                if (!Local(eventData, out Vector2 local)) return;
                if (eventData.button == PointerEventData.InputButton.Right && transform.parent != null)
                {
                    // The room answers right-clicks; let the event continue up to it.
                    ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, ExecuteEvents.pointerClickHandler);
                    return;
                }
                AvInput.Deselect(gameObject);
                Board.Clicked?.Invoke(local, eventData.button);
            }

            /// <summary>Pointer position in the board parent's AvKit coordinates.</summary>
            private bool Local(PointerEventData eventData, out Vector2 local)
            {
                var rect = (RectTransform)transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position,
                        eventData.pressEventCamera ?? eventData.enterEventCamera, out Vector2 inside))
                {
                    local = default;
                    return false;
                }
                // The input rect is placed with a top-left pivot at the view's corner.
                local = new Vector2(Board.view.x + inside.x, Board.view.y + inside.y);
                return true;
            }
        }
    }
}

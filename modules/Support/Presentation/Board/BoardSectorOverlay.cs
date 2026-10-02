using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Presentation.Window;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Board
{
    /// <summary>Bounded geographic underlay shared by the two OPS ground desks. Cells are
    /// requested only by verified nodes, objectives or deployed teams; drawing never grants access.</summary>
    internal sealed class BoardSectorOverlay
    {
        internal const int MaximumCells = 32;
        private sealed class Cell
        {
            public OpsSector Sector;
            public bool Controlled, Selected;
            public Image Fill;
            public readonly Image[] Edge = new Image[4];
        }

        private readonly BoardSurface board;
        private readonly Cell[] cells = new Cell[MaximumCells];
        private readonly Color quiet, control, selected;
        private int count;
        internal int CellCount => count;
        internal int ControlledCount { get; private set; }

        public BoardSectorOverlay(RectTransform layer, BoardSurface board, Color quiet, Color control, Color selected)
        {
            this.board = board;
            this.quiet = quiet;
            this.control = control;
            this.selected = selected;
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = new Cell { Fill = Chrome.Panel(layer, new Rect(0, 0, 1, 1), Color.clear) };
                cell.Fill.name = "SectorFill";
                cell.Fill.raycastTarget = false;
                for (int e = 0; e < cell.Edge.Length; e++) cell.Edge[e] = Lines.Make(layer, quiet, null, "SectorBoundary");
                cells[i] = cell;
                Hide(cell);
            }
        }

        public void Begin() { count = 0; ControlledCount = 0; }

        public void Add(float x, float z, bool controlled, bool isSelected = false)
        {
            if (!OpsSectors.TryLocate(x, z, out OpsSector sector)) return;
            for (int i = 0; i < count; i++)
            {
                Cell cell = cells[i];
                if (cell.Sector.Column != sector.Column || cell.Sector.Row != sector.Row) continue;
                cell.Controlled |= controlled;
                cell.Selected |= isSelected;
                return;
            }
            if (count >= cells.Length) return;
            Cell next = cells[count++];
            next.Sector = sector;
            next.Controlled = controlled;
            next.Selected = isSelected;
        }

        public void Paint()
        {
            Rect view = board.View;
            ControlledCount = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                Cell cell = cells[i];
                if (i >= count) { Hide(cell); continue; }
                if (cell.Controlled) ControlledCount++;
                Vector2 a = board.Project(cell.Sector.MinX, cell.Sector.MinZ + OpsSectors.Size);
                Vector2 b = board.Project(cell.Sector.MinX + OpsSectors.Size, cell.Sector.MinZ);
                float left = Mathf.Max(a.x, view.x), right = Mathf.Min(b.x, view.x + view.width);
                float top = Mathf.Min(a.y, view.y), bottom = Mathf.Max(b.y, view.y - view.height);
                if (right <= left || top <= bottom) { Hide(cell); continue; }
                Color tone = cell.Selected ? selected : cell.Controlled ? control : quiet;
                cell.Fill.enabled = true;
                cell.Fill.color = (cell.Controlled ? control : tone).WithAlpha(cell.Controlled ? .18f : cell.Selected ? .07f : .035f);
                Chrome.Place(cell.Fill.rectTransform, new Rect(left, top, right - left, top - bottom));
                float width = cell.Selected ? 2.2f : cell.Controlled ? 1.8f : 1.1f;
                Color line = tone.WithAlpha(cell.Selected ? .95f : cell.Controlled ? .88f : .52f);
                for (int e = 0; e < cell.Edge.Length; e++) cell.Edge[e].color = line;
                // Do not invent an edge at a clipped viewport boundary: only the true cell
                // boundaries are drawn, while the fill itself remains inside every map viewport.
                SetEdge(cell.Edge[0], a.x, a.y, b.x, a.y, view, width);
                SetEdge(cell.Edge[1], a.x, b.y, b.x, b.y, view, width);
                SetEdge(cell.Edge[2], a.x, a.y, a.x, b.y, view, width);
                SetEdge(cell.Edge[3], b.x, a.y, b.x, b.y, view, width);
            }
        }

        private static void SetEdge(Image edge, float x1, float y1, float x2, float y2, Rect view, float width)
        {
            if (Mathf.Max(x1, x2) < view.x || Mathf.Min(x1, x2) > view.x + view.width ||
                Mathf.Max(y1, y2) < view.y - view.height || Mathf.Min(y1, y2) > view.y)
            { edge.enabled = false; return; }
            Lines.Set(edge, Mathf.Clamp(x1, view.x, view.x + view.width), Mathf.Clamp(y1, view.y - view.height, view.y),
                Mathf.Clamp(x2, view.x, view.x + view.width), Mathf.Clamp(y2, view.y - view.height, view.y), width);
        }

        private static void Hide(Cell cell)
        {
            cell.Fill.enabled = false;
            for (int e = 0; e < cell.Edge.Length; e++) cell.Edge[e].enabled = false;
        }
    }
}

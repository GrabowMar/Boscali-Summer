using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    internal enum SqdMark
    {
        Fuel = 0,
        Combat = 1,
        Ground = 2,
        Objective = 3,
        Logistics = 4,
        Recon = 5,
        Fortify = 6,
        Strike = 7,
        Ew = 8,
    }

    internal static class SqdMarks
    {
        public static SqdMark FromKey(string key)
        {
            switch (key)
            {
                case "fuel": return SqdMark.Fuel;
                case "ground": return SqdMark.Ground;
                case "objective": return SqdMark.Objective;
                case "logistics": return SqdMark.Logistics;
                case "recon": return SqdMark.Recon;
                case "fortify": return SqdMark.Fortify;
                case "strike": return SqdMark.Strike;
                case "ew": return SqdMark.Ew;
                default: return SqdMark.Combat;
            }
        }
    }

    /// <summary>Small vector skill marks: no font-symbol dependency, texture allocation or
    /// asset lifetime, and they tint with the owning <see cref="Image"/> colour.</summary>
    internal sealed class SqdGlyph : MaskableGraphic
    {
        public SqdMark Mark;

        public static SqdGlyph Create(RectTransform parent, Rect area, SqdMark mark)
        {
            var go = new GameObject(mark.ToString(), typeof(RectTransform), typeof(SqdGlyph));
            go.transform.SetParent(parent, false);
            var glyph = go.GetComponent<SqdGlyph>();
            glyph.Mark = mark;
            glyph.raycastTarget = false;
            AvKit.Place((RectTransform)go.transform, area);
            return glyph;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = rectTransform.rect;
            if (r.width <= 0f || r.height <= 0f) return;

            switch (Mark)
            {
                case SqdMark.Fuel: DrawFuel(mesh, r); break;
                case SqdMark.Ground: DrawGround(mesh, r); break;
                case SqdMark.Objective: DrawObjective(mesh, r); break;
                case SqdMark.Logistics: DrawLogistics(mesh, r); break;
                case SqdMark.Recon: DrawRecon(mesh, r); break;
                case SqdMark.Fortify: DrawFortify(mesh, r); break;
                case SqdMark.Strike: DrawStrike(mesh, r); break;
                case SqdMark.Ew: DrawEw(mesh, r); break;
                default: DrawCombat(mesh, r); break;
            }
        }

        private void DrawFuel(VertexHelper mesh, Rect r)
        {
            Line(mesh, r, 0.5f, 0.05f, 0.15f, 0.55f);
            Line(mesh, r, 0.5f, 0.05f, 0.85f, 0.55f);
            Line(mesh, r, 0.15f, 0.55f, 0.3f, 0.85f);
            Line(mesh, r, 0.85f, 0.55f, 0.7f, 0.85f);
            Line(mesh, r, 0.3f, 0.85f, 0.7f, 0.85f);
            Line(mesh, r, 0.5f, 0.3f, 0.5f, 0.62f);
        }

        private void DrawCombat(VertexHelper mesh, Rect r)
        {
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f, b = (i + 1) * Mathf.PI / 8f;
                Line(mesh, r, 0.5f + Mathf.Cos(a) * 0.3f, 0.5f + Mathf.Sin(a) * 0.3f,
                    0.5f + Mathf.Cos(b) * 0.3f, 0.5f + Mathf.Sin(b) * 0.3f);
            }
            Line(mesh, r, 0.5f, 0.5f, 0.5f, 0.95f);
            Line(mesh, r, 0.5f, 0.5f, 0.5f, 0.05f);
            Line(mesh, r, 0.5f, 0.5f, 0.95f, 0.5f);
            Line(mesh, r, 0.5f, 0.5f, 0.05f, 0.5f);
        }

        private void DrawGround(VertexHelper mesh, Rect r)
        {
            // Open spanner over a bolt head.
            Line(mesh, r, 0.2f, 0.85f, 0.62f, 0.42f);
            Line(mesh, r, 0.32f, 0.95f, 0.72f, 0.55f);
            Line(mesh, r, 0.62f, 0.42f, 0.72f, 0.55f);
            Line(mesh, r, 0.2f, 0.85f, 0.32f, 0.95f);
            Line(mesh, r, 0.62f, 0.42f, 0.72f, 0.3f);
            Line(mesh, r, 0.3f, 0.2f, 0.3f, 0.05f);
            Line(mesh, r, 0.3f, 0.05f, 0.55f, 0.05f);
            Line(mesh, r, 0.55f, 0.05f, 0.55f, 0.2f);
        }

        private void DrawObjective(VertexHelper mesh, Rect r)
        {
            Line(mesh, r, 0.25f, 0.05f, 0.25f, 0.95f);
            Line(mesh, r, 0.3f, 0.9f, 0.9f, 0.72f);
            Line(mesh, r, 0.3f, 0.9f, 0.3f, 0.55f);
            Line(mesh, r, 0.3f, 0.55f, 0.9f, 0.72f);
        }

        private void DrawLogistics(VertexHelper mesh, Rect r)
        {
            QuadOutline(mesh, r, 0.15f, 0.2f, 0.7f, 0.6f);
            Line(mesh, r, 0.15f, 0.8f, 0.5f, 0.95f);
            Line(mesh, r, 0.85f, 0.8f, 0.5f, 0.95f);
            Line(mesh, r, 0.85f, 0.8f, 0.85f, 0.2f);
            Line(mesh, r, 0.15f, 0.8f, 0.15f, 0.2f);
            Line(mesh, r, 0.5f, 0.4f, 0.5f, 0.6f);
        }

        private void DrawRecon(VertexHelper mesh, Rect r)
        {
            QuadOutline(mesh, r, 0.35f, 0.4f, 0.3f, 0.3f);
            Line(mesh, r, 0.35f, 0.4f, 0.05f, 0.25f);
            Line(mesh, r, 0.05f, 0.7f, 0.05f, 0.25f);
            Line(mesh, r, 0.65f, 0.4f, 0.95f, 0.25f);
            Line(mesh, r, 0.95f, 0.7f, 0.95f, 0.25f);
            Line(mesh, r, 0.35f, 0.7f, 0.05f, 0.7f);
            Line(mesh, r, 0.65f, 0.7f, 0.95f, 0.7f);
            Line(mesh, r, 0.5f, 0.3f, 0.5f, 0.1f);
        }

        private void DrawFortify(VertexHelper mesh, Rect r)
        {
            QuadOutline(mesh, r, 0.1f, 0.1f, 0.8f, 0.55f);
            Line(mesh, r, 0.1f, 0.55f, 0.1f, 0.8f);
            Line(mesh, r, 0.3f, 0.55f, 0.3f, 0.95f);
            Line(mesh, r, 0.5f, 0.55f, 0.5f, 0.8f);
            Line(mesh, r, 0.7f, 0.55f, 0.7f, 0.95f);
            Line(mesh, r, 0.9f, 0.55f, 0.9f, 0.8f);
            Line(mesh, r, 0.1f, 0.8f, 0.3f, 0.95f);
            Line(mesh, r, 0.5f, 0.8f, 0.7f, 0.95f);
            Line(mesh, r, 0.9f, 0.8f, 0.7f, 0.95f);
        }

        private void DrawStrike(VertexHelper mesh, Rect r)
        {
            Line(mesh, r, 0.5f, 0.95f, 0.5f, 0.25f);
            Line(mesh, r, 0.5f, 0.05f, 0.35f, 0.3f);
            Line(mesh, r, 0.5f, 0.05f, 0.65f, 0.3f);
            Line(mesh, r, 0.5f, 0.5f, 0.25f, 0.75f);
            Line(mesh, r, 0.5f, 0.5f, 0.75f, 0.75f);
        }

        private void DrawEw(VertexHelper mesh, Rect r)
        {
            Quad(mesh, new Vector2(0.46f * r.width, 0.1f * r.height),
                new Vector2(0.54f * r.width, 0.1f * r.height),
                new Vector2(0.54f * r.width, 0.18f * r.height),
                new Vector2(0.46f * r.width, 0.18f * r.height), r.min);
            Arc(mesh, r, 0.5f, 0.12f, 0.28f, -0.2f, 1.2f);
            Arc(mesh, r, 0.5f, 0.12f, 0.5f, -0.2f, 1.2f);
            Arc(mesh, r, 0.5f, 0.12f, 0.72f, -0.2f, 1.2f);
        }

        private void QuadOutline(VertexHelper mesh, Rect r, float x, float y, float w, float h)
        {
            Line(mesh, r, x, y, x + w, y);
            Line(mesh, r, x + w, y, x + w, y + h);
            Line(mesh, r, x + w, y + h, x, y + h);
            Line(mesh, r, x, y + h, x, y);
        }

        private void Arc(VertexHelper mesh, Rect r, float cx, float cy, float radius,
            float startAngle, float sweep)
        {
            const int steps = 10;
            float previous = startAngle;
            for (int i = 1; i <= steps; i++)
            {
                float angle = startAngle + sweep * i / steps;
                Line(mesh, r, cx + Mathf.Cos(previous) * radius, cy + Mathf.Sin(previous) * radius,
                     cx + Mathf.Cos(angle) * radius, cy + Mathf.Sin(angle) * radius);
                previous = angle;
            }
        }

        private void Line(VertexHelper mesh, Rect r, float x1, float y1, float x2, float y2)
        {
            Vector2 a = new Vector2(x1 * r.width, y1 * r.height);
            Vector2 b = new Vector2(x2 * r.width, y2 * r.height);
            Vector2 d = b - a;
            Vector2 n = new Vector2(-d.y, d.x).normalized * 0.8f;
            Quad(mesh, a - n, b - n, b + n, a + n, r.min);
        }

        private void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 origin)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(a + origin, color, Vector2.zero);
            mesh.AddVert(b + origin, color, Vector2.zero);
            mesh.AddVert(c + origin, color, Vector2.zero);
            mesh.AddVert(d + origin, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}


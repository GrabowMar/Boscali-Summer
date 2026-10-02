using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    /// <summary>Two-axis work indicator: horizontal servo error, vertical alignment deficit.</summary>
    internal sealed class AlignmentScopeGraphic : MaskableGraphic
    {
        private float alignment, match;
        public void Set(float align, float servo)
        {
            if (Mathf.Abs(alignment - align) < 0.002f && Mathf.Abs(match - servo) < 0.002f) return;
            alignment = Mathf.Clamp01(align); match = Mathf.Clamp01(servo); SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Vector2 centre = rectTransform.rect.center;
            float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.45f;
            for (int ring = 1; ring <= 3; ring++)
                for (int i = 0; i < 64; i++)
                {
                    if (ring == 1 && i % 4 == 3) continue;
                    float a = i * Mathf.PI / 32f, b = (i + 1) * Mathf.PI / 32f;
                    Line(mesh, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * ring / 3f,
                        centre + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius * ring / 3f, ring == 3 ? 1.15f : .65f,
                        RoomPaint.Instrument.WithAlpha(ring == 3 ? .72f : .38f));
                }
            for (int i = 0; i < 24; i++)
            {
                float angle = i * Mathf.PI / 12f;
                Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Line(mesh, centre + axis * radius * .98f, centre + axis * radius * (i % 6 == 0 ? .84f : .92f), .8f, RoomPaint.Instrument.WithAlpha(.8f));
            }
            Line(mesh, centre + Vector2.left * radius, centre + Vector2.right * radius, .8f, RoomPaint.Instrument.WithAlpha(.48f));
            Line(mesh, centre + Vector2.up * radius, centre + Vector2.down * radius, .8f, RoomPaint.Instrument.WithAlpha(.48f));
            // Static aim reference and a lead line make the actual error vector legible at small sizes.
            const float aim = 5f;
            Line(mesh, centre + new Vector2(-aim, -aim), centre + new Vector2(-aim, aim), .8f, RoomPaint.Ready.WithAlpha(.6f));
            Line(mesh, centre + new Vector2(aim, -aim), centre + new Vector2(aim, aim), .8f, RoomPaint.Ready.WithAlpha(.6f));
            Line(mesh, centre + new Vector2(-aim, -aim), centre + new Vector2(aim, -aim), .8f, RoomPaint.Ready.WithAlpha(.6f));
            Line(mesh, centre + new Vector2(-aim, aim), centre + new Vector2(aim, aim), .8f, RoomPaint.Ready.WithAlpha(.6f));
            Vector2 point = centre + new Vector2(1f - match, 1f - alignment) * radius * 0.65f;
            Color tone = match >= 0.8f && alignment >= 0.45f ? RoomPaint.Ready : RoomPaint.Command;
            if ((point - centre).sqrMagnitude > 1f) Line(mesh, centre, point, .8f, tone.WithAlpha(.3f));
            Line(mesh, point + Vector2.left * 4.5f, point + Vector2.right * 4.5f, 2.1f, tone);
            Line(mesh, point + Vector2.up * 4.5f, point + Vector2.down * 4.5f, 2.1f, tone);
        }
        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 d = (b - a).normalized, n = new Vector2(-d.y, d.x) * width * 0.5f;
            int start = mesh.currentVertCount;
            mesh.AddVert(a + n, tint, Vector2.zero); mesh.AddVert(b + n, tint, Vector2.zero);
            mesh.AddVert(b - n, tint, Vector2.zero); mesh.AddVert(a - n, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}

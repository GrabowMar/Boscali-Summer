using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    /// <summary>A bounded protocol circuit. Its lamps describe host gate state, never signal measurements.</summary>
    internal sealed class CyberGateGraphic : MaskableGraphic
    {
        private bool active, accepted, held;
        private int route = -1;
        private Color track, ink, caution;

        public void Paint(bool session, bool verified, bool responseHold, int acceptedRoute,
            Color dim, Color mint, Color amber)
        {
            if (active == session && accepted == verified && held == responseHold && route == acceptedRoute &&
                track == dim && ink == mint && caution == amber) return;
            active = session; accepted = verified; held = responseHold; route = acceptedRoute;
            track = dim; ink = mint; caution = amber;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float left = rect.xMin + 3, right = rect.xMax - 3, mid = rect.center.y;
            float port = rect.xMin + rect.width * .56f, upper = mid + 9, lower = mid - 9;
            Color live = accepted ? ink : held ? caution : active ? ink.WithAlpha(.65f) : track;
            Line(mesh, left, mid, left + 11, mid, 1.4f, live);
            Line(mesh, left + 11, lower, left + 11, upper, 1, track);
            Line(mesh, right - 10, lower, right - 10, upper, 1, track);
            Line(mesh, right - 10, mid, right, mid, 1.4f, live);
            for (int i = 0; i < 3; i++)
            {
                float y = i == 0 ? upper : i == 1 ? mid : lower;
                bool chosen = accepted && route == i;
                Color branch = chosen ? ink : track;
                Line(mesh, left + 11, y, port - 5, y, 1.1f, branch);
                Line(mesh, port + 5, y, right - 10, y, 1.1f, branch);
                Box(mesh, port - 4, y - 3, 8, 6, 1, branch);
                if (chosen) Quad(mesh, port - 2, y - 1, 4, 2, ink);
            }
            Box(mesh, left - 2, mid - 3, 5, 6, 1, live);
            Box(mesh, right - 3, mid - 3, 5, 6, 1, live);
        }

        private static void Line(VertexHelper mesh, float x1, float y1, float x2, float y2, float thickness, Color tint)
        {
            Vector2 from = new Vector2(x1, y1), to = new Vector2(x2, y2);
            Vector2 normal = new Vector2(-(to - from).y, (to - from).x).normalized * thickness * .5f;
            int n = mesh.currentVertCount;
            mesh.AddVert(from - normal, tint, Vector2.zero); mesh.AddVert(from + normal, tint, Vector2.zero);
            mesh.AddVert(to + normal, tint, Vector2.zero); mesh.AddVert(to - normal, tint, Vector2.zero);
            mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n, n + 2, n + 3);
        }

        private static void Box(VertexHelper mesh, float x, float y, float w, float h, float thickness, Color tint)
        {
            Quad(mesh, x, y, w, thickness, tint); Quad(mesh, x, y + h - thickness, w, thickness, tint);
            Quad(mesh, x, y + thickness, thickness, h - thickness * 2, tint);
            Quad(mesh, x + w - thickness, y + thickness, thickness, h - thickness * 2, tint);
        }

        private static void Quad(VertexHelper mesh, float x, float y, float w, float h, Color tint)
        {
            int n = mesh.currentVertCount;
            mesh.AddVert(new Vector3(x, y), tint, Vector2.zero); mesh.AddVert(new Vector3(x + w, y), tint, Vector2.zero);
            mesh.AddVert(new Vector3(x + w, y + h), tint, Vector2.zero); mesh.AddVert(new Vector3(x, y + h), tint, Vector2.zero);
            mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n, n + 2, n + 3);
        }
    }
}

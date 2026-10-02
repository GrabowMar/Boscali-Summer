using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>Two-stop vertex gradient multiplied into a Graphic's vertices (UIGradient-class).</summary>
    public sealed class AvGradient : BaseMeshEffect
    {
        public Color Top = Color.white, Bottom = Color.white;
        public bool Horizontal;
        private static readonly List<UIVertex> Verts = new List<UIVertex>(256);

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            Rect r = ((RectTransform)transform).rect;
            Verts.Clear(); vh.GetUIVertexStream(Verts);
            for (int i = 0; i < Verts.Count; i++)
            {
                UIVertex v = Verts[i];
                float t = Horizontal ? Mathf.InverseLerp(r.xMin, r.xMax, v.position.x) : Mathf.InverseLerp(r.yMin, r.yMax, v.position.y);
                v.color = v.color * Color.Lerp(Bottom, Top, t);
                Verts[i] = v;
            }
            vh.Clear(); vh.AddUIVertexTriangleStream(Verts);
        }
    }
}

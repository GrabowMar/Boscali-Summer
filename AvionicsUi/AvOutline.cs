using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>Crisp 4-direction outline for text over the moving map (NicerOutline-class). Capped at 4x vertices.</summary>
    public sealed class AvOutline : BaseMeshEffect
    {
        public Color Color = new Color(0f, 0f, 0f, 0.6f);
        public float Distance = 1f;
        private static readonly List<UIVertex> Verts = new List<UIVertex>(1024);
        private static readonly Vector2[] Offsets = { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) };

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            Verts.Clear(); vh.GetUIVertexStream(Verts);
            int original = Verts.Count;
            var result = new List<UIVertex>(original * 5);
            foreach (Vector2 o in Offsets)
                for (int i = 0; i < original; i++)
                {
                    UIVertex v = Verts[i];
                    v.position += (Vector3)(o * Distance);
                    Color32 c = Color; c.a = (byte)(c.a * v.color.a / 255);
                    v.color = c;
                    result.Add(v);
                }
            result.AddRange(Verts);
            vh.Clear(); vh.AddUIVertexTriangleStream(result);
        }
    }
}

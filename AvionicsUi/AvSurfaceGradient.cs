using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>UIEffect's rect-space vertex gradient, reduced to two colours for instrument plates.
    /// Source and MIT notice: ThirdParty/UIEffect/NOTICE.txt. No material instance or update loop.</summary>
    [DisallowMultipleComponent]
    public sealed class AvSurfaceGradient : BaseMeshEffect
    {
        private Color top = Color.white, bottom = Color.white;

        public static void Apply(Graphic target, Color top, Color bottom)
        {
            if (target == null) return;
            var effect = target.GetComponent<AvSurfaceGradient>();
            if (effect == null) effect = target.gameObject.AddComponent<AvSurfaceGradient>();
            if (effect.top == top && effect.bottom == bottom) return;
            effect.top = top;
            effect.bottom = bottom;
            target.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive() || graphic == null) return;
            Rect rect = graphic.rectTransform.rect;
            if (rect.height <= 0f) return;
            UIVertex vertex = default;
            for (int i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                float y = (vertex.position.y - rect.yMin) / rect.height;
                vertex.color *= Color.LerpUnclamped(bottom, top, y);
                vertices.SetUIVertex(vertex, i);
            }
        }
    }
}

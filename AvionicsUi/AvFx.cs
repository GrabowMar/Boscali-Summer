using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// Packs an effect into this Graphic's vertices: UV1 = (u, v, aspect, 0), UV2 = (id, start, intensity, param).
    /// Starting an effect dirties the vertices once; the animation itself runs on the GPU from _NOA_Now.
    /// Without the bundle (or at FxTier.Off) the Graphic keeps its default material and nothing is packed.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class AvFx : BaseMeshEffect
    {
        private static readonly List<UIVertex> Verts = new List<UIVertex>(256);
        private AvFxKind kind;
        private float start, intensity = 1f, param;
        private int revision = -1;

        public static AvFx On(Graphic g)
        {
            if (g == null) return null;
            AvFx fx = g.GetComponent<AvFx>();
            if (fx == null) fx = g.gameObject.AddComponent<AvFx>(); // not ??: the Editor returns a fake-null for missing components
            fx.ApplyMaterial();
            AvFxDriver.EnsureRunning();
            return fx;
        }

        public void Set(AvFxKind k, float strength = 1f, float p = 0f)
        {
            if (k == kind && Mathf.Approximately(strength, intensity) && Mathf.Approximately(p, param) && revision == AvFxDriver.Revision) return;
            kind = k; intensity = strength; param = p; start = -1000f;
            Dirty();
        }

        public void Play(AvFxKind k, float strength = 1f, float p = 0f)
        {
            kind = k; intensity = strength; param = p; start = Time.unscaledTime;
            Dirty();
        }

        public void Clear() => Set(AvFxKind.None);

        private void Dirty()
        {
            if (revision != AvFxDriver.Revision) ApplyMaterial();
            if (graphic != null) graphic.SetVerticesDirty();
        }

        private void ApplyMaterial()
        {
            revision = AvFxDriver.Revision;
            if (graphic == null) return;
            Material m = AvFxDriver.FxMaterial;
            if (graphic.material != m && (m != null || graphic.material != graphic.defaultMaterial))
                graphic.material = m; // null restores the default UI material
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0 || AvFxDriver.FxMaterial == null) return;
            AvFxKind effective = AvFxPacking.Resolve(kind, AvFxDriver.Tier, AvFxDriver.ReducedMotion);
            Rect r = ((RectTransform)transform).rect;
            float aspect = AvFxPacking.Aspect(r.width, r.height);
            Verts.Clear(); vh.GetUIVertexStream(Verts);
            for (int i = 0; i < Verts.Count; i++)
            {
                UIVertex v = Verts[i];
                v.uv1 = new Vector4(AvFxPacking.U(v.position.x, r.xMin, r.width), AvFxPacking.V(v.position.y, r.yMin, r.height), aspect, 0f);
                v.uv2 = new Vector4((float)effective, start, intensity, param);
                Verts[i] = v;
            }
            vh.Clear(); vh.AddUIVertexTriangleStream(Verts);
        }
    }
}

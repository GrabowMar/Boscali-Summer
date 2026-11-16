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
        private AvFxKind kind;
        private AvFxKind heldKind;
        private float start, intensity = 1f, param;
        private float heldIntensity = 1f, heldParam;
        private int revision = -1;
        private bool ownsMaterial;
        private static readonly List<AvFx> active = new List<AvFx>(16);

        public static AvFx On(Graphic g)
        {
            if (g == null) return null;
            AvFx fx = g.GetComponent<AvFx>();
            if (fx == null) fx = g.gameObject.AddComponent<AvFx>(); // not ??: the Editor returns a fake-null for missing components
            fx.ApplyMaterial();
            return fx;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            AvFxDriver.Changed += SettingsChanged;
            ApplyMaterial();
        }

        protected override void OnDisable()
        {
            AvFxDriver.Changed -= SettingsChanged;
            active.Remove(this);
            if (AvFxPacking.IsTimed(kind)) { kind = heldKind; intensity = heldIntensity; param = heldParam; start = -1000f; }
            if (ownsMaterial && graphic != null) graphic.material = null;
            ownsMaterial = false;
            base.OnDisable();
        }

        private void SettingsChanged()
        {
            // Suppressed one-shot feedback must not replay when motion is enabled later.
            if (AvFxPacking.IsTimed(kind) && AvFxPacking.Resolve(kind, AvFxDriver.Tier, AvFxDriver.ReducedMotion) == AvFxKind.None)
            { kind = heldKind; intensity = heldIntensity; param = heldParam; start = -1000f; }
            Dirty();
        }

        public void Set(AvFxKind k, float strength = 1f, float p = 0f)
        {
            if (k == kind && Mathf.Approximately(strength, intensity) && Mathf.Approximately(p, param) && revision == AvFxDriver.Revision) return;
            heldKind = AvFxPacking.IsTimed(k) ? AvFxKind.None : k;
            heldIntensity = strength; heldParam = p;
            kind = k; intensity = strength; param = p; start = -1000f;
            Dirty();
        }

        public void Play(AvFxKind k, float strength = 1f, float p = 0f)
        {
            if (AvFxPacking.Resolve(k, AvFxDriver.Tier, AvFxDriver.ReducedMotion) == AvFxKind.None) return;
            kind = k;
            intensity = strength; param = p; start = AvFxDriver.Now;
            Dirty();
        }

        public void Clear() => Set(AvFxKind.None);

        private void Dirty()
        {
            ApplyMaterial();
            if (graphic != null) graphic.SetVerticesDirty();
        }

        private void ApplyMaterial()
        {
            revision = AvFxDriver.Revision;
            if (graphic == null) return;
            AvFxKind effective = AvFxPacking.Resolve(kind, AvFxDriver.Tier, AvFxDriver.ReducedMotion);
            Material m = IsActive() && effective != AvFxKind.None ? AvFxDriver.FxMaterial : null;
            if (m != null)
            {
                if (graphic.material != m) graphic.material = m;
                ownsMaterial = true;
                if (!active.Contains(this)) active.Add(this);
                AvFxDriver.EnsureRunning();
            }
            else
            {
                if (ownsMaterial) graphic.material = null;
                ownsMaterial = false;
                active.Remove(this);
            }
        }

        // The shared driver visits only effects that are actually drawing, not every idle control.
        // Finishing a transient effect restores UI/Default once, so quiet panels batch normally.
        internal static bool TickActive(float now)
        {
            bool drawing = false;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                AvFx fx = active[i];
                if (fx == null) { active.RemoveAt(i); continue; }
                if (AvFxPacking.IsTimed(fx.kind) && now - fx.start >= AvFxPacking.DurationOf(fx.kind) * AvFxPacking.AnimationSpeed)
                {
                    fx.kind = fx.heldKind; fx.intensity = fx.heldIntensity; fx.param = fx.heldParam; fx.start = -1000f;
                    fx.Dirty();
                }
                if (fx.ownsMaterial && fx.graphic != null && fx.graphic.canvas != null && fx.graphic.canvas.isActiveAndEnabled)
                    drawing = true;
            }
            return drawing;
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || !ownsMaterial || vh.currentVertCount == 0) return;
            AvFxKind effective = AvFxPacking.Resolve(kind, AvFxDriver.Tier, AvFxDriver.ReducedMotion);
            Rect r = ((RectTransform)transform).rect;
            float aspect = AvFxPacking.Aspect(r.width, r.height);
            // Only the UV channels change: retain shared vertices and the original index buffer.
            UIVertex v = default;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.uv1 = new Vector4(AvFxPacking.U(v.position.x, r.xMin, r.width), AvFxPacking.V(v.position.y, r.yMin, r.height), aspect, 0f);
                v.uv2 = new Vector4((float)effective, start, intensity, param);
                vh.SetUIVertex(v, i);
            }
        }
    }
}

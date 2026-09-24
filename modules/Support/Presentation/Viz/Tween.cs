using BoscaliSummer.Features.Support.Domain.Layout;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>A widget value that eases on the shared motion curve. Reduced motion snaps.</summary>
    internal sealed class Tween
    {
        private TweenState state;
        private float shown = float.NaN;

        public float Value => state.Value;

        public void Set(float to, float duration, bool reduce)
        {
            if (!reduce && Mathf.Abs(to - state.To) < 0.0001f && Mathf.Abs(to - state.Value) < 0.0001f) return;
            state.Retarget(to, duration, reduce);
        }

        public bool Tick(float dt)
        {
            state.Tick(dt);
            if (!float.IsNaN(shown) && Mathf.Abs(state.Value - shown) < 0.0005f) return false;
            shown = state.Value;
            return true;
        }
    }
}

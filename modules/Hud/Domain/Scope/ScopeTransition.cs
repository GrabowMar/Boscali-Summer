using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The scope's page change (HUD v8), pure: a new page key restarts a <see cref="ScopeLayout.WipeSeconds"/> reveal and eases the
    /// scope's height from where it was to the new page's (compact strip to full page and back). The very first key snaps.
    /// </summary>
    internal sealed class ScopeTransition
    {
        private int key = int.MinValue;
        private float from, to, at = float.NegativeInfinity;

        /// <summary>The scope's current height in board units.</summary>
        public float Height { get; private set; }

        /// <summary>0..1: how much of the new page the wipe has revealed (1 = done).</summary>
        public float Wipe { get; private set; } = 1f;

        public bool Running => Wipe < 1f || Math.Abs(Height - to) > 0.01f;

        public void Update(int pageKey, float targetHeight, float now)
        {
            if (pageKey != key)
            {
                from = key == int.MinValue ? targetHeight : Height;
                to = targetHeight;
                at = key == int.MinValue ? float.NegativeInfinity : now;
                key = pageKey;
            }
            else if (targetHeight != to) { from = Height; to = targetHeight; at = now; }
            float p = at == float.NegativeInfinity ? 1f : Math.Max(0f, Math.Min(1f, (now - at) / ScopeLayout.WipeSeconds));
            Wipe = p;
            float e = p * p * (3f - 2f * p);
            Height = (float)Math.Round(from + (to - from) * e);
        }

        public void Reset()
        {
            key = int.MinValue;
            Wipe = 1f;
            Height = to = from = 0f;
            at = float.NegativeInfinity;
        }
    }
}

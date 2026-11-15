using System;

namespace BoscaliSummer.Modules.Spooktober.Domain
{
    /// <summary>A ground force must win the local fight before the corruption can take a base.</summary>
    internal static class HellControl
    {
        internal const float CaptureSeconds = 18f, AbandonSeconds = 60f, ReclaimSeconds = 12f;

        internal static float Advance(float progress, int attackers, int defenders, float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) return progress;
            float rate = attackers > defenders ? 1f / CaptureSeconds : -1f / ReclaimSeconds;
            return Math.Max(0f, Math.Min(1f, progress + rate * Math.Min(seconds, 2f)));
        }

        internal static bool Lost(bool controlled, int attackers, int defenders, float abandonedFor, float progress) =>
            controlled && ((defenders > attackers && progress <= 0f) || (attackers == 0 && abandonedFor >= AbandonSeconds));
    }
}

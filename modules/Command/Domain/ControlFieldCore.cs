using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// What one control cell reads as under the corrected rule. Numeric order matches
    /// <c>SectorControl</c> in Runtime so the grid can adopt this without remapping.
    /// </summary>
    internal enum ControlState : byte
    {
        Neutral = 0,
        Friendly = 1,
        Hostile = 2,
        Contested = 3
    }

    /// <summary>
    /// The corrected core of the territory-control field ("COREControl"): contact-gated
    /// contested classification, the exponential capture response, and the fixed-cadence
    /// elapsed policy. Pure, so the STR sector rows, the planner and the front-field
    /// repair share one tested rule instead of re-deriving it per caller.
    ///
    /// <para>Two verified defects live here. First, contested needs real contact: a cell
    /// whose hold sits within epsilon under strategic influence alone is unclaimed
    /// ground, not a battle, and must not raise DEFCON or draw the planner. Second, an
    /// unchanged observation snapshot preserves its elapsed interval instead of
    /// consuming it, and the field keeps evaluating at 1 Hz while converging, so a
    /// field read at host cadence advances at real speed.</para>
    /// </summary>
    internal static class ControlFieldCore
    {
        internal const float PresenceThreshold = 0.05f;
        internal const float HoldEpsilon = 0.01f;
        internal const float ClashLowRatio = 0.34f;
        internal const float ClashHighRatio = 0.66f;
        internal const float CaptureSeconds = 15f;
        internal const float RecoverySeconds = 90f;

        /// <summary>Widest interval one evaluation may advance; host cadence is seconds.</summary>
        internal const float MaxElapsedSeconds = 5f;

        /// <summary>Unchanged snapshots re-evaluate this often while converging.</summary>
        internal const float ReevaluateUnchangedSeconds = 1f;

        internal const float MaxResponseElapsedSeconds = 300f;

        /// <summary>
        /// The exponential capture response: how far hold moves toward target this pass.
        /// Troops present converge in seconds, empty ground recovers over minutes.
        /// </summary>
        public static float Response(float elapsedSeconds, float totalForce)
        {
            float elapsed = Finite(elapsedSeconds, 0f);
            if (elapsed < 0f) elapsed = 0f;
            if (elapsed > MaxResponseElapsedSeconds) elapsed = MaxResponseElapsedSeconds;
            float tau = totalForce > PresenceThreshold ? CaptureSeconds : RecoverySeconds;
            return 1f - (float)Math.Exp(-elapsed / tau);
        }

        /// <summary>Both sides hold ground weight in the cell: a battle is possible.</summary>
        public static bool HasContact(float friendly, float hostile) =>
            friendly > PresenceThreshold && hostile > PresenceThreshold;

        /// <summary>
        /// Whether the cell is contested. Without contact there is no battle: neither a
        /// balanced-looking hold nor strategic influence alone contests a cell.
        /// </summary>
        public static bool IsContested(float hold, float friendly, float hostile, bool anchoredClash)
        {
            if (anchoredClash) return true;
            if (!HasContact(friendly, hostile)) return false;
            float total = friendly + hostile;
            float ratio = friendly / total;
            if (ratio > ClashLowRatio && ratio < ClashHighRatio) return true;
            float pressure = (friendly - hostile) / Math.Max(3f, total);
            return Math.Abs(pressure) > PresenceThreshold && pressure * Finite(hold, 0f) < 0f;
        }

        /// <summary>
        /// The cell's state under the corrected rule. Near-zero hold with no contact is
        /// neutral ground, never a confident claim for either side.
        /// </summary>
        public static ControlState Classify(float hold, float friendly, float hostile, bool anchoredClash)
        {
            if (IsContested(hold, friendly, hostile, anchoredClash)) return ControlState.Contested;
            float value = Finite(hold, 0f);
            if (value > HoldEpsilon) return ControlState.Friendly;
            if (value < -HoldEpsilon) return ControlState.Hostile;
            return ControlState.Neutral;
        }

        /// <summary>
        /// The elapsed policy for one field read. Reports whether to evaluate, the capped
        /// interval, and the stamp to store. An unchanged snapshot inside the
        /// re-evaluation window skips without consuming its interval, so slow cadences
        /// advance at real speed instead of dropping time.
        /// </summary>
        public static bool TryElapsed(float now, float updated, bool evaluated, bool snapshotChanged,
            out float elapsed, out float consumed)
        {
            float at = Finite(now, 0f);
            float was = Finite(updated, -1f);
            if (!evaluated)
            {
                elapsed = 0f;
                consumed = at;
                return true;
            }
            if (at < was)
            {
                elapsed = 0f;
                consumed = at;
                return true;
            }
            if (!snapshotChanged && at - was < ReevaluateUnchangedSeconds)
            {
                elapsed = 0f;
                consumed = was;
                return false;
            }
            float interval = at - was;
            if (interval < 0f) interval = 0f;
            if (interval > MaxElapsedSeconds) interval = MaxElapsedSeconds;
            elapsed = interval;
            consumed = at;
            return true;
        }

        private static float Finite(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}

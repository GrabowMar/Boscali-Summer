using System;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>
    /// The last four closed operation ids; a sortie tied to one may still credit the wave bank for 120 s after it closed (ruling D4).
    /// The desk feeds it the live operation id at every review and at every finished sortie, so it does not depend on a result that a
    /// redirect chain overwrites. An unreadable clock fails closed: nothing is stamped and nothing counts.
    /// </summary>
    internal sealed class FocusHistory
    {
        internal const int Capacity = 4;
        internal const float LateWindowSeconds = 120f;

        private readonly int[] ids = new int[Capacity];
        private readonly float[] closedAt = new float[Capacity];
        private int count;
        private int live;

        /// <summary>Call with the current focus id (0 when none); stamps the previous live id as closed when it changed.</summary>
        internal void Observe(int liveOperationId, float now)
        {
            if (!float.IsFinite(now)) return;
            if (liveOperationId < 0) liveOperationId = 0;
            if (liveOperationId == live) return;
            if (live > 0) Close(live, now);
            live = liveOperationId;
        }

        /// <summary>True for a closed operation at most 120 s old. A live operation is covered by the focus itself, not by this window.</summary>
        internal bool Recent(int operationId, float now)
        {
            if (operationId <= 0 || !float.IsFinite(now)) return false;
            for (int i = 0; i < count; i++)
            {
                if (ids[i] != operationId) continue;
                float age = now - closedAt[i];
                return age >= 0f && age <= LateWindowSeconds;
            }
            return false;
        }

        private void Close(int operationId, float now)
        {
            if (count == Capacity)
            {
                Array.Copy(ids, 1, ids, 0, Capacity - 1);
                Array.Copy(closedAt, 1, closedAt, 0, Capacity - 1);
                count--;
            }
            ids[count] = operationId;
            closedAt[count] = now;
            count++;
        }
    }
}

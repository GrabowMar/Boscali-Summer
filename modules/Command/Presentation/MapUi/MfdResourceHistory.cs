using System;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    // Local observations only: no backfill and no simulated resource production.
    internal sealed class MfdResourceHistory
    {
        internal const int Capacity = 60;
        internal const float Interval = 5f;
        private readonly float[,] values = new float[4, Capacity];
        private readonly float[] times = new float[Capacity];
        private int start;
        internal int Count { get; private set; }
        internal float Value(int series, int index) => values[series, (start + index) % Capacity];
        internal float Time(int index) => times[(start + index) % Capacity];

        /// <summary>
        /// Whether a new observation is due at <paramref name="now"/>. Callers use this to
        /// avoid reading expensive game state between the 5-second samples.
        /// </summary>
        internal bool Due(float now)
        {
            if (!Finite(now)) return false;
            if (Count == 0) return true;
            float elapsed = now - Time(Count - 1);
            return elapsed < 0f || elapsed > Interval * 3f || elapsed >= Interval;
        }

        internal bool Sample(float now, float funds, float warheads, float manpower, float morale)
        {
            if (!Finite(now)) return false;
            if (Count > 0)
            {
                float elapsed = now - Time(Count - 1);
                if (elapsed < 0f || elapsed > Interval * 3f) Clear();
                else if (elapsed < Interval) return false;
            }
            int slot = (start + Count) % Capacity;
            if (Count == Capacity) start = (start + 1) % Capacity;
            else Count++;
            times[slot] = now;
            values[0, slot] = funds;
            values[1, slot] = warheads;
            values[2, slot] = manpower;
            values[3, slot] = morale;
            return true;
        }

        internal void Range(int series, out float minimum, out float maximum)
        {
            minimum = 0f;
            maximum = series == 3 ? 100f : 0f;
            for (int i = 0; i < Count; i++)
            {
                float value = Value(series, i);
                if (!Finite(value)) continue;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
            if (minimum == maximum) maximum = minimum + 1f;
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal void Clear() { start = 0; Count = 0; }
    }
}

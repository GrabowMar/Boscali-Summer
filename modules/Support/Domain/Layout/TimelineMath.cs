using System;
using BoscaliSummer.Modules.Support.Domain.SpecOps;

namespace BoscaliSummer.Modules.Support.Domain.Layout
{
    internal enum LaneKind : byte
    {
        Empty = 0,
        EnRoute = 1,
        OnTask = 2,
        Holding = 3,
        Recovering = 4,
        Deciding = 5
    }

    internal struct LaneSegment
    {
        public float Start;
        public float End;
        public LaneKind Kind;
        public bool Projected;
    }

    /// <summary>
    /// Team lanes use <see cref="FieldCatalog"/> durations. Later phases are projected until
    /// the arrival decision, task order and outcome actually happen.
    /// </summary>
    internal static class TimelineMath
    {
        public static int Team(TeamState state, float remaining, FieldMission mission, int rank, float window,
            LaneSegment[] into)
        {
            if (into == null || into.Length == 0 || !(window > 0f)) return 0;
            if (state != TeamState.EnRoute && state != TeamState.Deciding && state != TeamState.OnTask &&
                state != TeamState.Holding && state != TeamState.Recovering)
            {
                into[0] = Segment(0f, 1f, LaneKind.Empty, false);
                return 1;
            }

            float cursor = 0f;
            int count = 0;
            if (state == TeamState.EnRoute)
                count = Add(into, count, ref cursor, remaining, LaneKind.EnRoute, false, window);
            if (cursor < window && (state == TeamState.EnRoute || state == TeamState.Deciding))
            {
                float decision = state == TeamState.Deciding ? remaining : FieldCatalog.DecisionSeconds;
                count = Add(into, count, ref cursor, decision, LaneKind.Deciding,
                    state != TeamState.Deciding, window);
            }
            if (cursor < window && (state == TeamState.EnRoute || state == TeamState.Deciding || state == TeamState.OnTask))
            {
                float task = state == TeamState.OnTask ? remaining : FieldCatalog.TaskSeconds(mission);
                count = Add(into, count, ref cursor, task, LaneKind.OnTask,
                    state != TeamState.OnTask, window);
            }
            if (cursor < window && state != TeamState.Recovering)
            {
                float hold = state == TeamState.Holding ? remaining : FieldCatalog.HoldSeconds(rank);
                count = Add(into, count, ref cursor, hold, LaneKind.Holding, state != TeamState.Holding, window);
            }
            if (cursor < window)
            {
                float recover = state == TeamState.Recovering ? remaining : FieldCatalog.RecoverSeconds;
                count = Add(into, count, ref cursor, recover, LaneKind.Recovering, state != TeamState.Recovering, window);
            }
            return count;
        }

        private static int Add(LaneSegment[] into, int count, ref float cursor, float seconds, LaneKind kind,
            bool projected, float window)
        {
            if (count >= into.Length || !(seconds > 0f) || cursor >= window) return count;
            float end = cursor + seconds;
            if (end > window) end = window;
            into[count] = Segment(cursor / window, end / window, kind, projected);
            cursor = end;
            return count + 1;
        }

        private static LaneSegment Segment(float start, float end, LaneKind kind, bool projected) =>
            new LaneSegment { Start = start, End = end, Kind = kind, Projected = projected };
    }
}

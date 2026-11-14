using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    internal enum FocusNodeState : byte { Locked, Open, Queued, Running, Done }

    /// <summary>The focus tree's rules as the FAC screen reads them (pure): a focus needs the one before it in its branch, and tier 3 needs Escalation 1.</summary>
    internal static class FocusTreeModel
    {
        public const int Count = 12;
        public const float DefaultTakes = 240f;

        public static int Tier(int id) => id & 3;
        public static int Parent(int id) => Tier(id) == 0 ? -1 : id - 1;
        public static bool IsDone(NationView v, int id) => (v.Done & (1 << id)) != 0;

        public static bool RequirementsMet(NationView v, int id) =>
            (Parent(id) < 0 || IsDone(v, Parent(id))) && (Tier(id) < 3 || v.Escalation >= 1);

        public static FocusNodeState State(NationView v, int id) =>
            IsDone(v, id) ? FocusNodeState.Done
            : v.Running == id ? FocusNodeState.Running
            : v.Queued == id ? FocusNodeState.Queued
            : RequirementsMet(v, id) ? FocusNodeState.Open : FocusNodeState.Locked;

        /// <summary>Open, or locked only behind the focus that is running now (the host queues it).</summary>
        public static bool CanPick(NationView v, int id)
        {
            FocusNodeState s = State(v, id);
            if (s == FocusNodeState.Open) return true;
            return s == FocusNodeState.Locked && Parent(id) >= 0 && v.Running == Parent(id) && (Tier(id) < 3 || v.Escalation >= 1);
        }

        /// <summary>Why a locked node is shut: the missing parent first, then escalation.</summary>
        public static string LockReason(NationView v, int id) =>
            Parent(id) >= 0 && !IsDone(v, Parent(id)) ? "AFTER " + NationWords.Focus(Parent(id)) : "ESCALATION 1";

        public static float Percent(NationView v) =>
            v.Running >= 0 && v.RunningTotal > 0f ? UnityEngine.Mathf.Clamp01(1f - v.RunningLeft / v.RunningTotal) : 0f;

        public static int DoneCount(NationView v)
        {
            int n = 0;
            for (int i = 0; i < Count; i++) if (IsDone(v, i)) n++;
            return n;
        }
    }
}

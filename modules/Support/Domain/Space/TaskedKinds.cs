using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>The OPS domain a TASKED post belongs to. Wire values (2 bits on a post row); never renumber.</summary>
    internal enum TaskedDomain : byte { Space = 0, Cyber = 1, Sof = 2 }

    internal readonly struct TaskedKind
    {
        public readonly SupportActionId Action;
        public readonly TaskedDomain Domain;
        public readonly string Label;

        public TaskedKind(SupportActionId action, TaskedDomain domain, string label)
        { Action = action; Domain = domain; Label = label; }
    }

    /// <summary>
    /// Everything that may ride the TASKED board: the CALL sheet's actions (SPACE rod and friends) and the CYBER BURN packages, which
    /// are never CALL rows. One lookup for validity, label and domain, so the board and the wire agree.
    /// </summary>
    internal static class TaskedKinds
    {
        public static bool TryGet(SupportActionId action, out TaskedKind kind)
        {
            if (CyberPackages.TryOfAction(action, out PackageDef def))
            {
                kind = new TaskedKind(action, TaskedDomain.Cyber, def.Label);
                return true;
            }
            if (SofPosts.IsPost(action))
            {
                kind = new TaskedKind(action, TaskedDomain.Sof, SofPosts.Label(action));
                return true;
            }
            if (CallSheet.TryGet(action, out CallRow row))
            {
                kind = new TaskedKind(action, row.Front == Fronts.Front.Cyber ? TaskedDomain.Cyber : row.Front == Fronts.Front.Sof ? TaskedDomain.Sof : TaskedDomain.Space,
                    row.Label);
                return true;
            }
            kind = default;
            return false;
        }

        /// <summary>A host-built post (a CYBER package or a SOF team post): one fixed point, the maker the only contributor, never a SPACE rod.</summary>
        public static bool IsHostPost(SupportActionId action) => SofPosts.IsPost(action) || CyberPackages.TryOfAction(action, out _);

        public static TaskedDomain DomainOf(SupportActionId action) => TryGet(action, out TaskedKind kind) ? kind.Domain : TaskedDomain.Space;

        public static string Label(SupportActionId action) => TryGet(action, out TaskedKind kind) ? kind.Label : "TASKED";

        /// <summary>The 3-letter domain slab of a board row.</summary>
        public static string Slab(TaskedDomain domain) => domain == TaskedDomain.Cyber ? "CYB" : domain == TaskedDomain.Sof ? "SOF" : "SPC";
    }
}

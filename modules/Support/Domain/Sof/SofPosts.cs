using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Sof
{
    /// <summary>
    /// The two TASKED posts a SOF team makes (never CALL rows): COVER TEAM A-1 (a pinned team needs pilots to kill what is on it) and LASE (a team is
    /// holding a laser on a target). Both are free to claim (a service, not a strike); the post's one fixed point is the team's position, its mark id is
    /// the team slot + 1.
    /// </summary>
    internal static class SofPosts
    {
        public static bool IsPost(SupportActionId action) => action == SupportActionId.SofCover || action == SupportActionId.SofLase;

        public static string Label(SupportActionId action) => action == SupportActionId.SofCover ? "COVER TEAM" : "LASE TARGET";
    }
}

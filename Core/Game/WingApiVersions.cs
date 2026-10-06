using System;

namespace BoscaliSummer.Core.Game
{
    /// <summary>Wing Command public API versions Boscali can drive. Pure so the test runner links it.</summary>
    internal static class WingApiVersions
    {
        /// <summary>
        /// Wing membership. Wing Command 1.0 publishes the wing on the presence board every tick,
        /// and a published board is authoritative. Wing Command 0.9.x publishes no board, so an
        /// empty board falls back to its membership API (a cached delegate: no per-call allocation).
        /// </summary>
        internal static bool IsWingMember(int[] boardIds, Func<int, bool> apiContains, int persistentIdHash)
        {
            if (boardIds != null && boardIds.Length > 0)
            {
                for (int i = 0; i < boardIds.Length; i++)
                    if (boardIds[i] == persistentIdHash) return true;
                return false;
            }
            return apiContains != null && apiContains(persistentIdHash);
        }
    }
}

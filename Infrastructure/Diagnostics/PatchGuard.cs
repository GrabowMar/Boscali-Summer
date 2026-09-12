using System;
using System.Collections.Generic;

namespace BoscaliSummer.Infrastructure.Diagnostics
{
    /// <summary>
    /// Failure sink for a Harmony callback body. Harmony does not catch an exception thrown
    /// from a prefix or postfix at call time, so a null-ref inside a per-bullet or
    /// per-AI-tick patch propagates straight into the game loop it is attached to and throws
    /// again on the next frame. A guarded body catches, reports once, and lets the frame
    /// continue as vanilla.
    ///
    /// <para>The first failure at a call site logs; later failures at the same site are
    /// silent. The goal is to keep the mission running, not to fill the log with the same
    /// stack trace sixty times a second.</para>
    /// </summary>
    internal static class PatchGuard
    {
        private static readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Records a caught patch-body failure. Call from the <c>catch</c> of a patch that
        /// wraps its own body, so the happy path allocates nothing.
        /// </summary>
        public static void Report(string site, Exception error)
        {
            bool first;
            lock (reported) first = reported.Add(site);
            if (first)
                Plugin.Logger?.LogWarning(
                    "[" + site + "] patch body failed; further reports from this site are suppressed: " + error);
        }
    }
}

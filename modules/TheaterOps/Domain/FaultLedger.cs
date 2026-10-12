using System;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>Counts consecutive staff faults, spaces the retries and decides when a fault is worth a log line (annex 01 E10).</summary>
    internal sealed class FaultLedger
    {
        internal const int UnhealthyAt = 3;
        internal const float LogGapSeconds = 60f;
        private float lastLogAt;
        private bool logged;

        internal int Faults { get; private set; }
        internal bool Healthy => Faults < UnhealthyAt;

        /// <summary>The wait before the next review: reviewSeconds times the fault count, between 1 and 4 (10, 20, 30, 40, 40 ... for a 10 s review).</summary>
        internal float NextDelay(float reviewSeconds) => reviewSeconds * Math.Min(4, Math.Max(1, Faults));

        internal void Ok() { Faults = 0; }

        /// <summary>Counts a fault; logIt is true at most once per 60 s of the supplied clock (and again if that clock went backwards).</summary>
        internal void Fail(float now, out bool logIt)
        {
            if (Faults < int.MaxValue) Faults++;
            bool finite = !float.IsNaN(now) && !float.IsInfinity(now);
            logIt = !logged || finite && (now < lastLogAt || now - lastLogAt >= LogGapSeconds);
            if (!logIt) return;
            logged = true;
            if (finite) lastLogAt = now;
        }
    }

    /// <summary>Runs a body so that nothing it throws reaches the game.</summary>
    internal static class Guard
    {
        /// <summary>Runs body; on an exception records it on the ledger, hands log one line when due, and returns false. A clean pass resets the ledger's count and returns true.</summary>
        internal static bool Run(FaultLedger ledger, float now, Action body, Action<string> log, string where)
        {
            if (body == null) return true;
            try
            {
                body();
                if (ledger != null) ledger.Ok();
                return true;
            }
            catch (Exception error)
            {
                bool logIt = true;
                if (ledger != null) ledger.Fail(now, out logIt);
                if (logIt && log != null)
                {
                    try { log("[TheaterOps] " + where + " staff fault: " + error.Message + " (" + (ledger != null ? ledger.Faults : 1) + ")"); }
                    catch (Exception) { /* a broken log sink must not undo the containment */ }
                }
                return false;
            }
        }
    }
}

using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    internal struct PhaseInput
    {
        internal bool HaveReport, Steering;
        internal int Groups, Rally, Line, Assault, Contact, Withdraw;
        internal float NearestToFixMeters;               // -1 unknown
    }

    /// <summary>The operation's phase and progress, derived from what the ground groups report rather than from a timer (annex 01 E4).</summary>
    internal static class OpPhaseRules
    {
        internal const float HoldingRadius = 2500f;

        /// <summary>First match wins. With no report the phase is Awaiting and the note None (nothing is known, so nothing is blamed).</summary>
        internal static TheaterPhase Derive(TheaterOpKind kind, in PhaseInput r, out TheaterPhaseNote note)
        {
            note = TheaterPhaseNote.None;
            if (!r.HaveReport) return TheaterPhase.Awaiting;
            if (!r.Steering) { note = TheaterPhaseNote.NoSteering; return TheaterPhase.Awaiting; }
            if (r.Groups == 0) { note = TheaterPhaseNote.NoForces; return TheaterPhase.Awaiting; }
            if (r.Withdraw == r.Groups) return TheaterPhase.Withdrawing;
            if (r.Contact > 0) return TheaterPhase.InContact;
            switch (kind)
            {
                case TheaterOpKind.Defend:
                    return r.Rally + r.Line > 0 && r.NearestToFixMeters >= 0f && r.NearestToFixMeters <= HoldingRadius
                        ? TheaterPhase.Holding : TheaterPhase.Forming;
                case TheaterOpKind.Recon:
                    return r.Line + r.Assault > 0 ? TheaterPhase.Scouting : TheaterPhase.Forming;
                default:
                    return r.Assault > 0 ? TheaterPhase.Advancing : TheaterPhase.Forming;
            }
        }

        /// <summary>0..1 for the lane bar, -1 when it cannot be measured: sector ASSAULT is the gain toward the hold level, DEFEND the quiet time over ClearSeconds, RECON observed or not.</summary>
        internal static float Progress(TheaterOpKind kind, FrontSource source, bool hasHold, float holdNow, float initial, float clearSeconds, bool observed)
        {
            switch (kind)
            {
                case TheaterOpKind.Assault:
                {
                    if (source != FrontSource.Sector || !hasHold || float.IsNaN(holdNow) || float.IsNaN(initial)) return -1f;
                    float span = OutcomeRules.GainHold - initial;
                    return span > 0.0001f ? Clamp01((holdNow - initial) / span) : -1f;
                }
                case TheaterOpKind.Defend:
                    return float.IsNaN(clearSeconds) || float.IsInfinity(clearSeconds) ? -1f : Clamp01(clearSeconds / OutcomeRules.ClearSeconds);
                case TheaterOpKind.Recon:
                    return observed ? 1f : 0f;
                default:
                    return -1f;
            }
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}

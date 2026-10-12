using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>What kind of front an operation is aimed at: a contour sector, a mission objective or an airbase of ours.</summary>
    internal enum FrontSource : byte { Sector = 0, Objective = 1, Base = 2 }

    /// <summary>How long a condition has been continuously true. Advanced by the review; default(Sustained) is "not running".</summary>
    internal struct Sustained
    {
        internal float Since;      // the mission second the run began; -1 when not running
        internal bool Running;     // default is false, so a freshly zeroed field never claims to have been true since second 0

        /// <summary>Seconds the condition has held (0 when false and on the first true). A non-finite clock changes nothing and reads 0; a clock that went backwards reads 0.</summary>
        internal float Update(bool condition, float now)
        {
            if (float.IsNaN(now) || float.IsInfinity(now)) return 0f;
            if (!condition) { Reset(); return 0f; }
            if (!Running) { Running = true; Since = now; }
            return now > Since ? now - Since : 0f;
        }

        internal void Reset() { Running = false; Since = -1f; }
    }

    internal struct OutcomeInput
    {
        internal TheaterOpKind Kind; internal FrontSource Source;
        // Found: the front has a read this review. Live: that read says the contour is still there. Both are needed to trust Observed, the counts and the hold.
        internal bool Found, Live, Steering, HaveReport, HasHold, CarrierBased, Held, Observed;
        internal bool ObjectiveSecured, ObjectiveClosed, LostDefense;
        // The five timers are Sustained values advanced by the review and passed in as seconds; the rules never read a clock.
        internal float Elapsed, HoldAtOrigin, InitialHold, GainSeconds, LossSeconds, ClearSeconds, NoForcesSeconds, NotLiveSeconds;
        internal int NearFriendly, HostileNear, ReachFriendlyGround, ReachFriendly, InitialGround, Groups;
    }

    internal readonly struct OpEnd { internal readonly TheaterOutcome Outcome; internal readonly TheaterWhy Why; internal OpEnd(TheaterOutcome o, TheaterWhy w) { Outcome = o; Why = w; } }

    /// <summary>The conditions that end an operation, tested in the order the annex lists them (01 E5). Outcome None means keep running.</summary>
    internal static class OutcomeRules
    {
        // Sector ASSAULT thresholds on the control field at the origin fix, and the timers that make a reading count.
        internal const float GainHold = 0.35f, GainDelta = 0.25f, LossDelta = 0.35f, SustainSeconds = 20f, ClearSeconds = 30f,
            NoForcesSeconds = 120f, MinimumElapsedSeconds = 45f, LiveFrontGraceSeconds = 60f;

        /// <summary>hold &gt;= GainHold and hold - initial &gt;= GainDelta; false without a reading.</summary>
        internal static bool GainHolds(bool hasHold, float hold, float initial) => hasHold && hold >= GainHold && hold - initial >= GainDelta;

        /// <summary>hold &lt;= initial - LossDelta; false without a reading.</summary>
        internal static bool LossHolds(bool hasHold, float hold, float initial) => hasHold && hold <= initial - LossDelta;

        internal static OpEnd Evaluate(in OutcomeInput i)
        {
            bool assault = i.Kind == TheaterOpKind.Assault, defend = i.Kind == TheaterOpKind.Defend, recon = i.Kind == TheaterOpKind.Recon;

            // 1. The mission's own word on an objective.
            if (i.Source == FrontSource.Objective)
            {
                if (i.ObjectiveSecured) return new OpEnd(TheaterOutcome.Secured, TheaterWhy.ObjectiveCaptured);
                if (i.ObjectiveClosed) return new OpEnd(TheaterOutcome.Repulsed, TheaterWhy.ObjectiveLost);
            }

            // 2. A lost defence, whatever the read says (the caller already folded a vanished place into it).
            if (defend && i.LostDefense)
                return new OpEnd(TheaterOutcome.Repulsed, i.Source == FrontSource.Base ? TheaterWhy.BaseLost
                    : i.Source == FrontSource.Objective ? TheaterWhy.ObjectiveLost : TheaterWhy.FrontPushedBack);

            // 2-5. Everything below reads the front, so it needs a live read.
            if (i.Found && i.Live)
            {
                bool late = i.Elapsed > MinimumElapsedSeconds;
                if (defend)
                {
                    bool presence = i.Source == FrontSource.Base || i.NearFriendly > 0;
                    if (i.Held && presence && i.HostileNear == 0 && i.ClearSeconds >= ClearSeconds && late)
                        return new OpEnd(TheaterOutcome.Secured, TheaterWhy.HeldClear);
                }
                else if (recon)
                {
                    if (i.Observed && late) return new OpEnd(TheaterOutcome.Secured, TheaterWhy.Observed);
                }
                else if (assault && i.Source == FrontSource.Sector && i.HasHold)
                {
                    if (i.GainSeconds >= SustainSeconds) return new OpEnd(TheaterOutcome.Secured, TheaterWhy.FrontAdvanced);
                    if (i.LossSeconds >= SustainSeconds) return new OpEnd(TheaterOutcome.Repulsed, TheaterWhy.FrontPushedBack);
                }

                // 5. The force is gone.
                if (late)
                {
                    if ((assault || recon) && i.InitialGround > 0 && i.ReachFriendlyGround == 0)
                        return new OpEnd(TheaterOutcome.Repulsed, TheaterWhy.ForcesLost);
                    if (defend && i.Source != FrontSource.Base && i.ReachFriendly == 0)
                        return new OpEnd(TheaterOutcome.Repulsed, TheaterWhy.ForcesLost);
                }
            }

            // 6. Steering is on, the ground report exists, a carrier was there to take the order and no group ever formed.
            if (i.HaveReport && i.Steering && i.CarrierBased && i.Groups == 0 && i.NoForcesSeconds >= NoForcesSeconds)
                return new OpEnd(TheaterOutcome.Stalled, TheaterWhy.NoForces);

            // 7. The front stopped being sensed for the whole grace.
            if (i.NotLiveSeconds >= LiveFrontGraceSeconds)
                return assault && i.Source == FrontSource.Sector && GainHolds(i.HasHold, i.HoldAtOrigin, i.InitialHold)
                    ? new OpEnd(TheaterOutcome.Secured, TheaterWhy.FrontAdvanced)
                    : new OpEnd(TheaterOutcome.Stalled, TheaterWhy.FrontVanished);

            // 8. The ceiling.
            if (i.Elapsed >= LivingWarRules.OperationLimitSeconds) return new OpEnd(TheaterOutcome.Stalled, TheaterWhy.TimeLimit);

            return default;
        }
    }

    internal readonly struct Reward { internal readonly int Funds, Morale; internal Reward(int funds, int morale) { Funds = funds; Morale = morale; } }

    internal static class OutcomeRewards
    {
        /// <summary>Only a SECURED outcome for a faction with a human pays (D5): funds and morale, never score. Every other input is 0/0.</summary>
        internal static Reward For(TheaterOpKind kind, TheaterOutcome outcome, bool hasHuman)
        {
            if (outcome != TheaterOutcome.Secured || !hasHuman) return default;
            switch (kind)
            {
                case TheaterOpKind.Assault: return new Reward(150, 4);
                case TheaterOpKind.Defend: return new Reward(100, 3);
                case TheaterOpKind.Recon: return new Reward(40, 1);
                default: return default;
            }
        }
    }
}

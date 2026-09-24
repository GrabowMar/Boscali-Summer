using System;
using System.Globalization;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Command.Domain
{
    /// <summary>
    /// The arithmetic and copy behind the strategic readouts, kept free of Unity so it can
    /// be tested without a game install.
    ///
    /// <para>Formatting lives here rather than at each label because the same figure is
    /// written from several pages, and "58%" and "58.0%" appearing on adjacent rows is the
    /// kind of thing that makes an instrument look approximate.</para>
    /// </summary>
    internal static class TheaterReadout
    {
        /// <summary>
        /// The share of the board each side holds, as three fractions that sum to at most 1.
        ///
        /// <para>Contested ground is its own band rather than a shortfall in someone's bar.
        /// A single friendly-versus-total fill cannot distinguish "we hold 40%, they hold
        /// 60%" from "we hold 40%, they hold 40%, and 20% is being fought over" — which are
        /// opposite situations to fly into.</para>
        /// </summary>
        public static void Shares(
            int friendly, int contested, int hostile, int neutral,
            out float friendlyShare, out float contestedShare, out float hostileShare)
        {
            int total = Math.Max(0, friendly) + Math.Max(0, contested)
                      + Math.Max(0, hostile) + Math.Max(0, neutral);

            if (total <= 0)
            {
                friendlyShare = contestedShare = hostileShare = 0f;
                return;
            }

            friendlyShare = Math.Max(0, friendly) / (float)total;
            contestedShare = Math.Max(0, contested) / (float)total;
            hostileShare = Math.Max(0, hostile) / (float)total;
        }

        /// <summary>A ratio as whole percent. NaN and infinity read as an unknown dash.</summary>
        public static string Percent(float ratio)
        {
            if (float.IsNaN(ratio) || float.IsInfinity(ratio)) return "—";
            float clamped = ratio < 0f ? 0f : ratio > 1f ? 1f : ratio;
            return ((int)Math.Round(clamped * 100f, MidpointRounding.AwayFromZero))
                   .ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>A length in metres as kilometres, one decimal. The front's honest figure.</summary>
        public static string Kilometres(float metres)
        {
            if (float.IsNaN(metres) || float.IsInfinity(metres)) return "—";
            return (Math.Max(0f, metres) / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";
        }

        /// <summary>
        /// Seconds since an event as a short age stamp. The log needs "how long ago" at a
        /// glance, so the unit changes with the magnitude rather than printing 738 seconds.
        /// </summary>
        public static string Age(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return "—";
            int value = (int)Math.Round(Math.Max(0f, seconds), MidpointRounding.AwayFromZero);
            if (value < 60) return value.ToString(CultureInfo.InvariantCulture) + "s";
            if (value < 3600) return (value / 60).ToString(CultureInfo.InvariantCulture) + "m";
            return (value / 3600).ToString(CultureInfo.InvariantCulture) + "h";
        }

        /// <summary>
        /// How a DEFCON level reads as a rail state, so severity is carried by position on
        /// the scale rather than by a colour the caller picked.
        /// </summary>
        public static string DefconRail(int level)
        {
            if (level <= 1) return "danger";
            if (level == 2) return "contested";
            if (level == 3) return "armed";
            return "ready";
        }

        /// <summary>
        /// A node's rail: who holds it, and whether that hold is currently being argued with.
        /// </summary>
        public static string NodeRail(bool friendly, bool contested)
        {
            if (contested) return "contested";
            return friendly ? "ready" : "hostile";
        }

        /// <summary>
        /// Pressure as a word, for rows whose figure column already carries the number.
        /// Below the noise floor it is not pressure, it is a rounding artefact, and calling
        /// it pressure would overstate what is known.
        /// </summary>
        public static string PressureState(float captureProgress)
        {
            if (float.IsNaN(captureProgress) || float.IsInfinity(captureProgress)) return "UNKNOWN";
            if (captureProgress < 0.05f) return "HOLDING";
            if (captureProgress >= 0.75f) return "FALLING";
            return "UNDER PRESSURE";
        }

        /// <summary>
        /// The state word an offensive wears, one word for the phase and, once it is over,
        /// the outcome. It is the reading the rail colour repeats, never the only carrier.
        /// </summary>
        public static string OffensivePhaseWord(TheaterOperationPhase phase, TheaterOperationOutcome outcome)
        {
            switch (phase)
            {
                case TheaterOperationPhase.Mustering: return "MUSTERING";
                case TheaterOperationPhase.Planning: return "PLANNING";
                case TheaterOperationPhase.AwaitingTarget: return "AWAITING TARGET";
                case TheaterOperationPhase.Launching: return "H-HOUR";
                case TheaterOperationPhase.Assault: return "ASSAULT";
                case TheaterOperationPhase.Holding: return "HOLDING";
                default: return OffensiveOutcomeWord(outcome);
            }
        }

        /// <summary>
        /// The stage an offensive has reached on the staff's own timeline, 0..4. Gathering,
        /// planning, the target, H-hour and the push itself are the five things that happen,
        /// and the board draws them as one strip so a plan that runs itself still reads as a
        /// sequence rather than a single progress bar.
        /// </summary>
        public static int OffensiveStage(TheaterOperationPhase phase)
        {
            switch (phase)
            {
                case TheaterOperationPhase.Mustering: return 0;
                case TheaterOperationPhase.Planning: return 1;
                case TheaterOperationPhase.AwaitingTarget: return 2;
                case TheaterOperationPhase.Launching: return 3;
                default: return 4;
            }
        }

        /// <summary>The five stage captions the strip is drawn with, in order.</summary>
        public static readonly string[] OffensiveStages =
        {
            "MUSTER", "PLAN", "TARGET", "H-HOUR", "PUSH",
        };

        /// <summary>
        /// A duration as clock time, minutes and seconds. A battle report says how long the
        /// push ran, and "7m" cannot say whether a fight lasted 7:02 or 7:58.
        /// </summary>
        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return "—";
            int total = (int)Math.Round(Math.Max(0f, seconds), MidpointRounding.AwayFromZero);
            return (total / 60).ToString(CultureInfo.InvariantCulture) + ":" +
                   (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>How a concluded offensive reads in its state word.</summary>
        public static string OffensiveOutcomeWord(TheaterOperationOutcome outcome)
        {
            switch (outcome)
            {
                case TheaterOperationOutcome.ObjectiveSecured: return "SECURED";
                case TheaterOperationOutcome.ObjectiveLost: return "OBJECTIVE CLOSED";
                case TheaterOperationOutcome.Stalled: return "STALLED";
                case TheaterOperationOutcome.CommitmentSpent: return "COMMITMENT SPENT";
                case TheaterOperationOutcome.Cancelled: return "CANCELLED";
                default: return "CONCLUDED";
            }
        }

        /// <summary>
        /// The rail state for an offensive: gathering and planning are armed, awaiting a
        /// target wants the commander's attention, H-hour is imminent, the assault is running,
        /// a hold needs a decision, and only a secured objective reads as nominal; a stalled
        /// or lost one reads danger.
        /// </summary>
        public static string OffensiveRail(TheaterOperationPhase phase, TheaterOperationOutcome outcome)
        {
            switch (phase)
            {
                case TheaterOperationPhase.Mustering:
                case TheaterOperationPhase.Planning: return "armed";
                case TheaterOperationPhase.AwaitingTarget: return "contested";
                case TheaterOperationPhase.Launching: return "info";
                case TheaterOperationPhase.Holding: return "cooling";
                case TheaterOperationPhase.Assault: return "ready";
                default:
                    switch (outcome)
                    {
                        case TheaterOperationOutcome.ObjectiveSecured: return "ready";
                        case TheaterOperationOutcome.ObjectiveLost:
                        case TheaterOperationOutcome.Stalled: return "danger";
                        default: return "locked";
                    }
            }
        }
    }
}

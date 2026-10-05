using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Support.Domain.Ops
{
    /// <summary>The words of OPERATIONS (one radio voice: <c>NEGATIVE: &lt;reason&gt; — &lt;what fixes it&gt;</c>). Pure.</summary>
    internal static class OpsWords
    {
        public const byte MaxOutcome = (byte)OpOutcome.Offline;

        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) seconds = 0f;
            int s = (int)Math.Ceiling(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>The full name an operator reads on the box header.</summary>
        public static string Name(OpKind kind) =>
            kind == OpKind.Asat ? "DECRYPT SATELLITE TRACK" : kind == OpKind.ZeroDay ? "ZERO-DAY: SAM NET FAIL" : kind == OpKind.Fob ? "FORWARD OPERATING BASE" : "NO OPERATION";

        /// <summary>The short tag for buttons and console lines.</summary>
        public static string Short(OpKind kind) => kind == OpKind.Asat ? "ASAT" : kind == OpKind.ZeroDay ? "ZERO-DAY" : kind == OpKind.Fob ? "FOB" : "NONE";

        public static string Bird(int id) => id == 0 ? "OPTICAL" : id == 1 ? "RADAR" : id == 2 ? "KINETIC" : "UNKNOWN";

        /// <summary>What the pick control reads: the bird, the SAM cluster number or the held building number.</summary>
        public static string TargetWord(OpKind kind, int id) =>
            kind == OpKind.Asat ? Bird(id) : kind == OpKind.ZeroDay ? "SAM NET " + id : kind == OpKind.Fob ? "HELD H" + id : "NONE";

        /// <summary>The state word; the countdown reads <c>EXECUTE T-45</c>.</summary>
        public static string State(OpState state, float countdownLeft)
        {
            switch (state)
            {
                case OpState.Funding: return "FUNDING";
                case OpState.NeedsFunding: return "NEEDS FUNDING";
                case OpState.Execute: return "EXECUTE T-" + Math.Max(0, (int)Math.Ceiling(countdownLeft));
                case OpState.Done: return "DONE";
                case OpState.Broken: return "BROKEN";
                default: return "IDLE";
            }
        }

        public static string Of(OpOutcome outcome, int detail = 0)
        {
            switch (outcome)
            {
                case OpOutcome.None: return "";
                case OpOutcome.Started: return "OPERATION STARTED — FUND THE BAR (GOAL " + Math.Max(0, detail) + " CR)";
                case OpOutcome.Funded: return "FUNDED — BAR " + Math.Max(0, detail) + " %";
                case OpOutcome.Retargeted: return "TARGET CHANGED";
                case OpOutcome.Cancelled: return "OPERATION CANCELLED — FUNDS RETURNED";
                case OpOutcome.NoTarget: return "NEGATIVE: NO SUCH TARGET — PICK ONE FROM THE LIST";
                case OpOutcome.NoOperation: return "NEGATIVE: NO OPERATION RUNNING — START ONE FIRST";
                case OpOutcome.BadAmount: return "NEGATIVE: FUND TAPS ARE 25 OR 50 CR";
                case OpOutcome.Busy: return "NEGATIVE: ANOTHER OPERATION IS RUNNING — FINISH OR CANCEL IT";
                case OpOutcome.Cooldown: return "NEGATIVE: OPERATION COOLING — READY IN " + Clock(detail);
                case OpOutcome.AnchorDown: return "NEGATIVE: ANCHOR DOWN — RESTORE IT TO CONTINUE";
                case OpOutcome.Executing: return "NEGATIVE: COUNTDOWN RUNNING — PROTECT THE ANCHOR";
                case OpOutcome.PlayerCap: return "NEGATIVE: YOUR SHARE IS CAPPED AT " + (detail > 0 ? detail : 30) + " % — LET OTHERS FUND";
                case OpOutcome.LowCredit: return "NEGATIVE: LOW CREDIT — NEED " + Math.Max(0, detail) + " CR";
                case OpOutcome.Frozen: return "NEGATIVE: CREDIT FROZEN — STAND BY";
                case OpOutcome.RateLimited: return "NEGATIVE: RATE LIMITED — SLOW DOWN";
                case OpOutcome.NotOwner: return "NEGATIVE: NOT YOUR OPERATION — ONLY ITS OWNER CHANGES OR CANCELS IT";
                case OpOutcome.Unavailable: return "NEGATIVE: OPERATIONS UNAVAILABLE — TRY AGAIN";
                default: return "NEGATIVE: OPERATIONS OFFLINE — NO CYBER OR SOF ASSETS";
            }
        }

        /// <summary>One line of what the operation does and what stops it (the box hint on the tall pages).</summary>
        public static string Hint(OpKind kind)
        {
            switch (kind)
            {
                case OpKind.Asat: return "KILLS ONE ENEMY SATELLITE · BREAKS ON DATA CENTER OR LAUNCHER LOSS";
                case OpKind.ZeroDay: return "ONE SAM NET CANNOT LAUNCH FOR 3 MIN · HALF NEAR AN ENEMY EW TRUCK";
                case OpKind.Fob: return "HELD BUILDING BECOMES A FOB 20 MIN: +1 TEAM, RAISE, REARM, REFUEL";
                default: return "";
            }
        }

        /// <summary>What an operator must do before the operation can start (no valid target in the list).</summary>
        public static string NeedTarget(OpKind kind) =>
            kind == OpKind.ZeroDay ? "REVEAL A SAM SITE FIRST (HOP TO IT OR SCAN IT)" : kind == OpKind.Fob ? "SEIZE A BUILDING FIRST (SOF SEIZE)" : "PICK A SATELLITE";

        /// <summary>The anchor line shown while an operation is paused: what to protect or restore.</summary>
        public static string Anchor(OpKind kind) =>
            kind == OpKind.Fob ? "HELD BUILDING" : "DATA CENTER";

        /// <summary>What an enemy is told. <paramref name="name"/> is the attacking faction.</summary>
        public static string Ping(OpKind kind, OpPingPhase phase, string name, int detail = 0)
        {
            string who = string.IsNullOrWhiteSpace(name) ? "ENEMY" : name.Trim().ToUpperInvariant();
            switch (kind)
            {
                case OpKind.Asat:
                    return phase == OpPingPhase.Half ? who + " IS DECRYPTING YOUR SATELLITE TRACK" :
                        phase == OpPingPhase.Execute ? "ASAT STRIKE COUNTDOWN — " + who + " LAUNCHER IS UP" :
                        phase == OpPingPhase.Launch ? "ASAT LAUNCH DETECTED" : "SAT LOST: " + Bird(detail);
                case OpKind.ZeroDay:
                    return phase == OpPingPhase.Half ? who + " IS PROBING YOUR SAM NET" : "SAM NET FAILURE IMMINENT — " + who + " ZERO-DAY";
                case OpKind.Fob:
                    return phase == OpPingPhase.Half ? who + " IS PREPARING A FORWARD BASE" : who + " FORWARD BASE GOING LIVE";
                default:
                    return "ENEMY OPERATION";
            }
        }

        /// <summary>The own-faction console line of an event.</summary>
        public static string Event(OpEventKind kind, OpKind op)
        {
            string tag = Short(op);
            switch (kind)
            {
                case OpEventKind.Started: return tag + " OPERATION STARTED";
                case OpEventKind.Half: return tag + " BAR PAST 50 % — ENEMY MAY HAVE NOTICED";
                case OpEventKind.Execute: return tag + " EXECUTE T-60 — PROTECT THE ANCHOR";
                case OpEventKind.Fired: return tag + " EXECUTED";
                case OpEventKind.Broken: return tag + " BROKEN — HALF RETURNED, RESTART AT 50 %";
                case OpEventKind.CounterTrace: return tag + " COUNTER-TRACED — BAR -10 %";
                case OpEventKind.Stalled: return tag + " NEEDS FUNDING";
                case OpEventKind.Resumed: return tag + " FUNDING RESUMED";
                case OpEventKind.Cancelled: return tag + " CANCELLED";
                case OpEventKind.Retargeted: return tag + " TARGET CHANGED";
                default: return tag + " COMPLETE";
            }
        }
    }
}

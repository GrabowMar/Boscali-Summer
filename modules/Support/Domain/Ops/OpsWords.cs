using System;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Ops
{
    /// <summary>The words of OPERATIONS (one radio voice: <c>NEGATIVE: &lt;reason&gt; — &lt;what fixes it&gt;</c>). Pure.</summary>
    internal static class OpsWords
    {
        public const byte MaxOutcome = (byte)OpOutcome.Offline;

        /// <summary>The short tag for buttons and console lines.</summary>
        public static string Short(OpKind kind) => kind == OpKind.Asat ? "ASAT" : kind == OpKind.ZeroDay ? "ZERO-DAY" : kind == OpKind.Fob ? "FOB" : "NONE";

        public static string Bird(int id) => id == 0 ? "OPTICAL" : id == 1 ? "RADAR" : id == 2 ? "KINETIC" : "UNKNOWN";

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
                case OpOutcome.Cooldown: return "NEGATIVE: OPERATION COOLING — READY IN " + SpaceRules.Clock(detail);
                case OpOutcome.AnchorDown: return "NEGATIVE: ANCHOR DOWN — RESTORE IT TO CONTINUE";
                case OpOutcome.Executing: return "NEGATIVE: COUNTDOWN RUNNING — PROTECT THE ANCHOR";
                case OpOutcome.PlayerCap: return "NEGATIVE: YOUR SHARE IS CAPPED AT " + (detail > 0 ? detail : 30) + " % — LET OTHERS FUND";
                case OpOutcome.LowCredit: return CallWords.Refusal(CallRefusal.LowCredit, need: Math.Max(0, detail));
                case OpOutcome.Frozen: return "NEGATIVE: CREDIT FROZEN — STAND BY";
                case OpOutcome.RateLimited: return "NEGATIVE: RATE LIMITED — SLOW DOWN";
                case OpOutcome.NotOwner: return "NEGATIVE: NOT YOUR OPERATION — ONLY ITS OWNER CHANGES OR CANCELS IT";
                case OpOutcome.Unavailable: return "NEGATIVE: OPERATIONS UNAVAILABLE — TRY AGAIN";
                default: return "NEGATIVE: OPERATIONS OFFLINE — NO CYBER OR SOF ASSETS";
            }
        }

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

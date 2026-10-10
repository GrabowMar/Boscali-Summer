using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>How the WING page words a wingman (HUD v8): one state out of what its snapshot entry says, and the ack log's words. Pure.</summary>
    internal static class BoardRules
    {
        /// <summary>A formation member further from its slot than this reads JOIN (the WING panel's own in-slot limit).</summary>
        public const float OutOfSlotM = 60f;

        /// <summary>
        /// The state word's rule, most urgent first: a missile on it (DEFEND), fighting (ENGAGE), going home (RTB, or REFIT to rearm), out of weapons (WINCH),
        /// on the ground or landing, then in formation: JOIN when it is falling behind or out of its slot, else FORM. A hit is not a state: the damage flag is latched until the
        /// hull is mended (a wingman that ever lost a part would read DAMAGED for the rest of the sortie and hide what it is doing), so the page words it for a few seconds
        /// after the hit (<see cref="WingBoardMember.Damaged"/>) and keeps it as an amber suffix after that.
        /// </summary>
        public static WingState State(bool defending, bool recovering, bool refit, bool engaged, bool dry, bool grounded, bool settled, bool behind, float slotErrorM)
        {
            if (defending) return WingState.Defend;
            if (engaged) return WingState.Engage;
            if (recovering) return refit ? WingState.Refit : WingState.Rtb;
            if (dry) return WingState.Winch;
            if (grounded) return WingState.Ground;
            if (settled) return WingState.Land;
            return behind || slotErrorM > OutOfSlotM ? WingState.Join : WingState.Form;
        }

        /// <summary>The ack log's line: <c>REJOIN - WILCO</c>, or the refusal's reason with <c>UNABLE</c>.</summary>
        public static string AckText(bool accepted, string what, string reason)
        {
            if (accepted) return (string.IsNullOrWhiteSpace(what) ? "ORDER" : what.Trim().ToUpperInvariant()) + " - WILCO";
            return (string.IsNullOrWhiteSpace(reason) ? "REFUSED" : reason.Trim().ToUpperInvariant()) + " - UNABLE";
        }

        /// <summary>A snapshot fraction byte (0..255) as 0..1.</summary>
        public static float Fraction(byte b) => b / 255f;
    }
}

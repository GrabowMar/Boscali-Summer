using System.Globalization;

using BoscaliSummer.Modules.Wing.Networking;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>The SQUADRON tab's words for a member, from its snapshot entry (spec M7b §3): the host and a client read the
    /// same entry.</summary>
    internal static class WingRows
    {
        /// <summary>"#2" for slot 0 (the leader is #1), as the HUD numbers members.</summary>
        public static string Number(int slot) => "#" + (slot + 2).ToString(CultureInfo.InvariantCulture);

        /// <summary>FIGHT, RTB, DEFEND, GROUND or LANDED by duty; in formation the HUD phase (JOIN, SLOT, HOLD, TRAIL,
        /// BEHIND).</summary>
        public static string State(in SnapshotMember m)
        {
            switch ((MemberDuty)m.Duty)
            {
                case MemberDuty.Engaged: return "FIGHT";
                case MemberDuty.Recovering: return "RTB";
                case MemberDuty.Defending: return "DEFEND";
                case MemberDuty.Grounded: return "GROUND";
                case MemberDuty.Settled: return "LANDED";
                default:
                    return WingHudText.Phase((BehaviourId)m.Behaviour, (m.Flags & (byte)SnapshotFlags.FallingBehind) != 0);
            }
        }

        /// <summary>A snapshot fraction byte (0..255) as 0..1.</summary>
        public static float Fraction(byte b) => b / 255f;

        /// <summary>The row of the aircraft with persistent id <paramref name="id"/>, or -1 (0 is no aircraft). Slots
        /// renumber when a member leaves; the id does not (review M7b-1 I2).</summary>
        public static int IndexOf(SnapshotMember[] rows, int count, uint id)
        {
            if (id == 0u) return -1;
            for (int i = 0; i < count; i++)
                if (rows[i].Id == id) return i;
            return -1;
        }
    }
}

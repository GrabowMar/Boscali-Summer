using BoscaliSummer.Modules.Wing.Networking;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>TACTICAL v3's member row words (spec tactical v3 §2.2): the second line and the fuel and ammo tape classes.</summary>
    internal static class MemberLine
    {
        /// <summary>"ATK SU-27" while engaged on a known target (host), "FORM · SLOT" in formation, else the state word.</summary>
        public static string Task(in SnapshotMember m, string targetCode)
        {
            var duty = (MemberDuty)m.Duty;
            if (duty == MemberDuty.Engaged && !string.IsNullOrEmpty(targetCode)) return "ATK " + targetCode;
            string state = WingRows.State(m);
            return duty == MemberDuty.Formation ? "FORM · " + state : state;
        }

        /// <summary>The fuel tape's rail class: danger at bingo, armed at joker, else ready.</summary>
        public static string FuelRail(byte flags)
        {
            var f = (SnapshotFlags)flags;
            return (f & SnapshotFlags.Bingo) != 0 ? "danger" : (f & SnapshotFlags.Joker) != 0 ? "armed" : "ready";
        }
    }

    /// <summary>TACTICAL v3's alert stack (spec tactical v3 §2.1): which line each row shows.</summary>
    internal static class AlertStack
    {
        public const int Cue = -2, Empty = -1;

        /// <summary>Fills <paramref name="into"/>[0..rows): <see cref="Cue"/> first while an order is armed, then alert indices, then
        /// <see cref="Empty"/>.</summary>
        public static void Lines(int alertCount, bool armed, int rows, int[] into)
        {
            int a = 0;
            for (int r = 0; r < rows && r < into.Length; r++)
            {
                if (r == 0 && armed) into[r] = Cue;
                else into[r] = a < alertCount ? a++ : Empty;
            }
        }
    }
}

namespace BoscaliSummer.Modules.Wing.Domain
{
}

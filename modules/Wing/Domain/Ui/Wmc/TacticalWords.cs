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
}

namespace BoscaliSummer.Modules.Wing.Domain
{
}

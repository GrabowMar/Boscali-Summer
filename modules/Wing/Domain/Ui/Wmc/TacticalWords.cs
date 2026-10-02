using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
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

        /// <summary>The ammo tape's rail class: danger at winchester, else info.</summary>
        public static string AmmoRail(byte flags) => ((SnapshotFlags)flags & SnapshotFlags.Winchester) != 0 ? "danger" : "info";
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
    /// <summary>TUNING's OVERRIDES (spec tactical v3 §5): how a doctrine differs from the one it would inherit, on the axes the AI
    /// reads (targets, range, weapons, radar); guard, response, interval and spread are not wired yet, so they are never listed.</summary>
    internal static class DoctrineDiff
    {
        private static readonly DoctrineAxis[] Axes = { DoctrineAxis.Targets, DoctrineAxis.Reach, DoctrineAxis.Weapons, DoctrineAxis.Radar };
        private static readonly string[] AxisWords = { "TARGETS", "RANGE", "WEAPONS", "RADAR" };

        /// <summary>"TARGETS AIR · RADAR SILENT"; empty when the two agree.</summary>
        public static string Words(WingDoctrine d, WingDoctrine inherited)
        {
            string s = "";
            for (int a = 0; a < Axes.Length; a++)
            {
                DoctrineAxis axis = Axes[a];
                byte v = d.Get(axis);
                if (v == inherited.Get(axis)) continue;
                string word = WingDoctrine.ValueName(axis, v);
                s += (s.Length > 0 ? " · " : "") + AxisWords[a] + " " + (word ?? "?").ToUpperInvariant();
            }
            return s;
        }
    }
}

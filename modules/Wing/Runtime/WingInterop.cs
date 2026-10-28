using System.Collections.Generic;
using NOAvionics;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>In-tree presence marker: what used to identify the standalone plugin.</summary>
    internal static class WingPresence
    {
        internal const string Guid = "com.marci.wingcommand";
    }

    /// <summary>The recruited wing's live roster, keyed by
    /// <c>Aircraft.persistentID.GetHashCode()</c>. Backed by the in-process wing (WingService
    /// calls <see cref="Publish"/> on every roster change). Boscali systems read the published
    /// board so theater doctrine and sortie tallies leave the player's wingmen alone.</summary>
    internal static class WingMembership
    {
        /// <summary>Refresh the published roster from the live wing on every roster change. Also publishes it on
        /// the cross-mod PresenceBoard (<c>NO.Wing.ids.v1</c>, <c>NO.Wing.guid.v1</c>).</summary>
        internal static void Publish(List<WingMember> members)
        {
            int n = members?.Count ?? 0;
            var snapshot = new int[n];
            for (int i = 0; i < n; i++)
            {
                Aircraft aircraft = members[i].Aircraft;
                snapshot[i] = aircraft == null ? 0 : aircraft.persistentID.GetHashCode();
            }
            PresenceBoard.SetString(PresenceBoard.WingGuid, WingPresence.Guid);
            PresenceBoard.SetInts(PresenceBoard.WingMemberIds, snapshot);
        }
    }
}

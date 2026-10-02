using System;
using System.Collections.Generic;
using NOAvionics;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
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
        private static readonly int[] Empty = Array.Empty<int>();
        private static int[] ids = Empty;
        private static int count;

        internal static int Count => count;

        internal static bool Contains(int persistentIdHash)
        {
            for (int i = 0; i < count; i++)
                if (ids[i] == persistentIdHash) return true;
            return false;
        }

        /// <summary>Refresh the published roster from the live wing on every roster change. Also publishes it on
        /// the cross-mod PresenceBoard (<c>NO.Wing.ids.v1</c>, <c>NO.Wing.guid.v1</c>).</summary>
        internal static void Publish(List<WingMember> members)
        {
            int n = members?.Count ?? 0;
            if (ids.Length < n) ids = new int[n];
            for (int i = 0; i < n; i++)
            {
                Aircraft aircraft = members[i].Aircraft;
                ids[i] = aircraft == null ? 0 : aircraft.persistentID.GetHashCode();
            }
            count = n;
            var snapshot = new int[n];
            Array.Copy(ids, snapshot, n);
            PresenceBoard.SetString(PresenceBoard.WingGuid, WingPresence.Guid);
            PresenceBoard.SetInts(PresenceBoard.WingMemberIds, snapshot);
        }

        internal static void Clear() => Publish(null);
    }
}

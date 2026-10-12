using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>What a peer shows for the staff, from facts it can see locally (annex 01 E3).</summary>
    internal static class ViewStateRules
    {
        internal const float WaitingSeconds = 12f;
        internal const int LacksModuleQueries = 2;
        internal const float CommandResultSeconds = 6f;

        /// <summary>StaffOff (local switch) > NoFaction > Live on the host > no snapshot: HostLacksModule (>= 2 unanswered queries and >= 12 s) else WaitingForHost > Stale (age outside 0..15) > Live.</summary>
        internal static TheaterViewState Decide(bool localEnabled, bool hasFaction, bool isServer, bool hasSnapshot, float ageSeconds,
            float secondsSinceFirstQuery, int unansweredQueries)
        {
            if (!localEnabled) return TheaterViewState.StaffOff;
            if (!hasFaction) return TheaterViewState.NoFaction;
            if (isServer) return TheaterViewState.Live;
            if (!hasSnapshot)
                return unansweredQueries >= LacksModuleQueries && secondsSinceFirstQuery >= WaitingSeconds
                    ? TheaterViewState.HostLacksModule : TheaterViewState.WaitingForHost;
            return ageSeconds >= 0f && ageSeconds <= LivingWarRules.SnapshotFreshSeconds ? TheaterViewState.Live : TheaterViewState.Stale;
        }

        /// <summary>True when the command result is 0..6 s old (unscaled clock). A stamp in the future or a non-finite value is not fresh.</summary>
        internal static bool CommandFresh(float nowUnscaled, float stampUnscaled)
        {
            float age = nowUnscaled - stampUnscaled;
            return age >= 0f && age <= CommandResultSeconds;
        }
    }

    /// <summary>The age of the staff's data on each side of the wire (annex 01 E8).</summary>
    internal static class SnapshotAge
    {
        /// <summary>Host age on the mission clock. -1 when sensedAt is unset (negative) or non-finite; never negative otherwise.</summary>
        internal static float Host(float missionNow, float sensedAt)
        {
            if (!IsFinite(missionNow) || !IsFinite(sensedAt) || sensedAt < 0f) return -1f;
            return Math.Max(0f, missionNow - sensedAt);
        }

        /// <summary>Client age: the host's age at send plus unscaled seconds since receipt. -1 when any input is non-finite; never negative otherwise.</summary>
        internal static float Client(float sensedAgeAtSend, float unscaledNow, float receivedAt)
        {
            if (!IsFinite(sensedAgeAtSend) || !IsFinite(unscaledNow) || !IsFinite(receivedAt)) return -1f;
            return Math.Max(0f, sensedAgeAtSend) + Math.Max(0f, unscaledNow - receivedAt);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

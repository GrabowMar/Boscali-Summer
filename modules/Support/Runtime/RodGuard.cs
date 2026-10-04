using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Live-game side of a rod's safety and warning rules: the friendly standoff over every friendly player aircraft and the
    /// native warning to the threatened factions. The arithmetic lives in <see cref="SpaceStandoff"/> and <see cref="RodTiming"/>.
    /// </summary>
    internal static class RodGuard
    {
        /// <summary>Flight time the warning is planned with; learns from rods that really landed.</summary>
        public static readonly RodFlightEstimate Flight = new RodFlightEstimate();

        public static float StandoffRadius() => SpaceStandoff.Radius(TheaterFrame.Resolve().magnitude);

        /// <summary>
        /// True when any friendly player aircraft is inside the scaled standoff of the impact point. Every player of the owner's
        /// faction counts, modded or not: the aircraft is a native object. An unreadable position fails closed.
        /// </summary>
        public static bool FriendlyNear(FactionHQ owner, GlobalPosition impact)
        {
            if (owner == null) return true;
            float radius = StandoffRadius();
            System.Collections.Generic.List<Player> players = owner.GetPlayers(false);
            for (int i = 0; i < players.Count; i++)
            {
                Aircraft aircraft = players[i] != null ? players[i].Aircraft : null;
                if (aircraft == null || aircraft.disabled) continue;
                GlobalPosition at = aircraft.transform.GlobalPosition();
                if (SpaceStandoff.Violates(impact.x, impact.z, radius, at.x, at.z)) return true;
            }
            return false;
        }

        /// <summary>
        /// Warns every other faction that has a player, through the native HQ radio message: vanilla clients play the radio cue
        /// and print it on the game-message line, so an unmodded peer is warned too. Returns the number of factions told.
        /// </summary>
        public static int Warn(FactionHQ owner, string text)
        {
            MessageManager messages = NetworkSceneSingleton<MessageManager>.i;
            if (messages == null || owner == null) return 0;
            int told = 0;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null || hq == owner || hq.GetPlayers(false).Count == 0) continue;
                try { messages.RpcHQMessage(hq, text); told++; }
                catch (System.Exception e) { Plugin.Logger?.LogWarning("[Support] Rod warning not delivered: " + e.Message); }
            }
            return told;
        }

        public static string InboundText(float leadSeconds) =>
            "ORBITAL STRIKE INBOUND — IMPACT IN " + Mathf.Max(1, Mathf.FloorToInt(leadSeconds + 0.001f)) + " S";

        public const string CancelledText = "ORBITAL STRIKE CANCELLED";
    }
}

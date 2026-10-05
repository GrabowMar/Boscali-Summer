using System;
using NuclearOption.Networking;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// One stable per-player key for session-scoped feature state. SteamID when present;
    /// otherwise a high-bit-tagged player index so single-player and non-Steam servers still
    /// separate players.
    /// ponytail: the index fallback is reusable across disconnects, so on a non-Steam server a
    /// rejoining slot can inherit the previous occupant's session state. Acceptable while state
    /// is session-scoped; a persistent profile must key on a non-zero SteamID only.
    /// </summary>
    internal static class PlayerIdentity
    {
        public const ulong None = 0UL;

        /// <summary>
        /// The id Support reserves for WATCH OFFICER OVERLORD (<c>SpaceContacts.WatchOfficerId</c>, the same value: the Domain code is engine-free and cannot reference Core). No player may
        /// carry it, so a SteamID that equals it falls back to the index form.
        /// </summary>
        public const ulong Reserved = ulong.MaxValue;

        public static ulong Of(Player player) =>
            player == null ? None :
            player.SteamID != 0UL && player.SteamID != Reserved ? player.SteamID :
            0x8000000000000000UL | (uint)System.Math.Max(0, player.PlayerIndex);
    }
}

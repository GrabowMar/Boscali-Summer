using System;
using NuclearOption.Networking;

namespace BoscaliSummer.Framework.Contracts
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

        public static ulong Of(Player player) =>
            player == null ? None :
            player.SteamID != 0UL ? player.SteamID :
            0x8000000000000000UL | (uint)Math.Max(0, player.PlayerIndex);
    }
}

using System;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// Which builds of the mod's own maps a peer has, as "prefab:hash" entries. The Maps module fills the two sources and
    /// the Session handshake carries them, so a client with another build of the hosted map is turned away instead of
    /// flying over terrain the host does not have.
    /// </summary>
    internal static class MapBuilds
    {
        /// <summary>Every map this peer has, comma separated; "" when it has none.</summary>
        internal static Func<string> Catalog = () => "";

        /// <summary>The entry of the map being played when it is one of the mod's; "" on a vanilla map.</summary>
        internal static Func<string> Hosted = () => "";

        /// <summary>True when the host is on one of the mod's maps and the client's catalog lacks that exact build.</summary>
        internal static bool Mismatch(string hosted, string clientCatalog) =>
            !string.IsNullOrEmpty(hosted) && Array.IndexOf((clientCatalog ?? "").Split(','), hosted) < 0;

        internal static string NameOf(string entry)
        {
            int at = (entry ?? "").IndexOf(':');
            return at < 0 ? entry ?? "" : entry.Substring(0, at);
        }
    }
}

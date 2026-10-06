using System;

namespace BoscaliSummer.Modules.Support.Domain
{
    /// <summary>Finite supplier offers unlocked by a prepared hostile espionage post.</summary>
    internal static class BlackMarketCatalog
    {
        public const float Intel = 35f;
        public static bool Known(byte offer) => offer < 2;
        public static float Price(byte offer) => offer == 0 ? 550f : offer == 1 ? 250f : 0f;
        public static string Name(byte offer) => offer == 0 ? "SIGNAL KIT" : offer == 1 ? "COVERT CARGO" : "UNKNOWN OFFER";
    }
}

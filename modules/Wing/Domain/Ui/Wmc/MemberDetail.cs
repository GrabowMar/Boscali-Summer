using System.Text;

namespace BoscaliSummer.Modules.Wing.Domain
{
    internal struct StoreLine
    {
        public string Name;
        public int Ammo, Full;
        public StoreClass Class;
    }

    /// <summary>One part of the aircraft on the AIRCRAFT damage map: its place on the drawing (−1..1 sideways and along the length, nose
    /// positive) and its state (0 whole, 1 damaged, 2 lost).</summary>
    internal struct PartDot
    {
        public float X, Z;
        public byte State;
    }

    /// <summary>What the deep card knows about one member (spec WMC program §4); NaN, -1 and null are unknown.</summary>
    internal struct MemberDetail
    {
        public float Fuel, Ammo, BingoSeconds, Damage;
        /// <summary>-1 unknown, 0 off, 1 on.</summary>
        public int Radar;
        public string Target, Callsign, Rank, Perks;
        public StoreLine[] Stores;
        public int StoreCount;
        /// <summary>The parts as the damage map draws them (only those whose place is known), and how many parts are lost in all.</summary>
        public PartDot[] Parts;
        public int PartCount, PartsLost, PartsHit;
    }

    /// <summary>The deep card's four lines: fuel and bingo, ammo, damage; radar and target; stores; pilot. Unknowns are a
    /// dash, never a made-up zero.</summary>
    internal static class DetailLines
    {
        public const int MaxStores = 6;
    }
}

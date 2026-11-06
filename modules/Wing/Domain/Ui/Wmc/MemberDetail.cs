using System.Text;

namespace BoscaliSummer.Modules.Wing.Domain
{
    internal struct StoreLine
    {
        public string Name;
        public int Ammo, Full;
        public StoreClass Class;
    }

    /// <summary>What the AIRCRAFT card knows about one member (spec WMC program §4); NaN is unknown.</summary>
    internal struct MemberDetail
    {
        public float Fuel, Ammo, BingoSeconds, Damage;
        public StoreLine[] Stores;
        public int StoreCount;
    }

    /// <summary>The AIRCRAFT card's limits.</summary>
    internal static class DetailLines
    {
        public const int MaxStores = 6;
    }
}

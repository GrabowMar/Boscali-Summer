using UnityEngine;

namespace BoscaliSummer.Features.Command.Domain
{
    internal sealed class TacticalTheaterState
    {
        public int FriendlyAircraftCount;
        public int HostileAircraftCount;
        public int FriendlyAirbaseCount;
        public int HostileAirbaseCount;
        public int ContestedAirbaseCount;
        public int FriendlySamCount;
        public int HostileSamCount;
        public int ActiveSortiesCount;
        public int CapSortiesCount;
        public int StrikeSortiesCount;
        public int CasSortiesCount;
        public int SeadSortiesCount;
        public int RtbSortiesCount;
        public float AirSuperiorityRatio;
        public int DefconLevel = 3;
        public string PrimaryThreatDescription = "NOMINAL";

        public void Reset()
        {
            FriendlyAircraftCount = 0;
            HostileAircraftCount = 0;
            FriendlyAirbaseCount = 0;
            HostileAirbaseCount = 0;
            ContestedAirbaseCount = 0;
            FriendlySamCount = 0;
            HostileSamCount = 0;
            ActiveSortiesCount = 0;
            CapSortiesCount = 0;
            StrikeSortiesCount = 0;
            CasSortiesCount = 0;
            SeadSortiesCount = 0;
            RtbSortiesCount = 0;
            AirSuperiorityRatio = 0.5f;
            DefconLevel = 3;
            PrimaryThreatDescription = "NOMINAL";
        }
    }
}

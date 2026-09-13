using Mirage;

namespace BoscaliSummer.Features.Support.Networking
{
    /// <summary>Client poll for its faction's constellation and infrastructure state.</summary>
    [NetworkMessage]
    internal struct OpsQueryMessage
    {
        public byte Protocol;
    }

    /// <summary>One client intent: launch, retask, recall or upgrade. The host validates everything.</summary>
    [NetworkMessage]
    internal struct OpsCommandMessage
    {
        public byte Protocol;
        public int RequestId;
        public byte Command;
        public byte Arg;
        public byte Arg2;
        public float X, Z;
    }

    /// <summary>
    /// Bounded faction snapshot: at most four satellites and four facility levels. Stations,
    /// transfer origin and transfer time let the client rebuild the same model locally.
    /// </summary>
    [NetworkMessage]
    internal struct OpsStateMessage
    {
        public byte Protocol;
        public int RequestId;
        public byte Result;
        public byte SatelliteCount;
        public byte[] SatelliteIds;
        public byte[] SatelliteRoles;
        public byte[] SatelliteAltitudes;
        public byte[] SatelliteStates;
        public byte[] SatelliteFuel;
        public float[] StationXs;
        public float[] StationZs;
        public float[] OriginXs;
        public float[] OriginZs;
        public float[] TransitLeft;
        public float[] TransitTotal;
        public byte Sigint, Crypto, Disrupt, Ew;
    }

    /// <summary>Host broadcast when a track-deception operation starts; all peers mirror it.</summary>
    [NetworkMessage]
    internal struct CyberEffectMessage
    {
        public byte Protocol;
        public byte Kind;
        public string FactionName;
        public float X, Z, Duration;
    }
}

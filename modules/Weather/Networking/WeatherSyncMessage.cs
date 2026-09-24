using Mirage;

namespace BoscaliSummer.Features.Weather.Networking
{
    /// <summary>
    /// Lightweight periodic host-to-client weather state broadcast.
    /// Drives smooth client-side LevelInfo interpolation with minimal wire traffic.
    /// </summary>
    [NetworkMessage]
    internal struct WeatherSyncMessage
    {
        public byte Protocol;
        public float TargetConditions;
        public float TargetCloudHeight;
        public float TargetWindX;
        public float TargetWindZ;
        public float TargetTurbulence;
        public float TransitionProgress;
        public uint MissionTimeSeconds;
        public float ForcedRain; // -1f = auto/unforced, 0f..1f = forced manual rain
    }
}

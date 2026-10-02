using Mirage;

namespace BoscaliSummer.Modules.Weather.Networking
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
        public uint FieldSeed;
        public float FieldEpoch;
        public byte FieldStartRegime;
        public bool FieldDynamic;
        public bool FieldManual;
        public float HoldMinutes;
        public float BlendMinutes;
        /// <summary>Console-forced set-piece storms (Superstructures bits).</summary>
        public byte FieldSets;
        /// <summary>Console layout re-roll.</summary>
        public byte FieldSalt;
        /// <summary>Console placement of the storm eye and lenticulars.</summary>
        public bool FieldHasAnchor;
        public float FieldAnchorX;
        public float FieldAnchorZ;
        /// <summary>Frontal boundary turned by 45 degree steps.</summary>
        public byte FieldFrontTurn;
    }
}

using BoscaliSummer.Modules.Weather.Domain;
using Mirage;

namespace BoscaliSummer.Modules.Weather.Networking
{
    /// <summary>
    /// Periodic host-to-client weather broadcast: the whole weather key (protocol 8), the native targets that
    /// preload a late joiner, and the host's mission clock for the adoption check. Field order is the wire order
    /// and is pinned by the PatchProbe; keep the Mirage identity and bump <see cref="WeatherNet.ProtocolVersion"/>
    /// whenever the meaning or serialisation changes.
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
        /// <summary>Host mission clock in whole seconds; a client adopts a moving key only if its own clock is within 10 s.</summary>
        public uint MissionTimeSeconds;
        public float ForcedRain; // -1f = auto/unforced, 0f..1f = forced manual rain
        public uint FieldSeed;
        /// <summary>255 = auto.</summary>
        public byte FieldStory;
        /// <summary>255 = none.</summary>
        public byte FieldSkyPin;
        /// <summary>Console layout re-roll.</summary>
        public byte FieldSalt;
        /// <summary>The mission's weather class at capture, 255 = none.</summary>
        public byte FieldMissionClass;
        public float FieldOpeningBase;
        /// <summary>Compass heading the wind blows from, set at capture.</summary>
        public float FieldHeading;
        public float FieldEpoch;
        public float FieldOffset;
        public float FieldSystemSpeed;
        public bool FieldDynamic;
        public bool FieldManual;
        /// <summary>Console-forced set-piece storms (Superstructures bits).</summary>
        public byte FieldSets;
        /// <summary>Console placement of the storm eye and lenticulars.</summary>
        public bool FieldHasAnchor;
        public float FieldAnchorX;
        public float FieldAnchorZ;
        public float FieldSetOriginSquall;
        public float FieldSetOriginSupercell;
        public float FieldSetOriginCell;
    }

    /// <summary>The one place the flat message and <see cref="WeatherKeyWire.Fields"/> are mapped to each other.</summary>
    internal static class WeatherSyncKey
    {
        public static WeatherKeyWire.Fields Read(in WeatherSyncMessage m) => new WeatherKeyWire.Fields
        {
            Seed = m.FieldSeed, Story = m.FieldStory, SkyPin = m.FieldSkyPin, LayoutSalt = m.FieldSalt,
            MissionClass = m.FieldMissionClass, OpeningBase = m.FieldOpeningBase, HeadingDeg = m.FieldHeading,
            Epoch = m.FieldEpoch, Offset = m.FieldOffset, SystemSpeed = m.FieldSystemSpeed, Dynamic = m.FieldDynamic,
            Sets = m.FieldSets, HasAnchor = m.FieldHasAnchor, AnchorX = m.FieldAnchorX, AnchorZ = m.FieldAnchorZ,
            SetOriginSquall = m.FieldSetOriginSquall, SetOriginSupercell = m.FieldSetOriginSupercell,
            SetOriginCell = m.FieldSetOriginCell,
        };

        public static void Write(ref WeatherSyncMessage m, in WeatherKeyWire.Fields f)
        {
            m.FieldSeed = f.Seed;
            m.FieldStory = f.Story;
            m.FieldSkyPin = f.SkyPin;
            m.FieldSalt = f.LayoutSalt;
            m.FieldMissionClass = f.MissionClass;
            m.FieldOpeningBase = f.OpeningBase;
            m.FieldHeading = f.HeadingDeg;
            m.FieldEpoch = f.Epoch;
            m.FieldOffset = f.Offset;
            m.FieldSystemSpeed = f.SystemSpeed;
            m.FieldDynamic = f.Dynamic;
            m.FieldSets = f.Sets;
            m.FieldHasAnchor = f.HasAnchor;
            m.FieldAnchorX = f.AnchorX;
            m.FieldAnchorZ = f.AnchorZ;
            m.FieldSetOriginSquall = f.SetOriginSquall;
            m.FieldSetOriginSupercell = f.SetOriginSupercell;
            m.FieldSetOriginCell = f.SetOriginCell;
        }
    }
}

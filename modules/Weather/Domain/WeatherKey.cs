using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// Everything the host decides about the weather, and the only weather data that ever crosses the network.
    /// Immutable, compared by value, every float already rounded to float32 so all peers hold the same bits.
    ///
    /// <para>Three groups (spec 9.1). The <b>tape identity</b> (<see cref="Seed"/>, <see cref="Story"/>,
    /// <see cref="SkyPin"/>, <see cref="LayoutSalt"/>, <see cref="MissionClass"/>, <see cref="OpeningBase"/>) is what
    /// <see cref="TapeId"/> hashes; changing it is a cut. The <b>frame</b> (<see cref="HeadingDeg"/>) is set at
    /// capture from the mission wind and never edited. The <b>kinematics</b> (epoch, offset, speed, dynamic, set-pieces)
    /// move the tape without rebuilding it; <see cref="Rebased"/> is the only way to change speed or hold state.</para>
    /// </summary>
    internal sealed class WeatherKey : IEquatable<WeatherKey>
    {
        /// <summary>Sentinel for <see cref="Story"/>, <see cref="SkyPin"/> and <see cref="MissionClass"/>: not authored.</summary>
        public const byte None = 255;

        /// <summary>Old name of <see cref="None"/> for the timeline's opener.</summary>
        public const byte AutoState = None;

        public const float DefaultSystemSpeed = 27f;

        /// <summary>Table sizes the wire validates against. Slices 2 and 4 assert the real tables match.</summary>
        public const int ClassCount = 7, StoryCount = 6, SkyCount = 47;

        public const float EditorCloudAltitude = 1800f, OpeningBaseStep = 10f;

        private readonly uint tapeId;

        public WeatherKey(uint seed, float epoch, bool dynamic, byte missionClass = None, byte sets = 0,
            byte layoutSalt = 0, bool hasAnchor = false, float anchorX = 0f, float anchorZ = 0f,
            byte story = None, byte skyPin = None, float openingBase = 0f, float headingDeg = float.NaN,
            float offset = 0f, float systemSpeed = DefaultSystemSpeed, float setOriginSquall = 0f,
            float setOriginSupercell = 0f, float setOriginCell = 0f)
        {
            Seed = seed;
            Story = story;
            SkyPin = skyPin;
            LayoutSalt = layoutSalt;
            MissionClass = missionClass;
            OpeningBase = Math.Max(0f, Clean(openingBase));
            HeadingDeg = Scalar.IsFinite(headingDeg) ? Wrap(headingDeg) : SeededHeading(seed);
            Epoch = Clean(epoch);
            Offset = Clean(offset);
            SystemSpeed = Scalar.IsFinite(systemSpeed) ? Math.Max(0f, systemSpeed) : DefaultSystemSpeed;
            Dynamic = dynamic;
            Sets = (byte)(sets & Superstructures.AllSets);
            HasAnchor = hasAnchor && Scalar.IsFinite(anchorX) && Scalar.IsFinite(anchorZ);
            AnchorX = HasAnchor ? Clean(anchorX) : 0f;
            AnchorZ = HasAnchor ? Clean(anchorZ) : 0f;
            SetOriginSquall = Clean(setOriginSquall);
            SetOriginSupercell = Clean(setOriginSupercell);
            SetOriginCell = Clean(setOriginCell);
            tapeId = Deterministic.Hash((int)Deterministic.Hash((int)seed, story, skyPin, layoutSalt), missionClass,
                (int)Math.Round(OpeningBase / OpeningBaseStep, MidpointRounding.AwayFromZero), 0x5A9E);
        }

        // ---- tape identity -------------------------------------------------------------------
        public uint Seed { get; }
        public byte Story { get; }
        public byte SkyPin { get; }
        public byte LayoutSalt { get; }
        public byte MissionClass { get; }
        public float OpeningBase { get; }

        // ---- frame ---------------------------------------------------------------------------
        /// <summary>Compass direction the wind blows <b>from</b>, [0, 360).</summary>
        public float HeadingDeg { get; }

        // ---- kinematics ----------------------------------------------------------------------
        public float Epoch { get; }
        public float Offset { get; }
        public float SystemSpeed { get; }
        public bool Dynamic { get; }

        /// <summary>Set-piece storms the host forced on from the weather console (see <see cref="Superstructures"/>).</summary>
        public byte Sets { get; }
        public bool HasAnchor { get; }
        public float AnchorX { get; }
        public float AnchorZ { get; }
        public float SetOriginSquall { get; }
        public float SetOriginSupercell { get; }
        public float SetOriginCell { get; }

        /// <summary>Hash of the tape identity: every array, texture and lattice salt is a pure function of it.</summary>
        public uint TapeId => tapeId;

        /// <summary>Heading the wind blows toward; what the field's prevailing wind and cloud orientation use.</summary>
        public float DownwindHeading => Wrap(HeadingDeg + 180f);

        /// <summary>The one time function (spec 3.1). <paramref name="t"/> is the gated mission clock.</summary>
        public double OffsetAt(double t) => Dynamic ? Offset + (double)SystemSpeed * (t - Epoch) : Offset;

        /// <summary>Rebase: the tape position at <paramref name="now"/> becomes the new offset, so changing the speed
        /// or the hold state never moves it. Never a cut.</summary>
        public WeatherKey Rebased(float now, bool? dynamic = null, float? systemSpeed = null) =>
            Copy(epoch: now, offset: (float)OffsetAt(now), dynamic: dynamic, systemSpeed: systemSpeed);

        /// <summary>The mission clock stepped back from <paramref name="lastTime"/> to <paramref name="newTime"/>
        /// (restart, replay): keep the tape where it was.</summary>
        public WeatherKey AfterClockReset(double lastTime, float newTime) =>
            Copy(epoch: newTime, offset: (float)OffsetAt(lastTime));

        /// <summary>Same tape and same frame: tape texture, windows and caches need no rebuild (spec 9.2).</summary>
        public bool SameTape(WeatherKey other) =>
            other != null && TapeId == other.TapeId && HeadingDeg == other.HeadingDeg;

        public WeatherKey WithSets(byte sets) => Copy(sets: sets);
        public WeatherKey WithLayoutSalt(byte salt) => Copy(salt: salt);
        public WeatherKey WithAnchor(float x, float z) => Copy(anchor: true, x: x, z: z);
        public WeatherKey WithoutAnchor() => Copy(anchor: false);

        /// <summary>INTERIM(slice 4): the legacy timeline needs an opener; the tape never re-keys its class.</summary>
        public WeatherKey WithMissionClass(byte missionClass) => Copy(missionClass: missionClass);

        private WeatherKey Copy(byte? missionClass = null, byte? sets = null, byte? salt = null, bool? anchor = null,
            float? x = null, float? z = null, float? epoch = null, float? offset = null, bool? dynamic = null,
            float? systemSpeed = null) =>
            new WeatherKey(Seed, epoch ?? Epoch, dynamic ?? Dynamic, missionClass ?? MissionClass, sets ?? Sets,
                salt ?? LayoutSalt, anchor ?? HasAnchor, x ?? AnchorX, z ?? AnchorZ, Story, SkyPin, OpeningBase,
                HeadingDeg, offset ?? Offset, systemSpeed ?? SystemSpeed, SetOriginSquall, SetOriginSupercell, SetOriginCell);

        /// <summary>The state the legacy timeline opens with. INTERIM(slice 4).</summary>
        public WeatherRegimeType OpeningState()
        {
            if (MissionClass < ClassCount) return (WeatherRegimeType)MissionClass;
            // No authored class (or one outside the table): missions open flyable, mostly fair skies.
            float roll = WeatherMath.Hash01(Seed, 7, 1);
            if (roll < 0.20f) return WeatherRegimeType.Clear;
            if (roll < 0.55f) return WeatherRegimeType.Fair;
            if (roll < 0.80f) return WeatherRegimeType.Scattered;
            if (roll < 0.92f) return WeatherRegimeType.Broken;
            return WeatherRegimeType.Overcast;
        }

        // ---- capture helpers -----------------------------------------------------------------

        /// <summary>The heading a key carries when the mission wind gives none: a pure function of the seed.</summary>
        public static float SeededHeading(uint seed) => Wrap(WeatherMath.Hash01(seed, 41) * 360f + 180f);

        /// <summary>Compass heading the mission wind comes from. <paramref name="windX"/> and <paramref name="windZ"/>
        /// are the vanilla wind velocity (east, north). Calm (0.5 m/s or less, which includes vanilla's forced 0.1)
        /// has no direction and takes the seeded one.</summary>
        public static float HeadingFromWind(float windX, float windZ, float windSpeed, uint seed) =>
            Scalar.IsFinite(windX) && Scalar.IsFinite(windZ) && Scalar.IsFinite(windSpeed) && windSpeed > 0.5f
                ? Wrap(WeatherMath.VectorToHeading(windX, windZ) + 180f)
                : SeededHeading(seed);

        /// <summary>The mission cloud altitude in 10 m steps, unclamped; 0 (none) for the editor default 1800 (which is
        /// indistinguishable from an authored 1800), zero, negative or non-finite altitudes.</summary>
        public static float QuantiseOpeningBase(float altitude)
        {
            if (!Scalar.IsFinite(altitude) || altitude <= 0f) return 0f;
            float quantised = (float)(Math.Round(altitude / OpeningBaseStep, MidpointRounding.AwayFromZero) * OpeningBaseStep);
            return quantised == EditorCloudAltitude ? 0f : quantised;
        }

        private static float Wrap(float degrees)
        {
            float h = WeatherMath.WrapHeading(degrees);
            return h >= 360f ? 0f : h;
        }

        private static float Clean(float value) => Scalar.IsFinite(value) ? value + 0f : 0f;

        public bool Equals(WeatherKey other)
        {
            if (other is null) return false;
            return Seed == other.Seed && Story == other.Story && SkyPin == other.SkyPin &&
                LayoutSalt == other.LayoutSalt && MissionClass == other.MissionClass &&
                OpeningBase == other.OpeningBase && HeadingDeg == other.HeadingDeg &&
                Epoch == other.Epoch && Offset == other.Offset && SystemSpeed == other.SystemSpeed &&
                Dynamic == other.Dynamic && Sets == other.Sets && HasAnchor == other.HasAnchor &&
                AnchorX == other.AnchorX && AnchorZ == other.AnchorZ && SetOriginSquall == other.SetOriginSquall &&
                SetOriginSupercell == other.SetOriginSupercell && SetOriginCell == other.SetOriginCell;
        }

        public override bool Equals(object obj) => Equals(obj as WeatherKey);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Seed;
                h = h * 31 + Story;
                h = h * 31 + SkyPin;
                h = h * 31 + LayoutSalt;
                h = h * 31 + MissionClass;
                h = h * 31 + OpeningBase.GetHashCode();
                h = h * 31 + HeadingDeg.GetHashCode();
                h = h * 31 + Epoch.GetHashCode();
                h = h * 31 + Offset.GetHashCode();
                h = h * 31 + SystemSpeed.GetHashCode();
                h = h * 31 + (Dynamic ? 1 : 0);
                h = h * 31 + Sets;
                h = h * 31 + (HasAnchor ? 1 : 0);
                h = h * 31 + AnchorX.GetHashCode();
                h = h * 31 + AnchorZ.GetHashCode();
                h = h * 31 + SetOriginSquall.GetHashCode();
                h = h * 31 + SetOriginSupercell.GetHashCode();
                h = h * 31 + SetOriginCell.GetHashCode();
                return h;
            }
        }
    }
}

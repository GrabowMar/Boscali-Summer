using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// Everything the host decides about the weather, and the only weather data that ever
    /// crosses the network. Every peer derives the same state timeline and the same static cloud
    /// layout from this key and the synced mission clock. Immutable, compared by value.
    /// </summary>
    internal sealed class WeatherKey : IEquatable<WeatherKey>
    {
        /// <summary>No authored starting state: the seed picks one.</summary>
        public const byte AutoState = 255;

        public WeatherKey(uint seed, float epoch, bool dynamic, byte startState,
            float intervalMinutes = 5f, float fadeSeconds = 60f, byte sets = 0, byte layoutSalt = 0,
            bool hasAnchor = false, float anchorX = 0f, float anchorZ = 0f, byte frontTurn = 0)
        {
            HasAnchor = hasAnchor && !float.IsNaN(anchorX) && !float.IsNaN(anchorZ);
            AnchorX = HasAnchor ? anchorX : 0f;
            AnchorZ = HasAnchor ? anchorZ : 0f;
            FrontTurn = (byte)(frontTurn % 8);
            Seed = seed;
            Epoch = epoch;
            Dynamic = dynamic;
            StartState = startState;
            IntervalMinutes = Math.Max(1f, intervalMinutes);
            FadeSeconds = Math.Max(5f, fadeSeconds);
            Sets = (byte)(sets & Superstructures.AllSets);
            LayoutSalt = layoutSalt;
        }

        public uint Seed { get; }
        public float Epoch { get; }

        /// <summary>False holds the start state for the whole mission.</summary>
        public bool Dynamic { get; }
        public byte StartState { get; }
        public float IntervalMinutes { get; }
        public float FadeSeconds { get; }

        /// <summary>Set-piece storms the host forced on from the weather console, whatever the
        /// state (bit per <see cref="SuperstructureKind"/> slot, see <see cref="Superstructures"/>).</summary>
        public byte Sets { get; }

        /// <summary>Re-rolls the cloud layout (and set-piece sites) without touching the timeline.</summary>
        public byte LayoutSalt { get; }

        /// <summary>Where console-placed set-pieces (storm eye, lenticulars) stand; map frame metres.</summary>
        public bool HasAnchor { get; }
        public float AnchorX { get; }
        public float AnchorZ { get; }

        /// <summary>Turns the frontal boundary by 45 degree steps (0..7).</summary>
        public byte FrontTurn { get; }

        public WeatherKey WithSets(byte sets) => With(sets: sets);
        public WeatherKey WithLayoutSalt(byte salt) => With(salt: salt);
        public WeatherKey WithAnchor(float x, float z) => With(anchor: true, x: x, z: z);
        public WeatherKey WithoutAnchor() => With(anchor: false);
        public WeatherKey WithFrontTurn(int turn) => With(turn: (byte)(((turn % 8) + 8) % 8));

        private WeatherKey With(byte? sets = null, byte? salt = null, bool? anchor = null, float? x = null,
            float? z = null, byte? turn = null) =>
            new WeatherKey(Seed, Epoch, Dynamic, StartState, IntervalMinutes, FadeSeconds,
                sets ?? Sets, salt ?? LayoutSalt, anchor ?? HasAnchor, x ?? AnchorX, z ?? AnchorZ, turn ?? FrontTurn);

        /// <summary>The state the timeline opens with.</summary>
        public WeatherRegimeType OpeningState()
        {
            if (StartState != AutoState) return StateTable.Clamp(StartState);
            // Missions open flyable: mostly fair skies, sometimes something brewing.
            float roll = WeatherMath.Hash01(Seed, 7, 1);
            if (roll < 0.20f) return WeatherRegimeType.Clear;
            if (roll < 0.55f) return WeatherRegimeType.Fair;
            if (roll < 0.80f) return WeatherRegimeType.Scattered;
            if (roll < 0.92f) return WeatherRegimeType.Broken;
            return WeatherRegimeType.Overcast;
        }

        public bool Equals(WeatherKey other)
        {
            if (other is null) return false;
            return Seed == other.Seed && Epoch == other.Epoch && Dynamic == other.Dynamic &&
                StartState == other.StartState && IntervalMinutes == other.IntervalMinutes &&
                FadeSeconds == other.FadeSeconds && Sets == other.Sets && LayoutSalt == other.LayoutSalt &&
                HasAnchor == other.HasAnchor && AnchorX == other.AnchorX && AnchorZ == other.AnchorZ &&
                FrontTurn == other.FrontTurn;
        }

        public override bool Equals(object obj) => Equals(obj as WeatherKey);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Seed;
                h = h * 31 + Epoch.GetHashCode();
                h = h * 31 + (Dynamic ? 1 : 0);
                h = h * 31 + StartState;
                h = h * 31 + IntervalMinutes.GetHashCode();
                h = h * 31 + FadeSeconds.GetHashCode();
                h = h * 31 + Sets;
                h = h * 31 + LayoutSalt;
                h = h * 31 + (HasAnchor ? 1 : 0);
                h = h * 31 + AnchorX.GetHashCode();
                h = h * 31 + AnchorZ.GetHashCode();
                h = h * 31 + FrontTurn;
                return h;
            }
        }
    }
}

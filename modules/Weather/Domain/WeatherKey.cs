using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>Gameplay switches the host decides for everyone. Travels inside the key.</summary>
    [Flags]
    internal enum WeatherFlags : byte
    {
        None = 0,
        StormTurbulence = 1,
        SensorEffects = 2,
        LightningHazard = 4,
        All = StormTurbulence | SensorEffects | LightningHazard,
    }

    /// <summary>One authored or forced change of regime, effective from <see cref="Time"/>.</summary>
    internal readonly struct RegimeOverride : IEquatable<RegimeOverride>
    {
        public readonly float Time;
        public readonly WeatherRegime Regime;

        public RegimeOverride(float time, WeatherRegime regime)
        {
            Time = time;
            Regime = regime;
        }

        public bool Equals(RegimeOverride other) => Time == other.Time && Regime == other.Regime;
        public override bool Equals(object obj) => obj is RegimeOverride other && Equals(other);
        public override int GetHashCode() => Time.GetHashCode() * 31 + (int)Regime;
    }

    /// <summary>
    /// Everything the host decides about the weather, and the only weather data that ever
    /// crosses the network. Every peer derives the whole sky — schedule, front, cells, strikes,
    /// rain — from this key and the synced mission clock, so nothing else is sent and a late
    /// joiner is correct the moment it has the key.
    ///
    /// <para>Immutable. <see cref="WithOverride"/> returns a new key; at most
    /// <see cref="MaxOverrides"/> keyframes are kept, oldest dropped first, and they are always
    /// in time order.</para>
    /// </summary>
    internal sealed class WeatherKey : IEquatable<WeatherKey>
    {
        public const int MaxOverrides = 4;

        /// <summary>No authored starting regime: the seed picks one.</summary>
        public const byte AutoRegime = 255;

        private readonly RegimeOverride[] overrides;

        public WeatherKey(uint seed, float epoch, bool dynamic, byte startRegime, WeatherFlags flags,
            RegimeOverride[] overrides = null)
        {
            Seed = seed;
            Epoch = epoch;
            Dynamic = dynamic;
            StartRegime = startRegime;
            Flags = flags;
            this.overrides = Normalise(overrides);
        }

        public uint Seed { get; }
        public float Epoch { get; }
        public bool Dynamic { get; }
        public byte StartRegime { get; }
        public WeatherFlags Flags { get; }
        public int OverrideCount => overrides.Length;

        public RegimeOverride Override(int index) => overrides[index];

        public bool Has(WeatherFlags flag) => (Flags & flag) == flag;

        /// <summary>The regime the schedule opens with.</summary>
        public WeatherRegime OpeningRegime()
        {
            if (StartRegime != AutoRegime) return RegimeTable.Clamp(StartRegime);
            // Missions open flyable: mostly fair-weather skies, sometimes something brewing.
            float roll = WeatherMath.Hash01(Seed, 7, 1);
            if (roll < 0.25f) return WeatherRegime.Clear;
            if (roll < 0.60f) return WeatherRegime.Fair;
            if (roll < 0.80f) return WeatherRegime.Showers;
            if (roll < 0.92f) return WeatherRegime.Frontal;
            return WeatherRegime.Storms;
        }

        public WeatherKey WithOverride(float time, WeatherRegime regime)
        {
            var list = new RegimeOverride[Math.Min(overrides.Length + 1, MaxOverrides + 1)];
            int n = 0;
            for (int i = 0; i < overrides.Length; i++)
            {
                // A new keyframe supersedes any that would take effect at or after it.
                if (overrides[i].Time < time) list[n++] = overrides[i];
            }
            list[n++] = new RegimeOverride(time, regime);
            Array.Resize(ref list, n);
            return new WeatherKey(Seed, Epoch, Dynamic, StartRegime, Flags, list);
        }

        public WeatherKey WithFlags(WeatherFlags flags)
            => new WeatherKey(Seed, Epoch, Dynamic, StartRegime, flags, overrides);

        public WeatherKey WithSchedule(bool dynamic, byte startRegime)
            => new WeatherKey(Seed, Epoch, dynamic, startRegime, Flags, overrides);

        public WeatherKey WithoutOverrides()
            => new WeatherKey(Seed, Epoch, Dynamic, StartRegime, Flags, null);

        public bool Equals(WeatherKey other)
        {
            if (other is null) return false;
            if (Seed != other.Seed || Epoch != other.Epoch || Dynamic != other.Dynamic ||
                StartRegime != other.StartRegime || Flags != other.Flags ||
                overrides.Length != other.overrides.Length)
            {
                return false;
            }
            for (int i = 0; i < overrides.Length; i++)
            {
                if (!overrides[i].Equals(other.overrides[i])) return false;
            }
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as WeatherKey);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Seed;
                h = h * 31 + Epoch.GetHashCode();
                h = h * 31 + (Dynamic ? 1 : 0);
                h = h * 31 + StartRegime;
                h = h * 31 + (int)Flags;
                for (int i = 0; i < overrides.Length; i++) h = h * 31 + overrides[i].GetHashCode();
                return h;
            }
        }

        private static RegimeOverride[] Normalise(RegimeOverride[] source)
        {
            if (source == null || source.Length == 0) return Array.Empty<RegimeOverride>();
            var copy = (RegimeOverride[])source.Clone();
            Array.Sort(copy, (a, b) => a.Time.CompareTo(b.Time));
            if (copy.Length <= MaxOverrides) return copy;
            var kept = new RegimeOverride[MaxOverrides];
            Array.Copy(copy, copy.Length - MaxOverrides, kept, 0, MaxOverrides);
            return kept;
        }
    }
}

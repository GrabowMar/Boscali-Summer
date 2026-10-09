using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The weather key on the wire (spec 9.1). <see cref="Fields"/> is the key as plain primitives; the Mirage
    /// message carries them flat, and <c>WeatherNet</c> only serialises that message. A client builds its key from
    /// the fields alone, never from its own <c>LevelInfo</c>, so every peer holds the same bits.
    /// </summary>
    internal static class WeatherKeyWire
    {
        /// <summary>A moving key is adopted only if the host's mission clock is within this many seconds of ours.</summary>
        public const double MaxClockSkewSeconds = 10.0;

        public struct Fields
        {
            public uint Seed;
            public byte Story, SkyPin, LayoutSalt, MissionClass;
            public float OpeningBase, HeadingDeg, Epoch, Offset, SystemSpeed;
            public bool Dynamic;
            public byte Sets;
            public bool HasAnchor;
            public float AnchorX, AnchorZ, SetOriginSquall, SetOriginSupercell, SetOriginCell;
        }

        public static Fields FromKey(WeatherKey key) => new Fields
        {
            Seed = key.Seed, Story = key.Story, SkyPin = key.SkyPin, LayoutSalt = key.LayoutSalt,
            MissionClass = key.MissionClass, OpeningBase = key.OpeningBase, HeadingDeg = key.HeadingDeg,
            Epoch = key.Epoch, Offset = key.Offset, SystemSpeed = key.SystemSpeed, Dynamic = key.Dynamic,
            Sets = key.Sets, HasAnchor = key.HasAnchor, AnchorX = key.AnchorX, AnchorZ = key.AnchorZ,
            SetOriginSquall = key.SetOriginSquall, SetOriginSupercell = key.SetOriginSupercell,
            SetOriginCell = key.SetOriginCell,
        };

        /// <summary>
        /// Builds the key a peer holds. <c>Story</c>, <c>SkyPin</c> and <c>MissionClass</c> pass 255 through; any other
        /// value must be below its table count, else it falls back to auto/none and <paramref name="warn"/> is told
        /// (the caller logs once). Never a clamp to Count-1: that turned 255 into Storm.
        /// </summary>
        public static WeatherKey ToKey(in Fields f, Action<string> warn = null)
        {
            byte story = Sentinel(f.Story, WeatherKey.StoryCount, "story", warn);
            byte skyPin = Sentinel(f.SkyPin, WeatherKey.SkyCount, "sky pin", warn);
            byte missionClass = Sentinel(f.MissionClass, WeatherKey.ClassCount, "mission class", warn);
            return new WeatherKey(f.Seed, f.Epoch, f.Dynamic, missionClass, f.Sets, f.LayoutSalt, f.HasAnchor,
                f.AnchorX, f.AnchorZ, story, skyPin, f.OpeningBase, f.HeadingDeg, f.Offset, f.SystemSpeed,
                f.SetOriginSquall, f.SetOriginSupercell, f.SetOriginCell);
        }

        /// <summary>A held key needs no clock check. A moving key is adopted only with a local clock that agrees with
        /// the host's; otherwise the client keeps its previous key and the next resend retries.</summary>
        public static bool CanAdopt(bool dynamic, bool haveClock, double localMissionTime, uint messageSeconds) =>
            !dynamic || (haveClock && Math.Abs(localMissionTime - messageSeconds) <= MaxClockSkewSeconds);

        /// <summary>Instance stability (spec 10.1): a value-equal resend returns the receiver's current key, so
        /// every consumer sees one stable object and nothing that compares references can mistake a heartbeat for a change.</summary>
        public static WeatherKey Reuse(WeatherKey current, WeatherKey incoming) =>
            incoming != null && incoming.Equals(current) ? current : incoming;

        private static byte Sentinel(byte value, int count, string what, Action<string> warn)
        {
            if (value == WeatherKey.None || value < count) return value;
            warn?.Invoke("weather key " + what + " " + value + " is outside its table (" + count + "); using auto");
            return WeatherKey.None;
        }
    }
}

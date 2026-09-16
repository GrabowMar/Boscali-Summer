using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// One read of the sky, for the panel and the debug overlay. Pure: the Unity layer fills it,
    /// everything downstream formats it.
    /// </summary>
    internal readonly struct WeatherSnapshot
    {
        /// <summary>False when there is no mission or the module is off; every field is then default.</summary>
        public readonly bool Available;

        public readonly float MissionTime;

        /// <summary>What the deterministic schedule wants right now.</summary>
        public readonly WeatherState Model;

        /// <summary>What the world is actually doing right now.</summary>
        public readonly WeatherState Live;

        /// <summary>Local wind at the player's position — the vanilla sample, not the mean field.</summary>
        public readonly float LocalWindX;
        public readonly float LocalWindY;
        public readonly float LocalWindZ;

        /// <summary>Cloud occlusion where the player is; 0 under a clear sky, 1 in the deck.</summary>
        public readonly float CloudOcclusion;

        public readonly float DaylightFactor;

        /// <summary>Somebody else owns the sky: an authored beat, a debug override, another mod.</summary>
        public readonly bool Overridden;

        /// <summary>True when the host is driving this sky; false for a client reading it.</summary>
        public readonly bool HostAuthority;

        public readonly bool Severe;

        /// <summary>Caller-owned cell array; valid up to <see cref="CellCount"/>.</summary>
        public readonly StormCell[] Cells;

        public readonly int CellCount;

        /// <summary>How much the strongest nearby cell owns the sky over the reader, 0..1.</summary>
        public readonly float CellInfluence;

        public readonly StormWarning Warning;

        /// <summary>The cell that imposed <see cref="Warning"/>, or default when there is none.</summary>
        public readonly StormCell WarningSource;

        public WeatherSnapshot(
            bool available,
            float missionTime,
            WeatherState model,
            WeatherState live,
            float localWindX,
            float localWindY,
            float localWindZ,
            float cloudOcclusion,
            float daylightFactor,
            bool overridden,
            bool hostAuthority,
            StormCell[] cells,
            int cellCount,
            float cellInfluence,
            StormWarning warning,
            StormCell warningSource)
        {
            Available = available;
            MissionTime = missionTime;
            Model = model;
            Live = live;
            LocalWindX = localWindX;
            LocalWindY = localWindY;
            LocalWindZ = localWindZ;
            CloudOcclusion = cloudOcclusion;
            DaylightFactor = daylightFactor;
            Overridden = overridden;
            HostAuthority = hostAuthority;
            Severe = live.IsSevere;
            Cells = cells;
            CellCount = cellCount;
            CellInfluence = cellInfluence;
            Warning = warning;
            WarningSource = warningSource;
        }

        /// <summary>
        /// Precipitation the player is standing in: the strongest local cell influence. Zero
        /// between storms, and the same number the radar, the haze and the warning ring read.
        /// </summary>
        public float RainIntensity => WeatherRegimes.Clamp01(CellInfluence);

        public float LocalWindSpeed
        {
            get
            {
                float speed = (float)Math.Sqrt(LocalWindX * LocalWindX + LocalWindY * LocalWindY + LocalWindZ * LocalWindZ);
                return float.IsNaN(speed) ? 0f : speed;
            }
        }

        /// <summary>Direction the local wind blows toward, as a compass heading.</summary>
        public float LocalWindHeading
        {
            get
            {
                if (float.IsNaN(LocalWindX) || float.IsNaN(LocalWindZ)) return 0f;
                if (Math.Abs(LocalWindX) < 1e-4f && Math.Abs(LocalWindZ) < 1e-4f) return Live.WindHeading;
                float degrees = (float)(Math.Atan2(LocalWindX, LocalWindZ) * 180.0 / Math.PI);
                return WeatherState.WrapHeading(degrees);
            }
        }

        public static WeatherSnapshot Unavailable => default;
    }
}

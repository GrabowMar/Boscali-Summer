using System;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    internal enum Warning { None, Bingo, OverG, Terrain, PullUp }

    /// <summary>Flight state the voice warnings read, once a frame.</summary>
    internal struct WarningInput
    {
        public float RadarAltM, ClimbMps, G, Fuel01;
        public bool GearDown, Airborne;
    }

    /// <summary>
    /// Which voice warning the cockpit should be saying now (2026-10-27). Ground proximity uses the HUD's own rule
    /// (Hud/Domain HudCues.Terrain: PULL UP under 5 s to the ground below 300 m, TERRAIN under 150 m sinking faster than
    /// 5 m/s) so the voice and the HUD caption agree; with the gear down only a hard sink counts, so a landing is quiet.
    /// Each warning repeats at its own interval while it holds; the most urgent one wins.
    /// </summary>
    internal sealed class WarningMath
    {
        public const float AglCeilingM = 300f, TerrainAglM = 150f, TerrainSinkMps = 5f, PullUpSeconds = 5f;
        public const float GearDownSinkMps = 10f, OverG = 9.5f, BingoFuel = .15f, BingoRearm = .25f;
        private static readonly float[] Repeat = { 0f, 30f, 3f, 3f, 2.2f };

        private readonly float[] nextAt = new float[5];
        private bool bingoArmed = true;

        public static Warning Ground(WarningInput s)
        {
            if (!s.Airborne || float.IsNaN(s.RadarAltM) || float.IsNaN(s.ClimbMps) || s.RadarAltM >= AglCeilingM || s.ClimbMps >= 0f)
                return Warning.None;
            float sink = -s.ClimbMps;
            if (s.GearDown && sink < GearDownSinkMps) return Warning.None;
            if (s.RadarAltM / sink < PullUpSeconds) return Warning.PullUp;
            return s.RadarAltM < TerrainAglM && sink > TerrainSinkMps ? Warning.Terrain : Warning.None;
        }

        /// <summary>The warning to say at <paramref name="now"/> (seconds), or None when nothing is due.</summary>
        public Warning Step(WarningInput s, float now)
        {
            if (s.Fuel01 > BingoRearm) bingoArmed = true;
            Warning due = Ground(s);
            if (due == Warning.None && s.Airborne && s.G > OverG) due = Warning.OverG;
            if (due == Warning.None && s.Airborne && bingoArmed && s.Fuel01 > 0f && s.Fuel01 <= BingoFuel) due = Warning.Bingo;
            if (due == Warning.None || now < nextAt[(int)due]) return Warning.None;
            nextAt[(int)due] = now + Repeat[(int)due];
            if (due == Warning.Bingo) bingoArmed = false;
            return due;
        }

        public void Reset()
        {
            Array.Clear(nextAt, 0, nextAt.Length);
            bingoArmed = true;
        }
    }
}

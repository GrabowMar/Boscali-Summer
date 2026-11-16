using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The AR helmet layer's pure rules (2026-11-14, docs/superpowers/specs/2026-11-14-ar-helmet-layer-design.md): when each world-locked item earns its place, the
    /// flight path prediction, the SAM envelope's shape, and the timing of the lock spin-in and the hit glitch. Every item is contextual: it draws only while it answers
    /// a question the pilot has now.
    /// </summary>
    internal static class ArRules
    {
        /// <summary>The path: one bead every <see cref="PathStepS"/> out to <see cref="PathSamples"/> beads (8 s).</summary>
        public const float PathStepS = 0.5f;
        public const int PathSamples = 16;
        /// <summary>The pull (centripetal load in g) above which the path shows as a manoeuvre preview.</summary>
        public const float ManoeuvreG = 3f;
        /// <summary>A SAM ring draws while the aircraft is within this share of its reach; the nearest <see cref="DomeMax"/> only.</summary>
        public const float DomeReach = 1.35f;
        public const int DomeMax = 4, DomeLevels = 5, DomeSegments = 32, DomeStruts = 8;
        /// <summary>The objective beacon stands only while the zone is farther than this.</summary>
        public const float ObjectiveMinM = 2000f;
        /// <summary>Lock spin-in: the spin, the hold, the fade.</summary>
        public const float SpinS = 0.18f, SpinHoldS = 0.35f, SpinFadeS = 0.25f;
        /// <summary>Hit glitch: how long, and the least time between two (under 2 a second, the flashing limit is 3).</summary>
        public const float GlitchS = 0.28f, GlitchGapS = 0.6f, GlitchJitterPx = 5f;
        public const int GlitchBars = 4;
        private const float G = 9.81f;

        /// <summary>
        /// The constant-turn prediction: the velocity turns at <c>w</c> (rad/s about an axis, the measured rate the flight path is turning) and the position follows it.
        /// Fills <paramref name="xyz"/> with <paramref name="count"/> offsets from the aircraft (x, y, z per sample), one every <paramref name="step"/> seconds.
        /// </summary>
        public static void Predict(float vx, float vy, float vz, float wx, float wy, float wz, float step, int count, float[] xyz)
        {
            float rate = (float)Math.Sqrt(wx * wx + wy * wy + wz * wz), angle = rate * step;
            float kx = 0f, ky = 0f, kz = 0f, cos = 1f, sin = 0f;
            if (angle > 1e-5f)
            {
                kx = wx / rate; ky = wy / rate; kz = wz / rate;
                cos = (float)Math.Cos(angle); sin = (float)Math.Sin(angle);
            }
            float px = 0f, py = 0f, pz = 0f;
            for (int i = 0; i < count; i++)
            {
                // Rodrigues: v' = v cos + (k x v) sin + k (k . v)(1 - cos)
                float dot = kx * vx + ky * vy + kz * vz;
                float nx = vx * cos + (ky * vz - kz * vy) * sin + kx * dot * (1f - cos);
                float ny = vy * cos + (kz * vx - kx * vz) * sin + ky * dot * (1f - cos);
                float nz = vz * cos + (kx * vy - ky * vx) * sin + kz * dot * (1f - cos);
                px += (vx + nx) * 0.5f * step; py += (vy + ny) * 0.5f * step; pz += (vz + nz) * 0.5f * step;
                vx = nx; vy = ny; vz = nz;
                xyz[i * 3] = px; xyz[i * 3 + 1] = py; xyz[i * 3 + 2] = pz;
            }
        }

        /// <summary>The rate the velocity turned from <c>a</c> to <c>b</c> over <paramref name="dt"/>: axis times angle per second (zero for a still or reversed vector).</summary>
        public static void TurnRate(float ax, float ay, float az, float bx, float by, float bz, float dt, out float wx, out float wy, out float wz)
        {
            wx = wy = wz = 0f;
            float cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
            float cross = (float)Math.Sqrt(cx * cx + cy * cy + cz * cz), dot = ax * bx + ay * by + az * bz;
            if (!(dt > 1e-4f) || cross < 1e-6f || dot <= 0f) return;
            float perSecond = (float)Math.Atan2(cross, dot) / dt / cross;
            wx = cx * perSecond; wy = cy * perSecond; wz = cz * perSecond;
        }

        /// <summary>The centripetal load of that turn in g: how hard the pilot is pulling, without reading the pilot.</summary>
        public static float PullG(float speed, float wx, float wy, float wz) => speed * (float)Math.Sqrt(wx * wx + wy * wy + wz * wz) / G;

        /// <summary>Whether the path draws: it meets the ground within the horizon (not with the gear down: an approach meets the runway on purpose), or a hard pull.</summary>
        public static bool ShowPath(float collisionS, bool gearDown, float pullG) => (!float.IsNaN(collisionS) && !gearDown) || pullG >= ManoeuvreG;

        /// <summary>A known ring the layer may draw: a SAM (radar, launch-only or IR), not a gun or a bare sensor.</summary>
        public static bool IsDome(AirDefenceKind kind) => kind == AirDefenceKind.RadarSam || kind == AirDefenceKind.IrSam || kind == AirDefenceKind.Unknown;

        /// <summary>The ring is near enough to draw: within <see cref="DomeReach"/> times its reach, horizontally.</summary>
        public static bool DomeNear(float distance, float maxRange) => maxRange > 0f && distance < maxRange * DomeReach;

        /// <summary>
        /// The envelope's level <paramref name="level"/> of <see cref="DomeLevels"/>: its height above the site and its radius there, in the game's own engagement shape
        /// (<see cref="AirDefenceRing.EffectiveRadius(float, float, float, float)"/>: a cone out to full reach at the minimum altitude, then straight up to the maximum).
        /// Level 0 is where full reach starts (the ground for a ring with no minimum), the last is the ceiling.
        /// </summary>
        public static void Level(float maxRange, float minAltitude, float maxAltitude, int level, out float height, out float radius)
        {
            float floor = Math.Max(0f, Math.Min(minAltitude, maxAltitude));
            height = floor + (maxAltitude - floor) * level / (DomeLevels - 1);
            radius = AirDefenceRing.EffectiveRadius(maxRange, minAltitude, maxAltitude, Math.Min(height, maxAltitude));
        }

        /// <summary>Alpha by distance for world symbols: whole up close, a third far out.</summary>
        public static float Fade(float metres, float near = 2000f, float far = 40000f)
        {
            float t = (metres - near) / (far - near);
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return 1f - 0.65f * t;
        }

        /// <summary>The way home stands when the pilot needs it: fuel at JOKER or BINGO, or damage that lasts.</summary>
        public static bool HomeWanted(bool joker, bool bingo, DamageFlags damage, int enginesOut, float detached) =>
            joker || bingo || damage != DamageFlags.None || enginesOut > 0 || detached > 0.02f;

        /// <summary>A hit worth a glitch: the lasting damage just got worse (a new fire, leak or engine fault, an engine lost, or more of the airframe gone).</summary>
        public static bool IsHit(in DamageState before, in DamageState now) =>
            (now.Flags & ~before.Flags) != 0 || now.EnginesOut > before.EnginesOut || now.DetachedRatio > before.DetachedRatio + 0.005f;

        /// <summary>
        /// The lock spin-in at <paramref name="age"/> seconds: a bracket that closes from 3x to 1x and turns 45 degrees to square in <see cref="SpinS"/> (with a little
        /// overshoot), holds, then fades. <paramref name="calm"/> (REDUCED MOTION) skips the spin and shows the final bracket. False once it is over.
        /// </summary>
        public static bool Spin(float age, bool calm, out float scale, out float angleDeg, out float alpha)
        {
            scale = 1f; angleDeg = 0f; alpha = 0f;
            if (age < 0f || age > SpinS + SpinHoldS + SpinFadeS) return false;
            float e = calm ? 1f : BackOut(Math.Min(1f, age / SpinS));
            scale = 3f - 2f * e;
            angleDeg = 45f * (1f - e);
            alpha = age < SpinS + SpinHoldS ? 1f : 1f - (age - SpinS - SpinHoldS) / SpinFadeS;
            return true;
        }

        /// <summary>Ease-out with an overshoot (the "back" curve, gentle: about 4 % past 1), then settles exactly on 1.</summary>
        public static float BackOut(float t)
        {
            const float c1 = 1f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        /// <summary>The glitch's sideways jitter at <paramref name="age"/>: a new random step 30 times a second, dying out over <see cref="GlitchS"/>; 0 outside it.</summary>
        public static float Jitter(float age, int seed)
        {
            if (age < 0f || age >= GlitchS) return 0f;
            return Noise(seed, (int)(age * 30f)) * GlitchJitterPx * (1f - age / GlitchS);
        }

        /// <summary>Scan bar <paramref name="index"/>'s height on the screen (0 bottom .. 1 top) and its thickness in reference pixels, fixed for one glitch.</summary>
        public static void Bar(int seed, int index, out float share, out float thickness)
        {
            share = 0.5f + 0.45f * Noise(seed, 101 + index);
            thickness = 2f + 2f * (1f + Noise(seed, 211 + index));
        }

        /// <summary>A stable pseudo-random value in -1..1 for the pair (integer hash, no allocation).</summary>
        public static float Noise(int seed, int n)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)n * 0x85EBCA77u;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x7FFFFF - 1f;
            }
        }
    }
}

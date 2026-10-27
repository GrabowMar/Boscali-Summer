using UnityEngine;

namespace BoscaliSummer.Modules.CombatAdjust.Domain
{
    internal enum MoverClass { Static, Ground, Ship, Air }

    /// <summary>
    /// Stale contacts: how far the picture's guess drifts from the last report, and when an unseen contact leaves the picture.
    /// A contact being reported stays exact (vanilla); only the unreported ones drift.
    /// </summary>
    internal static class TrackFog
    {
        /// <summary>Vanilla keeps a report live (exact) for 4 s; drift starts only then, so the marker does not jump.</summary>
        public const float LiveSeconds = 4f;

        public static float StaleAge(float secondsSinceReport) => Mathf.Max(0f, secondsSinceReport - LiveSeconds);

        /// <summary>
        /// Drift radius after <paramref name="ageSeconds"/> unseen: ground 3 m/s up to 600 m, ships 5 m/s up to 600 m, aircraft 30 m/s up to
        /// 1000 m. The cap applies after the scale, so no scale pushes a datalink missile's datum past ARHSeeker's ~2 km give-up distance.
        /// </summary>
        public static float Radius(MoverClass c, float ageSeconds, float scale)
        {
            if (c == MoverClass.Static || ageSeconds <= 0f || scale <= 0f) return 0f;
            float rate = c == MoverClass.Air ? 30f : c == MoverClass.Ship ? 5f : 3f;
            float cap = c == MoverClass.Air ? 1000f : 600f;
            return Mathf.Min(cap, rate * ageSeconds * scale);
        }

        /// <summary>
        /// The drift itself: a direction and length that wander with periods of 90-150 s, seeded by the track and its report, never longer than
        /// <paramref name="radius"/>. Slow enough that a 600 m drift moves under a metre a frame.
        /// </summary>
        public static Vector3 Drift(uint id, float reportTime, float now, float radius, bool surface)
        {
            if (radius <= 0f) return Vector3.zero;
            uint seed = Seeded.Hash(id, (uint)Mathf.RoundToInt(reportTime * 10f), 0xF06u);
            float t = now - reportTime;
            float w1 = Omega(seed, 1), w2 = Omega(seed, 2), w3 = Omega(seed, 3);
            float angle = Seeded.Roll01(seed, 0, 4) * 2f * Mathf.PI
                + 0.8f * Mathf.Sin(w1 * t + Phase(seed, 5))
                + 0.4f * Mathf.Sin(w2 * t + Phase(seed, 6));
            float length = radius * (0.6f + 0.4f * Mathf.Sin(w3 * t + Phase(seed, 7)));
            if (surface) return new Vector3(Mathf.Cos(angle) * length, 0f, Mathf.Sin(angle) * length);
            float h = length * 0.95f;
            return new Vector3(Mathf.Cos(angle) * h, length * 0.3f * Mathf.Sin(w2 * t + Phase(seed, 8)), Mathf.Sin(angle) * h);
        }

        /// <summary>An unseen moving contact leaves the picture after its memory (aircraft a third of it); never while engaged; never buildings.</summary>
        public static bool Expired(MoverClass c, float ageSeconds, float expirySeconds, bool engaged)
        {
            if (c == MoverClass.Static || expirySeconds <= 0f || engaged) return false;
            return ageSeconds > (c == MoverClass.Air ? expirySeconds / 3f : expirySeconds);
        }

        private static float Omega(uint seed, uint salt) => 2f * Mathf.PI / Seeded.Range(seed, salt, 1, 90f, 150f);
        private static float Phase(uint seed, uint salt) => Seeded.Roll01(seed, salt, 2) * 2f * Mathf.PI;
    }
}

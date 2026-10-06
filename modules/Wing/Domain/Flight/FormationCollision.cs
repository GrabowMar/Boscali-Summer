using System;

namespace BoscaliSummer.Modules.Wing.Domain
{
    internal static class FormationCollision
    {

        public static float Threat(float px, float py, float pz, float vx, float vy, float vz,
            float radius, out float time, out float miss)
        {
            float speedSquared = vx * vx + vy * vy + vz * vz;
            time = speedSquared > 1f ? Math.Max(0f, Math.Min(WingTuning.CollisionHorizon,
                -(px * vx + py * vy + pz * vz) / speedSquared)) : 0f;
            float x = px + vx * time, y = py + vy * time, z = pz + vz * time;
            miss = (float)Math.Sqrt(x * x + y * y + z * z);
            if (radius <= 0f || miss >= radius) return 0f;
            return (1f - miss / radius) * (1f + 1f / (1f + time));
        }
    }
}

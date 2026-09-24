namespace BoscaliSummer.Features.Hud.Runtime
{
    internal static class ThirdPersonCameraPolicy
    {
        public static float Response(float seconds, float deltaTime) =>
            seconds <= 0f ? 1f : (float)(1d - System.Math.Exp(-System.Math.Max(0f, deltaTime) / seconds));

        public static float FollowDistance(float radius, float length, float width, float zoom) =>
            System.Math.Max(12f, System.Math.Max(radius * 2.4f,
                System.Math.Max(length * 1.65f, width * 1.3f))) * (1f + System.Math.Clamp(zoom, 0f, 10f));
    }
}

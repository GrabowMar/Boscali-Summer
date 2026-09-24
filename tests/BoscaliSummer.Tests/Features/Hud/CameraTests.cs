using BoscaliSummer.Features.Hud.Runtime;

namespace BoscaliSummer.Tests.Features.Hud
{
    internal static class CameraTests
    {
        public static void Run()
        {
            float remaining30 = 1f, remaining144 = 1f;
            for (int i = 0; i < 30; i++) remaining30 *= 1f - ThirdPersonCameraPolicy.Response(0.22f, 1f / 30f);
            for (int i = 0; i < 144; i++) remaining144 *= 1f - ThirdPersonCameraPolicy.Response(0.22f, 1f / 144f);
            TestAssert.That(System.Math.Abs(remaining30 - remaining144) < 0.00001f,
                "Camera response must settle consistently at 30 and 144 FPS");
            TestAssert.That(ThirdPersonCameraPolicy.Response(0.2f, 0f) == 0f,
                "A stopped clock must not move the camera");
            TestAssert.That(ThirdPersonCameraPolicy.FollowDistance(0f, 0f, 0f, 0f) >= 12f,
                "Missing dimensions must not collapse the camera into ownship");
            float baseDistance = ThirdPersonCameraPolicy.FollowDistance(10f, 20f, 14f, 0f);
            TestAssert.That(ThirdPersonCameraPolicy.FollowDistance(10f, 20f, 14f, -5f) == baseDistance &&
                ThirdPersonCameraPolicy.FollowDistance(10f, 20f, 14f, 20f) == baseDistance * 11f,
                "Follow distance must preserve bounded native zoom");
        }

    }
}

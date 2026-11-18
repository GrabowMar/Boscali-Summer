using UnityEngine;

namespace BoscaliSummer.Core.Fx
{
    internal static class WorldEffectCamera
    {
        internal static Camera Main
        {
            get
            {
                var manager = SceneSingleton<CameraStateManager>.i;
                return manager != null && manager.mainCamera != null ? manager.mainCamera : Camera.main;
            }
        }

        internal static float DistanceTo(Vector3 world)
        {
            Camera camera = Main;
            return camera != null ? Vector3.Distance(camera.transform.position, world) : 2000f;
        }

        internal static void Shake(Vector3 world, float reach, float strength)
        {
            var manager = SceneSingleton<CameraStateManager>.i;
            if (manager == null) return;
            float falloff = Mathf.Clamp01(1f - DistanceTo(world) / reach);
            if (falloff > 0.02f)
                manager.ShakeCamera(Mathf.Lerp(0.2f, 3.4f, falloff * falloff) * strength,
                    Mathf.Lerp(0.3f, 4.2f, falloff) * strength);
        }
    }
}

using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Runtime
{
    internal static class NativeCamera
    {
        private static readonly FieldInfo CameraField = AccessTools.Field(typeof(TargetCam), "cam");
        private static readonly FieldInfo ModeField = AccessTools.Field(typeof(TargetCam), "currentMode");
        public static bool Available => CameraField?.FieldType == typeof(Camera) &&
            ModeField?.FieldType == typeof(TargetCam.CamMode);
        public static Camera ReadCamera(TargetCam source) => Available ? CameraField.GetValue(source) as Camera : null;
        public static TargetCam.CamMode ReadMode(TargetCam source) => (TargetCam.CamMode)ModeField.GetValue(source);

        public static bool TryGet(Aircraft aircraft, out Camera camera, out string mode)
        {
            camera = null;
            mode = "CAMERA";
            if (!Available || aircraft == null || aircraft.targetCam == null ||
                !aircraft.targetCam.isActiveAndEnabled) return false;
            camera = ReadCamera(aircraft.targetCam);
            if (camera == null || !camera.isActiveAndEnabled || camera.targetTexture == null ||
                !camera.targetTexture.IsCreated()) return false;
            TargetCam.CamMode value = ReadMode(aircraft.targetCam);
            mode = value == TargetCam.CamMode.landingMode ? "LANDING" :
                value == TargetCam.CamMode.targetRear ? "REAR" : "FORWARD";
            return true;
        }

    }
}

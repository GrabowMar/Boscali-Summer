using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Core.Game
{
    internal static class NativeCamera
    {
        private static readonly AccessTools.FieldRef<TargetCam, Camera> CameraRef = Ref<Camera>("cam");
        private static readonly AccessTools.FieldRef<TargetCam, TargetCam.CamMode> ModeRef = Ref<TargetCam.CamMode>("currentMode");
        // FieldRefAccess throws when the field is missing or its type differs: null then means unavailable.
        public static bool Available { get; } = CameraRef != null && ModeRef != null;
        public static Camera ReadCamera(TargetCam source) => Available && source != null ? CameraRef(source) : null;
        public static TargetCam.CamMode ReadMode(TargetCam source) => ModeRef(source);

        private static AccessTools.FieldRef<TargetCam, F> Ref<F>(string name)
        {
            try { return AccessTools.FieldRefAccess<TargetCam, F>(name); }
            catch (System.Exception) { return null; }
        }

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

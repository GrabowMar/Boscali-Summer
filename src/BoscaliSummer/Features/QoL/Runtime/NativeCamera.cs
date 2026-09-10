using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.QoL.Runtime
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

        public static string ContactFreshness(Aircraft aircraft)
        {
            var targets = aircraft.weaponManager != null ? aircraft.weaponManager.GetTargetList() : null;
            if (targets == null || targets.Count != 1 || targets[0] == null) return "CONTACT AGE: SELECT ONE TARGET";
            FactionHQ hq = aircraft.NetworkHQ;
            if (hq == null) return "CONTACT AGE UNKNOWN";
            if (targets[0].NetworkHQ == hq) return "FRIENDLY CONTACT";
            TrackingInfo report = hq.GetTrackingData(targets[0].persistentID);
            if (report == null) return "CONTACT AGE UNKNOWN";
            float age = Time.timeSinceLevelLoad - report.lastSpottedTime;
            if (!ObservationStore.Finite(age) || age < 0f) return "CONTACT AGE UNKNOWN";
            return age < 4f ? "CONTACT UPDATED <4s AGO" : $"LAST CONTACT REPORT {age:0}s AGO";
        }
    }
}

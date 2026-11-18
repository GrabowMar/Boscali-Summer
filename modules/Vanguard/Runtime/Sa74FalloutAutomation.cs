#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>Explicit nomodkit acceptance hook. Only "sample" freezes presentation; ordinary actions preserve native time.</summary>
    public static class Sa74FalloutAutomation
    {
        private static float beforeSampleScale = 1f;
        private static bool sampling;
        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                !GameAccess.IsServer() || GameManager.gameState != GameState.SinglePlayer)
                return Failure("Sa74FalloutAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            Sa74Fallout fallout = Sa74Fallout.Instance;
            if (fallout == null) return Failed("SA-74N fallout service unavailable");
            string action = Text(args, "action") ?? "status";
            if (action == "nativeFront")
            {
                float farthest = 0f;
                foreach (var wave in UnityEngine.Object.FindObjectsOfType<Shockwave>())
                    if (wave.enabled && wave.GetComponentInParent<Sa74BlastTag>() != null)
                        farthest = Mathf.Max(farthest, (float)HarmonyLib.AccessTools.Field(typeof(Shockwave), "blastPropagation").GetValue(wave));
                return new Dictionary<string, object> { ["radius"] = farthest };
            }
            if (action == "reset")
            {
                fallout.ResetForScene();
                if (sampling) { Time.timeScale = beforeSampleScale; sampling = false; }
            }
            if (action == "detonate")
            {
                Unit source = GameManager.GetLocalAircraft(out Aircraft aircraft) ? aircraft : null;
                Vector3 at = source != null ? source.transform.position : Vector3.zero;
                if (Has(args, "land") && Convert.ToBoolean(Arg(args, "land")))
                {
                    Airbase field = null;
                    foreach (Airbase candidate in UnityEngine.Object.FindObjectsOfType<Airbase>())
                        if (candidate.center != null && candidate.center.position.y > Datum.LocalSeaY + 5f &&
                            (field == null || candidate.center.position.y > field.center.position.y)) field = candidate;
                    if (field == null) return Failed("no elevated land airbase available");
                    at = field.center.position;
                }
                at.x = Number(args, "x", at.x); at.z = Number(args, "z", at.z);
                at.y = Datum.LocalSeaY + 10f;
                if (Physics.Raycast(at + Vector3.up * 10000f, Vector3.down, out RaycastHit hit, 20000f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)) at.y = hit.point.y + .5f;
                at.y = Number(args, "y", at.y);
                Sa74Fallout.Detonate(at, source);
            }
            if (action == "age" || action == "sample")
            {
                float age = Mathf.Clamp(Number(args, "seconds", 90f), 0f, 86400f);
                foreach (Sa74Fallout.Site site in Sa74Fallout.Sites) site.Started = Time.time - age;
            }
            if (action == "look" || action == "sample")
            {
                CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
                if (camera == null || Sa74Fallout.Sites.Count == 0) return Failed("no blast/camera to observe");
                Sa74Fallout.Site site = Sa74Fallout.Sites[Sa74Fallout.Sites.Count - 1];
                Vector3 at = site.Position.ToLocalPosition();
                Vector3 eye = at + new Vector3(Number(args, "distance", 16000f), Number(args, "height", 7000f), Number(args, "side", -3000f));
                Vector3 aim = at + new Vector3(Number(args, "aimX", 0f), Number(args, "aimHeight", 3500f), Number(args, "aimZ", 0f));
                camera.SetCameraPosition(eye.ToGlobalPosition(), Quaternion.LookRotation(aim - eye));
                camera.allowInputs = false;
                float fov = Number(args, "fov", 65f);
                camera.SetDesiredFoV(fov, fov);
            }
            if (action == "sample")
            {
                if (Time.timeScale > 0f) beforeSampleScale = Time.timeScale;
                sampling = true;
                Time.timeScale = 0f; // Rendering continues: sub-second flash captures do not require real-time waits.
            }
            if (action == "time")
            {
                LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                if (level != null) level.SetTimeOfDay(Mathf.Repeat(Number(args, "hours", 12f), 24f));
            }
            return fallout.Snapshot();
        }
    }
}
#endif

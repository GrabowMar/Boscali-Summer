#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using NuclearOption.Effects;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Fire
{
    /// <summary>nomodkit hook for the forest fire sim: ignite dense forest, frame it, read the result.</summary>
    public static class ForestFireAutomation
    {
        private static GlobalPosition anchor;
        private static readonly float[] probe = new float[3 * 64];

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            ImpactFireManager fire = ImpactFireManager.Instance;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer() || fire == null)
                return Failure("ForestFireAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            string action = Text(args, "action");
            if (action == "ignite")
            {
                if (!fire.Forest.Ready) return Failure("ForestFireAutomation", "ignite", "forest index not ready");
                // Densest clump nearest the requested map point, on a 500 m grid out to 30 km.
                float x0 = Number(args, "x", 0f), z0 = Number(args, "z", 0f);
                int want = (int)Number(args, "clumps", 10f);
                bool found = false;
                for (int ring = 0; ring <= 60 && !found; ring++)
                for (int i = -ring; i <= ring && !found; i++)
                for (int j = -ring; j <= ring && !found; j++)
                {
                    if (Math.Max(Math.Abs(i), Math.Abs(j)) != ring) continue;
                    float x = x0 + i * 500f, z = z0 + j * 500f;
                    if (fire.Forest.CopyNear(x, z, 70f, probe) < want) continue;
                    anchor = new GlobalPosition(probe[0], probe[1], probe[2]);
                    found = true;
                }
                if (!found) return Failure("ForestFireAutomation", "ignite", "no dense forest found");
                ((IFireStarter)fire).Ignite(anchor);
            }
            else if (action == "frame")
            {
                Vector3 center = anchor.ToLocalPosition();
                float distance = Number(args, "distance", 350f), angle = Number(args, "angle", 0f);
                Vector3 position = center + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.back * distance + Vector3.up * distance * Number(args, "height", 0.8f);
                CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
                camera.SetCameraPosition(position.ToGlobalPosition(), Quaternion.LookRotation(center - position));
                camera.cameraVelocity = Vector3.zero;
                camera.allowInputs = false;
                camera.SetDesiredFoV(55f, 55f);
                SceneSingleton<DynamicMap>.i?.Minimize();
            }
            else if (action != "status") return Failure("ForestFireAutomation", "Step", "unknown action");

            var reply = new Dictionary<string, object>
            {
                { "ok", true },
                { "x", anchor.x }, { "z", anchor.z },
                { "forestSites", fire.ActiveForestSites },
                { "clearedClumps", fire.ClearedClumps },
                { "clumpsNear", fire.Forest.CopyNear(anchor.x, anchor.z, 70f, probe) },
                { "blastAtFire", BlastValue(anchor.x, anchor.z) },
                { "blastAt150m", BlastValue(anchor.x + 150f, anchor.z) }
            };
            Debug.Log("[ForestFireAutomation] " + action + ": " + string.Join(", ", reply));
            return reply;
        }

        /// <summary>Vanilla blast-map value (0..1) under a map point; trees and ground blacken with it.</summary>
        private static float BlastValue(float x, float z)
        {
            BlastManager blast = SceneSingleton<BlastManager>.i;
            if (blast == null || blast.Texture == null) return -1f;
            Vector2 size = blast.mapSettings.MapSize;
            RenderTexture rt = blast.Texture;
            int px = Mathf.Clamp((int)((x + size.x / 2f) / size.x * rt.width), 0, rt.width - 1);
            int pz = Mathf.Clamp((int)((z + size.y / 2f) / size.y * rt.height), 0, rt.height - 1);
            RenderTexture previous = RenderTexture.active;
            var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            pixel.ReadPixels(new Rect(px, pz, 1, 1), 0, 0);
            RenderTexture.active = previous;
            float value = pixel.GetPixel(0, 0).r;
            UnityEngine.Object.Destroy(pixel);
            return value;
        }
    }
}
#endif

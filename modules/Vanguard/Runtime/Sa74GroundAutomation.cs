#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>Native physics-height proof for ground, water and high-airburst fixtures; never available in ordinary play.</summary>
    public static class Sa74GroundAutomation
    {
        private static GlobalPosition ground;
        private static float before;
        private static bool chosen;

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                !GameAccess.IsServer() || GameManager.gameState != GameState.SinglePlayer || Sa74Fallout.Instance == null)
                return Failure("Sa74GroundAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            string action = Text(args, "action") ?? "probe";
            if (action == "land" || action == "recover" || (action == "air" && !chosen)) ChooseGround();
            if ((action == "land" || action == "air" || action == "recover") && !chosen)
                return Failed("no open elevated native terrain available");
            if (action == "land") Sa74Fallout.Detonate(ground.ToLocalPosition() + Vector3.up * .5f, null);
            if (action == "recover")
                Sa74Fallout.Receive(Sa74Fallout.Epoch, 1u, new Vector3(ground.x, ground.y + .5f, ground.z), 1800f, 0u, 1);
            if (action == "air") Sa74Fallout.Detonate(ground.ToLocalPosition() + Vector3.up * Number(args, "height", 6000f), null);
            if (action == "water")
            {
                Vector2 span = TheaterFrame.Resolve();
                Sa74Fallout.Detonate(new GlobalPosition(span.x * 1.4f, -.25f, span.y * 1.4f).ToLocalPosition(), null);
            }
            if (action == "reset") Sa74FalloutAutomation.Step(new Dictionary<string, object> { ["action"] = "reset" });
            if (action == "soot")
            {
                bool visible = !Has(args, "visible") || Convert.ToBoolean(Arg(args, "visible"));
                foreach (DecalProjector projector in UnityEngine.Object.FindObjectsOfType<DecalProjector>())
                    if (projector.name == "SA-74N persistent native soot") projector.enabled = visible;
            }
            Dictionary<string, object> result = Sa74Fallout.Instance.Snapshot();
            if (chosen)
            {
                Vector3 ray = ground.ToLocalPosition() + Vector3.up * 1000f;
                float floor = (ray - Vector3.up * Sa74FalloutTerrain.GroundClearance(ray)).ToGlobalPosition().y;
                result["groundProbe"] = new Dictionary<string, object>
                {
                    ["before"] = before, ["current"] = floor, ["drop"] = before - floor,
                    ["restoredError"] = Mathf.Abs(before - floor), ["x"] = ground.x, ["z"] = ground.z
                };
            }
            if (Sa74Fallout.Sites.Count != 0)
                result["groundClearance"] = Sa74Fallout.Sites[Sa74Fallout.Sites.Count - 1].GroundClearance;
            return result;
        }

        private static void ChooseGround()
        {
            chosen = false;
            float best = float.MinValue;
            Vector3[] offsets = { new Vector3(4000f, 0f, 0f), new Vector3(-4000f, 0f, 0f),
                new Vector3(0f, 0f, 4000f), new Vector3(0f, 0f, -4000f),
                new Vector3(2800f, 0f, 2800f), new Vector3(-2800f, 0f, -2800f) };
            foreach (Airbase airbase in UnityEngine.Object.FindObjectsOfType<Airbase>())
            {
                if (airbase.center == null) continue;
                foreach (Vector3 offset in offsets)
                {
                    Vector3 ray = airbase.center.position + offset + Vector3.up * 10000f;
                    foreach (RaycastHit hit in Physics.RaycastAll(ray, Vector3.down, 20000f,
                        PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.point.y <= Datum.LocalSeaY + 100f || hit.point.y <= best ||
                            !Sa74FalloutTerrain.TryGroundBurst(hit.point + Vector3.up * .5f, out Vector3 native) || native.y <= best) continue;
                        ground = native.ToGlobalPosition(); before = ground.y; best = native.y; chosen = true;
                    }
                }
            }
        }
    }
}
#endif

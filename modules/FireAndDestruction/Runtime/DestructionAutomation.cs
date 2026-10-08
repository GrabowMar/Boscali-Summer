using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Fire
{
    /// <summary>Explicit nomodkit single-player test hook; inert outside a simulation run.</summary>
    public static class DestructionAutomation
    {
        private static MapBuilding target;
        private static GlobalPosition anchor;
        private static Vector3 size;
        private static Vector3 face;
        private static string targetName, rubbleName, meshName;
        private static double hitCostMs, destroyCostMs, stressCostMs;
        private static int hits;
        private static float lastOriginHeight;

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("DestructionAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            string action = Text(args, "action");
            if (action == "select")
            {
                target = null; hits = 0;
                meshName = Text(args, "mesh") ?? "commercial_2a";
                int skip = (int)Number(args, "skip", 0f);
                foreach (MapBuilding building in UnityEngine.Object.FindObjectsOfType<MapBuilding>())
                {
                    if (!building.gameObject.activeInHierarchy) continue;
                    foreach (MeshFilter filter in building.GetComponentsInChildren<MeshFilter>(true))
                        if (filter.sharedMesh != null && filter.sharedMesh.name == meshName && skip-- <= 0) { target = building; break; }
                    if (target != null) break;
                }
                if (target == null) return Failure("DestructionAutomation", "select", "no native " + meshName + " building");
                Bounds bounds = default;
                bool found = false;
                foreach (Renderer renderer in target.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!found) { bounds = renderer.bounds; found = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                if (!found) return Failure("DestructionAutomation", "select", "building has no renderer");
                Vector3 center = bounds.center;
                center.y = Mathf.Max(target.transform.position.y, bounds.min.y) + 0.5f;
                // Pivot and bounds can sit far underground (buried foundations): use what it rests on
                // (terrain, plaza or a podium roof), measured straight under its centre.
                float street = float.NegativeInfinity;
                foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(bounds.center.x, bounds.max.y + 5f, bounds.center.z), Vector3.down,
                    bounds.size.y + 50f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                    if (!hit.collider.transform.IsChildOf(target.transform)) street = Mathf.Max(street, hit.point.y);
                if (!float.IsNegativeInfinity(street)) center.y = street + 0.5f;
                anchor = center.ToGlobalPosition();
                size = bounds.size;
                size.y = Mathf.Max(1f, bounds.max.y - center.y + 0.5f);
                face = size.x >= size.z ? Vector3.forward : Vector3.right;
                targetName = target.name;
                var prefab = HarmonyLib.AccessTools.Field(typeof(MapBuilding), "destroyedPrefab")?.GetValue(target) as GameObject;
                rubbleName = prefab != null ? prefab.name : null;
                BuildingCarver.Instance?.ResetMetrics();
                Frame(1f, 0f);
            }
            else if (action == "hit")
            {
                if (target == null) return Failure("DestructionAutomation", "hit", "native building disappeared");
                // Spread successive hits across the facade and up the storeys like a real strike pattern.
                float reach = Mathf.Abs(face.x) > 0.5f ? size.x * 0.5f : size.z * 0.5f;
                Vector3 across = Vector3.Cross(Vector3.up, face) * (Mathf.Abs(face.x) > 0.5f ? size.z : size.x);
                float[] spread = { 0f, 0.22f, -0.2f, 0.05f, 0.3f };
                float[] rise = { 0.4f, 0.55f, 0.25f, 0.75f, 0.45f };
                int k = hits % spread.Length;
                Vector3 aim = anchor.ToLocalPosition() + Vector3.up * size.y * rise[k] + across * spread[k];
                Vector3 origin = aim + face * (reach + 3f);
                // Contact detonation: 1.5 m off the real facade at that height, not off the widest bounds
                // (a podium would otherwise catch every blast meant for the tower above it).
                float nearest = float.MaxValue;
                foreach (Collider collider in target.GetComponentsInChildren<Collider>())
                    if (!collider.isTrigger && collider.Raycast(new Ray(aim + face * (reach + 80f), -face), out RaycastHit surface, reach + 80f) && surface.distance < nearest)
                    { nearest = surface.distance; origin = surface.point + surface.normal * 1.5f; }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                target.TakeShockwave(origin, 1f, Number(args, "power", 4f));
                hitCostMs = clock.Elapsed.TotalMilliseconds;
                hits++;
                lastOriginHeight = origin.y - anchor.ToLocalPosition().y;
            }
            else if (action == "destroy")
            {
                if (target == null) return Failure("DestructionAutomation", "destroy", "native building disappeared");
                var clock = System.Diagnostics.Stopwatch.StartNew();
                target.TakeDamage(0f, 0f, 1f, 0f, 10000f, default);
                destroyCostMs = clock.Elapsed.TotalMilliseconds;
            }
            else if (action == "replay")
            {
                if (target == null || Time.timeSinceLevelLoad >= 10f)
                    return Failure("DestructionAutomation", "replay", "requires early scene replay window");
                target.Destruct();
            }
            else if (action == "poke")
            {
                float reach = Mathf.Abs(face.x) > 0.5f ? size.x * 0.5f : size.z * 0.5f;
                RuinAftermathManager.Instance?.Poke(anchor.ToLocalPosition() + Vector3.up * 2.5f + face * (reach - 0.5f), 4f);
            }
            else if (action == "frame") Frame(Number(args, "distance", 1f), Number(args, "angle", 0f), Number(args, "low", 0f) > 0f);
            else if (action == "breachview")
            {
                Vector3 center = BuildingHitLedger.Instance.LastImpact.ToLocalPosition();
                Vector3 normal = BuildingHitLedger.Instance.LastImpactNormal;
                float distance = Number(args, "distance", 18f);
                Vector3 cameraPoint = center + normal * distance + Vector3.up * distance * 0.22f + Vector3.Cross(Vector3.up, normal) * distance * 0.4f;
                Aim(cameraPoint, center);
            }
            else if (action == "stress")
            {
                int built = 0;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                foreach (MapBuilding building in UnityEngine.Object.FindObjectsOfType<MapBuilding>())
                {
                    if (building == target) continue;
                    Renderer renderer = building.GetComponentInChildren<MeshRenderer>();
                    if (renderer == null) continue;
                    Bounds b = renderer.bounds;
                    if (BuildingCarver.Instance.Hit(building, b.center + Vector3.forward * b.extents.z, Vector3.forward, 7f)) built++;
                    if (built >= 14) break;
                }
                stressCostMs = clock.Elapsed.TotalMilliseconds;
                var audit = BuildingCarver.Instance.Audit();
                audit["built"] = built; audit["stressCostMs"] = stressCostMs;
                return audit;
            }
            else if (action == "audit") return BuildingCarver.Instance.Audit();
            else if (action != "status") return Failure("DestructionAutomation", "Step", "unknown action");

            var reply = Snapshot();
            Debug.Log("[DestructionAutomation] " + action + ": " + Describe(reply));
            return reply;
        }

        private static void Frame(float multiplier, float angle, bool low = false)
        {
            // A ruin sits at street level: frame the footprint, not the vanished tower.
            Vector3 center = anchor.ToLocalPosition() + Vector3.up * (low ? 3f : size.y * 0.4f);
            float distance = (low ? Mathf.Max(size.x, size.z, 20f) : Mathf.Max(size.x, size.y, size.z, 20f)) * 1.5f * Mathf.Clamp(multiplier, 0.4f, 40f);
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * face;
            Aim(center + direction * distance + Vector3.up * distance * 0.6f, center);
        }

        private static void Aim(Vector3 position, Vector3 lookAt)
        {
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (camera == null) throw new InvalidOperationException("native camera manager unavailable");
            camera.SetCameraPosition(position.ToGlobalPosition(), Quaternion.LookRotation(lookAt - position));
            camera.cameraVelocity = Vector3.zero;
            camera.allowInputs = false;
            camera.SetDesiredFoV(55f, 55f);
            SceneSingleton<DynamicMap>.i?.Minimize();
        }

        private static Dictionary<string, object> Snapshot()
        {
            int nativeVisible = 0, nativeHidden = 0;
            if (target != null)
                foreach (MeshRenderer renderer in target.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.enabled) nativeVisible++; else nativeHidden++;
            // Only the wreck cloned for THIS building counts; other ruins in the scene are irrelevant.
            int wreckVisible = 0, wreckColliders = 0;
            if (rubbleName != null)
                foreach (Transform root in UnityEngine.Object.FindObjectsOfType<Transform>())
                    if (root.name == rubbleName + "(Clone)" && Vector3.Distance(root.position, anchor.ToLocalPosition()) < size.magnitude)
                    {
                        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true)) if (renderer.enabled) wreckVisible++;
                        wreckColliders += root.GetComponentsInChildren<Collider>(true).Length;
                    }
            int pressure = 0;
            foreach (ParticleSystem system in Datum.origin.GetComponentsInChildren<ParticleSystem>(true))
                if (system.gameObject.activeInHierarchy && system.name == "ConcretePressureFront") pressure += system.particleCount;
            Dictionary<string, object> reply = BuildingCarver.Instance != null ? BuildingCarver.Instance.Audit() : new Dictionary<string, object> { { "ok", false } };
            reply["building"] = targetName ?? "";
            reply["mesh"] = meshName ?? "";
            reply["nativeAlive"] = target != null ? 1 : 0;
            reply["nativeVisible"] = nativeVisible;
            reply["nativeHidden"] = nativeHidden;
            reply["wreckVisible"] = wreckVisible;
            reply["wreckColliders"] = wreckColliders;
            reply["pressureParticles"] = pressure;
            reply["impactMarks"] = BuildingHitLedger.Instance != null ? BuildingHitLedger.Instance.ImpactMarks : 0;
            reply["hitCostMs"] = hitCostMs;
            reply["originHeight"] = lastOriginHeight;
            reply["impactHeight"] = BuildingHitLedger.Instance != null ? BuildingHitLedger.Instance.LastImpact.ToLocalPosition().y - anchor.ToLocalPosition().y : 0f;
            string colliders = "";
            if (target != null)
                foreach (Collider collider in target.GetComponentsInChildren<Collider>())
                    colliders += collider.GetType().Name + ":" + collider.name + "@" + collider.bounds.min.y.ToString("0") + ".." + collider.bounds.max.y.ToString("0") + ";";
            reply["colliders"] = colliders;
            reply["destroyCostMs"] = destroyCostMs;
            reply["managedBytes"] = GC.GetTotalMemory(false);
            return reply;
        }
    }
}

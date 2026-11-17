#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>Disposable production-query fixture; owns no runtime fire behavior.</summary>
    public static class VehicleExplosionProof
    {
        public static Dictionary<string, object> Run(Dictionary<string, object> args)
        {
            Dictionary<string, object> state = PhysicsWarningProbe.Read(null);
            if (!(bool)state["installed"] || !(bool)state["onMainThread"])
                return AutomationArgs.Failed("install the scenario probe on the main thread first");
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            Type managerType = typeof(ImpactFireManager);
            object manager = managerType.GetProperty("Instance", all).GetValue(null);
            if (manager == null) return AutomationArgs.Failed("production fire manager is unavailable");
            var buffer = (Collider[])managerType.GetField("colliderBuffer", all).GetValue(manager);
            var savedBuffer = (Collider[])buffer.Clone();
            var cooldowns = (Dictionary<long, float>)managerType.GetField("cellCooldowns", all).GetValue(manager);
            var fires = (System.Collections.ICollection)managerType.GetField("fires", all).GetValue(manager);
            int sitesBefore = fires.Count;
            long key = 0;
            float oldCooldown = 0f;
            bool hadCooldown = false, changedCooldown = false;
            GameObject shape = null;
            Mesh mesh = null;
            string oldScope = PhysicsWarningProbe.CaptureScope;
            Dictionary<string, object> result;
            try
            {
                shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shape.name = "PhysicsWarningProbe disposable MapBuilding";
                shape.hideFlags = HideFlags.HideAndDontSave;
                shape.transform.position = new Vector3(0f, Datum.LocalSeaY + 10000f, 0f);
                UnityEngine.Object.DestroyImmediate(shape.GetComponent<BoxCollider>());
                mesh = UnityEngine.Object.Instantiate(shape.GetComponent<MeshFilter>().sharedMesh);
                mesh.name = "PhysicsWarningProbe MapBuilding mesh";
                mesh.hideFlags = HideFlags.HideAndDontSave;
                var collider = shape.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
                shape.AddComponent<MapBuilding>();
                Physics.SyncTransforms();
                GlobalPosition position = (collider.bounds.center + new Vector3(2f, 2f, 2f)).ToGlobalPosition();
                Type math = typeof(PhysicsWarningProbe).Assembly.GetType("BoscaliSummer.Core.Math.Deterministic", true);
                key = (long)math.GetMethod("CellKey", all).Invoke(null, new object[] { position.x, position.z, 24f });
                hadCooldown = cooldowns.TryGetValue(key, out oldCooldown);
                cooldowns[key] = Time.timeSinceLevelLoad + 1000f;
                changedCooldown = true;
                Type eventType = managerType.GetNestedType("VehicleExplosionEvent", all);
                object explosion = Activator.CreateInstance(eventType);
                eventType.GetField("Position", all).SetValue(explosion, position);
                eventType.GetField("InstanceId", all).SetValue(explosion, shape.GetInstanceID());
                PhysicsWarningProbe.CaptureScope = "disposable production vehicle-loss fixture";
                int warningsBefore = (int)PhysicsWarningProbe.Read(null)["nativeWarnings"];
                managerType.GetMethod("ProcessVehicleExplosion", all).Invoke(manager, new[] { explosion });
                result = new Dictionary<string, object> {
                    { "ok", true }, { "engine", Application.unityVersion },
                    { "scope", "unchanged production vehicle-loss query; cooldown stops before ignition" },
                    { "visitedFixture", Array.IndexOf(buffer, collider) >= 0 }, { "colliderNonconvex", !collider.convex },
                    { "unsupportedWarnings", (int)PhysicsWarningProbe.Read(null)["nativeWarnings"] - warningsBefore },
                    { "firesUnchanged", fires.Count == sitesBefore },
                };
            }
            finally
            {
                PhysicsWarningProbe.CaptureScope = oldScope;
                if (changedCooldown)
                {
                    if (hadCooldown) cooldowns[key] = oldCooldown;
                    else cooldowns.Remove(key);
                }
                Array.Copy(savedBuffer, buffer, buffer.Length);
                if (shape != null) UnityEngine.Object.DestroyImmediate(shape);
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
            result["cooldownRestored"] = hadCooldown ? cooldowns.TryGetValue(key, out float restored) && restored == oldCooldown : !cooldowns.ContainsKey(key);
            return result;
        }

    }
}
#endif

#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.CombatAdjust.Runtime
{
    /// <summary>Dev-only, hosted native missile collision regression. Changes only three disposable missiles.</summary>
    public static class MissileWaterProof
    {
        private static readonly FieldInfo ImpactFuse = AccessTools.Field(typeof(Missile), "impactFuse");
        private static readonly FieldInfo Warhead = AccessTools.Field(typeof(Missile), "warhead");
        private static readonly FieldInfo Yield = AccessTools.Field(typeof(Missile), "blastYield");
        private static readonly FieldInfo Detonated = AccessTools.Field(typeof(Missile.Warhead), "detonated");
        private static readonly MethodInfo Detect = AccessTools.Method(typeof(Missile), "DetectCollisions");
        private static readonly List<Missile> missiles = new List<Missile>(3);
        private static Vector3 water;
        private static float spawnedAt;

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                throw new InvalidOperationException("Missile water proof requires a hosted single-player scenario.");
            switch (AutomationArgs.Text(args, "action"))
            {
                case "spawn": return Spawn();
                case "impact": return Impact();
                case "cleanup": return Cleanup();
                default: throw new ArgumentException("Use spawn, impact or cleanup.");
            }
        }

        private static Dictionary<string, object> Spawn()
        {
            if (missiles.Count != 0) throw new InvalidOperationException("Clean up the previous missile proof first.");
            if (!GameManager.GetLocalAircraft(out Aircraft owner) || owner == null || owner.disabled)
                throw new InvalidOperationException("A live seated aircraft is required.");
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            Encyclopedia encyclopedia = GameAccess.EncyclopediaOrNull;
            if (spawner == null || !spawner.IsServer || encyclopedia == null || Detect == null ||
                ImpactFuse == null || Warhead == null || Yield == null || Detonated == null)
                throw new InvalidOperationException("Native missile collision capabilities are unavailable.");
            MissileDefinition pick = null;
            float leastYield = 200f;
            foreach (MissileDefinition definition in encyclopedia.missiles)
            {
                if (definition?.unitPrefab == null || string.IsNullOrEmpty(definition.jsonKey) ||
                    definition.jsonKey.StartsWith("VG_", StringComparison.Ordinal)) continue;
                Missile prefab = definition.unitPrefab.GetComponent<Missile>();
                if (prefab == null || !(bool)ImpactFuse.GetValue(prefab) || Warhead.GetValue(prefab) == null) continue;
                float blastYield = (float)Yield.GetValue(prefab);
                if (!(blastYield > 0f) || blastYield >= leastYield) continue;
                pick = definition;
                leastYield = blastYield;
            }
            if (pick == null) throw new InvalidOperationException("No small native impact-fused missile was found.");
            water = FindEmptyWater(owner.GlobalPosition());
            for (int i = 0; i < 3; i++)
            {
                Vector3 position = water + Vector3.right * (i * 200f) + Vector3.up * 300f;
                Missile missile = spawner.SpawnMissile(pick, position, Quaternion.identity, Vector3.zero, null, owner);
                if (missile == null || !missile.IsServer || missile.rb == null || missile.rb.isKinematic)
                    throw new InvalidOperationException("Native missile did not spawn as a dynamic server identity.");
                missiles.Add(missile);
                // Native StartMissile defers an owner-relative position update until FixedUpdate.
                // Let it finish before the impact call; these missiles do not fly or seek meanwhile.
                missile.enabled = false;
            }
            spawnedAt = Time.timeSinceLevelLoad;
            return new Dictionary<string, object> { ["ok"] = true, ["count"] = missiles.Count,
                ["definition"] = pick.jsonKey, ["blastYield"] = leastYield,
                ["nativeModuleMvid"] = typeof(Missile).Module.ModuleVersionId.ToString(),
                ["nativeMethodToken"] = Detect.MetadataToken,
                ["water"] = water.ToGlobalPosition().ToString() };
        }

        private static Dictionary<string, object> Impact()
        {
            if (missiles.Count != 3 || Time.timeSinceLevelLoad - spawnedAt < 0.25f)
                throw new InvalidOperationException("Spawn three missiles and wait for native initialization first.");
            return new Dictionary<string, object> { ["ok"] = true,
                ["armedWater"] = Run(missiles[0], 0, true, true),
                ["unarmedWater"] = Run(missiles[1], 1, false, true),
                ["armedAir"] = Run(missiles[2], 2, true, false) };
        }

        private static Dictionary<string, object> Run(Missile missile, int index, bool armed, bool underwater)
        {
            if (missile == null || missile.disabled || !missile.IsServer || missile.rb == null || missile.rb.isKinematic)
                throw new InvalidOperationException("A prepared native missile is no longer live and dynamic.");
            var warhead = (Missile.Warhead)Warhead.GetValue(missile);
            warhead.Armed = armed;
            ImpactFuse.SetValue(missile, true);
            Vector3 position = water + Vector3.right * (index * 200f) + Vector3.up * (underwater ? -1f : 100f);
            missile.transform.position = position;
            missile.rb.position = position;
            missile.rb.velocity = Vector3.zero;
            int warnings = 0, disabledEvents = 0;
            string warningStack = "", warningType = "";
            Action<Unit> disabled = _ => disabledEvents++;
            Application.LogCallback log = (message, stack, type) =>
            {
                if (message.IndexOf("Setting linear velocity of a kinematic body is not supported", StringComparison.Ordinal) < 0) return;
                warnings++;
                warningType = type.ToString();
                warningStack = string.IsNullOrEmpty(stack) ? new System.Diagnostics.StackTrace().ToString() : stack;
            };
            StackTraceLogType oldWarning = Application.GetStackTraceLogType(LogType.Warning);
            StackTraceLogType oldError = Application.GetStackTraceLogType(LogType.Error);
            StackTraceLogType oldLog = Application.GetStackTraceLogType(LogType.Log);
            missile.onDisableUnit += disabled;
            Application.logMessageReceived += log;
            try
            {
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.Full);
                Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.Full);
                Detect.Invoke(missile, null); // Actual installed native method, including the locally invoked RpcDetonate.
            }
            finally
            {
                Application.logMessageReceived -= log;
                missile.onDisableUnit -= disabled;
                Application.SetStackTraceLogType(LogType.Warning, oldWarning);
                Application.SetStackTraceLogType(LogType.Error, oldError);
                Application.SetStackTraceLogType(LogType.Log, oldLog);
            }
            return new Dictionary<string, object> { ["wasArmed"] = armed, ["underwater"] = underwater,
                ["serverAfter"] = missile.IsServer, ["disabled"] = missile.disabled,
                ["kinematic"] = missile.rb.isKinematic, ["detonated"] = (bool)Detonated.GetValue(warhead),
                ["disabledEvents"] = disabledEvents, ["kinematicWarnings"] = warnings,
                ["warningType"] = warningType, ["warningStack"] = warningStack,
                ["stackNamesDetectCollisions"] = warningStack.IndexOf("DetectCollisions", StringComparison.Ordinal) >= 0 };
        }

        private static Vector3 FindEmptyWater(GlobalPosition origin)
        {
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null || units.Count > 4096) throw new InvalidOperationException("The proof requires a bounded unit roster.");
            for (int ring = 0; ring < 6; ring++)
                for (int bearing = 0; bearing < 8; bearing++)
                {
                    float angle = bearing * Mathf.PI / 4f, radius = 10000f + ring * 7500f;
                    Vector3 point = (origin + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius)).ToLocalPosition();
                    point.y = Datum.LocalSeaY;
                    if (Physics.Raycast(point + Vector3.up * 10000f, Vector3.down, out RaycastHit hit, 20000f,
                        (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask) && hit.point.y > Datum.LocalSeaY - 20f) continue;
                    bool occupied = false;
                    foreach (Unit unit in units)
                        if (unit != null && (unit.transform.position - point).sqrMagnitude < 1000f * 1000f) { occupied = true; break; }
                    if (!occupied) return point;
                }
            throw new InvalidOperationException("No empty deep-water point was found for the proof.");
        }

        private static Dictionary<string, object> Cleanup()
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            foreach (Missile missile in missiles)
                if (missile != null && missile.IsServer) spawner.ServerObjectManager.Destroy(missile.gameObject);
            missiles.Clear();
            return new Dictionary<string, object> { ["ok"] = true, ["remaining"] = missiles.Count };
        }
    }
}
#endif

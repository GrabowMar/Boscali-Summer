using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>
    /// Explicit nomodkit single-player test hook (tests/ingame/vanguard.json); inert outside a sim run.
    /// Launches Vanguard missiles straight from the spawner so a scenario needs no loadout UI.
    /// </summary>
    public static class VanguardAutomation
    {
        private static readonly Dictionary<string, Missile> Launched = new Dictionary<string, Missile>();
        private static readonly Dictionary<string, float> TopSpeed = new Dictionary<string, float>();
        private static Missile threat;
        private static int threatsGone;

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("VanguardAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            Aircraft lead = FindAircraft(Text(args, "faction") ?? "Boscali");
            Aircraft bandit = FindAircraft(Text(args, "enemy") ?? "Primeva");
            switch (Text(args, "action"))
            {
                case "catalog": return Catalog();
                case "launch": return Launch(Text(args, "key"), lead, Text(args, "at") == "threat" ? threat : (Unit)bandit);
                case "threat": return Threat(bandit, lead, Number(args, "range", 5000f));
                case "strike":
                    Modules.Vanguard.Runtime.DroneOrders.Order(lead, bandit);
                    return Status(lead);
                case "status": return Status(lead);
                case "showcase": return Showcase(lead);
                case "view": return View(Text(args, "angle") ?? "front");
                default: return Failure("VanguardAutomation", "Step", "unknown action");
            }
        }

        // ---- showcase: inert copies of every Vanguard prefab, lit by the game, for model review captures

        private static readonly string[] ShowcaseMounts = { "VG_Remora_AGM_heavy_single", "VG_Aegis_Pod", "VG_Lance_Pod" };
        private const float Spacing = 7f;
        private static GameObject showcase;
        private static GlobalPosition showcaseAnchor;

        private static Dictionary<string, object> Showcase(Aircraft lead)
        {
            if (lead == null) return Failure("VanguardAutomation", "showcase", "no friendly aircraft");
            if (showcase != null) UnityEngine.Object.Destroy(showcase);
            showcase = new GameObject("VanguardShowcase");
            showcaseAnchor = lead.GlobalPosition() + Vector3.up * 400f;
            showcase.transform.position = showcaseAnchor.ToLocalPosition();
            var prefabs = new List<GameObject>();
            foreach (MissileDefinition m in Encyclopedia.i.missiles)
                if (m != null && m.unitPrefab != null && VanguardKeys.RoleOf(m.jsonKey) != VanguardRole.None &&
                    m.jsonKey != VanguardKeys.MaldJ) prefabs.Add(m.unitPrefab);
            foreach (WeaponMount w in Encyclopedia.i.weaponMounts)
                if (w != null && w.prefab != null && Array.IndexOf(ShowcaseMounts, w.jsonKey) >= 0) prefabs.Add(w.prefab);
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject copy = Inert(prefabs[i], showcase.transform);
                int row = i / 4, col = i % 4;
                copy.transform.localPosition = new Vector3((col - 1.5f) * Spacing, -row * 4f, row * 2f);
                copy.transform.localRotation = Quaternion.identity;
            }
            return new Dictionary<string, object> { ["ok"] = true, ["shown"] = prefabs.Count };
        }

        // Instantiate under an inactive holder so no Awake runs, then strip everything but visuals.
        private static GameObject Inert(GameObject prefab, Transform parent)
        {
            var holder = new GameObject("hold");
            holder.SetActive(false);
            GameObject g = UnityEngine.Object.Instantiate(prefab, holder.transform);
            for (int pass = 0; pass < 4; pass++)
                foreach (Component c in g.GetComponentsInChildren<Component>(true))
                {
                    if (c is Transform || c is MeshFilter || c is Renderer) continue;
                    try { UnityEngine.Object.DestroyImmediate(c); }
                    catch (Exception) { /* a dependent component goes first; next pass gets this one */ }
                }
            g.transform.SetParent(parent, false);
            UnityEngine.Object.Destroy(holder);
            return g;
        }

        private static Dictionary<string, object> View(string angle)
        {
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (camera == null || showcase == null) return Failure("VanguardAutomation", "view", "showcase first");
            Vector3 offset;
            Quaternion rotation;
            switch (angle)
            {
                case "rear": offset = new Vector3(8f, 4f, -16f); break;
                case "close": offset = new Vector3(-6f, 2f, 9f); break;
                default: offset = new Vector3(10f, 5f, 20f); break;
            }
            GlobalPosition at = showcaseAnchor + offset + (angle == "close" ? new Vector3(-1.5f * Spacing, 0f, 0f) : Vector3.zero);
            Vector3 look = (showcaseAnchor + (angle == "close" ? new Vector3(-1.5f * Spacing, -1f, 0f) : new Vector3(0f, -2f, 1f))) - at;
            rotation = Quaternion.LookRotation(look);
            camera.SetCameraPosition(at, rotation);
            camera.cameraVelocity = Vector3.zero;
            camera.allowInputs = false;
            camera.SetDesiredFoV(angle == "close" ? 40f : 60f, angle == "close" ? 40f : 60f);
            return new Dictionary<string, object> { ["ok"] = true };
        }

        private static Aircraft FindAircraft(string faction)
        {
            foreach (Aircraft a in UnityEngine.Object.FindObjectsOfType<Aircraft>())
                if (!a.disabled && a.NetworkHQ != null && a.NetworkHQ.faction != null &&
                    a.NetworkHQ.faction.factionName.IndexOf(faction, StringComparison.OrdinalIgnoreCase) >= 0) return a;
            return null;
        }

        private static Dictionary<string, object> Catalog()
        {
            Encyclopedia e = Encyclopedia.i;
            if (e == null) return Failure("VanguardAutomation", "catalog", "no encyclopedia");
            int missiles = 0, mounts = 0;
            foreach (MissileDefinition m in e.missiles)
                if (m != null && VanguardKeys.RoleOf(m.jsonKey) != VanguardRole.None) missiles++;
            foreach (WeaponMount m in e.weaponMounts)
                if (m != null && m.jsonKey != null && m.jsonKey.StartsWith("VG_", StringComparison.Ordinal)) mounts++;
            return new Dictionary<string, object> { ["ok"] = true, ["missiles"] = missiles, ["mounts"] = mounts };
        }

        private static MissileDefinition Find(Func<MissileDefinition, bool> match)
        {
            foreach (MissileDefinition m in Encyclopedia.i.missiles)
                if (m != null && m.unitPrefab != null && match(m)) return m;
            return null;
        }

        private static Dictionary<string, object> Launch(string key, Aircraft lead, Unit target)
        {
            if (lead == null) return Failure("VanguardAutomation", "launch", "no friendly aircraft");
            MissileDefinition def = Find(m => m.jsonKey == key);
            if (def == null) return Failure("VanguardAutomation", "launch", "unknown missile " + key);
            Transform t = lead.transform;
            Missile missile = NetworkSceneSingleton<Spawner>.i.SpawnMissile(def, t.position - t.up * 3f + t.forward * 10f,
                t.rotation, lead.rb.velocity, target, lead);
            Launched[key] = missile;
            TopSpeed[key] = 0f;
            return Status(lead);
        }

        // An enemy radar AAM fired at the lead from behind, for the decoy / drone / AEGIS checks.
        private static Dictionary<string, object> Threat(Aircraft bandit, Aircraft lead, float range)
        {
            if (lead == null || bandit == null) return Failure("VanguardAutomation", "threat", "need lead and bandit");
            MissileDefinition def = Find(m => m.jsonKey.StartsWith("AAM", StringComparison.Ordinal) &&
                m.unitPrefab.GetComponent<ARHSeeker>() != null);
            if (def == null) return Failure("VanguardAutomation", "threat", "no ARH air-to-air missile");
            Transform t = lead.transform;
            Vector3 from = t.position - t.forward * range + Vector3.up * 200f;
            threat = NetworkSceneSingleton<Spawner>.i.SpawnMissile(def, from, Quaternion.LookRotation(t.position - from),
                (t.position - from).normalized * 600f, lead, bandit);
            threat.onDisableUnit += _ => threatsGone++;
            return Status(lead);
        }

        private static Dictionary<string, object> Status(Aircraft lead)
        {
            var state = new Dictionary<string, object> { ["ok"] = true, ["threatsGone"] = threatsGone };
            if (threat != null && !threat.disabled)
                state["threatOnLead"] = lead != null && threat.targetID.Id == lead.persistentID.Id ? 1 : 0;
            foreach (KeyValuePair<string, Missile> pair in Launched)
            {
                Missile m = pair.Value;
                string k = pair.Key;
                bool alive = m != null && !m.disabled;
                state[k + "_alive"] = alive ? 1 : 0;
                if (!alive) continue;
                TopSpeed[k] = Mathf.Max(TopSpeed[k], m.speed);
                state[k + "_speed"] = m.speed;
                state[k + "_topSpeed"] = TopSpeed[k];
                state[k + "_altitude"] = m.GlobalPosition().y;
                state[k + "_guided"] = Modules.Vanguard.Runtime.VanguardRegistry.TryGet(m, out _) ? 1 : 0;
                if (lead != null) state[k + "_fromLead"] = FastMath.Distance(m.GlobalPosition(), lead.GlobalPosition());
            }
            return state;
        }
    }
}

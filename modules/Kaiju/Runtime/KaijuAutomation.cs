#if !PUBLIC_RELEASE
using System;
using System.Collections;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Kaiju.Domain;
using BoscaliSummer.Modules.Kaiju.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Kaiju
{
    /// <summary>nomodkit single-player test hook (tests/ingame/kaiju.json); inert outside a sim run.</summary>
    public static class KaijuAutomation
    {
        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("KaijuAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            KaijuDirector director = KaijuDirector.Instance;
            if (director == null) return Failure("KaijuAutomation", "Step", "Kaiju module not installed");
            string action = Text(args, "action") ?? "status";
            string message = null;
            switch (action)
            {
                case "summon":
                    message = director.Summon();
                    break;
                case "dismiss":
                    message = director.Dismiss();
                    break;
                case "force":
                    if (director.Brain == null) return Failure("KaijuAutomation", action, "no kaiju");
                    director.Brain.Force((KaijuAction)Enum.Parse(typeof(KaijuAction), Text(args, "what") ?? "Roar", true));
                    break;
                case "hit":
                    // Damage weak spot(s) through the game's own TakeDamage: "index" one spot, or "count" the first N non-heart spots.
                    if (director.Body == null) return Failure("KaijuAutomation", action, "no kaiju");
                    float impact = Number(args, "impact", 1000f);
                    int first = (int)Number(args, "index", -1f), count = (int)Number(args, "count", 1f);
                    for (int i = 0, done = 0; i < director.Body.Nodes.Length && done < count; i++)
                    {
                        if (first >= 0 ? i != first : i == KaijuRig.HeartIndex || !director.Body.Alive(i)) continue;
                        Building node = director.Body.Nodes[i];
                        if (node == null) continue;
                        foreach (UnitPart part in node.GetComponentsInChildren<UnitPart>()) part.TakeDamage(0f, 0f, 1f, 0f, impact, default);
                        done++;
                    }
                    break;
                case "fire":
                    // Queue a trial: the game's own munitions at weak spots ("spots": "0,3,9"), one at a time until each dies or
                    // "max" shots. "weapon" is a missile or bomb jsonKey (Spawner.SpawnMissile), "gun" or "gun:<mount jsonKey>"
                    // (10-round bursts of the shooter's own gun, else that mount's, default gun_20mm_internal) or "break" (kill
                    // every live weak spot but the heart, which then opens).
                    if (director.Body == null) return Failure("KaijuAutomation", action, "no kaiju");
                    Trials.Enqueue((Text(args, "weapon") ?? "AGM1", Text(args, "spots") ?? "0", (int)Number(args, "max", 6f), Number(args, "range", 2000f)));
                    if (!trialRunning) director.StartCoroutine(RunTrials(director.Body));
                    break;
                case "fx":
                    if (director.Body == null) return Failure("KaijuAutomation", action, "no kaiju");
                    message = Census(director.Body, Text(args, "hide"), Text(args, "requeue"));
                    break;
                case "look":
                    // After a scenario "camera" op (free camera): sit "distance" m out on "bearing" deg off the kaiju's nose,
                    // "height" m above its chest, looking at the chest raised by "lift" m. The kaiju walks; fixed poses miss it.
                    if (director.Body == null || SceneSingleton<CameraStateManager>.i == null) return Failure("KaijuAutomation", action, "no kaiju or camera");
                    Vector3 chest = director.Body.Chest;
                    Vector3 eye = chest + Quaternion.Euler(0f, director.Body.transform.eulerAngles.y + Number(args, "bearing", 0f), 0f) *
                                  Vector3.forward * Number(args, "distance", 6000f) + Vector3.up * Number(args, "height", 0f);
                    SceneSingleton<CameraStateManager>.i.SetCameraPosition(eye.ToGlobalPosition(),
                        Quaternion.LookRotation(chest + Vector3.up * Number(args, "lift", 0f) - eye));
                    SceneSingleton<CameraStateManager>.i.cameraVelocity = Vector3.zero;
                    break;
            }
            return Status(director, action, message);
        }

        private static Dictionary<string, object> Status(KaijuDirector director, string action, string message)
        {
            KaijuBody body = director.Body;
            var result = new Dictionary<string, object> { ["ok"] = true, ["step"] = action, ["message"] = message, ["exists"] = body != null };
            if (body == null) return result;
            int alive = 0, spawned = 0;
            float worstPose = 0f;
            for (int i = 0; i < body.Nodes.Length; i++)
            {
                if (body.Nodes[i] == null) continue;
                spawned++;
                if (!body.Nodes[i].disabled)
                {
                    alive++;
                    worstPose = Mathf.Max(worstPose, Vector3.Distance(body.Nodes[i].transform.position, body.SpotWorld(i)));
                }
            }
            Vector3 p = body.Position.AsVector3();
            result["action"] = body.Action.ToString();
            result["dying"] = body.Action == KaijuAction.Death ? 1 : 0;
            result["position"] = new[] { p.x, p.y, p.z };
            result["yaw"] = body.Yaw;
            result["spawned"] = spawned;
            result["alive"] = alive;
            result["broken"] = body.Broken;
            result["heart"] = body.Nodes[KaijuRig.HeartIndex] != null ? (body.Nodes[KaijuRig.HeartIndex].disabled ? "dead" : "open") : "shut";
            result["nodePoseError"] = worstPose;
            result["nodeName"] = body.Nodes[0] != null ? body.Nodes[0].unitName : null;
            result["nodeHq"] = body.Nodes[0] != null && body.Nodes[0].NetworkHQ != null ? body.Nodes[0].NetworkHQ.faction.factionName : null;
            result["hasAim"] = body.HasAim;
            result["sounds"] = body.Sounds;
            result["trialRunning"] = trialRunning ? 1 : 0;
            result["trial"] = string.Join(" | ", TrialLog);
            if (GameManager.GetLocalAircraft(out Aircraft me) && me != null)
            {
                result["playerDistance"] = Vector3.Distance(me.transform.position, body.Chest);
                result["playerAlive"] = !me.disabled;
            }
            return result;
        }

        private static readonly List<string> TrialLog = new List<string>();
        private static readonly Queue<(string weapon, string spots, int max, float range)> Trials = new Queue<(string, string, int, float)>();
        private static bool trialRunning;

        private static IEnumerator RunTrials(KaijuBody body)
        {
            trialRunning = true;
            try
            {
                while (Trials.Count > 0 && body != null)
                {
                    var job = Trials.Dequeue();
                    yield return Trial(body, job.weapon, job.spots, job.max, job.range);
                }
            }
            finally
            {
                Trials.Clear();
                trialRunning = false;
            }
        }

        /// <summary>Fires one munition at a time from <paramref name="range"/> m outside each spot and logs its hit points
        /// after every shot, so a log line reads "SPINE SAC 1 x AGM1: dead after 3 [64 29 x]" ("x": the game deleted it).</summary>
        private static IEnumerator Trial(KaijuBody body, string weapon, string spots, int max, float range)
        {
            if (weapon == "break")
            {
                int broke = 0;
                for (int i = 0; i < body.Nodes.Length; i++)
                    if (i != KaijuRig.HeartIndex && body.Alive(i))
                    {
                        foreach (UnitPart part in body.Nodes[i].GetComponentsInChildren<UnitPart>()) part.TakeDamage(0f, 0f, 1f, 0f, 1000f, default);
                        broke++;
                    }
                TrialLog.Add("broke " + broke);
                yield return new WaitForSeconds(3f); // the brain opens the heart on its next tick
                yield break;
            }
            bool gun = weapon.StartsWith("gun", StringComparison.Ordinal);
            MissileDefinition def = gun ? null : Encyclopedia.i.missiles.Find(d => d != null && (d.jsonKey == weapon || d.name == weapon));
            Missile prefab = def != null && def.unitPrefab != null ? def.unitPrefab.GetComponent<Missile>() : null;
            if (!gun && prefab == null)
            {
                TrialLog.Add(weapon + ": no such missile");
                yield break;
            }
            if (prefab != null) TrialLog.Add(weapon + " yield " + prefab.GetYield() + " pierce " + prefab.GetPierce());
            foreach (string token in spots.Split(','))
            {
                if (!int.TryParse(token.Trim(), out int i) || i < 0 || i >= body.Nodes.Length) continue;
                for (float wait = Time.time + 10f; body.Nodes[i] == null && Time.time < wait;) yield return null; // the heart spawns late
                var hp = new List<string>();
                int shots = 0;
                while (body != null && body.Alive(i) && shots < max)
                {
                    Building node = body.Nodes[i];
                    Aircraft shooter = Shooter(node);
                    if (shooter == null)
                    {
                        hp.Add("no-shooter");
                        break;
                    }
                    Vector3 spot = node.transform.position, dir = -Approach(body, spot, node, range), from = spot - dir * range;
                    Missile shot = null;
                    if (gun)
                    {
                        // The game's own hit path for a round (what a host bullet or a client's claimed hit runs), at 90 %
                        // muzzle speed square on the sphere: misses are not modelled, so this counts hits.
                        WeaponInfo info = shooter.GetComponentInChildren<Gun>(true)?.info;
                        if (info == null && Encyclopedia.WeaponLookup.TryGetValue(weapon.Length > 4 ? weapon.Substring(4) : "gun_20mm_internal",
                                out WeaponMount mount)) info = mount.info;
                        if (info == null)
                        {
                            hp.Add("no-gun");
                            break;
                        }
                        Vector3 skin = node.transform.InverseTransformPoint(spot - dir * (KaijuBody.HitboxRadius(i) + 2f));
                        for (int r = 0; r < 10; r++) shooter.RegisterHit(node, skin, dir * (info.muzzleVelocity * 0.9f), info);
                        shots += 10;
                    }
                    else
                    {
                        float t = range / 250f;
                        Vector3 v = (spot - from) / t + Vector3.up * (4.9f * t); // ballistic: a dumb bomb lands on the spot too
                        shooter.NetworkHQ?.RpcUpdateTrackingInfo(node.persistentID);
                        shot = NetworkSceneSingleton<Spawner>.i.SpawnMissile(def, from, Quaternion.LookRotation(v), v, node, shooter);
                        shots++;
                    }
                    float until = Time.time + 25f;
                    while (Time.time < until && shot != null && !shot.disabled) yield return null;
                    // Where it burst: metres outside the hitbox surface (a hull or the ground in the way shows as a gap).
                    string burst = shot != null && node != null
                        ? "@" + (Vector3.Distance(shot.transform.position, node.transform.position) - KaijuBody.HitboxRadius(i)).ToString("+0;-0")
                        : "";
                    yield return new WaitForSeconds(gun ? 0.5f : 1.5f);
                    hp.Add(Hp(body, i) + burst);
                }
                string line = KaijuRig.Spots[i].Name + " x " + weapon + ": " + (body != null && body.Alive(i) ? "ALIVE" : "dead") +
                              " after " + shots + " [" + string.Join(" ", hp) + "]";
                TrialLog.Add(line);
                Debug.Log("[Kaiju] trial " + line);
            }
        }

        /// <summary>Where a pilot would shoot from: the first direction of a fan (outward from the body first, then round it,
        /// shallow before steep) from which a ray reaches the hitbox before any hull, terrain or other weak spot.</summary>
        private static Vector3 Approach(KaijuBody body, Vector3 spot, Building node, float range)
        {
            Vector3 flat = spot - body.Chest;
            flat.y = 0f;
            flat = flat.sqrMagnitude > 1f ? flat.normalized : body.transform.forward;
            Collider hitbox = node.GetComponentInChildren<SphereCollider>();
            foreach (float yaw in new[] { 0f, 40f, -40f, 80f, -80f, 130f, -130f, 180f })
                foreach (float pitch in new[] { 15f, 40f, 70f, -10f })
                {
                    Vector3 side = Quaternion.AngleAxis(yaw, Vector3.up) * flat;
                    Vector3 dir = side * Mathf.Cos(pitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad);
                    if (Physics.Raycast(spot + dir * range, -dir, out RaycastHit hit, range + 200f, ~0, QueryTriggerInteraction.Ignore) &&
                        hit.collider == hitbox) return dir;
                }
            return (flat + Vector3.up * 0.3f).normalized;
        }

        private static string Hp(KaijuBody body, int i)
        {
            Building node = body.Nodes[i];
            UnitPart part = node != null ? node.GetComponentInChildren<UnitPart>() : null;
            return part == null ? "x" : part.hitPoints.ToString("0");
        }

        /// <summary>A live aircraft to own the shot, preferably on the other side from the hardpoint.</summary>
        private static Aircraft Shooter(Unit target)
        {
            Aircraft any = null;
            foreach (Aircraft a in UnitRegistry.allAircraft)
            {
                if (a == null || a.disabled) continue;
                if (a.NetworkHQ != target.NetworkHQ) return a;
                if (any == null) any = a;
            }
            return any;
        }

        /// <summary>Every enabled renderer within 6 km of the chest, grouped by type, material, shader and queue (the effects
        /// that can cut the cloud deck). <paramref name="hide"/> switches off those whose material or shader name contains it;
        /// <paramref name="requeue"/> puts matching materials below the transparent range back at 3000.</summary>
        private static string Census(KaijuBody body, string hide, string requeue)
        {
            var groups = new SortedDictionary<string, int>(StringComparer.Ordinal);
            Vector3 chest = body.Chest;
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || r.forceRenderingOff || r.transform.IsChildOf(body.transform) ||
                    (r.bounds.center - chest).sqrMagnitude > 6000f * 6000f) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string shader = m.shader != null ? m.shader.name : "?";
                    string key = r.GetType().Name + " | " + m.name + " | " + shader + " | " + m.renderQueue;
                    if (!string.IsNullOrEmpty(hide) && (m.name + shader).IndexOf(hide, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        r.enabled = false;
                        key = "HIDDEN " + key;
                    }
                    if (!string.IsNullOrEmpty(requeue) && m.renderQueue < 2500 && m.name.IndexOf(requeue, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        m.renderQueue = 3000;
                        key = "REQUEUED " + key;
                    }
                    groups.TryGetValue(key, out int n);
                    groups[key] = n + 1;
                }
            }
            var lines = new List<string>();
            foreach (KeyValuePair<string, int> g in groups) lines.Add(g.Key + " x" + g.Value);
            Debug.Log("[Kaiju] fx census:\n" + string.Join("\n", lines));
            return string.Join("\n", lines);
        }
    }
}
#endif

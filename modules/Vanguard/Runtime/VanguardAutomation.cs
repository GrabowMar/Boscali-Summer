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
        private static readonly Dictionary<string, float> Deaths = new Dictionary<string, float>();
        private static Missile threat;
        private static Unit groundTarget;
        private static Unit testShip;
        private static FactionHQ enemyHq;
        private static int launchCount;
        private static Aircraft trackedLead;
        private static int threatsGone;

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("VanguardAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            Aircraft lead = FindAircraft(Text(args, "faction") ?? "Boscali");
            Aircraft bandit = FindAircraft(Text(args, "enemy") ?? "Primeva");
            if (bandit != null) enemyHq = bandit.NetworkHQ; // REMORA STRIKE may kill the bandit before batch-2 steps
            if (lead != null && lead != trackedLead)
            {
                trackedLead = lead;
                lead.onDisableUnit += _ =>
                {
                    Deaths["lead_deathTime"] = Time.timeSinceLevelLoad;
                    Deaths["lead_deathAltitude"] = (float)lead.GlobalPosition().y;
                };
            }
            switch (Text(args, "action"))
            {
                case "catalog": return Catalog();
                case "launch":
                    string at = Text(args, "at");
                    return Launch(Text(args, "key"), lead, at == "threat" ? threat : at == "ground" ? groundTarget : at == "ship" ? testShip : (Unit)bandit);
                case "lure":
                    Modules.Vanguard.Domain.SeductionRule.TowedRearChance = Number(args, "chance", 0.35f);
                    Modules.Vanguard.Domain.SeductionRule.TowedFrontChance = Number(args, "chance", 0.12f);
                    return Status(lead);
                case "pk":
                    Modules.Vanguard.Runtime.VanguardFlight.InterceptKillChance = Number(args, "chance", 0.7f);
                    return Status(lead);
                case "snapg":
                    Modules.Vanguard.Domain.TowedTrail.SnapG = Number(args, "g", 7f);
                    return Status(lead);
                case "battery":
                    Modules.Vanguard.Runtime.PayloadLifetime.BatterySeconds = Number(args, "seconds", 240f);
                    return Status(lead);
                case "ground": return Ground(lead, Number(args, "range", 12000f));
                case "ship": return SpawnTestShip(lead, Number(args, "range", 14000f), Text(args, "type") ?? "Frigate1", (int)Number(args, "from", 0f));
                case "threat": return Threat(bandit, lead, Number(args, "range", 5000f), Text(args, "seeker") ?? "ARH", Number(args, "climb", 200f));
                case "strike":
                    Modules.Vanguard.Runtime.DroneOrders.Order(lead, bandit);
                    return Status(lead);
                case "equip": return Equip();
                case "skyview": return SkyView(Text(args, "angle") ?? "side");
                case "drain": return Drain(receiver, Number(args, "fuel", 0.3f));
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

        // ---- SKYWELL: an AI Tarantula with the kit ahead of the lead; the lead (AI) is drained and called in.

        private static Aircraft tanker, receiver;
        private static readonly System.Reflection.FieldInfo LeakRate =
            typeof(FuelTank).GetField("leakRate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        private static Dictionary<string, object> Equip()
        {
            tanker = receiver = null;
            foreach (Aircraft a in UnityEngine.Object.FindObjectsOfType<Aircraft>())
            {
                if (a.disabled || a.definition == null) continue;
                if (a.definition.unitName.IndexOf("Tarantula", StringComparison.OrdinalIgnoreCase) >= 0) tanker = a;
                else if (a.definition.unitName.IndexOf("FS-20", StringComparison.OrdinalIgnoreCase) >= 0) receiver = a;
            }
            WeaponMount kit = null;
            foreach (WeaponMount w in Encyclopedia.i.weaponMounts)
                if (w != null && w.jsonKey == VanguardKeys.SkywellMount) kit = w;
            if (tanker == null || receiver == null || kit == null) return Failure("VanguardAutomation", "equip", "need a Tarantula, an FS-20 and the kit");
            var loadout = new NuclearOption.SavedMission.Loadout { weapons = new List<WeaponMount>() };
            for (int i = 0; i < tanker.weaponManager.hardpointSets.Length; i++) loadout.weapons.Add(i == 0 ? kit : null);
            tanker.Networkloadout = loadout;
            NuclearOption.SavedMission.Loadout armed = null;
            foreach (StandardLoadout sl in receiver.definition.aircraftParameters.StandardLoadouts ?? Array.Empty<StandardLoadout>())
                if (armed == null && sl?.loadout?.weapons != null && sl.loadout.weapons.Exists(w => w != null)) armed = sl.loadout;
            if (armed != null) receiver.Networkloadout = armed;
            return new Dictionary<string, object> { ["ok"] = true,
                ["kit"] = Modules.Vanguard.Runtime.SkywellBoard.KitStation(tanker) != null ? 1 : 0,
                ["armed"] = armed != null ? 1 : 0 };
        }

        private static Dictionary<string, object> SkyView(string angle)
        {
            if (tanker == null) return Failure("VanguardAutomation", "skyview", "equip first");
            SkyChase.Offset = angle == "off" ? (Vector3?)null
                : angle == "rear" ? new Vector3(6f, 4f, -45f)
                : angle == "close" ? new Vector3(14f, -3f, -8f) : new Vector3(38f, 0f, -16f);
            SkyChase.Target = tanker;
            if (SkyChase.Offset != null && UnityEngine.Object.FindObjectOfType<SkyChase>() == null)
                new GameObject("SkywellChase").AddComponent<SkyChase>();
            return new Dictionary<string, object> { ["ok"] = true,
                ["mountFromTanker"] = tanker.transform.InverseTransformPoint(Modules.Vanguard.Runtime.SkywellBoard.Mount(tanker).position).ToString("F1"),
                ["rig"] = Modules.Vanguard.Presentation.SkywellVisuals.Probe };
        }

        /// <summary>Sim-only chase camera, re-posed every frame after the floating-origin shift.</summary>
        [DefaultExecutionOrder(2000)]
        private sealed class SkyChase : MonoBehaviour
        {
            public static Aircraft Target;
            public static Vector3? Offset;

            private void LateUpdate()
            {
                CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
                if (camera == null || Target == null || Offset == null) return;
                Transform t = Target.transform;
                Vector3 mount = Modules.Vanguard.Runtime.SkywellBoard.Mount(Target).position;
                Vector3 eye = mount + t.rotation * Offset.Value;
                Vector3 look = mount + t.rotation * new Vector3(0f, -6f, -14f);
                camera.SetCameraPosition(eye.ToGlobalPosition(), Quaternion.LookRotation(look - eye, t.up));
                camera.cameraVelocity = Vector3.zero;
            }
        }

        private static Dictionary<string, object> Drain(Aircraft lead, float fuel)
        {
            if (lead == null) return Failure("VanguardAutomation", "drain", "no friendly aircraft");
            foreach (FuelTank t in lead.GetFuelTanks()) t.Refuel(fuel);
            int fired = 0;
            foreach (WeaponStation s in lead.weaponStations)
                if (s?.WeaponInfo != null && !s.WeaponInfo.cargo && !s.WeaponInfo.gun && s.Ammo > 0 && fired < 2)
                {
                    s.LaunchMount(lead, null, lead.GlobalPosition() + lead.transform.forward * 3000f);
                    fired++;
                }
            return new Dictionary<string, object> { ["ok"] = true, ["fired"] = fired, ["fuel"] = lead.GetFuelLevel(),
                ["missing"] = Modules.Vanguard.Runtime.SkywellService.MissingRounds(lead) };
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
            // Stagger launches sideways: back-to-back spawns at one point collide once they turn tangible (1.5 s).
            Vector3 slot = t.right * (((launchCount++ % 3) - 1) * 20f);
            Missile missile = NetworkSceneSingleton<Spawner>.i.SpawnMissile(def, t.position - t.up * 3f + t.forward * 10f + slot,
                t.rotation, lead.rb.velocity, target, lead);
            Launched[key] = missile;
            Unit aimed = target;
            missile.onDisableUnit += _ =>
            {
                Deaths[key + "_deathAge"] = missile.timeSinceSpawn;
                Deaths[key + "_deathAltitude"] = (float)missile.GlobalPosition().y;
                Deaths[key + "_deathAgl"] = missile.radarAlt;
                Deaths[key + "_deathSpeed"] = missile.speed;
                if (aimed != null) Deaths[key + "_deathRange"] = FastMath.Distance(missile.GlobalPosition(), aimed.GlobalPosition());
            };
            TopSpeed[key] = 0f;
            return Status(lead);
        }

        // An enemy Hexhound on dry, flat ground `range` ahead of the lead (sweeps +/-60 deg and +6 km until one fits).
        private static Dictionary<string, object> Ground(Aircraft lead, float range)
        {
            if (lead == null || enemyHq == null) return Failure("VanguardAutomation", "ground", "need lead and an enemy HQ");
            if (!Encyclopedia.Lookup.TryGetValue("UGV1_grenade", out UnitDefinition def))
                return Failure("VanguardAutomation", "ground", "UGV1_grenade not in Encyclopedia.Lookup");
            Vector3 fwd = lead.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            for (float d = range; d <= range + 6000f; d += 1000f)
                for (int b = -60; b <= 60; b += 15)
                {
                    Vector3 dir = Quaternion.Euler(0f, b, 0f) * fwd;
                    Vector3 desired = lead.transform.position + dir * d;
                    desired.y = Datum.LocalSeaY + 1400f; // DryGround probes from +500 m: start above any hill
                    if (!GroundPlacement.TryPlace(def, desired, Quaternion.LookRotation(-dir), out Vector3 point)) continue;
                    groundTarget = NetworkSceneSingleton<Spawner>.i.SpawnVehicle(def.unitPrefab, point.ToGlobalPosition(),
                        Quaternion.LookRotation(-dir), Vector3.zero, enemyHq, "VG_TestTarget", 1f, true, null);
                    return new Dictionary<string, object> { ["ok"] = groundTarget != null, ["range"] = d, ["bearing"] = b };
                }
            return Failure("VanguardAutomation", "ground", "no dry flat ground ahead");
        }

        // An enemy ship (default Argus frigate) on deep water near `range` from the lead (sweeps 16 bearings).
        private static Dictionary<string, object> SpawnTestShip(Aircraft lead, float range, string type, int from)
        {
            if (lead == null || enemyHq == null) return Failure("VanguardAutomation", "ship", "need lead and an enemy HQ");
            if (!Encyclopedia.Lookup.TryGetValue(type, out UnitDefinition def))
                return Failure("VanguardAutomation", "ship", type + " not in Encyclopedia.Lookup");
            for (float d = range; d <= range + 8000f; d += 2000f)
                for (int b = from; b < from + 360; b += 22)
                {
                    Vector3 p = lead.transform.position + Quaternion.Euler(0f, b, 0f) * lead.transform.forward * d;
                    p.y = Datum.LocalSeaY + 3000f; // probe from above any hill
                    bool land = Physics.Raycast(p, Vector3.down, out RaycastHit hit, 3500f, (int)PhysicsLayers.StaticsMask) &&
                        hit.point.y > Datum.LocalSeaY - 15f;
                    if (land) continue;
                    p.y = Datum.LocalSeaY;
                    testShip = NetworkSceneSingleton<Spawner>.i.SpawnShip(def.unitPrefab, p.ToGlobalPosition(),
                        Quaternion.Euler(0f, b + 90f, 0f), enemyHq, "VG_TestShip", 1f, false);
                    return new Dictionary<string, object> { ["ok"] = testShip != null, ["range"] = d, ["bearing"] = b };
                }
            return Failure("VanguardAutomation", "ship", "no deep water near the lead");
        }

        // An enemy radar AAM fired at the lead from behind, for the decoy / drone / AEGIS checks.
        private static Dictionary<string, object> Threat(Aircraft bandit, Aircraft lead, float range, string seeker, float climb)
        {
            if (lead == null || bandit == null) return Failure("VanguardAutomation", "threat", "need lead and bandit");
            bool ir = seeker == "IR"; // IR threats ignore lures, so the AEGIS check cannot be stolen by MALD/REMORA
            MissileDefinition def = Find(m => m.jsonKey.StartsWith("AAM", StringComparison.Ordinal) &&
                (ir ? m.unitPrefab.GetComponent<IRSeeker>() != null : m.unitPrefab.GetComponent<ARHSeeker>() != null));
            if (def == null) return Failure("VanguardAutomation", "threat", "no " + seeker + " air-to-air missile");
            Transform t = lead.transform;
            Vector3 from = t.position - t.forward * range + Vector3.up * climb;
            threat = NetworkSceneSingleton<Spawner>.i.SpawnMissile(def, from, Quaternion.LookRotation(t.position - from),
                (t.position - from).normalized * 600f, lead, bandit);
            Missile shot = threat;
            Aircraft victim = lead;
            threat.onDisableUnit += _ =>
            {
                threatsGone++;
                if (victim != null) Deaths["threat_toLead"] = FastMath.Distance(shot.GlobalPosition(), victim.GlobalPosition());
                if (Launched.TryGetValue(VanguardKeys.AleX, out Missile decoy) && decoy != null)
                    Deaths["threat_toDecoy"] = FastMath.Distance(shot.GlobalPosition(), decoy.GlobalPosition());
                Deaths["threat_time"] = Time.timeSinceLevelLoad;
            };
            return Status(lead);
        }

        private static Dictionary<string, object> Status(Aircraft lead)
        {
            var state = new Dictionary<string, object> { ["ok"] = true, ["threatsGone"] = threatsGone, ["time"] = Time.timeSinceLevelLoad };
            state["waterEntries"] = Modules.Vanguard.Runtime.VanguardStats.WaterEntries;
            state["shipHits"] = Modules.Vanguard.Runtime.VanguardStats.ShipHits;
            state["seductions"] = Modules.Vanguard.Runtime.VanguardStats.Seductions;
            state["ugvsSpawned"] = Modules.Vanguard.Runtime.VanguardStats.UgvsSpawned;
            state["ugvsAlive"] = Modules.Vanguard.Runtime.PayloadLifetime.Alive;
            state["refuels"] = Modules.Vanguard.Runtime.VanguardStats.Refuels;
            state["rearms"] = Modules.Vanguard.Runtime.VanguardStats.Rearms;
            if (tanker != null)
            {
                state["skywellFuelKg"] = Modules.Vanguard.Runtime.SkywellService.FuelKg(tanker);
                state["skywellStockKg"] = Modules.Vanguard.Runtime.SkywellService.StockKg(tanker);
                state["tankerAlive"] = tanker.disabled ? 0 : 1;
                int tLeaks = 0, tDetached = 0;
                foreach (FuelTank tank in tanker.GetFuelTanks())
                    if (LeakRate?.GetValue(tank) is float rate && rate > 0f) tLeaks++;
                var detachedNames = new List<string>();
                foreach (UnitPart part in tanker.partLookup)
                    if (part != null && part.IsDetached())
                    {
                        tDetached++;
                        if (detachedNames.Count < 6) detachedNames.Add(part.name);
                    }
                state["tankerDetachedNames"] = string.Join(",", detachedNames);
                state["tankerLeaks"] = tLeaks;
                state["tankerDetached"] = tDetached;
                state["tankerUp"] = Vector3.Dot(tanker.transform.up, Vector3.up);
                state["tankerAlt"] = (float)tanker.GlobalPosition().y;
                if (Modules.Vanguard.Runtime.SkywellBoard.Views.TryGetValue(tanker.persistentID.Id, out var view))
                {
                    state["skywellActive"] = view.Active ? 1 : 0;
                    state["skywellPhase"] = view.Phase.ToString();
                }
                if (receiver != null) state["receiverToContact"] = Vector3.Distance(
                    Modules.Vanguard.Runtime.SkywellBoard.ProbePoint(receiver), Modules.Vanguard.Runtime.SkywellBoard.ContactPoint(tanker));
            }
            if (receiver != null)
            {
                state["receiverAlive"] = receiver.disabled ? 0 : 1;
                state["receiverFuel"] = receiver.GetFuelLevel();
                int leaking = 0;
                foreach (FuelTank tank in receiver.GetFuelTanks())
                    if (LeakRate?.GetValue(tank) is float rate && rate > 0f) leaking++;
                state["receiverLeaks"] = leaking;
                int rDetached = 0;
                foreach (UnitPart part in receiver.partLookup)
                    if (part != null && part.IsDetached()) rDetached++;
                state["receiverDetached"] = rDetached;
                Bounds rb = Modules.Vanguard.Runtime.SkywellBoard.LocalBounds(receiver);
                state["receiverBounds"] = rb.center.ToString("F1") + "/" + rb.size.ToString("F1");
                state["rail"] = Modules.Vanguard.Presentation.SkywellVisuals.RailProbe;
                state["receiverMissing"] = Modules.Vanguard.Runtime.SkywellService.MissingRounds(receiver);
            }
            foreach (KeyValuePair<string, float> death in Deaths) state[death.Key] = death.Value;
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

#if !PUBLIC_RELEASE
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Readback and damage hooks for the megaplane sandbox scenarios (tests/ingame/megaplane-*.json).</summary>
    public static class MegaplaneAutomation
    {
        public static object Status(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a))
                return new { registered = MegaplaneTemplate.Definition != null, error = MegaplaneTemplate.LastError };
            var flight = a.GetComponent<MegaplaneFlight>();
            ControlInputs input = a.GetInputs();
            Vector3 e = a.transform.eulerAngles;
            return new
            {
                registered = true, speed = a.rb.velocity.magnitude, altitude = a.GlobalPosition().y, radarAlt = a.radarAlt,
                climbRate = a.rb.velocity.y, heading = e.y, smoothFrameMs = Time.smoothDeltaTime * 1000f / Mathf.Max(Time.timeScale, .001f),
                slip = Mathf.Abs(Mathf.DeltaAngle(e.y, Mathf.Atan2(a.rb.velocity.x, a.rb.velocity.z) * Mathf.Rad2Deg)), pitch = Mathf.DeltaAngle(0, e.x), bank = Mathf.DeltaAngle(0, e.z),
                holdHeading = flight.Heading, holdAltitude = flight.HoldAltitude, thrust = flight.Thrust, fans = a.GetComponent<MegaplaneFans>()?.Count ?? 0, fanSpool = a.GetComponent<MegaplaneFans>()?.Spool ?? 0f,
                mass = a.rb.mass, simple = HarmonyLib.Traverse.Create(a).Field<bool>("simplePhysics").Value,
                bodies = a.GetComponentsInChildren<Rigidbody>().Length, disabled = a.disabled,
                gear = a.gearState.ToString(), input = new { input.pitch, input.roll, input.yaw, input.throttle },
                guns = a.GetComponentsInChildren<Turret>(true).Length, gunsPicked = MegaplaneGuns.Picked,
                sideGuns = a.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith(MegaplaneGuns.Prefix + "Side_")),
                sideStations = a.weaponStations.Count(s => s.WeaponInfo == MegaplaneGuns.Bastion), sideAmmo = a.weaponStations.Where(s => s.WeaponInfo == MegaplaneGuns.Bastion).Sum(s => s.Ammo),
                railStations = a.weaponStations.Count(s => s.WeaponInfo == MegaplaneGuns.Trident),
                stationNames = a.weaponStations.Select(s => s.WeaponInfo != null ? s.WeaponInfo.weaponName : "-").ToArray(),
                lift = flight.Lift, liftBanks = flight.LiftBanks,
                turretStations = a.weaponStations.Count(s => s.HasTurret()), stations = a.weaponStations.Count,
                silos = a.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith(MegaplaneGuns.Prefix + "SiloMount_")), siloPicked = MegaplaneGuns.PickedSilo,
                missileStations = a.weaponStations.Count(s => s.Weapons.Count > 0 && s.Weapons[0] is MissileLauncher),
                missileAmmo = a.weaponStations.Where(s => s.Weapons.Count > 0 && s.Weapons[0] is MissileLauncher).Sum(s => s.Ammo),
                missileNames = a.weaponStations.Where(s => s.Weapons.Count > 0 && s.Weapons[0] is MissileLauncher).Select(s => s.WeaponInfo != null ? s.WeaponInfo.weaponName : "-").Distinct().ToArray(),
                mounts = a.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(MegaplaneGuns.Prefix)).Select(t => t.name).ToArray(),
                gearDown = a.GetComponent<MegaplaneGear>() is MegaplaneGear g ? g.Leg : -1f, gearLegs = a.GetComponent<MegaplaneGear>() is MegaplaneGear g2 ? g2.Legs : 0,
                nan = float.IsNaN(a.rb.velocity.x) || float.IsNaN(a.transform.position.x),
            };
        }

        /// <summary>Command the undercarriage ("down" true/false) and read where the animation is.</summary>
        public static object Gear(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a) || !(a.GetComponent<MegaplaneGear>() is MegaplaneGear g)) return new { ok = false };
            if (args.TryGetValue("down", out object d)) a.SetGear(System.Convert.ToBoolean(d));
            return new { ok = true, legs = g.Legs, leg = g.Leg, door = g.Door, deployed = a.NetworkgearDeployed, state = a.gearState.ToString() };
        }

        /// <summary>Belly railgun: "down" true/false forces the mount down or up, absent = automatic; "select" true makes it
        /// the current station; "fire" true pulls its trigger once at the current target. Reads the mount state.</summary>
        public static object Railgun(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a) || !(a.GetComponent<MegaplaneRailgun>() is MegaplaneRailgun r)) return new { ok = false };
            if (args.TryGetValue("down", out object d)) r.Command = d == null ? (bool?)null : System.Convert.ToBoolean(d);
            WeaponStation st = r.Turret != null ? r.Turret.GetWeaponStation() : null;
            Unit target = r.Turret != null ? r.Turret.GetTarget() : null;
            bool fired = false;
            if (args.TryGetValue("fire", out object f) && System.Convert.ToBoolean(f) && st != null && r.Live && st.Ready())
            {
                st.Fire(a, target);
                fired = true;
            }
            return new { ok = true, door = r.Door, lower = r.Lower, live = r.Live, artAim = V(r.ArtAim), nativeAim = V(r.NativeAim), pivot = V(r.MuzzleLocal), ammo = st != null ? st.Ammo : -1, fired, target = target != null ? target.unitName : null };
        }

        private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };

        /// <summary>Run a native aircraft spawn on the test host and inspect the same replica on both peers.</summary>
        public static string PeerProbe(bool spawn)
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")))
                throw new System.InvalidOperationException("Groza peer inspection requires a nomodkit loopback sandbox.");
            AircraftDefinition definition = MegaplaneTemplate.Definition;
            if (spawn)
            {
                if (NuclearOption.Networking.NetworkManagerNuclearOption.i?.Server?.Active != true || definition == null)
                    throw new System.InvalidOperationException("Groza must be registered on a running peer host.");
                if (!Object.FindObjectsOfType<Aircraft>().Any(MegaplaneModule.Is))
                    NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null, definition.unitPrefab, definition.aircraftParameters.loadouts[0], 1f, default,
                        new GlobalPosition(10000f, 2500f, -35000f), Quaternion.identity, Vector3.forward * 130f,
                        null, FactionRegistry.GetSortedHQs()[0], "Groza peer test", .5f, .5f);
            }
            return Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                definitionIndex = definition == null ? -1 : ((INetworkDefinition)definition).LookupIndex,
                aircraft = Object.FindObjectsOfType<Aircraft>().Where(MegaplaneModule.Is).Select(a => new
                {
                    key = a.definition.jsonKey, netId = a.Identity.NetId, prefab = a.Identity.PrefabHash,
                    width = a.definition.width, length = a.definition.length,
                    lodIslands = a.GetComponentsInChildren<LODGroup>().Count(g => g.GetLODs().Any(l => l.renderers.Any(r => r != null && r.name.StartsWith("GrozaLOD_")))),
                    stores = a.weaponManager.hardpointSets.Select(s => s.weaponMount == null ? "" : s.weaponMount.jsonKey),
                }),
            });
        }

        /// <summary>Read actual renderer visibility, control-surface pose and seating from the live aircraft.</summary>
        public static object Inspect(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object unit) && unit is Aircraft a) || !MegaplaneModule.Is(a)) return new { ok = false };
            var islands = a.GetComponentsInChildren<LODGroup>().Select(g => new { island = g.name, levels = g.GetLODs() })
                .Where(g => g.levels.Length == 2 && g.levels[1].renderers.Any(r => r != null && r.name.StartsWith("GrozaLOD_"))).ToArray();
            Renderer display = a.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "GrozaTacticalDisplay");
            StatusDisplay schematic = Object.FindObjectsOfType<StatusDisplay>().FirstOrDefault(s => s.name.StartsWith("StatusDisplay_KY44"));
            var sections = schematic != null ? HarmonyLib.Traverse.Create(schematic).Field<List<PartStatusDisplay>>("statusDisplays").Value : null;
            return new
            {
                ok = true, width = a.definition.width, length = a.definition.length, height = a.definition.height, lodBias = QualitySettings.lodBias,
                groups = islands.Length, near = islands.Sum(g => g.levels[0].renderers.Length), far = islands.Sum(g => g.levels[1].renderers.Length),
                nearVisible = islands.Sum(g => g.levels[0].renderers.Count(r => r != null && r.isVisible)),
                farVisible = islands.Sum(g => g.levels[1].renderers.Count(r => r != null && r.isVisible)),
                flap = a.GetComponent<MegaplaneSurfaces>().InnerFlapAngle, display = display != null && display.enabled,
                liveTexture = display != null && display.sharedMaterial.GetTexturePropertyNames().Any(k => display.sharedMaterial.GetTexture(k) is RenderTexture),
                schematic = a.definition.aircraftParameters.StatusDisplay.name, sections = sections?.Count ?? 0,
                damagedSections = sections == null ? new string[0] : sections.Where(s => s.partImage.color.a > .02f).Select(s => s.partImage.name).ToArray(),
                eyeDistance = Camera.main == null ? -1f : Vector3.Distance(Camera.main.transform.position, a.cockpitViewPoint.position),
                eye = V(a.transform.InverseTransformPoint(a.cockpitViewPoint.position)),
                camera = Camera.main == null ? null : V(a.transform.InverseTransformPoint(Camera.main.transform.position)),
            };
        }

        public static object SavedFit(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object unit) && unit is Aircraft a) || !MegaplaneModule.Is(a)) return new { valid = false };
            // A legacy save with an intentionally empty outboard beam must not silently receive the default escort.
            var saved = new SavedLoadout();
            foreach (WeaponMount mount in a.loadout.weapons.Take(4))
                saved.Selected.Add(new SavedLoadout.SelectedMount { Key = saved.Selected.Count == 1 || mount == null ? "" : mount.jsonKey });
            Loadout loaded = saved.CreateLoadout(a.definition.unitPrefab);
            string[] oldKeys = saved.Selected.Select(s => s.Key).ToArray();
            string[] newKeys = loaded.weapons.Select(w => w != null ? w.jsonKey : "").ToArray();
            var hangars = Object.FindObjectsOfType<Hangar>().Where(h => h.attachedUnit != null && !h.attachedUnit.disabled)
                .Select(h => h.GetAvailableAircraft()).Where(choices => choices != null && choices.Any(d => d != null && d.jsonKey == "Darkreach")).ToArray();
            int offered = hangars.Count(choices => choices.Contains(a.definition));
            return new { valid = newKeys.Length == 5 && newKeys.Take(4).SequenceEqual(oldKeys) && newKeys[4] == "", oldKeys, newKeys,
                darkreachHangars = hangars.Length, grozaHangars = offered, allHangars = offered == hangars.Length && offered > 0 };
        }

        /// <summary>Exercise the occupied carrier's native station and report the actual ammunition consumed.</summary>
        public static object Fire(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object unit) && unit is Aircraft a) || !MegaplaneModule.Is(a) ||
                !(args.TryGetValue("weapon", out object weapon) && weapon is string name)) return new { fired = false };
            WeaponStation station = a.weaponStations.FirstOrDefault(s => s.Ammo > 0 && s.HasTurret() && (name == "@missile"
                ? s.Weapons.Any(w => w is MissileLauncher)
                : s.WeaponInfo != null && s.WeaponInfo.weaponName.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0));
            if (station == null) return new { fired = false };
            WeaponStation previous = a.weaponManager.currentWeaponStation;
            int before = station.GetAmmoTotal();
            VolleySample sample = a.GetComponent<VolleySample>() ?? a.gameObject.AddComponent<VolleySample>();
            int previousVolleyRounds = sample.Station == station ? sample.Rounds : 0;
            try
            {
                a.SetActiveStation(station.Number);
                if (station.Ready())
                {
                    station.Fire(a, null);
                    sample.StartCoroutine(sample.ReadNextPhysicsTick(station, before));
                }
            }
            finally
            {
                if (previous != null && previous != station) a.SetActiveStation(previous.Number);
            }
            int after = station.GetAmmoTotal();
            // A missile leaves immediately. A Gun consumes its queued rounds in the next FixedUpdate.
            return new { fired = after < before || previousVolleyRounds > 0, before, after, previousVolleyRounds, weapon = station.WeaponInfo.weaponName };
        }

        private sealed class VolleySample : MonoBehaviour
        {
            internal WeaponStation Station;
            internal int Rounds;

            internal IEnumerator ReadNextPhysicsTick(WeaponStation station, int before)
            {
                Station = station;
                Rounds = 0;
                yield return new WaitForFixedUpdate();
                Rounds = before - station.GetAmmoTotal();
            }
        }

        /// <summary>Hardpoint stores: "fire" = text in a station's weapon name or mount key (e.g. "REMORA", "Paratroop", "UGV"),
        /// launched once through the station's own LaunchMount (target: the "idTarget" unit, else none). Reads every station,
        /// the hold door, and what the carrier has put out (live drones it owns, friendly ground vehicles on the map).</summary>
        public static object Stores(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a)) return new { ok = false };
            args.TryGetValue("idTargetUnit", out object tv);
            Unit target = tv as Unit;
            string fired = null;
            int vehiclesNow = UnitRegistry.allUnits.Count(u => u is GroundVehicle g && !g.disabled && g.NetworkHQ == a.NetworkHQ);
            if (args.ContainsKey("reset")) vehicleBase = vehiclesNow;
            if (args.TryGetValue("fire", out object f) && f is string want)
                foreach (WeaponStation s in a.weaponStations)
                {
                    string name = (s.WeaponInfo != null ? s.WeaponInfo.weaponName : "") + "|" + string.Join("|", s.Weapons.Select(w => HarmonyLib.Traverse.Create(w).Field<WeaponMount>("mount").Value?.jsonKey));
                    bool cargo = want == "cargo" && (s.Cargo || s.WeaponInfo != null && s.WeaponInfo.cargo) && !(s.WeaponInfo?.weaponName ?? "").Contains("SKYWELL") && !name.Contains("Skywell");
                    if (s.Ammo <= 0 || !cargo && name.IndexOf(want, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    s.LaunchMount(a, target, target != null ? target.GlobalPosition() : (a.transform.position + a.transform.forward * 3000f - a.transform.up * 4000f).ToGlobalPosition());
                    fired = name;
                    break;
                }
            var ramp = a.GetComponent<MegaplaneRamp>();
            return new
            {
                ok = true, fired, summary = MegaplaneStores.Summary, door = ramp != null ? ramp.Open : -1f,
                sets = a.weaponManager.hardpointSets.Select(s => s.name + ":" + (s.weaponMount != null ? s.weaponMount.jsonKey : "-")).ToArray(),
                stations = a.weaponStations.Select(s => (s.WeaponInfo != null ? s.WeaponInfo.weaponName : "-") + " " + s.Ammo).ToArray(),
                drones = UnitRegistry.allUnits.Count(u => u is Missile m && !m.disabled && m.owner == a && m.definition != null && m.definition.jsonKey.StartsWith("VG_")),
                mounts = a.weaponManager.hardpointSets.Select(s => s.name + ": " + string.Join(" ", s.hardpoints.Select(h => h.transform == null ? "null" : h.transform.childCount + "c/" + h.transform.GetComponentsInChildren<Renderer>().Count(x => x.enabled) + "r/" + h.transform.GetComponentsInChildren<Renderer>().Select(x => x.bounds.size.magnitude).DefaultIfEmpty(0f).Max().ToString("0.0") + "m"))).ToArray(),
                tender = ModuleServices.TryGet(out ISkywellTenderView tenders) ? tenders.TenderState(a) : "none", vehiclesNew = vehiclesNow - vehicleBase, hasTender = a.weaponStations.Any(s => s.WeaponInfo != null && s.WeaponInfo.name.Contains("Skywell")),
            };
        }

        private static int vehicleBase;
        private static readonly List<Missile> threats = new List<Missile>();
        private static int pdAmmoStart;

        /// <summary>Point-defence test: "count" hostile missiles ("key", default AAM2) are spawned "range" m out on the beams and
        /// quarters, owned by any enemy unit and aimed at the carrier. Without "count" it reads: alive, hits so far, the
        /// point-defence rounds spent (CIWS + RAM stations) and how many hostile missiles the conformal arrays track.</summary>
        public static object Threat(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a)) return new { ok = false };
            int PdAmmo() => a.weaponStations.Where(s => s.HasTurret() && s.WeaponInfo != MegaplaneGuns.Bastion && s.WeaponInfo != MegaplaneGuns.Trident).Sum(s => s.Ammo);
            string error = null;
            if (args.TryGetValue("count", out object c))
            {
                threats.Clear();
                pdAmmoStart = PdAmmo();
                string key = args.TryGetValue("key", out object k) ? (string)k : "AAM2";
                float range = args.TryGetValue("range", out object r) ? System.Convert.ToSingle(r) : 4000f;
                Unit enemy = UnitRegistry.allUnits.FirstOrDefault(u => u != null && !u.disabled && u.NetworkHQ != null && u.NetworkHQ != a.NetworkHQ && !(u is Missile));
                if (!(Encyclopedia.Lookup.TryGetValue(key, out UnitDefinition def) && def is MissileDefinition md)) error = "no missile " + key;
                else if (enemy == null) error = "no enemy unit to own the missiles";
                else
                    for (int i = 0; i < System.Convert.ToInt32(c); i++)
                    {
                        float bearing = 50f + 260f * i / Mathf.Max(1, System.Convert.ToInt32(c) - 1);   // around the carrier, never dead ahead
                        Vector3 from = a.transform.position + Quaternion.AngleAxis(bearing, a.transform.up) * a.transform.forward * range + Vector3.up * 300f;
                        Missile m = NetworkSceneSingleton<Spawner>.i.SpawnMissile(md, from, Quaternion.LookRotation(a.transform.position - from), (a.transform.position - from).normalized * 300f, a, enemy);
                        if (m != null) threats.Add(m);
                    }
            }
            return new
            {
                ok = error == null, error, spawned = threats.Count, alive = threats.Count(m => m != null && !m.disabled),
                pdSpent = pdAmmoStart - PdAmmo(), tracked = a.GetComponent<MegaplaneDefence>()?.Tracked ?? -1,
                pdStations = a.weaponStations.Where(s => s.HasTurret() && s.WeaponInfo != MegaplaneGuns.Bastion && s.WeaponInfo != MegaplaneGuns.Trident)
                    .Select(s => s.WeaponInfo.weaponName + " " + s.Ammo + (s.Turrets.Count > 0 && s.Turrets[0].GetTarget() != null ? " ->" + s.Turrets[0].GetTarget().unitName : "")).ToArray(),
                role = new { a.definition.roleIdentity.antiAir, a.definition.roleIdentity.antiMissile },
            };
        }

        /// <summary>Destroy the damage islands carrying the named art nodes (e.g. "Pod_L1,Pod_L2"): an engine-loss test.</summary>
        public static object Detach(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a)) return new { ok = false };
            var names = ((string)args["nodes"]).Split(',');
            var hit = a.GetComponentsInChildren<Transform>(true).Where(t => names.Contains(t.name))
                .Select(t => t.GetComponentInParent<AeroPart>()).Where(p => p != null && p.gameObject != a.gameObject).Distinct().ToList();
            var damage = a.GetComponent<MegaplaneDamage>();
            foreach (AeroPart part in hit) damage.Break(part);
            return new { ok = hit.Count > 0, parts = hit.Select(p => p.name).ToArray() };
        }

        /// <summary>Shoot the islands carrying the named art nodes through their own hitboxes ("pierce" per hitbox call,
        /// "times" calls each): the real damage path, so islands break off when their hit points run out. Reads the damage state.</summary>
        public static object Hit(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a) || !(a.GetComponent<MegaplaneDamage>() is MegaplaneDamage d)) return new { ok = false };
            if (args.TryGetValue("nodes", out object n))
            {
                string[] names = ((string)n).Split(',');
                float pierce = args.TryGetValue("pierce", out object p) ? System.Convert.ToSingle(p) : 2000f;
                int times = args.TryGetValue("times", out object t) ? System.Convert.ToInt32(t) : 1;
                List<MegaplaneHitbox> all = a.GetComponentsInChildren<MegaplaneHitbox>(true).ToList();
                // One hitbox per named island (donor part name, e.g. wing1b_L), else every hitbox under a named art node.
                List<MegaplaneHitbox> boxes = names.Select(nm => all.FirstOrDefault(h => h.Owner != null && h.Owner.name == nm)).Where(h => h != null).ToList();
                if (boxes.Count == 0) boxes = all.Where(h => names.Any(nm => h.transform.name == nm || h.transform.parent != null && h.transform.parent.name == nm)).ToList();
                if (boxes.Count == 0) return new { state = DamageState(a, d), boxes = all.Take(40).Select(h => h.name + ">" + (h.Owner != null ? h.Owner.name : "-")).ToArray(), before = -1f, after = -1f, armor = (float[])null };
                if (args.TryGetValue("heal", out object heal) && System.Convert.ToBoolean(heal))
                    foreach (MegaplaneHitbox h in boxes) h.Owner.Repair();
                ArmorProperties armor = boxes.Count > 0 ? boxes[0].Owner.GetArmorProperties() : null;
                float before = boxes.Count > 0 ? boxes[0].Owner.hitPoints : -1f;
                foreach (MegaplaneHitbox h in boxes)
                    for (int i = 0; i < times; i++) h.TakeDamage(pierce, 0f, 1f, 0f, 0f, default);
                return new { state = DamageState(a, d), boxes = boxes.Select(b => b.name + ">" + (b.Owner != null ? b.Owner.name : "-")).ToArray(), before,
                    after = boxes.Count > 0 ? boxes[0].Owner.hitPoints : -1f, armor = armor == null ? null : new[] { armor.pierceArmor, armor.pierceTolerance, armor.blastArmor, armor.blastTolerance } };
            }
            return DamageState(a, d);
        }

        private static object DamageState(Aircraft a, MegaplaneDamage d) => new
        {
            ok = true, owners = a.GetComponentsInChildren<MegaplaneHitbox>(true).GroupBy(h => h.Owner != null ? h.Owner.name : "-").Select(g => g.Key + " " + g.Count()).ToArray(),
            wingPath = a.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Wing_Outer_L").Select(t => { string s = t.name; for (Transform q = t.parent; q != null; q = q.parent) s = q.name + "/" + s; return s + " meshes " + t.GetComponentsInChildren<MeshFilter>(true).Length + " boxes " + t.GetComponentsInChildren<MegaplaneHitbox>(true).Length; }).ToArray(),
            liftL = d.LiftL, liftR = d.LiftR, pitchAuth = d.PitchAuthority, yawAuth = d.YawAuthority, lost = d.Lost.ToArray(), lostCount = d.Lost.Count(), burning = d.GetComponent<MegaplaneFx>()?.Burning ?? 0, fxCount = d.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("BS_Fx")) + UnityEngine.Object.FindObjectsOfType<Transform>().Count(t => t.name.StartsWith("BS_Fx") && t.root != d.transform),
            hp = a.partLookup.OfType<AeroPart>().Where(p => !p.IsDetached()).Select(p => p.name + " " + Mathf.RoundToInt(p.hitPoints)).ToArray(),
            disabled = a.disabled, bodies = Object.FindObjectsOfType<Rigidbody>().Count(r => r.GetComponent<AeroPart>() is AeroPart ap && ap.parentUnit == a),
            turretsDisabled = a.GetComponentsInChildren<Turret>(true).Count(t => HarmonyLib.Traverse.Create(t).Field<bool>("disabled").Value),
            altitude = a.GlobalPosition().y, climbRate = a.rb.velocity.y, bank = Mathf.DeltaAngle(0, a.transform.eulerAngles.z), pitch = Mathf.DeltaAngle(0, a.transform.eulerAngles.x),
            speed = a.rb.velocity.magnitude, nan = float.IsNaN(a.rb.velocity.x),
        };

        /// <summary>Belly fighter bay: start a drop ("slot" 0 front, 1 aft, -1 next) and/or read the bay state.</summary>
        public static object Bay(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a) || !(a.GetComponent<MegaplaneBay>() is MegaplaneBay bay)) return new { ok = false };
            bool started = args.TryGetValue("slot", out object slot) && bay.Drop(System.Convert.ToInt32(slot));
            int recalled = args.TryGetValue("recall", out object r) && System.Convert.ToBoolean(r) ? bay.Recall() : 0;
            return new
            {
                ok = true, started, recalled, door = bay.Door, stowed = bay.State.Count(x => x == 0),
                capturePhase = bay.Released.Select(f => f != null && f.GetComponent<MegaplaneCapture>() is MegaplaneCapture cap ? cap.Phase : -1).ToArray(),
                releasedAlive = bay.Released.Count(r => r != null && !r.disabled), releasedFriendly = bay.Released.Count(r => r != null && r.NetworkHQ == a.NetworkHQ), arm = bay.Arm.ToArray(), state = bay.State.ToArray(), fighter = bay.FighterKey,
                visuals = a.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("BayFighter_") && t.gameObject.activeSelf),
                released = bay.Released.Select((r, i) => r == null ? null : new { r.unitName, alive = !r.disabled, speed = r.rb != null ? r.rb.velocity.magnitude : 0f,
                    below = a.transform.InverseTransformPoint(r.transform.position).y, hookRange = Vector3.Distance(r.transform.position, bay.HookPoint(i)), sameSide = r.NetworkHQ == a.NetworkHQ }).ToArray(),
            };
        }

        /// <summary>Close-up shot camera: places the main camera at a point in the carrier's local frame (metres; x right,
        /// y up, z forward), aimed at another local point, with a field of view. Runs after the vanilla camera each frame.</summary>
        public static object View(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a) || Camera.main == null) return new { ok = false };
            if (args.TryGetValue("release", out object release) && System.Convert.ToBoolean(release))
            {
                ShotCam previous = Camera.main.GetComponent<ShotCam>();
                if (previous != null) { previous.enabled = false; Object.Destroy(previous); }
                return new { ok = true };
            }
            float F(string k) => System.Convert.ToSingle(args[k]);
            ShotCam shot = Camera.main.GetComponent<ShotCam>() ?? Camera.main.gameObject.AddComponent<ShotCam>();
            shot.target = a.transform;
            shot.offset = new Vector3(F("x"), F("y"), F("z"));
            shot.look = new Vector3(F("lx"), F("ly"), F("lz"));
            shot.fov = F("fov");
            return new { ok = true };
        }

        [DefaultExecutionOrder(30000)]
        private sealed class ShotCam : MonoBehaviour
        {
            public Transform target;
            public Vector3 offset, look;
            public float fov;

            private void LateUpdate()
            {
                if (target == null) { Destroy(this); return; }
                transform.position = target.TransformPoint(offset);
                transform.rotation = Quaternion.LookRotation(target.TransformPoint(look) - transform.position, target.up);
                GetComponent<Camera>().fieldOfView = fov;
            }
        }
    }
}
#endif

using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Spooktober.Domain;
using UnityEngine;
using Random = UnityEngine.Random;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>
    /// Hell's Army, the third boss (host only, single player like GHOST). A gate tears open on land ahead of the host's aircraft
    /// (shockwave, a pillar of fire) with a ring of AA wardens held round it, then themed waves (<see cref="HellRules"/>: scouts,
    /// iron, skyfire, horde, bigger each lap) pour out of it one unit at a time: ground vehicles that drive on the nearest enemy
    /// airbase, and from the second wave hellish gunships and jets. The gate grows with every wave. Everything is on the HELL side,
    /// hostile to all; every listed side is fed its tracks (HELL is unlisted, so radars never see it) and HELL is fed what stands
    /// near its units. Flying through the portal is <see cref="Enter"/>: the way into the hell pocket dimension and the objective
    /// that shuts the gate.
    /// </summary>
    internal static class HellGate
    {
        internal const string Name = "HELL'S ARMY";
        private const float OrdersSeconds = 15f, FeedSeconds = 1f, SightRange = 6000f, AirSight = 15000f, MarchRange = 30000f, BaseRange = 45000f;

        private static readonly List<Unit> Army = new List<Unit>(), Air = new List<Unit>(), Wardens = new List<Unit>();
        private static readonly Queue<UnitDefinition> Pending = new Queue<UnitDefinition>();
        private static readonly List<UnitDefinition> Pool = new List<UnitDefinition>();
        private static readonly Dictionary<string, VehicleDefinition> Known = new Dictionary<string, VehicleDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AircraftDefinition> Flyers = new Dictionary<string, AircraftDefinition>(StringComparer.OrdinalIgnoreCase);
        private static HellPortal portal;
        private static float nextWave, nextStream, nextOrders, nextFeed, wasAlong;
        private static bool haveAlong, faltered;
        private static int serial;

        /// <summary>Raised on the host when the gate is shut by the player (not on a dismiss).</summary>
        internal static event Action Defeated;

        public static int Waves, Spawned, SpawnedAir, Entered, Falters;
        public static bool WasDefeated;

        /// <summary>The last wave's theme (sim readout).</summary>
        public static HellTheme LastTheme;

        public static bool Alive => portal != null && portal.Alive;

        public static HellPortal Portal => Alive ? portal : null;

        /// <summary>The ground attackers on the field.</summary>
        public static IReadOnlyList<Unit> Units => Army;

        public static IReadOnlyList<Unit> Aircraft => Air;

        public static IReadOnlyList<Unit> Guards => Wardens;

        /// <summary>Why the gate cannot open now, or null.</summary>
        public static string CannotSpawn()
        {
            if (!GameAccess.IsServer()) return "HOST ONLY";
            if (GhostSide.RemoteClients()) return Name + ": SINGLE PLAYER ONLY FOR NOW";
            if (!GameManager.GetLocalAircraft(out global::Aircraft host) || host == null || host.disabled) return Name + ": GET AIRBORNE FIRST";
            if (Roll(HellTheme.Horde, false).Count == 0) return Name + ": NO VEHICLES";
            // Probed here too, so the menu never sends the boss on the field away for a gate that then finds no ground.
            return Site(host, out _, out _) ? null : Name + ": NO LAND IN REACH";
        }

        /// <summary>Host: tears the gate open on dry land 5-13 km from the host's aircraft (ahead first), facing it. The menu line.</summary>
        public static string Spawn()
        {
            string why = CannotSpawn();
            if (why != null) return why;
            if (Alive) Close(false);
            GameManager.GetLocalAircraft(out global::Aircraft host);
            if (!Site(host, out Vector3 ground, out Vector3 toward)) return Name + ": NO LAND IN REACH";
            if (HellSide.Side.Ensure() == null) return Name + ": NO HELL SIDE";
            portal = HellPortal.Spawn(ground + Vector3.up * (HellRules.PortalRadius + 15f), Quaternion.LookRotation(toward, Vector3.up),
                HellRules.PortalRadius);
            portal.Shockwave();
            Waves = Spawned = SpawnedAir = Entered = Falters = 0;
            WasDefeated = haveAlong = faltered = false;
            Pending.Clear();
            nextWave = Time.time + HellRules.FirstWaveSeconds; // the gate opens first, the army a breath later
            nextStream = nextOrders = nextFeed = 0f;
            PostWardens(ground, toward);
            FallenAce.Announce("THE GATE IS OPEN");
            FallenAce.Announce("FLY INTO IT");
            Plugin.Logger.LogInfo("[Spooktober] HELL'S ARMY gate opened " + (Vector3.Distance(ground, host.transform.position) / 1000f).ToString("0.0") +
                                  " km out, " + Wardens.Count + " wardens.");
            return Name + ": THE GATE IS OPEN";
        }

        /// <summary>Host: shuts the gate without a defeat (the menu's DISMISS).</summary>
        public static string Dismiss()
        {
            if (!Alive) return "NO BOSS";
            Close(false);
            return Name + " DISMISSED";
        }

        /// <summary>
        /// The player flew through the portal. ponytail: until the hell scene (the pocket dimension, user-supplied) is wired in, going
        /// through is the objective itself and shuts the gate as the defeat; the scene will replace this body with the transfer there
        /// and call <see cref="Close"/> with true once its objective falls.
        /// </summary>
        public static void Enter()
        {
            if (!Alive) return;
            Entered++;
            Plugin.Logger.LogInfo("[Spooktober] HELL'S ARMY: the player flew through the gate.");
            FallenAce.Announce("YOU WENT THROUGH. IT FOLLOWS YOU BACK.");
            Close(true);
        }

        /// <summary>Host: the army burns away, the portal collapses and HELL is forgotten; a defeat credits the chain.</summary>
        internal static void Close(bool defeated)
        {
            Mirage.ServerObjectManager objects = GameAccess.NetworkManagerOrNull?.ServerObjectManager;
            foreach (List<Unit> list in new[] { Army, Air, Wardens })
            {
                foreach (Unit unit in list)
                {
                    if (unit == null || unit.Identity == null || unit.Identity.NetId == 0) continue;
                    Burst(unit.transform.position);
                    if (unit is global::Aircraft aircraft && aircraft.pilots != null)
                        foreach (Pilot pilot in aircraft.pilots)
                            if (pilot != null) pilot.SwitchState(null);
                    try { objects?.Destroy(unit.Identity, !unit.Identity.IsSceneObject); }
                    catch (Exception e) { Plugin.Logger.LogWarning("[Spooktober] HELL'S ARMY despawn " + unit.name + ": " + e.Message); }
                }
                list.Clear();
            }
            Pending.Clear();
            HellArmy.Clear();
            if (portal != null && portal.Alive)
            {
                Burst(portal.Root.transform.position);
                portal.Close();
            }
            portal = null;
            HellSide.Side.Forget();
            if (!defeated) return;
            WasDefeated = true;
            FallenAce.Announce("THE GATE IS SHUT");
            Defeated?.Invoke();
        }

        /// <summary>Scene reset: the scene took the portal and the army with it.</summary>
        internal static void Forget()
        {
            portal = null;
            Army.Clear();
            Air.Clear();
            Wardens.Clear();
            Pending.Clear();
            Known.Clear();
            Flyers.Clear();
        }

        /// <summary>Host, every frame while the gate is open: waves, the stream out of the gate, orders, tracks and the fly-through.</summary>
        internal static void Frame(float now)
        {
            if (!Alive) return;
            Army.RemoveAll(u => u == null || u.disabled);
            Air.RemoveAll(u => u == null || u.disabled);
            Wardens.RemoveAll(u => u == null || u.disabled);
            if (Waves > 0 && Army.Count == 0 && Air.Count == 0 && Pending.Count == 0 && !faltered)
            {
                // The player wiped out everything that came through: the gate falters and the next wave is late.
                faltered = true;
                Falters++;
                nextWave += HellRules.FalterSeconds;
                FallenAce.Announce("THE GATE FALTERS");
            }
            if (now >= nextWave)
            {
                nextWave = now + HellRules.Delay(Waves);
                Wave();
            }
            if (Pending.Count > 0 && now >= nextStream)
            {
                nextStream = now + HellRules.StreamSeconds;
                Emit(Pending.Dequeue());
            }
            if (now >= nextOrders)
            {
                nextOrders = now + OrdersSeconds;
                foreach (Unit unit in Army) March(unit);
            }
            if (now >= nextFeed)
            {
                nextFeed = now + FeedSeconds;
                Feed();
            }
            FlyThrough();
        }

        /// <summary>Queues the wave (ground first, aircraft last, so the jets break out over the column), announces it and grows the gate.</summary>
        private static void Wave()
        {
            HellTheme theme = HellRules.Theme(Waves);
            int ground = HellRules.ToSpawn(Waves, Army.Count + Pending.Count);
            int air = HellRules.AirToSpawn(Waves, Air.Count);
            List<UnitDefinition> pool = Roll(theme, false);
            for (int i = 0; i < ground && pool.Count > 0; i++) Pending.Enqueue(pool[Random.Range(0, pool.Count)]);
            pool = Roll(theme, true);
            for (int i = 0; i < air && pool.Count > 0; i++) Pending.Enqueue(pool[Random.Range(0, pool.Count)]);
            if (ground + air == 0) return;
            FallenAce.Announce(HellRules.Banner(Waves));
            if (theme == HellTheme.Skyfire) FallenAce.Announce("THEY ARE AIMING AT THE SKY");
            else if (theme == HellTheme.Horde) FallenAce.Announce("THE GATE IS SCREAMING");
            else if (air > 0 && Waves == 1) FallenAce.Announce("SOMETHING IS FLYING OUT OF IT");
            LastTheme = theme;
            Waves++;
            faltered = false;
            portal.SetScale(HellRules.GateScale(Waves));
            Burst(portal.Root.transform.position);
        }

        /// <summary>One unit through the gate: a vehicle rolls out in front of it, an aircraft flies out of the disc.</summary>
        private static void Emit(UnitDefinition definition)
        {
            FactionHQ hell = HellSide.Side.HQ;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (hell == null || spawner == null || definition == null) return;
            Transform gate = portal.Root.transform;
            Vector3 forward = Flat(gate.forward);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);
            string name = "HELL:" + (definition.jsonKey ?? definition.unitName ?? "unit") + ":" + ++serial;
            Unit unit = null;
            try
            {
                if (definition is AircraftDefinition flyer)
                {
                    float speed = CruiseSpeed(flyer);
                    bool rotor = speed < 60f;
                    Vector3 at = gate.position + forward * (portal.Radius * portal.Scale + 30f) + Vector3.up * (rotor ? 0f : 120f);
                    unit = spawner.SpawnAircraft(null, flyer.unitPrefab, Loadout(flyer), 1f, default, at.ToGlobalPosition(), facing,
                        forward * speed, null, hell, name, 1f, 1f);
                    if (unit != null)
                    {
                        Wake((global::Aircraft)unit);
                        Air.Add(unit);
                        SpawnedAir++;
                    }
                }
                else
                {
                    Vector3 foot = Ground(gate.position);
                    Vector3 want = foot + right * Random.Range(-portal.Radius, portal.Radius) * 0.8f + forward * Random.Range(40f, 90f);
                    Vector3 at = GroundPlacement.TryPlace(definition, want, facing, out Vector3 free) ? free : Ground(want);
                    unit = spawner.SpawnVehicle(definition.unitPrefab, at.ToGlobalPosition(), facing, Vector3.zero, hell, name, 1f, true, null);
                    if (unit != null)
                    {
                        Army.Add(unit);
                        March(unit);
                    }
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning("[Spooktober] HELL'S ARMY spawn " + name + ": " + e.Message); }
            if (unit == null) return;
            Spawned++;
            HellArmy.Enlist(unit);
            Burst(unit.transform.position + Vector3.up * 3f);
        }

        /// <summary>The gate's AA ring: <see cref="HellRules.Wardens"/> air-defence vehicles held in place round its foot.</summary>
        private static void PostWardens(Vector3 ground, Vector3 toward)
        {
            FactionHQ hell = HellSide.Side.HQ;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (hell == null || spawner == null) return;
            Wardens.Clear();
            for (int i = 0; i < HellRules.Wardens; i++)
            {
                if (!Known.TryGetValue(HellRules.WardenKeys[i % HellRules.WardenKeys.Length], out VehicleDefinition definition)) continue;
                Vector3 outward = Quaternion.Euler(0f, 45f + i * 360f / HellRules.Wardens, 0f) * toward;
                Quaternion facing = Quaternion.LookRotation(outward, Vector3.up);
                Vector3 want = ground + outward * HellRules.WardenRing;
                Vector3 at = GroundPlacement.TryPlace(definition, want, facing, out Vector3 free) ? free : Ground(want);
                try
                {
                    GroundVehicle unit = spawner.SpawnVehicle(definition.unitPrefab, at.ToGlobalPosition(), facing, Vector3.zero, hell,
                        "HELL:WARDEN:" + ++serial, 1f, true, null);
                    if (unit == null) continue;
                    unit.SetHoldPosition(true);
                    Wardens.Add(unit);
                    HellArmy.Enlist(unit);
                }
                catch (Exception e) { Plugin.Logger.LogWarning("[Spooktober] HELL'S ARMY warden: " + e.Message); }
            }
        }

        /// <summary>On to the nearest enemy-held airbase within 45 km (the invasion's goal), else the nearest enemy vehicle, ship or
        /// building within 30 km, else the host's aircraft's ground point. Units shoot what they meet on the way.</summary>
        private static void March(Unit unit)
        {
            if (!(unit is GroundVehicle vehicle) || vehicle.disabled || vehicle.UnitCommand == null) return;
            Vector3 at = unit.transform.position;
            GlobalPosition goal = default;
            bool found = false;
            float nearest = BaseRange;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.center == null || airbase.CurrentHQ == null || HellSide.Side.Owns(airbase.CurrentHQ)) continue;
                float distance = Vector3.Distance(airbase.center.position, at);
                if (distance >= nearest) continue;
                nearest = distance;
                goal = airbase.center.position.ToGlobalPosition();
                found = true;
            }
            if (!found)
            {
                nearest = MarchRange;
                foreach (Unit other in UnitRegistry.allUnits)
                {
                    if (!Enemy(other) || other is global::Aircraft || other is PilotDismounted) continue;
                    float distance = Vector3.Distance(other.transform.position, at);
                    if (distance >= nearest) continue;
                    nearest = distance;
                    goal = other.GlobalPosition();
                    found = true;
                }
            }
            if (!found && GameManager.GetLocalAircraft(out global::Aircraft host) && host != null)
            {
                goal = Ground(host.transform.position).ToGlobalPosition();
                found = true;
            }
            if (!found) return;
            vehicle.SetHoldPosition(false);
            vehicle.UnitCommand.SetDestination(goal, true);
        }

        /// <summary>HELL is unlisted: every listed side gets the tracks of everything on HELL (so the player sees and locks it, and
        /// the AI fights it), and HELL gets what stands within 6 km of its ground units or 15 km of its aircraft (the game's AI only
        /// shoots at a fresh track).</summary>
        private static void Feed()
        {
            FactionHQ hell = HellSide.Side.HQ;
            if (hell == null) return;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                if (hq != null && hq != hell)
                    foreach (List<Unit> list in new[] { Army, Air, Wardens })
                        foreach (Unit unit in list) Track(hq, unit);
            foreach (Unit other in UnitRegistry.allUnits)
            {
                if (!Enemy(other)) continue;
                Vector3 there = other.transform.position;
                if (Near(Army, there, SightRange) || Near(Wardens, there, SightRange) || Near(Air, there, AirSight)) Track(hell, other);
            }
        }

        private static bool Near(List<Unit> units, Vector3 there, float range)
        {
            foreach (Unit unit in units)
                if ((unit.transform.position - there).sqrMagnitude < range * range) return true;
            return false;
        }

        private static void Track(FactionHQ hq, Unit unit)
        {
            TrackingInfo track = hq.GetTrackingData(unit.persistentID);
            if (track == null) hq.RpcUpdateTrackingInfo(unit.persistentID);
            else track.UpdateInfo(unit.GlobalPosition());
        }

        private static bool Enemy(Unit unit) => unit != null && !unit.disabled && unit.NetworkHQ != null && !HellSide.Side.Owns(unit.NetworkHQ);

        /// <summary>The host's aircraft through the disc between two frames (<see cref="HellRules.Crossed"/>, at the gate's grown size).</summary>
        private static void FlyThrough()
        {
            if (!GameManager.GetLocalAircraft(out global::Aircraft host) || host == null || host.disabled) { haveAlong = false; return; }
            Transform gate = portal.Root.transform;
            Vector3 rel = host.transform.position - gate.position;
            float along = Vector3.Dot(rel, gate.forward);
            float across = (rel - gate.forward * along).magnitude;
            bool crossed = haveAlong && HellRules.Crossed(wasAlong, along, across, portal.Radius * portal.Scale);
            wasAlong = along;
            haveAlong = true;
            if (crossed) Enter();
        }

        /// <summary>The definitions this build has for <paramref name="theme"/>'s ground or air roster (lookups cached per scene; a
        /// duplicate key weights the pick). Reuses one list: the caller picks from it before the next call.</summary>
        private static List<UnitDefinition> Roll(HellTheme theme, bool air)
        {
            Encyclopedia book = Encyclopedia.i;
            if (Known.Count == 0 && book?.vehicles != null)
                foreach (VehicleDefinition definition in book.vehicles)
                    if (definition != null && definition.unitPrefab != null && !string.IsNullOrEmpty(definition.jsonKey))
                        Known[definition.jsonKey] = definition;
            Pool.Clear();
            if (!air)
            {
                foreach (string key in HellRules.Roster(theme))
                    if (Known.TryGetValue(key, out VehicleDefinition definition)) Pool.Add(definition);
                return Pool;
            }
            foreach (string word in HellRules.AirRoster(theme))
            {
                if (!Flyers.TryGetValue(word, out AircraftDefinition found) && book?.aircraft != null)
                {
                    foreach (AircraftDefinition definition in book.aircraft)
                        if (definition != null && definition.unitPrefab != null && definition.unitName != null &&
                            definition.unitName.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = definition;
                            break;
                        }
                    Flyers[word] = found; // a miss is cached as null too
                }
                if (found != null) Pool.Add(found);
            }
            return Pool;
        }

        /// <summary>The airframe's first standard loadout (null flies it clean).</summary>
        private static NuclearOption.SavedMission.Loadout Loadout(AircraftDefinition definition)
        {
            StandardLoadout[] standard = definition.aircraftParameters != null ? definition.aircraftParameters.StandardLoadouts : null;
            if (standard != null)
                foreach (StandardLoadout option in standard)
                    if (option != null && !option.disabled && option.loadout != null) return option.loadout;
            return null;
        }

        /// <summary>A rotorcraft starts slow; a jet at about its corner speed (ARES's rule).</summary>
        private static float CruiseSpeed(AircraftDefinition definition)
        {
            AircraftParameters p = definition.aircraftParameters;
            if (p == null) return 150f;
            if (p.takeoffSpeed < 5f) return 25f;
            return Mathf.Max(80f, Mathf.Clamp(p.cornerSpeed, p.takeoffSpeed * 1.5f, Mathf.Max(p.takeoffSpeed * 1.5f, p.maxSpeed * 0.75f)));
        }

        private static void Wake(global::Aircraft aircraft)
        {
            Pilot pilot = aircraft.pilots != null && aircraft.pilots.Length > 0 ? aircraft.pilots[0] : null;
            if (pilot == null) return;
            if (pilot.AICombatState != null) pilot.SwitchState(pilot.AICombatState);
            else if (pilot.AIHeloCombatState != null) pilot.SwitchState(pilot.AIHeloCombatState);
        }

        /// <summary>Dry, near-level land 5-13 km from the host (ahead first, then fanning out and behind); the gate faces the host.</summary>
        private static bool Site(global::Aircraft host, out Vector3 ground, out Vector3 toward)
        {
            Vector3 from = host.transform.position;
            Vector3 ahead = Flat(host.transform.forward);
            foreach (float turn in new[] { 0f, 25f, -25f, 50f, -50f, 90f, -90f, 180f })
                foreach (float distance in new[] { 7000f, 9000f, 5000f, 11000f, 13000f })
                {
                    Vector3 probe = from + Quaternion.Euler(0f, turn, 0f) * ahead * distance;
                    if (!GroundPlacement.DryGround(Ground(probe) + Vector3.up * 2f, out ground)) continue;
                    toward = Flat(from - ground);
                    return true;
                }
            ground = toward = default;
            return false;
        }

        /// <summary>The terrain (or sea) point under <paramref name="at"/>, whatever its height.</summary>
        private static Vector3 Ground(Vector3 at)
        {
            var origin = new Vector3(at.x, Datum.LocalSeaY + 9000f, at.z);
            return Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 18000f, PhysicsLayers.StaticsMask) && hit.point.y > Datum.LocalSeaY
                ? hit.point : new Vector3(at.x, Datum.LocalSeaY, at.z);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        private static void Burst(Vector3 at)
        {
            try { RadGhostLook.Burst(at, fire: true); }
            catch (Exception e) { Plugin.Logger.LogWarning("[Spooktober] HELL'S ARMY ember burst: " + e.Message); }
        }
    }

    /// <summary>Host: runs <see cref="HellGate.Frame"/> while the gate is open, and shuts it (no defeat) the frame a remote player
    /// connects, like the FALLEN ACE: HELL cannot exist on a client (R15).</summary>
    internal sealed class HellGateDirector : MonoBehaviour, Core.Lifecycle.ISceneService
    {
        public void ResetForScene() => HellGate.Forget();

        private void OnDestroy() => HellGate.Forget();

        private void Update()
        {
            if (!HellGate.Alive || !GameAccess.IsServer()) return;
            if (GhostSide.RemoteClients())
            {
                HellGate.Close(false);
                Plugin.Logger.LogWarning("[Spooktober] HELL'S ARMY left: a remote player joined (single player only for now).");
                FallenAce.Announce("THE GATE CLOSED: A PLAYER JOINED");
                return;
            }
            try { HellGate.Frame(Time.time); }
            catch (Exception e)
            {
                // A broken gate would stand there forever making nothing: close it (not a defeat) and say so, once per gate.
                Plugin.Logger.LogError("[Spooktober] HELL'S ARMY director: " + e);
                HellGate.Close(false);
                FallenAce.Announce("THE GATE COLLAPSED");
            }
        }
    }
}

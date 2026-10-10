using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Presentation;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>KS-01 KESTREL: the flight hands the airframe to its KestrelDrone (server only).</summary>
    internal sealed partial class VanguardFlight
    {
        private KestrelDrone scout;

        private void InitScout(Unit requested)
        {
            scout = KestrelDrone.Attach(missile);
            scout?.Configure(requested, launchPos, bearing);
        }

        private void TickScout() => scout?.TickHost();
    }

    /// <summary>
    /// KS-01 KESTREL. Host: guidance over native aerodynamics (GLAIVE's left-hand pylon turn, wider and higher), the 2 s
    /// spot pass around the orbit centre and one laser mark. Every peer: the state the visuals read and the launcher's
    /// HUD line. No rig here: the joints belong to the visuals.
    /// </summary>
    internal sealed class KestrelDrone : MonoBehaviour
    {
        private const float PlanSeconds = .2f;
        private const float HudSeconds = .2f;
        private readonly HashSet<uint> known = new HashSet<uint>(); // every unit this glider has spotted (kestrel.spot)
        private readonly List<Unit> candidates = new List<Unit>();  // this pass: hostile ground vehicles in sight
        private readonly List<float> ranges = new List<float>();    // parallel: flat range to the orbit centre
        private Missile missile;
        private Unit objectiveUnit, lastOrder, hudLased;
        private FactionHQ markedBy; // the faction that holds this glider's +1 on its lase count
        private GlobalPosition objective, centre;
        private float enteredOrbit, departedAt, nextPlan, nextSense, nextSnapshot, nextHud;
        private int cursor, hudSpotted; // cursor: where the next spot pass starts in UnitRegistry.allUnits
        private bool configured, held;  // held: the +1 is out and not yet taken back
        private string hudKey, hudDetail;

        public KestrelPhase Phase { get; private set; }
        /// <summary>Seconds of station time still to come (the host counts, clients take it from the snapshot).</summary>
        public float StationLeft { get; private set; } = KestrelProfile.StationSeconds;
        /// <summary>Units refreshed on the faction picture by the last spot pass.</summary>
        public int Spotted { get; private set; }
        /// <summary>The laser-marked unit, or null.</summary>
        public Unit Lased { get; private set; }

        /// <summary>The orbit centre (host only: clients never learn it).</summary>
        internal GlobalPosition Centre => centre;

        /// <summary>
        /// What the sensor looks at: the lased unit, else the orbit centre. Clients do not know the centre and get a guess
        /// at it (the port beam on station, ahead in transit). ToLocalPosition() gives the scene point.
        /// </summary>
        public GlobalPosition LookPoint
        {
            get
            {
                if (Lased != null) return Lased.GlobalPosition();
                if (configured) return centre;
                Transform body = missile.transform;
                Vector3 flat = Vector3.ProjectOnPlane(Phase == KestrelPhase.Station ? -body.right : body.forward, Vector3.up).normalized;
                return missile.GlobalPosition() + flat * KestrelProfile.OrbitRadius - Vector3.up * KestrelProfile.OrbitHeight;
            }
        }

        public static KestrelDrone Attach(Missile missile)
        {
            if (missile == null || missile.definition == null || VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Scout) return null;
            var old = missile.GetComponent<KestrelDrone>();
            if (old != null) return old;
            var drone = missile.gameObject.AddComponent<KestrelDrone>();
            drone.missile = missile;
            KestrelVisuals.Attach(missile, drone); // every peer: unfold, prop, turret, laser line
            return drone;
        }

        public void Configure(Unit requested, GlobalPosition launch, Vector3 bearing)
        {
            if (!missile.IsServer || configured) return;
            configured = true;
            lastOrder = DroneOrders.StrikeTarget(missile.owner); // only a CHANGE of designation after launch moves the orbit
            objectiveUnit = GroundHostile(lastOrder) ? lastOrder : GroundHostile(requested) ? requested : null;
            // A designation carries where its target stands, on the faction picture or not.
            if (objectiveUnit != null) objective = objectiveUnit.GlobalPosition();
            else
            {
                // Nothing to watch: park ahead of the launch, over the ground height found under the launch point.
                missile.UpdateRadarAlt();
                objective = launch + SentinelProfile.ParkOffset(Vector3.zero, false, bearing);
                objective.y = launch.y - missile.radarAlt;
            }
            centre = objective;
            // Unarmed: it must not count as a missile attack on the unit it watches (track counter, intercept priority).
            missile.SetTarget(null);
            missile.onDisableUnit += Unmark;
            VanguardStats.Bump("kestrel.launch");
        }

        public void TickHost()
        {
            if (!configured || !missile.IsServer || missile.disabled) return;
            if (Time.time >= nextPlan) { nextPlan = Time.time + PlanSeconds; Plan(missile.timeSinceSpawn); if (missile.disabled) return; }
            if (Phase == KestrelPhase.Station && Time.time >= nextSense) { nextSense = Time.time + KestrelProfile.SpotInterval; Sense(); }
            if (Time.time >= nextSnapshot) { nextSnapshot = Time.time + PlanSeconds; Publish(); }
        }

        private void Plan(float age)
        {
            GlobalPosition here = missile.GlobalPosition();
            missile.UpdateRadarAlt();
            if (GlaiveProfile.Crashing(missile.radarAlt, age)) { VanguardStats.Bump("kestrel.lost"); Remove(); return; }
            Designate();
            if (GroundHostile(objectiveUnit) && missile.NetworkHQ.TryGetKnownPosition(objectiveUnit, out GlobalPosition seen)) objective = seen;
            Vector3 toObjective = objective - here; toObjective.y = 0f;
            float flat = toObjective.magnitude;
            float onStation = Phase == KestrelPhase.Station ? Time.time - enteredOrbit : 0f;
            KestrelPhase next = KestrelProfile.Next(Phase, KestrelProfile.Capture(flat, missile.radarAlt, age, missile.speed), onStation, age);
            if (next != Phase) Enter(next);
            StationLeft = KestrelProfile.StationLeft(Phase, onStation, age);
            if (Phase == KestrelPhase.Depart && Time.time - departedAt > KestrelProfile.DepartSeconds) { Remove(); return; }
            if (Phase == KestrelPhase.Station) centre += GlaiveProfile.SlideStep(objective - centre, PlanSeconds);
            else if (Phase == KestrelPhase.Transit) centre = objective;

            Vector3 nose = missile.transform.forward; nose.y = 0f;
            nose = nose.sqrMagnitude > .01f ? nose.normalized : Vector3.forward;
            // Leaving the hold and leaving the station it holds its own heading, level: no turn inside the carrier.
            bool coast = Phase == KestrelPhase.Depart || age < KestrelProfile.ClearSeconds;
            Vector3 step = coast ? nose * GlaiveProfile.Lookahead
                : Phase == KestrelPhase.Station ? KestrelProfile.OrbitOffset(here - centre, nose)
                : flat > 1f ? toObjective * (GlaiveProfile.Lookahead / flat) : nose * GlaiveProfile.Lookahead;
            GlobalPosition waypoint = here + step;
            waypoint.y = coast ? here.y : KestrelProfile.AimHeight(centre.y, here.y - missile.radarAlt, here.y);
            missile.SetThrottle(age < KestrelProfile.ClearSeconds ? 0f : KestrelProfile.Throttle(missile.speed));
            missile.SetAimpoint(waypoint, Vector3.zero);
        }

        private void Enter(KestrelPhase next)
        {
            Phase = next;
            nextSnapshot = 0f; // a phase change goes out at once
            if (next == KestrelPhase.Station) { enteredOrbit = Time.time; VanguardStats.Bump("kestrel.orbit"); return; }
            departedAt = Time.time; Spotted = 0;
            Release(); // the mark only stands while the glider is on station
            VanguardStats.Bump("kestrel.depart");
        }

        // The launcher's designation (radial DESIGNATE via DroneOrders; AI launchers: their current target). A CHANGE
        // to another ground target moves the orbit, as the GLAIVE-2S hand-off does.
        private void Designate()
        {
            Unit order = DroneOrders.StrikeTarget(missile.owner);
            if (order == lastOrder) return;
            lastOrder = order;
            if (order == objectiveUnit || !GroundHostile(order)) return;
            objectiveUnit = order;
            objective = order.GlobalPosition();
            Release(); // retask: the next pass marks the new target
        }

        private bool Hostile(Unit unit) => unit != null && unit.definition != null && !unit.disabled &&
            missile.NetworkHQ != null && unit.NetworkHQ != null && unit.NetworkHQ != missile.NetworkHQ;
        private bool GroundHostile(Unit unit) => Hostile(unit) && !(unit is Aircraft) && !(unit is Missile);

        // Every 2 s on station: what the glider sees around the orbit centre is refreshed on the owner's faction picture
        // (the call every detector ends in; a spot is fresh for 4 s), then one unit is laser-marked. Sight is taken from
        // the hull. ponytail: O(all units) and one linecast per unit in range, per pass; with more than SpotCap units in
        // sight the passes take turns and a fallback mark may hop. A grid query and a sticky pick if that ever shows.
        private void Sense()
        {
            FactionHQ hq = missile.NetworkHQ;
            if (hq == null) return;
            Vector3 eye = missile.transform.position;
            List<Unit> all = UnitRegistry.allUnits;
            candidates.Clear(); ranges.Clear();
            int sent = 0, scanned = 0;
            for (; scanned < all.Count && sent < KestrelProfile.SpotCap; scanned++)
            {
                Unit unit = all[(cursor + scanned) % all.Count];
                if (unit == null || unit.disabled || unit.NetworkHQ == null || unit.NetworkHQ == hq) continue;
                Vector3 offset = unit.GlobalPosition() - centre;
                float range = new Vector2(offset.x, offset.z).magnitude;
                if (!KestrelProfile.MaySpot(range, unit is GroundVehicle, unit is Ship) || !unit.LineOfSight(eye, 1000f)) continue;
                hq.RpcUpdateTrackingInfo(unit.persistentID);
                sent++;
                if (known.Add(unit.persistentID.Id)) VanguardStats.Bump("kestrel.spot");
                if (unit is GroundVehicle) { candidates.Add(unit); ranges.Add(range); }
            }
            cursor = all.Count > 0 ? (cursor + scanned) % all.Count : 0; // the cap must not starve the tail of the list
            Spotted = sent;

            // The orbit target while it lives; once it is dead, or there never was one, the nearest vehicle in sight.
            Unit pick;
            if (GroundHostile(objectiveUnit)) pick = objectiveUnit.LineOfSight(eye, 1000f) ? objectiveUnit : null;
            else
            {
                int nearest = KestrelProfile.LasePick(candidates.IndexOf(Lased), ranges);
                pick = nearest < 0 ? null : candidates[nearest];
            }
            if (held && ReferenceEquals(pick, Lased)) return;
            Release();
            if (pick == null) return;
            // A lase is a count on the faction (FactionHQ.UpdateLasedState): laser seekers only ask whether it is above zero.
            markedBy = hq;
            hq.UpdateLasedState(pick, true);
            Lased = pick; held = true;
            pick.onDisableUnit += Unmark;
            VanguardStats.Bump("kestrel.lase");
        }

        private void Unmark(Unit _) => Release();

        // Takes back exactly what Sense placed, once: the target died (its onDisableUnit runs before it is destroyed), the
        // glider is gone, left its station or was retasked. Not skipped for a dead target: a destroyed unit left in the
        // faction's lase table throws on every later laser-store launch.
        private void Release()
        {
            if (!held) return;
            held = false;
            Unit unit = Lased; Lased = null;
            unit.onDisableUnit -= Unmark;
            if (markedBy != null) markedBy.UpdateLasedState(unit, false);
            VanguardStats.Bump("kestrel.release");
        }

        // The end is quiet: no warhead, no blast, no shell falling for two seconds (the HAMILCAR husk removal).
        private void Remove()
        {
            Release();
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            GameObject shell = missile.gameObject;
            missile.Networkdisabled = true;
            shell.SetActive(false);
            if (spawner != null && spawner.IsServer && spawner.ServerObjectManager != null) spawner.ServerObjectManager.Destroy(shell);
            Destroy(shell); // the network destroy silently skips an unregistered identity
        }

        private void Publish() => KestrelNet.Broadcast(new KestrelState
        {
            Protocol = KestrelNet.ProtocolVersion, MissileId = missile.persistentID.Id, Phase = (byte)Phase,
            StationLeft = (uint)Mathf.CeilToInt(StationLeft), Spotted = (byte)Spotted,
            LasedId = Lased != null ? Lased.persistentID.Id : 0u,
        });

        public void Receive(KestrelState state)
        {
            if (missile.IsServer || state.Phase > (byte)KestrelPhase.Depart) return;
            Phase = (KestrelPhase)state.Phase;
            StationLeft = Mathf.Min(state.StationLeft, KestrelProfile.StationSeconds);
            Spotted = Mathf.Min(state.Spotted, KestrelProfile.SpotCap);
            Lased = state.LasedId != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = state.LasedId }, out Unit unit) ? unit : null;
        }

        private void Update()
        {
            // The board keeps a line for 1.5 s after its last Set, so 5 Hz is plenty and keeps the per-frame cost at zero.
            if (missile == null || missile.disabled || Time.unscaledTime < nextHud) return;
            nextHud = Time.unscaledTime + HudSeconds;
            if (missile.persistentID.Id == 0 || !GameManager.GetLocalAircraft(out Aircraft me) || me == null || missile.owner != me) return;
            if (hudDetail == null || hudSpotted != Spotted || !ReferenceEquals(hudLased, Lased))
            {
                hudSpotted = Spotted; hudLased = Lased;
                hudDetail = KestrelProfile.HudDetail(Spotted, Lased != null && Lased.definition != null ? Lased.definition.unitName : null);
            }
            KestrelHud.Show(hudKey ??= missile.persistentID.Id.ToString(), Phase, StationLeft, hudDetail);
        }

        private void OnDestroy() => Release();
    }
}

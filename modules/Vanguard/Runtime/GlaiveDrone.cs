using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Presentation;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// GLAIVE-2S SPECTRE. Host: guidance over native aerodynamics (left-hand pylon turn), HAND-OFF to the launcher's
    /// designation, port-side HE gun. Every peer: wings, propeller, orbit bank, gun pose and the launcher's HUD line.
    /// Rig joints are optional (fail soft); only the Gun component is required.
    /// </summary>
    internal sealed class GlaiveDrone : MonoBehaviour
    {
        private const float PlanSeconds = .2f;
        private const float HudSeconds = .2f;
        internal static readonly HashSet<GlaiveDrone> Active=new HashSet<GlaiveDrone>();
        private static readonly HashSet<int> RigRejected=new HashSet<int>(); // missiles already warned about (Attach runs per snapshot)
        internal static void ResetScene() { Active.Clear(); RigRejected.Clear(); }
        private Missile missile;
        private Gun gun;
        private Transform visual,yaw,pitch,muzzle,propeller,wingL,wingR;
        private Quaternion visualRest,yawRest,pitchRest,propellerRest,wingLRest,wingRRest;
        private Vector2 rest; // imported barrel yaw/pitch in the drone frame
        private BulletSim bullets;
        private Unit objectiveUnit,target,engaged,lastOrder;
        private GlobalPosition objective,centre;
        private Vector3 departureDirection;
        private float enteredOrbit,departedAt,nextPlan,nextShot,nextSnapshot;
        private float yawAngle,pitchAngle,desiredYaw,desiredPitch,bank;
        private uint shots,seenShots;
        private bool hasSnapshot,configured;
        private ParticleSystem[] flashes;
        private string hudKey;
        private float nextHud;
        public GlaivePhase Phase { get; private set; }
        /// <summary>Horizontal distance from the orbit centre to its objective (HAND-OFF still to run).</summary>
        public float Slide { get; private set; }
        public int RoundsLeft => GlaiveProfile.Rounds-(int)shots;
        public GlobalPosition Centre => centre;
        public float BankAngle => bank;

        private static class Effects
        {
            internal static readonly System.Action<Gun> Sound=AccessTools.MethodDelegate<System.Action<Gun>>(AccessTools.Method(typeof(Gun),"ShotSound"));
            internal static readonly AccessTools.FieldRef<Gun,ParticleSystem[]> Flashes=AccessTools.FieldRefAccess<Gun,ParticleSystem[]>("muzzleParticles");
        }

        public static GlaiveDrone Attach(Missile missile)
        {
            if (missile == null || missile.definition == null || VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Gunship) return null;
            var old=missile.GetComponent<GlaiveDrone>();
            if (old != null) return old;
            int id=missile.GetInstanceID();
            if (RigRejected.Contains(id)) return null;
            Transform visual=missile.transform.Find("VanguardVisual");
            var gun=missile.GetComponentInChildren<Gun>(true);
            if (visual == null || gun == null)
            {
                RigRejected.Add(id);
                Plugin.Logger?.LogWarning("[Vanguard] GLAIVE-2S has no visual or gun");
                return null;
            }
            var drone=missile.gameObject.AddComponent<GlaiveDrone>();
            drone.missile=missile; drone.gun=gun; drone.visual=visual; drone.visualRest=visual.localRotation;
            Transform breech=null;
            foreach (Transform part in visual.GetComponentsInChildren<Transform>(true))
                switch (part.name)
                {
                    case "GunYaw": drone.yaw=part; break;
                    case "GunPitch": drone.pitch=part; break;
                    case "GlaiveMuzzle": drone.muzzle=part; break;
                    case "GlaiveBreech": breech=part; break;
                    case "Propeller": drone.propeller=part; break;
                    case "WingL": drone.wingL=part; break;
                    case "WingR": drone.wingR=part; break;
                }
            if (drone.muzzle == null)
            {
                drone.muzzle=new GameObject("GlaiveMuzzle").transform;
                drone.muzzle.SetParent(missile.transform,false);
                drone.muzzle.localPosition=new Vector3(-.4f,-.15f,0f);
                Plugin.Logger?.LogWarning("[Vanguard] GLAIVE-2S rig has no GlaiveMuzzle; firing from the hull"); // once: the component now exists
            }
            Vector3 barrel=breech != null ? drone.muzzle.position-breech.position : missile.transform.forward;
            drone.rest=GlaiveProfile.GunAngles(missile.transform.InverseTransformDirection(barrel.normalized));
            drone.yawAngle=drone.desiredYaw=drone.rest.x; drone.pitchAngle=drone.desiredPitch=drone.rest.y;
            if (drone.yaw != null) drone.yawRest=drone.yaw.localRotation;
            if (drone.pitch != null) drone.pitchRest=drone.pitch.localRotation;
            if (drone.propeller != null) drone.propellerRest=drone.propeller.localRotation;
            if (drone.wingL != null) drone.wingLRest=drone.wingL.localRotation;
            if (drone.wingR != null) drone.wingRRest=drone.wingR.localRotation;
            drone.Wings();
            gun.AttachToUnit(missile); gun.ForceServerAuthority=true;
            drone.flashes=Effects.Flashes(gun);
            if (drone.flashes != null) foreach (var flash in drone.flashes)
                if (flash != null) flash.transform.localScale*=.35f;
            Active.Add(drone);
            return drone;
        }

        public void Configure(Unit requested,GlobalPosition aim)
        {
            if (!missile.IsServer || configured) return;
            configured=true; objective=centre=aim;
            objectiveUnit=GroundHostile(requested) ? requested : null;
            // A designation carries where its target stands, on the faction picture or not (else: 100 km straight ahead).
            if (objectiveUnit != null) objective=centre=objectiveUnit.GlobalPosition();
            lastOrder=Order(); // only a CHANGE of designation after launch hands the drone off
            Phase=GlaivePhase.Launch;
            VanguardStats.Bump("glaive2s.launch");
        }

        public void TickHost()
        {
            if (!configured || !missile.IsServer || missile.disabled) return;
            float age=missile.timeSinceSpawn;
            if (Time.time >= nextPlan) { nextPlan=Time.time+PlanSeconds; Plan(age); if (missile.disabled) return; Aim(); }
            Pose(Time.fixedDeltaTime,true);
            if (Phase == GlaivePhase.Orbit && Time.time >= nextShot && GlaiveProfile.Burst(Time.time-enteredOrbit)) TryFire();
            if (Time.time >= nextSnapshot) { nextSnapshot=Time.time+.2f; Publish(); }
        }

        private void Plan(float age)
        {
            GlobalPosition here=missile.GlobalPosition();
            missile.UpdateRadarAlt();
            if (GlaiveProfile.Crashing(missile.radarAlt,age)) { missile.Detonate(Vector3.up,false,false); return; }
            if (engaged != null && engaged.disabled) { VanguardStats.Bump("glaive2s.kills"); engaged=null; }
            Designate();
            if (TryPosition(objectiveUnit,out GlobalPosition seen)) objective=seen;
            if (Phase == GlaivePhase.Orbit && GlaiveProfile.Retire(RoundsLeft,Time.time-enteredOrbit)) Depart();
            if (Phase == GlaivePhase.Depart)
            {
                GlobalPosition exit=here+departureDirection*1400f; exit.y=here.y+100f;
                missile.SetThrottle(.7f); missile.SetAimpoint(exit,Vector3.zero);
                if (Time.time-departedAt > 8f) missile.Detonate(Vector3.up,false,false);
                return;
            }
            if (age < GlaiveProfile.LaunchSeconds)
            {
                GlobalPosition climb=objective; climb.y=Mathf.Max(objective.y+GlaiveProfile.Height((objective-here).magnitude),here.y+80f);
                missile.SetThrottle(1f); missile.SetAimpoint(climb,Vector3.zero);
                return;
            }
            if (Phase == GlaivePhase.Launch) Phase=GlaivePhase.Ingress;
            Vector3 toObjective=objective-here;
            float flat=new Vector2(toObjective.x,toObjective.z).magnitude;
            if (Phase == GlaivePhase.Ingress && GlaiveProfile.Capture(flat,missile.radarAlt,age,missile.speed))
            {
                Phase=GlaivePhase.Orbit; centre=objective; enteredOrbit=Time.time;
                VanguardStats.Bump("glaive2s.orbit");
            }
            missile.SetThrottle(GlaiveProfile.Throttle(missile.speed));
            if (Phase == GlaivePhase.Orbit)
            {
                centre+=GlaiveProfile.SlideStep(objective-centre,PlanSeconds);
                Vector3 left=objective-centre; Slide=new Vector2(left.x,left.z).magnitude;
                GlobalPosition waypoint=here+GlaiveProfile.OrbitOffset(here-centre,missile.transform.forward);
                waypoint.y=Mathf.Max(centre.y+GlaiveProfile.OrbitHeight,here.y-missile.radarAlt+GlaiveProfile.MinOrbitAgl);
                missile.SetAimpoint(waypoint,Vector3.zero);
            }
            else
            {
                GlobalPosition ingress=objective; ingress.y=objective.y+GlaiveProfile.Height(flat);
                missile.SetAimpoint(ingress,Vector3.zero);
            }
        }

        private void Depart()
        {
            Phase=GlaivePhase.Depart; departedAt=Time.time; Slide=0f;
            departureDirection=Vector3.ProjectOnPlane(missile.transform.forward,Vector3.up).normalized;
            VanguardStats.Bump("glaive2s.depart");
            VanguardStats.Bump(RoundsLeft <= 0 ? "glaive2s.winchester" : "glaive2s.offstation");
        }

        // HAND-OFF: the launcher's designation (radial DESIGNATE via DroneOrders); AI launchers: their combat target.
        private Unit Order()
        {
            Unit launcher=missile.owner;
            Unit order=DroneOrders.StrikeTarget(launcher);
            if (order == null && launcher is Aircraft aircraft && aircraft.Player == null && aircraft.pilots != null &&
                aircraft.pilots.Length > 0 && aircraft.pilots[0] != null && aircraft.pilots[0].currentState is AIPilotCombatModes combat)
                order=GameAccess.GetAiCurrentTarget(combat);
            return order;
        }

        private void Designate()
        {
            Unit order=Order();
            if (order == lastOrder) return;
            lastOrder=order;
            if (order == objectiveUnit || !GroundHostile(order)) return;
            objectiveUnit=order;
            objective=order.GlobalPosition();
            VanguardStats.Bump("glaive2s.handoff");
        }

        private bool Hostile(Unit unit) => unit != null && unit.definition != null && !unit.disabled &&
            missile.NetworkHQ != null && unit.NetworkHQ != null && unit.NetworkHQ != missile.NetworkHQ;
        private bool GroundHostile(Unit unit) => Hostile(unit) && !(unit is Aircraft) && !(unit is Missile);

        // Faction picture first, else the drone's own sight inside gun range.
        private bool TryPosition(Unit unit,out GlobalPosition position)
        {
            position=default;
            if (!Hostile(unit)) return false;
            if (missile.NetworkHQ.TryGetKnownPosition(unit,out position)) return true;
            position=unit.GlobalPosition();
            return FastMath.InRange(position,missile.GlobalPosition(),GlaiveProfile.GunRange+300f);
        }

        private bool TryAimpoint(Unit unit,out GlobalPosition aim)
        {
            if (!TryPosition(unit,out aim)) return false;
            Vector3 fromCentre=aim-centre; fromCentre.y=0f;
            if (fromCentre.magnitude > GlaiveProfile.Reach) return false;
            aim+=Vector3.up*Mathf.Clamp(unit.definition.height*.4f,.4f,3f);
            return true;
        }

        private Unit Nearest()
        {
            return missile.NetworkHQ != null && missile.NetworkHQ.TryGetNearestGroundEnemy(centre,out TrackingInfo track) &&
                track.TryGetUnit(out Unit candidate) && TryAimpoint(candidate,out _) ? candidate : null;
        }

        private Vector3 Solution(Vector3 to)
        {
            float travel=Mathf.Clamp(to.magnitude/gun.info.muzzleVelocity,0f,2f);
            Vector3 velocity=target != null && target.rb != null ? target.rb.velocity : Vector3.zero;
            return (to+(velocity-missile.rb.velocity)*travel-Physics.gravity*(.5f*travel*travel)).normalized;
        }

        private Quaternion Frame() => missile.transform.rotation*Quaternion.AngleAxis(bank,Vector3.forward);

        private void Aim()
        {
            if (Phase != GlaivePhase.Orbit) { desiredYaw=rest.x; desiredPitch=rest.y; return; }
            if (TryAimpoint(objectiveUnit,out _)) target=objectiveUnit;
            else if (!TryAimpoint(target,out _)) target=Nearest();
            GlobalPosition from=muzzle.position.ToGlobalPosition();
            Vector3 dir=target != null && TryAimpoint(target,out GlobalPosition aim) ? Solution(aim-from) : (centre-from).normalized;
            Vector2 angles=GlaiveProfile.ClampGun(GlaiveProfile.GunAngles(Quaternion.Inverse(Frame())*dir));
            desiredYaw=angles.x; desiredPitch=angles.y;
        }

        private void TryFire()
        {
            nextShot=Time.time+.1f; // re-check cadence while the gun cannot bear
            if (!TryAimpoint(target,out GlobalPosition aim)) return;
            Vector3 to=aim-muzzle.position.ToGlobalPosition();
            if (!GlaiveProfile.CanFire(to.magnitude,-to.y,missile.speed,Vector3.Dot(muzzle.forward,Solution(to)))) return;
            if (Physics.Linecast(muzzle.position+to.normalized*.15f,aim.ToLocalPosition()-to.normalized*.4f,(int)PhysicsLayers.StaticsMask)) return;
            Fire(target); shots++; gun.ammo=RoundsLeft; engaged=target;
            missile.rb.AddForceAtPosition(-muzzle.forward*GlaiveProfile.RecoilImpulse,muzzle.position,ForceMode.Impulse);
            nextShot=Time.time+GlaiveProfile.ShotInterval;
            VanguardStats.Bump("glaive2s.shots");
        }

        private void Pose(float dt,bool slew)
        {
            if (slew)
            {
                yawAngle=Mathf.MoveTowardsAngle(yawAngle,desiredYaw,GlaiveProfile.AimRate*dt);
                pitchAngle=Mathf.MoveTowards(pitchAngle,desiredPitch,GlaiveProfile.AimRate*dt);
            }
            Quaternion frame=Frame();
            Vector3 up=frame*Vector3.up;
            Vector3 right=Quaternion.AngleAxis(yawAngle,up)*(frame*Vector3.right);
            if (yaw != null) yaw.localRotation=Quaternion.AngleAxis(yawAngle-rest.x,yaw.parent.InverseTransformDirection(up))*yawRest;
            if (pitch != null) pitch.localRotation=Quaternion.AngleAxis(pitchAngle-rest.y,pitch.parent.InverseTransformDirection(right))*pitchRest;
            Vector3 actual=frame*GlaiveProfile.Barrel(yawAngle,pitchAngle);
            muzzle.rotation=Quaternion.LookRotation(actual,up);
        }

        private void Fire(Unit at)
        {
            Effects.Sound(gun);
            if (flashes != null) foreach (var flash in flashes) if (flash != null)
            {
                flash.transform.SetPositionAndRotation(muzzle.position,muzzle.rotation);
                var renderer=flash.GetComponent<ParticleSystemRenderer>(); if (renderer != null) renderer.enabled=true;
                flash.Play();
            }
            if (bullets == null) bullets=BulletSim.Create(missile,gun,null);
            // Passing the target arms the vanilla proximity fuze against soft targets (HE airburst).
            bullets.AddBullet(muzzle,missile.rb.velocity+muzzle.forward*gun.info.muzzleVelocity,6f,2f,true,.7f,
                new Color(1f,.6f,.25f),0f,at);
        }

        private void Publish() => GlaiveNet.Broadcast(new GlaiveState
        { Protocol=GlaiveNet.ProtocolVersion,MissileId=missile.persistentID.Id,Phase=(byte)Phase,Yaw=yawAngle,Pitch=pitchAngle,Shots=shots,Slide=Slide });

        public void Receive(GlaiveState state)
        {
            if (missile.IsServer || state.Phase > (byte)GlaivePhase.Depart || !float.IsFinite(state.Yaw) ||
                !float.IsFinite(state.Pitch) || !float.IsFinite(state.Slide)) return;
            Phase=(GlaivePhase)state.Phase; Slide=Mathf.Clamp(state.Slide,0f,50000f);
            desiredYaw=Mathf.Clamp(state.Yaw,-180f,180f); desiredPitch=Mathf.Clamp(state.Pitch,-90f,90f);
            if (hasSnapshot && state.Shots > seenShots)
                for (uint i=0;i<System.Math.Min(state.Shots-seenShots,5u);i++) Fire(null);
            seenShots=System.Math.Max(seenShots,state.Shots); shots=seenShots; hasSnapshot=true;
        }

        private void LateUpdate()
        {
            if (missile == null || missile.disabled) return;
            bank=Mathf.MoveTowards(bank,GlaiveProfile.Bank(Phase),GlaiveProfile.BankRate*Time.deltaTime);
            visual.localRotation=Quaternion.AngleAxis(bank,Vector3.forward)*visualRest;
            Pose(Time.deltaTime,!missile.IsServer);
            Wings();
            if (propeller != null)
            {
                Vector3 axis=propeller.parent.InverseTransformDirection(missile.transform.forward);
                propeller.localRotation=Quaternion.AngleAxis(Mathf.Max(0f,missile.timeSinceSpawn-GlaiveProfile.LaunchSeconds)*3600f,axis)*propellerRest;
            }
            // The board keeps a line for 1.5 s after its last Set, so 5 Hz is plenty and keeps the per-frame cost at zero.
            if (Time.unscaledTime >= nextHud)
            {
                nextHud=Time.unscaledTime+HudSeconds;
                if (missile.persistentID.Id != 0 && GameManager.GetLocalAircraft(out Aircraft me) && me != null && missile.owner == me)
                    Glaive2sHud.Show(hudKey ??= missile.persistentID.Id.ToString(),Phase,RoundsLeft,Slide);
            }
        }

        private void Wings()
        {
            float fold=1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.3f,1.2f,missile.timeSinceSpawn));
            if (wingL != null) wingL.localRotation=Quaternion.AngleAxis(-90f*fold,wingL.parent.InverseTransformDirection(missile.transform.up))*wingLRest;
            if (wingR != null) wingR.localRotation=Quaternion.AngleAxis(90f*fold,wingR.parent.InverseTransformDirection(missile.transform.up))*wingRRest;
        }

        private void OnDestroy() => Active.Remove(this);
    }
}

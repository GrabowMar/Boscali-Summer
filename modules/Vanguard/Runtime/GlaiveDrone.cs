using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Host guidance over native aerodynamics; clients animate and render shots only.</summary>
    internal sealed class GlaiveDrone : MonoBehaviour
    {
        internal static readonly HashSet<GlaiveDrone> Active=new HashSet<GlaiveDrone>();
        private Missile missile;
        private Gun gun;
        private Transform yaw,pitch,muzzle,propeller,wingL,wingR;
        private Quaternion yawRest,pitchRest,propellerRest,wingLRest,wingRRest;
        private BulletSim bullets;
        private Unit target;
        private GlobalPosition objective,centre;
        private Vector3 departureDirection,desiredAim;
        private float enteredOrbit,departedAt,nextPlan,nextShot,nextSnapshot;
        private float yawAngle,pitchAngle,desiredYaw,desiredPitch;
        private uint shots,seenShots;
        private bool hasSnapshot,configured;
        private ParticleSystem[] flashes;
        public GlaivePhase Phase { get; private set; }

        private static class Effects
        {
            internal static readonly System.Action<Gun> Sound=AccessTools.MethodDelegate<System.Action<Gun>>(AccessTools.Method(typeof(Gun),"ShotSound"));
            internal static readonly AccessTools.FieldRef<Gun,ParticleSystem[]> Flashes=AccessTools.FieldRefAccess<Gun,ParticleSystem[]>("muzzleParticles");
        }

        public static GlaiveDrone Attach(Missile missile)
        {
            if (missile == null || missile.definition == null || VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Carrier) return null;
            var old=missile.GetComponent<GlaiveDrone>();
            if (old != null) return old;
            Transform visual=missile.transform.Find("VanguardVisual");
            var gun=missile.GetComponentInChildren<Gun>(true);
            if (visual == null || gun == null) return null;
            var drone=missile.gameObject.AddComponent<GlaiveDrone>();
            drone.missile=missile; drone.gun=gun;
            foreach (Transform part in visual.GetComponentsInChildren<Transform>(true))
            {
                if (part.name == "GunYaw") drone.yaw=part;
                else if (part.name == "GunPitch") drone.pitch=part;
                else if (part.name == "GlaiveMuzzle") drone.muzzle=part;
                else if (part.name == "Propeller") drone.propeller=part;
                else if (part.name == "WingL") drone.wingL=part;
                else if (part.name == "WingR") drone.wingR=part;
            }
            if (drone.yaw == null || drone.pitch == null || drone.muzzle == null || drone.propeller == null || drone.wingL == null || drone.wingR == null)
            {
                Destroy(drone); Plugin.Logger?.LogWarning("[Vanguard] GLAIVE drone rig incomplete"); return null;
            }
            drone.yawRest=drone.yaw.localRotation; drone.pitchRest=drone.pitch.localRotation;
            drone.propellerRest=drone.propeller.localRotation;
            drone.wingLRest=drone.wingL.localRotation; drone.wingRRest=drone.wingR.localRotation;
            drone.Wings();
            gun.AttachToUnit(missile); gun.ForceServerAuthority=true;
            drone.flashes=Effects.Flashes(gun);
            if (drone.flashes != null) foreach (var flash in drone.flashes)
                if (flash != null) flash.transform.localScale*=.18f;
            Active.Add(drone);
            return drone;
        }

        public void Configure(Unit requested,GlobalPosition aim)
        {
            if (!missile.IsServer || configured) return;
            configured=true; target=requested; objective=centre=aim;
            desiredAim=missile.transform.forward;
            Phase=GlaivePhase.Launch;
        }

        public void TickHost()
        {
            if (!configured || !missile.IsServer || missile.disabled) return;
            float age=missile.timeSinceSpawn;
            if (Time.time >= nextPlan) { nextPlan=Time.time+.2f; Plan(age); if (missile.disabled) return; Aim(); }
            Pose(Time.fixedDeltaTime,true);
            if (Phase == GlaivePhase.Orbit && GlaiveProfile.Burst(Time.time-enteredOrbit) && Time.time >= nextShot &&
                TryAimpoint(out GlobalPosition aim))
            {
                Vector3 to=aim-muzzle.position.ToGlobalPosition();
                bool visible=!Physics.Linecast(muzzle.position+to.normalized*.15f,aim.ToLocalPosition()-to.normalized*.4f,(int)PhysicsLayers.StaticsMask);
                float alignment=Vector3.Dot(muzzle.forward,desiredAim);
                if (GlaiveProfile.CanFire(to.magnitude,-to.y,missile.speed,alignment,Hostile(target),visible))
                {
                    Fire(); shots++; gun.ammo=GlaiveProfile.Rounds-(int)shots;
                    missile.rb.AddForceAtPosition(-muzzle.forward*GlaiveProfile.RecoilImpulse,muzzle.position,ForceMode.Impulse);
                    nextShot=Time.time+GlaiveProfile.ShotInterval;
                    VanguardStats.TurretShots++;
                }
            }
            if (Time.time >= nextSnapshot) { nextSnapshot=Time.time+.2f; Publish(); }
        }

        private void Plan(float age)
        {
            GlobalPosition here=missile.GlobalPosition();
            missile.UpdateRadarAlt();
            if (missile.radarAlt <= GlaiveProfile.RetireHeight && age > 5f)
            { missile.Detonate(Vector3.up,false,false); return; }
            if (Phase != GlaivePhase.Depart && GlaiveProfile.Retire(missile.radarAlt,GlaiveProfile.Rounds-(int)shots,age))
            {
                Phase=GlaivePhase.Depart; departedAt=Time.time;
                departureDirection=Vector3.ProjectOnPlane(missile.transform.forward,Vector3.up).normalized;
            }
            if (Phase == GlaivePhase.Depart)
            {
                GlobalPosition exit=here+departureDirection*1400f; exit.y=here.y+100f;
                missile.SetThrottle(.7f); missile.SetAimpoint(exit,Vector3.zero);
                if (Time.time-departedAt > 8f) missile.Detonate(Vector3.up,false,false);
                return;
            }
            if (age < GlaiveProfile.LaunchSeconds)
            {
                Phase=GlaivePhase.Launch;
                GlobalPosition climb=objective; climb.y=System.Math.Max(objective.y+GlaiveProfile.Height((objective-here).magnitude),here.y+80f);
                missile.SetThrottle(1f); missile.SetAimpoint(climb,Vector3.zero);
                return;
            }
            if (Phase == GlaivePhase.Launch) Phase=GlaivePhase.Ingress;
            Vector3 toObjective=objective-here;
            float flat=new Vector2(toObjective.x,toObjective.z).magnitude;
            if (Phase == GlaivePhase.Ingress && GlaiveProfile.Capture(flat,missile.radarAlt,age,missile.speed))
            {
                Phase=GlaivePhase.Orbit; centre=objective; enteredOrbit=Time.time;
                VanguardStats.TurretsDeployed++;
            }
            missile.SetThrottle(GlaiveProfile.Throttle(missile.speed));
            if (Phase == GlaivePhase.Orbit)
            {
                Vector3 radial=here-centre;
                GlobalPosition waypoint=here+GlaiveProfile.OrbitOffset(radial,missile.transform.forward);
                waypoint.y=centre.y+GlaiveProfile.OrbitHeight;
                missile.SetAimpoint(waypoint,Vector3.zero);
            }
            else
            {
                GlobalPosition ingress=objective; ingress.y=objective.y+GlaiveProfile.Height(flat);
                missile.SetAimpoint(ingress,Vector3.zero);
            }
        }

        private bool Hostile(Unit unit) => unit != null && unit.definition != null && !unit.disabled &&
            missile.NetworkHQ != null && unit.NetworkHQ != null && unit.NetworkHQ != missile.NetworkHQ;

        private bool TryAimpoint(out GlobalPosition aim)
        {
            aim=default;
            if (!Hostile(target) || !missile.NetworkHQ.TryGetKnownPosition(target,out aim)) return false;
            Vector3 fromCentre=aim-centre; fromCentre.y=0;
            if (fromCentre.magnitude > 500f) return false;
            aim+=Vector3.up*Mathf.Clamp(target.definition.height*.4f,.4f,3f);
            return true;
        }

        private void Aim()
        {
            if (Phase != GlaivePhase.Orbit) { desiredYaw=desiredPitch=0f; return; }
            if (!TryAimpoint(out GlobalPosition aim))
            {
                target=null;
                if (missile.NetworkHQ != null && missile.NetworkHQ.TryGetNearestGroundEnemy(centre,out TrackingInfo track) &&
                    track.TryGetUnit(out Unit candidate) && Hostile(candidate)) target=candidate;
                if (!TryAimpoint(out aim)) return;
            }
            Vector3 to=aim-muzzle.position.ToGlobalPosition();
            float travel=Mathf.Clamp(to.magnitude/gun.info.muzzleVelocity,0f,1f);
            Vector3 velocity=target.rb != null ? target.rb.velocity : Vector3.zero;
            desiredAim=(to+(velocity-missile.rb.velocity)*travel-Physics.gravity*(.5f*travel*travel)).normalized;
            Vector3 local=missile.transform.InverseTransformDirection(desiredAim);
            desiredYaw=Mathf.Clamp(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,-110f,110f);
            desiredPitch=Mathf.Clamp(Mathf.Atan2(-local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg,0f,80f);
        }

        private void Pose(float dt,bool slew)
        {
            if (slew)
            {
                yawAngle=Mathf.MoveTowardsAngle(yawAngle,desiredYaw,GlaiveProfile.AimRate*dt);
                pitchAngle=Mathf.MoveTowards(pitchAngle,desiredPitch,GlaiveProfile.AimRate*dt);
            }
            Vector3 up=missile.transform.up;
            yaw.localRotation=Quaternion.AngleAxis(yawAngle,yaw.parent.InverseTransformDirection(up))*yawRest;
            Vector3 right=Quaternion.AngleAxis(yawAngle,up)*missile.transform.right;
            pitch.localRotation=Quaternion.AngleAxis(pitchAngle,pitch.parent.InverseTransformDirection(right))*pitchRest;
            Vector3 actual=Quaternion.AngleAxis(pitchAngle,right)*Quaternion.AngleAxis(yawAngle,up)*missile.transform.forward;
            muzzle.rotation=Quaternion.LookRotation(actual,up);
        }

        private void Fire()
        {
            Effects.Sound(gun);
            if (flashes != null) foreach (var flash in flashes) if (flash != null)
            {
                flash.transform.SetPositionAndRotation(muzzle.position,muzzle.rotation);
                var renderer=flash.GetComponent<ParticleSystemRenderer>(); if (renderer != null) renderer.enabled=true;
                flash.Play();
            }
            if (bullets == null) bullets=BulletSim.Create(missile,gun,null);
            bullets.AddBullet(muzzle,missile.rb.velocity+muzzle.forward*gun.info.muzzleVelocity,.8f,1.2f,true,.45f,
                new Color(1f,.65f,.3f),0f,target);
        }

        private void Publish() => GlaiveNet.Broadcast(new GlaiveState
        { Protocol=GlaiveNet.ProtocolVersion,MissileId=missile.persistentID.Id,Phase=(byte)Phase,Yaw=yawAngle,Pitch=pitchAngle,Shots=shots });

        public void Receive(GlaiveState state)
        {
            if (missile.IsServer || state.Phase > (byte)GlaivePhase.Depart || !float.IsFinite(state.Yaw) || !float.IsFinite(state.Pitch)) return;
            Phase=(GlaivePhase)state.Phase; yawAngle=Mathf.Clamp(state.Yaw,-110f,110f); pitchAngle=Mathf.Clamp(state.Pitch,0f,80f);
            Pose(0f,false);
            if (hasSnapshot && state.Shots > seenShots)
                for (uint i=0;i<System.Math.Min(state.Shots-seenShots,4u);i++) Fire();
            seenShots=System.Math.Max(seenShots,state.Shots); hasSnapshot=true;
        }

        private void LateUpdate()
        {
            if (missile == null || missile.disabled) return;
            if (!missile.IsServer && hasSnapshot) Pose(Time.deltaTime,false);
            Wings();
            Vector3 axis=propeller.parent.InverseTransformDirection(missile.transform.forward);
            propeller.localRotation=Quaternion.AngleAxis(Mathf.Max(0f,missile.timeSinceSpawn-GlaiveProfile.LaunchSeconds)*3600f,axis)*propellerRest;
        }
        private void Wings()
        {
            float fold=1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.3f,1.2f,missile.timeSinceSpawn));
            wingL.localRotation=Quaternion.AngleAxis(-90f*fold,wingL.parent.InverseTransformDirection(missile.transform.up))*wingLRest;
            wingR.localRotation=Quaternion.AngleAxis(90f*fold,wingR.parent.InverseTransformDirection(missile.transform.up))*wingRRest;
        }
        private void OnDestroy() => Active.Remove(this);
    }
}

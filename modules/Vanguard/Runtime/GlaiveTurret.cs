using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using UnityEngine;
using HarmonyLib;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Parachute gun pod. Only the server flies and damages; peers receive visual state.</summary>
    internal sealed class GlaiveTurret : MonoBehaviour
    {
        internal static readonly HashSet<GlaiveTurret> Active = new HashSet<GlaiveTurret>();
        private Missile missile;
        private Gun gun;
        private Transform canopy, yaw, pitch, muzzle, left, right;
        private Quaternion yawRest, pitchRest, leftRest, rightRest, heading;
        private BulletSim bullets;
        private Unit target;
        private GlobalPosition anchor;
        private Vector3 initialVelocity, aimDirection;
        private float deployedAt, nextAim, nextShot, nextSnapshot;
        private uint shots, seenShots;
        private bool hasSnapshot;
        private float inflation;
        private ParticleSystem[] flashes;
        private static readonly System.Action<Gun> ShotSound = AccessTools.MethodDelegate<System.Action<Gun>>(
            AccessTools.Method(typeof(Gun), "ShotSound"));
        private static readonly AccessTools.FieldRef<Gun,ParticleSystem[]> MuzzleParticles =
            AccessTools.FieldRefAccess<Gun,ParticleSystem[]>("muzzleParticles");
        public bool Deployed { get; private set; }

        public static GlaiveTurret Attach(Missile missile)
        {
            if (VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Carrier) return null;
            var existing = missile.GetComponent<GlaiveTurret>();
            if (existing != null) return existing;
            Transform visual = missile.transform.Find("VanguardVisual");
            Transform canopy = missile.transform.Find("GlaiveCanopy");
            var gun = missile.GetComponentInChildren<Gun>(true);
            if (visual == null || canopy == null || gun == null) return null;
            var pod = missile.gameObject.AddComponent<GlaiveTurret>();
            pod.missile = missile;
            pod.gun = gun;
            pod.canopy = canopy;
            foreach (Transform part in visual.GetComponentsInChildren<Transform>(true))
            {
                if (part.name == "GunYaw") pod.yaw = part;
                else if (part.name == "GunPitch") pod.pitch = part;
                else if (part.name == "GlaiveMuzzle") pod.muzzle = part;
                else if (part.name == "WingL") pod.left = part;
                else if (part.name == "WingR") pod.right = part;
            }
            if (pod.yaw == null || pod.pitch == null || pod.muzzle == null || pod.left == null || pod.right == null)
            {
                Destroy(pod);
                Plugin.Logger?.LogWarning("[Vanguard] GLAIVE rig incomplete; turret disabled");
                return null;
            }
            pod.yawRest = pod.yaw.localRotation;
            pod.pitchRest = pod.pitch.localRotation;
            pod.leftRest = pod.left.localRotation;
            pod.rightRest = pod.right.localRotation;
            gun.AttachToUnit(missile);
            gun.ForceServerAuthority = true;
            pod.flashes = MuzzleParticles(gun);
            return pod;
        }

        public void Deploy(Unit requestedTarget)
        {
            if (!missile.IsServer || Deployed) return;
            Deployed = true;
            target = requestedTarget;
            anchor = missile.GlobalPosition();
            initialVelocity = missile.rb.velocity;
            Vector3 forward = Vector3.ProjectOnPlane(missile.transform.forward, Vector3.up);
            heading = Quaternion.LookRotation(forward.sqrMagnitude > .01f ? forward.normalized : Vector3.forward);
            deployedAt = Time.time;
            missile.SetThrottle(0f);
            missile.rb.useGravity = false;
            aimDirection = Vector3.down;
            Active.Add(this);
            VanguardStats.TurretsDeployed++;
            Publish();
        }

        /// <summary>Replaces native ServerFixedUpdate while suspended; native age/network lifecycle still runs.</summary>
        public void TickHost()
        {
            if (!Deployed || !missile.IsServer || missile.disabled) return;
            float age = Time.time - deployedAt;
            missile.UpdateRadarAlt();
            if (GlaiveProfile.Retire(missile.radarAlt, GlaiveProfile.Rounds - (int)shots, age))
            {
                missile.Detonate(Vector3.up, false, false);
                return;
            }
            inflation = GlaiveProfile.Inflation(age);
            Rigidbody body = missile.rb;
            body.useGravity = false;
            body.angularVelocity = Vector3.zero;
            body.MoveRotation(Quaternion.Slerp(body.rotation, heading, Mathf.Clamp01(Time.fixedDeltaTime * 4f)));
            body.velocity = Vector3.Lerp(initialVelocity, Vector3.down * GlaiveProfile.DescentSpeed, inflation);
            if (inflation >= 1f)
            {
                GlobalPosition here = missile.GlobalPosition();
                Vector3 position = here.ToLocalPosition();
                Vector3 horizontal = anchor.ToLocalPosition();
                position.x = Mathf.Lerp(position.x, horizontal.x, Mathf.Clamp01(Time.fixedDeltaTime * 3f));
                position.z = Mathf.Lerp(position.z, horizontal.z, Mathf.Clamp01(Time.fixedDeltaTime * 3f));
                body.MovePosition(position);
            }
            if (Time.time >= nextAim)
            {
                nextAim = Time.time + .2f;
                Aim();
            }
            Pose();
            if (Time.time >= nextShot && GlaiveProfile.Burst(age) && TryAimpoint(out GlobalPosition aim))
            {
                Vector3 to = aim - missile.GlobalPosition();
                Vector3 shot = aim - muzzle.position.ToGlobalPosition();
                bool visible = !Physics.Linecast(muzzle.position + shot.normalized * .25f,
                    aim.ToLocalPosition() - shot.normalized * .5f, (int)PhysicsLayers.StaticsMask);
                if (GlaiveProfile.CanFire(to.magnitude, -to.y, Hostile(target), visible))
                {
                    nextShot = Time.time + GlaiveProfile.ShotInterval;
                    Fire();
                    shots++;
                    gun.ammo = GlaiveProfile.Rounds - (int)shots;
                    VanguardStats.TurretShots++;
                }
            }
            if (Time.time >= nextSnapshot)
            {
                nextSnapshot = Time.time + .2f;
                Publish();
            }
        }

        private bool Hostile(Unit unit) => unit != null && unit.definition != null && !unit.disabled && unit != missile &&
            missile.NetworkHQ != null && unit.NetworkHQ != null && unit.NetworkHQ != missile.NetworkHQ;

        private bool TryAimpoint(out GlobalPosition aim)
        {
            aim = default;
            if (!Hostile(target) || !missile.NetworkHQ.TryGetKnownPosition(target, out aim)) return false;
            aim += Vector3.up * Mathf.Clamp(target.definition.height * .4f, .4f, 4f);
            return true;
        }

        private void Aim()
        {
            if (!TryAimpoint(out GlobalPosition aim))
            {
                target = null;
                if (missile.NetworkHQ != null && missile.NetworkHQ.TryGetNearestGroundEnemy(missile.GlobalPosition(), out TrackingInfo track)
                    && track.TryGetUnit(out Unit candidate) && Hostile(candidate)) target = candidate;
                if (!TryAimpoint(out aim)) return;
            }
            Vector3 to = aim - missile.GlobalPosition();
            float time = Mathf.Clamp(to.magnitude / gun.info.muzzleVelocity, 0f, 1.5f);
            Vector3 targetVelocity = target.rb != null ? target.rb.velocity : Vector3.zero;
            to += (targetVelocity - missile.rb.velocity) * time - Physics.gravity * (.5f*time*time);
            if (to.sqrMagnitude > 1f) aimDirection = to.normalized;
        }

        private void Fire()
        {
            ShotSound(gun);
            if (flashes != null)
                foreach (ParticleSystem flash in flashes)
                    if (flash != null)
                    {
                        flash.transform.SetPositionAndRotation(muzzle.position, Quaternion.LookRotation(aimDirection));
                        var renderer = flash.GetComponent<ParticleSystemRenderer>();
                        if (renderer != null) renderer.enabled = true;
                        flash.Play();
                    }
            if (bullets == null) bullets = BulletSim.Create(missile, gun, null);
            bullets.AddBullet(muzzle, missile.rb.velocity + aimDirection * gun.info.muzzleVelocity,
                1.8f, 2f, true, 1.2f, new Color(1f,.55f,.18f), 0f, target);
        }

        private void Publish() => GlaiveNet.Broadcast(new GlaiveState
        {
            Protocol = GlaiveNet.ProtocolVersion, MissileId = missile.persistentID.Id,
            Inflation = inflation, Aim = aimDirection, Shots = shots,
        });

        public void Receive(GlaiveState state)
        {
            if (missile.IsServer || !float.IsFinite(state.Inflation) ||
                !float.IsFinite(state.Aim.x) || !float.IsFinite(state.Aim.y) || !float.IsFinite(state.Aim.z)) return;
            Deployed = true;
            inflation = Mathf.Clamp01(state.Inflation);
            aimDirection = state.Aim.sqrMagnitude > .1f ? state.Aim.normalized : Vector3.down;
            missile.SetThrottle(0f);
            Pose();
            // A late joiner starts with the current count, never replays old rounds.
            if (hasSnapshot && state.Shots > seenShots)
                for (uint i = 0; i < System.Math.Min(state.Shots - seenShots, 4u); i++) Fire();
            seenShots = System.Math.Max(seenShots, state.Shots);
            hasSnapshot = true;
        }

        private void LateUpdate()
        {
            if (Deployed && !missile.disabled && !missile.IsServer) Pose();
        }

        private void Pose()
        {
            canopy.gameObject.SetActive(true);
            canopy.localScale = Vector3.one * Mathf.Max(.015f, inflation);
            Vector3 up = missile.transform.up;
            left.localRotation = Quaternion.AngleAxis(-90f * inflation, left.parent.InverseTransformDirection(up)) * leftRest;
            right.localRotation = Quaternion.AngleAxis(90f * inflation, right.parent.InverseTransformDirection(up)) * rightRest;
            Vector3 flat = Vector3.ProjectOnPlane(aimDirection, Vector3.up);
            float yawAngle = flat.sqrMagnitude < .001f ? 0f : Vector3.SignedAngle(
                Vector3.ProjectOnPlane(missile.transform.forward, Vector3.up), flat, Vector3.up);
            yaw.localRotation = Quaternion.AngleAxis(yawAngle, yaw.parent.InverseTransformDirection(Vector3.up)) * yawRest;
            Vector3 rightAxis = Quaternion.AngleAxis(yawAngle, Vector3.up) * missile.transform.right;
            float elevation = Mathf.Clamp(Mathf.Atan2(-aimDirection.y, flat.magnitude) * Mathf.Rad2Deg, 10f, 90f);
            pitch.localRotation = Quaternion.AngleAxis(elevation, pitch.parent.InverseTransformDirection(rightAxis)) * pitchRest;
            muzzle.rotation = Quaternion.LookRotation(aimDirection); // FBX anchor basis is not the gun's physical forward.
        }

        private void OnDestroy() => Active.Remove(this);
    }
}

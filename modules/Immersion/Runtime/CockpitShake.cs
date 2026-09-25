using BoscaliSummer.Features.Immersion.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Immersion.Runtime
{
    /// <summary>
    /// Extra shake sources fed into the game's own cockpit shake (<c>CameraStateManager.ShakeCamera</c>,
    /// cockpit view only), so they share its Perlin offsets, decay and rattle sound. The game already
    /// shakes for AoA buffet, transonic buffet, damage, explosions and sonic booms; this adds the
    /// pilot's own guns, touchdowns and the runway roll.
    /// </summary>
    internal sealed class CockpitShake
    {
        private Aircraft subscribed;
        private float lastVerticalSpeed;
        private float strength = 1f;
        private bool active;

        public int Shots { get; private set; }
        public int Touchdowns { get; private set; }

        /// <summary>Update: follow the camera's aircraft and add the runway rumble.</summary>
        public void Tick(CameraStateManager cameras, Aircraft aircraft, bool on, float shakeStrength, float dt)
        {
            active = on;
            strength = shakeStrength;
            if (aircraft != subscribed) Follow(aircraft);
            if (!on || aircraft == null || aircraft.rb == null) return;

            lastVerticalSpeed = aircraft.rb.velocity.y;
            if (aircraft.radarAlt > 0.3f) return;
            (float low, float high) = ImmersionMath.GroundRumble(aircraft.speed, strength);
            // Vanilla decays low at 5/s and high at 4/s; feeding level*rate*dt settles on that level.
            if (low > 0f || high > 0f) cameras.ShakeCamera(low * 5f * dt, high * 4f * dt);
        }

        /// <summary>Called for every round a gun spawns (Harmony postfix on <c>Gun.SpawnBullet</c>).</summary>
        public void OnShot(Gun gun, CameraStateManager cameras)
        {
            if (!active || gun == null || gun.info == null || cameras == null) return;
            if (!ReferenceEquals(gun.attachedUnit, cameras.followingUnit)) return;
            (float low, float high) = ImmersionMath.ShotShake(gun.info.massPerRound * gun.info.muzzleVelocity, strength);
            cameras.ShakeCamera(low, high);
            Shots++;
        }

        private void Follow(Aircraft aircraft)
        {
            if (subscribed != null) subscribed.OnTouchdown -= OnTouchdown;
            subscribed = aircraft;
            if (subscribed != null) subscribed.OnTouchdown += OnTouchdown;
        }

        private void OnTouchdown()
        {
            if (!active) return;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null) return;
            cameras.ShakeCamera(ImmersionMath.TouchdownShake(Mathf.Min(0f, lastVerticalSpeed), strength), 0.2f * strength);
            Touchdowns++;
        }

        public void Release()
        {
            Follow(null);
        }
    }
}

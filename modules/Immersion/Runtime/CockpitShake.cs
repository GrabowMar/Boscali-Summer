using BoscaliSummer.Modules.Immersion.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Runtime
{
    /// <summary>
    /// Cockpit camera vibrations fed into CameraStateManager.ShakeCamera:
    /// - Pilot's own gunfire recoil
    /// - Touchdown thump proportional to sink rate
    /// - High-speed runway roll rumble
    /// - Transonic aerodynamic Mach buffeting & sound barrier crossing jolt
    /// </summary>
    internal sealed class CockpitShake
    {
        private Aircraft subscribed;
        private float lastVerticalSpeed;
        private float lastMach = -1f;
        private float strength = 1f;
        private bool active;

        public int Shots { get; private set; }
        public int Touchdowns { get; private set; }

        public void Tick(CameraStateManager cameras, Aircraft aircraft, bool on, bool machBuffetOn, float shakeStrength, float dt)
        {
            active = on;
            strength = shakeStrength;
            if (aircraft != subscribed) Follow(aircraft);
            if (!on || aircraft == null || aircraft.rb == null) return;

            lastVerticalSpeed = aircraft.rb.velocity.y;

            // Runway roll rumble
            if (aircraft.radarAlt <= 0.3f)
            {
                (float low, float high) = ImmersionMath.GroundRumble(aircraft.speed, strength);
                if (low > 0f || high > 0f)
                {
                    cameras.ShakeCamera(low * 5f * dt, high * 4f * dt);
                }
            }

            // Transonic aerodynamic buffeting
            if (machBuffetOn && aircraft.radarAlt > 5f)
            {
                float alt = aircraft.transform.position.y;
                float mach = ImmersionMath.MachNumber(aircraft.speed, alt);
                float buffet = ImmersionMath.MachBuffet(mach, strength);
                if (buffet > 0f)
                {
                    cameras.ShakeCamera(0f, buffet * 4f * dt);
                }

                // Sound barrier transition bump
                if (lastMach > 0f)
                {
                    if ((lastMach < 1.0f && mach >= 1.0f) || (lastMach > 1.0f && mach <= 1.0f))
                    {
                        cameras.ShakeCamera(0.06f * strength, 0.12f * strength);
                    }
                }
                lastMach = mach;
            }
            else
            {
                lastMach = -1f;
            }
        }

        public void OnShot(Gun gun, CameraStateManager cameras)
        {
            if (!active || gun == null || gun.info == null || cameras == null) return;
            (float low, float high) = ImmersionMath.ShotShake(gun.info.massPerRound * gun.info.muzzleVelocity, strength);
            cameras.ShakeCamera(low, high);
            Shots++;
        }

        private void Follow(Aircraft aircraft)
        {
            if (subscribed != null) subscribed.OnTouchdown -= OnTouchdown;
            subscribed = aircraft;
            if (subscribed != null) subscribed.OnTouchdown += OnTouchdown;
            lastMach = -1f;
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
            lastMach = -1f;
        }
    }
}

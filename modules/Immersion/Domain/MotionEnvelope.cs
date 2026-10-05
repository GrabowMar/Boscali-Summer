using System;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    // Saturating event envelopes stay separate from native CameraStateManager shake.
    internal sealed class MotionEnvelope
    {
        private bool machPrimed;
        private bool machArmed;
        private int outsideSide;
        private float lastCrossing = -100f;
        private bool gearPrimed, gearMoving;
        private int gearDirection;
        public float Recoil { get; private set; }
        public float Landing { get; private set; }
        public float Sonic { get; private set; }
        public float GearLock { get; private set; }

        public void AddGun(float momentum, float strength)
        {
            if (strength <= 0f || momentum <= 0f) return;
            Recoil = Math.Min(1f, Recoil + Math.Min(0.25f, momentum * 0.00035f) * strength);
        }

        public void AddLanding(float sinkSpeed, float strength)
        {
            if (strength <= 0f) return;
            Landing = Math.Min(1f, Landing + Math.Min(1f, 0.15f + Math.Abs(sinkSpeed) * 0.1f) * strength);
        }

        public void Step(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            Recoil *= (float)Math.Exp(-8f * dt);
            Landing *= (float)Math.Exp(-4.5f * dt);
            Sonic *= (float)Math.Exp(-7f * dt);
            GearLock *= (float)Math.Exp(-12f * dt);
        }

        // Native gear sounds already supply the latch; this is only a small rotational impulse.
        public bool GearTransition(int direction, bool moving, bool enabled, float dt)
        {
            if (!enabled || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt) ||
                (direction != 1 && direction != -1))
            {
                ResetGear();
                return false;
            }
            bool locked = gearPrimed && gearMoving && !moving && direction == gearDirection;
            gearPrimed = true;
            gearMoving = moving;
            gearDirection = direction;
            if (locked) GearLock = -direction * 0.04f;
            return locked;
        }

        public bool SonicCrossing(float mach, float time)
        {
            if (float.IsNaN(mach) || float.IsInfinity(mach)) return false;
            int side = mach < 0.98f ? -1 : mach > 1.02f ? 1 : 0;
            if (!machPrimed)
            {
                machPrimed = true;
                outsideSide = side;
                machArmed = side != 0;
                return false;
            }
            bool crossed = outsideSide < 0 ? mach >= 1f : outsideSide > 0 && mach <= 1f;
            if (machArmed && crossed && time - lastCrossing >= 2f)
            {
                lastCrossing = time;
                machArmed = false;
                outsideSide = side;
                Sonic = 0.45f;
                return true;
            }
            if (!machArmed && side != 0 && time - lastCrossing >= 2f)
            {
                outsideSide = side;
                machArmed = true;
            }
            if (outsideSide == 0 && side != 0) { outsideSide = side; machArmed = true; }
            return false;
        }

        public void ResetMach() { machPrimed = machArmed = false; outsideSide = 0; lastCrossing = -100f; Sonic = 0f; }
        private void ResetGear() { gearPrimed = gearMoving = false; gearDirection = 0; GearLock = 0f; }
        public void ClearExtra() { Recoil = Landing = 0f; ResetGear(); }
        public void Reset() { ClearExtra(); ResetMach(); }
    }
}

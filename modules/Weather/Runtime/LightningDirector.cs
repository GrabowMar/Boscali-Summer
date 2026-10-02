using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    // Storm lightning: schedules bolts in heavy rain, pulses the vanilla sun with a
    // base-tracked boost (restored when the flash ends, like RainAtmosphere), and fires
    // no audio is produced.
    internal sealed class LightningDirector : IClientEffect
    {
        private const float FlashGain = 4f;
        private const float FlashSeconds = 0.8f;
        private float countdown = -1f;
        private float flashT = -1f;
        private float sunBase = -1f;
        private Light lastSun;

        public string EffectId => "lightning";

        public FxBudget Budget => new FxBudget(0, 0, 0, false);

        public void ReleaseFx()
        {
            Reset();
        }

        public void DescribeFx(System.Collections.Generic.IDictionary<string, object> state)
        {
            state["fx.lightning.strikes"] = Strikes;
        }

        internal float FlashNow { get; private set; }
        internal int Strikes { get; private set; }

        internal void Tick(float dt, float rain, LevelInfo level)
        {
            if (rain < LightningMath.MinRain)
            {
                countdown = -1f;
            }
            else
            {
                if (countdown < 0f) countdown = LightningMath.NextDelay(Random.value, rain);
                countdown -= Mathf.Max(0f, dt);
                if (countdown <= 0f)
                {
                    Strike();
                    countdown = LightningMath.NextDelay(Random.value, rain);
                }
            }

            float flash = 0f;
            if (flashT >= 0f)
            {
                flashT += Mathf.Max(0f, dt);
                if (flashT > FlashSeconds) flashT = -1f;
                else flash = LightningMath.FlashEnvelope(flashT);
            }
            FlashNow = flash;
            PulseSun(level, flash);

        }

        internal void Reset()
        {
            countdown = -1f;
            flashT = -1f;
            FlashNow = 0f;
            if (lastSun != null && sunBase >= 0f && lastSun.intensity >= sunBase - 0.001f &&
                lastSun.intensity <= sunBase * (1f + FlashGain) + 0.001f)
                lastSun.intensity = sunBase;
            sunBase = -1f;
            lastSun = null;
        }

        private void Strike()
        {
            flashT = 0f;
            Strikes++;
        }

        private void PulseSun(LevelInfo level, float flash)
        {
            Light sun = level != null ? level.sun : null;
            if (sun == null) { sunBase = -1f; lastSun = null; return; }
            lastSun = sun;
            if (flash <= 0.001f)
            {
                // Relaxed: vanilla owns the light (~1 Hz rewrites); restore the exact
                // base once after a flash, otherwise re-adopt whatever is there.
                if (sunBase >= 0f && RainSkyMath.IsOwnWrite(sunBase, sun.intensity))
                    sun.intensity = sunBase;
                else
                    sunBase = sun.intensity;
                return;
            }
            if (sunBase < 0f) sunBase = sun.intensity;
            float want = sunBase * (1f + flash * FlashGain);
            if (!RainSkyMath.IsOwnWrite(want, sun.intensity)) sun.intensity = want;
        }
    }
}

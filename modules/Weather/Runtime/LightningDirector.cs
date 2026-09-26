using System.Collections.Generic;
using BoscaliSummer.Features.Weather.Audio;
using BoscaliSummer.Features.Weather.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    // Storm lightning: schedules bolts in heavy rain, pulses the vanilla sun with a
    // base-tracked boost (restored when the flash ends, like RainAtmosphere), and fires
    // delayed thunder through the rain audio. At most MaxPending rumbles queued.
    internal sealed class LightningDirector
    {
        private const float MinStrikeM = 200f, MaxStrikeM = 1500f;
        private const float FlashGain = 4f;
        private const float FlashSeconds = 0.8f;
        private const int MaxPending = 4;

        private struct Rumble
        {
            public float At;
            public float Gain;
        }

        private readonly List<Rumble> pending = new List<Rumble>(MaxPending);
        private float countdown = -1f;
        private float flashT = -1f;
        private float sunBase = -1f;
        private Light lastSun;

        internal float FlashNow { get; private set; }
        internal int Strikes { get; private set; }

        internal void Tick(float dt, float rain, LevelInfo level,
            ProceduralRainAudio audio, float master)
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
                    Strike(audio, master);
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

            if (audio != null)
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    if (Time.time >= pending[i].At)
                    {
                        audio.PlayThunder(pending[i].Gain);
                        pending.RemoveAt(i);
                    }
                }
            }
        }

        internal void Reset()
        {
            pending.Clear();
            countdown = -1f;
            flashT = -1f;
            FlashNow = 0f;
            if (lastSun != null && sunBase >= 0f && lastSun.intensity >= sunBase - 0.001f &&
                lastSun.intensity <= sunBase * (1f + FlashGain) + 0.001f)
                lastSun.intensity = sunBase;
            sunBase = -1f;
            lastSun = null;
        }

        private void Strike(ProceduralRainAudio audio, float master)
        {
            flashT = 0f;
            Strikes++;
            float distance = Random.Range(MinStrikeM, MaxStrikeM);
            if (audio != null && pending.Count < MaxPending)
            {
                pending.Add(new Rumble
                {
                    At = Time.time + LightningMath.ThunderDelay(distance),
                    Gain = LightningMath.ThunderGain(distance, master)
                });
            }
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

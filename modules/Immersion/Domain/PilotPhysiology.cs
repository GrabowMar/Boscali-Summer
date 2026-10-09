using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    [Flags]
    internal enum PilotCue
    {
        None = 0, Inhale = 1, Exhale = 2, Grunt = 4, LostConsciousness = 8, RegainedConsciousness = 16,
        GSuitInflate = 32, GSuitVent = 64, Heartbeat = 128
    }

    /// <summary>One frame of the pilot's body: vanilla GLOC fields when available, plus threat/hit pulses.</summary>
    internal struct PilotVitalsSample
    {
        public float G;
        public bool HasVanilla;
        public float BloodPressure, Stamina;
        public bool Conscious;
        public int Threats, Hits;
    }

    // Presentation pacing, not medicine. Vanilla GLOC owns vision, controls and blackout;
    // this reads it and paces breath, strain, suit and heart cues on one clock.
    internal sealed class PilotPhysiology
    {
        public const float RestRate = 12f, MaxRate = 30f, InhaleShare = .4f;
        public const float AgsmHold = 2.6f, AgsmKeh = .35f, AgsmInhale = .45f;
        private const float PumpRate = .28f, DrainPerG = .04f, StaminaRecovery = .18f;

        public float BloodPressure { get; private set; } = 1f;
        public float Stamina { get; private set; } = 1f;
        public bool Conscious { get; private set; } = true;
        public float LoadG { get; private set; } = 1f;
        public float Stress01 { get; private set; }
        public float Exertion01 { get; private set; }
        public bool Straining { get; private set; }
        public float ChestLift01 { get; private set; }
        public float SegmentSeconds { get; private set; } = 2f;
        public float Greyout01 => Scalar.Clamp01((.6f - BloodPressure) / .4f);
        public bool Ragged => Straining && Stamina < .25f;
        public float BreathsPerMinute => RestRate + (MaxRate - RestRate) * Exertion01;
        public float HeartRate => 110f + 50f * Greyout01;

        private float phase, agsm, recovery, heart, blackout;
        private bool suit;

        public PilotCue Step(in PilotVitalsSample s, float dt)
        {
            if (!(dt > 0f) || dt > .25f || !Scalar.IsFinite(s.G)) return PilotCue.None;
            dt = Math.Min(dt, .05f);
            float g = Math.Abs(s.G);
            LoadG += (g - LoadG) * Scalar.Clamp01(dt / .5f);
            PilotCue cues = PilotCue.None;
            bool was = Conscious;
            if (s.HasVanilla && Scalar.IsFinite(s.BloodPressure) && Scalar.IsFinite(s.Stamina))
            {
                BloodPressure = Scalar.Clamp01(s.BloodPressure);
                Stamina = Scalar.Clamp01(s.Stamina);
                Conscious = s.Conscious;
            }
            else SimulateVanilla(g, dt);
            if (was && !Conscious) { cues |= PilotCue.LostConsciousness; agsm = 0f; Straining = false; }
            if (!was && Conscious) { cues |= PilotCue.RegainedConsciousness; recovery = 1f; phase = 0f; }

            Stress01 *= (float)Math.Exp(-dt / 10f);
            if (s.Threats > 0) Stress01 = Math.Max(Stress01, .6f);
            if (s.Hits > 0) Stress01 = Math.Min(1f, Stress01 + .35f * s.Hits);
            recovery *= (float)Math.Exp(-dt / 6f);
            float load = Scalar.Clamp01((LoadG - 1.5f) / 4.5f);
            Exertion01 = Math.Max(Math.Max(load, 1f - Stamina), Math.Max(Stress01, recovery));

            if (!suit && LoadG > 2.5f) { suit = true; cues |= PilotCue.GSuitInflate; }
            else if (suit && LoadG < 1.5f) { suit = false; cues |= PilotCue.GSuitVent; }

            if (Greyout01 > 0f)
            {
                heart += dt * HeartRate / 60f;
                if (heart >= 1f) { heart -= 1f; cues |= PilotCue.Heartbeat; }
            }
            else heart = 0f;

            if (!Conscious)
            {
                ChestLift01 = Math.Max(0f, ChestLift01 - dt);
                return cues;
            }
            bool straining = LoadG > 5f && Stamina > .02f;
            if (straining && !Straining)
            {
                agsm = AgsmHold + AgsmKeh;
                cues |= PilotCue.Inhale;
                SegmentSeconds = AgsmInhale;
            }
            else if (!straining && Straining) phase = InhaleShare;
            Straining = straining;
            cues |= Straining ? StepAgsm(dt) : StepNormal(dt);
            return cues;
        }

        private PilotCue StepNormal(float dt)
        {
            float period = 60f / BreathsPerMinute;
            float before = phase;
            phase += dt / period;
            PilotCue cue = PilotCue.None;
            if (phase >= 1f) { phase -= 1f; cue = PilotCue.Inhale; SegmentSeconds = period * InhaleShare; }
            else if (before < InhaleShare && phase >= InhaleShare) { cue = PilotCue.Exhale; SegmentSeconds = period * (1f - InhaleShare); }
            float target = phase < InhaleShare
                ? Scalar.SmoothStep(0f, 1f, phase / InhaleShare)
                : 1f - Scalar.SmoothStep(0f, 1f, (phase - InhaleShare) / (1f - InhaleShare));
            ChestLift01 = Approach(ChestLift01, target, dt * 4f);
            return cue;
        }

        private PilotCue StepAgsm(float dt)
        {
            float before = agsm, cycle = AgsmHold + AgsmKeh + AgsmInhale;
            agsm += dt;
            PilotCue cue = PilotCue.None;
            if (before < AgsmHold && agsm >= AgsmHold) { cue = PilotCue.Grunt; SegmentSeconds = AgsmKeh; }
            else if (before < AgsmHold + AgsmKeh && agsm >= AgsmHold + AgsmKeh) { cue = PilotCue.Inhale; SegmentSeconds = AgsmInhale; }
            if (agsm >= cycle) agsm -= cycle;
            float target = agsm < AgsmHold ? 1f
                : agsm < AgsmHold + AgsmKeh ? 1f - Scalar.SmoothStep(0f, 1f, (agsm - AgsmHold) / AgsmKeh)
                : Scalar.SmoothStep(0f, 1f, (agsm - AgsmHold - AgsmKeh) / AgsmInhale);
            ChestLift01 = Approach(ChestLift01, target, dt * 6f);
            return cue;
        }

        // Port of vanilla GLOC.SimulateGLOC for pilots without a GLOC component.
        private void SimulateVanilla(float g, float dt)
        {
            if (!Conscious)
            {
                blackout -= dt;
                if (blackout <= 0f) { BloodPressure = .2f; Conscious = true; }
                return;
            }
            float pump = PumpRate - g * DrainPerG;
            if (BloodPressure < .55f) pump += Stamina * .25f;
            Stamina = Scalar.Clamp01(Stamina + (StaminaRecovery - g * DrainPerG) * dt);
            BloodPressure = Scalar.Clamp01(BloodPressure + pump * dt);
            if (BloodPressure < .2f) { Conscious = false; blackout = 4.5f; }
        }

        private static float Approach(float value, float target, float step) =>
            target > value ? Math.Min(target, value + step) : Math.Max(target, value - step);

        public void Reset()
        {
            BloodPressure = Stamina = LoadG = 1f;
            Conscious = true;
            Stress01 = Exertion01 = ChestLift01 = phase = agsm = recovery = heart = blackout = 0f;
            SegmentSeconds = 2f;
            Straining = suit = false;
        }
    }
}

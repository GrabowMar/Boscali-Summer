using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Immersion.Domain;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    /// <summary>
    /// The local aircraft's voice warnings (2026-10-27): PULL UP led by a GPWS whoop, TERRAIN, OVER G, and BINGO FUEL after
    /// the master caution chime, on the game's interface mixer like its own stall and gear voices, in every view. Rules
    /// live in <see cref="WarningMath"/>. Also the canopy closing on a ground start and the gear lever thunk (cockpit view).
    /// Per frame: a handful of field reads; sounds are event-rare.
    /// </summary>
    internal sealed class CockpitWarnings
    {
        private const float EpisodeGap = 3f, CanopyDelay = 1.2f;

        private readonly WarningMath rules = new WarningMath();
        private AudioSource tone, voice;
        private Aircraft bound;
        private LandingGear.GearState gear;
        private float lastPullUp = -10f, canopyAt = -1f;

        internal int Said { get; private set; }
        internal int Foley { get; private set; }
        internal bool Bound => bound != null;
        internal WarningInput Last { get; private set; }

        internal void Initialize(GameObject host)
        {
            tone = Make(host);
            voice = Make(host);
        }

        internal void Release()
        {
            if (tone != null) Object.Destroy(tone);
            if (voice != null) Object.Destroy(voice);
            tone = voice = null;
            bound = null;
        }

        internal void Tick(Aircraft aircraft, bool warnings, bool foley, bool cockpit)
        {
            if (tone == null || aircraft == null || aircraft.disabled || aircraft.rb == null)
            {
                bound = null;
                return;
            }
            float now = Time.time;
            if (aircraft != bound)
            {
                bound = aircraft;
                rules.Reset();
                gear = aircraft.gearState;
                canopyAt = aircraft.radarAlt < 3f && aircraft.speed < 5f ? now + CanopyDelay : -1f;
            }
            // The canopy closes once the player is in the cockpit after a ground start; forgotten once the jet rolls.
            if (canopyAt > 0f && aircraft.speed > 15f) canopyAt = -1f;
            if (canopyAt > 0f && now >= canopyAt && cockpit)
            {
                canopyAt = -1f;
                if (foley && Play(tone, "canopy-close", .7f)) Foley++;
            }
            if (aircraft.gearState != gear)
            {
                gear = aircraft.gearState;
                if (foley && cockpit && (gear == LandingGear.GearState.Extending || gear == LandingGear.GearState.Retracting))
                    if (Play(tone, "gear-lever", .8f)) Foley++;
            }
            if (!warnings) return;

            Pilot pilot = aircraft.pilots != null && aircraft.pilots.Length > 0 ? aircraft.pilots[0] : null;
            var input = new WarningInput
            {
                RadarAltM = aircraft.radarAlt,
                ClimbMps = aircraft.rb.velocity.y,
                G = pilot != null ? pilot.gForce : 1f,
                Fuel01 = aircraft.GetFuelLevel(),
                GearDown = aircraft.gearDeployed,
                Airborne = aircraft.radarAlt > 2f,
            };
            Last = input;
            switch (rules.Step(input, now))
            {
                case Warning.PullUp:
                    if (now - lastPullUp > EpisodeGap && Play(tone, "gpws-whoop", .8f)) Say("vo-pull-up", SfxClips.Get("gpws-whoop").length);
                    else Say("vo-pull-up", 0f);
                    lastPullUp = now;
                    break;
                case Warning.Terrain: Say("vo-terrain", 0f); break;
                case Warning.OverG: Say("vo-over-g", 0f); break;
                case Warning.Bingo:
                    Play(tone, "master-caution", .8f);
                    Say("vo-bingo", .35f);
                    break;
            }
        }

        private void Say(string name, float delay)
        {
            AudioClip clip = SfxClips.Get(name);
            if (clip == null || voice.isPlaying || !Route(voice)) return;
            voice.clip = clip;
            voice.PlayDelayed(delay);
            Said++;
        }

        private static bool Play(AudioSource source, string name, float volume)
        {
            AudioClip clip = SfxClips.Get(name);
            if (clip == null || !Route(source)) return false;
            source.PlayOneShot(clip, volume);
            return true;
        }

        private static bool Route(AudioSource source)
        {
            if (source.outputAudioMixerGroup != null) return true;
            AudioMixerGroup group = SoundManager.i != null ? SoundManager.i.InterfaceMixer : null;
            source.outputAudioMixerGroup = group;
            return group != null;
        }

        private static AudioSource Make(GameObject host)
        {
            AudioSource source = host.AddComponent<AudioSource>();
            source.loop = false;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.bypassEffects = true; // a warning is heard in the headset, never in the cockpit's reverb
            return source;
        }
    }
}

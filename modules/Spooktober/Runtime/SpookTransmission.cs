using System;
using System.Collections;
using BepInEx.Logging;
using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Spooktober.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>
    /// The mystery transmission. A few seconds after the local player first has an aircraft, static breaks in on the radio, a squelch
    /// closes it and one glitched line airs on the COMMS feed. Local to each peer (nothing is sent), once per game launch, and never
    /// again once the menu is unlocked. The words are kept as LAST INTERCEPT on PILOT (<see cref="SpookProgress.HeardIntercept"/>).
    /// </summary>
    internal sealed class SpookTransmission : MonoBehaviour, ISceneService
    {
        private const float DelaySeconds = 6f, StaticSeconds = 1.2f;

        public static SpookTransmission Instance { get; private set; }

        /// <summary>Survives scene loads on purpose: the transmission airs at most once per game launch, not once per mission.</summary>
        public static bool AiredThisLaunch { get; private set; }

#if !PUBLIC_RELEASE
        /// <summary>Sim seam (SpooktoberAutomation "relaunch"): as if the game were restarted, so the transmission may air again.</summary>
        internal static void ResetLaunchForSim() => AiredThisLaunch = false;
#endif

        private ManualLogSource log;
        private AudioSource source;
        private bool hadAircraft, airing, warnedNoRadio;
        private float airAt = -1f;

        public void Configure(ManualLogSource logger)
        {
            log = logger;
            Instance = this;
        }

        public void ResetForScene()
        {
            StopAllCoroutines();
            if (source != null) source.Stop();
            airing = false;
            hadAircraft = false;
            airAt = -1f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (source != null) Destroy(source);
        }

        private void Update()
        {
            if (AiredThisLaunch) return;
            // Same rising edge as ScenarioHost: the first frame the local pilot has a live aircraft.
            bool flying = GameManager.GetLocalAircraft(out Aircraft own) && own != null && !own.disabled;
            if (!flying) airAt = -1f;
            else if (!hadAircraft) airAt = Time.time + DelaySeconds;
            hadAircraft = flying;
            if (airAt < 0f || Time.time < airAt) return;
            airAt = -1f;
            if (!SpookStore.Current.Unlocked) Air();
        }

        /// <summary>
        /// Airs the transmission now (also the sim hook). It counts as heard and is kept for LAST INTERCEPT even when no radio can say
        /// it (no chatter module, radio switched off), so the words are never lost.
        /// </summary>
        public void Air()
        {
            if (airing || !isActiveAndEnabled) return;
            AiredThisLaunch = true;
            SpookStore.Current.HeardIntercept = true;
            SpookStore.Save();
            StartCoroutine(Broadcast(UnityEngine.Random.Range(1, int.MaxValue)));
        }

        private IEnumerator Broadcast(int seed)
        {
            airing = true;
            try
            {
                Play(GameAssets.i != null ? GameAssets.i.radioStatic : null, loop: true);
                yield return new WaitForSeconds(StaticSeconds);
                AudioClip tail = SfxClips.Get("radio-tail-0");
                if (tail != null) Play(tail, loop: false);
                else if (source != null) source.Stop(); // no tail clip: the looping static must not run on
                if (ModuleServices.TryGet(out IChatter chatter))
                    chatter.Say(chatter.Station(ChatterNet.Enemy, SpookText.Glitch("???", seed, 0.5f)), "spook:intercept",
                        SpookText.Glitch(SpookText.Transmission, seed, 0.12f), ChatterPriority.Emergency);
                else if (!warnedNoRadio)
                {
                    warnedNoRadio = true;
                    log?.LogInfo("[Spooktober] No radio to air the transmission on; it is kept as LAST INTERCEPT.");
                }
            }
            finally
            {
                airing = false;
            }
        }

        /// <summary>One clip on a 2D source through the game's interface mixer (so the volume sliders hold). Fails closed: no sound, never an exception.</summary>
        private void Play(AudioClip clip, bool loop)
        {
            if (clip == null || Application.isBatchMode) return;
            try
            {
                SoundManager sound = SoundManager.i;
                if (sound == null) return;
                if (source == null)
                {
                    source = gameObject.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.spatialBlend = 0f;
                    source.volume = 0.75f;
                }
                source.outputAudioMixerGroup = sound.InterfaceMixer;
                source.Stop();
                source.clip = clip;
                source.loop = loop;
                source.Play();
            }
            catch (Exception e)
            {
                log?.LogWarning("[Spooktober] Transmission audio failed: " + e.Message);
            }
        }
    }
}

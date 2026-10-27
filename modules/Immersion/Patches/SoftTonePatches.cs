using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using BoscaliSummer.Core.Fx;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Patches
{
    /// <summary>
    /// COCKPIT ALARMS (setting key SoftTonesEnabled): the game's own cues, played on the native sources so every volume
    /// slider still applies. Radar, missile and stall cues get recorded replacements (Core/Fx/Sfx radar-*, missile-*,
    /// stall-horn: calm radar, modern and urgent alarms); interface clicks, menu sounds and the RPM / VRS warnings play a softened copy of the vanilla
    /// clip, so each one keeps its meaning.
    /// </summary>
    internal static class SoftTones
    {
        private const float CutoffHz = 2600f;
        private const float Gain = 0.6f;
        private const float LongestSeconds = 5f;

        internal static ConfigEntry<bool> Master;
        internal static ConfigEntry<bool> Soft;
        private static readonly Dictionary<AudioClip, AudioClip> softened = new Dictionary<AudioClip, AudioClip>();

        internal static bool On => Soft != null && Soft.Value && Master != null && Master.Value;

        /// <summary>
        /// A rounder, quieter copy of a vanilla clip (two one-pole low-passes, -4.4 dB), made once per clip. The
        /// mod's own clips, long clips and clips whose samples cannot be read (compressed in memory) stay as they are.
        /// </summary>
        internal static AudioClip Soften(AudioClip clip)
        {
            if (clip == null) return clip;
            if (softened.TryGetValue(clip, out AudioClip soft) && soft != null) return soft;
            soft = clip;
            int channels = clip.channels, samples = clip.samples;
            if (!clip.name.StartsWith("BoscaliSfx.") && channels > 0 && samples > 0 && samples <= clip.frequency * LongestSeconds
                && clip.loadType == AudioClipLoadType.DecompressOnLoad)
            {
                var data = new float[samples * channels];
                if (clip.GetData(data, 0))
                {
                    float a = 1f - Mathf.Exp(-2f * Mathf.PI * CutoffHz / clip.frequency);
                    for (int c = 0; c < channels; c++)
                    {
                        float y1 = 0f, y2 = 0f;
                        for (int i = c; i < data.Length; i += channels)
                        {
                            y1 += a * (data[i] - y1);
                            y2 += a * (y1 - y2);
                            data[i] = y2 * Gain;
                        }
                    }
                    soft = AudioClip.Create(clip.name + ".soft", samples, channels, clip.frequency, false);
                    soft.SetData(data, 0);
                }
            }
            softened[clip] = soft;
            return soft;
        }
    }

    /// <summary>
    /// The native receiver beeps on every radar sweep (RadarWarning_OnRadarWarning ->
    /// SoundManager.PlayRadarWarningOneShot). Its two clips are captured when the HUD receiver starts and
    /// swapped for a soft bell (new radar) and a felt tick (repeat sweep). Emitters sweeping together play once.
    /// </summary>
    [HarmonyPatch(typeof(RadarWarning), "Start")]
    internal static class RadarToneCapturePatch
    {
        internal static AudioClip VanillaNew;
        internal static AudioClip VanillaExisting;

        private static void Postfix(AudioClip ___radarWarningNew, AudioClip ___radarWarningExisting)
        {
            VanillaNew = ___radarWarningNew;
            VanillaExisting = ___radarWarningExisting;
        }
    }

    [HarmonyPatch(typeof(SoundManager), nameof(SoundManager.PlayRadarWarningOneShot))]
    internal static class RadarToneSwapPatch
    {
        private const float NewGap = 0.8f;
        private const float SweepGap = 1.2f;

        private static float lastNew = -10f;
        private static float lastSweep = -10f;

        private static bool Prefix(ref AudioClip audioClip)
        {
            if (!SoftTones.On || audioClip == null) return true;
            float now = Time.unscaledTime;
            if (ReferenceEquals(audioClip, RadarToneCapturePatch.VanillaNew))
            {
                if (now - lastNew < NewGap) return false;
                lastNew = now;
                lastSweep = now; // the bell covers this sweep's tick
                audioClip = SfxClips.Get("radar-new") ?? audioClip;
            }
            else if (ReferenceEquals(audioClip, RadarToneCapturePatch.VanillaExisting))
            {
                if (now - lastSweep < SweepGap) return false;
                lastSweep = now;
                audioClip = SfxClips.Get("radar-sweep") ?? audioClip;
            }
            return true;
        }
    }

    /// <summary>
    /// The missile alarm (ThreatList.MissileAlarm, one looping source per seeker type) steps through alarmClips by
    /// seeker mode: 0 = active lock (most urgent), 1 = active search, 2 = passive. Setup runs once per aircraft and
    /// swaps this alarm's clips for modern electronic loops (two-tone warble for a lock, double beeps for search and
    /// passive, slowing with the index), and the launch alert for three rising beeps. The vanilla clips are kept per alarm, so turning the setting off restores them on the next aircraft.
    /// </summary>
    [HarmonyPatch]
    internal static class MissileAlarmSoftPatch
    {
        private sealed class Vanilla
        {
            public AudioClip[] Clips;
            public AudioClip Alert;
        }

        private static readonly ConditionalWeakTable<object, Vanilla> vanilla = new ConditionalWeakTable<object, Vanilla>();

        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.Inner(typeof(ThreatList), "MissileAlarm"), "Setup");

        private static void Prefix(object __instance)
        {
            Traverse alarm = Traverse.Create(__instance);
            if (!vanilla.TryGetValue(__instance, out Vanilla original))
            {
                original = new Vanilla { Clips = alarm.Field("alarmClips").GetValue<AudioClip[]>(), Alert = alarm.Field("alertClip").GetValue<AudioClip>() };
                vanilla.Add(__instance, original);
            }
            if (original.Clips == null || original.Clips.Length == 0) return;
            if (!SoftTones.On)
            {
                alarm.Field("alarmClips").SetValue(original.Clips);
                alarm.Field("alertClip").SetValue(original.Alert);
                return;
            }
            var clips = new AudioClip[original.Clips.Length];
            for (int i = 0; i < clips.Length; i++)
                clips[i] = SfxClips.Get(i == 0 ? "missile-alarm-0" : i == 1 ? "missile-alarm-1" : "missile-alarm-2") ?? original.Clips[i];
            alarm.Field("alarmClips").SetValue(clips);
            alarm.Field("alertClip").SetValue(SfxClips.Get("missile-alert") ?? original.Alert);
        }
    }

    /// <summary>Interface and menu one-shots (HUD clicks, weapon select, menu buttons) play a softened copy.</summary>
    [HarmonyPatch]
    internal static class InterfaceSoftPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SoundManager), nameof(SoundManager.PlayInterfaceOneShot));
            yield return AccessTools.Method(typeof(SoundManager), nameof(SoundManager.PlayInterface));
            yield return AccessTools.Method(typeof(SoundManager), nameof(SoundManager.PlayMenuOneShot));
            yield return AccessTools.Method(typeof(SoundManager), nameof(SoundManager.PlayMenu));
        }

        private static void Prefix(ref AudioClip audioClip)
        {
            if (SoftTones.On) audioClip = SoftTones.Soften(audioClip);
        }
    }

    /// <summary>The stall horn loop is the mod's modern warble (Core/Fx/Sfx stall-horn); vanilla's spoken "stall" still plays.</summary>
    [HarmonyPatch(typeof(AoADisplay), nameof(AoADisplay.Initialize))]
    internal static class StallHornPatch
    {
        private static void Postfix(AudioSource ___hornSource)
        {
            if (SoftTones.On && ___hornSource != null) ___hornSource.clip = SfxClips.Get("stall-horn") ?? ___hornSource.clip;
        }
    }

    /// <summary>The low-rotor-RPM warning loop (its pitch still follows the RPM) plays a softened copy.</summary>
    [HarmonyPatch(typeof(RPMGauge), nameof(RPMGauge.Initialize))]
    internal static class RpmWarningSoftPatch
    {
        private static void Postfix(AudioSource ___warningSource)
        {
            if (SoftTones.On && ___warningSource != null) ___warningSource.clip = SoftTones.Soften(___warningSource.clip);
        }
    }

    /// <summary>The vortex-ring-state warning plays a softened copy.</summary>
    [HarmonyPatch(typeof(VRSWarning), nameof(VRSWarning.Initialize))]
    internal static class VrsWarningSoftPatch
    {
        private static void Postfix(AudioSource ___warningSource)
        {
            if (SoftTones.On && ___warningSource != null) ___warningSource.clip = SoftTones.Soften(___warningSource.clip);
        }
    }
}

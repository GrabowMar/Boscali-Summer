using BoscaliSummer.Features.Radio.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.Radio.Patches
{
    [HarmonyPatch(typeof(MusicManager), nameof(MusicManager.PlayMusic))]
    internal static class VanillaPlayMusicPatch
    {
        private static bool Prefix(AudioClip audioClip, bool repeat) =>
            RadioManager.AllowVanillaMusic(audioClip, repeat, 0f, false);
    }

    [HarmonyPatch(typeof(MusicManager), nameof(MusicManager.CrossFadeMusic))]
    internal static class VanillaCrossFadeMusicPatch
    {
        private static bool Prefix(AudioClip audioClip, bool repeat, float priority) =>
            RadioManager.AllowVanillaMusic(audioClip, repeat, priority, true);
    }

    [HarmonyPatch(typeof(MusicManager), nameof(MusicManager.QueueMusicClip))]
    internal static class VanillaQueueMusicPatch
    {
        private static bool Prefix(AudioClip audioClip, float clipPriority) =>
            RadioManager.AllowVanillaMusic(audioClip, false, clipPriority, true);
    }
}

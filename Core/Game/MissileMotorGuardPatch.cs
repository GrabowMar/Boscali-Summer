using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// A missile whose motor lost a particle system, trail or audio source (destroyed with a damaged part) threw a
    /// NullReferenceException in <c>Missile.Motor.Burnout</c>, which aborted <c>RpcDetonate</c> half way: the missile's
    /// clean-up never ran ("NullReferenceException in SlowUpdate" from ARHSeeker.SlowChecks). Destroyed entries are
    /// dropped from the motor's arrays first; nothing is allocated unless one is gone.
    /// </summary>
    [HarmonyPatch]
    internal static class MissileMotorGuardPatch
    {
        private static readonly Type Motor = AccessTools.Inner(typeof(Missile), "Motor");
        private static readonly AccessTools.FieldRef<object, ParticleSystem[]> Particles = Ref<ParticleSystem[]>("particleSystems");
        private static readonly AccessTools.FieldRef<object, TrailEmitter[]> Trails = Ref<TrailEmitter[]>("trailEmitters");
        private static readonly AccessTools.FieldRef<object, AudioSource[]> Audio = Ref<AudioSource[]>("audioSources");

        private static bool Prepare() => Motor != null && Particles != null && Trails != null && Audio != null;

        private static MethodBase TargetMethod() => AccessTools.Method(Motor, "Burnout");

        private static void Prefix(object __instance)
        {
            Clean(ref Particles(__instance));
            Clean(ref Trails(__instance));
            Clean(ref Audio(__instance));
        }

        /// <summary>A part torn down inside the same detonation still throws from the native Stop after the prefix ran
        /// (seen in a soak): the burnout is cosmetic, so its NRE is dropped and the detonation carries on.</summary>
        private static System.Exception Finalizer(System.Exception __exception) =>
            __exception is NullReferenceException ? null : __exception;

        private static void Clean<T>(ref T[] items) where T : UnityEngine.Object
        {
            if (items == null) return;
            for (int i = 0; i < items.Length; i++)
                if (items[i] == null) { items = items.Where(x => x != null).ToArray(); return; }
        }

        private static AccessTools.FieldRef<object, T> Ref<T>(string name)
        {
            try { return Motor != null ? AccessTools.FieldRefAccess<T>(Motor, name) : null; }
            catch (Exception) { return null; }
        }
    }
}

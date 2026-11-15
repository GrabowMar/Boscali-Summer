using System;
using HarmonyLib;
using BoscaliSummer.Modules.Effects.Runtime;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace BoscaliSummer.Modules.Effects.Patches
{
    [HarmonyPatch(typeof(ColorGradingLutPass), nameof(ColorGradingLutPass.Execute))]
    internal static class AgxLutPatch
    {
        private static readonly AccessTools.StructFieldRef<RenderingData, CommandBuffer> Commands = CommandAccessor();

        internal struct State
        {
            internal Tonemapping Tone;
            internal TonemappingMode Mode;
            internal AgxTonemapping Owner;
            internal void Restore() { if (Tone != null) Tone.mode.value = Mode; }
        }

        [HarmonyPrefix]
        private static void Prefix(ref RenderingData renderingData, RTHandle ___m_InternalLut, out State __state)
        {
            __state = default;
            AgxTonemapping owner = AgxTonemapping.Live;
            if (Commands == null || owner == null || ___m_InternalLut?.rt == null ||
                Commands(ref renderingData) == null || !owner.CanApply(ref renderingData)) return;
            GraphicsFormat format = ___m_InternalLut.rt.graphicsFormat;
            if (format != GraphicsFormat.R16G16B16A16_SFloat && format != GraphicsFormat.B10G11R11_UFloatPack32) return;
            Tonemapping tone = VolumeManager.instance.stack?.GetComponent<Tonemapping>();
            if (tone == null) return;
            __state = new State { Tone = tone, Mode = tone.mode.value, Owner = owner };
            // Native grading, including G-LOC saturation, stays in linear HDR until AgX resolves it.
            tone.mode.value = TonemappingMode.None;
        }

        [HarmonyPostfix]
        private static void Postfix(ref RenderingData renderingData, RTHandle ___m_InternalLut, State __state)
        {
            __state.Restore();
            // URP submits this buffer after Execute returns: append after the native LUT draw.
            if (__state.Owner != null) __state.Owner.Compose(Commands(ref renderingData), ___m_InternalLut);
        }

        [HarmonyFinalizer]
        private static void Finalizer(Exception __exception, State __state)
        {
            if (__exception != null) __state.Restore();
        }

        private static AccessTools.StructFieldRef<RenderingData, CommandBuffer> CommandAccessor()
        {
            try { return AccessTools.StructFieldRefAccess<RenderingData, CommandBuffer>("commandBuffer"); }
            catch (Exception) { return null; }
        }
    }
}

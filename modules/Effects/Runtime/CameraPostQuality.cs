using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using BoscaliSummer.Modules.Effects.Domain;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace BoscaliSummer.Modules.Effects.Runtime
{
    /// <summary>Reversible stack-level dithering and SMAA, using only retained native post-process resources.</summary>
    internal sealed class CameraPostQuality : IDisposable
    {
        internal static CameraPostQuality Live { get; private set; }
        internal UniversalAdditionalCameraData CameraData => camera;
        private static readonly PropertyInfo PostPass = AccessTools.Property(typeof(UniversalRenderer), "postProcessPass");
        private static readonly FieldInfo PostData = PostPass != null ? AccessTools.Field(PostPass.PropertyType, "m_Data") : null;
        private UniversalAdditionalCameraData camera;
        private Camera postOwner;
        private bool ownsDithering, savedDithering, ownsSmaa, warnedDithering, warnedSmaa;
        private AntialiasingMode savedAntialiasing;
        private AntialiasingQuality savedQuality;

        internal void Apply(Camera main, bool dithering, EdgeSmoothing smoothing, ManualLogSource logger)
        {
            if (main == null || !main.isActiveAndEnabled ||
                !main.TryGetComponent(out UniversalAdditionalCameraData data) || data.renderType != CameraRenderType.Base ||
                (!dithering && smoothing != EdgeSmoothing.Smaa))
            {
                Restore();
                return;
            }
            Camera owner = AgxTonemapping.PostCamera(main);
            if (owner == null || !owner.isActiveAndEnabled ||
                !owner.TryGetComponent(out UniversalAdditionalCameraData post) || !post.renderPostProcessing)
            {
                Restore();
                return;
            }
            PostProcessData resources = Resources(post);
            bool enableDithering = dithering && HasDithering(resources);
            bool enableSmaa = smoothing == EdgeSmoothing.Smaa && HasSmaa(resources);
            if (dithering && !enableDithering && !warnedDithering)
            {
                logger?.LogWarning("[Effects] Dithering unavailable: native post-process shaders or blue-noise textures are missing.");
                warnedDithering = true;
            }
            if (smoothing == EdgeSmoothing.Smaa && !enableSmaa && !warnedSmaa)
            {
                logger?.LogWarning("[Effects] SMAA unavailable: the native shader or lookup textures are missing.");
                warnedSmaa = true;
            }
            if (enableDithering) warnedDithering = false;
            if (enableSmaa) warnedSmaa = false;
            if (!enableDithering && !enableSmaa)
            {
                Restore();
                return;
            }
            if (camera != data) Restore();
            camera = data;
            postOwner = owner;
            Live = this;

            // URP14 copies these fields from the base camera into every overlay's CameraData.
            // Changing the final post-processing overlay's component would have no effect.
            if (enableDithering)
            {
                if (!ownsDithering) { savedDithering = data.dithering; ownsDithering = true; }
                data.dithering = true;
            }
            else RestoreDithering();
            if (enableSmaa)
            {
                if (!ownsSmaa)
                {
                    savedAntialiasing = data.antialiasing;
                    savedQuality = data.antialiasingQuality;
                    ownsSmaa = true;
                }
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
            else RestoreSmaa();
        }

        internal void Restore()
        {
            RestoreDithering();
            RestoreSmaa();
            camera = null;
            postOwner = null;
            if (Live == this) Live = null;
        }

        public void Dispose() => Restore();

        internal void Describe(IDictionary<string, object> state)
        {
            state["dithering"] = ownsDithering && camera != null && camera.dithering;
            state["edgeSmoothing"] = ownsSmaa ? "SMAA" : "Game";
            state["postQualityOwner"] = postOwner != null ? postOwner.name : "none";
        }

        private void RestoreDithering()
        {
            if (ownsDithering && camera != null) camera.dithering = savedDithering;
            ownsDithering = false;
        }

        private void RestoreSmaa()
        {
            if (ownsSmaa && camera != null)
            {
                camera.antialiasing = savedAntialiasing;
                camera.antialiasingQuality = savedQuality;
            }
            ownsSmaa = false;
        }

        private static PostProcessData Resources(UniversalAdditionalCameraData post)
        {
            try
            {
                if (PostPass == null || PostData == null || !(post.scriptableRenderer is UniversalRenderer renderer)) return null;
                object pass = PostPass.GetValue(renderer);
                return pass != null ? PostData.GetValue(pass) as PostProcessData : null;
            }
            catch (Exception) { return null; }
        }

        private static bool Supported(Shader shader) => shader != null && shader.isSupported;

        private static bool HasDithering(PostProcessData resources)
        {
            if (resources?.shaders == null || resources.textures == null ||
                !Supported(resources.shaders.uberPostPS) || !Supported(resources.shaders.finalPostPassPS)) return false;
            Texture2D[] noise = resources.textures.blueNoise16LTex;
            if (noise == null || noise.Length == 0) return false;
            foreach (Texture2D texture in noise) if (texture == null) return false;
            return true;
        }

        private static bool HasSmaa(PostProcessData resources) => resources?.shaders != null && resources.textures != null &&
            Supported(resources.shaders.subpixelMorphologicalAntialiasingPS) &&
            resources.shaders.subpixelMorphologicalAntialiasingPS.passCount >= 3 &&
            resources.textures.smaaAreaTex != null && resources.textures.smaaSearchTex != null;
    }
}

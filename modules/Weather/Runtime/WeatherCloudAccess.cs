using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// The module's one reflection seam into the vanilla sky. It resolves the vanilla
    /// <c>CloudLayer</c>, hands the supercell renderer the deck's emitter and material to borrow,
    /// and owns the optional hijack that takes the local cloud deck away so the cell renderer can
    /// own the sky.
    ///
    /// <para>The material instance belongs to the game — a storm tints itself through its own
    /// <c>main.startColor</c> and never writes to it. The hijack disables <c>Renderer.enabled</c>
    /// only: the <c>CloudLayer</c> component keeps running, so sun and moon cookies, cloud
    /// occlusion, fog and the distant horizon band are all still driven by vanilla.</para>
    ///
    /// <para>Binding is permanent, but resolving the live instance is retried for a few ticks
    /// after a scene load because <c>CloudLayer</c> assigns its fields in <c>Start</c>, which can
    /// trail the first renderer tick. Once the retry budget is spent the failure latches until
    /// the next scene, so nothing here probes per frame or spams the log.</para>
    /// </summary>
    internal static class WeatherCloudAccess
    {
        private const int MaxAttempts = 8;

        private static AccessTools.FieldRef<CloudLayer, ParticleSystem> cloudSystemRef;
        private static AccessTools.FieldRef<CloudLayer, Material> cloudMaterialRef;
        private static AccessTools.FieldRef<CloudLayer, MeshRenderer> cloudRendererRef;
        private static bool bound;
        private static bool warned;
        private static int attempts;

        private static bool hijacked;

        public static bool Available { get; private set; }

        /// <summary>The vanilla deck emitter; read only, used to borrow its look.</summary>
        public static ParticleSystem Template { get; private set; }

        /// <summary>The deck's live material instance. Borrow it; never mutate it.</summary>
        public static Material PuffMaterial { get; private set; }

        /// <summary>True while the local vanilla cloud deck is hidden for the cell renderer.</summary>
        public static bool Hijacked => hijacked;

        public static bool TryResolve(ManualLogSource log)
        {
            if (Available) return true;

            if (!bound)
            {
                try
                {
                    cloudSystemRef = FieldRef<CloudLayer, ParticleSystem>("cloudSystem");
                    cloudMaterialRef = FieldRef<CloudLayer, Material>("cloudMaterial");
                    cloudRendererRef = FieldRef<CloudLayer, MeshRenderer>("cloudRenderer");
                    bound = true;
                }
                catch (Exception e)
                {
                    attempts = MaxAttempts;
                    Warn(log, "CloudLayer fields unavailable: " + e.Message);
                    return false;
                }
            }

            if (attempts >= MaxAttempts) return false;
            attempts++;

            CloudLayer layer = UnityEngine.Object.FindObjectOfType<CloudLayer>(true);
            if (layer == null)
            {
                Warn(log, "no CloudLayer in the scene");
                return false;
            }

            ParticleSystem system = cloudSystemRef(layer);
            Material material = cloudMaterialRef(layer);
            if (system == null || material == null)
            {
                Warn(log, "CloudLayer has not finished initialising");
                return false;
            }

            Template = system;
            PuffMaterial = material;
            Available = true;
            return true;
        }

        /// <summary>
        /// Take the local cloud deck away or give it back. Only the two renderers that draw the
        /// near sky are switched off — the mask plane (<c>cloudRenderer</c>) and the near puff
        /// emitter (<c>cloudSystem</c>). The distant horizon band and the fly-through wisps stay,
        /// and the <c>CloudLayer</c> component itself keeps updating, so vanilla still drives the
        /// sun and moon cookies, the cloud occlusion and the fog while the cells own the sky.
        ///
        /// <para>Idempotent, and safe to call when the layer has not resolved yet: it simply
        /// records the intent and the next resolve applies it.</para>
        /// </summary>
        public static bool SetLocalDeckHidden(bool hidden, ManualLogSource log)
        {
            if (hidden && !Available)
            {
                // Nothing to hide yet. Remember it so a later resolve applies the hijack.
                hijacked = true;
                return false;
            }

            if (!Available)
            {
                hijacked = false;
                return false;
            }

            CloudLayer layer = UnityEngine.Object.FindObjectOfType<CloudLayer>(true);
            if (layer == null)
            {
                hijacked = hidden;
                return false;
            }

            if (cloudRendererRef != null)
            {
                MeshRenderer plane = cloudRendererRef(layer);
                if (plane != null) plane.enabled = !hidden;
            }

            ParticleSystem system = cloudSystemRef != null ? cloudSystemRef(layer) : null;
            if (system != null)
            {
                ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer != null) renderer.enabled = !hidden;
            }

            if (hidden != hijacked)
            {
                hijacked = hidden;
                log?.LogInfo(hidden
                    ? "Weather: the vanilla cloud deck is hidden; storm cells own the local sky."
                    : "Weather: the vanilla cloud deck is restored.");
            }

            return true;
        }

        /// <summary>
        /// A new scene means a new CloudLayer and a new material instance, and the deck it brings
        /// is visible again. The hijack intent survives; the caller re-applies it after resolving.
        /// </summary>
        public static void Invalidate()
        {
            Available = false;
            Template = null;
            PuffMaterial = null;
            attempts = 0;
        }

        /// <summary>Forget the hijack entirely, e.g. when the feature or the setting is off.</summary>
        public static void Release()
        {
            SetLocalDeckHidden(false, null);
            hijacked = false;
        }

        private static void Warn(ManualLogSource log, string reason)
        {
            if (warned || attempts < MaxAttempts) return;
            warned = true;
            log?.LogWarning("Weather supercell clouds unavailable: " + reason + ".");
        }

        private static AccessTools.FieldRef<TInstance, TField> FieldRef<TInstance, TField>(string name)
        {
            FieldInfo field = AccessTools.Field(typeof(TInstance), name) ??
                throw new MissingFieldException(typeof(TInstance).FullName, name);
            return AccessTools.FieldRefAccess<TInstance, TField>(field);
        }
    }
}

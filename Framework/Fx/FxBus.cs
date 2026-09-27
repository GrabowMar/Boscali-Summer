using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using UnityEngine;

namespace BoscaliSummer.Framework.Fx
{
    /// <summary>
    /// Shared client-effect services inside the one BepInEx plugin. Registration is explicit:
    /// owning modules create and tick effects, while this bus validates identity, bounds
    /// diagnostics, and releases registered effects when the plugin stops.
    /// </summary>
    internal static class FxBus
    {
        private const int MaxEffects = 24;
        private static readonly FxRegistry effects = new FxRegistry(MaxEffects);
        private static readonly Dictionary<string, float> lastMs =
            new Dictionary<string, float>(MaxEffects, StringComparer.Ordinal);
        private static int refusedRegistrations;
        private static FxQuality? adaptiveCap;
        private static int qualityLevelCount;

        /// <summary>Zero-alloc millisecond scope: using (FxBus.Time("canopy")) { ... }</summary>
        internal readonly struct EffectTime : IDisposable
        {
            private readonly string id;
            private readonly long start;

            internal EffectTime(string effectId)
            {
                id = effectId;
                start = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (id == null) return;
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 /
                    System.Diagnostics.Stopwatch.Frequency;
                Record(id, (float)ms);
            }
        }

        /// <summary>False on invalid id, duplicate owner or a full registry.</summary>
        public static bool Register(IClientEffect effect)
        {
            if (effects.TryRegister(effect)) return true;
            if (refusedRegistrations < int.MaxValue) refusedRegistrations++;
            return false;
        }

        public static void Unregister(IClientEffect effect)
        {
            if (!effects.Unregister(effect)) return;
            lastMs.Remove(effect.EffectId);
        }

        public static EffectTime Time(string effectId) => new EffectTime(effectId);

        /// <summary>Vanilla quality tier, read live (cheap property, no caching to stale).</summary>
        public static FxQuality Quality
        {
            get
            {
                try
                {
                    int count = qualityLevelCount;
                    if (count < 2)
                    {
                        string[] names = QualitySettings.names;
                        count = names != null ? names.Length : 0;
                        if (count > 1) qualityLevelCount = count;
                    }
                    return FxQualityMapper.Map(QualitySettings.GetQualityLevel(), count);
                }
                catch (Exception)
                {
                    return FxQuality.High;
                }
            }
        }

        public static FxQuality EffectiveQuality
        {
            get
            {
                FxQuality selected = Quality;
                return adaptiveCap.HasValue && adaptiveCap.Value < selected ? adaptiveCap.Value : selected;
            }
        }

        public static FxScales Scales => FxQualityMapper.ScalesFor(EffectiveQuality);

        internal static void SetAdaptiveCap(FxQuality? quality) => adaptiveCap = quality;

        public static void Describe(IDictionary<string, object> state)
        {
            if (state == null) return;
            state["fxQuality"] = Quality.ToString();
            state["fxEffectiveQuality"] = EffectiveQuality.ToString();
            state["fxEffects"] = effects.Count;
            state["fxRegistrationRefused"] = refusedRegistrations;
            FxRtPool.Describe(state);
            FxVoiceBus.Describe(state);
            effects.Describe(state);
            foreach (KeyValuePair<string, float> pair in lastMs)
                state["fxMs." + pair.Key] = pair.Value;
        }

        /// <summary>Call on the Unity main thread after feature patches are removed.</summary>
        public static int Shutdown()
        {
            int failures = effects.ReleaseAll();
            lastMs.Clear();
            refusedRegistrations = 0;
            adaptiveCap = null;
            qualityLevelCount = 0;
            FxRtPool.ReleaseAll();
            FxVoiceBus.Clear();
            return failures;
        }

        private static void Record(string id, float ms)
        {
            if (!effects.Contains(id)) return;
            lastMs[id] = ms;
        }
    }
}

using System.Collections.Generic;
using BoscaliSummer.Modules.Immersion.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Runtime
{
    /// <summary>Reads vanilla GLOC (private bloodPressure/stamina/conscious), inbound missiles and hit pulses.</summary>
    internal sealed class PilotVitals
    {
        private static readonly AccessTools.FieldRef<GLOC, float> BloodPressure = Ref<float>("bloodPressure");
        private static readonly AccessTools.FieldRef<GLOC, float> Stamina = Ref<float>("stamina");
        private static readonly AccessTools.FieldRef<GLOC, bool> Conscious = Ref<bool>("conscious");
        private readonly List<GLOC> found = new List<GLOC>(2);
        private Pilot pilot;
        private Aircraft aircraft;
        private GLOC gloc;
        private MissileWarning warning;
        private float resolveAt;
        private int hits;

        internal bool HasVanilla => gloc != null;
        internal void AddHit() => hits++;
        /// <summary>Clear hit pulses once a physiology step actually consumed them.</summary>
        internal void ConsumeHits() => hits = 0;

        internal PilotVitalsSample Read(Aircraft followed)
        {
            Pilot current = followed != null && followed.pilots != null && followed.pilots.Length > 0 ? followed.pilots[0] : null;
            if (followed != aircraft || current != pilot) Bind(followed, current);
            if (pilot != null && Time.unscaledTime >= resolveAt) Resolve();
            var s = new PilotVitalsSample { G = pilot != null ? pilot.gForce : 1f, Hits = hits };
            if (gloc != null && BloodPressure != null && Stamina != null && Conscious != null)
            {
                s.HasVanilla = true;
                s.BloodPressure = BloodPressure(gloc);
                s.Stamina = Stamina(gloc);
                s.Conscious = Conscious(gloc);
            }
            List<Missile> known = warning != null ? warning.knownMissiles : null;
            if (known != null)
                for (int i = 0; i < known.Count; i++)
                    if (known[i] != null && !known[i].disabled) s.Threats++;
            return s;
        }

        internal void Reset() => Bind(null, null);

        private void Bind(Aircraft value, Pilot current)
        {
            aircraft = value; pilot = current; gloc = null; hits = 0; resolveAt = 0f;
            warning = value != null ? value.GetComponent<MissileWarning>() : null;
        }

        // PilotPlayerState adds a GLOC on every EnterState; the newest enabled one is simulated.
        // Re-checked every 2 s into a reused list, so a re-entered player state is picked up.
        private void Resolve()
        {
            resolveAt = Time.unscaledTime + 2f;
            pilot.GetComponents(found);
            gloc = null;
            for (int i = found.Count - 1; i >= 0 && gloc == null; i--) if (found[i] != null && found[i].enabled) gloc = found[i];
            found.Clear();
            if (gloc == null) resolveAt = Time.unscaledTime + .25f;
        }

        private static AccessTools.FieldRef<GLOC, T> Ref<T>(string name)
        {
            try { return AccessTools.FieldRefAccess<GLOC, T>(name); }
            catch (System.Exception)
            {
                Plugin.Logger?.LogWarning("[Immersion] GLOC." + name + " not found; pilot cues use the built-in model.");
                return null;
            }
        }
    }
}

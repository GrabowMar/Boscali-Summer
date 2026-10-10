using System.Collections.Generic;
using BoscaliSummer.Modules.Immersion.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Runtime
{
    /// <summary>
    /// Reads vanilla GLOC (private bloodPressure/stamina/conscious), inbound missiles, hit pulses and whether the
    /// airframe is crippled. Damage costs nothing per frame: the airframe is looked at only when a hit lands.
    /// </summary>
    internal sealed class PilotVitals
    {
        private static readonly AccessTools.FieldRef<GLOC, float> BloodPressure = Ref<float>("bloodPressure");
        private static readonly AccessTools.FieldRef<GLOC, float> Stamina = Ref<float>("stamina");
        private static readonly AccessTools.FieldRef<GLOC, bool> Conscious = Ref<bool>("conscious");
        // Net blast (hit points on one part) that marks a warhead rather than a cannon shell. A rough line on purpose.
        private const float WarheadBlast = 10f;
        private readonly List<GLOC> found = new List<GLOC>(2);
        private Pilot pilot;
        private Aircraft aircraft;
        private GLOC gloc;
        private MissileWarning warning;
        private float resolveAt, checkAt, startHealth;
        private int hits, startParts;
        private bool blast, struck, critical;

        internal bool HasVanilla => gloc != null;
        internal bool Critical => critical;
        internal void AddHit(float blastDamage)
        {
            hits++;
            struck = true;
            if (blastDamage >= WarheadBlast) blast = true;
        }
        /// <summary>Clear hit pulses once a physiology step actually consumed them.</summary>
        internal void ConsumeHits() { hits = 0; blast = false; }

        internal PilotVitalsSample Read(Aircraft followed)
        {
            Pilot current = followed != null && followed.pilots != null && followed.pilots.Length > 0 ? followed.pilots[0] : null;
            if (followed != aircraft || current != pilot) Bind(followed, current);
            if (pilot != null && Time.unscaledTime >= resolveAt) Resolve();
            // The airframe is inspected when a hit lands, then every 2 s while crippled so a repair is noticed.
            if (struck || (critical && Time.unscaledTime >= checkAt))
            {
                struck = false;
                checkAt = Time.unscaledTime + 2f;
                critical = Crippled();
            }
            var s = new PilotVitalsSample { G = pilot != null ? pilot.gForce : 1f, Hits = hits, Blast = blast, Critical = critical };
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
            blast = critical = false;
            startParts = value != null && value.partLookup != null ? value.partLookup.Count : 0;
            startHealth = PartHealth(value, out _);
            struck = value != null; // one look on entry, so an already crippled aircraft is recognised
            warning = value != null ? value.GetComponent<MissileWarning>() : null;
        }

        // Crippled: destroyed, a part torn off, or a fifth of the part health it had on entry gone. Approximate by design.
        private bool Crippled()
        {
            if (aircraft == null) return false;
            if (aircraft.disabled) return true;
            float health = PartHealth(aircraft, out bool torn);
            return torn || (aircraft.partLookup != null && aircraft.partLookup.Count < startParts) || health < startHealth * .8f;
        }

        private static float PartHealth(Aircraft unit, out bool torn)
        {
            torn = false;
            float health = 0f;
            List<UnitPart> parts = unit != null ? unit.partLookup : null;
            if (parts == null) return 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                UnitPart part = parts[i];
                if (part == null || part.IsDetached()) torn = true;
                else health += Mathf.Max(0f, part.hitPoints);
            }
            return health;
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

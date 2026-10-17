using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer: a sparse ribbon of tumbling foil left by a MALD-X BAIT. Looks only:
    /// the clouds that blind seekers and their lifetime live in MaldxService on the server.
    /// </summary>
    internal static class MaldxChaff
    {
        private static readonly ChaffVisuals Visuals = new ChaffVisuals();

        public static void Clear() => Visuals.Clear();

        public static void Attach(Missile missile)
        {
            if (VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Decoy || Datum.origin == null) return;

            var go = new GameObject("Vanguard / Bait Chaff");
            go.SetActive(false); // set up before the system starts
            go.transform.SetParent(missile.transform, false);
            ParticleSystem particles = go.AddComponent<ParticleSystem>();
            if (!Visuals.Configure(particles, Datum.origin, MaldxRules.ChaffSeconds, ribbon: true))
            {
                UnityEngine.Object.Destroy(go);
                return;
            }
            go.SetActive(true);

            // Visible fibres drift after the bait is gone; radar-cloud persistence is independent.
            missile.onDisableUnit += _ =>
            {
                if (go == null) return;
                go.transform.SetParent(Datum.origin, true);
                ParticleSystem.EmissionModule stop = particles.emission;
                stop.enabled = false;
                UnityEngine.Object.Destroy(go, particles.main.startLifetime.constantMax);
            };
        }
    }
}

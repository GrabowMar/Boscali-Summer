using System;
using System.Reflection;
using BoscaliSummer.Modules.Vanguard.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer: the glitter ribbon a MALD-X BAIT leaves, one puff every PuffSpacing metres flown, hanging as long as a
    /// chaff cloud does. Looks only: the clouds that blind seekers live in MaldxService on the server. The puff borrows the
    /// flare smoke's material, as the aircraft's own chaff bundle does; without it the bait flies with no ribbon.
    /// </summary>
    internal static class MaldxChaff
    {
        private const float PuffSpacing = 60f;
        private static readonly FieldInfo FlarePrefab = AccessTools.Field(typeof(FlareEjector), "flarePrefab");
        private static Material puff;

        public static void Attach(Missile missile)
        {
            if (VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Decoy || Datum.origin == null) return;
            if (puff == null) puff = Puff();
            if (puff == null) return;

            var go = new GameObject("Vanguard / Bait Chaff");
            go.SetActive(false); // set up before the system starts
            go.transform.SetParent(missile.transform, false);
            ParticleSystem particles = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.startLifetime = MaldxRules.ChaffSeconds;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(20f, 45f);
            main.startColor = new Color(0.86f, 0.9f, 0.96f, 0.3f);
            main.gravityModifier = 0.01f;
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom; // the flare smoke's choice: the ribbon stays put through origin shifts
            main.customSimulationSpace = Datum.origin;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 1f / PuffSpacing;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 8f;
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2f));
            ParticleSystem.ColorOverLifetimeModule fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = puff;
            go.SetActive(true);

            // The ribbon outlives the bait, as its clouds do.
            missile.onDisableUnit += _ =>
            {
                if (go == null) return;
                go.transform.SetParent(Datum.origin, true);
                ParticleSystem.EmissionModule stop = particles.emission;
                stop.enabled = false;
                UnityEngine.Object.Destroy(go, MaldxRules.ChaffSeconds);
            };
        }

        private static Material Puff()
        {
            FlareEjector flare = UnityEngine.Object.FindObjectOfType<FlareEjector>();
            var round = flare != null && FlarePrefab != null ? FlarePrefab.GetValue(flare) as GameObject : null;
            if (round == null) return null;
            Material found = null;
            foreach (ParticleSystemRenderer renderer in round.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (renderer.sharedMaterial == null) continue;
                if (found == null) found = renderer.sharedMaterial;
                if (renderer.sharedMaterial.name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0) return renderer.sharedMaterial;
            }
            return found;
        }
    }
}

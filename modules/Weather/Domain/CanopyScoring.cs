using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// Pure name scoring that picks the canopy glass renderer out of an aircraft
    /// hierarchy. A positive score means "plausibly canopy glass"; gunsights, HUD
    /// glass, mirrors and lamps score zero or negative and never win. Engine-agnostic;
    /// covered by CanopyScoringTests.
    /// </summary>
    internal static class CanopyScoring
    {
        public static bool PlausibleBounds(float eyeDistance, float diameter) =>
            eyeDistance >= 0f && eyeDistance <= 4f && diameter >= 0.08f && diameter <= 32f;

        public static int ScoreSurface(string obj, string mat, string shader, bool transparent,
            float eyeDistance, float diameter)
        {
            if (!transparent || !PlausibleBounds(eyeDistance, diameter)) return 0;
            int score = ScoreGlass(obj, mat, shader);
            // Transparency also describes instruments and effects; require glass evidence.
            return score >= 80 ? score : 0;
        }

        public static int ScoreGlass(string objectName, string materialName, string shaderName)
        {
            string obj = (objectName ?? string.Empty).ToLowerInvariant();
            string mat = (materialName ?? string.Empty).ToLowerInvariant();
            string shd = (shaderName ?? string.Empty).ToLowerInvariant();

            // Hard exclusions first: glazing that is NOT the canopy.
            if (ContainsAny(obj, "gunsight", "sightglass", "hud", "hmd", "mirror", "mfd", "mfdscreen")) return -500;
            if (ContainsAny(mat, "gunsight", "hud", "hmd", "mirror", "mfd", "reticle", "sight")) return -500;
            if (ContainsAny(obj, "lamp", "light", "beacon", "strobe", "tire", "wheel", "gear")) return -500;
            if (ContainsAny(mat, "lamp", "light", "beacon", "strobe", "tire", "emissive")) return -500;

            int score = 0;
            if (ContainsAny(obj, "canopy")) score += 400;
            if (ContainsAny(obj, "windscreen", "windshield")) score += 350;
            if (ContainsAny(obj, "cockpitglass", "cockpit_glass", "glasscanopy")) score += 300;
            if (ContainsAny(mat, "canopy")) score += 250;
            if (ContainsAny(mat, "windscreen", "windshield")) score += 220;
            if (ContainsAny(mat, "glass") && !ContainsAny(mat, "fiberglass", "plexiglass_nose")) score += 120;
            if (ContainsAny(obj, "cockpit") && ContainsAny(mat, "glass", "transparent")) score += 150;
            if (ContainsAny(shd, "glass", "transparent")) score += 60;
            if (ContainsAny(obj, "glass")) score += 80;
            return score;
        }

        private static bool ContainsAny(string haystack, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (haystack.Contains(needles[i])) return true;
            }
            return false;
        }
    }
}

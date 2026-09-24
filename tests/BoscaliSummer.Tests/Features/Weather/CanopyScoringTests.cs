using BoscaliSummer.Core;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class CanopyScoringTests
    {
        public static void Run()
        {
            // 1. Canonical canopy glass scores positive and wins over plain parts
            int canopy = CanopyScoring.ScoreGlass("Canopy_Glass", "CanopyGlass_Mat", "Universal Render Pipeline/Lit");
            TestAssert.That(canopy > 0, "Canopy glass must score positive");
            int windscreen = CanopyScoring.ScoreGlass("Windscreen", "Windscreen_Glass", "Glass/Transparent");
            TestAssert.That(windscreen > 0, "Windscreen must score positive");
            int fuselage = CanopyScoring.ScoreGlass("Fuselage_LOD0", "Body_Paint", "Universal Render Pipeline/Lit");
            TestAssert.That(canopy > fuselage && windscreen > fuselage, "Glass must beat painted fuselage");

            // 2. Case-insensitive matching
            TestAssert.That(CanopyScoring.ScoreGlass("CANOPY", "MAT", "SHD") > 0, "Scoring must ignore case");

            // 3. Hard exclusions: glazing that is NOT the canopy never wins
            TestAssert.That(CanopyScoring.ScoreGlass("Gunsight_Glass", "Glass", "Glass") < 0, "Gunsight must be excluded");
            TestAssert.That(CanopyScoring.ScoreGlass("HUD_Glass", "HUDGlass", "Transparent") < 0, "HUD glass must be excluded");
            TestAssert.That(CanopyScoring.ScoreGlass("Canopy", "HUD_MFD", "Lit") < 0, "MFD material must be excluded");
            TestAssert.That(CanopyScoring.ScoreGlass("Mirror_L", "MirrorGlass", "Reflective") < 0, "Mirror must be excluded");
            TestAssert.That(CanopyScoring.ScoreGlass("LandingLight_Lens", "Glass", "Emissive") < 0, "Lamp lens must be excluded");
            TestAssert.That(CanopyScoring.ScoreGlass("Canopy", "Tire_Rubber", "Standard") < 0, "Wheel parts must be excluded");

            // 4. Null-safe
            TestAssert.That(CanopyScoring.ScoreGlass(null, null, null) == 0, "Null names must score zero");

            // 5. Shader response curves: frozen when dry, bounded, monotonic
            TestAssert.That(CanopyShaderParams.FlowPhaseRate(0f, 1f) == 0f, "Dry glass must freeze flow");
            TestAssert.That(System.Math.Abs(CanopyShaderParams.FlowPhaseRate(1f, 0f) - 0.65f) < 0.0001f,
                "Slow storm flow must be 0.65/s");
            TestAssert.That(System.Math.Abs(CanopyShaderParams.FlowPhaseRate(1f, 1f) - 2.65f) < 0.0001f,
                "Fast storm flow must be 2.65/s");
            TestAssert.That(CanopyScoring.ScoreSurface("Canopy_Frame", "Paint", "Lit", false, 0.5f, 2f) == 0,
                "A canopy name cannot turn opaque frames into glass");
            TestAssert.That(CanopyScoring.ScoreSurface("Mesh42", "WindowGlass", "Lit", true, 1f, 3f) > 0,
                "Unnamed model parts must resolve through glass material and nearby geometry");
            TestAssert.That(CanopyScoring.ScoreSurface("Mesh42", "WindowGlass", "Lit", true, 20f, 3f) == 0,
                "Glass far from the cockpit must be rejected");
            TestAssert.That(CanopyScoring.ScoreSurface("HUD", "WindowGlass", "Lit", true, 1f, 3f) == 0,
                "Transparent HUD surfaces must remain excluded");
            TestAssert.That(CanopyScoring.ScoreSurface("Canopy", "Glass", "Lit", true, float.NaN, 3f) == 0,
                "Invalid bounds must fail closed");
            float wet = CanopyShaderParams.Wetness(0f, 1f, 0f, 0.5f);
            TestAssert.That(wet > 0f && wet < 1f, "Rain must wet glass gradually");
            TestAssert.That(CanopyShaderParams.Wetness(1f, 0f, 0f, 1f) > 0f,
                "Stopping rain must leave residual drops");
            TestAssert.That(CanopyShaderParams.Wetness(1f, 0f, 1f, 1f) < CanopyShaderParams.Wetness(1f, 0f, 0f, 1f),
                "Airspeed must clear residual water faster");
            TestAssert.That(CanopyShaderParams.Wetness(1f, 0f, 1f, 100f) == 0f,
                "Long dry interval must clear without undershoot");
        }
    }
}

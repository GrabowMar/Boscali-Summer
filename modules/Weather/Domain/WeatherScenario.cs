using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal enum WeatherScenario { HurricaneEye, SquallAssault, MountainWave, SeaFog, FrontalPassage, ResetAll }

    /// <summary>The console's complete presets, shared with the render bench. Inputs and
    /// anchors use map coordinates so floating-origin camera positions cannot leak into a key.</summary>
    internal static class WeatherScenarios
    {
        public static WeatherKey Apply(WeatherScenario scenario, WeatherKey basis, bool hasCamera,
            float x, float z, float forwardX, float forwardZ)
        {
            // A scenario is a pin or a story plus the set-pieces (spec 8.4). The tape is held in slice 4a, so SquallAssault and FrontalPassage show the
            // story's first segments with the front upwind; "front in 5" arrives with the moving tape (slice 5).
            byte story = WeatherKey.None, pin = WeatherKey.None, sets = 0;
            float ahead = -1f;
            switch (scenario)
            {
                case WeatherScenario.HurricaneEye: pin = 43; sets = Superstructures.StormEyeSet; ahead = 0f; break;
                case WeatherScenario.SquallAssault: story = (byte)StoryKind.ColdFront; sets = Superstructures.SquallLineSet | Superstructures.SupercellSet; break;
                case WeatherScenario.MountainWave: pin = 7; sets = Superstructures.LenticularSet; ahead = 25000f; break;
                case WeatherScenario.SeaFog: pin = 0; sets = Superstructures.FogBankSet; break;
                case WeatherScenario.FrontalPassage: story = (byte)StoryKind.FrontalSystem; break;
                case WeatherScenario.ResetAll: break;
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }
            float length = (float)Math.Sqrt(forwardX * forwardX + forwardZ * forwardZ);
            if (length < 0.001f) { forwardX = 0f; forwardZ = 1f; length = 1f; }
            bool anchor = hasCamera && ahead >= 0f;
            // One key edit: the tape fields and the frame survive; the dynamic flag goes through Rebased so the tape position is continuous.
            WeatherKey keyed = basis.WithStory(story).WithSkyPin(pin).WithSets(sets);
            keyed = anchor
                ? keyed.WithAnchor(x + forwardX / length * ahead, z + forwardZ / length * ahead)
                : keyed.WithoutAnchor();
            return keyed.Rebased(basis.Epoch, scenario == WeatherScenario.ResetAll);
        }
    }
}

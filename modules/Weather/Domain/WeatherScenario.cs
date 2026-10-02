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
            WeatherRegimeType state;
            byte sets = 0;
            float ahead = -1f;
            switch (scenario)
            {
                case WeatherScenario.HurricaneEye: state = WeatherRegimeType.Storm; sets = Superstructures.StormEyeSet; ahead = 0f; break;
                case WeatherScenario.SquallAssault: state = WeatherRegimeType.RainSquall; sets = Superstructures.SquallLineSet | Superstructures.SupercellSet; break;
                case WeatherScenario.MountainWave: state = WeatherRegimeType.Fair; sets = Superstructures.LenticularSet; ahead = 25000f; break;
                case WeatherScenario.SeaFog: state = WeatherRegimeType.Clear; sets = Superstructures.FogBankSet; break;
                case WeatherScenario.FrontalPassage: state = WeatherRegimeType.Overcast; break;
                case WeatherScenario.ResetAll: state = basis.OpeningState(); break;
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }
            float length = (float)Math.Sqrt(forwardX * forwardX + forwardZ * forwardZ);
            if (length < 0.001f) { forwardX = 0f; forwardZ = 1f; length = 1f; }
            bool anchor = hasCamera && ahead >= 0f;
            return new WeatherKey(basis.Seed, basis.Epoch, scenario == WeatherScenario.ResetAll, (byte)state,
                basis.IntervalMinutes, basis.FadeSeconds, sets, basis.LayoutSalt, anchor,
                anchor ? x + forwardX / length * ahead : 0f,
                anchor ? z + forwardZ / length * ahead : 0f, 0);
        }
    }
}

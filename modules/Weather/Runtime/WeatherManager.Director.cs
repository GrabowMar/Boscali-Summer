using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    // The weather console's presets under the names a mission script uses (IWeatherDirector).
    internal sealed partial class WeatherManager : IWeatherDirector
    {
        public bool Hold(string setPiece)
        {
            switch (setPiece)
            {
                case "storm": return ApplyScenario(WeatherScenario.SquallAssault);
                case "hurricane": return ApplyScenario(WeatherScenario.HurricaneEye);
                case "front": return ApplyScenario(WeatherScenario.FrontalPassage);
                case "fog": return ApplyScenario(WeatherScenario.SeaFog);
                case "wave": return ApplyScenario(WeatherScenario.MountainWave);
                default: return false;
            }
        }

        public void Resume() => ApplyScenario(WeatherScenario.ResetAll);

        public void OpenConsole()
        {
            if (console == null || !console.IsOpen) ToggleConsole();
        }
    }
}

using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The host's once-per-second settings check (<c>WeatherManager.UpdateField</c>): does the live key still match
    /// <c>TapeMoves</c> / console auto and the pace speed the mission wind gives? Pure so that "an idle host never
    /// re-keys" (spec 9.6.7) can be tested; a needless re-key would restart the interim step counter and broadcast.
    /// </summary>
    internal static class TapeReconcile
    {
        public static bool Needed(WeatherKey key, bool wantDynamic, float wantSpeed) =>
            key.Dynamic != wantDynamic || (Scalar.IsFinite(wantSpeed) && Math.Abs(key.SystemSpeed - wantSpeed) > 1e-4f);
    }
}

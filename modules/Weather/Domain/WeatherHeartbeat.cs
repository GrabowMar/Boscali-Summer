namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// When the host re-sends its key (spec 9.4). Pure so it can be tested. It deliberately takes no "dynamic"
    /// input: a held host must keep sending, or a late joiner never gets a key.
    /// </summary>
    internal static class WeatherHeartbeat
    {
        public const float PeriodSeconds = 8f;

        public static bool Due(bool server, bool fieldReady, bool hasKey, float unscaledNow, float lastBroadcast) =>
            server && fieldReady && hasKey && unscaledNow - lastBroadcast > PeriodSeconds;
    }
}

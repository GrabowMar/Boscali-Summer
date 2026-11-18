namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Optional final adjustment after the Weather module applies native conditions and updates the skybox.</summary>
    internal delegate void NativeWeatherOverride(LevelInfo level);
}

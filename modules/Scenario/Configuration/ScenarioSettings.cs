using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Scenario.Configuration
{
    internal sealed class ScenarioSettings
    {
        internal ConfigEntry<float> TextSpeed { get; }

        internal ScenarioSettings(ConfigFile config)
        {
            const string section = "Scenario";
            TextSpeed = config.Bind(section, "TextSpeed", 1f,
                new ConfigDescription("How fast scripted dialogue moves on: 1 is an unhurried read, 2 holds each line half as long.",
                    new AcceptableValueRange<float>(0.5f, 3f)));
        }
    }
}

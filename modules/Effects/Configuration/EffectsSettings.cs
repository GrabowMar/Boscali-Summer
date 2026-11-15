using BepInEx.Configuration;
using BoscaliSummer.Modules.Effects.Domain;

namespace BoscaliSummer.Modules.Effects.Configuration
{
    internal sealed class EffectsSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> AgxEnabled { get; }
        public ConfigEntry<AgxLook> AgxLook { get; }
        public ConfigEntry<bool> Dithering { get; }
        public ConfigEntry<EdgeSmoothing> EdgeSmoothing { get; }
        public CinematicSettings Cinematic { get; }
        public ConfigEntry<float> Contrast { get; }
        public ConfigEntry<float> Saturation { get; }
        public ConfigEntry<float> Temperature { get; }
        public ConfigEntry<string> Lut { get; }
        public ConfigEntry<float> LutStrength { get; }
        public ConfigEntry<bool> SharpenEnabled { get; }
        public ConfigEntry<float> SharpenStrength { get; }

        public EffectsSettings(ConfigFile config)
        {
            Cinematic = new CinematicSettings(config);
            Dithering = config.Bind("Effects", "Dithering", true,
                "Reduce banding in sky and fog gradients with native blue-noise dithering. Independent of colour grade; restores the game's setting when off. Requires retained game shader/noise resources.");
            EdgeSmoothing = config.Bind("Effects", "EdgeSmoothing", Domain.EdgeSmoothing.Game,
                "Game: keep native anti-aliasing. Smaa: use high-quality SMAA on the world/cockpit camera stack. Adds GPU work; applies live and restores native settings when switched back to Game.");
            AgxEnabled = config.Bind("Effects", "AgxEnabled", true,
                "AgX HDR tonemapping on the world/cockpit picture. Independent of Enabled; preserves automatic exposure and G-LOC. Night vision and sensor feeds keep their own look. Applies live.");
            AgxLook = config.Bind("Effects", "AgxLook", Domain.AgxLook.Base,
                "AgX look: Base (neutral), Punchy (more contrast), Hue (richer colour).");
            Enabled = config.Bind("Effects", "Enabled", false,
                "Client-only display grade: contrast, saturation, white balance, an optional LUT file and sharpening. Applies live.");
            Contrast = config.Bind("Effects", "Contrast", 8f, "Contrast, -100 to 100. 0 leaves the game's own.");
            Saturation = config.Bind("Effects", "Saturation", 6f, "Saturation, -100 (grey) to 100. 0 leaves the game's own.");
            Temperature = config.Bind("Effects", "Temperature", 0f, "White balance, -100 (cool) to 100 (warm).");
            Lut = config.Bind("Effects", "Lut", "",
                "File name of a .cube 3D LUT in BepInEx/config/BoscaliSummer/luts (any size up to 65). Use display (Rec.709/sRGB) LUTs, not camera-log ones. Empty for none; edits to the file reload live.");
            LutStrength = config.Bind("Effects", "LutStrength", 1f, "How much of the LUT is blended in, 0 to 1.");
            SharpenEnabled = config.Bind("Effects", "SharpenEnabled", true,
                "AMD FidelityFX RCAS sharpening in the final blit. Needs Enabled.");
            SharpenStrength = config.Bind("Effects", "SharpenStrength", 0.4f, "Sharpening strength, 0 to 1.");
        }
    }
}

using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Compatibility.Runtime;

namespace BoscaliSummer.Modules.Compatibility
{
    /// <summary>
    /// Sharing the game with other client mods built on the same ground as Boscali (2026-11-06): NoAtmoFX and the
    /// plugins of Kelly's Client Effects that still load beside us. Client-only, no patches; SET > COMPAT.
    /// </summary>
    internal sealed class CompatibilityModule : IModule
    {
        private static readonly ModuleMetadata Module = new ModuleMetadata("compatibility", "Compatibility");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Type.EmptyTypes;

        public void Install(ModuleContext context)
        {
            var s = context.Settings.Compatibility;
            CompatBridge bridge = context.AddSceneService<CompatBridge>(50);
            bridge.Configure(s, context.ClientSettings, context.Logger);
            context.OnTeardown(ForeignMods.ClearYields);

            const string Atmo = "NOATMOFX", Kelly = "KELLY'S FX", Keys = "KEY CLASHES";
            ClientSettingToggle atmo = context.AddClientSetting(Atmo, "NOATMOFX LINK",
                "Share the sky with NoAtmoFX (volumetric clouds). Off: Boscali ignores it and both may draw clouds.", s.NoAtmoFxLink);
            atmo.Gate = () => ForeignMods.Find(ForeignMods.NoAtmoFx) == null ? "NoAtmoFX is not installed." : null;
            context.AddClientSetting(Atmo, "NOATMOFX SKY",
                "NoAtmoFX draws the clouds; Boscali's clouds, sun shafts, lightning and thunder stand down while its clouds are on (F10).",
                s.NoAtmoFxSky, requires: atmo);
            context.AddClientSetting(Atmo, "WEATHER LINK",
                "NoAtmoFX's clouds follow Boscali's weather where you fly: fronts, overcast and storms. Off hands it back the mission's weather.",
                s.WeatherLink, requires: atmo);
            context.AddClientChoice(Atmo, "RAIN",
                "Who draws rain and canopy rain. BOSCALI keeps NoAtmoFX's cloud mist and frost on the glass, without its rain.",
                s.Rain, new[] { "BOSCALI", "NOATMOFX" }, atmo);

            ClientSettingToggle kelly = context.AddClientSetting(Kelly, "KELLY'S LINK",
                "Share the effects both packs have with Kelly's Client Effects. Its core, weather and cockpit parts step aside for Boscali on their own.",
                s.KellyLink);
            kelly.Gate = () => CompatBridge.KellyInstalled ? null : "Kelly's Client Effects is not installed.";
            context.AddClientChoice(Kelly, "OVERLAPS",
                "Who draws contrails, dust wake, bullet cracks, airfield lights and the missile boom. The other side's copy is switched off.",
                s.Overlaps, new[] { "BOSCALI", "KELLY'S" }, kelly);

            ClientSettingToggle keys = context.AddClientSetting(Keys, "MOVE CLASHING KEYS",
                "A NoAtmoFX key on one of ours moves to a Ctrl chord (F8: Alt). Boscali's own keys ignore Ctrl and Alt chords, so Kelly's Ctrl+F8 panel is safe.",
                s.MoveKeys);
            keys.Gate = () => ForeignMods.Find(ForeignMods.NoAtmoFx) == null ? "No installed mod's keys clash with Boscali's." : null;
        }
    }
}

using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command;
using BoscaliSummer.Features.FireAndDestruction;
using BoscaliSummer.Features.Radio;
using BoscaliSummer.Features.Progression;
using BoscaliSummer.Features.Support;
using BoscaliSummer.Features.UrbanCombat;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Runtime;

namespace BoscaliSummer.Bootstrap
{
    internal static class ModCompositionRoot
    {
        public static FeatureHost Start(ManualLogSource logger, ModConfiguration settings)
        {
            GameAccess.Initialise();
            var host = new FeatureHost(logger, settings);
            try
            {
                // A feature turned off in config is never installed, so it also never patches,
                // registers a network handler or polls. Support and Command depend on Progression.
                var features = new List<IModFeature>
                {
                    new FireAndDestructionFeature(),
                    new UrbanCombatFeature(),
                    new RadioFeature()
                };
                if (settings.Progression.Enabled.Value)
                {
                    features.Add(new ProgressionFeature());
                    if (settings.Support.Enabled.Value) features.Add(new SupportFeature());
                    if (settings.Command.Enabled.Value) features.Add(new CommandFeature());
                }
                host.Load(features.ToArray());
                CapabilityReport.Log();
                return host;
            }
            catch
            {
                host.Dispose();
                throw;
            }
        }
    }
}

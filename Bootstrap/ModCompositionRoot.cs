using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command;
using BoscaliSummer.Features.DynamicOperations;
using BoscaliSummer.Features.FireAndDestruction;
using BoscaliSummer.Features.Progression;
using BoscaliSummer.Features.QoL;
using BoscaliSummer.Features.Radio;
using BoscaliSummer.Features.Support;
using BoscaliSummer.Features.Trenches;
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
                // Progression, Support and Command disabled at startup are not installed.
                var features = new List<IModFeature>
                {
                    new FireAndDestructionFeature(),
                    new UrbanCombatFeature(),
                    new RadioFeature()
                };
                if (settings.QoL.Enabled.Value && !UnityEngine.Application.isBatchMode)
                    features.Add(new QoLFeature());
                if (settings.Progression.Enabled.Value)
                {
                    features.Add(new ProgressionFeature());
                    if (settings.Support.Enabled.Value) features.Add(new SupportFeature());
                    if (settings.Command.Enabled.Value) features.Add(new CommandFeature());
                }
                if (settings.DynamicOperations.Enabled.Value) features.Add(new DynamicOperationsFeature());
                if (settings.Trenches.Enabled.Value) features.Add(new TrenchesFeature());
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

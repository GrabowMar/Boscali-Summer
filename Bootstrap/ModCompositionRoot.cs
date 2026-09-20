using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Autopilot;
using BoscaliSummer.Features.Campaign;
using BoscaliSummer.Features.Command;
using BoscaliSummer.Features.DynamicOperations;
using BoscaliSummer.Features.Events;
using BoscaliSummer.Features.FireAndDestruction;
using BoscaliSummer.Features.HighCommand;
using BoscaliSummer.Features.Hud;
using BoscaliSummer.Features.Progression;
using BoscaliSummer.Features.QoL;
using BoscaliSummer.Features.Radio;
using BoscaliSummer.Features.Support;
using BoscaliSummer.Features.Squad;
using BoscaliSummer.Features.TheaterOps;
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
                    // The common HUD element installs first so every presentation feature below
                    // can find it; each of them resolves it late and works without it.
                    new HudFeature(),
                    new RadioFeature()
                };
                if (settings.QoL.Enabled.Value && !UnityEngine.Application.isBatchMode)
                    features.Add(new QoLFeature());
                if (settings.Autopilot.Enabled.Value && !UnityEngine.Application.isBatchMode)
                    features.Add(new AutopilotFeature());
                if (settings.Progression.Enabled.Value)
                {
                    features.Add(new SquadFeature());
                    features.Add(new ProgressionFeature());
                    if (settings.Support.Enabled.Value) features.Add(new SupportFeature());
                    if (settings.Command.Enabled.Value) features.Add(new CommandFeature());
                }
                if (settings.DynamicOperations.Enabled.Value) features.Add(new DynamicOperationsFeature());
                if (settings.HighCommand.Enabled.Value) features.Add(new HighCommandFeature());
                if (settings.TheaterOps.Enabled.Value) features.Add(new TheaterOpsFeature());
                if (settings.Trenches.Enabled.Value) features.Add(new TrenchesFeature());
                if (settings.Events.Enabled.Value) features.Add(new EventsFeature());
                if (settings.Campaign.Enabled.Value) features.Add(new CampaignFeature());
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

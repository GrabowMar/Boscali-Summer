using System;
using BoscaliSummer.Features.Squad.Networking;
using BoscaliSummer.Features.Squad.Patches;
using BoscaliSummer.Features.Squad.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Runtime;

namespace BoscaliSummer.Features.Squad
{
    internal sealed class SquadFeature : IModFeature
    {
        public FeatureMetadata Metadata => new FeatureMetadata("squad", "Squad / ace hunts");
        public Type[] PatchTypes => new[] { typeof(SquadDamagePatch), typeof(SquadKillPatch), typeof(SquadPilotDeathPatch) };
        public void Install(FeatureContext context)
        {
            SquadManager manager = context.AddSceneService<SquadManager>(44);
            SquadNet network = context.AddComponent<SquadNet>();
            manager.Configure(context.Settings.Squad, network, context.Logger, context.Services); network.Configure(manager);
            context.AddService<ISquadView>(manager);
            context.Logger.LogInfo("[Squad] Wing Command API: " + (WingLink.SquadAvailable ? "ready" : WingLink.SquadUnavailableReason));
        }
    }
}

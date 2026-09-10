using System;
using BoscaliSummer.Features.DynamicOperations.Networking;
using BoscaliSummer.Features.DynamicOperations.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.DynamicOperations
{
    internal sealed class DynamicOperationsFeature : IModFeature
    {
        public FeatureMetadata Metadata { get; } = new FeatureMetadata("dynamic-operations", "Dynamic secondary operations");
        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            OperationsManager manager = context.AddSceneService<OperationsManager>(51);
            OperationsNet network = context.AddComponent<OperationsNet>();
            network.Configure(manager);
            manager.Configure(context.Settings.DynamicOperations, network, context.Logger);
            context.AddService<ISecondaryObjectivesView>(manager);
            context.Logger.LogInfo("[Operations] Server-owned capture/defense/interdiction; 3 objectives/faction, 24 reward units; experimental.");
        }
    }
}

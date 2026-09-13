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
        public Type[] PatchTypes => new[] { typeof(OperationJamPatch) };

        public void Install(FeatureContext context)
        {
            OperationsManager manager = context.AddSceneService<OperationsManager>(51);
            OperationsNet network = context.AddComponent<OperationsNet>();
            network.Configure(manager);
            manager.Configure(context.Settings.DynamicOperations, network, context.Logger);
            context.AddService<ISecondaryObjectivesView>(manager);
            context.AddService<IOperationOutcomeSource>(manager);
            context.AddSceneService<OperationMapOverlay>(52).Configure(manager);
            context.Logger.LogInfo("[Operations] Eight contract families; acceptance required, 3 cards/2 active per faction, 24 reward units; experimental.");
        }
    }
}

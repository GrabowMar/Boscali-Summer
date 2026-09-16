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
        public Type[] PatchTypes => new[] { typeof(OperationJamPatch), typeof(OperationRescuePatch),
            typeof(OperationRepairPatch), typeof(OperationSupplyPatch), typeof(OperationSupplyTransferPatch) };

        public void Install(FeatureContext context)
        {
            OperationsManager manager = context.AddSceneService<OperationsManager>(51);
            OperationsNet network = context.AddComponent<OperationsNet>();
            network.Configure(manager);
            manager.Configure(context.Settings.DynamicOperations, network, context.Logger);
            context.AddService<ISecondaryObjectivesView>(manager);
            context.AddService<IOperationOutcomeSource>(manager);
            context.AddSceneService<ContractHud>(52).Configure(manager);
            context.AddSceneService<ContractMapHud>(53).Configure(manager);
            context.AddSceneService<OperationZoneHud>(54).Configure(manager);
            context.AddHostSettings(new HostSettingsTable("DYNAMIC OPERATIONS")
                .Number(1, context.Settings.DynamicOperations.RewardMultiplier, "REWARD SCALE",
                    "Scale mission money and XP for accepted contracts. Money uses the normal faction tax; XP is vanilla mission score.",
                    0.25f, v => v.ToString("0.00") + "x"));
            context.Logger.LogInfo("[Operations] 17 contract families; mod-drawn contract markers on the cockpit HUD and the tactical map plus the vicinity card; native rescue/repair/supply observations; acceptance required, 3 cards/2 active per faction, 24 reward units; experimental.");
        }
    }
}

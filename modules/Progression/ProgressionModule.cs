using System;
using BoscaliSummer.Modules.Progression.Networking;
using BoscaliSummer.Modules.Progression.Patches;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Modules.Progression.Presentation;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Progression
{
    internal sealed class ProgressionModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("progression", "Progression", "squad");
        private static readonly Type[] Patches =
        {
            typeof(AircraftEngineMapPatch)
        };

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            ProgressionManager manager = context.AddSceneService<ProgressionManager>(45);
            ProgressionNet network = context.AddComponent<ProgressionNet>();
            network.Configure(manager);
            ISquadView squad = context.Services.GetRequired<ISquadView>();
            manager.Configure(context.Settings.Progression, context.Logger, network, squad);
            manager.ConfigureBypass(context.Settings.Diagnostics.BypassRequirements);
            context.AddService<IPlayerPerks>(manager);
            context.AddService<IProgressionView>(manager);

            // The dial that decides how a mission's progression feels: how fast picks arrive.
            // The ceiling on picks is balance and stays in the config file.
            context.AddHostSettings(new HostSettingsTable("PROGRESSION")
                .Number(1, context.Settings.Progression.ScorePerPoint, "SCORE PER PICK",
                    "Score for the first OPS skill pick; pick n costs n x this, so picks get longer as they get better.",
                    50));

            if (!UnityEngine.Application.isBatchMode)
            {
                context.AddSceneService<SqdMfdPanel>(54).Configure(
                    manager, squad, context.Settings.Progression, context.Logger);
                context.AddSceneService<AceHuntHud>(54).Configure(squad);
                context.AddService<ISquadronInsignia>(new SquadronInsignia(context.Settings.Progression));
                context.AddSceneService<Visuals.TailPaint>(56).Configure(
                    context.Settings.Progression, context.Logger, network.Crests, network);
            }
        }
    }
}

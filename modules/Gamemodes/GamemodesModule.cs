using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Gamemodes.Domain;
using BoscaliSummer.Modules.Gamemodes.Networking;
using BoscaliSummer.Modules.Gamemodes.Runtime;
using BoscaliSummer.Modules.Gamemodes.Patches;
using UnityEngine;

namespace BoscaliSummer.Modules.Gamemodes
{
    internal sealed class GamemodesModule : IModule
    {
        public ModuleMetadata Metadata => new ModuleMetadata("gamemodes", "WarBox, Fuzzball and Heavyweight missions");
        public Type[] PatchTypes => [.. GamemodePatches.Types, .. FuzzballFlight.PatchTypes];

        public void Install(ModuleContext context)
        {
            var missions = new GamemodeMissions(context.Logger);
            BoscaliMission.ExtraGroups += missions.Groups;
            BoscaliMission.PresetModulesOff = Preset;
            GamemodeLaunchPatch.Resolve = missions.Launch;
            var runtime = context.AddSceneService<GamemodeRuntime>(94);
            var network = context.AddComponent<GamemodeNet>();
            runtime.Configure(network);
            network.Configure(runtime);
            context.AddService<IMissionDashboardView>(runtime);
            if (!Application.isBatchMode) context.AddSceneService<GamemodeHud>(96).Configure(runtime);
            context.OnTeardown(() =>
            {
                BoscaliMission.ExtraGroups -= missions.Groups;
                BoscaliMission.PresetModulesOff = null;
                GamemodeLaunchPatch.Resolve = null;
                MissionProfile.SetRequiredDisabled(null);
            });
        }

        private static string[] Preset(NuclearOption.SavedMission.Mission mission)
        {
            GameModeKind kind = GamemodeRules.DetectMode(mission?.missionSettings?.description);
            if (kind == GameModeKind.None) return null;
            // WarBox uses the existing operation, nation and command directors, with native flight/economy.
            if (kind == GameModeKind.WarBox) return ["spooktober", "ares", "combat"];
            var off = new List<string>(MissionProfile.BattlefieldModules)
                { "wing", "spooktober", "ares", "skylark" };
            return off.ToArray();
        }
    }
}

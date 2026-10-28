using System;
using System.IO;
using BepInEx;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Scenario;
using BoscaliSummer.Modules.Scenario.Patches;
using BoscaliSummer.Modules.Scenario.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Scenario
{
    /// <summary>
    /// Mission scripts (docs/SCENARIOS.md): the scripted missions in the game's mission picker, their dialogue, cutscenes and hints, and campaigns that unlock
    /// chapter by chapter. Always installed: a script is what puts its mission in the picker, and a mission without one costs this module nothing.
    /// </summary>
    internal sealed class ScenarioModule : IModule
    {
        private static readonly ModuleMetadata Module = new ModuleMetadata("scenario", "Mission scripts, dialogue and campaigns");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => new[] { typeof(LaunchMissionPatch) };

        public void Install(ModuleContext context)
        {
            var library = new ScenarioLibrary(Path.Combine(Paths.ConfigPath, "BoscaliSummer", "scenarios"), context.Logger);
            var missions = new ScenarioMissions(library, context.Logger);
            BoscaliMission.ExtraGroups = missions.Groups;
            BoscaliMission.ScriptedModulesOff = mission => missions.ScriptOf(mission)?.Without.ToArray();
            BoscaliMission.ScriptedPeace = mission => FactionPeace.Of(missions.ScriptOf(mission));
            LaunchMissionPatch.Resolve = missions.Launch;
            context.OnTeardown(() =>
            {
                BoscaliMission.ExtraGroups = null;
                BoscaliMission.ScriptedModulesOff = null;
                BoscaliMission.ScriptedPeace = null;
                LaunchMissionPatch.Resolve = null;
            });
            // A dedicated server builds and hosts the scripted mission; the story is each player's own screen.
            if (Application.isBatchMode) return;
            context.AddClientSetting("MISSIONS", "CUTSCENES",
                "Camera cutscenes in scripted missions. OFF keeps you in the cockpit; the story still plays on the boards.",
                context.Settings.Scenario.Cutscenes);
            context.AddSceneService<ScenarioHost>(95).Configure(library, missions.ScriptOf, context.Settings.Scenario, context.Logger);
        }
    }
}

using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Cinematography.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Cinematography
{
    internal sealed class CinematographyModule : IModule
    {
        public ModuleMetadata Metadata => new ModuleMetadata("cinematography", "Cutscene camera and private shot tools");
        public Type[] PatchTypes => Array.Empty<Type>();
        public void Install(ModuleContext context)
        {
            if (Application.isBatchMode) return;
            // The cutscene camera of scripted missions is for every player; the shot editor below stays a private, opt-in tool.
            context.AddService<ICinematicStage>(context.AddSceneService<StagePlayer>(89));
            if (!context.Settings.Cinematography.Enabled.Value) return;
            context.AddSceneService<CinematicDirector>(90).Configure(context.Settings.Cinematography, context.Logger);
            context.Logger.LogInfo("Cinematography: native spectator state; private production opt-in required; no recorder attached.");
        }
    }
}

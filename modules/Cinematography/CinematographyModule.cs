using System;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Cinematography.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Cinematography
{
    internal sealed class CinematographyModule : IModule
    {
        public ModuleMetadata Metadata => new ModuleMetadata("cinematography", "Private cinematic shot tools");
        public Type[] PatchTypes => Array.Empty<Type>();
        public void Install(ModuleContext context)
        {
            if (Application.isBatchMode) return;
            context.AddSceneService<CinematicDirector>(90).Configure(context.Settings.Cinematography, context.Logger);
            context.Logger.LogInfo("Cinematography: native spectator state; private production opt-in required; no recorder attached.");
        }
    }
}

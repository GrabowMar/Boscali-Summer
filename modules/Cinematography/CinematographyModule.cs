using System;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Cinematography.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Cinematography
{
    /// <summary>
    /// The camera half of cutscene production: nomodkit scripts the shots (CinematicAutomation.Step) and films them (its film op). Development builds only
    /// (the csproj leaves this folder out of a public release); it does nothing until a script commands it.
    /// </summary>
    internal sealed class CinematographyModule : IModule
    {
        public ModuleMetadata Metadata => new ModuleMetadata("cinematography", "Scripted camera shots for filming with nomodkit");
        public Type[] PatchTypes => Array.Empty<Type>();
        public void Install(ModuleContext context)
        {
            if (!Application.isBatchMode) context.AddSceneService<CinematicDirector>(90);
        }
    }
}

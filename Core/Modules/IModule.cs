using System;

namespace BoscaliSummer.Core.Modules
{
    internal interface IModule
    {
        ModuleMetadata Metadata { get; }
        Type[] PatchTypes { get; }
        void Install(ModuleContext context);
    }
}

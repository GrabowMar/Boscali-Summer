using BoscaliSummer.Core.Modules;

namespace BoscaliSummer.Modules.Command.Configuration
{
    /// <summary>
    /// The command module's settings for its static map-UI helpers and dev hooks, which have no instance to be handed
    /// them. Bound first thing in <see cref="CommandModule.Install"/> and cleared on rollback, so <see cref="Current"/>
    /// is null whenever the module is not installed; every reader is null-safe and falls back to the entry's default.
    /// Instances (scene services, panels) take <see cref="CommandSettings"/> through their own <c>Configure</c> instead.
    /// </summary>
    internal static class CommandSettingsScope
    {
        internal static CommandSettings Current { get; private set; }

        internal static void Bind(ModuleContext context)
        {
            Current = context.Settings.Command;
            context.OnTeardown(() => Current = null);
        }
    }
}

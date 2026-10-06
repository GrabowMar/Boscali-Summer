using System;
using BoscaliSummer.Core.Config;
using BoscaliSummer.Modules.Session.Networking;
using BoscaliSummer.Core.Modules;

namespace BoscaliSummer.Modules.Session
{
    /// <summary>
    /// SESSION: the multiplayer handshake every other module leans on. Always installed. A
    /// joining client learns the host's mod version and runs the session on the host's
    /// gameplay settings (<see cref="HostAuthority"/>), so prices, gates and timers on its
    /// panels match what the host enforces; its own values come back when it leaves.
    /// </summary>
    internal sealed class SessionModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("session", "Multiplayer session handshake and host settings");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => Type.EmptyTypes;

        public void Install(ModuleContext context)
        {
            context.AddComponent<SessionNet>().Configure(
                HostAuthority.Entries(context.Settings), HostAuthority.Switches(context.Settings), context.Logger);
        }
    }
}

using System;
using BoscaliSummer.Features.Session.Networking;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Session
{
    /// <summary>
    /// SESSION: the multiplayer handshake every other module leans on. Always installed. A
    /// joining client learns the host's mod version and runs the session on the host's
    /// gameplay settings (<see cref="HostAuthority"/>), so prices, gates and timers on its
    /// panels match what the host enforces; its own values come back when it leaves.
    /// </summary>
    internal sealed class SessionFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("session", "Multiplayer session handshake and host settings");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => Type.EmptyTypes;

        public void Install(FeatureContext context)
        {
            context.AddComponent<SessionNet>().Configure(
                HostAuthority.Entries(context.Settings), HostAuthority.Switches(context.Settings), context.Logger);
        }
    }
}

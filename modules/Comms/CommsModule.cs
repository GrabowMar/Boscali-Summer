using System;
using BoscaliSummer.Modules.Comms.Networking;
using BoscaliSummer.Modules.Comms.Patches;
using BoscaliSummer.Modules.Comms.Presentation;
using BoscaliSummer.Modules.Comms.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Comms
{
    /// <summary>
    /// COMMS: the multiplayer talk layer on its own COM bezel screen. Shared map pings,
    /// labels and drawings, brevity calls and activity logs, relayed by the host
    /// and kept inside each team unless a player posts to ALL. Installs independently; its one
    /// patch holds the map still while the pen is down.
    /// </summary>
    internal sealed class CommsModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("comms", "Multiplayer tactical map and team comms");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => new[] { typeof(CommsMapControlsPatch) };

        public void Install(ModuleContext context)
        {
            CommsNet network = context.AddComponent<CommsNet>();
            CommsManager manager = context.AddSceneService<CommsManager>(70);
            manager.Configure(context.Settings.Comms, network, context.Logger);
            context.AddService<IMapBoxInput>(manager);
            context.AddService<IQuickCalls>(manager);
            network.Configure(manager);

            CommsMapLayer layer = context.AddSceneService<CommsMapLayer>(71);
            layer.Configure(context.Settings.Comms, manager, context.Logger);

            CommsCockpitMarkers markers = context.AddSceneService<CommsCockpitMarkers>(72);
            markers.Configure(context.Settings.Comms, manager);

            CommsMfdPanel panel = context.AddSceneService<CommsMfdPanel>(73);
            panel.Configure(context.Settings.Comms, manager, context.Logger);

            context.AddHostSettings(new HostSettingsTable("COMMS")
                .Toggle(1, context.Settings.Comms.AllowAllChannel, "ALL CHANNEL",
                    "Let players post to everyone, the other side included. Off keeps all comms inside each team.")
                .Toggle(2, context.Settings.Comms.AllowDrawing, "MAP DRAWING",
                    "Let players draw strokes and shapes on the shared map.")
                .Number(4, context.Settings.Comms.PingSeconds, "PING LIFE",
                    "How long a map ping stays up.", 10, v => v.ToString("0") + " s")
                .Number(5, context.Settings.Comms.DrawingSeconds, "DRAWING LIFE",
                    "How long drawings and shapes stay on the map.", 60, v => (v / 60f).ToString("0") + " min")
                .Number(6, context.Settings.Comms.StickerSeconds, "LABEL LIFE",
                    "How long text labels stay on the map.", 60, v => (v / 60f).ToString("0") + " min"));
        }
    }
}

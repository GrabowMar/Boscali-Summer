using System;
using BoscaliSummer.Features.Comms.Networking;
using BoscaliSummer.Features.Comms.Patches;
using BoscaliSummer.Features.Comms.Presentation;
using BoscaliSummer.Features.Comms.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Comms
{
    /// <summary>
    /// COMMS: the multiplayer talk layer on its own COM bezel screen. Shared map pings,
    /// stickers, labels and drawings, brevity calls, polls and small games, relayed by the host
    /// and kept inside each team unless a player posts to ALL. Installs independently; its one
    /// patch holds the map still while the pen is down.
    /// </summary>
    internal sealed class CommsFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("comms", "Multiplayer map comms, polls and games");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => new[] { typeof(CommsMapControlsPatch) };

        public void Install(FeatureContext context)
        {
            CommsNet network = context.AddComponent<CommsNet>();
            CommsManager manager = context.AddSceneService<CommsManager>(70);
            manager.Configure(context.Settings.Comms, network, context.Logger);
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
                .Toggle(3, context.Settings.Comms.AllowGames, "GAMES",
                    "Allow dice, rock-paper-scissors and map hunts.")
                .Number(4, context.Settings.Comms.PingSeconds, "PING LIFE",
                    "How long a map ping stays up.", 10, v => v.ToString("0") + " s")
                .Number(5, context.Settings.Comms.DrawingSeconds, "DRAWING LIFE",
                    "How long drawings and shapes stay on the map.", 60, v => (v / 60f).ToString("0") + " min")
                .Number(6, context.Settings.Comms.StickerSeconds, "STICKER LIFE",
                    "How long stickers and text labels stay on the map.", 60, v => (v / 60f).ToString("0") + " min"));
        }
    }
}

using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliSummer.Features.Comms.Configuration
{
    /// <summary>
    /// COMMS: the multiplayer map-talk screen. Lifetimes, channels and games are
    /// host-authoritative — on a server only the host's values apply — while everything about
    /// what this player sees and hears (HUD notices, cockpit markers, hotkeys) is client-local.
    /// </summary>
    internal sealed class CommsSettings
    {
        public ConfigEntry<bool> Enabled { get; }

        // ---- host-authoritative
        public ConfigEntry<int> PingSeconds { get; }
        public ConfigEntry<int> StickerSeconds { get; }
        public ConfigEntry<int> DrawingSeconds { get; }
        public ConfigEntry<bool> AllowAllChannel { get; }
        public ConfigEntry<bool> AllowDrawing { get; }
        public ConfigEntry<bool> AllowGames { get; }

        // ---- client-local
        public ConfigEntry<bool> ShowOnMap { get; }
        public ConfigEntry<bool> HudNotices { get; }
        public ConfigEntry<bool> CockpitPingMarkers { get; }
        public ConfigEntry<bool> PingSound { get; }
        public ConfigEntry<KeyCode> DrawHoldKey { get; }
        public ConfigEntry<KeyCode> QuickPingKey { get; }

        public CommsSettings(ConfigFile config)
        {
            const string section = "Comms";
            Enabled = config.Bind(section, "Enabled", true,
                "Run the COM bezel screen: shared map pings, stickers, drawings and labels, brevity " +
                "calls, polls and small games between players. On the host this also stops relaying " +
                "everyone's comms; on a client it hides the screen and the overlays.");

            PingSeconds = config.Bind(section, "PingSeconds", 60,
                new ConfigDescription(
                    "How long a map ping stays up. Host-authoritative.",
                    new AcceptableValueRange<int>(10, 600)));
            StickerSeconds = config.Bind(section, "StickerSeconds", 900,
                new ConfigDescription(
                    "How long stickers and text labels stay on the map. Host-authoritative.",
                    new AcceptableValueRange<int>(30, 3600)));
            DrawingSeconds = config.Bind(section, "DrawingSeconds", 1200,
                new ConfigDescription(
                    "How long drawn strokes and shapes stay on the map. Host-authoritative.",
                    new AcceptableValueRange<int>(30, 3600)));
            AllowAllChannel = config.Bind(section, "AllowAllChannel", true,
                "Let players post to ALL, which the other side sees too. Off keeps every ping, " +
                "drawing, poll and game inside each team. Host-authoritative.");
            AllowDrawing = config.Bind(section, "AllowDrawing", true,
                "Let players draw strokes and shapes on the shared map. Pings, stickers and labels " +
                "stay available either way. Host-authoritative.");
            AllowGames = config.Bind(section, "AllowGames", true,
                "Allow dice, rock-paper-scissors and map hunts. Host-authoritative.");

            ShowOnMap = config.Bind(section, "ShowOnMap", true,
                "Draw shared comms on your tactical map. Client-local.");
            HudNotices = config.Bind(section, "HudNotices", true,
                "Show teammates' pings, calls, polls and game invites as cockpit HUD notices. " +
                "Client-local.");
            CockpitPingMarkers = config.Bind(section, "CockpitPingMarkers", true,
                "Project recent pings into the cockpit view as markers with range, so a teammate's " +
                "SAM call can be found without opening the map. Client-local.");
            PingSound = config.Bind(section, "PingSound", true,
                "Play a short tick when a ping or call arrives. Client-local.");
            DrawHoldKey = config.Bind(section, "DrawHoldKey", KeyCode.LeftShift,
                "Hold this key and left-drag on the open map to draw with the current pen, without " +
                "opening COM first. None disables it. Client-local.");
            QuickPingKey = config.Bind(section, "QuickPingKey", KeyCode.Mouse2,
                "Press this over the open map to drop the selected ping type at the cursor (middle " +
                "mouse by default). None disables it. Client-local.");
        }
    }
}

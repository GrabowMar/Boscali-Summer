using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliSummer.Modules.Cinematography.Configuration
{
    internal sealed class CinematographySettings
    {
        internal ConfigEntry<bool> Enabled { get; }
        internal ConfigEntry<bool> PrivateProductionSession { get; }
        internal ConfigEntry<bool> AllowSimulationSlowMotion { get; }
        internal ConfigEntry<bool> EnableScriptInbox { get; }
        internal ConfigEntry<KeyboardShortcut> ConsoleKey { get; }
        internal ConfigEntry<KeyboardShortcut> CaptureKey { get; }
        internal ConfigEntry<KeyboardShortcut> PlayKey { get; }
        internal ConfigEntry<KeyboardShortcut> BookmarkKey { get; }
        internal CinematographySettings(ConfigFile config)
        {
            const string section = "Cinematography";
            Enabled = config.Bind(section, "Enabled", false, "Install local cinematic shot tools on next startup. No gameplay or network effects.");
            PrivateProductionSession = config.Bind(section, "PrivateProductionSession", false,
                "Explicit opt-in for private host/single-player production only. Requires spectator mode with no local aircraft. Never enables remote observers.");
            AllowSimulationSlowMotion=config.Bind(section,"AllowSimulationSlowMotion",false,
                "Permit real timeScale/physics-step slow motion only in single-player spectator production. Never allowed in multiplayer; restored on every take exit.");
            EnableScriptInbox=config.Bind(section,"EnableScriptInbox",false,
                "Read bounded JSON requests from config/BoscaliSummer/Cinematics/Inbox and write Outbox replies, on Unity's main thread. Private spectator gates still apply; requests are consumed.");
            ConsoleKey = config.Bind(section, "ConsoleKey", new KeyboardShortcut(KeyCode.None), "Assign a non-conflicting shortcut to open the shot console. Escape closes it or aborts playback.");
            CaptureKey = config.Bind(section, "CaptureKey", new KeyboardShortcut(KeyCode.None), "Optional shortcut to append the current native free-camera pose.");
            PlayKey = config.Bind(section, "PlayKey", new KeyboardShortcut(KeyCode.None), "Optional shortcut to play the current shot; Escape always aborts.");
            BookmarkKey = config.Bind(section, "BookmarkKey", new KeyboardShortcut(KeyCode.None), "Optional manual take bookmark shortcut; does not observe gameplay events.");
        }
    }
}

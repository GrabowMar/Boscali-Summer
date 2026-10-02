using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Short feedback through the game's own message feed; the last line is kept for WMC's status strip.</summary>
    internal static class WingToast
    {
        public static string Last { get; private set; }
        public static float LastAt { get; private set; } = float.NegativeInfinity;

        public static void Show(string text)
        {
            Last = text;
            LastAt = Time.unscaledTime;
            MessageUI ui = SceneSingleton<MessageUI>.i;
            if (ui != null) ui.GameMessage(text);
            WingLog.Verbose("[Toast] " + text);
        }
    }
}

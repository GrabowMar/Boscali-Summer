using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>WMC states as stylesheet classes (spec WMC program §2): the rail carries the state, the level class the
    /// fuel/ammo band, and each has a word so colour is never the only carrier.</summary>
    internal static class WmcStyle
    {
        public const float Low = 0.35f, Critical = 0.15f;

        public static string Rail(string state)
        {
            switch (state)
            {
                case "FIGHT":
                case "DEFEND": return "danger";
                case "RTB":
                case "BEHIND":
                case "JOIN": return "armed";
                case "SLOT":
                case "HOLD":
                case "TRAIL": return "ready";
                case "GROUND":
                case "LANDED": return "locked";
                default: return "info";
            }
        }

        /// <summary>A chip state (live, warn, info, danger, inert) as the rail class the stylesheet has (.rail.ready / armed /
        /// info / danger / locked); a rail class passes through.</summary>
        public static string RailOf(string state)
        {
            switch (state)
            {
                case "live": return "ready";
                case "warn": return "armed";
                case "inert": return "locked";
                default: return state;
            }
        }

        public static string Level(float fraction) =>
            float.IsNaN(fraction) ? "" : fraction < Critical ? "bad" : fraction < Low ? "warn" : "ok";
    }
}

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Networking
{
    /// <summary>Which end of a session this game is (review M7b-1 I3): a client runs no server; single player and a host
    /// run both.</summary>
    internal static class NetRole
    {
        public static bool ClientOnly(bool serverActive, bool clientActive) => clientActive && !serverActive;
    }
}

using System.Globalization;

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
    /// <summary>What a field launch answers (spec M3 §3; WMC rebuild §SUPPLY): the type, the field and the pilot, and why the
    /// rest did not go when the launch was cut short.</summary>
    internal static class CallWords
    {
        public static string Launched(int n, string type, string field, string pilot, string cutShort) =>
            (n == 1 ? type : n.ToString(CultureInfo.InvariantCulture) + " × " + type) + " launching from " + field
            + (n == 1 && !string.IsNullOrEmpty(pilot) ? " · " + pilot : "")
            + (string.IsNullOrEmpty(cutShort) ? "" : " (then: " + cutShort + ")");

        public static string Refused(string type, string reason, string field = null) =>
            !string.IsNullOrEmpty(reason) ? "Cannot call " + type + ": " + reason : field + " could not launch " + type;
    }
}

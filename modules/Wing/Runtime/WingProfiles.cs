using System.Collections.Generic;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>One <see cref="AirframeProfile"/> per unit name, built from the aircraft's native numbers and the
    /// data layers on first use. Rejected override keys are logged once per unit.</summary>
    internal static class WingProfiles
    {
        private static readonly Dictionary<string, AirframeProfile> cache = new Dictionary<string, AirframeProfile>();

        public static AirframeProfile For(Aircraft a)
        {
            ProfileInputs inputs;
            try
            {
                inputs = ProfileReader.Read(a);
            }
            catch (System.Exception e)
            {
                // Spec §8: a failed derivation flies the generic profile of the aircraft's class (a helicopter must
                // never get the fixed-wing stack).
                WingLog.Logger.LogWarning("[Profile] could not read the airframe's numbers, using the generic profile: " + e.Message);
                return AirframeProfile.Derive(new ProfileInputs { Class = SafeClassOf(a) });
            }
            string key = inputs.UnitName ?? "generic";
            if (cache.TryGetValue(key, out AirframeProfile p)) return p;
            try
            {
                inputs.LiftStallSpeed = ProfileReader.LiftStallSpeed(a);
            }
            catch (System.Exception e)
            {
                WingLog.Logger.LogWarning("[Profile] could not read " + key + "'s wings: " + e.Message);
            }
            var rejected = new List<string>();
            p = WingData.Profiles.Build(inputs, rejected);
            foreach (string r in rejected) WingLog.Logger.LogWarning("[Data] airframe override ignored: " + r);
            cache[key] = p;
            WingLog.Verbose($"[Profile] {key}: stall {p.StallSpeed:0} m/s (published {inputs.PublishedStallKmh / 3.6f:0}, wings {inputs.LiftStallSpeed:0}), corner {p.CornerSpeed:0}, max {p.MaxSpeed:0}, " +
                $"g {p.GLimit:0.0}, roll {p.RollRateMaxDps:0} deg/s" +
                (p.Class != AirframeClass.FixedWing ? $", hover collective {p.HoverCollective:0.00}" : ""));
            return p;
        }

        public static void Clear() => cache.Clear();

        private static AirframeClass SafeClassOf(Aircraft a)
        {
            try
            {
                return ProfileReader.ClassOf(a);
            }
            catch (System.Exception)
            {
                return AirframeClass.FixedWing;
            }
        }
    }
}

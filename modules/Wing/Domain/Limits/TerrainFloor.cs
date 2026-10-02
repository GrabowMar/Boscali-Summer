using System;

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
    /// <summary>The wing's terrain floor: the highest probe under and ahead of the formation. It rises at once
    /// (the 10 s look-ahead already gives the warning) and falls at most <see cref="FallRate"/>, so the floor
    /// does not dive into every valley between ridges. NaN until the first probe.</summary>
    internal sealed class TerrainFloor
    {
        public static float FallRate = 15f;

        public float Value { get; private set; } = float.NaN;

        public float Update(float raw, float dt)
        {
            if (float.IsNaN(raw)) return Value;
            if (float.IsNaN(Value) || raw >= Value) Value = raw;
            else Value = Math.Max(raw, Value - FallRate * Math.Max(0f, dt));
            return Value;
        }
    }
}

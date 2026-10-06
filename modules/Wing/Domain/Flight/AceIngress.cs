using System;
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
namespace BoscaliSummer.Modules.Wing.Domain
{
    internal static class AceIngress
    {
        internal readonly struct Base
        {
            internal readonly float X, Z;
            internal readonly bool Enemy;
            internal Base(float x, float z, bool enemy) { X = x; Z = z; Enemy = enemy; }
        }
        internal static string Airframe(int tier) => tier == 1 ? "T/A-30" : tier == 2 ? "CT-7" :
            tier == 3 ? "FS-12" : tier == 4 ? "FS-20" : tier == 5 ? "KR-67" : null;

        private static float Distance(float ax, float az, float bx, float bz)
        { float dx = ax - bx, dz = az - bz; return (float)Math.Sqrt(dx * dx + dz * dz); }
    }
}

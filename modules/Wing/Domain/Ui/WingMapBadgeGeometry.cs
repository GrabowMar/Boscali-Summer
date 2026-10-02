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
    /// <summary>Badge dimensions in screen pixels, independent of local map scale.</summary>
    internal readonly struct WingMapBadgeGeometry
    {
        public const float StrokePixels = 1f;
        public const float FeatherPixels = 0.6f;
        public const float IconGapPixels = 2.5f;

        public float InnerRadiusPixels { get; }
        public float OuterRadiusPixels => InnerRadiusPixels + StrokePixels;
        public float CommandHalfExtentPixels => OuterRadiusPixels + 3f;

        private WingMapBadgeGeometry(float innerRadiusPixels)
        {
            InnerRadiusPixels = innerRadiusPixels;
        }

        public static WingMapBadgeGeometry ForIcon(float widthPixels, float heightPixels)
        {
            float width = FiniteSize(widthPixels);
            float height = FiniteSize(heightPixels);
            float halfDiagonal = (float)Math.Sqrt(width * width + height * height) * 0.5f;
            // Enclose every icon heading with clearance at the corners.
            return new WingMapBadgeGeometry(Math.Max(8.5f, halfDiagonal + IconGapPixels));
        }

        private static float FiniteSize(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Abs(value);
    }
}

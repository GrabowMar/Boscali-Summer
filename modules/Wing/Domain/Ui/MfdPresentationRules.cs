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
    internal static class MfdPresentationRules
    {
        internal readonly struct Placement
        {
            public readonly float X, Top, Scale;
            public Placement(float x, float top, float scale)
            {
                X = x;
                Top = top;
                Scale = scale;
            }
        }

        public static Placement FitBesideBezel(float width, float height, bool left,
            float viewportLeft, float viewportRight, float viewportBottom, float viewportTop,
            float bezelLeft, float bezelRight, float gap, float? centerY = null)
        {
            float min = left ? viewportLeft : Math.Max(viewportLeft, bezelRight + gap);
            float max = left ? Math.Min(viewportRight, bezelLeft - gap) : viewportRight;
            float center = centerY ?? (viewportTop + viewportBottom) * 0.5f;
            // Fit around the map's visual centre using the smaller vertical clearance; asymmetric
            // reserves must not shift it.
            float availableHeight = 2f * Math.Min(viewportTop - center, center - viewportBottom);
            float scale = FitScale(width, height, max - min, availableHeight);
            // Position beside the native button column instead of an off-screen prefab origin.
            float top = center + height * scale * 0.5f;
            return new Placement(left ? max - width * scale : min, top, scale);
        }

        // Defer installation when dimensions are invalid; an invisible or enormous screen must not
        // capture map input.
        public static float FitScale(float width, float height, float availableWidth, float availableHeight)
        {
            if (!PositiveFinite(width) || !PositiveFinite(height) ||
                !PositiveFinite(availableWidth) || !PositiveFinite(availableHeight)) return 0f;
            return Math.Min(availableWidth / width, availableHeight / height);
        }

        private static bool PositiveFinite(float value) =>
            value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
    }
}

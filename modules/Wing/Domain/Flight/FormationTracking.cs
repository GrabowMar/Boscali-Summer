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
    /// <summary>Engine-free continuous formation target motion.</summary>
    internal static class FormationTracking
    {

        // Integrate constant-rate turn velocity instead of rotating velocity times duration, which
        // doubles shallow-turn lateral lead. Bound sweep to avoid predicted loops.
        public static float Sweep(float turnRate, float seconds) =>
            Math.Max(-(float)Math.PI / 2f, Math.Min((float)Math.PI / 2f,
                turnRate * Math.Max(0f, seconds)));

        public static (float x, float y, float z) Arc(
            float vx, float vy, float vz, float turnRate, float seconds)
        {
            double time = Math.Max(0f, seconds);
            double angle = Sweep(turnRate, seconds);
            double squared = angle * angle;
            double sinc = Math.Abs(angle) < 0.001d
                ? 1d - squared / 6d + squared * squared / 120d : Math.Sin(angle) / angle;
            double cosc = Math.Abs(angle) < 0.001d
                ? angle * (0.5d - squared / 24d + squared * squared / 720d)
                : (1d - Math.Cos(angle)) / angle;
            return ((float)(time * (vx * sinc + vz * cosc)),
                    (float)(time * vy),
                    (float)(time * (vz * sinc - vx * cosc)));
        }

        // Cubic Hermite capture follows current velocity at departure and future slot velocity at
        // arrival. Bound tangents by gap to prevent loops.
        public static (float x, float z) Capture(
            float targetX, float targetZ, float ownVx, float ownVz,
            float slotVx, float slotVz, float seconds, float previewSeconds)
        {
            double time = Math.Max(0.001f, seconds);
            double t = Math.Max(0d, Math.Min(1d, previewSeconds / time));
            double distance = Math.Sqrt((double)targetX * targetX + (double)targetZ * targetZ);
            double ownScale = Math.Min(time, distance / Math.Max(1d,
                Math.Sqrt((double)ownVx * ownVx + (double)ownVz * ownVz)));
            double slotScale = Math.Min(time, distance / Math.Max(1d,
                Math.Sqrt((double)slotVx * slotVx + (double)slotVz * slotVz)));
            // Fade tangents outside the forward cone so rearward rendezvous can enter the heading
            // limiter instead of keeping preview ahead indefinitely.
            double alignment = ((double)targetX * ownVx + (double)targetZ * ownVz) /
                Math.Max(1d, distance * Math.Sqrt((double)ownVx * ownVx + (double)ownVz * ownVz));
            double tangentBlend = Math.Max(0d, Math.Min(1d, alignment * 4d));
            tangentBlend *= tangentBlend * (3d - 2d * tangentBlend);
            ownScale *= tangentBlend;
            slotScale *= tangentBlend;
            double h10 = t * (1d - t) * (1d - t);
            double h01 = t * t * (3d - 2d * t);
            double h11 = t * t * (t - 1d);
            return ((float)(h10 * ownVx * ownScale + h01 * targetX + h11 * slotVx * slotScale),
                    (float)(h10 * ownVz * ownScale + h01 * targetZ + h11 * slotVz * slotScale));
        }
    }
}

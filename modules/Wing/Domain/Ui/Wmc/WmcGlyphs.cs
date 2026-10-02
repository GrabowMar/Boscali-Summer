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
    /// <summary>The WMC's drawn symbols (spec FUI §kit): the MFD font has no symbol shapes, so every icon is line segments.</summary>
    internal enum Glyph : byte
    {
        None,
        // Orders.
        Attack, MyTarget, Splash, Engage, Disengage, ClearSix, Ecm, FormUp, Move, Orbit, Hold, Patrol, Cap, Sweep, Scout, Escort,
        Rtb, Refit, Land, Cargo, TakeOff, Rescue, Detach, Call, Bogey, Dismiss, React,
        // Alerts and sections.
        Missile, Warn, Fuel, Ammo, Lost, Behind, Plane, Threat, Pool, Formation, Route, Pilot, Gear, Base, Pylon, Check, Info,
        Record, Plan, Tuning, Posture, Clear,
    }

    /// <summary>Glyph geometry as line segments (x0, y0, x1, y1) in unit space: −1..1 on both axes, +Y up. The view scales them into
    /// a box and strokes them; nothing here knows about Unity.</summary>
    internal static class WmcGlyphs
    {
        /// <summary>Floats a glyph can need (segments × 4).</summary>
        public const int MaxFloats = 64 * 4;

        private const float Pi = (float)Math.PI;

        /// <summary>Writes <paramref name="g"/>'s segments into <paramref name="into"/>; returns the segment count.</summary>
        public static int Segments(Glyph g, float[] into)
        {
            var w = new Writer(into);
            switch (g)
            {
                case Glyph.Attack:
                    w.Ring(0f, 0f, 0.62f, 16);
                    w.L(0f, 0.95f, 0f, 0.35f); w.L(0f, -0.95f, 0f, -0.35f); w.L(0.95f, 0f, 0.35f, 0f); w.L(-0.95f, 0f, -0.35f, 0f);
                    break;
                case Glyph.MyTarget:
                    w.Diamond(0f, 0f, 0.85f);
                    w.Diamond(0f, 0f, 0.25f);
                    break;
                case Glyph.Splash:
                    w.Ring(0f, 0f, 0.85f, 16);
                    w.Ring(0f, 0f, 0.4f, 10);
                    break;
                case Glyph.Engage:
                    w.L(-0.8f, -0.8f, 0.8f, 0.8f); w.L(-0.8f, 0.8f, 0.8f, -0.8f);
                    w.L(0.8f, 0.8f, 0.35f, 0.8f); w.L(-0.8f, 0.8f, -0.35f, 0.8f);
                    break;
                case Glyph.Disengage:
                    w.Arc(0f, 0f, 0.7f, 30f, 300f, 12);
                    w.Chevron(0.61f, 0.35f, 0.4f, 120f);
                    break;
                case Glyph.ClearSix:
                    w.Ring(0f, 0f, 0.8f, 16);
                    w.L(0f, 0f, 0f, -0.8f);
                    w.L(-0.25f, -0.55f, 0f, -0.8f); w.L(0.25f, -0.55f, 0f, -0.8f);
                    break;
                case Glyph.Ecm:
                    w.Zig(-0.9f, 0.45f, 0.9f, 4, 0.2f);
                    w.Zig(-0.9f, 0f, 0.9f, 4, 0.2f);
                    w.Zig(-0.9f, -0.45f, 0.9f, 4, 0.2f);
                    break;
                case Glyph.FormUp:
                case Glyph.Formation:
                    w.Diamond(0f, 0.5f, 0.3f); w.Diamond(-0.55f, -0.3f, 0.3f); w.Diamond(0.55f, -0.3f, 0.3f);
                    break;
                case Glyph.Move:
                    w.L(-0.9f, 0f, 0.7f, 0f);
                    w.Chevron(0.5f, 0f, 0.8f, 0f);
                    break;
                case Glyph.Orbit:
                    w.Ring(0f, 0f, 0.75f, 16);
                    w.Chevron(0.75f, 0f, 0.45f, 90f);
                    break;
                case Glyph.Hold:
                    w.L(-0.35f, -0.8f, -0.35f, 0.8f); w.L(0.35f, -0.8f, 0.35f, 0.8f);
                    break;
                case Glyph.Patrol:
                    w.DashedRing(0f, 0f, 0.8f, 8);
                    w.Diamond(0f, 0f, 0.2f);
                    break;
                case Glyph.Cap:
                    w.Box(-0.8f, -0.8f, 0.8f, 0.8f);
                    w.Diamond(0f, 0f, 0.25f);
                    break;
                case Glyph.Sweep:
                    w.L(-0.9f, 0.55f, 0.9f, 0.55f); w.L(-0.6f, 0f, 0.9f, 0f); w.L(-0.9f, -0.55f, 0.9f, -0.55f);
                    break;
                case Glyph.Scout:
                    w.Ring(-0.2f, 0.2f, 0.55f, 12);
                    w.L(0.2f, -0.2f, 0.85f, -0.85f);
                    break;
                case Glyph.Escort:
                    w.L(-0.75f, 0.8f, 0.75f, 0.8f); w.L(-0.75f, 0.8f, -0.75f, 0.1f); w.L(0.75f, 0.8f, 0.75f, 0.1f);
                    w.L(-0.75f, 0.1f, 0f, -0.9f); w.L(0.75f, 0.1f, 0f, -0.9f);
                    break;
                case Glyph.Rtb:
                case Glyph.Base:
                    w.L(-0.9f, 0f, 0f, 0.85f); w.L(0f, 0.85f, 0.9f, 0f);
                    w.L(-0.6f, 0.05f, -0.6f, -0.85f); w.L(0.6f, 0.05f, 0.6f, -0.85f); w.L(-0.6f, -0.85f, 0.6f, -0.85f);
                    break;
                case Glyph.Refit:
                    w.Arc(0f, 0f, 0.7f, 60f, 340f, 12);
                    w.Chevron(0.35f, 0.61f, 0.4f, 150f);
                    break;
                case Glyph.Land:
                    w.Chevron(0f, 0.1f, 0.9f, -90f);
                    w.L(-0.9f, -0.8f, 0.9f, -0.8f);
                    break;
                case Glyph.TakeOff:
                    w.Chevron(0f, 0.2f, 0.9f, 90f);
                    w.L(-0.9f, -0.8f, 0.9f, -0.8f);
                    break;
                case Glyph.Cargo:
                    w.Box(-0.75f, -0.75f, 0.75f, 0.75f);
                    w.L(-0.75f, 0.75f, 0.75f, -0.75f); w.L(-0.75f, -0.75f, 0.75f, 0.75f);
                    break;
                case Glyph.Rescue:
                    w.Box(-0.25f, -0.85f, 0.25f, 0.85f);
                    w.Box(-0.85f, -0.25f, 0.85f, 0.25f);
                    break;
                case Glyph.Detach:
                    w.L(0f, -0.9f, 0f, 0f); w.L(0f, 0f, -0.7f, 0.8f); w.L(0f, 0f, 0.7f, 0.8f);
                    break;
                case Glyph.Call:
                    w.L(-0.8f, 0f, 0.8f, 0f); w.L(0f, -0.8f, 0f, 0.8f);
                    break;
                case Glyph.Bogey:
                    w.Arc(-0.8f, -0.8f, 1.5f, 0f, 90f, 8);
                    w.Arc(-0.8f, -0.8f, 0.85f, 0f, 90f, 6);
                    w.L(-0.8f, -0.8f, 0.3f, 0.3f);
                    break;
                case Glyph.Dismiss:
                    w.L(-0.75f, -0.75f, 0.75f, 0.75f); w.L(-0.75f, 0.75f, 0.75f, -0.75f);
                    break;
                case Glyph.React:
                    w.L(-0.8f, -0.8f, -0.1f, 0.1f); w.L(-0.1f, 0.1f, 0.2f, -0.2f); w.L(0.2f, -0.2f, 0.85f, 0.85f);
                    w.Chevron(0.7f, 0.7f, 0.45f, 45f);
                    break;
                case Glyph.Missile:
                case Glyph.Threat:
                    w.L(0f, 0.9f, -0.8f, -0.75f); w.L(-0.8f, -0.75f, 0.8f, -0.75f); w.L(0.8f, -0.75f, 0f, 0.9f);
                    break;
                case Glyph.Warn:
                    w.L(0f, 0.9f, -0.85f, -0.75f); w.L(-0.85f, -0.75f, 0.85f, -0.75f); w.L(0.85f, -0.75f, 0f, 0.9f);
                    w.L(0f, 0.35f, 0f, -0.2f); w.L(0f, -0.4f, 0f, -0.5f);
                    break;
                case Glyph.Fuel:
                    w.Box(-0.5f, -0.85f, 0.5f, 0.85f);
                    w.L(-0.5f, -0.1f, 0.5f, -0.1f); w.L(-0.5f, -0.45f, 0.5f, -0.45f);
                    break;
                case Glyph.Ammo:
                    w.L(-0.5f, -0.8f, -0.5f, 0.5f); w.L(0f, -0.8f, 0f, 0.5f); w.L(0.5f, -0.8f, 0.5f, 0.5f);
                    w.Chevron(-0.5f, 0.6f, 0.3f, 90f); w.Chevron(0f, 0.6f, 0.3f, 90f); w.Chevron(0.5f, 0.6f, 0.3f, 90f);
                    break;
                case Glyph.Lost:
                    w.Ring(0f, 0f, 0.85f, 16);
                    w.L(-0.5f, -0.5f, 0.5f, 0.5f); w.L(-0.5f, 0.5f, 0.5f, -0.5f);
                    break;
                case Glyph.Behind:
                    w.Chevron(-0.3f, 0f, 0.7f, 180f); w.Chevron(0.3f, 0f, 0.7f, 180f);
                    break;
                case Glyph.Plane:
                    w.L(0f, 0.9f, 0f, -0.8f); w.L(-0.9f, 0.1f, 0.9f, 0.1f); w.L(-0.35f, -0.75f, 0.35f, -0.75f);
                    break;
                case Glyph.Pool:
                    w.Box(-0.8f, 0.35f, 0.8f, 0.8f); w.Box(-0.8f, -0.25f, 0.8f, 0.2f); w.Box(-0.8f, -0.85f, 0.8f, -0.4f);
                    break;
                case Glyph.Route:
                    w.L(-0.85f, -0.7f, -0.2f, 0.5f); w.L(-0.2f, 0.5f, 0.3f, -0.3f); w.L(0.3f, -0.3f, 0.85f, 0.7f);
                    w.Diamond(-0.85f, -0.7f, 0.15f); w.Diamond(0.85f, 0.7f, 0.15f);
                    break;
                case Glyph.Pilot:
                    w.Ring(0f, 0.4f, 0.38f, 12);
                    w.Arc(0f, -0.9f, 0.8f, 20f, 160f, 8);
                    break;
                case Glyph.Gear:
                    w.Ring(0f, 0f, 0.5f, 12);
                    w.L(0f, 0.55f, 0f, 0.9f); w.L(0f, -0.55f, 0f, -0.9f); w.L(0.55f, 0f, 0.9f, 0f); w.L(-0.55f, 0f, -0.9f, 0f);
                    break;
                case Glyph.Pylon:
                    w.L(-0.9f, 0.8f, 0.9f, 0.8f); w.L(0f, 0.8f, 0f, 0.35f);
                    w.Box(-0.3f, -0.85f, 0.3f, 0.35f);
                    break;
                case Glyph.Check:
                    w.L(-0.8f, 0f, -0.25f, -0.6f); w.L(-0.25f, -0.6f, 0.85f, 0.7f);
                    break;
                case Glyph.Info:
                    w.Ring(0f, 0f, 0.85f, 16);
                    w.L(0f, -0.5f, 0f, 0.1f); w.L(0f, 0.35f, 0f, 0.45f);
                    break;
                case Glyph.Record:
                    w.L(-0.8f, 0.6f, 0.8f, 0.6f); w.L(-0.8f, 0f, 0.5f, 0f); w.L(-0.8f, -0.6f, 0.7f, -0.6f);
                    break;
                case Glyph.Plan:
                    w.Box(-0.85f, 0.35f, -0.45f, 0.75f); w.L(-0.25f, 0.55f, 0.85f, 0.55f);
                    w.Box(-0.85f, -0.75f, -0.45f, -0.35f); w.L(-0.25f, -0.55f, 0.85f, -0.55f);
                    break;
                case Glyph.Tuning:
                case Glyph.Posture:
                    w.L(-0.85f, 0.45f, 0.85f, 0.45f); w.L(-0.85f, -0.45f, 0.85f, -0.45f);
                    w.Box(-0.45f, 0.25f, -0.15f, 0.65f); w.Box(0.25f, -0.65f, 0.55f, -0.25f);
                    break;
                case Glyph.Clear:
                    w.Ring(0f, 0f, 0.85f, 16);
                    w.L(-0.45f, 0f, -0.1f, -0.35f); w.L(-0.1f, -0.35f, 0.5f, 0.35f);
                    break;
            }
            return w.Count;
        }

        /// <summary>Appends segments into a caller-owned array; stops quietly when it is full.</summary>
        private struct Writer
        {
            private readonly float[] into;
            public int Count;

            public Writer(float[] into)
            {
                this.into = into;
                Count = 0;
            }

            public void L(float x0, float y0, float x1, float y1)
            {
                int i = Count * 4;
                if (into == null || i + 4 > into.Length) return;
                into[i] = x0; into[i + 1] = y0; into[i + 2] = x1; into[i + 3] = y1;
                Count++;
            }

            public void Arc(float cx, float cy, float r, float fromDeg, float toDeg, int segments)
            {
                float step = (toDeg - fromDeg) / segments;
                for (int k = 0; k < segments; k++)
                {
                    float a0 = (fromDeg + k * step) * Pi / 180f, a1 = (fromDeg + (k + 1) * step) * Pi / 180f;
                    L(cx + r * (float)Math.Cos(a0), cy + r * (float)Math.Sin(a0), cx + r * (float)Math.Cos(a1), cy + r * (float)Math.Sin(a1));
                }
            }

            public void Ring(float cx, float cy, float r, int segments) => Arc(cx, cy, r, 0f, 360f, segments);

            public void DashedRing(float cx, float cy, float r, int dashes)
            {
                float span = 360f / dashes;
                for (int k = 0; k < dashes; k++) Arc(cx, cy, r, k * span, k * span + span * 0.55f, 2);
            }

            public void Diamond(float cx, float cy, float r)
            {
                L(cx + r, cy, cx, cy + r); L(cx, cy + r, cx - r, cy); L(cx - r, cy, cx, cy - r); L(cx, cy - r, cx + r, cy);
            }

            public void Box(float x0, float y0, float x1, float y1)
            {
                L(x0, y0, x1, y0); L(x1, y0, x1, y1); L(x1, y1, x0, y1); L(x0, y1, x0, y0);
            }

            /// <summary>An open V pointing along <paramref name="angleDeg"/>, <paramref name="size"/> across, tip ahead of the centre.</summary>
            public void Chevron(float cx, float cy, float size, float angleDeg)
            {
                float a = angleDeg * Pi / 180f, dx = (float)Math.Cos(a), dy = (float)Math.Sin(a), h = size * 0.5f;
                float tx = cx + dx * h, ty = cy + dy * h, bx = cx - dx * h, by = cy - dy * h;
                L(bx - dy * h, by + dx * h, tx, ty);
                L(bx + dy * h, by - dx * h, tx, ty);
            }

            public void Zig(float x0, float y, float x1, int teeth, float amp)
            {
                float step = (x1 - x0) / (teeth * 2);
                for (int k = 0; k < teeth * 2; k++)
                    L(x0 + k * step, y + (k % 2 == 0 ? -amp : amp), x0 + (k + 1) * step, y + (k % 2 == 0 ? amp : -amp));
            }
        }
    }
}

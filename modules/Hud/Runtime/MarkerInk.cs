using System.Runtime.CompilerServices;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// THE RANGE INK on the game's unit markers (AV LAYER 2.3): each marker's colour desaturates toward its own luma and dims with distance (<see cref="HudInk.Range"/>, 16 steps,
    /// so the colour write, and the mesh rebuild, lands only on a step change). Re-tint only: never sprite, material, size, position or <c>image.enabled</c>.
    /// The base colour is what the game (and the wing tint after it) last set in <c>UpdateColor</c> (<c>Patches/MarkerPatches.cs</c> -> <see cref="Capture"/>); an rgb some
    /// other writer put on the image since (the wing tint's re-assert, the end of the 1 s fresh flash) becomes the base, alpha kept. Untouched (the game owns them): selected,
    /// fresh, flashing (an inbound missile), hidden, any jamming. Full colour: a unit locking us right now (the "on you" fact is never faded).
    /// Called from <see cref="ThirdPersonHudCenter"/>'s repair, between the marker's projection and its jamming distortion. Allocation-free.
    /// </summary>
    internal static class MarkerInk
    {
        private sealed class Ink
        {
            public Color Base, Written;
            public bool Wrote;
        }

        private static readonly AccessTools.FieldRef<HUDUnitMarker, bool> Flashing = NativeRefs.Field<HUDUnitMarker, bool>("flashing");
        private static readonly AccessTools.FieldRef<HUDUnitMarker, bool> Hidden = NativeRefs.Field<HUDUnitMarker, bool>("hidden");
        private static readonly ConditionalWeakTable<HUDUnitMarker, Ink> Table = new ConditionalWeakTable<HUDUnitMarker, Ink>();
        private static readonly ConditionalWeakTable<HUDUnitMarker, Ink>.CreateValueCallback Make =
            m => new Ink { Base = m.image != null ? m.image.color : Color.white };

        // Radars locking us (Aircraft.onRadarWarning with isTarget), newest per emitter: held HudThreats.LockHold like the LOCK lamp.
        private const int MaxLocks = 8;
        private static readonly Unit[] lockUnits = new Unit[MaxLocks];
        private static readonly float[] lockAt = new float[MaxLocks];
        private static float lastLock = float.NegativeInfinity;
        private static Aircraft watched;

        /// <summary>The game's (and the wing tint's) colour for this marker, just set by <c>UpdateColor</c>.</summary>
        public static void Capture(HUDUnitMarker marker)
        {
            if (marker == null || marker.image == null) return;
            Ink ink = Table.GetValue(marker, Make);
            ink.Base = ink.Written = marker.image.color;
            ink.Wrote = true;
        }

        /// <summary>Follow <paramref name="aircraft"/>'s radar warnings (null lets go).</summary>
        public static void Watch(Aircraft aircraft)
        {
            if (ReferenceEquals(watched, aircraft)) return;
            if (watched != null) watched.onRadarWarning -= OnRadarWarning;
            System.Array.Clear(lockUnits, 0, MaxLocks);
            lastLock = float.NegativeInfinity;
            watched = aircraft;
            if (aircraft != null) aircraft.onRadarWarning += OnRadarWarning;
        }

        public static void Apply(HUDUnitMarker marker, FactionHQ hq, GlobalPosition view, float jam)
        {
            Image image = marker.image;
            if (jam > 0f || image == null || !image.enabled || marker.selected || marker.fresh || marker.unit == null ||
                NativeRefs.Get(Flashing, marker) || NativeRefs.Get(Hidden, marker)) return;
            Ink ink = Table.GetValue(marker, Make);
            Color now = image.color;
            if (ink.Wrote && (now.r != ink.Written.r || now.g != ink.Written.g || now.b != ink.Written.b))
                ink.Base = new Color(now.r, now.g, now.b, ink.Base.a);      // another writer since ours: its rgb is the base now

            Color target = ink.Base;
            if (!LockingUs(marker.unit))
            {
                GlobalPosition known = marker.unit.GlobalPosition();
                if (marker.outdated && (hq == null || !hq.TryGetKnownPosition(marker.unit, out known))) return;
                float metres = FastMath.Distance(view, known);
                if (HudInk.RangeStep(metres) != 0)
                {
                    Rgba r = HudInk.Range(new Rgba(target.r, target.g, target.b, target.a), metres);
                    target = new Color(r.R, r.G, r.B, r.A);
                }
            }
            image.color = target;      // Graphic.color ignores an equal value: a rebuild only on a step change
            ink.Written = target;
            ink.Wrote = true;
        }

        private static bool LockingUs(Unit unit)
        {
            float now = Time.unscaledTime;
            if (now - lastLock >= HudThreats.LockHold) return false;
            for (int i = 0; i < MaxLocks; i++)
                if (ReferenceEquals(lockUnits[i], unit) && now - lockAt[i] < HudThreats.LockHold) return true;
            return false;
        }

        private static void OnRadarWarning(Aircraft.OnRadarWarning e)
        {
            if (!e.isTarget || e.emitter == null || e.emitter is Missile) return;
            float now = Time.unscaledTime;
            int slot = -1, oldest = 0;
            for (int i = 0; i < MaxLocks; i++)
            {
                if (ReferenceEquals(lockUnits[i], e.emitter)) { slot = i; break; }
                if (lockAt[i] < lockAt[oldest]) oldest = i;
            }
            if (slot < 0) slot = oldest;
            lockUnits[slot] = e.emitter;
            lockAt[slot] = now;
            lastLock = now;
        }
    }
}

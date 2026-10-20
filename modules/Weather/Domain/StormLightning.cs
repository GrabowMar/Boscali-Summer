using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal struct LightningEvent
    {
        internal float X, Y, Z, BottomY, Time;
        /// <summary>The storm's cloud top (m): tall storms throw sprites and jets at night.</summary>
        internal float Top;
        /// <summary>The storm's cloud base (m): only the channel below it is drawn (BottomY below Base is a ground strike).</summary>
        internal float Base;
        internal uint Seed;
    }

    /// <summary>
    /// Cosmetic source events derived from lattice cells and mission-time slots. An event is a pure function of the source (a cell's
    /// seed, or a set-piece's layout seed and bit) and the slot, never of an observer: the observer only selects which events this peer
    /// renders (<see cref="StormLattice.Enumerate"/>).
    /// </summary>
    internal static class StormLightning
    {
        internal const float SlotSeconds = 2f;

        /// <summary>Strikes per minute over the cell's own rate (2026-10-18, user: storms need more thunder): every peer runs the same pure
        /// rule, so they still see the same strikes.</summary>
        internal const float StrikeBoost = 2f;

        /// <summary>The strike of a storm cell in a 2 s slot, in world metres (the cell lives in the tape frame).</summary>
        internal static bool TryCell(in StormCell cell, in TapeFrame frame, int slot, out LightningEvent strike)
        {
            frame.FromTape(cell.X, cell.Z, out float x, out float z);
            return TryEvent(x, z, cell.Radius, cell.Top, cell.Base, cell.LightningRate, cell.Seed, slot, out strike);
        }

        /// <summary>The strike of a set-piece storm in a slot. Seeds derive from the layout seed and the console bit (spec 6.1).</summary>
        internal static bool TryHero(in Superstructure s, uint layoutSeed, int slot, out LightningEvent strike)
        {
            strike = default;
            if (s.Kind == SuperstructureKind.Lenticulars || s.Strength < 0.3f) return false;
            float radius = s.Kind == SuperstructureKind.ShelfLine ? s.Size * 0.6f : s.Size;
            uint seed = layoutSeed ^ unchecked((uint)(11939 * ((int)s.Set + 1)));
            return TryEvent(s.X, s.Z, radius, s.Top, 1300f, 2f + 6f * s.Strength, seed, slot, out strike);
        }

        private static bool TryEvent(float x, float z, float radius, float top, float bottom, float rate, uint seed, int slot, out LightningEvent strike)
        {
            strike = default;
            if (slot < 0 || rate <= 0f || WeatherMath.Hash01(seed, slot, 91) >= Math.Min(1f, rate * StrikeBoost * SlotSeconds / 60f)) return false;
            strike = new LightningEvent
            {
                X = x + WeatherMath.HashRange(seed, slot, 92, 0, -radius * 0.4f, radius * 0.4f),
                Z = z + WeatherMath.HashRange(seed, slot, 93, 0, -radius * 0.4f, radius * 0.4f),
                Y = bottom + (top - bottom) * WeatherMath.HashRange(seed, slot, 94, 0, 0.35f, 0.7f),
                BottomY = WeatherMath.Hash01(seed, slot, 95) < 0.25f ? 40f : bottom,
                Time = slot * SlotSeconds + WeatherMath.HashRange(seed, slot, 96, 0, 0.02f, 1.98f),
                Top = top,
                Base = bottom,
                Seed = seed ^ unchecked((uint)slot * 2654435761u),
            };
            return true;
        }
    }
}

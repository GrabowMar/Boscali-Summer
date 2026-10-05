using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal struct LightningEvent
    {
        internal float X, Y, Z, BottomY, Time;
        internal uint Seed;
    }

    /// <summary>Cosmetic source events derived from static sites and mission-time slots.</summary>
    internal static class StormLightning
    {
        internal const float SlotSeconds = 2f;

        internal static bool TryEvent(WeatherField field, int source, int slot, out LightningEvent strike)
        {
            strike = default;
            if (field == null || !field.IsBuilt || slot < 0 || source < 0) return false;
            float x, z, radius, top, bottom, rate;
            uint seed;
            if (source < field.CellCount)
            {
                StormCell c = field.Cell(source);
                x = c.X; z = c.Z; radius = c.Radius; top = c.Top; bottom = c.Base;
                rate = c.LightningRate;
                seed = c.Seed;
            }
            else
            {
                int index = source - field.CellCount;
                if (index >= field.SuperstructureCount) return false;
                Superstructure s = field.SuperstructureAt(index);
                if (s.Kind == SuperstructureKind.Lenticulars || s.Strength < 0.3f) return false;
                x = s.X; z = s.Z; radius = s.Kind == SuperstructureKind.ShelfLine ? s.Size * 0.6f : s.Size;
                top = s.Top; bottom = 1300f;
                rate = 2f + 6f * s.Strength;
                seed = field.Timeline.Layout ^ unchecked((uint)(11939 * ((int)s.Set + 1)));
            }
            if (rate <= 0f || WeatherMath.Hash01(seed, slot, 91) >= Math.Min(1f, rate * SlotSeconds / 60f)) return false;
            strike = new LightningEvent
            {
                X = x + WeatherMath.HashRange(seed, slot, 92, 0, -radius * 0.4f, radius * 0.4f),
                Z = z + WeatherMath.HashRange(seed, slot, 93, 0, -radius * 0.4f, radius * 0.4f),
                Y = bottom + (top - bottom) * WeatherMath.HashRange(seed, slot, 94, 0, 0.35f, 0.7f),
                BottomY = WeatherMath.Hash01(seed, slot, 95) < 0.25f ? 40f : bottom,
                Time = slot * SlotSeconds + WeatherMath.HashRange(seed, slot, 96, 0, 0.02f, 1.98f),
                Seed = seed ^ unchecked((uint)slot * 2654435761u),
            };
            return true;
        }
    }
}

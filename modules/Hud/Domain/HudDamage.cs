using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    [Flags]
    internal enum DamageFlags : byte { None = 0, EngineDamaged = 1, FuelLeak = 2, Fire = 4 }

    /// <summary>The aircraft's damage as the board's damage lamps need it (read from the game's own events and parts).</summary>
    internal struct DamageState
    {
        public DamageFlags Flags;
        /// <summary>Engines the aircraft has, and how many have been knocked out (<c>OnEngineDisable</c>).</summary>
        public int Engines, EnginesOut;
        /// <summary>Share of the airframe's parts that have broken off (<c>PartDamageTracker.GetDetachedRatio</c>), 0..1.</summary>
        public float DetachedRatio;
    }

    /// <summary>
    /// Persistent damage cautions (2026-10-10), pure. The vanilla damage silhouette fades ten seconds after a hit; these
    /// lamps stay lit for as long as the condition does: FIRE, ENG OUT, FUEL LEAK, STR n% (the structure that is left) and ENG
    /// DMG (a hit that has not yet killed an engine). Severity order, at most <see cref="MaxLamps"/>; a condition that
    /// is not present has no lamp at all.
    /// </summary>
    internal static class HudDamage
    {
        public const int MaxLamps = HudBoardLayout.Tiles;
        /// <summary>Structure lost at or beyond this share cautions; beyond <see cref="StructureWarning"/> it warns.</summary>
        public const float StructureCaution = 0.10f, StructureWarning = 0.25f;

        /// <summary>Fill <paramref name="lamps"/> (length at least <see cref="MaxLamps"/>) and return how many are lit.</summary>
        public static int Fill(in DamageState s, Lamp[] lamps)
        {
            int n = 0;
            if ((s.Flags & DamageFlags.Fire) != 0) lamps[n++] = new Lamp { Legend = "FIRE", Present = true, State = LampState.Warning, Flash = true };
            bool engineOut = s.EnginesOut > 0;
            if (engineOut && n < MaxLamps)
                lamps[n++] = new Lamp
                {
                    Legend = s.Engines > 1 && s.EnginesOut < s.Engines ? "ENG " + s.EnginesOut + " OUT" : "ENG OUT",
                    Present = true, State = LampState.Warning,
                };
            if ((s.Flags & DamageFlags.FuelLeak) != 0 && n < MaxLamps) lamps[n++] = new Lamp { Legend = "FUEL LEAK", Present = true, State = LampState.Caution };
            if (s.DetachedRatio >= StructureCaution && n < MaxLamps)
            {
                int left = (int)Math.Round((1f - Math.Min(1f, s.DetachedRatio)) * 100f, MidpointRounding.AwayFromZero);
                lamps[n++] = new Lamp
                {
                    Legend = "STR " + left + "%", Present = true,
                    State = s.DetachedRatio >= StructureWarning ? LampState.Warning : LampState.Caution,
                };
            }
            if ((s.Flags & DamageFlags.EngineDamaged) != 0 && !engineOut && n < MaxLamps)
                lamps[n++] = new Lamp { Legend = "ENG DMG", Present = true, State = LampState.Caution };
            for (int i = n; i < lamps.Length; i++) lamps[i] = default;
            return n;
        }
    }
}

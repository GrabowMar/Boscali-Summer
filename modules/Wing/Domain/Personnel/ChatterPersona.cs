using System;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>How a pilot talks on the radio: each has its own lines for every event in the chatter packs.</summary>
    internal enum ChatterPersona { Veteran, Rookie, Hothead, Ice, Joker, Book }

    internal static class ChatterPersonas
    {
        public const int Count = 6;
        private static readonly string[] Keys = { "veteran", "rookie", "hothead", "ice", "joker", "book" };

        /// <summary>The name a pack line is tagged with.</summary>
        public static string Key(ChatterPersona persona) => Keys[Math.Max(0, Math.Min(Count - 1, (int)persona))];

        /// <summary>A saved name back to a persona. Pilots saved before 2026-10-13 carry one of the four old names; junk reads as Veteran.</summary>
        public static ChatterPersona Parse(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "professional": return ChatterPersona.Veteran;
                case "aggressive": return ChatterPersona.Hothead;
                case "calm": return ChatterPersona.Ice;
                case "dry": return ChatterPersona.Joker;
            }
            return Enum.TryParse(name, true, out ChatterPersona parsed) && Enum.IsDefined(typeof(ChatterPersona), parsed) ? parsed : ChatterPersona.Veteran;
        }
    }
}

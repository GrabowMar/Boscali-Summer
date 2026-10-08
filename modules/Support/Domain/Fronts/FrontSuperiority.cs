using System;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    /// <summary>The counts one side contributes to the superiority formulas (spec 3.3). A front only reads the fields it names.</summary>
    internal struct FrontSides
    {
        /// <summary>SPACE: satellites up (0..3).</summary>
        public int BirdsUp;
        /// <summary>SPACE: uplinks live / total.</summary>
        public int UplinksLive, UplinksTotal;
        /// <summary>SPACE: this side's CYBER currently jams an opposing satellite.</summary>
        public bool JamsOpponent;
        /// <summary>CYBER: opposing nodes this side holds (the enemy's value is "enemy-held own nodes").</summary>
        public int HeldEnemyNodes;
        /// <summary>CYBER: anchors (data centers, trucks) live.</summary>
        public int AnchorsLive;
        /// <summary>CYBER: trace against this side, 0..1 (balance = enemy trace - own trace).</summary>
        public float Trace;
        /// <summary>SOF: teams afield, buildings held, camps live.</summary>
        public int TeamsAfield, HeldBuildings, CampsLive;
    }

    internal static class FrontSuperiority
    {
        public static int Compute(Front front, in FrontSides own, in FrontSides enemy)
        {
            float sum;
            switch (front)
            {
                case Front.Space:
                    sum = Clamp(40f * (own.BirdsUp - enemy.BirdsUp) / 3f, 40f) +
                          Clamp(30f * (Frac(own.UplinksLive, own.UplinksTotal) - Frac(enemy.UplinksLive, enemy.UplinksTotal)), 30f) +
                          Clamp(30f * ((own.JamsOpponent ? 1f : 0f) - (enemy.JamsOpponent ? 1f : 0f)), 30f);
                    break;
                case Front.Cyber:
                    sum = Clamp(50f * (own.HeldEnemyNodes - enemy.HeldEnemyNodes) / 4f, 50f) +
                          Clamp(30f * (own.AnchorsLive - enemy.AnchorsLive) / 4f, 30f) +
                          Clamp(20f * (enemy.Trace - own.Trace), 20f);
                    break;
                default:
                    sum = Clamp(40f * (own.TeamsAfield - enemy.TeamsAfield) / 4f, 40f) +
                          Clamp(40f * (own.HeldBuildings - enemy.HeldBuildings) / 4f, 40f) +
                          Clamp(20f * (own.CampsLive - enemy.CampsLive) / 2f, 20f);
                    break;
            }
            return (int)Math.Round(Clamp(sum, 100f), MidpointRounding.AwayFromZero);
        }

        /// <summary>One number for "how strong is this side on the front": the strongest enemy is the one with the most.</summary>
        public static float Strength(Front front, in FrontSides s) =>
            front == Front.Space ? s.BirdsUp * 10f + s.UplinksLive :
            front == Front.Cyber ? s.HeldEnemyNodes * 10f + s.AnchorsLive : s.TeamsAfield * 10f + s.HeldBuildings * 3f + s.CampsLive;

        private static float Frac(int live, int total) =>total <= 0 ? 0f : Math.Max(0f, Math.Min(1f, (float)live / total));

        private static float Clamp(float v, float limit) => float.IsNaN(v) ? 0f : Math.Max(-limit, Math.Min(limit, v));

        /// <summary>Perk quality: radius, duration, accuracy.</summary>
        public static float Quality(int s) => 1f + 0.25f * Math.Max(-100, Math.Min(100, s)) / 100f;

        public static string Word(int s) => s >= 60 ? "DOMINANT" : s >= 20 ? "AHEAD" : s > -20 ? "CONTESTED" : s > -60 ? "BEHIND" : "LOST";

        public static bool CounterActive(int s) => s >= 40;
    }
}

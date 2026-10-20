using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    internal readonly struct CallRow
    {
        public readonly SupportActionId Id;
        public readonly Front Front;
        /// <summary>1..5: the front readiness a pilot needs, which also sets the price and the per-perk cooldown.</summary>
        public readonly int Rung;
        public readonly string Label;

        public CallRow(SupportActionId id, Front front, int rung, string label)
        {
            Id = id;
            Front = front;
            Rung = rung;
            Label = label;
        }

        public string FrontWord => Front == Front.Space ? "SPACE" : Front == Front.Cyber ? "CYBER" : "SOF";

        /// <summary>E.g. <c>SPACE R3</c>.</summary>
        public string RungWord => FrontWord + " R" + Rung;
    }

    /// <summary>
    /// The perk catalogue (OPS FRONTS spec 3.6): 18 perks by front and rung (two may share a rung), priced in vanilla allocation, one cooldown each per pilot, shortened by the front's POWER when it is ahead.
    /// Adding a perk is one row here, one <c>SupportCatalog</c> row and one action file. JTAC UNLASE is the free recovery half of JTAC LASE and has no row.
    /// </summary>
    internal static class CallSheet
    {
        public const int MaxRung = 5;
        private static readonly int[] prices = { 4, 6, 10, 16, 30 };
        private static readonly float[] cooldowns = { 30f, 45f, 60f, 120f, 300f };

        private static readonly CallRow[] rows =
        {
            new CallRow(SupportActionId.Recon, Front.Space, 1, "RECON PASS"),
            new CallRow(SupportActionId.SatCamera, Front.Space, 2, "SAT CAMERA"),
            new CallRow(SupportActionId.Overwatch, Front.Space, 2, "OVERWATCH"),
            new CallRow(SupportActionId.Prsm, Front.Space, 3, "PRSM"),
            new CallRow(SupportActionId.Cruise, Front.Space, 4, "CRUISE SALVO"),
            new CallRow(SupportActionId.Starfall, Front.Space, 4, "STARFALL"),
            new CallRow(SupportActionId.Artillery, Front.Space, 5, "ORBITAL ROD"),
            new CallRow(SupportActionId.ElintSweep, Front.Cyber, 1, "ELINT SWEEP"),
            new CallRow(SupportActionId.FlareMissile, Front.Cyber, 2, "DECOY BARRAGE"),
            new CallRow(SupportActionId.RadarBlind, Front.Cyber, 3, "RADAR BLIND"),
            new CallRow(SupportActionId.Siphon, Front.Cyber, 3, "SIPHON"),
            new CallRow(SupportActionId.SamNetDown, Front.Cyber, 4, "SAM NET DOWN"),
            new CallRow(SupportActionId.Emp, Front.Cyber, 5, "EMP"),
            new CallRow(SupportActionId.JtacMark, Front.Sof, 1, "JTAC LASE"),
            new CallRow(SupportActionId.ReconTeam, Front.Sof, 2, "RECON TEAM"),
            new CallRow(SupportActionId.Fortify, Front.Sof, 3, "FORTIFY"),
            new CallRow(SupportActionId.SabotageStrike, Front.Sof, 4, "SABOTAGE STRIKE"),
            new CallRow(SupportActionId.DirectAction, Front.Sof, 5, "DIRECT ACTION"),
        };

        public static IReadOnlyList<CallRow> Rows => rows;

        /// <summary>False for a perk the host answers without an aim (SIPHON moves funds between books): the pilot may fire it with nothing designated.</summary>
        public static bool NeedsAim(SupportActionId id) => id != SupportActionId.Siphon;

        /// <summary>Allocation price of a rung before any scale or modifier (R1 4, R2 6, R3 10, R4 16, R5 30).</summary>
        public static int BasePrice(int rung) => prices[Clamp(rung) - 1];

        /// <summary>Seconds one pilot waits before using the same perk again (R1 30, R2 45, R3 60, R4 120, R5 300).</summary>
        public static float Cooldown(int rung) => cooldowns[Clamp(rung) - 1];

        private static int Clamp(int rung) => Math.Max(1, Math.Min(MaxRung, rung));

        public static bool TryGet(SupportActionId id, out CallRow row)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Id != id) continue;
                row = rows[i];
                return true;
            }
            row = default;
            return false;
        }
    }
}

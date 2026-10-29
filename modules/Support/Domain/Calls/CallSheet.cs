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
        /// <summary>0..6: the pilot's skill grade in this system (SPACE, CYBER or SOF lane) the perk needs; 0 needs none.</summary>
        public readonly int Grade;
        /// <summary>1..5: the price and per-perk cooldown tier (the perk's old front rung, so M1 changes no balance).</summary>
        public readonly int Tier;
        public readonly string Label;

        public CallRow(SupportActionId id, Front front, int grade, int tier, string label)
        {
            Id = id;
            Front = front;
            Grade = grade;
            Tier = tier;
            Label = label;
        }

        /// <summary>The system word, which is also the SQD skill lane: SPACE, CYBER or SOF.</summary>
        public string FrontWord => Front == Front.Space ? "SPACE" : Front == Front.Cyber ? "CYBER" : "SOF";

        /// <summary>E.g. <c>SPACE 4</c>; just the system for a perk that needs no grade.</summary>
        public string GradeWord => Grade > 0 ? FrontWord + " " + Grade : FrontWord;
    }

    /// <summary>
    /// The one perk gate (OPS M1): the pilot's grade in the perk's system. The host request path and every
    /// client tile read this, so a tile can never say ready while the host refuses.
    /// </summary>
    internal static class CallGate
    {
        public static bool Open(in CallRow row, int grade) => row.Grade == 0 || grade >= row.Grade;

        public static string LockWord(in CallRow row) => "NEEDS " + row.FrontWord + " SKILL " + row.Grade;
    }

    /// <summary>
    /// The perk catalogue: 18 perks by system and skill grade (OPS rework spec 4.1), priced by tier in vanilla allocation, one cooldown each per pilot, shortened by the front's POWER when it is ahead.
    /// PRSM and CRUISE SALVO need no grade until M3 gives them their hardware (spec 4.3).
    /// Adding a perk is one row here, one <c>SupportCatalog</c> row and one action file. JTAC UNLASE is the free recovery half of JTAC LASE and has no row.
    /// </summary>
    internal static class CallSheet
    {
        public const int MaxTier = 5, MaxGrade = 6;
        private static readonly int[] prices = { 4, 6, 10, 16, 30 };
        private static readonly float[] cooldowns = { 30f, 45f, 60f, 120f, 300f };

        private static readonly CallRow[] rows =
        {
            // (id, system, grade, price tier, label)
            new CallRow(SupportActionId.Recon, Front.Space, 1, 1, "RECON PASS"),
            new CallRow(SupportActionId.SatCamera, Front.Space, 2, 2, "SAT CAMERA"),
            new CallRow(SupportActionId.Overwatch, Front.Space, 3, 2, "OVERWATCH"),
            new CallRow(SupportActionId.Starfall, Front.Space, 4, 4, "STARFALL"),
            new CallRow(SupportActionId.Artillery, Front.Space, 5, 5, "ORBITAL ROD"),
            new CallRow(SupportActionId.ElintSweep, Front.Cyber, 1, 1, "ELINT SWEEP"),
            new CallRow(SupportActionId.FlareMissile, Front.Cyber, 2, 2, "DECOY BARRAGE"),
            new CallRow(SupportActionId.RadarBlind, Front.Cyber, 3, 3, "RADAR BLIND"),
            new CallRow(SupportActionId.Siphon, Front.Cyber, 4, 3, "SIPHON"),
            new CallRow(SupportActionId.SamNetDown, Front.Cyber, 5, 4, "SAM NET DOWN"),
            new CallRow(SupportActionId.Emp, Front.Cyber, 6, 5, "EMP"),
            new CallRow(SupportActionId.Cruise, Front.Cyber, 0, 4, "CRUISE SALVO"),
            new CallRow(SupportActionId.JtacMark, Front.Sof, 1, 1, "JTAC LASE"),
            new CallRow(SupportActionId.ReconTeam, Front.Sof, 2, 2, "RECON TEAM"),
            new CallRow(SupportActionId.Fortify, Front.Sof, 3, 3, "FORTIFY"),
            new CallRow(SupportActionId.SabotageStrike, Front.Sof, 4, 4, "SABOTAGE STRIKE"),
            new CallRow(SupportActionId.DirectAction, Front.Sof, 5, 5, "DIRECT ACTION"),
            new CallRow(SupportActionId.Prsm, Front.Sof, 0, 3, "PRSM"),
        };

        public static IReadOnlyList<CallRow> Rows => rows;

        /// <summary>False for a perk the host answers without an aim (SIPHON moves funds between books): the pilot may fire it with nothing designated.</summary>
        public static bool NeedsAim(SupportActionId id) => id != SupportActionId.Siphon;

        /// <summary>Allocation price of a tier before any scale or modifier (T1 4, T2 6, T3 10, T4 16, T5 30).</summary>
        public static int BasePrice(int tier) => prices[Math.Clamp(tier, 1, MaxTier) - 1];

        /// <summary>Seconds one pilot waits before using the same perk again (T1 30, T2 45, T3 60, T4 120, T5 300).</summary>
        public static float Cooldown(int tier) => cooldowns[Math.Clamp(tier, 1, MaxTier) - 1];

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

using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>Turns the ground service's report into what the snapshot and the phase rule take: unknown stays unknown and every number stays inside the wire bounds.</summary>
    internal static class ForcesBuilder
    {
        internal const int MaxMarks = 8;
        private const byte Unknown = 255;
        private const float MaxCoordinate = 1e6f, MaxDistance = 1e7f;

        /// <summary>The phase rule's input. haveReport false means the ground side could not say anything yet (the phase is Awaiting with no note).</summary>
        internal static PhaseInput PhaseOf(in GroundOpReport r, bool haveReport) => new PhaseInput
        {
            HaveReport = haveReport, Steering = r.Steering, Groups = r.Groups, Rally = r.Rally, Line = r.Line, Assault = r.Assault,
            Contact = r.Contact, Withdraw = r.Withdraw, NearestToFixMeters = r.NearestToFixMeters,
        };

        /// <summary>A fresh, never-mutated view of the report. A negative count is unknown (255), a count past its wire bound is clamped to it, at most MaxMarks finite marks are kept.</summary>
        internal static TheaterForcesView Of(in GroundOpReport r, GroundMark[] marks, int markCount)
        {
            var view = new TheaterForcesView
            {
                Groups = Count(r.Groups, 32), Vehicles = Count(r.Vehicles, 192), Formed = Count(r.Formed, 192),
                Rally = Count(r.Rally, 32), Line = Count(r.Line, 32), Assault = Count(r.Assault, 32), Contact = Count(r.Contact, 32), Withdraw = Count(r.Withdraw, 32),
                Moving = Count(r.Moving, 192), Steered = Count(r.Steered, 192), OnSlot = Count(r.OnSlot, 192), Stalled = Count(r.Stalled, 192),
                EtaSeconds = r.EtaSeconds < 0 ? (ushort)65535 : (ushort)Math.Min(3600, r.EtaSeconds),
                NearestToFixMeters = float.IsFinite(r.NearestToFixMeters) && r.NearestToFixMeters >= 0f ? Math.Min(MaxDistance, r.NearestToFixMeters) : -1f,
            };
            int count = marks == null ? 0 : Math.Min(Math.Min(markCount, marks.Length), MaxMarks * 2);
            var kept = new TheaterForceMark[Math.Max(0, Math.Min(count, MaxMarks))];
            int n = 0;
            for (int i = 0; i < count && n < kept.Length; i++)
            {
                GroundMark m = marks[i];
                if (!(Math.Abs(m.X) <= MaxCoordinate) || !(Math.Abs(m.Z) <= MaxCoordinate)) continue;
                kept[n++] = new TheaterForceMark { X = m.X, Z = m.Z, Stage = (byte)Math.Min((int)m.Stage, 5), Alive = (byte)Math.Min((int)m.Alive, 6) };
            }
            view.Marks = n == kept.Length ? kept : Trim(kept, n);
            return view;
        }

        private static byte Count(int value, int max) => value < 0 ? Unknown : (byte)Math.Min(value, max);

        private static TheaterForceMark[] Trim(TheaterForceMark[] marks, int n)
        {
            var trimmed = new TheaterForceMark[n];
            Array.Copy(marks, trimmed, n);
            return trimmed;
        }
    }
}

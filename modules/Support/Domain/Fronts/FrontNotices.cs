using System;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    internal enum FrontNoticeKind : byte { None, Up, Failed, Lost }

    internal readonly struct FrontNotice
    {
        public static readonly FrontNotice None = new FrontNotice(FrontNoticeKind.None, "");
        public readonly FrontNoticeKind Kind;
        public readonly string Text;
        public FrontNotice(FrontNoticeKind kind, string text) { Kind = kind; Text = text; }
    }

    /// <summary>
    /// The milestones of the fronts, said once in the cockpit as they happen (OPS keeps no log): a programme done (READINESS names the perks it
    /// unlocked), a programme that failed, an asset lost. Queued / started / posture lines stay quiet: the pilot did those, or they are bookkeeping.
    /// A lost asset outranks a failure, which outranks good news, when several land in one update.
    /// </summary>
    internal sealed class FrontNoticeTracker : NoticeTrackerBase
    {
        private readonly float[] seen = { -1f, -1f, -1f };

        public void Reset() { seen[0] = seen[1] = seen[2] = -1f; ResetGate(); }

        public FrontNotice Observe(bool known, FrontStateData state, float now, bool quiet)
        {
            if (!known || state == null) { Reset(); return FrontNotice.None; }
            FrontNotice found = FrontNotice.None;
            for (int f = 0; f < state.Fronts.Length && f < seen.Length; f++)
            {
                FrontRow row = state.Fronts[f];
                float newest = 0f;
                foreach (FrontLogRow l in row.Log) newest = Math.Max(newest, l.Time);
                if (seen[f] < 0f) { seen[f] = newest; continue; } // the first look takes the history as read
                foreach (FrontLogRow l in row.Log)
                {
                    if (l.Time <= seen[f]) continue;
                    FrontNotice n = Classify((Front)f, l);
                    if (n.Kind > found.Kind) found = n;
                }
                seen[f] = Math.Max(seen[f], newest);
            }
            return Admit(found.Kind != FrontNoticeKind.None, now, quiet) ? found : FrontNotice.None;
        }

        private static FrontNotice Classify(Front f, in FrontLogRow l)
        {
            switch ((FrontLogCode)l.Code)
            {
                case FrontLogCode.Done:
                    if (l.A == (short)ProgrammeId.Readiness)
                    {
                        string unlocks = "";
                        foreach (CallRow r in CallSheet.Rows) if (r.Front == f && r.Rung == l.B) unlocks += (unlocks.Length > 0 ? ", " : "") + r.Label;
                        return new FrontNotice(FrontNoticeKind.Up, FrontRules.Name(f) + " READINESS " + l.B + (unlocks.Length > 0 ? " · NEW: " + unlocks : ""));
                    }
                    return new FrontNotice(FrontNoticeKind.Up, FrontWords.Line(f, l));
                case FrontLogCode.Failed: return new FrontNotice(FrontNoticeKind.Failed, FrontWords.Line(f, l));
                case FrontLogCode.Rebuild: return new FrontNotice(FrontNoticeKind.Lost, FrontWords.Line(f, l));
                default: return FrontNotice.None;
            }
        }
    }
}

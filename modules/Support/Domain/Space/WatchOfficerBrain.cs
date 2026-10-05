using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>
    /// What OVERLORD needs from the live game for one faction. The host adapter implements it over the real contacts, bird state
    /// and TASKED desk; the offline simulation implements it over the same pure types, so the loop below is exercised for real.
    /// </summary>
    internal interface IWatchHost
    {
        /// <summary>Connected humans of the faction (the host census).</summary>
        int Humans { get; }

        /// <summary>
        /// Fills <paramref name="inputs"/> (all but <see cref="WatchInputs.Now"/> and <see cref="WatchInputs.Humans"/>), the revealed
        /// enemy ground contacts the faction could MARK right now, and the public scan sites. Both lists arrive cleared.
        /// </summary>
        void Gather(float now, ref WatchInputs inputs, List<WatchTarget> targets, List<WatchSite> sites);

        /// <summary>MARK + CONFIRM through the same host path a human uses, attributed to OVERLORD.</summary>
        MarkVerdict Mark(int contactId);

        /// <summary>SEND the confirmed MARKs through the same builder a human uses, with the WATCH OFFICER source.</summary>
        TaskedOutcome Send(int[] markIds, int count);

        /// <summary>Task the bird over the site through the normal action path. False when it was refused (nothing was spent).</summary>
        bool Scan(WatchScan kind, in WatchSite site);
    }

    /// <summary>
    /// The OVERLORD loop for one faction: ask the policy what to do, do it through the host, tell the policy what happened.
    /// Allocation-light: the lists and the id buffer are reused; only a successful post allocates (the desk's id array).
    /// </summary>
    internal sealed class WatchOfficerBrain
    {
        public const float SuspendedPollSeconds = 1f;
        private readonly List<WatchTarget> targets = new List<WatchTarget>(SpaceContacts.MaxReveals);
        private readonly List<WatchSite> sites = new List<WatchSite>(8);
        private readonly int[] marked = new int[WatchOfficerPolicy.MaxTargets];
        private readonly int[] one = new int[1];

        public WatchOfficerPolicy Policy { get; } = new WatchOfficerPolicy();
        public int Posts, Marks, Scans, Failures, Thinks;
        public WatchWhy LastWhy { get; private set; }
        public WatchPlan LastPlan { get; private set; }

        public void RecordHuman(float now) => Policy.RecordHuman(now);

        public void Reset()
        {
            Policy.Reset();
            targets.Clear(); sites.Clear();
            Posts = Marks = Scans = Failures = Thinks = 0;
            LastWhy = WatchWhy.None; LastPlan = default;
        }

        /// <summary>One frame. Returns the plan it acted on (or the reason it did nothing).</summary>
        public WatchPlan Step(IWatchHost host, float now)
        {
            if (!Policy.Due(now)) return WatchPlan.Idle(WatchWhy.NotDue);
            var inputs = new WatchInputs { Now = now, Humans = host.Humans };
            WatchPlan plan;
            if (!Policy.Idle(inputs.Humans, now))
            {
                // A human is working this domain: do not even gather the facts, and look again in a second.
                plan = Policy.Think(inputs, targets, sites);
                Policy.Defer(now, SuspendedPollSeconds);
            }
            else
            {
                targets.Clear(); sites.Clear();
                host.Gather(now, ref inputs, targets, sites);
                plan = Policy.Think(inputs, targets, sites);
                Thinks++;
                if (plan.Action == WatchAction.Post) Post(host, plan, now);
                else if (plan.Action == WatchAction.Scan) Scan(host, plan, now);
            }
            LastPlan = plan;
            LastWhy = plan.Why;
            return plan;
        }

        private void Post(IWatchHost host, in WatchPlan plan, float now)
        {
            int ok = 0;
            for (int i = 0; i < plan.Count; i++)
            {
                int id = plan.Id(i);
                Policy.NoteMarkAttempt(now);
                MarkVerdict verdict = host.Mark(id);
                if (verdict == MarkVerdict.Confirmed) { marked[ok++] = id; Marks++; continue; }
                one[0] = id; // not workable (neutral, friendly, lapsed, rate limited): skip that contact for a minute
                Policy.NoteFailed(one, 1, now);
            }
            if (ok == 0) { Failures++; return; }
            TaskedOutcome outcome = host.Send(marked, ok);
            if (outcome == TaskedOutcome.Posted) { Posts++; Policy.NotePosted(marked, ok, now); }
            else { Failures++; Policy.NoteFailed(marked, ok, now); }
        }

        private void Scan(IWatchHost host, in WatchPlan plan, float now)
        {
            var site = new WatchSite(plan.SiteKey, plan.SiteX, plan.SiteZ);
            if (host.Scan(plan.Scan, site)) { Scans++; Policy.NoteScan(now, plan.SiteKey); }
            else { Failures++; Policy.NoteScanFailed(now); }
        }
    }
}

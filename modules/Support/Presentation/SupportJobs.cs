using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// The faction's posted strike packages as MIS jobs. ACCEPT arms the post here (nothing is claimed or spent), FIRE sends the host's claim and
    /// launch, RELEASE disarms: the controller's own verbs, the ones the old OPS TASKED cards pressed. Reading the jobs keeps the SPACE feed's
    /// lease open a moment, since the host only streams the posts to a pilot who is looking.
    /// </summary>
    internal sealed class SupportJobs : ISupportJobs
    {
        private const float LeaseSeconds = 2f;
        private readonly SupportManager manager;
        private readonly CallsController calls;
        private readonly SpaceFeedController feed;

        internal SupportJobs(SupportManager supportManager, CallsController callsController, SpaceFeedController feedController)
        {
            manager = supportManager; calls = callsController; feed = feedController;
        }

        public void CopyJobs(List<JobRow> into)
        {
            if (manager == null || calls == null) return;
            if (feed != null) feed.WatchUntil = Time.unscaledTime + LeaseSeconds;
            if (!manager.SpaceMirror.Known) return;
            IReadOnlyList<FeedPost> posts = manager.SpaceMirror.State.Posts;
            float now = SupportManager.MissionNow();
            for (int i = 0; i < posts.Count; i++)
            {
                FeedPost post = posts[i];
                PostStatus status = SpaceFeedRules.PostStatusOf(post, now);
                if (status == PostStatus.Stale) continue;
                bool intel = post.Action == SupportActionId.OrbitalIntel;
                bool armed = post.CallId != 0 && calls.TaskedArmedId == post.CallId, launching = status == PostStatus.Launching;
                int targets = post.Points != null ? Mathf.Max(1, post.Points.Length) : 1;
                bool site = post.Points != null && post.Points.Length > 0;
                string origin = post.Own ? "YOU" : post.WatchOfficer ? "OVERLORD" : string.IsNullOrEmpty(post.Maker) ? "OPERATOR" : "OPERATOR " + post.Maker;
                into.Add(new JobRow
                {
                    Kind = JobKind.StrikePackage, Id = post.CallId, Title = TaskedKinds.Label(post.Action),
                    Detail = TaskedKinds.Slab(post.Domain) + " · " + targets + " TGT" + (site ? " · " + OpsKit.Grid(post.Points[0].X, post.Points[0].Z) : "") + " · FROM " + origin,
                    State = (intel ? "CONFIRMED FIX" : launching ? (string.IsNullOrEmpty(post.Claimant) ? "ANOTHER PILOT" : post.Claimant) + " LAUNCHING"
                        : armed ? calls.TaskedClaimPending ? "CLAIM SENT" : "ARMED" : "OPEN") + " · " + FrontRules.Clock(Mathf.Max(0f, post.Expires - now)) + " LEFT",
                    Act = intel || launching ? "" : armed ? "FIRE" : "ACCEPT", ActLive = !intel && !launching && !calls.TaskedClaimPending,
                    Mine = armed, Pay = -post.Price, Currency = intel ? "" : "ALLOCATION",
                    HasSite = site, X = site ? post.Points[0].X : 0f, Z = site ? post.Points[0].Z : 0f,
                });
            }
        }

        public void Act(int id)
        {
            if (calls == null) return;
            foreach (FeedPost post in manager.SpaceMirror.State.Posts)
                if (post.CallId == id && post.Action == SupportActionId.OrbitalIntel) return;
            if (calls.TaskedArmedId == id) calls.FireTasked(id);
            else calls.ArmTasked(id);
        }

        public void Release(int id)
        {
            if (calls != null && calls.TaskedArmedId == id) calls.Disarm();
        }
    }
}

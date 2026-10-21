using System;
using System.Collections.Generic;
using System.Text;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>What a front card stamps on itself. WAITING = no host snapshot yet, OFFLINE = no host link, DEGRADED = a current unresolved condition.</summary>
    internal enum FrontStatus : byte { Waiting, Operational, Degraded, Offline }

    /// <summary>Which phase of the shown programme the card is in: money coming in, or construction running. Never one painted as the other.</summary>
    internal enum ProgrammePhase : byte { None, Funding, Building }

    /// <summary>One front card of the OPS page, already worded.</summary>
    internal struct FrontCardView
    {
        public Front Front;
        public bool Known;
        public FrontStatus Status;
        public int Readiness, Superiority;
        /// <summary>Alert = the current blocker or recovery line (what is wrong NOW, "NO ACTIVE BLOCKER" otherwise); Event = the newest log row as history, EventAge its age.</summary>
        public string Word, Directive, Building, BuildNote, Alert, Queue, Event, EventAge;
        /// <summary>The front's perk power as the card shows it: <c>POWER ×1.25</c> (empty before the host has spoken).</summary>
        public string Power;
        /// <summary>The pilot's perks of this front: free to arm now (an armed or pending one is counted apart).</summary>
        public int Available, ArmedPerks, PendingPerks, Unlocked, QueueCount, FundingPct;
        public ushort Budget;
        public AvState Tone, AlertTone;
        public ProgrammePhase Phase;
        public float BuildProgress;
    }

    /// <summary>One perk of the PERKS tab: a call card (brevity code, rung, price, state). Clicking it inspects it; nothing is armed or spent.</summary>
    internal struct PerkRowView
    {
        public SupportActionId Id;
        public Front Front;
        public int Rung;
        public string Code, Name, RungWord, Price, State;
        public AvState Tone;
        public bool Enabled, Armed, Pending, Inspected;
    }

    /// <summary>One posted strike of the TASKED tab: a package card any pilot of the faction can claim.</summary>
    internal struct TaskedRowView
    {
        public int PostId, Targets;
        public string Code, Name, Meta, Grids, GridsFull, Price, Expiry, Origin, Stamp, Detail;
        public AvState Tone;
        public bool Armed, Launching, Inspected, Urgent;
    }

    /// <summary>The pinned order strip: the inspected (or armed) perk or package in the strip's own lines, and what its buttons may do.</summary>
    internal struct StripView
    {
        public bool Has;
        public string Code, Stamp, Name, Meta, Price, Brief, Gate, Primary;
        public AvState StampTone, GateTone;
        public bool PrimaryEnabled, PrimaryFire, DisarmEnabled;
        /// <summary>The perk (a <see cref="SupportActionId"/>) or the post id the buttons act on.</summary>
        public int Id;
    }

    /// <summary>Everything the OPS page paints. Built each refresh by <see cref="OpsPageViews"/> from the manager, the controller and the front mirror.</summary>
    internal sealed class OpsPageView
    {
        public const int FavouriteSlots = 4;

        public int Allocation, PerksReady, PerksTotal, FrontsAhead, Alerts;
        public readonly FrontCardView[] Cards = new FrontCardView[FrontRules.FrontCount];
        public readonly List<PerkRowView> Perks = new List<PerkRowView>(24);
        /// <summary>Per front: <c>POWER ×1.25 · AHEAD</c>, the line the PERKS tab puts on each group header (empty before the host has spoken).</summary>
        public readonly string[] PowerLine = { "", "", "" };
        public readonly CallTile?[] Favourites = new CallTile?[FavouriteSlots];
        public string Words = "";
        public AvState WordsTone = AvState.Ready;
        public bool Armed, Pending;
        public string FrontWords = "";
        public readonly List<TaskedRowView> Tasked = new List<TaskedRowView>(MaxTaskedRows);
        public int TaskedTotal;
        /// <summary>The post the pilot was inspecting is no longer on the board (the strip says so instead of silently showing another).</summary>
        public bool PostGone;
        public StripView PerkStrip, PostStrip;
        public bool CruiseFlying, Steering;
        public string CruiseRoute = "";
        public int CruiseLegs;
        public const int MaxTaskedRows = SpaceWire.MaxPosts;
        public AvState FrontWordsTone = AvState.Inert;
    }

    /// <summary>The pure mapping from the mirror and the tiles onto <see cref="OpsPageView"/>.</summary>
    internal static class OpsPageViews
    {
        /// <summary>Pass as the inspected post after the inspected one left the board: nothing is shown until the pilot picks another.</summary>
        public const int PostGone = -1;

        private const string Dot = " · ";
        private const int ShortGrids = 3;

        // ---- Fronts -----------------------------------------------------------------------------------------

        /// <param name="online">False when the host link is down: every front stamps OFFLINE (the last mirror reading is still shown).</param>
        public static void Cards(OpsPageView v, FrontStateData d, bool known, float now, bool online = true)
        {
            int ahead = 0, alerts = 0;
            for (int i = 0; i < FrontRules.FrontCount; i++)
            {
                var f = (Front)i;
                FrontCardView c = default;
                c.Front = f; c.Known = known;
                foreach (PerkRowView p in v.Perks)
                {
                    if (p.Front != f) continue;
                    if (p.Armed) c.ArmedPerks++; else if (p.Pending) c.PendingPerks++; else if (p.Enabled) c.Available++;
                }
                FrontRow row = known && d != null ? d.Fronts[i] : null;
                if (row == null)
                {
                    c.Status = FrontStatus.Waiting; c.Word = "NO LINK"; c.Tone = AvState.Inert; c.Directive = "—"; c.Power = ""; v.PowerLine[i] = "";
                    c.Building = "WAITING FOR THE HOST"; c.BuildNote = ""; c.Queue = ""; c.Event = ""; c.EventAge = "";
                    c.Alert = "WAITING FOR HOST" + Dot + "FRONT STATE NOT RECEIVED"; c.AlertTone = AvState.Inert;
                    v.Cards[i] = c;
                    continue;
                }
                c.Readiness = row.Readiness;
                c.Superiority = row.Superiority;
                c.FundingPct = row.PriorityPct; c.Budget = row.Budget;
                c.QueueCount = row.Queue.Count;
                foreach (CallRow r in CallSheet.Rows) if (r.Front == f && r.Rung <= row.Readiness) c.Unlocked++;
                c.Word = FrontSuperiority.Word(row.Superiority);
                c.Power = FrontSuperiority.PowerWord(row.Superiority);
                v.PowerLine[i] = FrontSuperiority.PowerLine(row.Superiority);
                c.Tone = row.Superiority >= 20 ? AvState.Ready : row.Superiority > -20 ? AvState.Info : row.Superiority > -60 ? AvState.Caution : AvState.Danger;
                c.Directive = FrontRules.Name(row.Directive) + (row.DirectiveLockUntil > now
                    ? Dot + "LOCK " + FrontRules.Clock(row.DirectiveLockUntil - now) + (string.IsNullOrEmpty(row.DirectiveBy) ? "" : " " + row.DirectiveBy) : "");
                if (row.Superiority >= 20) ahead++;

                // The current blocker: unresolved conditions only, in a fixed priority (no link, a lost anchor still being rebuilt, enemy superiority).
                // The newest log row is history: it never makes an alert by itself.
                int conditions = 0;
                string blocker = null;
                AvState bt = AvState.Inert;
                if (!online) { conditions++; blocker = "NO HOST LINK" + Dot + "LAST READING SHOWN"; bt = AvState.Danger; }
                if (RebuildPending(row, out ProgrammeId lostId, out int lostAt))
                {
                    conditions++;
                    if (blocker == null)
                    {
                        FrontQueueRow lq = row.Queue[lostAt];
                        blocker = FrontRules.Info(lostId).Name + " LOST" + Dot + "REBUILD " +
                            (lq.Percent >= 100 ? "BUILDING " + FrontRules.Clock(BuildLeft(lq, d, now)) : "FUNDING " + lq.Percent + " %");
                        bt = AvState.Caution;
                    }
                }
                if (row.Superiority <= -20)
                {
                    conditions++;
                    if (blocker == null)
                    {
                        blocker = "ENEMY HOLDS SUPERIORITY" + Dot + c.Word + " " + row.Superiority;
                        bt = row.Superiority <= -60 ? AvState.Danger : AvState.Caution;
                    }
                }
                if (blocker == null) blocker = "NO ACTIVE BLOCKER";
                else
                {
                    if (conditions > 1) blocker += Dot + "+" + (conditions - 1) + " MORE";
                    blocker = AvStates.Glyph(bt) + blocker;
                }
                c.Alert = blocker; c.AlertTone = bt;
                c.Status = !online ? FrontStatus.Offline : conditions > 0 ? FrontStatus.Degraded : FrontStatus.Operational;
                alerts += conditions;

                // The programme being built, else the first one filling, else nothing. Funding and building are different phases.
                int pick = -1;
                for (int q = 0; q < row.Queue.Count; q++) if (row.Queue[q].Percent >= 100) { pick = q; break; }
                if (pick < 0 && row.Queue.Count > 0) pick = 0;
                if (pick >= 0)
                {
                    FrontQueueRow q = row.Queue[pick];
                    bool building = q.Percent >= 100;
                    int left = BuildLeft(q, d, now);
                    float build = Math.Max(1f, FrontRules.BuildSeconds(q.Id, row.Readiness));
                    c.Phase = building ? ProgrammePhase.Building : ProgrammePhase.Funding;
                    c.Building = q.Id == ProgrammeId.Readiness ? "READINESS " + row.Readiness + " → " + Math.Min(FrontRules.MaxReadiness, row.Readiness + 1) : FrontRules.Info(q.Id).Name;
                    c.BuildProgress = building ? 1f - left / build : q.Percent / 100f;
                    c.BuildNote = building ? FrontRules.Clock(left) + " LEFT" : q.Percent + " % FUNDED";
                }
                else { c.Building = "NOTHING QUEUED"; c.BuildNote = ""; }

                // The newest log row, as history with its age (the log is oldest first).
                if (row.Log.Count > 0)
                {
                    FrontLogRow l = row.Log[row.Log.Count - 1];
                    string call = FrontWords.Callsign(f) + Dot;
                    string text = FrontWords.Line(f, l, row.DirectiveBy);
                    if (text.StartsWith(call, StringComparison.Ordinal)) text = text.Substring(call.Length); // the card already carries the codename
                    c.Event = text.Length > 0 ? text : "NO NEWS";
                    c.EventAge = FrontRules.Clock(Math.Max(0f, now - l.Time)) + " AGO";
                }
                else { c.Event = ""; c.EventAge = ""; }

                // What waits behind the shown programme.
                string behind = "";
                for (int q = 0; q < row.Queue.Count; q++)
                {
                    if (q == pick) continue;
                    behind += (behind.Length > 0 ? Dot : "") + (row.Queue[q].Id == ProgrammeId.Readiness ? "READINESS" : FrontRules.Info(row.Queue[q].Id).Name);
                }
                c.Queue = behind;
                v.Cards[i] = c;
            }
            v.FrontsAhead = ahead;
            v.Alerts = alerts;
        }

        private static int BuildLeft(in FrontQueueRow q, FrontStateData d, float now) =>
            (int)Math.Max(0f, Math.Ceiling(q.BuildLeft - (q.Percent >= 100 ? now - d.Now : 0f)));

        /// <summary>
        /// True while an anchor the log says was lost still has its rebuild programme in the queue: that is a current condition. A rebuild
        /// line whose programme finished (a later DONE row) or left the queue is history. ponytail: matches by programme id, so a fresh
        /// queue of the same programme for growth reads as a rebuild until the log scrolls past it; the log keeps 8 rows.
        /// </summary>
        private static bool RebuildPending(FrontRow row, out ProgrammeId id, out int queueIndex)
        {
            id = default; queueIndex = -1;
            for (int q = row.Log.Count - 1; q >= 0; q--)
            {
                FrontLogRow l = row.Log[q];
                if ((FrontLogCode)l.Code != FrontLogCode.Rebuild) continue;
                bool done = false;
                for (int n = q + 1; n < row.Log.Count && !done; n++)
                    done = (FrontLogCode)row.Log[n].Code == FrontLogCode.Done && row.Log[n].A == l.A;
                if (done) continue;
                var pid = (ProgrammeId)Math.Max(0, Math.Min(FrontRules.Programmes.Length - 1, (int)l.A));
                for (int k = 0; k < row.Queue.Count; k++)
                    if (row.Queue[k].Id == pid) { id = pid; queueIndex = k; return true; }
            }
            return false;
        }

        // ---- Perks ------------------------------------------------------------------------------------------

        /// <summary>The state column word of a perk row.</summary>
        public static string StateWord(in CallTile t)
        {
            switch (t.State)
            {
                case CallState.Ready: return "READY";
                case CallState.Armed: return "ARMED";
                case CallState.Pending: return "PENDING";
                case CallState.Offline: return "NO UPLINK";
                case CallState.LowCredit: return LowAlloc(t.StateWord);
                case CallState.Cooldown: return C2Cap.StateWord(t).Replace("COOL ", "COOLDOWN ");
                default: return t.StateWord; // LOCKED carries its reason (NEEDS READINESS 3)
            }
        }

        private static string StampWord(CallState s) =>
            s == CallState.Ready ? "READY" : s == CallState.Armed ? "ARMED" : s == CallState.Pending ? "PENDING" : s == CallState.Cooldown ? "COOLDOWN"
            : s == CallState.Locked ? "LOCKED" : s == CallState.LowCredit ? "LOW AP" : "NO LINK";

        public static void Perks(OpsPageView v, IReadOnlyList<CallTile> tiles)
        {
            v.Perks.Clear();
            int ready = 0;
            foreach (CallTile t in tiles)
            {
                CallRow row; CallSheet.TryGet(t.Id, out row);
                if (t.State == CallState.Ready) ready++; // available to arm: an armed perk is spoken for and a request in flight is not available
                v.Perks.Add(new PerkRowView
                {
                    Id = t.Id, Front = row.Front, Rung = row.Rung, Code = Code(t.Id), Name = t.Label, RungWord = t.RungWord, Price = Ap(t.CostText), State = CallsView.RowWord(StateWord(t)), Tone = Ink(t.State),
                    Enabled = t.Enabled, Armed = t.State == CallState.Armed, Pending = t.State == CallState.Pending
                });
            }
            v.PerksReady = ready;
            v.PerksTotal = tiles.Count;
        }

        public static AvState Ink(CallState s) =>
            s == CallState.Ready ? AvState.Ready : s == CallState.Armed ? AvState.Caution : s == CallState.Pending ? AvState.Info
            : s == CallState.Offline || s == CallState.LowCredit ? AvState.Danger : AvState.Inert;

        /// <summary>
        /// The pinned perk strip and the highlighted row. The strip shows the inspected perk, else the armed one, else the first ready one, else
        /// the first: it always reads a real perk. Inspecting arms nothing and spends nothing.
        /// </summary>
        public static void Order(OpsPageView v, IReadOnlyList<CallTile> tiles, AimSource aim, string aimGrid, bool pending, string words, int allocation,
            SupportActionId? inspected = null)
        {
            CallTile armed = default;
            v.Armed = false;
            foreach (CallTile t in tiles) if (t.State == CallState.Armed) { armed = t; v.Armed = true; break; }
            v.Pending = pending;
            string where = aim == AimSource.Pod ? "LOCKED TARGET" : aim == AimSource.Map ? "MAP CLICK" : aim == AimSource.Team ? "SOF TEAM LASER" : "";

            int shown = -1;
            for (int i = 0; i < tiles.Count && shown < 0; i++) if (inspected.HasValue && tiles[i].Id == inspected.Value) shown = i;
            for (int i = 0; i < tiles.Count && shown < 0; i++) if (tiles[i].State == CallState.Armed) shown = i;
            for (int i = 0; i < tiles.Count && shown < 0; i++) if (tiles[i].State == CallState.Ready) shown = i;
            if (shown < 0 && tiles.Count > 0) shown = 0;

            v.PerkStrip = default;
            for (int i = 0; i < v.Perks.Count; i++)
            {
                PerkRowView p = v.Perks[i];
                p.Inspected = i == shown;
                if (p.Armed && where.Length == 0 && CallSheet.NeedsAim(p.Id)) p.State = "AIM REQUIRED"; // armed but not ready to fire: the row says what is missing
                v.Perks[i] = p;
            }
            if (shown >= 0)
            {
                CallTile t = tiles[shown];
                CallSheet.TryGet(t.Id, out CallRow row);
                bool isArmed = t.State == CallState.Armed;
                string gate; AvState gt;
                if (isArmed)
                {
                    gt = AvState.Caution;
                    gate = !CallSheet.NeedsAim(t.Id) ? "NO AIM NEEDED" + Dot + "PRESS FIRE"
                        : where.Length == 0 ? "AIM REQUIRED" + Dot + "LOCK A TARGET OR RIGHT-CLICK THE MAP"
                        : "AIM " + where + (aimGrid.Length > 0 ? Dot + aimGrid : "") + Dot + "FIRE, OR RIGHT-CLICK THE MAP";
                }
                else if (t.State == CallState.Pending) { gt = AvState.Info; gate = "ORDER SENT" + Dot + "WAITING FOR THE HOST"; }
                else if (t.State == CallState.Ready)
                {
                    gt = AvState.Ready;
                    // The cooldown the host will apply: the rung's value, divided by the front's POWER while it is ahead (the cards were mapped before this).
                    float power = FrontSuperiority.Power(v.Cards[(int)row.Front].Superiority);
                    gate = "READY" + Dot + FrontRules.Clock(PerkPower.Cooldown(CallSheet.Cooldown(row.Rung), power)) + " COOLDOWN AFTER USE" + (t.Reason.Length > 0 ? Dot + t.Reason : "");
                }
                else { gt = Ink(t.State); gate = StateWord(t); }
                v.PerkStrip = new StripView
                {
                    Has = true, Id = (int)t.Id, Code = Code(t.Id), Stamp = StampWord(t.State), StampTone = Ink(t.State), Name = t.Label, Meta = t.RungWord,
                    Price = Ap(t.CostText), Brief = Brief(t.Id), Gate = gate, GateTone = gt,
                    Primary = isArmed ? "FIRE" : t.State == CallState.Pending ? "WAIT" : "ARM",
                    PrimaryEnabled = isArmed ? aim != AimSource.None || !CallSheet.NeedsAim(t.Id) : t.State == CallState.Ready, PrimaryFire = isArmed, DisarmEnabled = v.Armed
                };
            }
            v.Words = words ?? ""; // empty: the panel words its own tab's hint (CallsPanel)
            v.WordsTone = v.Armed ? AvState.Caution : pending ? AvState.Info : C2Cap.StartsNegative(words ?? "") ? AvState.Danger : AvState.Ready;
        }

        // ---- Tasked -----------------------------------------------------------------------------------------

        /// <summary>
        /// The TASKED cards: live posts only (stale ones are gone), in mirror order, capped for the page, and the pinned package strip.
        /// The strip shows the inspected post (0 = nothing inspected yet: the armed post, else the first; <see cref="PostGone"/> = it left the
        /// board). The press itself is re-validated by <see cref="CallsController.PressTasked"/>.
        /// </summary>
        public static void Tasked(OpsPageView v, IReadOnlyList<FeedPost> posts, bool known, float now, int armedPostId, bool claimPending, int inspectedPost = 0)
        {
            v.Tasked.Clear();
            v.TaskedTotal = 0;
            v.PostGone = false;
            v.PostStrip = default;
            if (!known || posts == null) { v.PostStrip = Idle("WAITING FOR HOST", "THE SPACE FEED HAS NOT ANSWERED YET", "", AvState.Inert); return; }
            int shown = -1, armedAt = -1;
            for (int i = 0; i < posts.Count; i++)
            {
                FeedPost post = posts[i];
                PostStatus status = SpaceFeedRules.PostStatusOf(post, now);
                if (status == PostStatus.Stale) continue;
                v.TaskedTotal++;
                if (v.Tasked.Count >= OpsPageView.MaxTaskedRows) continue;
                int targets = post.Points != null ? Math.Max(1, post.Points.Length) : 1;
                bool armed = post.CallId != 0 && armedPostId == post.CallId;
                bool launching = status == PostStatus.Launching;
                PostGrids g = Grids(post);
                float left = Math.Max(0f, post.Expires - now);
                string origin = post.Own ? "YOU" : post.WatchOfficer ? "OVERLORD" : string.IsNullOrEmpty(post.Maker) ? "OPERATOR" : "OPERATOR " + post.Maker;
                string price = post.Price > 0 ? Ap(post.Price) : "FREE";
                var row = new TaskedRowView
                {
                    PostId = post.CallId, Targets = targets, Code = Code(post.Action), Name = TaskedKinds.Label(post.Action),
                    Meta = TaskedKinds.Slab(post.Domain) + Dot + targets + " TGT", Grids = g.Short, GridsFull = g.Full, Price = price,
                    Expiry = FrontRules.Clock(left) + " LEFT", Origin = origin, Urgent = left < 20f,
                    // ARM is a local arm only; FIRE sends the host's claim-and-launch. Another pilot's launch is LAUNCHING, never ARMED.
                    Stamp = launching ? "LAUNCHING" : armed ? (claimPending ? "CLAIM SENT" : "ARMED") : "OPEN",
                    Detail = launching ? (string.IsNullOrEmpty(post.Claimant) ? "ANOTHER PILOT" : post.Claimant) + " LAUNCHING" + Dot + targets + " TGT"
                        : "FROM " + origin + Dot + targets + " TGT" + Dot + price,
                    Tone = armed ? AvState.Caution : launching ? AvState.Info : AvState.Ready,
                    Armed = armed, Launching = launching
                };
                if (inspectedPost > 0 && row.PostId == inspectedPost) shown = v.Tasked.Count;
                if (armed) armedAt = v.Tasked.Count;
                v.Tasked.Add(row);
            }
            if (shown < 0 && inspectedPost == 0) shown = armedAt >= 0 ? armedAt : v.Tasked.Count > 0 ? 0 : -1; // nothing inspected yet
            if (shown >= 0)
            {
                TaskedRowView r = v.Tasked[shown];
                r.Inspected = true;
                v.Tasked[shown] = r;
                string gate = r.Launching ? r.Detail + Dot + r.Expiry
                    : r.Armed ? (claimPending ? "CLAIM SENT" + Dot + "WAITING FOR THE HOST" : "ARMED" + Dot + "FIRE CLAIMS AND LAUNCHES IT" + Dot + r.Expiry)
                    : "OPEN" + Dot + "FROM " + r.Origin + Dot + r.Expiry + Dot + "ARM, THEN FIRE LAUNCHES IT";
                v.PostStrip = new StripView
                {
                    Has = true, Id = r.PostId, Code = r.Code, Stamp = r.Stamp, StampTone = r.Tone, Name = r.Name, Meta = r.Meta, Price = r.Price,
                    Brief = "TARGETS " + r.Targets + Dot + r.GridsFull, Gate = gate, GateTone = r.Urgent && !r.Launching ? AvState.Caution : r.Tone,
                    Primary = r.Launching ? "ARM" : claimPending ? "WAIT" : r.Armed ? "FIRE" : "ARM",
                    PrimaryEnabled = !r.Launching && !claimPending, PrimaryFire = r.Armed && !r.Launching, DisarmEnabled = armedPostId != 0
                };
            }
            else if (inspectedPost != 0)
            {
                v.PostGone = true;
                v.PostStrip = Idle("POST EXPIRED OR TAKEN", "THE PACKAGE YOU WERE REVIEWING IS NO LONGER ON THE BOARD", "PICK ANOTHER PACKAGE BELOW", AvState.Caution);
                v.PostStrip.DisarmEnabled = armedPostId != 0;
            }
            else
            {
                v.PostStrip = Idle("NO PACKAGES POSTED", "MARK CONTACTS IN ORBIT AND SEND THEM TO THE BOARD · THEY APPEAR HERE", "THE BOARD IS CLEAR", AvState.Inert);
                v.PostStrip.DisarmEnabled = armedPostId != 0;
            }
        }

        /// <summary>A strip with nothing to act on: a name, a line and a reason, buttons off.</summary>
        public static StripView Idle(string name, string brief, string gate, AvState tone) => new StripView
        {
            Has = true, Name = name, Brief = brief, Gate = gate, GateTone = tone, StampTone = tone, Primary = "ARM", Code = "", Stamp = "", Meta = "", Price = ""
        };

        // ---- Words and caches (bounded; filled on first use so the 0.2 s tick does not rebuild them) ----------------

        /// <summary>Post ids restart with every faction desk and every mission, so a cached entry is kept only with the points it was made from: a reused id never shows another post's grids.</summary>
        private sealed class PostGrids
        {
            public string Short, Full;
            public int Count;
            public float FirstX, FirstZ, LastX, LastZ;

            public bool Matches(FeedPoint[] pts)
            {
                int n = pts != null ? pts.Length : 0;
                return Count == n && (n == 0 || (FirstX == pts[0].X && FirstZ == pts[0].Z && LastX == pts[n - 1].X && LastZ == pts[n - 1].Z));
            }
        }
        private static readonly Dictionary<int, PostGrids> gridCache = new Dictionary<int, PostGrids>(16);
        private static readonly StringBuilder gridText = new StringBuilder(64);

        /// <summary>
        /// The grid squares of a post's targets (its points are fixed at SEND, so one list per post id is enough). Cached only once the map's
        /// grid labels exist: before that the kilometre fallback is shown but not kept.
        /// </summary>
        private static PostGrids Grids(in FeedPost post)
        {
            FeedPoint[] pts = post.Points;
            if (post.CallId != 0 && gridCache.TryGetValue(post.CallId, out PostGrids hit) && hit.Matches(pts)) return hit;
            var g = new PostGrids { Short = "—", Full = "—" };
            if (pts != null && pts.Length > 0)
            {
                g.Count = pts.Length; g.FirstX = pts[0].X; g.FirstZ = pts[0].Z; g.LastX = pts[pts.Length - 1].X; g.LastZ = pts[pts.Length - 1].Z;
                gridText.Length = 0;
                int shortLen = 0;
                for (int i = 0; i < pts.Length; i++)
                {
                    if (i > 0) gridText.Append(Dot);
                    gridText.Append(OpsKit.Grid(pts[i].X, pts[i].Z));
                    if (i == ShortGrids - 1) shortLen = gridText.Length;
                }
                g.Full = gridText.ToString();
                g.Short = pts.Length <= ShortGrids ? g.Full : g.Full.Substring(0, shortLen) + " +" + (pts.Length - ShortGrids);
            }
            if (post.CallId != 0 && SceneSingleton<DynamicMap>.i?.gridLabels != null)
            {
                if (gridCache.Count >= 48) gridCache.Clear();
                gridCache[post.CallId] = g;
            }
            return g;
        }

        private static readonly string[] apCache = new string[128];

        /// <summary>"n AP": allocation points, the personal currency (cached for the usual prices).</summary>
        public static string Ap(int n)
        {
            if (n < 0) n = 0;
            return n < apCache.Length ? apCache[n] ?? (apCache[n] = n + " AP") : n + " AP";
        }

        /// <summary>"10 ALLOC" (a perk tile's cost text) as "10 AP"; text without a leading number passes through.</summary>
        public static string Ap(string costText)
        {
            if (string.IsNullOrEmpty(costText)) return "";
            int n = 0, i = 0;
            while (i < costText.Length && i < 6 && costText[i] >= '0' && costText[i] <= '9') n = n * 10 + (costText[i++] - '0');
            return i > 0 ? Ap(n) : costText;
        }

        private static readonly Dictionary<string, string> lowAlloc = new Dictionary<string, string>(8);

        private static string LowAlloc(string needWord)
        {
            needWord = needWord ?? "";
            if (lowAlloc.TryGetValue(needWord, out string hit)) return hit;
            string s = "LOW ALLOC" + Dot + needWord.Replace(" ALLOC", " AP");
            if (lowAlloc.Count < 32) lowAlloc[needWord] = s;
            return s;
        }

        private static readonly Dictionary<SupportActionId, string> codes = new Dictionary<SupportActionId, string>(24);

        /// <summary>
        /// A three-letter brevity code derived from the action's name (initials of its words, else its first letters), so a new perk gets one
        /// without a table: RECON PASS = REC, SAT CAMERA = SCA, SAM NET DOWN = SND.
        /// </summary>
        public static string Code(SupportActionId id)
        {
            if (codes.TryGetValue(id, out string hit)) return hit;
            string n = id.ToString();
            int words = 0;
            for (int i = 0; i < n.Length; i++) if (i == 0 || char.IsUpper(n[i])) words++;
            var sb = new StringBuilder(3);
            if (words == 1) sb.Append(n, 0, Math.Min(3, n.Length));
            else if (words == 2)
            {
                int second = 1;
                while (second < n.Length && !char.IsUpper(n[second])) second++;
                sb.Append(n[0]).Append(n, second, Math.Min(2, n.Length - second));
            }
            else for (int i = 0; i < n.Length && sb.Length < 3; i++) if (i == 0 || char.IsUpper(n[i])) sb.Append(n[i]);
            string code = sb.ToString().ToUpperInvariant();
            if (codes.Count < 64) codes[id] = code;
            return code;
        }

        // Plain effect lines of the catalogue (SupportCatalog descriptions), radius and duration only where they state them.
        // ponytail: mirrors the catalogue text; point this at SupportCatalog.Find(id).Description through a manager accessor if the numbers ever move.
        private static readonly Dictionary<SupportActionId, string> briefs = new Dictionary<SupportActionId, string>
        {
            { SupportActionId.Recon, "RADAR BIRD REVEALS GROUND CONTACTS, STATIC AND MOVING · NEEDS A LIVE UPLINK" },
            { SupportActionId.SatCamera, "OPTICAL BIRD IMAGES THE AIM IN DAYLIGHT, REVEALING UNITS IN ITS WINDOW · LIVE UPLINK" },
            { SupportActionId.Prsm, "ONE OFFBOARD BALLISTIC MISSILE ONTO THE AIM" },
            { SupportActionId.Cruise, "SALVO OF OFFBOARD CRUISE MISSILES ONTO THE AIM · COUNTS AGAINST THE FACTION CAP" },
            { SupportActionId.Artillery, "KINETIC BIRD RELEASES ONE PHYSICAL ROD ONTO THE AIM · NEEDS A LIVE UPLINK" },
            { SupportActionId.ElintSweep, "STATION SIGINT LOCATES ENEMY GROUND RADARS THAT ARE EMITTING" },
            { SupportActionId.FlareMissile, "AIRBURST IR COUNTERMEASURE CLOUD AT THE AIM" },
            { SupportActionId.RadarBlind, "HOSTILE RADAR RANGE X0.4 WITHIN 1.5 KM OF THE AIM · 90 S" },
            { SupportActionId.SamNetDown, "HOSTILE SAMS CANNOT LAUNCH WITHIN 3 KM OF THE AIM · 60 S" },
            { SupportActionId.Emp, "HOSTILE RADAR BLACKOUT FOR 30 S · BATTERIES WIDEN THE PULSE" },
            { SupportActionId.JtacMark, "SOF TEAM LASES THE NEAREST HOSTILE AT THE MARK · LASER-GUIDED WEAPONS SEE IT" },
            { SupportActionId.ReconTeam, "SOF TEAM REVEALS ENEMY GROUND UNITS WITHIN 2 KM OF THE AIM FOR 5 MIN · NEEDS A LIVE CAMP" },
            { SupportActionId.Fortify, "REINFORCES A CONTROLLED ZONE WITH DEFENDERS" },
            { SupportActionId.SabotageStrike, "SOF TEAM SABOTAGES THE NEAREST ENEMY ANCHOR WITHIN 1 KM OF THE AIM · A ROLL DECIDES IT · NEEDS A LIVE CAMP" },
            { SupportActionId.Overwatch, "RADAR BIRD FOLLOWS THE MOVING ENEMY GROUND UNITS NEAREST THE AIM FOR 60 S · NEEDS A MOVER WITHIN 3 KM" },
            { SupportActionId.Starfall, "KINETIC BIRD WALKS 3 TO 5 SMALL RODS, 300 M APART, ALONG YOUR LINE THROUGH THE AIM" },
            { SupportActionId.Siphon, "TAKES FUNDS FROM THE STRONGEST ENEMY'S FRONT BUDGETS INTO YOURS · NO AIM NEEDED" },
            { SupportActionId.DirectAction, "A READY SOF TEAM RAIDS THE REVEALED SAM, RADAR OR ENEMY ANCHOR NEAREST THE AIM · 45 S, THEN A ROLL" },
        };

        public static string Brief(SupportActionId id) => briefs.TryGetValue(id, out string text) ? text : "";
    }
}

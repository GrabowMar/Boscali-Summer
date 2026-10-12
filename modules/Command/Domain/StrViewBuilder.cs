using System;
using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The one place that turns feeds into words, numbers and gates. Pure, never throws on a malformed feed. Unknown is a dash:
    /// every wire sentinel (Observed false, 255, 65535, -1, age below 0, Census.Known false) becomes NaN or -1 in the model and a dash in text.
    /// </summary>
    internal static partial class StrViewBuilder
    {
        /// <summary>Tickets the board draws under the hero lane. Three keep the first ACCEPT and the wave strip above the 520 px fold of the 596 px bay (annex 05 P9); the fallback if a fixture breaks the fold gate is two, never a lower fold line.</summary>
        public const int HeroTickets = 3;
        /// <summary>Seconds of waiting for a first report after which the host is taken not to answer.</summary>
        public const float NoAnswerSeconds = 30f;

        /// <summary>The 596 px bay is the smallest the panel is built for; the footer overlays its last 76 px, so everything that matters ends at or above FoldLine.</summary>
        public const float BayHeight = 596f, FoldLine = 520f;

        /// <summary>Tickets the hero lane draws: the first few of the model's sorties. The model keeps up to MaxSorties so the map can tag them all.</summary>
        public static int TicketsDrawn(StrViewModel vm) => vm == null ? 0 : Math.Min(vm.SortieCount, HeroTickets);

        /// <summary>Tied sorties the board leaves to the map ("+n MORE ON THE MAP"): the ones beyond the tickets drawn, kept or not.</summary>
        public static int MoreOnTheMap(StrViewModel vm) => vm == null ? 0 : vm.SortieMore + vm.SortieCount - TicketsDrawn(vm);

        /// <summary>
        /// Fills the model in place. True when something displayed changed (Revision bumped). The same stamp, whole second, ui and
        /// clock-driven flags (notice shown, replan armed) build nothing and allocate nothing, so the reader must change Stamp whenever any feed changed.
        /// </summary>
        public static bool Build(StrViewModel into, in StrFeeds feeds, in StrUiState ui, StrOrdinals platforms, StrOrdinals sorties, float nowUnscaled)
        {
            if (into == null || platforms == null || sorties == null) return false;

            long second = float.IsNaN(nowUnscaled) || float.IsInfinity(nowUnscaled) ? 0L : (long)Math.Floor(nowUnscaled);
            bool noticeOn = ui.Notice != null && nowUnscaled < ui.NoticeUntil;
            bool armed = nowUnscaled < ui.ReplanArmedUntil;
            // The decaying numbers (report age, time since publication, waiting, post feed age) enter the key by whole second, so a reader that
            // refreshes them every tick still builds once per second, not once per tick.
            int sinceKey = Sec(feeds.WarSincePublished), ageKey = Sec(feeds.WarAgeSeconds), waitKey = Sec(feeds.WaitSeconds), postsKey = Sec(feeds.PostsAgeSeconds);
            if (into.Built && into.BuiltStamp == feeds.Stamp && into.BuiltSecond == second && into.BuiltNotice == noticeOn &&
                into.BuiltArmed == armed && into.BuiltSince == sinceKey && into.BuiltAge == ageKey && into.BuiltWait == waitKey &&
                into.BuiltPostsAge == postsKey && SameUi(in into.BuiltUi, in ui))
                return false;

            var w = new Worker(into, in feeds, in ui, platforms, sorties, nowUnscaled, noticeOn, armed);
            w.Run();

            bool first = !into.Built;
            into.Built = true; into.BuiltStamp = feeds.Stamp; into.BuiltSecond = second;
            into.BuiltNotice = noticeOn; into.BuiltArmed = armed; into.BuiltUi = ui;
            into.BuiltSince = sinceKey; into.BuiltAge = ageKey; into.BuiltWait = waitKey; into.BuiltPostsAge = postsKey;
            if (!w.Changed && !first) return false;
            into.Revision++;
            return true;
        }

        private static bool SameUi(in StrUiState a, in StrUiState b) =>
            a.Selected == b.Selected && a.Hover == b.Hover && a.ShowFronts == b.ShowFronts && a.ShowOperation == b.ShowOperation &&
            a.ShowSorties == b.ShowSorties && a.ReplanArmedUntil.Equals(b.ReplanArmedUntil) && a.NoticeUntil.Equals(b.NoticeUntil) &&
            string.Equals(a.Notice, b.Notice, StringComparison.Ordinal);

        // ---- small shared helpers (the unknown mapping lives here) ------------------------------------------------------------

        /// <summary>Whole seconds of a decaying feed number, -1 when it is unknown.</summary>
        private static int Sec(float v) => float.IsNaN(v) || float.IsInfinity(v) || v < 0f ? -1 : (int)Math.Floor(Math.Min(v, 1e9f));

        private static float Fin(float v) => float.IsNaN(v) || float.IsInfinity(v) ? float.NaN : v;

        /// <summary>0..1 or NaN.</summary>
        private static float Unit(float v) => float.IsNaN(v) || float.IsInfinity(v) ? float.NaN : v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>A byte count where 255 means unknown: -1 for unknown.</summary>
        private static int Cnt(byte b) => b == 255 ? -1 : b;

        private static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

        private static string Count(int n) => n < 0 ? StrWords.Dash : N(n);

        private static string Clock(int seconds)
        {
            if (seconds < 0) return StrWords.Dash;
            return (seconds / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>"6 MIN OLD": a whole age in the largest unit that fits.</summary>
        private static string OldText(float seconds)
        {
            int s = (int)Math.Floor(Math.Max(0f, seconds));
            if (s < 60) return s.ToString(CultureInfo.InvariantCulture) + " S OLD";
            if (s < 3600) return (s / 60).ToString(CultureInfo.InvariantCulture) + " MIN OLD";
            return (s / 3600).ToString(CultureInfo.InvariantCulture) + " H OLD";
        }

        private static string TrendWords(float trend)
        {
            if (float.IsNaN(trend)) return "";
            if (trend >= 0.05f) return StrWords.Up + "RISING";
            if (trend <= -0.05f) return StrWords.Down + "FALLING";
            return StrWords.Level + "STEADY";
        }

        /// <summary>Splits "TITLE - detail" at the first " - "; a text without one is all title.</summary>
        private static void Split(string text, out string title, out string detail)
        {
            text = text ?? "";
            int at = text.IndexOf(" - ", StringComparison.Ordinal);
            if (at < 0) { title = text; detail = ""; return; }
            title = text.Substring(0, at);
            detail = text.Substring(at + 3);
        }

        private static T Get<T>(T[] rows, int i) where T : class, new() => rows[i] ?? (rows[i] = new T());

        // ---- the worker: one build, a struct on the stack, so a steady build never reaches here --------------------------------

        private partial struct Worker
        {
            private readonly StrViewModel m;
            private readonly StrFeeds f;
            private readonly StrUiState ui;
            private readonly StrOrdinals plat, sort;
            private readonly float now;
            private readonly bool noticeOn, armed;
            private TheaterWarSnapshot snap;
            private StrBoardMode mode;
            private bool stale;
            private int waveCost;
            private StrTri waveAfford;
            /// <summary>True when any displayed value differs from the last build.</summary>
            public bool Changed;

            public Worker(StrViewModel model, in StrFeeds feeds, in StrUiState state, StrOrdinals platforms, StrOrdinals sorties,
                          float nowUnscaled, bool noticeShown, bool replanArmed)
            {
                m = model; f = feeds; ui = state; plat = platforms; sort = sorties; now = nowUnscaled;
                noticeOn = noticeShown; armed = replanArmed;
                snap = null; mode = StrBoardMode.StaffOff; stale = false; waveCost = -1; waveAfford = StrTri.Unknown; Changed = false;
            }

            /// <summary>Seconds since the report was published, 0 when the engine did not say.</summary>
            private float Since => f.WarSincePublished > 0f && !float.IsInfinity(f.WarSincePublished) ? f.WarSincePublished : 0f;

            private void Put<T>(ref T field, T value)
            {
                if (EqualityComparer<T>.Default.Equals(field, value)) return;
                field = value;
                Changed = true;
            }

            private void PutAct(ref StrAct field, StrAct value)
            {
                if (field.Why == value.Why && field.Sent == value.Sent && string.Equals(field.Text, value.Text, StringComparison.Ordinal)) return;
                field = value;
                Changed = true;
            }

            public void Run()
            {
                mode = ResolveMode();
                bool hasSnap = mode == StrBoardMode.Operation || mode == StrBoardMode.Proposals || mode == StrBoardMode.Assessing ||
                               mode == StrBoardMode.NoOpening || mode == StrBoardMode.Reduced;
                snap = hasSnap ? f.War : null;
                stale = hasSnap && f.WarState == TheaterViewState.Stale;

                Header(hasSnap);
                WaveQuote();
                Fronts();
                Proposals();
                Effort();
                Sorties();
                Posts();
                Wave();
                Tiles();
                Sitrep();
                Log();
                Banner();
                Footer(hasSnap);
                Gates();
                Selection();
            }

            // Rule 1. A null snapshot is Waiting however the state reads; N-16: an operation or a deck wins over Reduced.
            private StrBoardMode ResolveMode()
            {
                if (!f.WarAvailable || f.WarState == TheaterViewState.StaffOff) return StrBoardMode.StaffOff;
                if (f.WarState == TheaterViewState.HostLacksModule) return StrBoardMode.HostLacksStaff;
                if (f.WarState == TheaterViewState.NoFaction) return StrBoardMode.NoFaction;
                TheaterWarSnapshot s = f.War;
                if (s == null || f.WarState == TheaterViewState.WaitingForHost)
                    return f.WaitSeconds >= NoAnswerSeconds ? StrBoardMode.NoAnswer : StrBoardMode.Waiting;
                if (s.Operation != null) return StrBoardMode.Operation;
                if (s.Proposals != null && s.Proposals.Length > 0) return StrBoardMode.Proposals;
                if (s.Reason == TheaterEmptyReason.NoGroundWar || s.Reason == TheaterEmptyReason.TacticsOff || s.Reason == TheaterEmptyReason.RtsOwnsOrders)
                    return StrBoardMode.Reduced;
                return s.Reason == TheaterEmptyReason.FirstReview ? StrBoardMode.Assessing : StrBoardMode.NoOpening;
            }

            // Rule 2: the age is the engine's own; the builder has no threshold.
            private void Header(bool hasSnap)
            {
                float age = !float.IsNaN(f.WarAgeSeconds) && !float.IsInfinity(f.WarAgeSeconds) && f.WarAgeSeconds >= 0f && hasSnap ? f.WarAgeSeconds : float.NaN;
                Put(ref m.Mode, mode);
                Put(ref m.Stale, stale);
                Put(ref m.AgeSeconds, age);
                Put(ref m.AgeText, TheaterReadout.Age(age));
                Put(ref m.CanCommand, hasSnap && f.WarCanCommand);
                Put(ref m.IsHost, f.IsHost);
                Put(ref m.Posture, snap != null ? snap.Posture : TheaterWarPosture.Cautious);
                Put(ref m.SortieActiveLimit, Math.Max(0, f.SortieActiveLimit));
                Put(ref m.SortiesAvailable, f.SortiesAvailable);
                Put(ref m.SortiesLive, f.SortiesAvailable && f.SortiesFresh);
                Put(ref m.SortieActionPending, f.SortieActionPending);
                Put(ref m.WarPending, f.WarPending);
                Put(ref m.LocalId, f.LocalId);
                Put(ref m.FactionCode, f.FactionCode ?? "");
            }

            // Rule 8's numbers are needed by the proposals too: one quote per faction, the same on every ticket (integration I13).
            private void WaveQuote()
            {
                TheaterWaveView w = snap?.Wave;
                waveCost = w != null && w.Cost >= 0 ? w.Cost : -1;
                waveAfford = w != null && w.Cost >= 0 && w.Pool >= 0 ? (w.Pool >= w.Cost ? StrTri.Yes : StrTri.No) : StrTri.Unknown;
            }

            // Rule 14: banner, countdown and checklist per mode.
            private void Banner()
            {
                StrBanner b = m.Banner;
                bool show = mode != StrBoardMode.Operation || stale;
                string chip = StrWords.Chip(mode), head = StrWords.Head(mode), why = "", @do = "";
                int count = -1; string label = "";
                float since = Since;
                int checkCount = 0;

                switch (mode)
                {
                    case StrBoardMode.Operation:
                        if (stale) { chip = "STALE"; head = StrWords.StaleReport(m.AgeSeconds); }
                        break;
                    case StrBoardMode.Proposals:
                        if (snap.StaffPickSeconds >= 0f)
                        {
                            count = Remaining(snap.StaffPickSeconds - since);
                            label = "AUTO-PICK " + RecommendedTag() + " IN";
                        }
                        break;
                    case StrBoardMode.Assessing:
                        Split(TheaterWords.Empty(TheaterEmptyReason.FirstReview, Math.Max(0f, snap.ReasonSeconds - since)), out why, out @do);
                        count = snap.ReasonSeconds > 0f ? Remaining(snap.ReasonSeconds - since) : -1;
                        label = count >= 0 ? "FIRST REVIEW IN" : "";
                        break;
                    case StrBoardMode.NoOpening:
                        Split(TheaterWords.Empty(snap.Reason, Math.Max(0f, snap.ReasonSeconds - since)), out why, out @do);
                        count = snap.ReasonSeconds > 0f ? Remaining(snap.ReasonSeconds - since) : -1;
                        label = count < 0 ? "" : snap.Reason == TheaterEmptyReason.CalledOff ? "BACK IN THE DECK IN" : "NEXT REVIEW IN";
                        checkCount = Checklist(b);
                        break;
                    case StrBoardMode.Reduced:
                        head = StrWords.ReducedHead(snap.Reason);
                        Split(TheaterWords.Empty(snap.Reason, 0f), out why, out @do);
                        break;
                    case StrBoardMode.StaffOff:
                        Split(TheaterWords.Empty(TheaterEmptyReason.StaffOff, 0f), out why, out @do);
                        break;
                    case StrBoardMode.HostLacksStaff:
                        Split(TheaterWords.Empty(TheaterEmptyReason.HostLacksModule, 0f), out why, out @do);
                        break;
                    case StrBoardMode.NoFaction:
                        Split(TheaterWords.Empty(TheaterEmptyReason.NoFaction, 0f), out why, out @do);
                        break;
                    case StrBoardMode.Waiting:
                        Split(TheaterWords.Empty(TheaterEmptyReason.WaitingForHost, 0f), out why, out @do);
                        count = Remaining(NoAnswerSeconds - Math.Max(0f, f.WaitSeconds));
                        label = "NO ANSWER IN";
                        break;
                    default:
                        why = StrWords.NoAnswerWhy(); @do = StrWords.NoAnswerDo();
                        break;
                }
                if (stale && mode != StrBoardMode.Operation && @do.Length == 0) @do = StrWords.StaleReport(m.AgeSeconds);

                Put(ref b.Show, show);
                Put(ref b.Chip, show ? chip : "");
                Put(ref b.Head, show ? head : "");
                Put(ref b.Why, show ? why : "");
                Put(ref b.Do, show ? @do : "");
                Put(ref b.CountSeconds, show ? count : -1);
                Put(ref b.CountLabel, show ? label : "");
                Put(ref b.CheckCount, checkCount);
            }

            // Whole seconds left of a countdown: counted up so it never reads 00:00 while something is still to come.
            private static int Remaining(float seconds) => float.IsNaN(seconds) || seconds <= 0f ? 0 : (int)Math.Ceiling(Math.Min(seconds, 100000f));

            private string RecommendedTag()
            {
                TheaterProposalView[] p = snap.Proposals;
                int n = p == null ? 0 : Math.Min(p.Length, StrViewModel.MaxProposals);
                for (int i = 0; i < n; i++) if (p[i] != null && p[i].Recommended) return "O" + (i + 1);
                return "O1";
            }

            private static readonly TheaterEmptyReason[] CheckOrder =
            {
                TheaterEmptyReason.StaffFault, TheaterEmptyReason.NoTerritoryField, TheaterEmptyReason.NoFronts,
                TheaterEmptyReason.NoFriendlyGroundNearFront, TheaterEmptyReason.FrontsQuiet, TheaterEmptyReason.PostureTooCautious,
            };

            // The no-opening checklist follows the engine's reason order (N-17). Rows before the blocking reason pass, the blocking row
            // fails with the engine's own sentence, the rest are not checked yet. A reason that is not on the list leaves every row unchecked.
            private int Checklist(StrBanner b)
            {
                TheaterEmptyReason reason = snap.Reason == TheaterEmptyReason.CalledOff ? TheaterEmptyReason.PostureTooCautious : snap.Reason;
                int block = Array.IndexOf(CheckOrder, reason);
                string engineDo = "";
                if (block >= 0) { Split(TheaterWords.Empty(snap.Reason, Math.Max(0f, snap.ReasonSeconds - Since)), out _, out engineDo); }
                for (int i = 0; i < CheckOrder.Length; i++)
                {
                    StrTri pass = block < 0 || i > block ? StrTri.Unknown : i < block ? StrTri.Yes : StrTri.No;
                    string todo = pass == StrTri.No ? engineDo : pass == StrTri.Unknown ? "NOT CHECKED YET" : "";
                    StrCheck c = b.Checks[i];
                    string label = StrWords.CheckLabel(CheckOrder[i]);
                    if (c.Pass != pass || c.Label != label || c.Do != todo) { b.Checks[i] = new StrCheck { Label = label, Do = todo, Pass = pass }; Changed = true; }
                }
                return CheckOrder.Length;
            }

            // Rule 13: line 1 is the mode's strip with the worst age of the feeds present, line 2 by precedence.
            private void Footer(bool hasSnap)
            {
                float worst = float.NaN;
                if (hasSnap && !float.IsNaN(m.AgeSeconds)) worst = m.AgeSeconds;
                if (f.PostsAvailable && !float.IsNaN(f.PostsAgeSeconds) && !float.IsInfinity(f.PostsAgeSeconds) && f.PostsAgeSeconds >= 0f)
                    worst = float.IsNaN(worst) ? f.PostsAgeSeconds : Math.Max(worst, f.PostsAgeSeconds);
                string age = hasSnap ? StrWords.Dot + "REPORT " + (float.IsNaN(worst) ? "AGE " + StrWords.Dash : TheaterReadout.Age(worst).ToUpperInvariant() + " OLD") : "";

                string status;
                switch (mode)
                {
                    case StrBoardMode.Operation:
                        status = "BOARD " + (stale ? "STALE" : "CURRENT") + age +
                                 (string.IsNullOrEmpty(f.FactionCode) ? "" : StrWords.Dot + "FACTION " + f.FactionCode) +
                                 StrWords.Dot + FrontCountText();
                        break;
                    case StrBoardMode.Proposals:
                        status = StrWords.Status(mode) + (m.Banner.CountSeconds >= 0
                            ? StrWords.Dot + "STAFF PICKS " + RecommendedTag() + " IN " + Clock(m.Banner.CountSeconds) + " IF NOBODY DOES" : "") + age;
                        break;
                    case StrBoardMode.Assessing:
                        status = StrWords.Status(mode) + (m.Banner.CountSeconds >= 0 ? " IN " + Clock(m.Banner.CountSeconds) : "") + age;
                        break;
                    case StrBoardMode.Reduced:
                        status = StrWords.Status(mode) + StrWords.Dot + m.Banner.Head + age;
                        break;
                    case StrBoardMode.NoOpening:
                        status = StrWords.Status(mode) + age;
                        break;
                    default:
                        status = StrWords.Status(mode);
                        break;
                }
                if (f.SortiesAvailable && !f.SortiesFresh) status += StrWords.Dot + "SORTIES STALE";

                string note = null;
                if (!string.IsNullOrEmpty(f.PickerPrompt)) note = f.PickerPrompt;
                if (note == null && f.WarLast != null && f.WarLast.Code != TheaterCommandCode.None)
                {
                    string said = TheaterWords.Command(f.WarLast.Code, f.WarLast.Detail, f.WarLast.LockSeconds);
                    if (said.Length > 0) note = said;
                }
                if (note == null && noticeOn && ui.Notice.Length > 0) note = ui.Notice;
                Put(ref m.FooterStatus, status);
                Put(ref m.FooterNote, note ?? StrWords.Flavour(mode));
            }

            private string FrontCountText()
            {
                int n = snap?.Fronts == null ? 0 : Math.Min(snap.Fronts.Length, StrViewModel.MaxFronts);
                return n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " PLATFORM" : " PLATFORMS");
            }

            // The rows carry their own gate result, so a part never calls a gate itself. Everything a gate reads is filled by now.
            private void Gates()
            {
                for (int i = 0; i < m.ProposalCount; i++)
                    PutAct(ref m.Proposals[i].Pick, StrGate.Check(m, StrActionKind.Pick, m.Proposals[i].Id));

                bool live = m.Effort.Live;
                PutAct(ref m.Effort.Replan, StrGate.Check(m, StrActionKind.Replan, m.Effort.Id));
                Put(ref m.Effort.ReplanArmed, live && armed);

                for (int i = 0; i < m.FrontCount; i++)
                {
                    StrFrontRow r = m.Fronts[i];
                    StrAct redirect = !live ? new StrAct(StrWhy.NoOperation, "NO OPERATION RUNNING" + StrWords.Dot + "PICK A PROPOSAL FIRST")
                        : r.Offer.IsNone ? new StrAct(StrWhy.Gone, "THE STAFF HAS NO OFFER FOR THIS PLATFORM")
                        : StrGate.Check(m, StrActionKind.Pick, r.Offer);
                    PutAct(ref r.Redirect, redirect);
                }

                for (int i = 0; i < m.SortieCount; i++)
                {
                    StrSortieRow r = m.Sorties[i];
                    PutAct(ref r.Accept, StrGate.Check(m, StrActionKind.Accept, r.Id));
                    bool tasked = r.HasWing && (r.WingState == WingSortieState.Tasked || r.WingState == WingSortieState.OnStation);
                    PutAct(ref r.Wing, StrGate.WingGate(m.IsHost, m.SortiesLive, r, tasked));
                }
            }

            // Rule 15: a selection survives only while its row exists.
            private void Selection()
            {
                bool gone = false;
                StrItemId sel = Exists(ui.Selected) ? ui.Selected : StrItemId.None;
                StrItemId hov = Exists(ui.Hover) ? ui.Hover : StrItemId.None;
                if (!ui.Selected.IsNone && sel.IsNone) gone = true;
                if (!ui.Hover.IsNone && hov.IsNone) gone = true;
                Put(ref m.SelectedId, sel);
                Put(ref m.HoverId, hov);
                Put(ref m.SelectionVanished, gone);
            }

            private bool Exists(StrItemId id)
            {
                switch (id.Kind)
                {
                    case StrItemKind.None: return true;
                    case StrItemKind.Effort: return m.Effort.Live && m.Effort.Id == id;
                    case StrItemKind.Proposal: for (int i = 0; i < m.ProposalCount; i++) if (m.Proposals[i].Id == id) return true; return false;
                    case StrItemKind.Front: for (int i = 0; i < m.FrontCount; i++) if (m.Fronts[i].Id == id) return true; return false;
                    case StrItemKind.Sortie: for (int i = 0; i < m.SortieCount; i++) if (m.Sorties[i].Id == id) return true; return false;
                    case StrItemKind.Post: for (int i = 0; i < m.PostCount; i++) if (m.Posts[i].Id == id) return true; return false;
                    default: return false;
                }
            }
        }
    }
}

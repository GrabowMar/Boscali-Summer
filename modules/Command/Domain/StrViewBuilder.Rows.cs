using System;
using System.Globalization;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    internal static partial class StrViewBuilder
    {
        private partial struct Worker
        {
            // ---- rule 4: fronts ----------------------------------------------------------------------------------------------

            private void Fronts()
            {
                if (mode == StrBoardMode.Reduced) { AirLane(); return; }

                int shown = 0;
                TheaterFrontView[] fr = snap?.Fronts;
                if (fr != null)
                {
                    int cnt = Math.Min(fr.Length, StrViewModel.MaxFronts);
                    // The operation's own front is the hero lane's pressure, not a compact lane.
                    string opKey = mode == StrBoardMode.Operation ? snap.Operation.TargetKey : null;
                    Span<int> order = stackalloc int[StrViewModel.MaxFronts];
                    for (int i = 0; i < cnt; i++)
                    {
                        TheaterFrontView fv = fr[i];
                        if (fv == null) continue;
                        plat.Of(fv.Key ?? "");                       // a place keeps its number whether or not it is listed
                        if (!string.IsNullOrEmpty(opKey) && fv.Key == opKey) continue;
                        int at = shown;
                        while (at > 0 && FrontBefore(fv, fr[order[at - 1]])) { order[at] = order[at - 1]; at--; }
                        order[at] = i;
                        shown++;
                    }
                    for (int k = 0; k < shown; k++) FillFront(Get(m.Fronts, k), fr[order[k]]);
                }
                Put(ref m.FrontCount, shown);
            }

            private float FrontPressure(TheaterFrontView fv) => fv.Observed ? Unit(fv.Pressure) : float.NaN;

            // Pressure descending, unknown last, then platform number ascending.
            private bool FrontBefore(TheaterFrontView a, TheaterFrontView b)
            {
                float pa = FrontPressure(a), pb = FrontPressure(b);
                bool ua = float.IsNaN(pa), ub = float.IsNaN(pb);
                if (ua != ub) return ub;
                if (!ua && pa != pb) return pa > pb;
                return plat.Of(a.Key ?? "") < plat.Of(b.Key ?? "");
            }

            private void FillFront(StrFrontRow r, TheaterFrontView fv)
            {
                int number = plat.Of(fv.Key ?? "");
                bool seen = fv.Observed;
                float pressure = FrontPressure(fv);
                float trend = fv.Observed && float.IsFinite(fv.Trend) ? Math.Max(-1f, Math.Min(1f, fv.Trend)) : float.NaN;

                string status;
                StrInkRole ink;
                if (!seen)
                {
                    status = "NOT OBSERVED" + (fv.ObservedAgeSeconds >= 0f ? StrWords.Dot + OldText(fv.ObservedAgeSeconds) : "");
                    ink = StrInkRole.Muted;
                }
                else if (fv.Status == TheaterFrontStatus.InContact) { status = "IN CONTACT"; ink = StrInkRole.Danger; }
                else if (fv.Status == TheaterFrontStatus.UnderPressure) { status = "UNDER PRESSURE"; ink = StrInkRole.Caution; }
                else if (fv.Status == TheaterFrontStatus.Unconfirmed) { status = "NO UNITS NEAR"; ink = StrInkRole.Muted; }
                else { status = !float.IsNaN(pressure) && pressure < 0.25f ? "QUIET" : "HOLDING"; ink = StrInkRole.Own; }

                StrItemId offer = StrItemId.None;
                string offerText = seen ? "NO OFFER" : "NO REPORT";
                TheaterProposalView[] props = snap.Proposals;
                int pc = props == null ? 0 : Math.Min(props.Length, StrViewModel.MaxProposals);
                for (int i = 0; i < pc; i++)
                {
                    TheaterProposalView p = props[i];
                    if (p == null || string.IsNullOrEmpty(p.TargetKey) || p.TargetKey != fv.Key) continue;
                    offer = new StrItemId(StrItemKind.Proposal, p.Id);
                    offerText = "O" + (i + 1) + " " + TheaterWords.Kind(p.Kind) + StrWords.Dot + RiskWord(p.Risk);
                    break;
                }

                Put(ref r.Id, new StrItemId(StrItemKind.Front, number));
                Put(ref r.Tag, plat.Tag(number));
                Put(ref r.Name, fv.Name ?? "");
                Put(ref r.X, Fin(fv.X));
                Put(ref r.Z, Fin(fv.Z));
                Put(ref r.Pressure, pressure);
                Put(ref r.Trend, trend);
                Put(ref r.Observed, fv.Observed);
                Put(ref r.PressureText, TheaterReadout.Percent(pressure));
                Put(ref r.TrendText, TrendWords(trend));
                Put(ref r.StatusText, status);
                Put(ref r.Ink, ink);
                Put(ref r.Offer, offer);
                Put(ref r.OfferText, offerText);
            }

            // A mission with no ground war: one air picture lane from the census (hostile share of tracked air).
            private void AirLane()
            {
                TheaterCensus c = f.Census;
                float share = c.Known && c.AirFriendly >= 0 && c.AirHostileTracked >= 0 ? 1f - TheaterReadout.AlliedShare(c.AirFriendly, c.AirHostileTracked) : float.NaN;
                StrFrontRow r = Get(m.Fronts, 0);
                Put(ref r.Id, new StrItemId(StrItemKind.Front, 0));
                Put(ref r.Tag, "");
                Put(ref r.Name, "AIR PICTURE");
                Put(ref r.X, float.NaN);
                Put(ref r.Z, float.NaN);
                Put(ref r.Pressure, share);
                Put(ref r.Trend, float.NaN);
                Put(ref r.Observed, !float.IsNaN(share));
                Put(ref r.PressureText, TheaterReadout.Percent(share));
                Put(ref r.TrendText, "");
                Put(ref r.StatusText, float.IsNaN(share) ? "NOT MEASURED" : "TRACKED AIR ONLY");
                Put(ref r.Ink, float.IsNaN(share) ? StrInkRole.Muted : StrInkRole.Own);
                Put(ref r.Offer, StrItemId.None);
                Put(ref r.OfferText, "NO OFFER");
                Put(ref m.FrontCount, 1);
            }

            // N-04: TheaterWords.Risk is the only table; its unknown hyphen becomes the dash every surface uses.
            private static string RiskWord(TheaterRisk risk) => risk == TheaterRisk.Unknown ? StrWords.Dash : TheaterWords.Risk(risk);

            // ---- rule 5: proposals -------------------------------------------------------------------------------------------

            private void Proposals()
            {
                TheaterProposalView[] props = snap?.Proposals;
                int cnt = props == null ? 0 : Math.Min(props.Length, StrViewModel.MaxProposals);
                int shown = 0;
                for (int i = 0; i < cnt; i++)
                {
                    TheaterProposalView p = props[i];
                    if (p == null) continue;
                    StrProposalRow r = Get(m.Proposals, shown);
                    int front = FrontNumber(p.TargetKey);
                    TheaterFrontView fv = front > 0 ? FrontByKey(p.TargetKey) : null;
                    string tag = front > 0 ? plat.Tag(front) : "";
                    string cost = waveCost >= 0 ? N(waveCost) : StrWords.Dash;

                    Put(ref r.Id, new StrItemId(StrItemKind.Proposal, p.Id));
                    Put(ref r.ProposalId, p.Id);
                    Put(ref r.Revision, p.Revision);
                    Put(ref r.Tag, "O" + (shown + 1));
                    Put(ref r.KindText, TheaterWords.Kind(p.Kind));
                    Put(ref r.Name, p.Name ?? "");
                    Put(ref r.RiskText, RiskWord(p.Risk));
                    Put(ref r.Aim, (tag.Length > 0 ? tag + StrWords.Dot : "") + Count(Cnt(p.FriendlyGround)) + " FRIENDLY NEARBY, " + Count(Cnt(p.HostileTracked)) + " TRACKED HOSTILE.");
                    Put(ref r.WaveText, "WAVE " + cost + " " + (waveAfford == StrTri.Yes ? StrWords.Yes : waveAfford == StrTri.No ? StrWords.No : "?"));
                    Put(ref r.Kind, p.Kind);
                    Put(ref r.Risk, p.Risk);
                    Put(ref r.X, Fin(p.X));
                    Put(ref r.Z, Fin(p.Z));
                    Put(ref r.Radius, !float.IsFinite(p.Radius) ? 0f : Math.Max(0f, Math.Min(30000f, p.Radius)));
                    Put(ref r.Pressure, fv != null ? FrontPressure(fv) : float.NaN);
                    Put(ref r.Front, front > 0 ? new StrItemId(StrItemKind.Front, front) : StrItemId.None);
                    Put(ref r.Recommended, p.Recommended);
                    Put(ref r.WaveAffordable, waveAfford);
                    shown++;
                }
                Put(ref m.ProposalCount, shown);
            }

            private TheaterFrontView FrontByKey(string key)
            {
                TheaterFrontView[] fr = snap?.Fronts;
                if (fr == null || string.IsNullOrEmpty(key)) return null;
                int n = Math.Min(fr.Length, StrViewModel.MaxFronts);
                for (int i = 0; i < n; i++) if (fr[i] != null && fr[i].Key == key) return fr[i];
                return null;
            }

            // The platform number of a front key, 0 when the key is not a listed front (a lookup never invents a number).
            private int FrontNumber(string key) => FrontByKey(key) != null ? plat.Of(key) : 0;

            // ---- rule 6: the live operation ----------------------------------------------------------------------------------

            private void Effort()
            {
                TheaterOperationView op = mode == StrBoardMode.Operation ? snap.Operation : null;
                StrEffortView e = m.Effort;
                bool live = op != null;

                TheaterForcesView fo = live ? op.Forces : null;
                int steps = live ? StrWords.RouteSteps(op.Kind) : 0, step = 0;
                bool alarm = false;
                if (live)
                {
                    switch (op.Phase)
                    {
                        case TheaterPhase.Forming: step = 1; break;
                        case TheaterPhase.Advancing: step = 2; break;
                        case TheaterPhase.InContact: step = 3; break;
                        case TheaterPhase.Holding: step = 2; break;
                        case TheaterPhase.Scouting: step = 2; break;
                        case TheaterPhase.Withdrawing: step = steps; alarm = true; break;
                    }
                    step = Math.Min(step, steps);
                }

                int lockLeft = -1;
                if (live && op.LockSeconds > 0f && float.IsFinite(op.LockSeconds))
                {
                    float left = op.LockSeconds - Since;
                    lockLeft = left <= 0f ? 0 : (int)Math.Floor(left);
                }

                TheaterFrontView front = live ? FrontByKey(op.TargetKey) : null;
                float pressure = front != null ? FrontPressure(front) : float.NaN;
                float trend = front != null && front.Observed && float.IsFinite(front.Trend) ? Math.Max(-1f, Math.Min(1f, front.Trend)) : float.NaN;
                bool byLocal = live && f.LocalId != 0UL && op.PickedById == f.LocalId;
                string by = live && !op.PickedByStaff && !string.IsNullOrEmpty(op.PickedBy) ? op.PickedBy : "";

                string phase = "";
                if (live)
                {
                    phase = TheaterWords.Phase(op.Phase, op.PhaseNote);
                    if (op.Phase != TheaterPhase.Awaiting && op.PhaseNote != TheaterPhaseNote.None)
                        phase += StrWords.Dot + (op.PhaseNote == TheaterPhaseNote.NoSteering ? "NO STEERING" : "NO FORCES");
                }

                Put(ref e.Live, live);
                Put(ref e.Id, live ? new StrItemId(StrItemKind.Effort, op.Id) : StrItemId.None);
                Put(ref e.OperationId, live ? op.Id : 0);
                Put(ref e.Revision, live ? op.Revision : 0);
                Put(ref e.Kind, live ? op.Kind : TheaterOpKind.Assault);
                Put(ref e.Tag, live ? "01" : "");
                Put(ref e.KindText, live ? "MAIN EFFORT" : "");
                Put(ref e.Name, live ? op.Name ?? "" : "");
                Put(ref e.FrontTag, front != null ? plat.Tag(plat.Of(front.Key)) : "");
                Put(ref e.PhaseText, phase);
                Put(ref e.PickedText, !live ? "" : op.PickedByStaff ? "SET BY STAFF" : by.Length > 0 ? "SET BY " + by.ToUpperInvariant() : "");
                Put(ref e.PickedBy, by);
                Put(ref e.RouteSteps, steps);
                Put(ref e.RouteStep, step);
                Put(ref e.Alarm, alarm);
                Put(ref e.LockSeconds, lockLeft);
                Put(ref e.LockText, lockLeft > 0 ? Clock(lockLeft) : "");
                Put(ref e.PickedByLocal, byLocal);
                Put(ref e.PickedByStaff, live && op.PickedByStaff);
                Put(ref e.X, live ? Fin(op.X) : float.NaN);
                Put(ref e.Z, live ? Fin(op.Z) : float.NaN);
                Put(ref e.OriginX, live ? Fin(op.OriginX) : float.NaN);
                Put(ref e.OriginZ, live ? Fin(op.OriginZ) : float.NaN);
                Put(ref e.Radius, live && float.IsFinite(op.Radius) ? Math.Max(0f, Math.Min(30000f, op.Radius)) : 0f);
                Put(ref e.Pressure, pressure);
                Put(ref e.Trend, trend);
                Put(ref e.PressureText, TheaterReadout.Percent(pressure));
                Put(ref e.TrendText, TrendWords(trend));
                Put(ref e.Advancing, live && op.Phase == TheaterPhase.Advancing);
                Put(ref e.VehiclesText, fo == null || Cnt(fo.Vehicles) < 0 ? StrWords.Dash : Cnt(fo.Vehicles) + " VEH");
                Put(ref e.GroupsText, GroupsText(fo));
                Put(ref e.MovementText, fo == null ? StrWords.Dash :
                    "MOVING " + Count(Cnt(fo.Moving)) + StrWords.Dot + "ON SLOT " + Count(Cnt(fo.OnSlot)) + StrWords.Dot + "STALLED " + Count(Cnt(fo.Stalled)));
                Put(ref e.EtaText, "ETA " + (fo == null || fo.EtaSeconds == ushort.MaxValue ? StrWords.Dash : Clock(fo.EtaSeconds)));
                Marks(e, fo, live ? op : null);
            }

            // Groups, with the stage counts that are known and above zero ("3 GRP · 2 ASSAULT · 1 CONTACT"); never one "assigned" figure (D7).
            private static string GroupsText(TheaterForcesView fo)
            {
                if (fo == null || Cnt(fo.Groups) < 0) return StrWords.Dash;
                string t = Cnt(fo.Groups) + " GRP";
                t += Stage(fo.Rally, "RALLY") + Stage(fo.Line, "LINE") + Stage(fo.Assault, "ASSAULT") + Stage(fo.Contact, "CONTACT") + Stage(fo.Withdraw, "WITHDRAW");
                return t;
            }

            private static string Stage(byte count, string word) => count == 255 || count == 0 ? "" : StrWords.Dot + count + " " + word;

            // Marks: nearest the fix first (the engine sorts them; a stable insertion sort makes it true of a malformed feed too), G1..G8.
            private void Marks(StrEffortView e, TheaterForcesView fo, TheaterOperationView op)
            {
                TheaterForceMark[] marks = fo?.Marks;
                int cnt = op == null || marks == null ? 0 : Math.Min(marks.Length, StrViewModel.MaxMarks);
                Span<int> order = stackalloc int[StrViewModel.MaxMarks];
                Span<float> dist = stackalloc float[StrViewModel.MaxMarks];
                int shown = 0;
                for (int i = 0; i < cnt; i++)
                {
                    TheaterForceMark mk = marks[i];
                    if (mk == null) continue;
                    float dx = Fin(mk.X) - Fin(op.X), dz = Fin(mk.Z) - Fin(op.Z);
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);          // NaN when either end is unknown
                    dist[i] = d;
                    int at = shown;
                    while (at > 0 && Nearer(d, dist[order[at - 1]])) { order[at] = order[at - 1]; at--; }
                    order[at] = i;
                    shown++;
                }
                for (int k = 0; k < shown; k++)
                {
                    TheaterForceMark mk = marks[order[k]];
                    float d = dist[order[k]];
                    var row = new StrGroupRow
                    {
                        Tag = "G" + (k + 1), DistanceText = float.IsNaN(d) ? StrWords.Dash : TheaterReadout.Kilometres(d).ToUpperInvariant(),
                        X = Fin(mk.X), Z = Fin(mk.Z), DistanceM = d, Stage = mk.Stage, Alive = mk.Alive,
                    };
                    StrGroupRow old = e.Marks[k];
                    if (old.Tag != row.Tag || old.DistanceText != row.DistanceText || !old.X.Equals(row.X) || !old.Z.Equals(row.Z) ||
                        !old.DistanceM.Equals(row.DistanceM) || old.Stage != row.Stage || old.Alive != row.Alive)
                    {
                        e.Marks[k] = row;
                        Changed = true;
                    }
                }
                Put(ref e.MarkCount, shown);
            }

            private static bool Nearer(float a, float b) => !float.IsNaN(a) && (float.IsNaN(b) || a < b);

            // ---- rule 7: sorties tied to the operation -----------------------------------------------------------------------

            private void Sorties()
            {
                var list = f.Sorties;
                int total = list == null ? 0 : list.Count, active = 0, tied = 0, kept = 0;
                Span<int> best = stackalloc int[StrViewModel.MaxSorties];
                bool board = mode == StrBoardMode.Operation;
                for (int i = 0; i < total; i++)
                {
                    SecondaryObjectiveView c = list[i];
                    if (c == null) continue;
                    if (c.IsActive) active++;
                    if (!board || c.Link == null || !c.Link.Tied || !c.Link.OperationLive) continue;
                    tied++;
                    int at = kept < best.Length ? kept : best.Length;
                    // Accepted first, then least time left, then id; a full table drops the card that sorts last.
                    while (at > 0 && CardBefore(c, list[best[at - 1]])) at--;
                    if (at >= best.Length) continue;
                    int end = Math.Min(kept, best.Length - 1);
                    for (int k = end; k > at; k--) best[k] = best[k - 1];
                    best[at] = i;
                    if (kept < best.Length) kept++;
                }
                Put(ref m.SortieActiveCount, active);
                Put(ref m.SortieMore, tied - kept);
                for (int k = 0; k < kept; k++) FillSortie(Get(m.Sorties, k), list[best[k]]);
                Put(ref m.SortieCount, kept);
            }

            private static bool CardBefore(SecondaryObjectiveView a, SecondaryObjectiveView b)
            {
                if (a.IsActive != b.IsActive) return a.IsActive;
                float ta = a.SecondsRemaining > 0f ? a.SecondsRemaining : float.MaxValue, tb = b.SecondsRemaining > 0f ? b.SecondsRemaining : float.MaxValue;
                if (ta != tb) return ta < tb;
                return a.Id < b.Id;
            }

            private void FillSortie(StrSortieRow r, SecondaryObjectiveView c)
            {
                SortieLink link = c.Link;
                ObjectiveTasking t = c.Tasking;
                // The card's own site before acceptance lives in the link (offered cards too); the marker is the fallback for an untied card.
                bool linkSite = link != null && link.SiteKnown;
                float x = Fin(linkSite ? link.X : c.HasMarker ? c.X : float.NaN);
                float z = Fin(linkSite ? link.Z : c.HasMarker ? c.Z : float.NaN);
                float radius = linkSite ? link.Radius : c.HasMarker ? c.Radius : 0f;
                bool siteKnown = !float.IsNaN(x) && !float.IsNaN(z);
                radius = !siteKnown || !float.IsFinite(radius) ? 0f : Math.Max(0f, Math.Min(SortieLink.MaximumRadius, radius));

                StrWingIn wing = default; bool hasWing = false;
                for (int i = 0; i < f.WingCount && f.Wing != null && i < f.Wing.Length; i++)
                    if (f.Wing[i].SortieId == c.Id) { wing = f.Wing[i]; hasWing = true; break; }
                bool tasked = hasWing && (wing.State == WingSortieState.Tasked || wing.State == WingSortieState.OnStation);

                string wingText = "";
                if (hasWing)
                {
                    switch (wing.State)
                    {
                        case WingSortieState.Tasked:
                        case WingSortieState.OnStation: wingText = (wing.Words ?? "") + (link != null && link.WingCounts ? StrWords.Dot + "COUNTS" : ""); break;
                        case WingSortieState.Done: wingText = "WING DONE"; break;
                        case WingSortieState.Failed: wingText = "WING LOST"; break;
                        case WingSortieState.Recalled: wingText = "WING RECALLED"; break;
                    }
                }

                int number = sort.Of(c.Id);
                Put(ref r.Id, new StrItemId(StrItemKind.Sortie, c.Id));
                Put(ref r.ContractId, c.Id);
                Put(ref r.PostId, t != null && t.SubjectId >= 0 ? t.SubjectId : -1);
                Put(ref r.Tag, sort.Tag(number));
                Put(ref r.ChipText, StrWords.Family(t != null ? t.Family : ObjectiveFamily.Unknown));
                Put(ref r.Title, c.Title ?? "");
                Put(ref r.DistanceText, SiteDistance(siteKnown, x, z));
                Put(ref r.RewardText, Reward(c.Money, c.Xp));
                Put(ref r.TimeText, c.SecondsRemaining > 0f && !float.IsInfinity(c.SecondsRemaining) ? Clock((int)Math.Floor(Math.Min(c.SecondsRemaining, 359999f))) : StrWords.Dash);
                Put(ref r.StateText, c.IsActive ? "ACCEPTED" : c.IsOffered ? "OFFERED" : c.IsComplete ? "DONE" : "");
                Put(ref r.ByText, string.IsNullOrEmpty(c.AcceptedBy) ? "" : "BY " + c.AcceptedBy.ToUpperInvariant());
                Put(ref r.Offered, c.IsOffered);
                Put(ref r.Active, c.IsActive);
                Put(ref r.SiteKnown, siteKnown);
                Put(ref r.X, x);
                Put(ref r.Z, z);
                Put(ref r.Radius, radius);
                Put(ref r.Progress, c.IsActive ? Unit(c.Progress) : float.NaN);
                Put(ref r.WingLabel, StrWords.WingLabel(hasWing ? wing.Intent : WingSortieIntent.None, tasked));
                Put(ref r.WingText, wingText);
                Put(ref r.WingRefusal, hasWing && wing.Refusal != null ? wing.Refusal : "");
                Put(ref r.HasWing, hasWing);
                Put(ref r.WingState, hasWing ? wing.State : WingSortieState.None);
            }

            // Distance and bearing from the local aircraft to the site: a dash unless both ends are known.
            private string SiteDistance(bool siteKnown, float x, float z)
            {
                if (!siteKnown || !f.HasOwn || !float.IsFinite(f.OwnX) || !float.IsFinite(f.OwnZ)) return StrWords.Dash;
                float dx = x - f.OwnX, dz = z - f.OwnZ;
                float km = (float)Math.Sqrt(dx * dx + dz * dz) / 1000f;
                int bearing = (int)Math.Round((Math.Atan2(dx, dz) * 180.0 / Math.PI + 360.0) % 360.0);
                if (bearing == 360) bearing = 0;
                return (km < 1f ? "<1" : ((int)Math.Round(km)).ToString(CultureInfo.InvariantCulture)) + " KM " + bearing.ToString("000", CultureInfo.InvariantCulture) + StrWords.Degree;
            }

            private static string Reward(int money, int xp)
            {
                if (money <= 0 && xp <= 0) return StrWords.Dash;
                return (money > 0 ? N(money) : "") + (money > 0 && xp > 0 ? " " : "") + (xp > 0 ? "+" + N(xp) + " XP" : "");
            }

            // ---- rule 9: enemy posts -----------------------------------------------------------------------------------------

            private void Posts()
            {
                int cnt = f.PostsAvailable && f.Posts != null ? Math.Min(f.PostCount, f.Posts.Length) : 0;
                Span<int> best = stackalloc int[StrViewModel.MaxPosts];
                int kept = 0;
                for (int i = 0; i < cnt; i++)
                {
                    int at = kept < best.Length ? kept : best.Length;
                    while (at > 0 && PostBefore(f.Posts[i], f.Posts[best[at - 1]])) at--;
                    if (at >= best.Length) continue;
                    int end = Math.Min(kept, best.Length - 1);
                    for (int k = end; k > at; k--) best[k] = best[k - 1];
                    best[at] = i;
                    if (kept < best.Length) kept++;
                }
                for (int k = 0; k < kept; k++)
                {
                    StrPostIn p = f.Posts[best[k]];
                    StrPostRow r = Get(m.Posts, k);
                    StrItemId ticket = StrItemId.None;
                    for (int s = 0; s < m.SortieCount; s++) if (m.Sorties[s].PostId == p.Id) { ticket = m.Sorties[s].Id; break; }

                    Put(ref r.Id, new StrItemId(StrItemKind.Post, p.Id));
                    Put(ref r.PostId, p.Id);
                    Put(ref r.Name, p.Name ?? "");
                    Put(ref r.Role, p.Role ?? "");
                    Put(ref r.Site, p.Site ?? "");
                    Put(ref r.BountyText, "BOUNTY " + (p.Bounty >= 0 ? N(p.Bounty) : StrWords.Dash) + (p.BountyCapped ? StrWords.Dot + "CAPPED" : ""));
                    Put(ref r.TraitText, p.Trait ?? "");
                    Put(ref r.SeenText, SeenWords(p.IntelAge));
                    Put(ref r.Short, "DOSSIER");
                    Put(ref r.Capped, p.BountyCapped);
                    Put(ref r.X, Fin(p.X));
                    Put(ref r.Z, Fin(p.Z));
                    Put(ref r.Ticket, ticket);
                }
                Put(ref m.PostCount, kept);
            }

            // A row never claims to be fresher than it is: its own age plus the age of the feed that carried it.
            private string SeenWords(float intelAge)
            {
                if (!float.IsFinite(intelAge) || intelAge < 0f) return "SEEN " + StrWords.Dash;
                if (intelAge >= 600f) return "SEEN 10+ MIN AGO";
                float seen = intelAge + (!float.IsFinite(f.PostsAgeSeconds) ? 0f : Math.Max(0f, f.PostsAgeSeconds));
                return "SEEN " + OldText(seen).Replace(" OLD", " AGO");
            }

            // Bounty descending, then id: the richest office first, the same order on every build.
            private static bool PostBefore(StrPostIn a, StrPostIn b) => a.Bounty != b.Bounty ? a.Bounty > b.Bounty : a.Id < b.Id;

            // ---- rule 8: the reinforcement wave ------------------------------------------------------------------------------

            // The game's own contribute menu, copied: the pilot's funds, each group's price and the host's cooldown. Funds round down and prices up, so
            // a group that reads affordable is. An unknown stays unknown (a cooldown the host has not answered is not a zero).
            private void Supply()
            {
                StrSupplyView v = m.Supply;
                bool known = f.SupplyKnown && float.IsFinite(f.Allocation);
                float funds = known ? Math.Max(0f, f.Allocation) : 0f;
                int count = known && f.Convoys != null ? Math.Min(Math.Min(f.ConvoyCount, f.Convoys.Length), StrViewModel.MaxConvoys) : 0;
                Put(ref v.Known, known);
                Put(ref v.Forbidden, known && f.DonationForbidden);
                Put(ref v.Funds, funds);
                Put(ref v.FundsText, known ? N(Whole(funds, false)) : StrWords.Dash);
                Put(ref v.DonateCooldown, known ? SecUp(f.DonateCooldown) : -1);
                for (int i = 0; i < count; i++)
                {
                    StrConvoyIn c = f.Convoys[i];
                    StrSupplyRow r = Get(v.Rows, i);
                    bool priced = float.IsFinite(c.Cost) && c.Cost >= 0f;
                    int wait = SecUp(c.Cooldown);
                    bool ready = priced && wait == 0 && c.Cost <= funds && !f.DonationForbidden;
                    Put(ref r.Name, (c.Name ?? "").ToUpperInvariant());
                    Put(ref r.Detail, (c.Composition ?? "").ToUpperInvariant());
                    Put(ref r.Cost, priced ? c.Cost : float.NaN);
                    Put(ref r.CostText, priced ? N(Whole(c.Cost, true)) : StrWords.Dash);
                    Put(ref r.Cooldown, wait);
                    Put(ref r.Ready, ready);
                    Put(ref r.StateText, !priced ? "NO PRICE" : f.DonationForbidden ? "FORBIDDEN" : wait < 0 ? "CHECKING" : wait > 0 ? "READY IN " + Clock(wait) :
                        c.Cost <= funds ? "READY" : "SHORT " + N(Whole(c.Cost - funds, true)));
                }
                Put(ref v.RowCount, count);
                Put(ref v.Note, !known ? "JOIN A FACTION TO SPEND YOUR FUNDS" :
                    f.DonationForbidden ? "THIS MISSION FORBIDS BUYING UNITS" + StrWords.Dot + "FUNDS CAN STILL GO TO THE POOL" :
                    count == 0 ? "THIS MISSION LISTS NO UNIT GROUPS TO BUY" : "");
            }

            /// <summary>Whole seconds left, rounded up; -1 when the host has not answered.</summary>
            private static int SecUp(float v) => !float.IsFinite(v) || v < 0f ? -1 : (int)Math.Ceiling(Math.Min(v, 1e9f));

            private static int Whole(float v, bool up) => (int)Math.Max(-2e9, Math.Min(2e9, up ? Math.Ceiling(v) : Math.Floor(v)));

            private void Wave()
            {
                TheaterWaveView w = snap?.Wave;
                StrWaveView v = m.Wave;
                bool present = w != null;
                int perWave = present ? Math.Max(1, w.CreditsPerWave) : 0, credits = present ? Math.Max(0, w.Credits) : 0;
                int cost = waveCost, pool = present && w.Pool >= 0 ? w.Pool : -1;

                string state, detail;
                bool held = false;
                if (!present) state = "WAVE DESK UNAVAILABLE";
                else if (w.State == WaveState.Off) state = TheaterWords.Wave(w.Block);
                else if (credits < perWave) state = "EARNING " + credits + "/" + perWave;
                else if (w.Block == WaveBlock.None) state = "READY";
                else { held = true; state = "HELD" + StrWords.Dot + StrWords.WaveBlockCaption(w.Block); }
                if (present && credits > perWave && w.State != WaveState.Off) state += " +" + (credits - perWave);

                if (held && w.Block != WaveBlock.PoolShort) detail = TheaterWords.Wave(w.Block);
                else if (cost >= 0 && pool >= 0)
                    detail = waveAfford == StrTri.Yes ? "POOL " + N(pool) + " " + StrWords.Arrow + " " + N(pool - cost) : "POOL " + N(pool) + StrWords.Dot + "NEEDS " + N(cost);
                else detail = "POOL " + (pool >= 0 ? N(pool) : StrWords.Dash) + StrWords.Dot + "COST " + (cost >= 0 ? N(cost) : StrWords.Dash);

                bool depot = present && w.DepotKnown && float.IsFinite(w.DepotX) && float.IsFinite(w.DepotZ);
                Put(ref v.Present, present);
                Put(ref v.Credits, credits);
                Put(ref v.PerWave, perWave);
                Put(ref v.Cost, cost);
                Put(ref v.Pool, pool);
                Put(ref v.Affordable, waveAfford);
                Put(ref v.StateText, state);
                Put(ref v.GroupText, present ? w.Group ?? "" : "");
                Put(ref v.PoolText, present ? detail : "");
                Put(ref v.LastText, present && w.LastBoughtAgo >= 0f && !float.IsInfinity(w.LastBoughtAgo) ? "LAST WAVE " + OldText(w.LastBoughtAgo).Replace(" OLD", " AGO") : "");
                Put(ref v.Held, held);
                Put(ref v.DepotKnown, depot);
                Put(ref v.DepotX, depot ? w.DepotX : 0f);
                Put(ref v.DepotZ, depot ? w.DepotZ : 0f);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Math;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    // The deck, the pilot's commands, the operation's life and its end.
    internal sealed partial class LivingFrontService
    {
        private bool SteeringAvailable => GroundFrontService.Active?.SteeringAvailable == true;

        /// <summary>The ground side is shimmed until the ground slice: the reason is whatever the settings and an RTS commander say, else unknown. Shown only after two reviews agree.</summary>
        private GroundReason RawGroundReason()
        {
            GroundFrontService ground = GroundFrontService.Active;
            if (ground == null || settings == null) return GroundReason.Unknown;
            if (!settings.FrontlineTacticsEnabled.Value) return GroundReason.TacticsOff;
            if (ground.HasRtsCommander()) return GroundReason.RtsOwnsOrders;
            return ground.SteeringAvailable ? GroundReason.Ready : GroundReason.Unknown;
        }

        /// <summary>A flip of the reason (depot-fresh vehicles turning NO_VEHICLES into READY, say) must not flicker the cards: it shows after two reviews agree.</summary>
        private void Debounce(FactionWar war)
        {
            GroundReason raw = RawGroundReason();
            if (raw == war.GroundPending) war.GroundShown = raw;
            war.GroundPending = raw;
        }

        /// <summary>
        /// Keeps the live deck: survivors keep their ids, free slots refill from the ranking, the live operation's front and every vetoed front stay out.
        /// Arms the staff's pick deadline when the deck appears with nothing running. Nothing deals before the first-deal time.
        /// </summary>
        private void Maintain(FactionWar war, float now)
        {
            if (now < war.FirstDealAt) return;
            vetoWar = war; vetoNow = now;
            war.Vetoes.Prune(now);
            LivingWarRules.Choose(war.Reads, war.Posture, new WarCapability(SteeringAvailable), isVetoed, ranked, out war.Tally);
            // An own base under attack cannot wait for a slot: cards keep their places, so a full deck gives up its weakest card that is not a base defence.
            int urgent = LivingWarRules.UrgentDefence(ranked, war.Deck, war.Active?.Key);
            string dropped = urgent >= 0 && war.Deck.Count >= DeckRules.Slots ? DeckRules.DropWeakest(war.Deck) : null;
            offers.Clear();
            if (urgent >= 0 && war.Deck.Count < DeckRules.Slots) offers.Add(new DeckOffer(ranked[urgent].Front.Key, ranked[urgent].Kind, ranked[urgent].Score));
            for (int i = 0; i < ranked.Count; i++)
                if (i != urgent && ranked[i].Front.Key != dropped) offers.Add(new DeckOffer(ranked[i].Front.Key, ranked[i].Kind, ranked[i].Score));
            DeckRules.Maintain(war.Deck, offers, war.Active?.Key, ref war.NextId, ref war.Revision);
            if (war.Deck.Count > 0 && war.FirstDeckAt < 0f) war.FirstDeckAt = now;
            if (war.Active != null || war.Deck.Count == 0) war.DeadlineArmed = false;
            else if (!war.DeadlineArmed)
            {
                war.DeadlineArmed = true;
                war.OfferDeadline = now + (HasHuman(war.HQ) ? LivingWarRules.StaffPickHumanSeconds : LivingWarRules.StaffPickAiSeconds);
            }
        }

        /// <summary>The staff picks its recommended card once the deadline passes with nothing running. It never runs over an operation, so it cannot override a pilot.</summary>
        private void StaffPick(FactionWar war, float now)
        {
            if (war.Active != null || !war.DeadlineArmed || now < war.OfferDeadline || war.Deck.Count == 0) return;
            DeckCard card = war.Deck[Math.Max(0, DeckRules.Recommended(war.Deck))];
            PickCard(war, card.Id, card.Revision, default, true, true, now);
        }

        // ---- Commands ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// The one command entry, for the host's own pilot and for a client's intent alike. Guarded: a throw answers Invalid and propagates nothing, and a host
        /// whose staff is faulting or whose mission is over refuses with NotAllowed.
        /// </summary>
        internal IntentReply ApplyIntent(FactionHQ hq, byte kind, int id, int revision, byte posture, in IntentActor actor)
        {
            if (!Authoritative || MissionOver || hq?.faction == null || !wars.TryGetValue(hq.faction.factionName, out FactionWar war) ||
                war.HQ == null || !war.Faults.Healthy) return new IntentReply(TheaterCommandCode.NotAllowed);
            float now = Time.timeSinceLevelLoad;
            IntentActor who = actor;
            var reply = new IntentReply(TheaterCommandCode.Invalid);
            if (!Guard.Run(war.Faults, now, () => reply = Apply(war, kind, id, revision, posture, who, now), LogFault, "command"))
                reply = new IntentReply(TheaterCommandCode.Invalid);
            return reply;
        }

        private IntentReply Apply(FactionWar war, byte kind, int id, int revision, byte posture, in IntentActor actor, float now)
        {
            IntentReply reply;
            switch (kind)
            {
                case 0: reply = PickCard(war, id, revision, actor, false, false, now); break;
                case 1: reply = CallOff(war, id, revision, actor, now); break;
                case 2: reply = SetPosture(war, posture, now); break;
                default: return new IntentReply(TheaterCommandCode.Invalid);
            }
            if (reply.Code == TheaterCommandCode.Accepted) Publish(war, now);
            return reply;
        }

        /// <summary>
        /// A pick, or a redirect when an operation lives. The deck's card must be current: Collect first, so the origin is on the live contour, then the front
        /// must still be live, unvetoed and evaluate to the same kind. Other pilots are locked out for 120 s of a pilot's pick; the staff's pick locks nobody.
        /// </summary>
        private IntentReply PickCard(FactionWar war, int id, int revision, in IntentActor actor, bool byStaff, bool collected, float now)
        {
            DeckCard card = null;
            for (int i = 0; i < war.Deck.Count; i++)
                if (war.Deck[i].Id == id && war.Deck[i].Revision == revision) card = war.Deck[i];
            if (card == null) return new IntentReply(TheaterCommandCode.StaleChoice);
            Operation live = war.Active;
            if (live != null)
            {
                TheaterCommandCode? locked = PickLock.Check(live.PickedByStaff, now, live.LockUntil, live.PickedById, actor.Id, out float left);
                if (locked.HasValue) return new IntentReply(locked.Value, live.PickedBy, Mathf.Min(left, LivingWarRules.PickLockSeconds));
            }
            if (!collected) Collect(war, now);
            int at = -1;
            for (int i = 0; i < war.Reads.Count; i++) if (war.Reads[i].Key == card.Key) at = i;
            if (at < 0) return new IntentReply(TheaterCommandCode.StaleChoice);
            WarFrontRead read = war.Reads[at];
            vetoWar = war; vetoNow = now;
            float score = LivingWarRules.Evaluate(read, war.Posture, new WarCapability(SteeringAvailable), isVetoed(read), out TheaterOpKind kind, out _);
            if (!(score > 0f) || kind != card.Kind) return new IntentReply(TheaterCommandCode.StaleChoice);

            if (live != null)
            {
                if (byStaff && !live.PickedByStaff) war.StaffOverrides++;
                EndOperation(war, TheaterOutcome.Redirected, TheaterWhy.None, now, "");
                war.Redirects++;
            }
            float hold = 0f;
            bool hasHold = territory != null && territory.TryGetHoldStrength(war.HQ.GetInstanceID(), read.X, read.Z, out hold);
            TheaterCarrier carrier = CarrierOf(read);
            string placeId = null;
            for (int i = 0; i < war.Candidates.Count; i++) if (war.Candidates[i].Key == read.Key) placeId = war.Candidates[i].PlaceId;
            war.Active = new Operation
            {
                Id = card.Id, Revision = ++war.Revision, Key = read.Key, Name = read.Name, PlaceId = placeId, Kind = kind, Source = read.Source,
                Carrier = carrier, CarrierBased = carrier == TheaterCarrier.Ground, InitialGround = read.ReachFriendlyGround,
                X = read.X, Z = read.Z, OriginX = read.X, OriginZ = read.Z, Started = now, InitialHold = hold, HoldNow = hold, HasHold = hasHold,
                PhaseSince = now, PickedByStaff = byStaff, PickedById = actor.Id, PickedBy = actor.Callsign,
                LockUntil = byStaff ? 0f : now + LivingWarRules.PickLockSeconds,
            };
            war.DeadlineArmed = false;
            if (byStaff) { war.StaffPicks++; war.StaffPickedAt = now; } else war.PilotPicks++;
            priority?.SetDirectedFix(war.HQ, read.Key, read.Name, read.X, read.Z);
            DirectNaval(war);
            string line = (byStaff ? "STAFF SELECTED " : "SELECTED ") + TheaterWords.Kind(kind) + " " + read.Name;
            war.Log.Add(line);
            Plugin.Logger?.LogInfo("Living Front " + war.HQ.faction.factionName + " " + line + (byStaff ? "" : " (" + actor.Callsign + ")"));
            Maintain(war, now);
            return new IntentReply(TheaterCommandCode.Accepted, "", byStaff ? 0f : LivingWarRules.PickLockSeconds);
        }

        private IntentReply CallOff(FactionWar war, int id, int revision, in IntentActor actor, float now)
        {
            Operation op = war.Active;
            if (op == null || op.Id != id || op.Revision != revision) return new IntentReply(TheaterCommandCode.NoOperation);
            TheaterCommandCode? locked = PickLock.Check(op.PickedByStaff, now, op.LockUntil, op.PickedById, actor.Id, out float left);
            if (locked.HasValue) return new IntentReply(locked.Value, op.PickedBy, Mathf.Min(left, LivingWarRules.PickLockSeconds));
            EndOperation(war, TheaterOutcome.CalledOff, TheaterWhy.None, now, actor.Callsign);
            // Calling off is also the route to a new choice when the field changes: the called-off front is vetoed, the rest of the deck stays.
            Collect(war, now);
            Maintain(war, now);
            return new IntentReply(TheaterCommandCode.Accepted);
        }

        private IntentReply SetPosture(FactionWar war, byte posture, float now)
        {
            if (posture > (byte)TheaterWarPosture.Bold) return new IntentReply(TheaterCommandCode.Invalid);
            war.Posture = (TheaterWarPosture)posture;
            war.Log.Add("POSTURE " + war.Posture.ToString().ToUpperInvariant());
            Collect(war, now);
            Maintain(war, now);
            return new IntentReply(TheaterCommandCode.Accepted);
        }

        /// <summary>Who the card's operation would be carried by: the ground when a carrier is in reach and steering is on, else other forces if any are near.</summary>
        private TheaterCarrier CarrierOf(in WarFrontRead read) =>
            read.Carriers > 0 && SteeringAvailable ? TheaterCarrier.Ground : read.Friendly > 0 ? TheaterCarrier.AirNaval : TheaterCarrier.None;

        internal static IntentActor ActorOf(Player player)
        {
            if (player == null) return default;
            string name = "";
            try { name = PlaceNames.Clip(PlaceNames.StripTags(player.GetDisplayName(PlayerNameContext.ChatOrLeaderboard)), Networking.LivingFrontWire.MaximumCallsign); }
            catch (Exception) { /* a player torn down mid-frame has no name; the lock compares ids, not names */ }
            return new IntentActor(PlayerIdentity.Of(player), name);
        }

        // ---- The operation's life -----------------------------------------------------------------------------------------

        /// <summary>Follows the front, derives the phase and progress, reads the timers and asks the pure rules whether the operation is over.</summary>
        private void AdvanceOperation(FactionWar war, float now)
        {
            Operation op = war.Active;
            if (op == null) return;
            ReadHold(war);
            WarFrontRead read = default;
            bool found = false;
            for (int i = 0; i < war.Reads.Count; i++)
                if (war.Reads[i].Key == op.Key) { read = war.Reads[i]; found = true; break; }
            bool live = found && read.Live;
            if (live && Scalar.Distance2DSquared(op.X, op.Z, read.X, read.Z) >= LivingWarRules.FollowStep * LivingWarRules.FollowStep)
            {
                op.X = read.X; op.Z = read.Z;
                priority?.SetDirectedFix(war.HQ, op.Key, op.Name, op.X, op.Z);
            }

            bool objectiveSecured = false, objectiveClosed = false;
            if (op.Source == FrontSource.Objective)
            {
                Objective objective = ObjectiveBook.Find(op.Key);
                objectiveSecured = objective != null && objective.Status == ObjectiveStatus.Complete;
                objectiveClosed = objective == null || activeObjectives != null && !activeObjectives.Contains(objective);
            }
            int placeAt = op.Source == FrontSource.Base ? IndexOfPlace(war, op.PlaceId) : -1;
            bool lostDefense = op.Kind == TheaterOpKind.Defend &&
                (op.Source == FrontSource.Base ? placeAt < 0 || !war.Places[placeAt].Owned : op.Source == FrontSource.Sector && live && !read.Held);

            float gain = op.Gain.Update(live && OutcomeRules.GainHolds(op.HasHold, op.HoldNow, op.InitialHold), now);
            float loss = op.Loss.Update(live && OutcomeRules.LossHolds(op.HasHold, op.HoldNow, op.InitialHold), now);
            float clear = op.Clear.Update(live && read.Held && read.HostileNear == 0, now);
            float notLive = op.NotLive.Update(!live, now);

            // The ground report arrives with the ground slice: until then there is no phase to derive and no NoForces to judge.
            TheaterPhase phase = OpPhaseRules.Derive(op.Kind, new PhaseInput { HaveReport = false, NearestToFixMeters = -1f }, out TheaterPhaseNote note);
            if (phase != op.Phase || note != op.PhaseNote)
            {
                op.Phase = phase; op.PhaseNote = note; op.PhaseSince = now;
                war.Log.Add(op.Name + " · " + TheaterWords.Phase(phase, note));
            }
            op.Progress = OpPhaseRules.Progress(op.Kind, op.Source, op.HasHold, op.HoldNow, op.InitialHold, clear, live && read.Observed);

            OpEnd end = OutcomeRules.Evaluate(new OutcomeInput
            {
                Kind = op.Kind, Source = op.Source, Found = found, Live = live, Steering = false, HaveReport = false, HasHold = op.HasHold,
                CarrierBased = op.CarrierBased, Held = read.Held, Observed = read.Observed,
                ObjectiveSecured = objectiveSecured, ObjectiveClosed = objectiveClosed, LostDefense = lostDefense,
                Elapsed = now - op.Started, HoldAtOrigin = op.HoldNow, InitialHold = op.InitialHold,
                GainSeconds = gain, LossSeconds = loss, ClearSeconds = clear, NoForcesSeconds = 0f, NotLiveSeconds = notLive,
                NearFriendly = read.NearFriendly, HostileNear = read.HostileNear, ReachFriendlyGround = read.ReachFriendlyGround,
                ReachFriendly = read.Friendly, InitialGround = op.InitialGround, Groups = 0,
            });
            if (end.Outcome != TheaterOutcome.None) EndOperation(war, end.Outcome, end.Why, now, "");
        }

        /// <summary>
        /// Ends the live operation once: pays a SECURED one to a faction with a human (funds and morale, never score; a key pays at most once per 300 s),
        /// records the result, says it on the staff log and the air, vetoes the front and clears the directive. Called off and redirected operations pay and cost nothing.
        /// </summary>
        private void EndOperation(FactionWar war, TheaterOutcome outcome, TheaterWhy why, float now, string by)
        {
            Operation op = war.Active;
            if (op == null) return;
            Reward reward = OutcomeRewards.For(op.Kind, outcome, HasHuman(war.HQ));
            if (reward.Funds > 0 && war.PaidUntil.TryGetValue(op.Key, out float until) && now < until) reward = default;
            if (reward.Funds > 0 || reward.Morale > 0)
            {
                war.HQ.AddBonusFunds(reward.Funds);
                war.RewardPaidTotal += reward.Funds;
                if (war.PaidUntil.Count >= MaximumRemembered) PruneExpired(war.PaidUntil, now);
                war.PaidUntil[op.Key] = now + LivingWarRules.RewardCooldownSeconds;
                try { MoraleAwarded?.Invoke(war.HQ.GetInstanceID(), reward.Morale); }
                catch (Exception error) { Plugin.Logger?.LogWarning("[TheaterOps] morale subscriber failed: " + error.Message); }
            }
            war.LastResult = new TheaterResultView
            {
                OperationId = op.Id, Kind = op.Kind, Name = op.Name, Outcome = outcome, Why = why,
                Funds = Mathf.Clamp(reward.Funds, 0, 100000), Morale = Mathf.Clamp(reward.Morale, -10, 10),
            };
            war.LastResultAt = now;
            war.ResultFunds += war.LastResult.Funds;
            war.Log.Add(TheaterWords.Result(op.Kind, op.Name, outcome, why, reward.Funds, by));
            Plugin.Logger?.LogInfo("Living Front " + war.HQ.faction.factionName + " " + op.Name + " " + TheaterWords.Outcome(outcome) +
                (why == TheaterWhy.None ? "" : " (" + TheaterWords.Why(why) + ")"));
            if (outcome == TheaterOutcome.Secured || outcome == TheaterOutcome.Repulsed || outcome == TheaterOutcome.Stalled) Announce(war.HQ, TheaterWords.Announce(op.Kind, op.Name, outcome, reward.Funds));

            float vetoFor = outcome == TheaterOutcome.CalledOff ? LivingWarRules.CallOffVetoSeconds
                : outcome == TheaterOutcome.Redirected ? 0f : LivingWarRules.EndVetoSeconds;
            if (vetoFor > 0f)
            {
                war.Vetoes.Add(op.Key, op.X, op.Z, now + vetoFor);
                war.LastVetoUntil = Mathf.Max(war.LastVetoUntil, now + vetoFor);
            }
            ClearOperationDirective(war);
            naval?.ClearObjective(war.HQ, op.Key);
            war.Active = null;
            war.GroupAssignments.Clear();
            war.DeadlineArmed = false;
            war.Revision++;
        }

        private static void Announce(FactionHQ hq, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { NetworkSceneSingleton<MessageManager>.i?.RpcHQMessage(hq, text); }
            catch (Exception error) { Plugin.Logger?.LogWarning("[TheaterOps] operation message not delivered: " + error.Message); }
        }

        private static void PruneExpired(Dictionary<string, float> until, float now)
        {
            var spent = new List<string>(until.Count);
            foreach (KeyValuePair<string, float> pair in until) if (now >= pair.Value) spent.Add(pair.Key);
            for (int i = 0; i < spent.Count; i++) until.Remove(spent[i]);
        }
    }
}

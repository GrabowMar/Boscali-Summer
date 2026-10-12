using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.TheaterOps.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
#if !PUBLIC_RELEASE
    /// <summary>
    /// Host-truth readback and levers for the unattended STR engine proof (<c>tests/ingame/str-2-war-*.json</c>, <c>str-3-reduced-*.json</c>): per faction what the staff
    /// decided and when, and a few game-side cross-checks a scenario can compare with a constant. <c>Command</c>, <c>Track</c> and <c>Freeze</c> change state, so every verb
    /// refuses unless a nomodkit scenario is running. Dev tooling, compiled out of public releases. Results are nested dictionaries without dots in key names; an unknown
    /// number is -1, never NaN.
    /// </summary>
    public static class WarProof
    {
        // The contact Track keeps planted: vanilla rewrites the position of a unit it can see, so the plant is renewed right before every contact scan.
        private static FactionHQ plantHq;
        private static Unit plantUnit;
        private static float plantX, plantZ, plantUntil;

        private static bool InSimRun => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("WarProof", "Read", "requires a nomodkit scenario run");
            LivingFrontService living = LivingFrontService.Active;
            if (living == null) return Failure("WarProof", "Read", "the Living Front service is not running");
            float now = Time.timeSinceLevelLoad;
            var war = new Dictionary<string, object>();
            string local = GameAccess.TryGetLocalFaction(out FactionHQ mine) && mine.faction != null ? mine.faction.factionName : null;
            foreach (KeyValuePair<string, LivingFrontService.FactionWar> pair in living.Wars)
                if (pair.Value.HQ != null) war[Key(pair.Key)] = FactionRead(living, pair.Value, now, pair.Key == local);
            return new Dictionary<string, object> { { "ok", true }, { "t", now }, { "war", war } };
        }

        private static Dictionary<string, object> FactionRead(LivingFrontService living, LivingFrontService.FactionWar w, float now, bool isLocal)
        {
            TheaterWarSnapshot snapshot = w.Published;
            LivingFrontService.Operation op = w.Active;
            int named = 0, idLike = 0;
            foreach (TheaterFrontView front in w.Fronts)
            {
                if (IdLike(front.Name)) idLike++; else named++;
            }
            int noCarrier = 0, defend = 0, vetoedInDeck = 0;
            var ids = new List<int>();
            foreach (DeckCard card in w.Deck)
            {
                ids.Add(card.Id);
                if (card.Kind == TheaterOpKind.Defend) defend++;
                WarFrontRead read = default;
                bool found = false;
                for (int i = 0; i < w.Reads.Count && !found; i++) if (w.Reads[i].Key == card.Key) { read = w.Reads[i]; found = true; }
                if (found && card.Kind != TheaterOpKind.Defend && read.Carriers <= 0) noCarrier++;
                if (found && w.Vetoes.IsVetoed(card.Key, read.X, read.Z, now, LivingWarRules.FollowRadius)) vetoedInDeck++;
            }
            TheaterResultView last = w.LastResult;
            bool ended = last != null && (last.Outcome == TheaterOutcome.Secured || last.Outcome == TheaterOutcome.Repulsed || last.Outcome == TheaterOutcome.Stalled);
            // Secured pays the table or nothing (inside the cooldown); every other outcome pays nothing.
            int mismatch = last == null ? 0 : last.Outcome == TheaterOutcome.Secured
                ? (last.Funds == 0 || last.Funds == OutcomeRewards.For(last.Kind, TheaterOutcome.Secured, true).Funds ? 0 : 1) : last.Funds != 0 ? 1 : 0;
            TheaterForcesView forces = snapshot?.Operation?.Forces;
            int stageDelta = forces == null || forces.Groups == 255 || forces.Rally == 255 || forces.Line == 255 || forces.Assault == 255 || forces.Contact == 255 || forces.Withdraw == 255
                ? -1 : Math.Abs(forces.Rally + forces.Line + forces.Assault + forces.Contact + forces.Withdraw - forces.Groups);
            int focusMatches = -1;
            if (op != null)
            {
                focusMatches = living.TryGetFocus(w.HQ.GetInstanceID(), out OperationFocus focus) && focus.OperationId == op.Id &&
                    focus.AnchorKey == op.Key && focus.Role == LivingWarRules.RoleOf(op.Kind) ? 1 : 0;
            }
            return new Dictionary<string, object>
            {
                { "healthy", Flag(w.Faults.Healthy) }, { "faults", w.Faults.Faults },
                { "reason", snapshot != null ? snapshot.Reason.ToString() : "" }, { "emptyReason", snapshot != null ? snapshot.Reason.ToString() : "" },
                { "groundReason", snapshot != null ? snapshot.GroundReason.ToString() : "" },
                { "deckCount", w.Deck.Count }, { "deckIds", string.Join(",", ids) },
                {
                    // Why the last ranking rejected what it did, and what it had to rank: the first thing to read when a card is missing.
                    "tally", new Dictionary<string, object>
                    {
                        { "candidates", w.Tally.Candidates }, { "badFix", w.Tally.BadFix }, { "gone", w.Tally.Gone }, { "noPresence", w.Tally.NoPresence },
                        { "noSteering", w.Tally.NoSteering }, { "noCarrier", w.Tally.NoCarrier }, { "heldQuiet", w.Tally.HeldQuiet },
                        { "postureRatio", w.Tally.PostureRatio }, { "vetoed", w.Tally.Vetoed }, { "baseCandidates", BaseCandidates(w) },
                        { "places", w.Places.Count }, { "ownedPlaces", OwnedPlaces(w) }, { "hostileFixes", w.HostileFixes }, { "postsHit", w.PostsHit },
                    }
                },
                { "frontsCount", w.Fronts.Count }, { "frontsNamed", named }, { "frontsIdLike", idLike },
                { "firstDeckAt", w.FirstDeckAt }, { "staffPickedAt", w.StaffPickedAt },
                { "staffPicks", w.StaffPicks }, { "pilotPicks", w.PilotPicks }, { "staffOverrides", w.StaffOverrides }, { "redirects", w.Redirects },
                { "vetoes", w.Vetoes.Count(now) }, { "vetoedKeyInDeck", vetoedInDeck },
                { "assaultCardsWithoutCarriers", noCarrier }, { "defendCards", defend },
                { "opLive", Flag(op != null) }, { "opKey", op != null ? op.Key : "" },
                { "opKind", op != null ? op.Kind.ToString() : "" }, { "opSource", op != null ? op.Source.ToString() : "" },
                { "opPhase", op != null ? op.Phase.ToString() : "" }, { "opPhaseNote", op != null ? op.PhaseNote.ToString() : "" },
                { "opElapsed", op != null ? now - op.Started : -1f },
                { "awaitingSeconds", op != null && op.Phase == TheaterPhase.Awaiting ? now - op.PhaseSince : op != null ? 0f : -1f },
                { "forcesGroups", forces != null && forces.Groups != 255 ? forces.Groups : -1 }, { "forcesKnown", Flag(forces != null && forces.Groups != 255) },
                { "lockSeconds", op != null && !op.PickedByStaff ? Mathf.Max(0f, op.LockUntil - now) : 0f },
                { "pickedByStaff", Flag(op != null && op.PickedByStaff) },
                { "age", SnapshotAge.Host(now, w.SensedAt) }, { "ageAtRefreshMax", w.AgeAtRefreshMax },
                { "canCommand", Flag(isLocal ? living.CanCommand : snapshot != null && w.Faults.Healthy) },
                { "lastCommandCode", isLocal ? (living.LastCommand?.Code ?? TheaterCommandCode.None).ToString() : TheaterCommandCode.None.ToString() },
                { "resultOutcomeSet", Flag(ended) }, { "resultOutcome", last != null ? last.Outcome.ToString() : "" },
                { "resultWhy", last != null ? last.Why.ToString() : "" }, { "resultWhyIsTimeLimit", Flag(last != null && last.Why == TheaterWhy.TimeLimit) },
                { "resultFunds", last != null ? last.Funds : 0 }, { "resultMorale", last != null ? last.Morale : 0 },
                { "rewardPaidTotal", w.RewardPaidTotal },
                { "staffPickSeconds", snapshot != null ? snapshot.StaffPickSeconds : -1f },
                {
                    "check", new Dictionary<string, object>
                    {
                        // -1 = not measurable (no ground report yet), so a check pairs it with result_above min 0.
                        { "stageTallyDelta", stageDelta }, { "rewardMismatch", mismatch },
                        { "rewardLedgerDelta", Math.Abs(w.RewardPaidTotal - w.ResultFunds) },
                        { "focusKeyMatchesOp", focusMatches }, { "trendWrites", w.TrendWrites },
                    }
                },
            };
        }

        /// <summary>Called by the contact scan just before it reads the tracking database (see Track).</summary>
        internal static void BeforeScan(FactionHQ hq, float now)
        {
            if (plantUnit == null || !ReferenceEquals(hq, plantHq) || !(now < plantUntil) || plantUnit.disabled) return;
            if (!hq.trackingDatabase.TryGetValue(plantUnit.persistentID, out TrackingInfo info) || info == null) info = new TrackingInfo(plantUnit);
            info.lastKnownPosition = new GlobalPosition(plantX, 0f, plantZ);
            info.lastSpottedTime = now;
            hq.trackingDatabase[plantUnit.persistentID] = info;
        }

        private static int OwnedPlaces(LivingFrontService.FactionWar w)
        {
            int count = 0;
            foreach (Place p in w.Places) if (p.Owned) count++;
            return count;
        }

        private static int BaseCandidates(LivingFrontService.FactionWar w)
        {
            int count = 0;
            foreach (LivingFrontService.FrontCandidate c in w.Candidates) if (c.Source == FrontSource.Base) count++;
            return count;
        }

        /// <summary>A front name that is a key or a grid label rather than a place: an underscore, the old FRONT gx/gz label or an S: key.</summary>
        private static bool IdLike(string name) =>
            string.IsNullOrEmpty(name) || name.IndexOf('_') >= 0 || name.StartsWith("FRONT ", StringComparison.Ordinal) || name.StartsWith("S:", StringComparison.Ordinal);

        /// <summary>
        /// {faction (default: the seat's), verb: pick|calloff|posture, card: 0-based index into the published deck, posture: 0-2, actor: a player id}. Without an actor the seat
        /// acts through the same path its panel uses (so the typed reply, its expiry and the lock all show); with an actor the host runs the command as that other player,
        /// which is how a single-seat sim exercises the lock.
        /// </summary>
        public static Dictionary<string, object> Command(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("WarProof", "Command", "requires a nomodkit scenario run");
            LivingFrontService living = LivingFrontService.Active;
            if (living == null) return Failure("WarProof", "Command", "the Living Front service is not running");
            if (!TryFaction(Text(args, "faction"), out FactionHQ hq, out string faction)) return Failure("WarProof", "Command", "unknown faction");
            if (!living.Wars.TryGetValue(faction, out LivingFrontService.FactionWar war) || war.Published == null)
                return Failure("WarProof", "Command", "no published picture for " + faction);
            string verb = (Text(args, "verb") ?? "").ToLowerInvariant();
            int card = (int)Number(args, "card", 0f);
            TheaterWarSnapshot snapshot = war.Published;
            byte kind = verb == "pick" ? (byte)0 : verb == "calloff" ? (byte)1 : (byte)2;
            int id = 0, revision = 0;
            if (kind == 0)
            {
                if (card < 0 || card >= snapshot.Proposals.Length) return Failure("WarProof", "Command", "no card " + card);
                id = snapshot.Proposals[card].Id; revision = snapshot.Proposals[card].Revision;
            }
            else if (kind == 1)
            {
                if (snapshot.Operation == null) return Failure("WarProof", "Command", "no live operation to call off");
                id = snapshot.Operation.Id; revision = snapshot.Operation.Revision;
            }
            else if (verb != "posture") return Failure("WarProof", "Command", "unknown verb " + verb);
            byte posture = (byte)Mathf.Clamp(Number(args, "posture", 1f), 0f, 2f);

            string before = war.Active != null ? war.Active.Key : "";
            TheaterCommandCode code;
            string detail = "";
            float lockSeconds = 0f;
            if (Has(args, "actor"))
            {
                IntentReply reply = living.ApplyIntent(hq, kind, id, revision, posture, new IntentActor((ulong)Number(args, "actor", 0f), "SIM"));
                code = reply.Code; detail = reply.Detail; lockSeconds = reply.LockSeconds;
            }
            else
            {
                bool sent = kind == 0 ? living.RequestPick(id, revision) : kind == 1 ? living.RequestCancel(id, revision) : living.RequestPosture((TheaterWarPosture)posture);
                TheaterCommandResult result = living.LastCommand;
                code = result != null ? result.Code : sent ? TheaterCommandCode.Accepted : TheaterCommandCode.None;
                if (result != null) { detail = result.Detail; lockSeconds = result.LockSeconds; }
            }
            string after = war.Active != null ? war.Active.Key : "";
            return new Dictionary<string, object>
            {
                { "ok", true }, { "code", code.ToString() }, { "detail", detail }, { "lockSeconds", lockSeconds },
                { "opKeyBefore", before }, { "opKeyAfter", after }, { "keyChanged", Flag(before != after) },
            };
        }

        /// <summary>
        /// {faction, seconds (default 30)}: for that long, before every contact scan of the faction, plants a fresh track at the centre of an owned airbase on an existing hostile
        /// ground vehicle (the best-hidden one), so the review counts a hostile at an owned base without a spawn. The tracked fix is what the engine reads; the unit stays where it is.
        /// </summary>
        public static Dictionary<string, object> Track(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("WarProof", "Track", "requires a nomodkit scenario run");
            LivingFrontService living = LivingFrontService.Active;
            if (living == null) return Failure("WarProof", "Track", "the Living Front service is not running");
            if (!TryFaction(Text(args, "faction"), out FactionHQ hq, out string faction) || !living.Wars.TryGetValue(faction, out LivingFrontService.FactionWar war))
                return Failure("WarProof", "Track", "unknown faction");
            Place post = default;
            bool havePost = false;
            foreach (Place place in war.Places)
                if (place.Owned && (!havePost || string.CompareOrdinal(place.Id, post.Id) < 0)) { post = place; havePost = true; }
            if (!havePost) return Failure("WarProof", "Track", faction + " holds no airbase");
            // Any hostile ground vehicle will do; one nobody of ours tracks is best, since vanilla rewrites the position of a unit it sees.
            Unit pick = null;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.disabled || unit.NetworkHQ == null || ReferenceEquals(unit.NetworkHQ, hq) || !(unit is GroundVehicle)) continue;
                bool tracked = hq.trackingDatabase.ContainsKey(unit.persistentID);
                if (pick == null || !tracked && hq.trackingDatabase.ContainsKey(pick.persistentID)) pick = unit;
                if (!tracked) break;
            }
            if (pick == null) return Failure("WarProof", "Track", "no hostile unit to track");
            plantHq = hq; plantUnit = pick; plantX = post.X; plantZ = post.Z; plantUntil = Time.timeSinceLevelLoad + Mathf.Max(1f, Number(args, "seconds", 30f));
            BeforeScan(hq, Time.timeSinceLevelLoad);
            bool readsBack = HostileContact.TryRead(hq, pick, Time.timeSinceLevelLoad, out GlobalPosition seen);
            return new Dictionary<string, object>
            {
                { "ok", true }, { "planted", 1 }, { "readsBack", Flag(readsBack) }, { "seenX", seen.x }, { "seenZ", seen.z },
                { "unit", pick.name }, { "baseId", post.Id }, { "baseName", post.Name }, { "baseX", post.X }, { "baseZ", post.Z },
            };
        }

        /// <summary>{seconds, faction (default: all)}: stalls the review, as a faulting staff would. The published picture stays and its age keeps growing; commands still work.</summary>
        public static Dictionary<string, object> Freeze(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("WarProof", "Freeze", "requires a nomodkit scenario run");
            LivingFrontService living = LivingFrontService.Active;
            if (living == null) return Failure("WarProof", "Freeze", "the Living Front service is not running");
            float until = Time.timeSinceLevelLoad + Mathf.Max(0f, Number(args, "seconds", 20f));
            string only = Text(args, "faction");
            int frozen = 0;
            foreach (KeyValuePair<string, LivingFrontService.FactionWar> pair in living.Wars)
            {
                if (only != null && Key(pair.Key) != Key(only)) continue;
                pair.Value.FrozenUntil = until;
                frozen++;
            }
            return new Dictionary<string, object> { { "ok", true }, { "frozen", frozen }, { "until", until } };
        }

        private static bool TryFaction(string name, out FactionHQ hq, out string faction)
        {
            hq = null; faction = null;
            if (string.IsNullOrEmpty(name))
            {
                if (!GameAccess.TryGetLocalFaction(out hq) || hq?.faction == null) return false;
                faction = hq.faction.factionName;
                return true;
            }
            foreach (FactionHQ candidate in FactionRegistry.GetAllHQs())
                if (candidate?.faction != null && Key(candidate.faction.factionName) == Key(name)) { hq = candidate; faction = candidate.faction.factionName; return true; }
            return false;
        }

        // 1 or 0: a scenario check compares numbers, and a bool is not one.
        private static int Flag(bool value) => value ? 1 : 0;

        private static string Key(string faction) =>
            (faction ?? "none").ToLowerInvariant().Replace('.', '_').Replace(' ', '_');
    }
#endif
}

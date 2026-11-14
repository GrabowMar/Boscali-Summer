using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Nation.Domain;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Nation.Runtime
{
    // POLITICS v2 on the host (war support from captures and kills, the cabinet, decisions, the staff, the hold on TheaterOps) and, on every
    // peer, the cabinet card handed to the Events dispatch and the political headlines for the log.
    internal sealed partial class NationService
    {
        private const int FortifyShells = 4;
        private readonly Dictionary<Airbase, FactionHQ> owners = new Dictionary<Airbase, FactionHQ>(64);
        private readonly Dictionary<FactionHQ, int> policyKeys = new Dictionary<FactionHQ, int>(NationRules.MaxFactions);
        private readonly CabinetCard card = new CabinetCard();
        private Action<int> pick;
        private int presented, postedOurs, postedTheirs, postedAnswer;
        private Doctrine postedDoctrine, postedEnemyDoctrine;
        private bool newsReady;
        private int newsPosted;
        private float nextPresent;

        // ---- War support from the war ------------------------------------------------------------

        /// <summary>Airbases changing hands: the loser -10 and FALL OF, the winner +10 and VICTORY AT (and GENERAL OFFENSIVE's +15 on its objective).</summary>
        private void PollAirbases(float now)
        {
            Dictionary<string, Airbase> lookup = FactionRegistry.airbaseLookup;
            if (lookup == null) return;
            foreach (Airbase airbase in lookup.Values)
            {
                if (airbase == null || airbase.AttachedAirbase) continue;
                FactionHQ hq = airbase.CurrentHQ;
                if (!owners.TryGetValue(airbase, out FactionHQ was)) { owners[airbase] = hq; continue; }
                if (was == hq) continue;
                owners[airbase] = hq;
                string name = MfdAirbaseFormatter.Format(airbase).ToUpperInvariant();
                if (was != null && sides.TryGetValue(was, out Side loser))
                {
                    loser.Politics.Earn(-PoliticsRules.BaseTaken, name + " LOST", now);
                    loser.LastLost = airbase;
                    loser.Politics.Open((int)CardId.FallOf, name, CabinetCards.StaffChoice((int)CardId.FallOf), now);
                }
                if (hq != null && sides.TryGetValue(hq, out Side winner))
                {
                    winner.Politics.Earn(PoliticsRules.BaseTaken, name + " TAKEN", now);
                    if (winner.OffensiveAt == airbase && winner.Politics.InForce(DecisionId.GeneralOffensive, now))
                    {
                        winner.Politics.Earn(PoliticsRules.OffensiveTaken, "GENERAL OFFENSIVE TAKES " + name, now);
                        winner.OffensiveAt = null;
                    }
                    winner.Politics.Open((int)CardId.VictoryAt, name, CabinetCards.StaffChoice((int)CardId.VictoryAt), now);
                }
            }
        }

        /// <summary>Host: one kill message (NationKillPatch). Aircraft: +1 to the side that shot it, -2 to a side whose human pilot fell, and a loss
        /// for HEAVY LOSSES. A plant, depot or air-defence site: +3 to the side that destroyed it, -3 to a side that lost a plant.</summary>
        internal static void NoteKill(PersistentID killerId, PersistentID killedId)
        {
            if (live == null || !live.layerOn || !GameAccess.IsServer()) return;
            try { live.Kill(killerId, killedId); }
            catch (Exception e) { Plugin.Logger?.LogWarning("[Nation] kill not counted: " + e.Message); }
        }

        private void Kill(PersistentID killerId, PersistentID killedId)
        {
            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            if (mission == null || !UnitRegistry.TryGetPersistentUnit(killedId, out PersistentUnit victim) || victim.unit is Missile) return;
            float now = mission.MissionTime;
            FactionHQ lost = victim.GetHQ();
            FactionHQ by = UnitRegistry.TryGetPersistentUnit(killerId, out PersistentUnit killer) ? killer.GetHQ() : null;
            Side ours = lost != null && sides.TryGetValue(lost, out Side owner) ? owner : null;
            Side theirs = by != null && by != lost && sides.TryGetValue(by, out Side hitter) ? hitter : null;
            UnitDefinition definition = victim.definition;
            if (definition is AircraftDefinition)
            {
                if (ours != null)
                {
                    ours.Politics.Lost(now);
                    if (victim.player != null) ours.Politics.Earn(PoliticsRules.PilotDown, "OUR PILOT SHOT DOWN", now);
                }
                theirs?.Politics.Earn(PoliticsRules.AircraftDown, "ENEMY AIRCRAFT DOWN", now);
                return;
            }
            if (definition == null) return;
            bool plant = definition.unitPrefab != null && definition.unitPrefab.TryGetComponent(out Factory _);
            bool defence = !plant && definition.roleIdentity.antiAir >= 0.5f;
            bool depot = !plant && !defence && (definition.unitName ?? "").IndexOf("DEPOT", StringComparison.OrdinalIgnoreCase) >= 0;
            if (plant) ours?.Politics.Earn(PoliticsRules.FactoryLost, "PLANT DESTROYED", now);
            if (theirs == null || !(plant || defence || depot)) return;
            theirs.Politics.Earn(PoliticsRules.SiteDestroyed, plant ? "ENEMY PLANT DESTROYED" : defence ? "ENEMY AIR DEFENCE DESTROYED" : "ENEMY DEPOT DESTROYED", now);
            if (plant && victim.unit != null && theirs.SabotagedPlant == victim.unit.GetInstanceID() && now < theirs.SabotagedUntil)
            {
                theirs.Politics.Earn(PoliticsRules.SabotageDestroyed, "SABOTAGED PLANT FINISHED", now);
                theirs.SabotagedPlant = 0;
            }
        }

        // ---- The cabinet, the decisions and the staff --------------------------------------------

        private void Politics(Side side, in NationWorld w, float now, bool staff)
        {
            PoliticsBook p = side.Politics;
            Side enemy = EnemyOf(side);
            p.Tick(now);
            if (p.StaffDue(now)) AnswerCard(side, p.Serial, p.Staff, PoliticsRules.Staff, now);
            int due = p.DueCard(now, w.Funds, enemy != null ? enemy.Politics.WarSupport : PoliticsRules.Max);
            if (due >= 0)
                p.Open(due, due == (int)CardId.PeaceFeeler ? NameOf(enemy) : "",
                    due == (int)CardId.WarCabinet ? PoliticsRules.WarCabinetPick(w.HeldAirbases, enemy != null ? Held(enemy.Owner) : 0) : CabinetCards.StaffChoice(due), now);
            if (staff)
            {
                int id = p.StaffPick(now, stage);
                if (id >= 0) Decide(side, id, PoliticsRules.Staff, now);
            }
            if (side.SabotagedPlant != 0 && now >= side.SabotagedUntil) side.SabotagedPlant = 0;
            if (!ModuleServices.TryGet(out ITheaterWarPolicy policy)) return;
            p.Band(now, out byte floor, out byte ceiling);
            float skill = 1f;
            foreach (Side other in sides.Values) if (other != side) skill *= other.Politics.EnemySkill(now);
            policy.Apply(side.Owner, new WarPolicy
            {
                Floor = (TheaterWarPosture)floor, Ceiling = (TheaterWarPosture)ceiling, Defence = p.Defence(now), Jets = p.Jets(now), Skill = skill,
            });
        }

        private NationOutcome Decide(Side side, int id, string by, float now)
        {
            NationOutcome outcome = side.Politics.Decide(id, by, now, stage, out PoliticsEffect effect);
            if (outcome != NationOutcome.Ok) return outcome;
            Plugin.Logger?.LogInfo("[Nation] " + side.Owner.name + " decides " + Decisions.Name(id) + " (" + by + "), war support " + Mathf.RoundToInt(side.Politics.WarSupport) + ".");
            Act(side, effect, now);
            return outcome;
        }

        private NationOutcome AnswerCard(Side side, int serial, int option, string by, float now)
        {
            PoliticsBook p = side.Politics;
            NationOutcome outcome = p.Answer(serial, option, by, now, out PoliticsEffect effect);
            if (outcome != NationOutcome.Ok) return outcome;
            Plugin.Logger?.LogInfo("[Nation] " + side.Owner.name + " cabinet: " + CabinetCards.Title(p.Card, p.Place) + " -> " + CabinetCards.OptionLabel(p.Card, option) + " (" + by + ").");
            Act(side, effect, now);
            return outcome;
        }

        /// <summary>What the books cannot do themselves: money, warheads, the other side's war support, offensives, waves, strongpoints, a raid.</summary>
        private void Act(Side side, in PoliticsEffect e, float now)
        {
            FactionHQ hq = side.Owner;
            Side enemy = EnemyOf(side);
            if (e.Funds != 0f) hq.AddFunds(e.Funds);
            if (e.Warheads > 0) hq.AddWarheadStockpile(e.Warheads);
            if (e.EnemySupport != 0f && enemy != null) enemy.Politics.Earn(e.EnemySupport, e.EnemySupport <= -20f ? NameOf(side) + " PROPAGANDA" : "PEACE TALKS", now);
            ModuleServices.TryGet(out ITheaterWarPolicy policy);
            if (e.Offensive && Closest(side, out _, out Airbase target))
            {
                side.OffensiveAt = target;
                Launch(policy, hq, target, "GENERAL OFFENSIVE");
            }
            if (e.Counterattack && side.LastLost != null && side.LastLost.CurrentHQ != hq) Launch(policy, hq, side.LastLost, "COUNTERATTACK");
            if (e.Wave) FreeWave(hq);
            if (e.Fortify && Closest(side, out Airbase threatened, out _) && ModuleServices.TryGet(out IZoneFortificationService fortify) &&
                !fortify.TryFortifyFor(threatened, hq, FortifyShells))
                Plugin.Logger?.LogInfo("[Nation] " + hq.name + " HOLD THE LINE: no shell to fortify at " + MfdAirbaseFormatter.Format(threatened) + ".");
            if (e.Sabotage && enemy != null) Sabotage(side, enemy, now);
        }

        private static void Launch(ITheaterWarPolicy policy, FactionHQ hq, Airbase at, string by)
        {
            if (policy == null || at == null || at.center == null) return;
            GlobalPosition g = at.center.position.ToGlobalPosition();
            if (!policy.LaunchOffensive(hq, (float)g.x, (float)g.z, by))
                Plugin.Logger?.LogInfo("[Nation] " + hq.name + " " + by + ": no front to launch from toward " + MfdAirbaseFormatter.Format(at) + ".");
        }

        /// <summary>A free reinforcement wave: the faction's largest convoy group, paid by nobody (it still draws on the vehicle reserve).</summary>
        private static void FreeWave(FactionHQ hq)
        {
            List<Faction.ConvoyGroup> groups = hq.faction != null ? hq.faction.GetConvoyGroups() : null;
            Faction.ConvoyGroup best = null;
            int most = 0;
            if (groups != null)
                foreach (Faction.ConvoyGroup group in groups)
                {
                    int n = 0;
                    if (group?.Constituents != null) foreach (Faction.ConvoyUnit unit in group.Constituents) n += unit != null ? unit.Count : 0;
                    if (n > most) { most = n; best = group; }
                }
            if (best == null) return;
            try { hq.AddConvoy(best); }
            catch (Exception e) { Plugin.Logger?.LogWarning("[Nation] free wave not sent: " + e.Message); }
        }

        /// <summary>SABOTAGE: the enemy's busiest plant stops for five minutes; destroying it while it is down pays +5.</summary>
        private void Sabotage(Side side, Side enemy, float now)
        {
            Plant target = null;
            foreach (Plant plant in enemy.Book.Plants)
                if (target == null || (plant.Kind == LineKind.Aircraft || plant.Kind == LineKind.Vehicle) && target.Kind != LineKind.Aircraft && target.Kind != LineKind.Vehicle)
                    target = plant;
            if (target == null) return;
            target.RaidedUntil = now + Decisions.Lasts((int)DecisionId.Sabotage);
            enemy.Book.Dirty = true;
            side.SabotagedPlant = target.Id;
            side.SabotagedUntil = target.RaidedUntil;
        }

        /// <summary>The closest pair of land airbases across the line: ours (the most threatened) and theirs (the next objective).</summary>
        private bool Closest(Side side, out Airbase ours, out Airbase theirs)
        {
            ours = theirs = null;
            float best = float.MaxValue;
            foreach (Airbase own in side.Owner.GetAirbases())
            {
                if (own == null || own.disabled || own.AttachedAirbase || own.center == null) continue;
                foreach (KeyValuePair<Airbase, FactionHQ> pair in owners)
                {
                    Airbase other = pair.Key;
                    if (other == null || other.AttachedAirbase || other.center == null || other.CurrentHQ == null || other.CurrentHQ == side.Owner) continue;
                    float d = (other.center.position - own.center.position).sqrMagnitude;
                    if (d < best) { best = d; ours = own; theirs = other; }
                }
            }
            return ours != null;
        }

        private Side EnemyOf(Side side)
        {
            foreach (Side other in sides.Values) if (other != side && other.Owner != null && other.Rows.Count > 0) return other;
            return null;
        }

        private static int Held(FactionHQ hq)
        {
            int n = 0;
            foreach (Airbase airbase in hq.GetAirbases()) if (airbase != null && !airbase.disabled && !airbase.AttachedAirbase) n++;
            return n;
        }

        private static string NameOf(Side side) => side?.Owner?.faction?.factionName?.ToUpperInvariant() ?? "THE ENEMY";

        private void FillPolitics(Side side, float now)
        {
            NationView v = side.View;
            side.Politics.Fill(v, now, stage);
            Side enemy = EnemyOf(side);
            v.EnemyName = enemy != null ? NameOf(enemy) : "";
            if (enemy != null) enemy.Politics.FillEnemy(v, now, stage);
            string intel = "";
            if (enemy != null && side.Politics.InForce(DecisionId.Defector, now) && ModuleServices.TryGet(out ITheaterWarPolicy policy) &&
                policy.TryMainEffort(enemy.Owner, out string effort)) intel = (effort ?? "").ToUpperInvariant();
            v.EnemyIntel = intel;
            // What moves with time or with the other side and never marks the book: the client hears of it on change, not only on the heartbeat.
            int key;
            unchecked
            {
                key = v.PostureFloor + v.PostureCeiling * 3 + (v.JetsDelta + 16) * 9 + v.Rank * 997 + (int)v.EnemyDoctrine * 7919 + v.EnemyRank * 104729 +
                      Mathf.RoundToInt(v.EnemyWarSupport) * 31337 + v.EnemyDecidedSerial * 65521 + intel.GetHashCode();
            }
            if (!policyKeys.TryGetValue(side.Owner, out int was) || was != key) { policyKeys[side.Owner] = key; side.Politics.Dirty = true; }
        }

        /// <summary>Hands every faction back to the vanilla war (AI cap, skill, posture band) when the layer stops.</summary>
        private void ReleasePolicy()
        {
            if (!GameAccess.IsServer() || !ModuleServices.TryGet(out ITheaterWarPolicy policy)) return;
            foreach (Side side in sides.Values)
                if (side.Owner != null)
                    try { policy.Apply(side.Owner, WarPolicy.Vanilla); }
                    catch (Exception e) { Plugin.Logger?.LogWarning("[Nation] war policy not released: " + e.Message); }
            policyKeys.Clear();
        }

        // ---- Every peer: the card on the dispatch, the headlines in the log ------------------------

        private void Present()
        {
            if (Time.unscaledTime < nextPresent) return;
            nextPresent = Time.unscaledTime + 0.5f;
            NationView v = Local;
            if (v == null) { ClosePresented(); newsReady = false; return; }
            News(v);
            if (!ModuleServices.TryGet(out IPoliticalDispatch dispatch)) return;
            if (!CabinetCards.Fill(v, card)) { ClosePresented(); return; }
            if (presented != 0 && presented != card.Serial) dispatch.Close(presented);
            presented = card.Serial;
            dispatch.Show(card, pick ??= option => Request(NationVerb.Answer, presented, option));
        }

        private void ClosePresented()
        {
            if (presented != 0 && ModuleServices.TryGet(out IPoliticalDispatch dispatch)) dispatch.Close(presented);
            presented = 0;
        }

        /// <summary>Doctrines, decisions and answers of both sides into the log's NEWS, once each; the first view only sets the marks.</summary>
        private void News(NationView v)
        {
            string us = GameAccess.TryGetLocalFaction(out FactionHQ hq) && hq.faction != null ? hq.faction.factionName.ToUpperInvariant() : "WE";
            if (newsReady)
            {
                if (v.DecidedSerial != postedOurs && v.Decided >= 0) Post(us + " DECIDES " + Decisions.Name(v.Decided) + " · " + v.DecidedBy);
                if (v.EnemyDecidedSerial != postedTheirs && v.EnemyDecided >= 0) Post(v.EnemyName + " DECIDES " + Decisions.Name(v.EnemyDecided));
                if (v.Doctrine != postedDoctrine && v.Doctrine != Doctrine.None) Post(us + " CHOOSES " + Doctrines.Name(v.Doctrine) + " · " + v.DoctrineBy);
                if (v.EnemyDoctrine != postedEnemyDoctrine && v.EnemyDoctrine != Doctrine.None) Post(v.EnemyName + " CHOOSES " + Doctrines.Name(v.EnemyDoctrine));
                if (v.CabinetSerial > 0 && v.CabinetChosen >= 0 && v.CabinetSerial != postedAnswer && v.CabinetId != (int)CardId.WarCabinet)
                    Post("CABINET · " + CabinetCards.Title(v.CabinetId, v.CabinetPlace) + ": " + CabinetCards.OptionLabel(v.CabinetId, v.CabinetChosen) + " · " + v.CabinetChosenBy);
            }
            newsReady = true;
            postedOurs = v.DecidedSerial;
            postedTheirs = v.EnemyDecidedSerial;
            postedDoctrine = v.Doctrine;
            postedEnemyDoctrine = v.EnemyDoctrine;
            if (v.CabinetSerial > 0 && v.CabinetChosen >= 0) postedAnswer = v.CabinetSerial;
        }

        private void Post(string headline)
        {
            newsPosted++;
            PoliticsWire.Post(headline);
        }

        // ---- Sim hook ---------------------------------------------------------------------------------

        /// <summary>
        /// Sim hook: every faction's nation and politics as plain values. Args: "verb" (+ "a", "b") presses a verb as the local player,
        /// "answer" answers the local open card with that option, "decide" takes that decision; dev builds also take "open" (a card id the local
        /// side's cabinet opens now) and "convoy" (draws convoys of that group until the reserve refuses one).
        /// </summary>
        public static Dictionary<string, object> Probe(Dictionary<string, object> args)
        {
            var result = new Dictionary<string, object> { ["ok"] = live != null && live.layerOn };
            if (live == null) return result;
            if (args != null && args.TryGetValue("verb", out object verb) && Enum.TryParse(Convert.ToString(verb), true, out NationVerb parsed))
                result["outcome"] = live.Request(parsed, args.TryGetValue("a", out object a) ? Convert.ToInt32(a) : 0,
                    args.TryGetValue("b", out object b) ? Convert.ToInt32(b) : 0).ToString();
#if !PUBLIC_RELEASE
            if (args != null && args.TryGetValue("open", out object open) && GameAccess.TryGetLocalFaction(out FactionHQ here) &&
                live.sides.TryGetValue(here, out Side local) && NetworkSceneSingleton<MissionManager>.i != null)
            {
                int id = Convert.ToInt32(open);
                result["opened"] = local.Politics.Open(id, id == (int)CardId.PeaceFeeler ? NameOf(live.EnemyOf(local)) : "",
                    id == (int)CardId.WarCabinet ? 0 : CabinetCards.StaffChoice(id), NetworkSceneSingleton<MissionManager>.i.MissionTime);
            }
#endif
            NationView view = live.Local;
            if (args != null && args.TryGetValue("answer", out object answer))
                result["answered"] = (view != null && view.CabinetSerial > 0 ? live.Request(NationVerb.Answer, view.CabinetSerial, Convert.ToInt32(answer)) : NationOutcome.NotOpen).ToString();
            if (args != null && args.TryGetValue("decide", out object decide))
                result["decided"] = live.Request(NationVerb.Decide, Convert.ToInt32(decide)).ToString();
            var factions = new List<object>();
            int cardsOpen = 0, answered = 0, doctrines = 0, decisions = 0, staffDoctrines = 0;
            foreach (Side side in live.sides.Values)
            {
                NationView v = side.View;
                PoliticsBook p = side.Politics;
                int produced = 0;
                for (int i = 0; i < v.Stock.Count; i++) produced += v.Stock[i].Count;
                if (p.Card >= 0) cardsOpen++;
                answered += p.Serial - (p.Card >= 0 && p.Chosen < 0 ? 1 : 0);
                if (p.Doctrine != Doctrine.None) doctrines++;
                if (p.Doctrine != Doctrine.None && p.ChosenBy == PoliticsRules.Staff) staffDoctrines++;
                decisions += p.DecidedSerial;
                factions.Add(new Dictionary<string, object>
                {
                    ["faction"] = side.Owner != null ? side.Owner.name : "", ["plants"] = v.Plants.Count, ["slots"] = v.Slots,
                    ["economy"] = v.Economy.ToString(), ["priority"] = v.Priority.ToString(), ["building"] = v.Building, ["stock"] = produced,
                    ["rows"] = v.Stock.Count, ["funds"] = Mathf.RoundToInt(v.Funds), ["output"] = v.Output,
                    ["warSupport"] = Mathf.RoundToInt(p.WarSupport), ["doctrine"] = p.Doctrine.ToString(), ["rank"] = v.Rank,
                    ["chosenBy"] = p.ChosenBy, ["card"] = p.Card, ["cardSerial"] = p.Serial, ["chosen"] = p.Chosen, ["answeredBy"] = p.AnsweredBy,
                    ["decided"] = p.Decided >= 0 ? Decisions.Name(p.Decided) : "", ["decidedBy"] = p.DecidedBy, ["decisions"] = p.DecidedSerial,
                    ["jets"] = v.JetsDelta, ["floor"] = (int)v.PostureFloor, ["ceiling"] = (int)v.PostureCeiling, ["moves"] = v.Moves.Count,
                });
            }
            if (args != null && args.TryGetValue("convoy", out object convoy) && GameAccess.TryGetLocalFaction(out FactionHQ mine) &&
                mine.faction.TryGetConvoyGroup(Convert.ToInt32(convoy), out Faction.ConvoyGroup group) && live.sides.TryGetValue(mine, out Side own))
            {
                // Draw convoys of this group until the reserve refuses one: proves the draw and the NOT PRODUCED gate together.
                int sent = 0;
                while (sent < 8 && Covers(mine, group)) { mine.AddConvoy(group); sent++; }
                int left = 0;
                for (int i = 0; i < own.Rows.Count; i++) if (!own.Air[i]) left += own.Reserve[i];
                result["convoysSent"] = sent;
                result["convoyRefused"] = !Covers(mine, group);
                result["reserveLeft"] = left;
            }
            int vehicleRows = 0, reserve = 0, plants = 0, stock = 0;
            foreach (Side side in live.sides.Values)
            {
                for (int i = 0; i < side.Rows.Count; i++) if (!side.Air[i]) { vehicleRows++; reserve += side.Reserve[i]; }
                plants += side.View.Plants.Count;
                for (int i = 0; i < side.View.Stock.Count; i++) stock += side.View.Stock[i].Count;
            }
            if (view != null)
                result["local"] = new Dictionary<string, object>
                {
                    ["warSupport"] = Mathf.RoundToInt(view.WarSupport), ["doctrine"] = view.Doctrine.ToString(), ["rank"] = view.Rank,
                    ["cabinetSerial"] = view.CabinetSerial, ["cabinetId"] = view.CabinetId, ["cabinetChosen"] = view.CabinetChosen,
                    ["cabinetLeft"] = Mathf.RoundToInt(view.CabinetLeft), ["decided"] = view.Decided >= 0 ? Decisions.Name(view.Decided) : "",
                    ["enemy"] = view.EnemyName, ["enemyDoctrine"] = view.EnemyDoctrine.ToString(), ["jets"] = view.JetsDelta, ["moves"] = view.Moves.Count,
                };
            result["vehicleRows"] = vehicleRows;
            result["reserve"] = reserve;
            result["sides"] = live.sides.Count;
            result["plantsTotal"] = plants;
            result["stockTotal"] = stock;
            result["cardsOpen"] = cardsOpen;
            result["cardsAnswered"] = answered;
            result["doctrines"] = doctrines;
            result["staffDoctrines"] = staffDoctrines;
            result["newsPosted"] = live.newsPosted;
            result["decisions"] = decisions;
            result["factions"] = factions;
            return result;
        }
    }
}

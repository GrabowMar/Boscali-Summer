using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.TheaterOps.Domain;
using HarmonyLib;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>Plain call counters for the MissionPosition postfixes and the slot fallback. Nothing in the game reads them; the sim hook below reports them.
    /// They change no behaviour, so they are compiled in every build.</summary>
    internal static class GroundProbe
    {
        /// <summary>Advance postfix: every call, the calls for a ground vehicle, the calls for an enrolled member, the calls that replaced vanilla's answer.</summary>
        internal static int AdvanceRan, AdvanceGround, AdvanceEnrolled, AdvanceReplaced;
        /// <summary>Why an enrolled member's poll was left to vanilla: its group has no key, no front to stage behind, or the last pass is stale.</summary>
        internal static int AdvanceUnassigned, AdvanceNoFront, AdvanceStale;
        /// <summary>Delivery postfix: every call, the calls that replaced the sort key, depot and airbase rewrites (airbase is never rewritten), airbase queries left alone.</summary>
        internal static int DeliveryRan, DeliveryReplaced, DepotRewrites, AirbaseRewrites, AirbaseSkips;
        /// <summary>Members that found no usable slot candidate and hold where they stood; group passes that held in place for want of a known friendly side, and passes with no line to stage behind.</summary>
        internal static int SlotFallbacks, SlotMarches, FrameHold, FrameNoFront;
    }

#if !PUBLIC_RELEASE
    /// <summary>
    /// Ground-war proof for the unattended STR scenarios (<c>tests/ingame/str-1-ground-*.json</c>). <c>Read</c> reports per faction what the ground service did and
    /// cross-checks it against the world (where vanilla really steers each answered vehicle); <c>Arm</c> picks for the seated pilot, <c>Order</c> gives one vehicle a
    /// real player order, <c>Tactics</c> flips the live switch, <c>Late</c> adds vehicles after the first sweep. The mutating verbs refuse outside a nomodkit run.
    /// Dev tooling, compiled out of public releases. Results are nested dictionaries without dots in key names; an unknown number is -1.
    /// </summary>
    public static class GroundProof
    {
        private const float AnsweredSeconds = 8f, SteadySeconds = 8f;

        private sealed class Memo
        {
            // One episode per group and (key, stage): the distance to the target when it was first read, when, and whether it ever closed 200 m or came within 600 m.
            internal readonly Dictionary<int, Episode> Episodes = new Dictionary<int, Episode>();
            // The most any single read has shown. The mission script can finish an objective operation within 90 s, so a check that must see "some group landed" or "some group
            // closed" cannot depend on one named sample falling inside a live operation.
            internal int LandedPeak, ClosingPeak, AssignedPeak;
            internal readonly Dictionary<int, Vector3> Target = new Dictionary<int, Vector3>();   // x = target x, y = stage, z = target z
        }

        private sealed class Episode { internal string Key; internal GroundStage Stage; internal float Distance, At; internal bool Closed; }

        private sealed class LateOrder
        {
            internal FactionHQ Hq; internal string Faction; internal bool Scan; internal float At;
            internal VehicleDefinition Definition; internal HashSet<int> Known;
        }

        private static readonly Dictionary<string, Memo> memos = new Dictionary<string, Memo>();
        private static readonly List<GroundGroupReport> groupBuffer = new List<GroundGroupReport>();
        private static readonly List<GroundMemberReport> memberBuffer = new List<GroundMemberReport>();
        private static readonly List<GroundVehicle> lateVehicles = new List<GroundVehicle>();
        private static readonly List<string> lateFactions = new List<string>();
        private static readonly List<LateOrder> pendingLate = new List<LateOrder>();
        private static readonly FieldInfo DepotSorted = AccessTools.Field(typeof(FactionHQ), "depotSorted");
        private static readonly FieldInfo NavigateObjectives = AccessTools.Field(typeof(GroundVehicle), "navigateToObjectives");

        // The pick the seat made (Arm), the hook counters at that moment, and the one player order.
        private static float pickFirstAt = -1f;
        private static int pickCount, pickOverridesBaseline;
        private static string pickKind = "", pickLabel = "", pickNote = "";
        private static GroundVehicle orderVehicle;
        private static int orderGroup;
        private static float orderAt = -1f, orderSlotX, orderSlotZ;
        private static PickBot bot;

        private static bool InSimRun => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));

        // ---- Arm: the seated pilot picks ------------------------------------------------------------------------------------------

        /// <summary>{kinds: "ASSAULT,DEFEND"}: sends the panel's own pick for the first card of the first listed kind (a mission objective preferred, since a sector front can lie across
        /// water from every vehicle); takes the next kind when none appears 30 s after the first deck; picks again when no operation has lived for 10 s, at most six picks.</summary>
        public static Dictionary<string, object> Arm(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("GroundProof", "Arm", "requires a nomodkit scenario run");
            if (bot != null) UnityEngine.Object.Destroy(bot.gameObject);
            pickFirstAt = -1f; pickCount = 0; pickKind = pickLabel = pickNote = "";
            string kinds = Text(args, "kinds");
            var go = new GameObject("GroundProofArm");
            bot = go.AddComponent<PickBot>();
            bot.Kinds = (string.IsNullOrEmpty(kinds) ? "ASSAULT,DEFEND" : kinds).ToUpperInvariant().Split(',');
            return new Dictionary<string, object> { { "ok", true }, { "kinds", kinds ?? "ASSAULT,DEFEND" } };
        }

        private sealed class PickBot : MonoBehaviour
        {
            internal string[] Kinds = { "ASSAULT", "DEFEND" };
            private ITheaterWarView view;
            private float nextTry, firstDeckAt = -1f, noOperationSince = -1f;
            private int kindIndex;

            private void Update()
            {
                float now = Time.timeSinceLevelLoad;
                if (now < nextTry) return;
                nextTry = now + 1f;
                if (view == null) ModuleServices.TryGet(out view);
                LivingFrontService living = LivingFrontService.Active;
                if (view == null || living == null || !GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq.faction == null) return;
                bool live = living.Wars.TryGetValue(hq.faction.factionName, out LivingFrontService.FactionWar war) && war.Active != null;
                if (live) { noOperationSince = -1f; return; }
                if (noOperationSince < 0f) noOperationSince = now;
                TheaterWarSnapshot snapshot = view.Snapshot;
                if (pickCount >= 6 || snapshot == null || snapshot.Proposals.Length == 0) return;
                if (firstDeckAt < 0f) firstDeckAt = now;
                if (pickCount > 0 && now - noOperationSince < 10f) return;
                if (!view.CanCommand) return;
                TheaterProposalView card = Choose(snapshot, now);
                if (card == null) { pickNote = "no " + string.Join("/", Kinds) + " card"; return; }
                if (!view.RequestPick(card.Id, card.Revision))
                {
                    pickNote = "pick refused: " + (view.LastCommand != null ? view.LastCommand.Code.ToString() : "none");
                    return;
                }
                if (pickCount == 0) { pickFirstAt = now; pickOverridesBaseline = GroundProbe.AdvanceReplaced; }
                pickCount++;
                pickKind = card.Kind.ToString().ToUpperInvariant();
                pickLabel = card.Name;
                pickNote = "picked " + card.TargetKey;
            }

            private TheaterProposalView Choose(TheaterWarSnapshot snapshot, float now)
            {
                // A kind that has no card 30 s after the first deck gives way to the next one listed.
                while (kindIndex < Kinds.Length - 1 && !HasKind(snapshot, Kinds[kindIndex]) && now - firstDeckAt >= 30f) kindIndex++;
                // A mission objective first (the script can finish one early; the picker then picks again), else the first card of the kind.
                TheaterProposalView objective = null, any = null;
                foreach (TheaterProposalView card in snapshot.Proposals)
                {
                    if (card.Kind.ToString().ToUpperInvariant() != Kinds[kindIndex]) continue;
                    if (objective == null && !card.TargetKey.StartsWith("@") && !card.TargetKey.StartsWith("#")) objective = card;
                    if (any == null) any = card;
                }
                return objective ?? any;
            }

            private static bool HasKind(TheaterWarSnapshot snapshot, string kind)
            {
                foreach (TheaterProposalView card in snapshot.Proposals)
                    if (card.Kind.ToString().ToUpperInvariant() == kind) return true;
                return false;
            }
        }

        // ---- Tactics, Order, Late --------------------------------------------------------------------------------------------------

        /// <summary>{enabled: bool}: writes the entry the SET SERVER row writes.</summary>
        public static Dictionary<string, object> Tactics(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("GroundProof", "Tactics", "requires a nomodkit scenario run");
            GroundFrontService front = GroundFrontService.Active;
            if (front == null || front.Settings == null) return Failure("GroundProof", "Tactics", "the ground service is not running");
            front.Settings.FrontlineTacticsEnabled.Value = Bool(args, "enabled");
            return new Dictionary<string, object> { { "ok", true }, { "enabled", front.Settings.FrontlineTacticsEnabled.Value } };
        }

        /// <summary>{faction: "Boscali"}: a real local-player command (UnitCommand.SetDestination with playerCommand true) for the last live member of the faction's largest group,
        /// to a point 800 m from its slot. Read reports whether the order won 40 s later.</summary>
        public static Dictionary<string, object> Order(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("GroundProof", "Order", "requires a nomodkit scenario run");
            GroundFrontService front = GroundFrontService.Active;
            if (front == null || !TryFaction(Text(args, "faction"), out FactionHQ hq)) return Failure("GroundProof", "Order", "no ground service or unknown faction");
            front.CopyGroups(hq, groupBuffer);
            front.CopyMembers(hq, memberBuffer);
            int best = -1, bestAlive = 0;
            foreach (GroundGroupReport g in groupBuffer) if (g.Alive > bestAlive) { best = g.Id; bestAlive = g.Alive; }
            if (best < 0) return Failure("GroundProof", "Order", "no enrolled group");
            GroundMemberReport pick = default;
            foreach (GroundMemberReport m in memberBuffer) if (m.GroupId == best && m.Vehicle != null && !m.Vehicle.disabled) pick = m;
            if (pick.Vehicle == null) return Failure("GroundProof", "Order", "no live member");
            GlobalPosition here = pick.Vehicle.GlobalPosition();
            orderSlotX = pick.HasStaged ? pick.StagedX : here.x;
            orderSlotZ = pick.HasStaged ? pick.StagedZ : here.z;
            orderVehicle = pick.Vehicle; orderGroup = best; orderAt = Time.timeSinceLevelLoad;
            pick.Vehicle.UnitCommand.SetDestination(new GlobalPosition(orderSlotX + 800f, here.y, orderSlotZ), true);
            return new Dictionary<string, object> { { "ok", true }, { "group", best }, { "slotX", orderSlotX }, { "slotZ", orderSlotZ } };
        }

        /// <summary>{faction, count, via: "scan" | "depot"}: scan spawns clones of a live member's definition 150 to 300 m behind the faction's largest group, so only the sweep can
        /// find them; depot adds supply so the vanilla deploy loop brings them out of a depot through MoveFromDepot (the fresh-wave path).</summary>
        public static Dictionary<string, object> Late(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("GroundProof", "Late", "requires a nomodkit scenario run");
            GroundFrontService front = GroundFrontService.Active;
            if (front == null || !TryFaction(Text(args, "faction"), out FactionHQ hq)) return Failure("GroundProof", "Late", "no ground service or unknown faction");
            int count = Mathf.Clamp((int)Number(args, "count", 2f), 1, 6);
            bool scan = (Text(args, "via") ?? "scan") == "scan";
            front.CopyGroups(hq, groupBuffer);
            front.CopyMembers(hq, memberBuffer);
            int best = -1, bestAlive = 0;
            foreach (GroundGroupReport g in groupBuffer) if (g.Alive > bestAlive) { best = g.Id; bestAlive = g.Alive; }
            GroundGroupReport group = default;
            foreach (GroundGroupReport g in groupBuffer) if (g.Id == best) group = g;
            // A definition the placement rule accepts (an artillery piece or a heavy truck may not fit), preferring a member of the largest group.
            VehicleDefinition definition = null;
            foreach (GroundMemberReport m in memberBuffer)
                if (m.Vehicle != null && m.Vehicle.definition is VehicleDefinition d && GroundPlacement.Usable(d) && (definition == null || m.GroupId == best)) definition = d;
            if (best < 0 || definition == null) return Failure("GroundProof", "Late", "no live member with a placeable definition");
            string faction = Key(hq.faction.factionName);
            if (!scan)
            {
                var known = new HashSet<int>();
                foreach (Unit unit in UnitRegistry.allUnits) if (unit is GroundVehicle v && ReferenceEquals(v.NetworkHQ, hq)) known.Add(v.GetInstanceID());
                pendingLate.Add(new LateOrder { Hq = hq, Faction = faction, Scan = false, At = Time.timeSinceLevelLoad, Definition = definition, Known = known });
                hq.ModifyUnitSupply(definition, count);
                return new Dictionary<string, object> { { "ok", true }, { "via", "depot" }, { "count", count } };
            }
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer) return Failure("GroundProof", "Late", "no spawner on this peer");
            // Behind the group first (away from its fix), then around it: any dry, level spot 150 to 450 m from its centroid will do for a vehicle only the sweep can find.
            // GroundPlacement works in local (floating-origin) coordinates at the terrain's own height: a member's transform is the anchor, not the global centroid.
            Vector3 anchor = Vector3.zero;
            foreach (GroundMemberReport m in memberBuffer) if (m.GroupId == best && m.Vehicle != null && !m.Vehicle.disabled) anchor = m.Vehicle.transform.position;
            float dx = group.X - group.FixX, dz = group.Z - group.FixZ;
            float baseAngle = Mathf.Atan2(dz, dx);
            int spawned = 0, tried = 0;
            for (int i = 0; i < count; i++)
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    tried++;
                    float angle = baseAngle + (attempt % 8) * Mathf.PI / 4f + i * 0.35f, radius = 150f + (attempt / 8) * 150f + i * 40f;
                    var wanted = new Vector3(anchor.x + Mathf.Cos(angle) * radius, anchor.y, anchor.z + Mathf.Sin(angle) * radius);
                    if (!GroundPlacement.TryPlace(definition, wanted, Quaternion.identity, out Vector3 position)) continue;
                    Unit unit = spawner.SpawnVehicle(definition.unitPrefab, position.ToGlobalPosition(), Quaternion.identity, Vector3.zero, hq,
                        "BoscaliSummer:GroundProof:late:" + (lateVehicles.Count + 1), 1f, true, null);
                    if (!(unit is GroundVehicle vehicle)) continue;
                    // A bare spawn holds still and does not head for objectives; a mission-scripted wave and a depot exit both move out, so the clone is set up like them.
                    vehicle.SetHoldPosition(false);
                    NavigateObjectives?.SetValue(vehicle, true);
                    lateVehicles.Add(vehicle); lateFactions.Add(faction); spawned++;
                    break;
                }
            if (spawned == 0) return Failure("GroundProof", "Late", "no spot to place " + definition.name + " near group " + best + " after " + tried + " tries");
            // Why a clone would not be eligible (the first one): the five conditions of GroundFrontService.AutoNavigating, one by one.
            GroundVehicle probe = lateVehicles[lateVehicles.Count - 1];
            return new Dictionary<string, object>
            {
                { "ok", true }, { "via", "scan" }, { "spawned", spawned },
                { "server", probe.IsServer }, { "disabled", probe.disabled }, { "hold", probe.GetHoldPosition() }, { "hasCommand", probe.UnitCommand != null },
                { "commanded", GroundFrontService.IsCommanded(probe) }, { "autoNavigating", GroundFrontService.AutoNavigating(probe) },
                { "sameHq", ReferenceEquals(probe.NetworkHQ, hq) }, { "slung", probe.IsSlung() },
            };
        }

        // ---- Read ----------------------------------------------------------------------------------------------------------------------

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("GroundProof", "Read", "requires a nomodkit scenario run");
            GroundFrontService front = GroundFrontService.Active;
            LivingFrontService living = LivingFrontService.Active;
            float now = Time.timeSinceLevelLoad;
            var ids = new Dictionary<string, object>();
            var ground = new Dictionary<string, object>();
            int membersAll = 0, answered = 0, answeredLanded = 0, unlandedAuto = 0, steadyAnswered = 0;
            DetectLate();

            if (front != null && living != null)
                foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                {
                    if (hq == null || hq.faction == null) continue;
                    string key = Key(hq.faction.factionName);
                    living.Wars.TryGetValue(hq.faction.factionName, out LivingFrontService.FactionWar war);
                    ground[key] = FactionRead(front, hq, key, war?.Active, now, ids, ref membersAll, ref answered, ref answeredLanded, ref unlandedAuto, ref steadyAnswered);
                }

            var hook = new Dictionary<string, object>
            {
                { "callsAll", GroundProbe.AdvanceGround }, { "calls", GroundProbe.AdvanceEnrolled }, { "overrides", GroundProbe.AdvanceReplaced },
                { "unassigned", GroundProbe.AdvanceUnassigned }, { "noFront", GroundProbe.AdvanceNoFront }, { "stale", GroundProbe.AdvanceStale },
                { "ran", GroundProbe.AdvanceRan }, { "members", membersAll },
                { "slotFallbacks", GroundProbe.SlotFallbacks }, { "slotMarches", GroundProbe.SlotMarches }, { "holdFrames", GroundProbe.FrameHold }, { "noFrontFrames", GroundProbe.FrameNoFront },
                // The postfix polled every enrolled member about three times or more (vanilla asks each vehicle about every 4 s); -1 below four members.
                { "h1", membersAll < 4 ? -1 : GroundProbe.AdvanceEnrolled >= 3 * membersAll ? 1 : 0 },
                { "overridesSincePick", pickFirstAt >= 0f ? GroundProbe.AdvanceReplaced - pickOverridesBaseline : -1 },
                { "answered", answered }, { "answeredShare", membersAll > 0 ? answered / (float)membersAll : -1f },
                // Polls of enrolled members that the staff answered (a staged point or a hold point) against all of them; the rest are groups with no key, left to vanilla.
                { "overrideShare", GroundProbe.AdvanceEnrolled > 0 ? GroundProbe.AdvanceReplaced / (float)GroundProbe.AdvanceEnrolled : -1f },
                // Of the vehicles the hook answered lately in a settled group: how many are steered by vanilla to the answered point (within max(300 m, 20 percent of the way)).
                { "h2Fraction", steadyAnswered < 4 ? -1f : answeredLanded / (float)steadyAnswered },
                { "h2Unlanded", steadyAnswered - answeredLanded }, { "h2UnlandedAutoNav", unlandedAuto },
            };
            return new Dictionary<string, object>
            {
                { "ok", true }, { "t", now },
                { "pick", PickRead(now) }, { "hook", hook }, { "delivery", DeliveryRead() }, { "order", OrderRead(front, now) },
                { "ground", ground }, { "ids", ids },
            };
        }

        private static Dictionary<string, object> PickRead(float now) => new Dictionary<string, object>
        {
            { "ok", pickCount > 0 ? 1 : 0 }, { "count", pickCount }, { "kind", pickKind }, { "label", pickLabel }, { "note", pickNote },
            { "at", pickFirstAt }, { "sincePick", pickFirstAt >= 0f ? now - pickFirstAt : -1f },
        };

        private static Dictionary<string, object> DeliveryRead()
        {
            int depots = -1, nearest = -1;
            if (GameAccess.TryGetLocalFaction(out FactionHQ hq) && hq.faction != null && DepotSorted != null &&
                DepotSorted.GetValue(hq) is List<(VehicleDepot, float)> sorted)
            {
                depots = sorted.Count;
                TheaterPriorityService priority = TheaterPriorityService.Active;
                if (depots > 0 && priority != null && priority.TryGetDirective(hq.faction.factionName, out PriorityDirective directive) && sorted[0].Item1 != null)
                {
                    // The independent check: of the depots in the faction's own list, is the one the vanilla deploy loop tries first the nearest to the effort?
                    var fix = new GlobalPosition(directive.X, directive.Y, directive.Z);
                    float first = FastMath.Distance(fix, sorted[0].Item1.transform.GlobalPosition()), least = first;
                    foreach ((VehicleDepot depot, float _) in sorted)
                        if (depot != null) least = Mathf.Min(least, FastMath.Distance(fix, depot.transform.GlobalPosition()));
                    nearest = first <= least + 1f ? 1 : 0;
                }
            }
            return new Dictionary<string, object>
            {
                { "calls", GroundProbe.DeliveryRan }, { "depotRewrites", GroundProbe.DepotRewrites }, { "airbaseRewrites", GroundProbe.AirbaseRewrites },
                { "airbaseSkips", GroundProbe.AirbaseSkips }, { "mode", "wrapper" }, { "depots", depots }, { "firstDepotIsNearest", nearest },
            };
        }

        private static Dictionary<string, object> OrderRead(GroundFrontService front, float now)
        {
            int held = -1, withdrew = -1;
            if (orderVehicle != null && orderAt >= 0f && now - orderAt >= 40f)
            {
                bool member = false;
                GroundGroupReport group = default;
                bool groupFound = false;
                if (front != null && orderVehicle.NetworkHQ != null)
                {
                    front.CopyMembers(orderVehicle.NetworkHQ, memberBuffer);
                    foreach (GroundMemberReport m in memberBuffer) if (ReferenceEquals(m.Vehicle, orderVehicle)) member = true;
                    front.CopyGroups(orderVehicle.NetworkHQ, groupBuffer);
                    foreach (GroundGroupReport g in groupBuffer) if (g.Id == orderGroup) { group = g; groupFound = true; }
                }
                GlobalPosition nav = orderVehicle.GetDestination();
                bool far = Scalar.Distance2D(nav.x, nav.z, orderSlotX, orderSlotZ) > 500f;
                // The order won: the vehicle left the formation, vanilla holds its commanded destination, and that is nowhere near the slot the staff wanted.
                held = !member && GroundFrontService.IsCommanded(orderVehicle) && far ? 1 : 0;
                withdrew = groupFound && group.Stage == GroundStage.Withdraw ? 1 : 0;
            }
            return new Dictionary<string, object> { { "held", held }, { "groupWithdrew", withdrew }, { "issued", orderAt >= 0f ? 1 : 0 } };
        }

        private static Dictionary<string, object> FactionRead(GroundFrontService front, FactionHQ hq, string key, LivingFrontService.Operation op, float now,
            Dictionary<string, object> ids, ref int membersAll, ref int answered, ref int answeredLanded, ref int unlandedAuto, ref int steadyAnswered)
        {
            if (!memos.TryGetValue(key, out Memo memo)) memos[key] = memo = new Memo();
            front.CopyGroups(hq, groupBuffer);
            front.CopyMembers(hq, memberBuffer);
            GroundFactionStats stats = front.StatsOf(hq);
            string name = hq.faction.factionName;
            bool haveReport = front.TryReport(name, op != null ? op.Key : "", out GroundOpReport report) && report.Steering;

            // Each group's centroid as the proof sees it (from the vehicles, not the service's field).
            var groups = new Dictionary<int, GroundGroupReport>();
            var cx = new Dictionary<int, float>(); var cz = new Dictionary<int, float>(); var alive = new Dictionary<int, int>();
            foreach (GroundGroupReport g in groupBuffer) groups[g.Id] = g;
            int supportEnrolled = 0, stagedMembers = 0, onSlot = 0, steered = 0, strayed = 0, frozen = 0;
            var answeredIn = new Dictionary<int, int>(); var landedIn = new Dictionary<int, int>();
            foreach (GroundMemberReport m in memberBuffer)
            {
                GroundVehicle v = m.Vehicle;
                if (v == null || v.disabled) continue;
                GlobalPosition at = v.GlobalPosition();
                cx[m.GroupId] = (cx.TryGetValue(m.GroupId, out float sx) ? sx : 0f) + at.x;
                cz[m.GroupId] = (cz.TryGetValue(m.GroupId, out float sz) ? sz : 0f) + at.z;
                alive[m.GroupId] = (alive.TryGetValue(m.GroupId, out int a) ? a : 0) + 1;
                ids["veh_g" + m.GroupId + "_" + v.GetInstanceID()] = v;
                membersAll++;
                if (v.GetComponentInChildren<MobileArtilleryAI>(true) != null || v.GetComponentInChildren<RearmVehicleAI>(true) != null ||
                    v.GetComponentInChildren<Rearmer>(true) != null || v.GetComponentInChildren<Repairer>(true) != null) supportEnrolled++;
                if (m.Frozen) frozen++;
                if (m.HasStaged)
                {
                    stagedMembers++;
                    if (Scalar.Distance2DSquared(at.x, at.z, m.StagedX, m.StagedZ) <= 300f * 300f) onSlot++;
                }
                if (m.AnsweredAgo < 0f || m.AnsweredAgo > AnsweredSeconds) continue;
                steered++; answered++;
                GlobalPosition nav = v.GetDestination();
                float away = Scalar.Distance2D(at.x, at.z, m.AnswerX, m.AnswerZ);
                bool isLanded = Scalar.Distance2D(nav.x, nav.z, m.AnswerX, m.AnswerZ) <= Mathf.Max(300f, .2f * away);
                answeredIn[m.GroupId] = (answeredIn.TryGetValue(m.GroupId, out int ai) ? ai : 0) + 1;
                if (isLanded) landedIn[m.GroupId] = (landedIn.TryGetValue(m.GroupId, out int li) ? li : 0) + 1;
                // Vanilla adopts a changed answer within about 30 s, so only members of a settled group are judged for the fraction and for straying.
                if (m.StageAge < SteadySeconds) continue;
                steadyAnswered++;
                if (isLanded) answeredLanded++;
                else
                {
                    strayed++;
                    if (GroundFrontService.AutoNavigating(v)) unlandedAuto++;
                }
            }

            // The quota recomputed from the proof's own centroids and the staff's operation fix.
            int expected = 0;
            if (op != null)
            {
                var facts = new GroupFacts[groupBuffer.Count];
                var chosen = new bool[groupBuffer.Count];
                for (int i = 0; i < groupBuffer.Count; i++)
                {
                    GroundGroupReport g = groupBuffer[i];
                    int n = alive.TryGetValue(g.Id, out int count) ? count : 0;
                    float x = n > 0 ? cx[g.Id] / n : g.X, z = n > 0 ? cz[g.Id] / n : g.Z;
                    bool wave = g.FromDepot && (g.BornAgo < GroundTasking.WaveSeconds || g.OnEffort);
                    facts[i] = new GroupFacts(g.Id, x, z, n > 0 && g.Usable, g.OnEffort, wave);
                }
                expected = GroundTasking.Choose(facts, facts.Length, op.X, op.Z, op.Kind, chosen);
            }

            int assigned = 0, contactGroups = 0, withdrawGroups = 0, stalledGroups = 0, arrivedOrClosing = 0, landedGroups = 0;
            int waveGroups = 0, waveOnEffort = 0, leadStage = -1, largest = 0;
            float waveDelay = -1f, drift = -1f;
            foreach (GroundGroupReport g in groupBuffer)
            {
                int n = alive.TryGetValue(g.Id, out int count) ? count : 0;
                if (g.Stage == GroundStage.Contact) contactGroups++;
                if (g.Stage == GroundStage.Withdraw) withdrawGroups++;
                if (!g.Usable && g.Stage != GroundStage.Withdraw) stalledGroups++;
                // A wave is a group bought for the pick: one that left a depot after the first pick (the vehicles that came out before it belong to nobody's operation).
                if (g.FromDepot && pickFirstAt >= 0f && g.BornAgo < now - pickFirstAt)
                {
                    waveGroups++;
                    if (g.OnEffort) waveOnEffort++;
                    if (g.FirstOnEffortAfter >= 0f && (waveDelay < 0f || g.FirstOnEffortAfter < waveDelay)) waveDelay = g.FirstOnEffortAfter;
                }
                float centreX = n > 0 ? cx[g.Id] / n : g.X, centreZ = n > 0 ? cz[g.Id] / n : g.Z;
                if (memo.Target.TryGetValue(g.Id, out Vector3 before) && (int)before.y == (int)g.Stage && n > 0 && (g.Stage == GroundStage.Rally || g.Stage == GroundStage.Line) && g.OnEffort)
                    drift = Mathf.Max(drift, Scalar.Distance2D(before.x, before.z, g.TargetX, g.TargetZ));
                memo.Target[g.Id] = new Vector3(g.TargetX, (float)g.Stage, g.TargetZ);
                if (!g.OnEffort) continue;
                assigned++;
                if (g.Alive > largest) { largest = g.Alive; leadStage = (int)g.Stage; }
                float distance = Scalar.Distance2D(centreX, centreZ, g.TargetX, g.TargetZ);
                // Closing is judged over the group's own episode (same key and stage), across every read the scenario makes, not between two named samples.
                if (!memo.Episodes.TryGetValue(g.Id, out Episode episode) || episode.Key != g.Key || episode.Stage != g.Stage)
                    memo.Episodes[g.Id] = episode = new Episode { Key = g.Key, Stage = g.Stage, Distance = distance, At = now };
                if (distance <= 600f || (now - episode.At >= 30f && episode.Distance - distance >= 200f)) episode.Closed = true;
                if (episode.Closed) arrivedOrClosing++;
                if (answeredIn.TryGetValue(g.Id, out int asked) && asked > 0 && landedIn.TryGetValue(g.Id, out int landedCount) && landedCount * 2 >= asked) landedGroups++;
            }

            int lateSpawned = 0, lateEnrolled = 0;
            var enrolled = new HashSet<int>();
            foreach (GroundMemberReport m in memberBuffer) if (m.Vehicle != null) enrolled.Add(m.Vehicle.GetInstanceID());
            for (int i = 0; i < lateVehicles.Count; i++)
            {
                if (lateFactions[i] != key) continue;
                lateSpawned++;
                if (lateVehicles[i] != null && enrolled.Contains(lateVehicles[i].GetInstanceID())) lateEnrolled++;
            }
            memo.LandedPeak = Math.Max(memo.LandedPeak, landedGroups);
            memo.ClosingPeak = Math.Max(memo.ClosingPeak, arrivedOrClosing);
            memo.AssignedPeak = Math.Max(memo.AssignedPeak, assigned);
            // A group that is not on the effort is not judged for how it moves; the counts below are the effort's.
            int reportMoving = haveReport ? report.Moving : -1;
            return new Dictionary<string, object>
            {
                { "reason", GroundReasonRule.Token(front.ReasonFor(hq)) },
                { "eligible", front.EligibleCount(hq) }, { "members", memberBuffer.Count }, { "ungrouped", stats.Ungrouped }, { "groups", groupBuffer.Count },
                { "assigned", assigned }, { "assignedPeak", memo.AssignedPeak }, { "expected", expected }, { "quotaGap", Math.Max(0, expected - assigned) },
                { "steered", steered }, { "landedGroups", landedGroups }, { "landedGroupsPeak", memo.LandedPeak }, { "strayed", strayed }, { "arrivedOrClosing", arrivedOrClosing }, { "closingPeak", memo.ClosingPeak },
                { "onSlot", onSlot }, { "staged", stagedMembers }, { "frozen", frozen },
                { "stagedShare", AliveShare(alive, stagedMembers, false) }, { "frozenShare", AliveShare(alive, frozen, true) }, { "contactGroups", contactGroups }, { "withdrawGroups", withdrawGroups }, { "stalledGroups", stalledGroups },
                { "maxContactDwellS", stats.MaxContactDwell }, { "rekeysSincePick", stats.Rekeys }, { "effortSwaps", stats.EffortSwaps },
                { "traceCount", front.TraceCount(hq) },
                { "supportSeen", stats.SupportSeen }, { "supportNavigating", stats.SupportNavigating }, { "supportEnrolled", supportEnrolled },
                { "lateSpawned", lateSpawned }, { "lateEnrolled", lateEnrolled },
                { "waveGroups", waveGroups }, { "waveOnEffort", waveOnEffort }, { "waveDelayS", waveDelay },
                { "opActive", op != null ? 1 : 0 }, { "opKind", op != null ? op.Kind.ToString().ToUpperInvariant() : "" }, { "opLabel", op != null ? op.Name : "" },
                { "opElapsedS", op != null ? now - op.Started : -1f }, { "leadStage", leadStage }, { "etaS", haveReport ? report.EtaSeconds : -1 },
                { "reportMoving", reportMoving }, { "movingDiff", reportMoving < 0 ? -1 : Math.Abs(reportMoving - arrivedOrClosing) },
                { "slotDriftM", drift },
            };
        }

        /// <summary>The share of the live members that count, for the scenario's floor and ceiling; with no member at all, a value that fails both (a floor reads -1, a ceiling 2).</summary>
        private static float AliveShare(Dictionary<int, int> alive, int count, bool ceiling)
        {
            int total = 0;
            foreach (int n in alive.Values) total += n;
            return total > 0 ? (float)count / total : ceiling ? 2f : -1f;
        }

        /// <summary>A depot-route late vehicle is whatever new vehicle of that definition and faction appeared since the call.</summary>
        private static void DetectLate()
        {
            for (int p = pendingLate.Count - 1; p >= 0; p--)
            {
                LateOrder pending = pendingLate[p];
                foreach (Unit unit in UnitRegistry.allUnits)
                {
                    if (!(unit is GroundVehicle v) || !ReferenceEquals(v.NetworkHQ, pending.Hq) || pending.Known.Contains(v.GetInstanceID())) continue;
                    if (!ReferenceEquals(v.definition, pending.Definition)) continue;
                    pending.Known.Add(v.GetInstanceID());
                    lateVehicles.Add(v); lateFactions.Add(pending.Faction);
                }
                if (Time.timeSinceLevelLoad - pending.At > 90f) pendingLate.RemoveAt(p);
            }
        }

        private static bool TryFaction(string name, out FactionHQ hq)
        {
            hq = null;
            if (string.IsNullOrEmpty(name)) return GameAccess.TryGetLocalFaction(out hq) && hq != null && hq.faction != null;
            foreach (FactionHQ candidate in FactionRegistry.GetAllHQs())
                if (candidate != null && candidate.faction != null && string.Equals(candidate.faction.factionName, name, StringComparison.OrdinalIgnoreCase)) { hq = candidate; return true; }
            return false;
        }

        private static string Key(string faction) =>
            (faction ?? "none").ToLowerInvariant().Replace('.', '_').Replace(' ', '_');
    }
#endif
}

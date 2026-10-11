using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>Plain call counters for the two MissionPosition postfixes (<c>MissionPositionPriorityPatch</c>). Nothing in the
    /// game reads them; the sim hook below reports them. They change no behaviour, so they are compiled in every build.</summary>
    internal static class GroundProbe
    {
        /// <summary>Advance postfix: every call, the calls for a ground vehicle, the calls for an enrolled member, the calls that
        /// replaced vanilla's answer.</summary>
        internal static int AdvanceRan, AdvanceGround, AdvanceEnrolled, AdvanceReplaced;
        /// <summary>Delivery postfix: every call and the calls that replaced the sort key.</summary>
        internal static int DeliveryRan, DeliveryReplaced;
    }

#if !PUBLIC_RELEASE
    /// <summary>Read-only ground-war readback for the unattended STR proof (<c>tests/ingame/str-ground-*.json</c>): per faction the
    /// staff's war state, the enrolled ground groups with their lead vehicle, and the postfix counters. Nothing here assigns, enrols
    /// or clears (it never calls <c>TryGetGroundAssignment</c>, which writes on read). Dev tooling, compiled out of public releases
    /// and inert outside a sim run. Results are nested dictionaries without dots in key names, so a check path can reach any value;
    /// an unknown number is -1.</summary>
    public static class GroundProof
    {
        private sealed class Anchor { internal float X, Z, Distance; }

        private sealed class Tally
        {
            internal int Groups, Members, Staged, Assigned, Moving;
            internal float Closing;
        }

        // A check compares one stored value to a constant, so deltas live here. Per group: the staged destination first seen and the
        // lead's distance to it then ("closing" = how much nearer it is now; a destination that moves more than 150 m starts over).
        private static readonly Dictionary<int, Anchor> anchors = new Dictionary<int, Anchor>();
        // The highest value each reading ever had, so the last sample answers "did this ever happen".
        private static readonly Dictionary<string, float> peaks = new Dictionary<string, float>();

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return Failure("GroundProof", "Read", "requires a nomodkit scenario run");
            LivingFrontService living = LivingFrontService.Active;
            GroundFrontService front = GroundFrontService.Active;
            float now = Time.timeSinceLevelLoad;

            var war = new Dictionary<string, object>();
            var ops = new Dictionary<string, LivingFrontService.Operation>();
            if (living != null)
                foreach (KeyValuePair<string, LivingFrontService.FactionWar> pair in living.Wars)
                {
                    string name = Key(pair.Key);
                    LivingFrontService.FactionWar w = pair.Value;
                    LivingFrontService.Operation op = w.Active;
                    if (op != null) ops[name] = op;
                    war[name] = new Dictionary<string, object>
                    {
                        { "posture", w.Posture.ToString() },
                        { "opLive", op != null }, { "opKey", op != null ? op.Key : "" }, { "opKind", op != null ? op.Kind : "" },
                        { "opLabel", op != null ? op.Label : "" }, { "opPhase", op != null ? op.Phase : "" },
                        { "opX", op != null ? op.X : 0f }, { "opZ", op != null ? op.Z : 0f },
                        { "opAge", op != null ? now - op.Started : -1f },
                        { "proposals", w.Proposals.Count }, { "maxProposals", Peak(name + ".proposals", w.Proposals.Count) },
                        { "fronts", w.Fronts.Count },
                    };
                }
            war["total"] = new Dictionary<string, object>
            {
                { "operations", ops.Count }, { "maxOperations", Peak("total.operations", ops.Count) },
            };

            var tallies = new Dictionary<string, Tally> { { "total", new Tally() } };
            foreach (string name in war.Keys)
                if (!tallies.ContainsKey(name)) tallies[name] = new Tally();
            var groups = new Dictionary<string, object>();
            var leads = new Dictionary<string, object>();
            if (front != null)
                foreach (GroundFrontService.Group g in front.Groups)
                {
                    string faction = g.HQ != null && g.HQ.faction != null ? Key(g.HQ.faction.factionName) : "none";
                    ops.TryGetValue(faction, out LivingFrontService.Operation op);
                    GroundFrontService.Member head = g.Members.Count > 0 ? g.Members[0] : null;
                    GroundVehicle car = head != null ? head.Vehicle : null;
                    bool alive = car != null && !car.disabled;
                    bool staged = head != null && head.HasDestination;
                    GlobalPosition at = alive ? car.GlobalPosition() : default;
                    int stagedMembers = 0;
                    foreach (GroundFrontService.Member m in g.Members)
                        if (m.HasDestination) stagedMembers++;
                    bool onOp = op != null && g.Key == op.Key;
                    float speed = alive ? Mathf.Abs(car.speed) : -1f;
                    float toDest = alive && staged
                        ? Scalar.Distance2D(at.x, at.z, head.Destination.x, head.Destination.z) : -1f;
                    float closing = 0f;
                    if (toDest >= 0f)
                    {
                        anchors.TryGetValue(g.Id, out Anchor anchor);
                        if (anchor == null || Scalar.Distance2D(anchor.X, anchor.Z, head.Destination.x, head.Destination.z) > 150f)
                            anchors[g.Id] = anchor = new Anchor { X = head.Destination.x, Z = head.Destination.z, Distance = toDest };
                        closing = anchor.Distance - toDest;
                    }
                    if (alive) leads["lead_g" + g.Id] = car;

                    void Add(Tally t)
                    {
                        t.Groups++;
                        t.Members += g.Members.Count;
                        t.Staged += stagedMembers;
                        if (onOp) t.Assigned++;
                        if (speed >= 1f) t.Moving++;
                        t.Closing = Mathf.Max(t.Closing, closing);
                    }
                    Add(tallies["total"]);
                    if (!tallies.ContainsKey(faction)) tallies[faction] = new Tally();
                    Add(tallies[faction]);

                    GlobalPosition nav = alive ? car.GetDestination() : default;
                    groups["g" + g.Id] = new Dictionary<string, object>
                    {
                        { "faction", faction }, { "members", g.Members.Count }, { "formed", g.Formed },
                        { "stage", g.Stage.ToString() }, { "key", g.Key ?? "" }, { "offensive", g.Offensive },
                        { "onOp", onOp }, { "staged", stagedMembers },
                        { "leadAlive", alive }, { "leadX", at.x }, { "leadZ", at.z }, { "speed", speed },
                        { "destX", staged ? head.Destination.x : 0f }, { "destZ", staged ? head.Destination.z : 0f },
                        { "distDest", toDest },
                        { "distFix", alive && op != null ? Scalar.Distance2D(at.x, at.z, op.X, op.Z) : -1f },
                        // Where vanilla is actually steering the lead, against where the postfix staged it.
                        { "navDist", toDest >= 0f ? Scalar.Distance2D(nav.x, nav.z, head.Destination.x, head.Destination.z) : -1f },
                        { "closing", closing },
                    };
                }

            var ground = new Dictionary<string, object>
            {
                { "present", front != null },
                { "tacticsEnabled", front != null && front.TacticsEnabled },
                { "authoritative", living != null && living.Authoritative },
                { "steering", front != null && front.Steering },
                { "groupList", groups },
            };
            foreach (KeyValuePair<string, Tally> pair in tallies)
                ground[pair.Key] = new Dictionary<string, object>
                {
                    { "groups", pair.Value.Groups }, { "members", pair.Value.Members }, { "staged", pair.Value.Staged },
                    { "assigned", pair.Value.Assigned }, { "moving", pair.Value.Moving },
                    { "maxGroups", Peak(pair.Key + ".groups", pair.Value.Groups) },
                    { "maxStaged", Peak(pair.Key + ".staged", pair.Value.Staged) },
                    { "maxAssigned", Peak(pair.Key + ".assigned", pair.Value.Assigned) },
                    { "maxMoving", Peak(pair.Key + ".moving", pair.Value.Moving) },
                    { "bestClosing", Peak(pair.Key + ".closing", pair.Value.Closing) },
                };

            Tally all = tallies["total"];
            Plugin.Logger?.LogInfo("[GroundProof] t=" + now.ToString("0") + " ops=" + ops.Count + " groups=" + all.Groups +
                " members=" + all.Members + " staged=" + all.Staged + " assigned=" + all.Assigned + " moving=" + all.Moving +
                " closing=" + all.Closing.ToString("0") + " advance ran/ground/enrolled/replaced=" + GroundProbe.AdvanceRan + "/" +
                GroundProbe.AdvanceGround + "/" + GroundProbe.AdvanceEnrolled + "/" + GroundProbe.AdvanceReplaced +
                " delivery ran/replaced=" + GroundProbe.DeliveryRan + "/" + GroundProbe.DeliveryReplaced);
            return new Dictionary<string, object>
            {
                { "ok", true }, { "t", now },
                {
                    "counters", new Dictionary<string, object>
                    {
                        { "advanceRan", GroundProbe.AdvanceRan }, { "advanceGround", GroundProbe.AdvanceGround },
                        { "advanceEnrolled", GroundProbe.AdvanceEnrolled }, { "advanceReplaced", GroundProbe.AdvanceReplaced },
                        { "deliveryRan", GroundProbe.DeliveryRan }, { "deliveryReplaced", GroundProbe.DeliveryReplaced },
                    }
                },
                { "war", war }, { "ground", ground },
                // The harness registers these lead vehicles, so a scenario's record op writes their tracks to units.csv.
                { "ids", leads },
            };
        }

        private static string Key(string faction) =>
            (faction ?? "none").ToLowerInvariant().Replace('.', '_').Replace(' ', '_');

        private static float Peak(string key, float value)
        {
            peaks.TryGetValue(key, out float best);
            if (value > best) peaks[key] = best = value;
            return best;
        }
    }
#endif
}

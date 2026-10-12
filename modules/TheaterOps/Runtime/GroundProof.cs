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
        /// replaced vanilla's answer with a front destination, the calls that replaced it with the old faction-directive fallback.</summary>
        internal static int AdvanceRan, AdvanceGround, AdvanceEnrolled, AdvanceReplaced, AdvanceFallback;
        /// <summary>Delivery postfix: every call and the calls that replaced the sort key.</summary>
        internal static int DeliveryRan, DeliveryReplaced;
    }

#if !PUBLIC_RELEASE
    /// <summary>Read-only ground-war readback for the unattended STR proof (<c>tests/ingame/str-ground-*.json</c>): per faction the
    /// staff's war state, the enrolled ground groups and vehicles, and the postfix counters. Nothing here assigns, enrols
    /// or clears (it never calls <c>TryGetGroundAssignment</c>, which writes on read). Dev tooling, compiled out of public releases
    /// and inert outside a sim run. Results are nested dictionaries without dots in key names, so a check path can reach any value;
    /// an unknown number is -1.</summary>
    public static class GroundProof
    {
        // Per enrolled vehicle (not per group: a group's lead changes when it dies or goes on hold). Where it was first seen, and the
        // staged destination it is measured against with its distance then ("closing" = how much nearer it is now; a destination
        // that moves more than 150 m starts over). Entries of dead vehicles stay; a run is minutes long.
        private sealed class Track { internal float StartX, StartZ, DestX, DestZ, DestDistance = -1f; }

        private sealed class Tally
        {
            internal int Groups, Members, Staged, Assigned, Attack, Moving, Landed;
            internal float Closing, Moved;
        }

        private static readonly Dictionary<GroundVehicle, Track> tracks = new Dictionary<GroundVehicle, Track>();
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
                        { "opLive", op != null }, { "opKey", op != null ? op.Key : "" }, { "opKind", op != null ? op.Kind.ToString().ToUpperInvariant() : "" },
                        { "opLabel", op != null ? op.Name : "" }, { "opPhase", op != null ? op.Phase.ToString().ToUpperInvariant() : "" },
                        { "opX", op != null ? op.X : 0f }, { "opZ", op != null ? op.Z : 0f },
                        { "opAge", op != null ? now - op.Started : -1f },
                        { "proposals", w.Deck.Count }, { "maxProposals", Peak(name + ".proposals", w.Deck.Count) },
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
            var vehicles = new Dictionary<string, object>();
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
                    bool onOp = op != null && g.Key == op.Key;
                    float speed = alive ? Mathf.Abs(car.speed) : -1f;
                    float toDest = alive && staged
                        ? Scalar.Distance2D(at.x, at.z, head.Destination.x, head.Destination.z) : -1f;

                    // Every live member is measured on its own: staged, moving, landed (vanilla steers it where the postfix staged it),
                    // closing on its destination, and net distance covered since first seen.
                    int stagedMembers = 0, moving = 0, landed = 0;
                    float closing = 0f, moved = 0f;
                    foreach (GroundFrontService.Member m in g.Members)
                    {
                        if (m.HasDestination) stagedMembers++;
                        GroundVehicle v = m.Vehicle;
                        if (v == null || v.disabled) continue;
                        GlobalPosition p = v.GlobalPosition();
                        if (Mathf.Abs(v.speed) >= 1f) moving++;
                        tracks.TryGetValue(v, out Track track);
                        if (track == null) tracks[v] = track = new Track { StartX = p.x, StartZ = p.z };
                        moved = Mathf.Max(moved, Scalar.Distance2D(track.StartX, track.StartZ, p.x, p.z));
                        vehicles["veh_g" + g.Id + "_" + v.GetInstanceID()] = v;
                        if (!m.HasDestination) continue;
                        float dist = Scalar.Distance2D(p.x, p.z, m.Destination.x, m.Destination.z);
                        if (track.DestDistance < 0f || Scalar.Distance2D(track.DestX, track.DestZ, m.Destination.x, m.Destination.z) > 150f)
                        {
                            track.DestX = m.Destination.x; track.DestZ = m.Destination.z; track.DestDistance = dist;
                        }
                        closing = Mathf.Max(closing, track.DestDistance - dist);
                        GlobalPosition nav = v.GetDestination();
                        if (Scalar.Distance2D(nav.x, nav.z, m.Destination.x, m.Destination.z) <= Mathf.Max(300f, .2f * dist)) landed++;
                    }

                    void Add(Tally t)
                    {
                        t.Groups++;
                        t.Members += g.Members.Count;
                        t.Staged += stagedMembers;
                        if (onOp) t.Assigned++;
                        if (onOp && g.Offensive) t.Attack++;
                        t.Moving += moving;
                        t.Landed += landed;
                        t.Closing = Mathf.Max(t.Closing, closing);
                        t.Moved = Mathf.Max(t.Moved, moved);
                    }
                    Add(tallies["total"]);
                    if (!tallies.ContainsKey(faction)) tallies[faction] = new Tally();
                    Add(tallies[faction]);

                    GlobalPosition leadNav = alive ? car.GetDestination() : default;
                    groups["g" + g.Id] = new Dictionary<string, object>
                    {
                        { "faction", faction }, { "members", g.Members.Count }, { "formed", g.Formed },
                        { "stage", g.Stage.ToString() }, { "key", g.Key ?? "" }, { "offensive", g.Offensive },
                        { "onOp", onOp }, { "staged", stagedMembers }, { "moving", moving }, { "landed", landed },
                        // The lead (members[0]) in detail; closing and moved are the best of any member.
                        { "leadAlive", alive }, { "leadX", at.x }, { "leadZ", at.z }, { "speed", speed },
                        { "destX", staged ? head.Destination.x : 0f }, { "destZ", staged ? head.Destination.z : 0f },
                        { "distDest", toDest },
                        { "distFix", alive && op != null ? Scalar.Distance2D(at.x, at.z, op.X, op.Z) : -1f },
                        // Where vanilla is actually steering the lead, against where the postfix staged it.
                        { "navDist", toDest >= 0f ? Scalar.Distance2D(leadNav.x, leadNav.z, head.Destination.x, head.Destination.z) : -1f },
                        { "closing", closing }, { "moved", moved },
                    };
                }

            var ground = new Dictionary<string, object>
            {
                { "present", front != null },
                { "tacticsEnabled", front != null && front.TacticsEnabled },
                { "authoritative", living != null && living.Authoritative },
                { "steering", front != null && front.SteeringAvailable },
                { "groupList", groups },
            };
            foreach (KeyValuePair<string, Tally> pair in tallies)
                ground[pair.Key] = new Dictionary<string, object>
                {
                    { "groups", pair.Value.Groups }, { "members", pair.Value.Members }, { "staged", pair.Value.Staged },
                    { "assigned", pair.Value.Assigned }, { "attack", pair.Value.Attack },
                    { "moving", pair.Value.Moving }, { "landed", pair.Value.Landed },
                    { "maxGroups", Peak(pair.Key + ".groups", pair.Value.Groups) },
                    { "maxMembers", Peak(pair.Key + ".members", pair.Value.Members) },
                    { "maxStaged", Peak(pair.Key + ".staged", pair.Value.Staged) },
                    { "maxAssigned", Peak(pair.Key + ".assigned", pair.Value.Assigned) },
                    { "maxAttack", Peak(pair.Key + ".attack", pair.Value.Attack) },
                    { "maxMoving", Peak(pair.Key + ".moving", pair.Value.Moving) },
                    { "maxLanded", Peak(pair.Key + ".landed", pair.Value.Landed) },
                    { "bestClosing", Peak(pair.Key + ".closing", pair.Value.Closing) },
                    { "bestMoved", Peak(pair.Key + ".moved", pair.Value.Moved) },
                };

            // The seated player only exists to own the local war view; its death is reported, never asserted.
            Aircraft seat = GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player) ? player.Aircraft : null;
            GlobalPosition seatAt = seat != null ? seat.GlobalPosition() : default;
            bool seatAlive = seat != null && !seat.disabled;

            Tally all = tallies["total"];
            peaks.TryGetValue("total.members", out float peakMembers);
            Plugin.Logger?.LogInfo("[GroundProof] t=" + now.ToString("0") + " ops=" + ops.Count + " groups=" + all.Groups +
                " members=" + all.Members + " staged=" + all.Staged + " assigned=" + all.Assigned + " moving=" + all.Moving +
                " landed=" + all.Landed + " closing=" + all.Closing.ToString("0") + " moved=" + all.Moved.ToString("0") +
                " advance ran/ground/enrolled/replaced/fallback=" + GroundProbe.AdvanceRan + "/" +
                GroundProbe.AdvanceGround + "/" + GroundProbe.AdvanceEnrolled + "/" + GroundProbe.AdvanceReplaced + "/" +
                GroundProbe.AdvanceFallback + " delivery ran/replaced=" + GroundProbe.DeliveryRan + "/" + GroundProbe.DeliveryReplaced +
                " player=" + (seatAlive ? "alive y=" + seatAt.y.ToString("0") : "gone"));
            return new Dictionary<string, object>
            {
                { "ok", true }, { "t", now },
                {
                    "counters", new Dictionary<string, object>
                    {
                        { "advanceRan", GroundProbe.AdvanceRan }, { "advanceGround", GroundProbe.AdvanceGround },
                        { "advanceEnrolled", GroundProbe.AdvanceEnrolled }, { "advanceReplaced", GroundProbe.AdvanceReplaced },
                        { "advanceFallback", GroundProbe.AdvanceFallback },
                        // Polls of enrolled vehicles per vehicle (vanilla asks every 4 s per vehicle): a smoke floor of 3 means "polled
                        // repeatedly", where a bare count of 1 only says the patch is applied.
                        { "enrolledPerMember", GroundProbe.AdvanceEnrolled / Mathf.Max(1f, peakMembers) },
                        { "deliveryRan", GroundProbe.DeliveryRan }, { "deliveryReplaced", GroundProbe.DeliveryReplaced },
                    }
                },
                { "war", war }, { "ground", ground },
                {
                    "player", new Dictionary<string, object>
                    {
                        { "alive", seatAlive }, { "x", seatAt.x }, { "y", seatAt.y }, { "z", seatAt.z },
                    }
                },
                // The harness registers these vehicles, so a scenario's record op writes their tracks to units.csv.
                { "ids", vehicles },
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

#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Presentation;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>
    /// Levers and host-truth readback for the wing AI scenarios (<c>tests/ingame/wing-ai-*.json</c>): air-start a wing on a scenario
    /// aircraft, give it the player's orders (WingOrders.Run, the production path), run a plan, read what each member does. Compiled out
    /// of public releases; refuses outside a sim run.
    /// </summary>
    public static class WingAiProof
    {
        private static bool InSimRun => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));

        /// <summary>{lead, count, shape?}: the wing forms on <c>lead</c> and <c>count</c> wingmen air-start round it.</summary>
        public static Dictionary<string, object> Call(Dictionary<string, object> args)
        {
            WingService w = WingService.Instance;
            if (!InSimRun) return Failure("WingAiProof", "Call", "requires a nomodkit scenario run");
            if (w?.Selection == null || SpawnService.Instance == null) return Failure("WingAiProof", "Call", "the wing is not active");
            if (!(Arg(args, "leadUnit") is Aircraft lead) || lead.disabled) return Failure("WingAiProof", "Call", "'lead' does not name a live aircraft");
            w.SetAnchor(lead);
            string shape = Text(args, "shape");
            if (shape != null && !w.SetShape(shape)) return Failure("WingAiProof", "Call", $"no formation '{shape}'");
            int n = SpawnService.Instance.Call((int)Number(args, "count", 2), null);
            return new Dictionary<string, object> { { "ok", n > 0 }, { "spawned", n } };
        }

        /// <summary>{verb: move|engage|rtb|formup|shape, numbers?: [wingman numbers], element?, x?, z?, alt?, shape?, confirm?}: one player
        /// order; <c>confirm</c> presses ENGAGE a second time when the first is refused as outnumbered.</summary>
        public static Dictionary<string, object> Order(Dictionary<string, object> args)
        {
            WingService w = WingService.Instance;
            if (!InSimRun) return Failure("WingAiProof", "Order", "requires a nomodkit scenario run");
            if (w?.Selection == null) return Failure("WingAiProof", "Order", "the wing is not active");
            WingScope scope = WingScope.Wing;
            if (Arg(args, "numbers") is List<object> numbers)
            {
                var ids = new List<uint>();
                foreach (object o in numbers)
                {
                    int number = Convert.ToInt32(o, System.Globalization.CultureInfo.InvariantCulture);
                    WingMember m = w.Members.Find(x => x.Number == number && !x.Released && (object)x.Aircraft != null);
                    if (m == null) return Failure("WingAiProof", "Order", $"no wingman #{number}");
                    ids.Add(m.Aircraft.persistentID.Id);
                }
                scope = WingScope.OfMembers(ids.ToArray());
            }
            else if (Has(args, "element")) scope = WingScope.OfElement((int)Number(args, "element", 0));
            string verb = Text(args, "verb") ?? "";
            WingOrder order;
            switch (verb)
            {
                case "move":
                    var at = Waypoint.At(Number(args, "x", 0f), Number(args, "z", 0f));
                    if (Has(args, "alt")) at.Altitude = Number(args, "alt", 0f);
                    order = WingOrder.Tasked(WingTask.Move(at), scope);
                    break;
                case "engage": order = WingOrder.Of(OrderKind.Engage, scope); break;
                case "rtb": order = WingOrder.Of(OrderKind.Rtb, scope); break;
                case "formup": order = WingOrder.Of(OrderKind.FormUp, scope); break;
                case "shape": order = new WingOrder { Kind = OrderKind.SetShape, Scope = scope, Text = Text(args, "shape") }; break;
                default: return Failure("WingAiProof", "Order", $"unknown verb '{verb}'");
            }
            OrderResult r = WingOrders.Run(order);
            bool confirmed = false;
            if (!r.Accepted && verb == "engage" && Bool(args, "confirm") && (r.Reason ?? "").StartsWith("Outnumbered", StringComparison.Ordinal))
            {
                r = WingOrders.Run(WingOrder.Of(OrderKind.Engage, scope));
                confirmed = true;
            }
            string text = r.Accepted ? r.Ack : r.Reason;
            WingLog.Logger.LogInfo($"[WingAiProof] {verb}: {(r.Accepted ? "acked" : "refused")} '{text}' element {r.Element}");
            return new Dictionary<string, object>
            {
                { "ok", true }, { "accepted", r.Accepted ? 1 : 0 }, { "text", text ?? "" }, { "element", r.Element }, { "confirmed", confirmed ? 1 : 0 },
            };
        }

        /// <summary>{steps: [{lane, kind, x?, z?, radius?, end?, seconds?}]}: loads and executes a plan (kind and end by PlanKind / PlanEnd name).</summary>
        public static Dictionary<string, object> Plan(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("WingAiProof", "Plan", "requires a nomodkit scenario run");
            if (WingPlans.Instance == null) return Failure("WingAiProof", "Plan", "the wing is not active");
            var plan = new WingPlan { Name = Text(args, "name") ?? "PROOF" };
            if (Arg(args, "steps") is List<object> steps)
                foreach (object o in steps)
                {
                    if (!(o is Dictionary<string, object> s)) continue;
                    var step = new PlanStep
                    {
                        Kind = (PlanKind)Enum.Parse(typeof(PlanKind), Text(s, "kind"), true),
                        End = (PlanEnd)Enum.Parse(typeof(PlanEnd), Text(s, "end") ?? "Arrive", true),
                        EndSeconds = Number(s, "seconds", 0f),
                        Radius = Number(s, "radius", 0f),
                    };
                    if (Has(s, "x")) step.Points = new[] { Waypoint.At(Number(s, "x", 0f), Number(s, "z", 0f)) };
                    plan.Add((int)Number(s, "lane", 0), step);
                }
            WingPlans.Instance.Load(plan);
            List<string> errors = WingPlans.Instance.Execute();
            return new Dictionary<string, object> { { "ok", errors == null }, { "errors", errors == null ? "" : string.Join("; ", errors) } };
        }

        /// <summary>{bingoReserve?, planTick?}: the bingo reserve fraction (the tanks kept beyond the flight home) and the plan runner's
        /// tick (a slow tick lets the element's planner fail its task before the plan sees the end rule: the race CAP-until-BINGO lost).</summary>
        public static Dictionary<string, object> Tune(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("WingAiProof", "Tune", "requires a nomodkit scenario run");
            float before = BingoMonitor.ReserveFraction;
            BingoMonitor.ReserveFraction = Number(args, "bingoReserve", before);
            WingPlans.TickSeconds = Number(args, "planTick", WingPlans.TickSeconds);
            return new Dictionary<string, object>
            {
                { "ok", true }, { "before", before }, { "after", BingoMonitor.ReserveFraction }, { "planTick", WingPlans.TickSeconds },
            };
        }

        private static readonly Dictionary<string, object> takenBack = new Dictionary<string, object>();
        private static long seen;
        private static WingEventRing seenRing;

        /// <summary>Every member (element, engaged, recovery, mind, fuel, place round the lead, its slot's lateral), the take-backs by reason
        /// since the first read, and the plan's steps.</summary>
        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            WingService w = WingService.Instance;
            if (!InSimRun) return Failure("WingAiProof", "Read", "requires a nomodkit scenario run");
            if (w == null) return Failure("WingAiProof", "Read", "the wing is not active");
            CountTakeBacks(w);
            Aircraft lead = w.Leader;
            Vec3 lp = lead != null ? lead.GlobalPosition().ToVec3() : default;
            Vec3 lv = lead != null ? lead.CockpitRB().velocity.ToVec3() : default;
            float fl = (float)Math.Sqrt(lv.X * lv.X + lv.Z * lv.Z);
            float fx = fl > 1f ? lv.X / fl : 0f, fz = fl > 1f ? lv.Z / fl : 1f;
            var members = new Dictionary<string, object>();
            int engaged = 0, recovering = 0, bingo = 0;
            float engagedFarKm = 0f;
            foreach (WingMember m in w.Members)
            {
                if (m.Released || (object)m.Aircraft == null) continue;
                Vec3 p = m.Aircraft.GlobalPosition().ToVec3();
                float dx = p.X - lp.X, dz = p.Z - lp.Z;
                WingFrame frame = w.FrameOf(m);
                bool slotted = frame != null && m.Brain.Slot >= 0 && m.Brain.Slot < frame.Count;
                if (m.Engaged) engagedFarKm = Math.Max(engagedFarKm, (float)Math.Sqrt(dx * dx + dz * dz) / 1000f);
                if (m.Engaged) engaged++;
                if (m.Recovery != null || m.HasPendingRecovery) recovering++;
                if (m.Bingo.Bingo) bingo++;
                members["w" + m.Number] = new Dictionary<string, object>
                {
                    { "number", m.Number }, { "element", w.ElementOf(m) }, { "alive", m.Alive ? 1 : 0 }, { "engaged", m.Engaged ? 1 : 0 },
                    { "state", m.Pilot?.currentState == null ? "none" : ReferenceEquals(m.Pilot.currentState, m.State) ? "ours" : m.Pilot.currentState.GetType().Name },
                    { "recovery", m.Recovery != null ? m.Recovery.Phase.ToString() : m.HasPendingRecovery ? "pending" : "" },
                    { "bingo", m.Bingo.Bingo ? 1 : 0 }, { "fuel", m.Aircraft.GetFuelLevel() },
                    { "mind", m.Brain.Mind.Current.ToString() },
                    { "leadKm", (float)Math.Sqrt(dx * dx + dz * dz) / 1000f },
                    { "right", dx * fz - dz * fx }, { "ahead", dx * fx + dz * fz },
                    { "slotLateral", slotted ? frame.Slots[m.Brain.Slot].Lateral : float.NaN },
                    { "shape", frame?.Definition?.Id ?? "" },
                };
            }
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "t", w.MissionTime }, { "count", members.Count }, { "engaged", engaged }, { "recovering", recovering },
                { "bingo", bingo }, { "engagedFarKm", engagedFarKm },
                { "leash", takenBack.TryGetValue(nameof(TransitionReason.Leash), out object leash) ? leash : 0 },
                { "takenBack", new Dictionary<string, object>(takenBack) }, { "members", members },
            };
            WingPlans plans = WingPlans.Instance;
            if (plans?.Runner != null)
            {
                var stepsOut = new Dictionary<string, object>();
                for (int l = 0; l < WingPlan.Lanes; l++)
                    for (int s = 0; s < plans.Plan.Steps[l].Count; s++)
                        stepsOut[PlanRules.Name(l, s)] = new Dictionary<string, object>
                        {
                            { "name", PlanRules.Name(l, s) }, { "kind", plans.Plan.Steps[l][s].Kind.ToString() },
                            { "state", plans.Runner.State(l, s).ToString() }, { "started", plans.Runner.StartedAt(l, s) },
                            { "ended", plans.Runner.EndedAt(l, s) }, { "why", plans.Runner.Why(l) ?? "" },
                        };
                result["plan"] = new Dictionary<string, object>
                {
                    { "running", plans.Running ? 1 : 0 }, { "completed", plans.Completed ? 1 : 0 }, { "sent", plans.Sent }, { "steps", stepsOut },
                };
            }
            WingLog.Logger.LogInfo($"[WingAiProof] Read t={w.MissionTime:0}: {members.Count} members, {engaged} engaged, {recovering} recovering, " +
                                   $"take-backs {Describe(takenBack)}");
            return result;
        }

        /// <summary>Disengaged events by reason, from where the last read stopped (the ring holds the last 256 events).</summary>
        private static void CountTakeBacks(WingService w)
        {
            WingEventRing ring = w.Events;
            if (!ReferenceEquals(ring, seenRing) || ring.Total < seen)
            {
                seenRing = ring;
                seen = 0;
                takenBack.Clear();
            }
            long fresh = Math.Min(ring.Total - seen, ring.Count);
            for (int i = ring.Count - (int)fresh; i < ring.Count; i++)
            {
                WingEvent e = ring[i];
                if (e.Kind != WingEventKind.Disengaged) continue;
                string key = e.Reason.ToString();
                takenBack[key] = (takenBack.TryGetValue(key, out object c) ? (int)c : 0) + 1;
            }
            seen = ring.Total;
        }
    }
}
#endif

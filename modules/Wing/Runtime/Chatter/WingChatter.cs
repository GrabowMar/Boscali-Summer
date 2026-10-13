using System;
using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Modules.Wing.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>
    /// The wingman net's publisher. It turns what the wing does into events on the radio (<see cref="IChatter"/>): the wing's event ring, launches, kills, new contacts,
    /// the answer to "bogey dope", and, when it has been quiet for a while, a conversation between pilots who are free to talk. Each wingman speaks as their pilot's
    /// persona and dialogue tag; the words are the packs' (docs/CHATTER_PACKS.md). It also owns the conditions only the wing knows: aftercombat, loss, recovery.
    /// </summary>
    internal sealed class WingChatter : IWingService
    {
        private const float QuietAfterUrgent = 25f, AfterCombatSeconds = 180f, LossSeconds = 300f;

        public static WingChatter Instance { get; private set; }

        public string Name => "Radio";
        /// <summary>What went on air, stamped on the wing's mission clock (the WMC log merges it with the wing's events).</summary>
        public ChatterLog Log { get; } = new ChatterLog();
        public ContactWatch Contacts { get; } = new ContactWatch();

        private readonly Dictionary<int, ChatterSpeaker> speakers = new Dictionary<int, ChatterSpeaker>();
        private readonly List<ChatterSpeaker> free = new List<ChatterSpeaker>();
        private readonly ContactSample[] samples = new ContactSample[ContactWatch.Capacity];
        private readonly Unit[] units = new Unit[ContactWatch.Capacity];
        private readonly Vec3[] tracked = new Vec3[ContactWatch.Capacity];
        private readonly int[] report = new int[4];
        private static readonly int[] NoReport = new int[0];

        private IChatter chatter;
        private ChatterDirector listening;
        private WingEventRing ring;
        private EventCursor cursor;
        private bool active;
        private float contactClock, nextExchange, lossAt = float.NegativeInfinity, combatAt = float.NegativeInfinity;
        private int asks;
        // The one contact call waiting for the channel: the next is chosen only once it is on air.
        private string pendingKey, pendingEvent;
        private uint pendingId;

        public WingChatter()
        {
            Instance = this;
        }

        public void Activate()
        {
            active = true;
            WingPilotRoster.Killed -= OnKill;
            WingPilotRoster.Killed += OnKill;
            speakers.Clear();
            Log.Clear();
            Contacts.Clear();
            pendingKey = null;
            ring = WingService.Instance?.Events;
            cursor.Seen = ring?.Total ?? 0;
            contactClock = 0f;
            lossAt = combatAt = float.NegativeInfinity;
            nextExchange = Time.time + UnityEngine.Random.Range(55f, 85f);
        }

        public void Deactivate()
        {
            active = false;
            WingPilotRoster.Killed -= OnKill;
            Listen(null);
            chatter?.Director.CancelExchange();
            speakers.Clear();
            ring = null;
        }

        public void FixedTick(float dt) { }

        public void Tick(float dt)
        {
            WingService wing = WingService.Instance;
            if (wing == null || !Radio()) return;
            if (!ReferenceEquals(ring, wing.Events))
            {
                ring = wing.Events;
                cursor.Seen = 0;
            }
            float now = chatter.Now;
            while (cursor.Next(ring, out WingEvent e)) Publish(wing, e, now);
            if ((contactClock += dt) >= 1f)
            {
                contactClock = 0f;
                CallContacts(wing);
            }
            TickTalk(wing, now);
        }

        /// <summary>The radio service, looked up late (the Chatter module may be missing: then the wing is simply silent).</summary>
        private bool Radio()
        {
            if (chatter == null && !ModuleServices.TryGet(out chatter)) return false;
            Listen(chatter.Director);
            return true;
        }

        private void Listen(ChatterDirector director)
        {
            if (ReferenceEquals(listening, director)) return;
            if (listening != null) listening.Aired -= OnAired;
            listening = director;
            if (listening != null) listening.Aired += OnAired;
        }

        private void OnAired(ChatterAired line)
        {
            line.Time = WingService.Instance?.MissionTime ?? 0f;
            Log.Push(line);
            // The contact call that was waiting is on air: the next one may be chosen.
            if (pendingKey != null && line.Event == pendingEvent) pendingKey = null;
        }

        // ------------------------------------------------------------------ speakers

        private ChatterSpeaker SpeakerFor(WingMember m)
        {
            if (m == null) return null;
            if (!speakers.TryGetValue(m.Id, out ChatterSpeaker s))
            {
                WingMember member = m;
                speakers[m.Id] = s = new ChatterSpeaker("wing:" + m.Id.ToString(CultureInfo.InvariantCulture), ChatterNet.Wing, "#" + m.Number)
                {
                    CanTalk = () => active && member.Alive && !member.Released,
                };
            }
            WingPilot pilot = WingPilotRoster.Of(m);
            s.Callsign = pilot != null && !string.IsNullOrEmpty(pilot.Callsign) ? pilot.Callsign : "#" + m.Number.ToString(CultureInfo.InvariantCulture);
            s.Persona = ChatterPersonas.Key(pilot != null ? pilot.Persona : ChatterPersona.Veteran);
            string tag = pilot == null ? null : string.IsNullOrWhiteSpace(pilot.DialogueTag) ? pilot.Callsign : pilot.DialogueTag;
            s.Character = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim().ToLowerInvariant();
            return s;
        }

        /// <summary>The member in the event's seat; for a wing-level event (or one whose member has gone), the first member.</summary>
        private static WingMember MemberOf(WingService wing, int seat)
        {
            WingMember first = null;
            foreach (WingMember m in wing.Members)
            {
                if (m.Released || !m.Alive) continue;
                if (m.Seat == seat) return m;
                if (first == null) first = m;
            }
            return first;
        }

        private bool Raise(WingMember m, string evt, string key = null, params (string, string)[] facts)
        {
            ChatterSpeaker s = SpeakerFor(m);
            if (s == null || chatter == null) return false;
            return chatter.Raise(evt, s, key ?? (WingCues.WingWide(evt) ? evt : null), facts);
        }

        // ------------------------------------------------------------------ the wing's events

        private void Publish(WingService wing, in WingEvent e, float now)
        {
            if (e.Kind == WingEventKind.MemberLost) lossAt = now;
            string evt = EventOf(e);
            if (evt == null) return;
            WingMember speaker = MemberOf(wing, e.Member);
            if (speaker == null) return;
            if (evt == WingCues.Joker)
                Raise(speaker, evt, null, ("n", Math.Max(1, (int)Math.Ceiling(speaker.Bingo.SecondsToBingo / 60f)).ToString(CultureInfo.InvariantCulture)));
            // A loss is told by a survivor about the seat that was lost.
            else if (evt == WingCues.AirframeLost || evt == WingCues.Ejected)
                Raise(speaker, evt, evt + ":" + e.Member, ("n", (e.Member + 2).ToString(CultureInfo.InvariantCulture)));
            else if (evt == WingCues.Maneuvering) Raise(speaker, evt, null, ("detail", ManeuverWords(speaker.Brain.Reaction.Kind)));
            else Raise(speaker, evt);
            // The tower answers a wingman who rolls or comes home.
            if (evt == WingCues.Rolling || evt == WingCues.Down) TowerCall(speaker, evt == WingCues.Rolling ? "tower.wingrolling" : "tower.wingdown");
        }

        private void TowerCall(WingMember m, string evt)
        {
            if (chatter == null || m.Aircraft == null) return;
            ChatterSpeaker tower = chatter.NearTower(m.Aircraft.transform.position);
            if (tower != null) chatter.Raise(evt, tower, evt + ":" + m.Id.ToString(CultureInfo.InvariantCulture), ("who", SpeakerFor(m).Callsign));
        }

        private static string EventOf(in WingEvent e)
        {
            switch (e.Kind)
            {
                case WingEventKind.BehaviourChanged:
                    // A maneuver speaks when it starts and when it is flown out.
                    if (e.To == BehaviourId.React) return WingCues.Maneuvering;
                    if (e.From == BehaviourId.React && e.Reason == TransitionReason.ManeuverDone) return WingCues.ManeuverDone;
                    if (e.Reason == TransitionReason.MissileInbound) return WingCues.Panic;
                    if (e.Reason == TransitionReason.MissileClear) return WingCues.DefensiveClear;
                    return null;
                case WingEventKind.Engaged: return WingCues.Engaging;
                case WingEventKind.Disengaged:
                    switch (e.Reason)
                    {
                        // Out of weapons: the line says what the member does next (rejoin, or go home).
                        case TransitionReason.Winchester:
                            return WingSettings.Instance.AfterWinchester.Value == WinchesterAction.Rejoin ? WingCues.OutOfAmmo : WingCues.Winchester;
                        case TransitionReason.Outnumbered: return WingCues.FallingBack;
                        case TransitionReason.Leash:
                        case TransitionReason.NoTarget:
                        case TransitionReason.Commanded: return WingCues.Rejoining;
                        default: return null;
                    }
                case WingEventKind.Joker: return WingCues.Joker;
                case WingEventKind.Bingo: return WingCues.Bingo;
                case WingEventKind.FallingBehind: return WingCues.FallingBehind;
                case WingEventKind.GcasActivated: return WingCues.PullUp;
                case WingEventKind.CollisionEmergency: return WingCues.BreakOff;
                // Pushed only when an escortee is gone; the wing then forms on the player.
                case WingEventKind.AnchorLost: return WingCues.EscortLost;
                case WingEventKind.Taxiing: return WingCues.Taxiing;
                case WingEventKind.HoldingShort: return WingCues.HoldingShort;
                case WingEventKind.LiningUp: return WingCues.LiningUp;
                case WingEventKind.Rolling: return WingCues.Rolling;
                case WingEventKind.Airborne: return WingCues.Airborne;
                case WingEventKind.Landed: return WingCues.Down;
                case WingEventKind.LandingFailed: return WingCues.GoAround;
                case WingEventKind.TaskStarted: return WingCues.Copy;
                case WingEventKind.TaskCompleted: return WingCues.TaskDone;
                case WingEventKind.TaskFailed: return WingCues.Unable;
                case WingEventKind.MemberLost:
                    return e.Reason == TransitionReason.Killed ? WingCues.AirframeLost : e.Reason == TransitionReason.Ejected ? WingCues.Ejected : null;
                case WingEventKind.Damaged: return WingCues.Damaged;
                // In formation nothing sends the member home.
                case WingEventKind.Winchester: return WingCues.OutOfAmmo;
                default: return null;
            }
        }

        private static string ManeuverWords(ReactionKind kind)
        {
            switch (kind)
            {
                case ReactionKind.BreakLeft: return "break left";
                case ReactionKind.BreakRight: return "break right";
                case ReactionKind.PullUp: return "pull up";
                case ReactionKind.Split: return "split";
                default: return "maneuver";
            }
        }

        // ------------------------------------------------------------------ shots, kills, contacts, questions

        /// <summary>A wingman launched: one Fox call per speaker per repeat window.</summary>
        public void Shot(WingMember m, bool air, bool infrared, bool antiRadar, string targetType)
        {
            if (active && Radio()) Raise(m, WingCues.Shot(air, infrared, antiRadar), null, ("target", targetType));
        }

        private void OnKill(Aircraft shooter, uint victimId, string victimType)
        {
            WingMember m = WingService.Instance?.MemberOf(shooter);
            if (!active || m == null || m.Released || !Radio()) return;
            Raise(m, WingCues.Splash, "splash:" + victimId.ToString(CultureInfo.InvariantCulture), ("target", victimType));
        }

        /// <summary>The Bogey Dope answer on the radio. False with the reason when nobody can answer; <paramref name="answer"/> is the order's result line either way.</summary>
        public bool BogeyDope(WingService w, out string answer)
        {
            WingMember speaker = null;
            foreach (WingMember m in w.Members)
                if (!m.Released && m.Alive && !m.OnGround) { speaker = m; break; }
            if (!active || !Radio())
            {
                answer = "No radio";
                return false;
            }
            // Nobody of the wing is up: the controller answers.
            ChatterSpeaker awacs = speaker == null ? chatter.Station(ChatterNet.Awacs, ChatterStations.Awacs) : null;
            // Every question gets its own key: an answer is never a repeat of the one before.
            string key = "bogeydope:" + (++asks).ToString(CultureInfo.InvariantCulture);
            if (w.NearestAirThreat(w.Player, out Vec3 pos, out Vec3 vel, out string type))
            {
                string bra = Bra.Format(w.Player.GlobalPosition().ToVec3(), pos, vel, PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Imperial);
                answer = "Bogey dope, " + bra + ". " + type + ".";
                if (string.IsNullOrEmpty(type)) type = "bandit";
                return awacs != null ? chatter.Raise("awacs.bogeydope", awacs, key, ("bra", bra), ("type", type))
                    : Raise(speaker, WingCues.BogeyDope, key, ("bra", bra), ("type", type));
            }
            answer = "Picture clean.";
            return awacs != null ? chatter.Raise("awacs.clean", awacs, key) : Raise(speaker, WingCues.Clean, key);
        }

        /// <summary>New air threats (and, scouting, ground units) called by the flying member nearest them, with BRA from the player.</summary>
        private void CallContacts(WingService wing)
        {
            WingConfig cfg = WingSettings.Instance;
            Contacts.Ground = wing.Planner.Active && wing.Planner.Current.Scout;
            if (!cfg.ContactCalls.Value && !Contacts.Ground) return;
            // A call that left the queue without going on air was dropped: forget it, so it is called again when it next qualifies.
            if (pendingKey != null && chatter.Director.Pending == 0 && !chatter.Director.Busy(chatter.Now))
            {
                Contacts.Forget(pendingId);
                pendingKey = null;
            }
            int n = wing.KnownContacts(Contacts, cfg.ContactCalls.Value, samples, units, tracked);
            // While a call waits, the watch still tracks and forgets, but chooses nothing new.
            int k = Contacts.Update(samples, n, chatter.Now, pendingKey != null ? NoReport : report);
            for (int i = 0; i < k; i++)
            {
                int at = report[i];
                ContactSample c = samples[at];
                Unit u = units[at];
                WingMember speaker = NearestFlying(wing, tracked[at]);
                if (speaker == null) continue;
                Vec3 from = wing.Player != null ? wing.Player.GlobalPosition().ToVec3() : speaker.Last.Pos;
                Vec3 vel = u.rb != null ? u.rb.velocity.ToVec3() : Vec3.Zero;
                string bra = Bra.Format(from, tracked[at], vel, PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Imperial);
                string type = u.definition != null ? u.definition.unitName : u.unitName;
                string evt = c.Air ? WingCues.Bandit : WingCues.Contact;
                string key = evt + ":" + c.Id.ToString(CultureInfo.InvariantCulture);
                if (Raise(speaker, evt, key, ("bra", bra), ("type", string.IsNullOrEmpty(type) ? "unknown" : type)))
                {
                    pendingKey = key;
                    pendingEvent = evt;
                    pendingId = c.Id;
                }
                else Contacts.Forget(c.Id);
            }
        }

        private static WingMember NearestFlying(WingService wing, Vec3 at)
        {
            WingMember best = null;
            float bestD = float.MaxValue;
            foreach (WingMember m in wing.Members)
            {
                if (m.Released || !m.Alive || m.OnGround) continue;
                float d = (m.Last.Pos - at).SqrLength;
                if (d >= bestD) continue;
                bestD = d;
                best = m;
            }
            return best;
        }

        // ------------------------------------------------------------------ quiet talk

        /// <summary>Flying level, not fighting, not being shot at: free to say something that is not business.</summary>
        private static bool FreeToTalk(WingMember member) => member != null && !member.Released && member.Alive &&
            !member.OnGround && member.Settle == null && !member.Engaged &&
            member.ThreatMissile == null && member.Last.Speed > 35f && member.Last.RadarAlt > 150f &&
            Math.Abs(member.Last.BankDeg) < 35f && Math.Abs(member.Last.PitchDeg) < 20f && member.Last.Nz < 2f &&
            member.Brain.Mind.Current != BehaviourId.Defend && member.Brain.Mind.Current != BehaviourId.React;

        private void TickTalk(WingService wing, float now)
        {
            ChatterDirector director = chatter.Director;
            bool combat = false, recovery = false;
            free.Clear();
            foreach (WingMember m in wing.Members)
            {
                if (m.Released || !m.Alive) continue;
                if (m.Engaged || m.ThreatMissile != null || m.Brain.Mind.Current == BehaviourId.Defend || m.Brain.Mind.Current == BehaviourId.React) combat = true;
                if (!FreeToTalk(m)) continue;
                if (m.Recovery != null) recovery = true;
                free.Add(SpeakerFor(m));
            }
            if (combat) combatAt = now;
            Set(director, "aftercombat", !combat && now - combatAt < AfterCombatSeconds);
            Set(director, "loss", now - lossAt < LossSeconds);
            Set(director, "recovery", recovery);
            if (combat)
            {
                director.CancelExchange();
                nextExchange = Math.Max(nextExchange, now + QuietAfterUrgent);
                return;
            }
            if (director.ExchangeRunning || now < nextExchange) return;
            bool flying = wing.Player != null && !wing.Player.disabled && wing.Player.rb != null && wing.Player.rb.velocity.sqrMagnitude > 1225f;
            if (!flying || director.Busy(now) || director.Pending > 0 || now - director.LastUrgent < QuietAfterUrgent) return;
            nextExchange = now + (director.StartExchange(now, free) ? UnityEngine.Random.Range(85f, 135f) : 20f);
        }

        private static void Set(ChatterDirector director, string condition, bool on)
        {
            if (on) director.Conditions.Add(condition); else director.Conditions.Remove(condition);
        }
    }

    /// <summary>Reads each event of a <see cref="WingEventRing"/> once, by its running total; events overwritten before they were read are skipped.</summary>
    internal struct EventCursor
    {
        public long Seen;

        public bool Next(WingEventRing ring, out WingEvent e)
        {
            e = default;
            long oldest = ring.Total - ring.Count;
            if (Seen < oldest) Seen = oldest;
            if (Seen >= ring.Total) return false;
            e = ring[(int)(Seen - oldest)];
            Seen++;
            return true;
        }
    }
}

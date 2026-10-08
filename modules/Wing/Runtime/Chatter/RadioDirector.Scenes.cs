using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Modules.Wing.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    internal sealed partial class RadioDirector
    {
        private readonly ChatterPacing pacing = new ChatterPacing();
        private bool active, wasSpeaking, radioOff;
        private float breathUntil, replyAt;
        private string openingKey, replyText;
        private int exchangeIndex = -1, dialogueCursor;
        private WingMember openingSpeaker, replySpeaker;
        private string worldEvent;
        private float worldEventStarted;
        private int operationId;
        private string operationPhase;
        private bool eventBaseline, operationBaseline;
        private IAirAssaultObservation assault;

        private void ResetScenes(float now)
        {
            seed = UnityEngine.Random.Range(0, 10000);
            dialogueCursor = seed;
            pacing.Reset(now, seed);
            CancelExchange();
            wasSpeaking = false;
            radioOff = false;
            breathUntil = 0f;
            worldEvent = operationPhase = null;
            worldEventStarted = 0f;
            operationId = 0;
            eventBaseline = operationBaseline = false;
        }

        public bool Report(string speaker, string key, string text, ChatterUrgency urgency = ChatterUrgency.Status)
        {
            if (!active || Application.isBatchMode || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(text) ||
                (byte)urgency > (byte)ChatterUrgency.Emergency) return false;
            RadioLevel level = WingSettings.Instance.Radio.Value;
            if (level == RadioLevel.Off || level == RadioLevel.Essential && urgency == ChatterUrgency.Ambient) return false;
            string who = Clean(speaker, 32);
            if (who.Length == 0) who = "CONTROL";
            if (urgency >= ChatterUrgency.Tactical) { CancelExchange(); pacing.Suppress(Time.time); }
            return Queue.Enqueue(new RadioLine
            {
                Speaker = RadioQueue.MaxSpeakers - 1, Voice = -1, Class = (RadioClass)urgency,
                Key = "REPORT:" + who + ":" + Clean(key, 96), WingWide = true,
                Text = who + ": " + Clean(text, 240),
            }, Time.time);
        }

        private static string Clean(string text, int max) =>
            string.IsNullOrWhiteSpace(text) ? "" : (text.Length > max ? text.Substring(0, max) : text)
                .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();

        private void CancelExchange()
        {
            Queue.CancelChatter();
            openingKey = replyText = null;
            openingSpeaker = replySpeaker = null;
            replyAt = 0f;
        }

        private static bool Eligible(WingMember member) => member != null && !member.Released && member.Alive &&
            !member.OnGround && member.Settle == null && !member.Engaged &&
            member.ThreatMissile == null && member.Last.Speed > 35f && member.Last.RadarAlt > 150f &&
            Math.Abs(member.Last.BankDeg) < 35f && Math.Abs(member.Last.PitchDeg) < 20f && member.Last.Nz < 2f &&
            member.Brain.Mind.Current != BehaviourId.Defend && member.Brain.Mind.Current != BehaviourId.React;

        private static bool Matches(WingMember member, string tag)
        {
            if (tag == null) return true;
            WingPilot pilot = WingPilotRoster.Of(member);
            return pilot != null && string.Equals(string.IsNullOrWhiteSpace(pilot.DialogueTag) ? pilot.Callsign : pilot.DialogueTag,
                tag, StringComparison.OrdinalIgnoreCase);
        }

        private static WingMember PickSpeaker(WingService wing, string tag, WingMember exclude, int selection)
        {
            int count = 0;
            foreach (WingMember member in wing.Members)
                if (!ReferenceEquals(member, exclude) && Eligible(member) && Matches(member, tag)) count++;
            if (count == 0) return null;
            int pick = (int)((uint)selection % (uint)count);
            foreach (WingMember member in wing.Members)
                if (!ReferenceEquals(member, exclude) && Eligible(member) && Matches(member, tag) && pick-- == 0) return member;
            return null;
        }

        private void TickScenes(WingService wing, float now)
        {
            bool combat = false, flying = wing.Player != null && !wing.Player.disabled && wing.Player.rb != null &&
                wing.Player.rb.velocity.sqrMagnitude > 1225f;
            foreach (WingMember m in wing.Members)
                if (!m.Released && m.Alive && (m.Engaged || m.ThreatMissile != null ||
                    m.Brain.Mind.Current == BehaviourId.Defend || m.Brain.Mind.Current == BehaviourId.React)) combat = true;
            if (combat) { CancelExchange(); pacing.Observe(now); return; }
            if (openingKey != null)
            {
                if (!Eligible(openingSpeaker) || replySpeaker != null && !Eligible(replySpeaker) || !flying ||
                    replyAt > 0f && now > replyAt + RadioQueue.MaxHold)
                { CancelExchange(); return; }
                if (replyAt == 0f)
                {
                    if (!Queue.Holds(openingKey)) CancelExchange();
                    return;
                }
                if (now < replyAt || Speaking() || Queue.Busy(now) || Queue.Queued > 0) return;
                if (replySpeaker != null && replyText != null)
                    QueueDialogue(replySpeaker, "AMBIENTREPLY" + exchangeIndex, replyText, now);
                openingKey = replyText = null;
                openingSpeaker = replySpeaker = null;
                return;
            }
            if (!pacing.Ready(now, WingSettings.Instance.Radio.Value == RadioLevel.Full, flying, combat,
                Queue.Queued > 0 || Queue.Busy(now) || Speaking() || now < breathUntil)) return;

            ChatterScene scene = pacing.Scene;
            if (scene == ChatterScene.Transit)
                foreach (WingMember member in wing.Members)
                    if (Eligible(member) && member.Recovery != null) { scene = ChatterScene.Recovery; break; }
            if (scene == ChatterScene.Transit && ModuleServices.TryGet<IWeatherView>(out var weather))
            {
                var position = wing.Player.GlobalPosition();
                if (weather.TrySample(position.x, position.z, out var sample))
                    scene = sample.Rain01 > 0.35f ? ChatterScene.Weather : sample.Night ? ChatterScene.Night : scene;
            }
            // Search the bounded catalogue once, without making unrelated named pilots speak.
            for (int offset = 0; offset < ChatterDialogue.AmbientCount; offset++)
            {
                int index = (int)((uint)(dialogueCursor + offset) % (uint)ChatterDialogue.AmbientCount);
                ChatterExchange exchange = ChatterDialogue.AmbientAt(index);
                if (exchange.Scene != scene || index == exchangeIndex) continue;
                WingMember first = PickSpeaker(wing, exchange.SpeakerTag, null, seed), second = null;
                if (first == null) continue;
                if (exchange.Reply != null)
                {
                    second = PickSpeaker(wing, exchange.ReplyTag, first, seed + 1);
                    if (second == null) continue;
                }
                string key = "AMBIENT" + index;
                if (!QueueDialogue(first, key, exchange.Opening, now)) break;
                openingKey = key;
                openingSpeaker = first;
                replySpeaker = second;
                replyText = exchange.Reply;
                replyAt = 0f;
                exchangeIndex = index;
                dialogueCursor = index + 1;
                break;
            }
            pacing.Scheduled(now, seed++);
        }

        private bool QueueDialogue(WingMember speaker, string key, string text, float now)
        {
            WingPilot pilot = WingPilotRoster.Of(speaker);
            return Queue.Enqueue(new RadioLine { Speaker = speaker.Seat, Voice = speaker.Voice, MemberId = speaker.Id,
                Class = RadioClass.Chatter, Key = key, ClipKey = key, WingWide = true,
                Text = Clean(pilot?.Callsign ?? ("#" + speaker.Number), 32) + ": " + text }, now);
        }

        private void ExchangeTransmitted(in RadioLine line, float now)
        {
            if (line.Key == openingKey) replyAt = now + RadioQueue.Airtime(line.Text) + 1.4f;
            else if (line.Class >= RadioClass.Tactical) CancelExchange();
        }

        private void ObserveBattlefield(WingService wing, float now)
        {
            ModuleServices.TryGet<IAirAssaultObservation>(out var observation);
            if (!ReferenceEquals(assault, observation))
            {
                ReleaseObservations();
                assault = observation;
                if (assault != null) assault.Landed += InsertionLanded;
            }
            if (ModuleServices.TryGet<IActiveEventsView>(out var events) && events.Available)
            {
                ActiveEventView current = events.Current;
                if (eventBaseline && current != null && (current.Id != worldEvent || current.StartedAtMissionTime != worldEventStarted))
                    Report("THEATER CONTROL", "EVENT:" + current.Id, "Theater update: " + current.Title + ". Check the event desk.");
                worldEvent = current?.Id;
                worldEventStarted = current?.StartedAtMissionTime ?? 0f;
                eventBaseline = true;
            }
            if (ModuleServices.TryGet<ITheaterWarView>(out var theater) && theater.Available && theater.HasSnapshot && theater.SnapshotAgeSeconds <= 5f)
            {
                var operation = theater.ActiveOperation;
                if (operationBaseline && operation != null && (operation.Id != operationId || operation.Phase != operationPhase))
                    Report("COMMAND", "OPERATION:" + operation.Id + ":" + operation.Phase,
                        operation.Label + ". Operation phase: " + operation.Phase + ".", ChatterUrgency.Status);
                operationId = operation?.Id ?? 0;
                operationPhase = operation?.Phase;
                operationBaseline = true;
            }
        }

        private void ReleaseObservations()
        {
            if (assault != null) assault.Landed -= InsertionLanded;
            assault = null;
        }

        private void InsertionLanded(int faction, float x, float z, int shell)
        {
            Aircraft player = WingService.Instance?.Player;
            if (player == null || player.NetworkHQ == null || player.NetworkHQ.GetInstanceID() != faction ||
                !float.IsFinite(x) || !float.IsFinite(z)) return;
            var at = player.GlobalPosition();
            if ((at.x - x) * (at.x - x) + (at.z - z) * (at.z - z) > 20000f * 20000f) return;
            Report("GROUND CONTROL", "INSERTION", "Friendly insertion touchdown observed. Await ground status.");
        }
    }
}

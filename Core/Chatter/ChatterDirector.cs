using System;
using System.Collections.Generic;
using System.Text;

namespace BoscaliSummer.Core.Chatter
{
    /// <summary>
    /// The radio's rules, pure (the host gives it a clock). Publishers raise an event for a speaker; the director draws a line from the packs for that speaker's character
    /// or persona, fills its tokens, and puts it on one channel shared by every net:
    /// <list type="bullet">
    /// <item><b>Priority</b>: the most urgent waiting line goes first, the oldest among equals. A Tactical line cuts an Ambient or Status line that is on air, an Emergency
    /// cuts anything below it, so an urgent call never waits behind talk.</item>
    /// <item><b>No stale calls</b>: a line that waited longer than its priority allows is dropped, never said late.</item>
    /// <item><b>No repeats</b>: the same key is not said again inside <see cref="RepeatSeconds"/>, and a speaker does not draw the same line for an event again until the
    /// others were used.</item>
    /// <item><b>No dead air</b>: a speaker is checked the moment the line would go on air; a dead one is skipped and costs the channel nothing.</item>
    /// <item><b>Exchanges</b>: a scripted conversation goes out one line at a time and is dropped whole when an urgent line arrives; nothing else is touched.</item>
    /// </list>
    /// </summary>
    internal sealed class ChatterDirector
    {
        public const int Capacity = 24;
        public const float RepeatSeconds = 15f, GapSeconds = 0.35f, ExchangePauseSeconds = 0.9f;
        public const float MinAirSeconds = 1.3f, MaxAirSeconds = 9f;

        public readonly ChatterLibrary Library = new ChatterLibrary();
        public readonly ChatterLog Log = new ChatterLog();
        /// <summary>What is true right now ("night", "rain", ...): the words a pack line may ask for.</summary>
        public readonly HashSet<string> Conditions = new HashSet<string>(StringComparer.Ordinal);

        public ChatterLevel Level = ChatterLevel.Full;
        /// <summary>What <c>&lt;lead&gt;</c> reads as.</summary>
        public string Lead = "Lead";
        public Action<ChatterAired> Aired;

        private sealed class Entry
        {
            public ChatterSpeaker Speaker;
            public ChatterPriority Priority;
            public string Key, Event, LineId, Text;
            public float Born;
            public int Exchange;
        }

        private readonly List<Entry> pending = new List<Entry>(Capacity);
        private readonly Dictionary<string, float> lastAired = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> used = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly HashSet<string> usedExchanges = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<ChatterLineDef> scratch = new List<ChatterLineDef>();
        private readonly List<ChatterExchangeDef> scratchExchanges = new List<ChatterExchangeDef>();
        private readonly StringBuilder words = new StringBuilder(160);
        private Random rng = new Random(1);

        private float busyUntil = float.NegativeInfinity;
        private ChatterPriority onAir;
        private ChatterExchangeDef exchange;
        private ChatterSpeaker[] cast;
        private int exchangeSerial, exchangeStep;
        private bool stepWaiting;
        private float nextStepAt;

        public int Pending => pending.Count;
        public int AiredCount { get; private set; }
        public int DroppedCount { get; private set; }
        public bool ExchangeRunning => exchange != null;
        /// <summary>When a Tactical or Emergency line last asked for the channel.</summary>
        public float LastUrgent { get; private set; } = float.NegativeInfinity;
        /// <summary>Events that were raised but that no pack has (for one warning each).</summary>
        public readonly HashSet<string> Unknown = new HashSet<string>(StringComparer.Ordinal);

        public bool Busy(float now) => now < busyUntil;

        /// <summary>A new mission: nothing waiting, nothing remembered. The packs stay.</summary>
        public void Reset(int seed)
        {
            pending.Clear();
            lastAired.Clear();
            used.Clear();
            usedExchanges.Clear();
            Log.Clear();
            Conditions.Clear();
            rng = new Random(seed);
            busyUntil = float.NegativeInfinity;
            LastUrgent = float.NegativeInfinity;
            exchange = null;
            cast = null;
            AiredCount = DroppedCount = 0;
        }

        /// <summary>
        /// Something happened that <paramref name="speaker"/> would call. <paramref name="key"/> names the thing for the repeat rule (default: the event and the speaker);
        /// <paramref name="facts"/> fill the line's tokens. False when the radio will not say it: off, unknown event, no line this speaker can say, a repeat, or a full channel.
        /// </summary>
        public bool Raise(float now, string evt, ChatterSpeaker speaker, string key = null, params (string Name, string Value)[] facts)
        {
            if (speaker == null || string.IsNullOrEmpty(evt) || Level == ChatterLevel.Off) return false;
            if (!Library.Events.TryGetValue(evt, out ChatterEventDef def)) { Unknown.Add(evt); return false; }
            if (Level == ChatterLevel.Essential && def.Priority == ChatterPriority.Ambient) return false;
            key = key ?? evt + ":" + speaker.Id;
            if (Repeat(now, key)) return false;
            ChatterLineDef line = Pick(def, speaker, facts);
            if (line == null) return false;
            return Enqueue(now, new Entry
            {
                Speaker = speaker, Priority = def.Priority, Key = key, Event = evt, LineId = line.Id,
                Text = Fill(line.Text, line.Tokens, speaker, facts), Born = now,
            });
        }

        /// <summary>Exact words, for a publisher whose text is made elsewhere (a mission script, a support desk's answer).</summary>
        public bool Say(float now, ChatterSpeaker speaker, string key, string text, ChatterPriority priority)
        {
            if (speaker == null || string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(key) || Level == ChatterLevel.Off) return false;
            if (Level == ChatterLevel.Essential && priority == ChatterPriority.Ambient) return false;
            if (Repeat(now, key)) return false;
            return Enqueue(now, new Entry { Speaker = speaker, Priority = priority, Key = key, Event = "say", Text = ChatterLibrary.Clean(text), Born = now });
        }

        private bool Repeat(float now, string key)
        {
            foreach (Entry e in pending) if (e.Key == key) return true;
            return lastAired.TryGetValue(key, out float at) && now - at < RepeatSeconds;
        }

        private bool Enqueue(float now, Entry entry)
        {
            if (entry.Priority >= ChatterPriority.Tactical)
            {
                LastUrgent = now;
                if (entry.Exchange == 0) CancelExchange();
            }
            if (pending.Count >= Capacity)
            {
                // Full: the least urgent, oldest line makes room for a more urgent one.
                int worst = 0;
                for (int i = 1; i < pending.Count; i++)
                    if (pending[i].Priority < pending[worst].Priority || pending[i].Priority == pending[worst].Priority && pending[i].Born < pending[worst].Born) worst = i;
                if (pending[worst].Priority >= entry.Priority) { DroppedCount++; return false; }
                Drop(worst);
            }
            pending.Add(entry);
            return true;
        }

        private void Drop(int index)
        {
            Entry e = pending[index];
            pending.RemoveAt(index);
            DroppedCount++;
            if (e.Exchange != 0 && e.Exchange == exchangeSerial) CancelExchange();
        }

        /// <summary>How long a line may wait for the channel before it is no longer worth saying.</summary>
        public static float Patience(ChatterPriority p)
        {
            switch (p)
            {
                case ChatterPriority.Emergency: return 4f;
                case ChatterPriority.Tactical: return 7f;
                case ChatterPriority.Status: return 12f;
                default: return 15f;
            }
        }

        /// <summary>How long the words hold the channel when nothing measures the audio.</summary>
        public static float Airtime(string text) =>
            System.Math.Max(MinAirSeconds, System.Math.Min(MaxAirSeconds, 0.7f + 0.062f * (text?.Length ?? 0)));

        /// <summary>The host played a clip that runs longer than the estimate: hold the channel for it.</summary>
        public void Hold(float now, float seconds)
        {
            if (!(seconds > 0f)) return;
            float until = now + System.Math.Min(seconds, 30f);
            if (until <= busyUntil) return;
            if (exchange != null && !stepWaiting) nextStepAt += until - busyUntil;
            busyUntil = until;
        }

        /// <summary>
        /// Once a frame. Puts at most one line on air and tells <see cref="Aired"/>. <paramref name="alive"/> says whether a speaker can still talk (null: everybody can).
        /// </summary>
        public void Tick(float now, Func<ChatterSpeaker, bool> alive = null)
        {
            if (exchange != null && !stepWaiting && now >= nextStepAt && now >= busyUntil && pending.Count == 0) QueueStep(now);

            for (int i = pending.Count - 1; i >= 0; i--)
                if (now - pending[i].Born > Patience(pending[i].Priority)) Drop(i);

            while (pending.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < pending.Count; i++)
                    if (pending[i].Priority > pending[best].Priority || pending[i].Priority == pending[best].Priority && pending[i].Born < pending[best].Born) best = i;
                Entry e = pending[best];
                bool busy = now < busyUntil;
                if (busy && !(e.Priority >= ChatterPriority.Tactical && e.Priority > onAir)) return;
                if (!busy && now < busyUntil + GapSeconds && e.Priority < ChatterPriority.Emergency) return;
                if (alive != null && !alive(e.Speaker)) { Drop(best); continue; }
                pending.RemoveAt(best);
                Air(now, e, busy);
                return;
            }
        }

        private void Air(float now, Entry e, bool cut)
        {
            float seconds = Airtime(e.Text);
            busyUntil = now + seconds;
            onAir = e.Priority;
            lastAired[e.Key] = now;
            if (e.Priority >= ChatterPriority.Tactical) LastUrgent = now;
            if (e.Exchange != 0 && e.Exchange == exchangeSerial)
            {
                stepWaiting = false;
                if (exchangeStep >= exchange.Steps.Count) { exchange = null; cast = null; }
                else nextStepAt = busyUntil + ExchangePauseSeconds;
            }
            var aired = new ChatterAired
            {
                Time = now, Net = e.Speaker.Net, Priority = e.Priority, Speaker = e.Speaker.Callsign, SpeakerId = e.Speaker.Id,
                Event = e.Event, LineId = e.LineId, Text = e.Text, Seconds = seconds, Cut = cut,
            };
            AiredCount++;
            Log.Push(aired);
            Aired?.Invoke(aired);
        }

        // ------------------------------------------------------------------ drawing a line

        private ChatterLineDef Pick(ChatterEventDef def, ChatterSpeaker speaker, (string Name, string Value)[] facts)
        {
            // The best tier that has a line this speaker can say now: their own character, their persona, anybody.
            string character = string.IsNullOrEmpty(speaker.Character) ? null : "@" + speaker.Character;
            for (int tier = 0; tier < 3 && scratch.Count == 0; tier++)
            {
                string who = tier == 0 ? character : tier == 1 ? speaker.Persona : "*";
                if (string.IsNullOrEmpty(who)) continue;
                foreach (ChatterLineDef line in def.Lines)
                    if (line.Who == who && Holds(line.Need, line.Forbid) && Fillable(line.Tokens, facts)) scratch.Add(line);
            }
            if (scratch.Count == 0) return null;
            string bag = speaker.Id + "|" + def.Id;
            if (!used.TryGetValue(bag, out HashSet<string> seen)) used[bag] = seen = new HashSet<string>(StringComparer.Ordinal);
            int fresh = 0;
            foreach (ChatterLineDef line in scratch) if (!seen.Contains(line.Id)) fresh++;
            if (fresh == 0) { seen.Clear(); fresh = scratch.Count; }
            int pick = rng.Next(fresh);
            ChatterLineDef chosen = null;
            foreach (ChatterLineDef line in scratch)
                if (!seen.Contains(line.Id) && pick-- == 0) { chosen = line; break; }
            scratch.Clear();
            seen.Add(chosen.Id);
            return chosen;
        }

        private bool Holds(string[] need, string[] forbid)
        {
            foreach (string c in need) if (!Conditions.Contains(c)) return false;
            foreach (string c in forbid) if (Conditions.Contains(c)) return false;
            return true;
        }

        private bool Fillable(string[] tokens, (string Name, string Value)[] facts)
        {
            foreach (string token in tokens)
            {
                if (token == "callsign" || token == "lead") continue;
                if (Fact(facts, token) != null) continue;
                if (Library.Lists.TryGetValue(token, out List<string> list) && list.Count > 0) continue;
                return false;
            }
            return true;
        }

        private static string Fact((string Name, string Value)[] facts, string name)
        {
            if (facts != null)
                foreach ((string Name, string Value) f in facts)
                    if (f.Name == name && !string.IsNullOrEmpty(f.Value)) return f.Value;
            return null;
        }

        private string Fill(string text, string[] tokens, ChatterSpeaker speaker, (string Name, string Value)[] facts)
        {
            if (tokens.Length == 0) return text;
            words.Clear();
            int from = 0;
            for (int at = text.IndexOf('<'); at >= 0; at = text.IndexOf('<', from))
            {
                int end = text.IndexOf('>', at);
                string name = text.Substring(at + 1, end - at - 1).ToLowerInvariant();
                words.Append(text, from, at - from);
                string value = Fact(facts, name);
                if (value == null)
                {
                    if (name == "callsign") value = speaker.Callsign;
                    else if (name == "lead") value = Lead;
                    else if (Library.Lists.TryGetValue(name, out List<string> list) && list.Count > 0) value = list[rng.Next(list.Count)];
                }
                words.Append(value);
                from = end + 1;
            }
            words.Append(text, from, text.Length - from);
            return ChatterLibrary.Clean(words.ToString());
        }

        // ------------------------------------------------------------------ exchanges

        /// <summary>
        /// Start a conversation among <paramref name="candidates"/> (pilots free to talk). One whose <c>when</c> holds now is preferred over an everyday one, none is
        /// repeated until the others were heard, and every role goes to a different candidate its <c>[who]</c> allows. False when the radio is not on Full, one is running, or nothing fits.
        /// </summary>
        public bool StartExchange(float now, IReadOnlyList<ChatterSpeaker> candidates)
        {
            if (Level != ChatterLevel.Full || exchange != null || candidates == null || candidates.Count < 2) return false;
            var roles = new ChatterSpeaker[ChatterExchangeDef.MaxRoles];
            for (int pass = 0; pass < 2; pass++)
            {
                scratchExchanges.Clear();
                bool scene = false;
                foreach (ChatterExchangeDef x in Library.Exchanges)
                {
                    if (usedExchanges.Contains(x.Id) || !Holds(x.When, Array.Empty<string>()) || !Cast(x, candidates, roles, 0)) continue;
                    if (x.When.Length > 0 && !scene) { scene = true; scratchExchanges.Clear(); }
                    if (scene == x.When.Length > 0) scratchExchanges.Add(x);
                }
                if (scratchExchanges.Count > 0) break;
                // Everything that fits was heard: start the round again.
                if (pass == 0) usedExchanges.Clear();
            }
            if (scratchExchanges.Count == 0) return false;
            ChatterExchangeDef chosen = scratchExchanges[rng.Next(scratchExchanges.Count)];
            scratchExchanges.Clear();
            Shuffle(candidates, out List<ChatterSpeaker> shuffled);
            Cast(chosen, shuffled, roles, 0);
            usedExchanges.Add(chosen.Id);
            exchange = chosen;
            cast = roles;
            exchangeSerial++;
            if (exchangeSerial == 0) exchangeSerial = 1;
            exchangeStep = 0;
            stepWaiting = false;
            QueueStep(now);
            return exchange != null;
        }

        public void CancelExchange()
        {
            if (exchange == null) return;
            int serial = exchangeSerial;
            exchange = null;
            cast = null;
            pending.RemoveAll(e => e.Exchange == serial);
        }

        private void QueueStep(float now)
        {
            ChatterStepDef step = exchange.Steps[exchangeStep];
            ChatterSpeaker speaker = cast[step.Role];
            var entry = new Entry
            {
                Speaker = speaker, Priority = ChatterPriority.Ambient, Key = "x:" + exchange.Id + ":" + exchangeStep, Event = "exchange." + exchange.Id,
                LineId = step.Id, Text = Fill(step.Text, step.Tokens ?? Array.Empty<string>(), speaker, null), Born = now, Exchange = exchangeSerial,
            };
            exchangeStep++;
            stepWaiting = true;
            if (!Enqueue(now, entry)) CancelExchange();
        }

        /// <summary>Give every role from <paramref name="role"/> on a candidate nobody else holds (four roles at most: plain backtracking).</summary>
        private static bool Cast(ChatterExchangeDef x, IReadOnlyList<ChatterSpeaker> candidates, ChatterSpeaker[] roles, int role)
        {
            if (role >= x.RoleWho.Count) return true;
            string who = x.RoleWho[role];
            foreach (ChatterSpeaker c in candidates)
            {
                bool taken = false;
                for (int i = 0; i < role; i++) if (ReferenceEquals(roles[i], c)) taken = true;
                if (taken || !Fits(c, who)) continue;
                roles[role] = c;
                if (Cast(x, candidates, roles, role + 1)) return true;
            }
            roles[role] = null;
            return false;
        }

        private static bool Fits(ChatterSpeaker speaker, string who) =>
            who == "*" || who == speaker.Persona || who.Length > 1 && who[0] == '@' && who.Substring(1) == speaker.Character;

        private void Shuffle(IReadOnlyList<ChatterSpeaker> from, out List<ChatterSpeaker> into)
        {
            into = new List<ChatterSpeaker>(from);
            for (int i = into.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                ChatterSpeaker swap = into[i];
                into[i] = into[j];
                into[j] = swap;
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Scenario
{
    /// <summary>Where a running script shows and says things. Everything here is local presentation: nothing changes the world, nothing is sent.</summary>
    internal interface IScenarioStage
    {
        /// <summary>A line on the radio, held for <paramref name="seconds"/>, spoken from <paramref name="voice"/> when it names a recording. Returns how long the
        /// line actually holds the channel: a recording longer than the reading time holds it to its end.</summary>
        float Say(ScenarioCast cast, string text, float seconds, string voice);
        void Card(string title, string text, float seconds);
        void Hint(string text, float seconds);

        /// <summary>The pilot is being asked something (<see cref="Choose"/>); the script waits for the answer.</summary>
        bool Waiting { get; }

        /// <summary>A quick fade through black: it covers whatever changes in the same moment.</summary>
        void Dip();

        /// <summary>Plays a prerendered video over the whole screen; the story waits (<see cref="Waiting"/>) until it ends or the pilot skips it.</summary>
        void Video(string file);

        /// <summary>Host only: holds a set-piece sky for everybody ("storm", "hurricane", "front", "fog", "wave"), or with "clear" lets the weather go on.</summary>
        void Sky(string name);

        /// <summary>Host only: sets the hour of the day for everybody.</summary>
        void Clock(float hour);

        /// <summary>Puts the pilot on a side without the game's join screen.</summary>
        void Enlist(ScenarioSide side);

        /// <summary>Asks the pilot which side to fly for, in place of the game's join screen; the story waits (<see cref="Waiting"/>) for the answer.</summary>
        void Choose();

        /// <summary>Puts the pilot, already on a side, into an aircraft of that name in the air. Host and single player only.</summary>
        void Seat(string aircraft, float x, float y, float z, float heading, float speed);

        /// <summary>Host only: takes the pilot's aircraft out of the world, so a later <c>seat</c> can hand over another.</summary>
        void Leave();

        /// <summary>Host only: gives a scripted aircraft something to fly. <paramref name="order"/> is "orbit" (a circle of <paramref name="radius"/> metres round x, z),
        /// "follow" (<paramref name="target"/> is a unit name or "player") or "free" (back to the game's own behaviour).</summary>
        void Orders(string unit, string order, string target, float x, float z, float radius);

        /// <summary>Offers the pilot two replies (paragon / renegade) under a prompt and waits (<see cref="Waiting"/>) until the stage calls <see cref="ScenarioRunner.Answer"/>;
        /// after <paramref name="timeout"/> seconds unanswered the stage answers paragon.</summary>
        void Reply(string prompt, string paragon, string renegade, float timeout);

        /// <summary>Plays a music file under the story, or with a null name fades out what is playing.</summary>
        void Music(string file, float volume, bool loop);

        /// <summary>Keep a flag for the campaign's later missions.</summary>
        void Remember(string flag);

        /// <summary>The mission's story is told: the campaign's next chapter opens.</summary>
        void Finish();

        /// <summary>Host only: starts, completes or cancels the game's own objectives by name; the game sends the change to every player.</summary>
        void Objectives(ActionKind verb, string[] names);

        /// <summary>A countdown on screen with its label, <paramref name="seconds"/> from now; a null label takes it down.</summary>
        void Countdown(string label, float seconds);

        /// <summary>A named enemy's health bar on screen, the unit followed by its mission name; a null unit takes it down.</summary>
        void Ace(string unit, string callsign, string line);
    }

    /// <summary>What a running script can ask about the world. Read a few times a second, never written.</summary>
    internal interface IScenarioWorld
    {
        /// <summary>The unit of that mission name was in the world and is now destroyed.</summary>
        bool Destroyed(string unit);

        /// <summary>A unit's health share, 0 to 1, or a negative number when it is not in the world.</summary>
        float Health(string unit);

        /// <summary>The pilot's height above the ground in metres, or NaN when they are not flying.</summary>
        float Height { get; }

        /// <summary>The pilot is flying within <paramref name="range"/> metres (across the ground) of the point.</summary>
        bool Near(float x, float z, float range);
    }

    /// <summary>
    /// Plays a script's beats for one pilot. Events come in through <see cref="Raise"/>; a beat whose trigger and conditions match joins a queue, and the queue plays
    /// one action at a time so two speakers never talk over each other. Pure: the clock is handed in, everything shown goes out through <see cref="IScenarioStage"/>.
    /// </summary>
    internal sealed class ScenarioRunner
    {
        public const int MaxQueued = 16;
        public const float LineGapSeconds = 0.4f, WatchSeconds = 0.5f;

        private readonly ScenarioDef def;
        private readonly IScenarioStage stage;
        private readonly Func<string, bool> remembered;
        private readonly HashSet<string> fired = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<ScenarioBeat> queue = new List<ScenarioBeat>();
        private readonly List<KeyValuePair<TriggerKind, string>> raised = new List<KeyValuePair<TriggerKind, string>>();
        private ScenarioBeat current;
        private int step;
        private float now, busyUntil, nextWatch, countdownEnds = -1f;
        private string countdownFlag;
        // Watched beats whose condition held at the last look: they fire again only after it has let go.
        private readonly HashSet<ScenarioBeat> holding = new HashSet<ScenarioBeat>();

        /// <summary>Where watched triggers (destroyed, damaged, near, below, above) are read; null leaves them silent.</summary>
        public IScenarioWorld World;

        /// <summary>The dice <c>random</c> rolls.</summary>
        public Random Dice = new Random();

        /// <summary>Flags the script set in this mission.</summary>
        public readonly HashSet<string> Flags = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The pilot's side once they have one; beats written for a side wait for it.</summary>
        public ScenarioSide Side;

        /// <summary>Above 1 the pilot reads faster: lines are held for less time.</summary>
        public float TextSpeed = 1f;

        public bool Finished { get; private set; }

        private ScenarioAction? replying;

        /// <summary>Nothing is playing and nothing waits.</summary>
        public bool Idle => current == null && queue.Count == 0 && raised.Count == 0 && now >= busyUntil && !stage.Waiting;

        /// <param name="remembered">Whether the campaign kept a flag from an earlier mission (null: it kept none).</param>
        public ScenarioRunner(ScenarioDef def, IScenarioStage stage, Func<string, bool> remembered = null)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
            this.stage = stage ?? throw new ArgumentNullException(nameof(stage));
            this.remembered = remembered;
        }

        /// <summary>Something happened. <paramref name="name"/> is the objective or the flag for the triggers that have one.</summary>
        public void Raise(TriggerKind kind, string name = null) => raised.Add(new KeyValuePair<TriggerKind, string>(kind, name));

        /// <summary>How long a line stays up: long enough to read it at an unhurried pace.</summary>
        public static float ReadSeconds(string text, float speed = 1f)
        {
            float seconds = 1.2f + (text?.Length ?? 0) * 0.06f;
            return System.Math.Min(11f, System.Math.Max(2.5f, seconds)) / System.Math.Max(0.25f, speed);
        }

        /// <summary>Once a frame, with the seconds the script has been running (a clock that stops when the game is paused).</summary>
        public void Tick(float seconds)
        {
            now = seconds;
            foreach (ScenarioBeat beat in def.Beats)
                if (beat.Trigger == TriggerKind.Time && now >= beat.Seconds) Fire(beat);
            if (World != null && now >= nextWatch)
            {
                nextWatch = now + WatchSeconds;
                foreach (ScenarioBeat beat in def.Beats)
                {
                    if (beat.Trigger < TriggerKind.Destroyed || beat.Trigger > TriggerKind.Above) continue;
                    if (!Holds(beat)) holding.Remove(beat);
                    else if (holding.Add(beat)) Fire(beat);
                }
            }
            if (countdownEnds >= 0f && now >= countdownEnds)
            {
                countdownEnds = -1f;
                stage.Countdown(null, 0f);
                if (countdownFlag != null && Flags.Add(countdownFlag)) Raise(TriggerKind.Flag, countdownFlag);
            }
            // A beat may set a flag while this runs; the list is read by index so that event is seen too.
            for (int i = 0; i < raised.Count; i++)
            {
                foreach (ScenarioBeat beat in def.Beats)
                    if (beat.Trigger == raised[i].Key && beat.Trigger != TriggerKind.Time &&
                        (beat.Arg == null || string.Equals(beat.Arg, raised[i].Value, StringComparison.Ordinal))) Fire(beat);
            }
            raised.Clear();

            while (!stage.Waiting && now >= busyUntil)
            {
                if (current == null)
                {
                    if (queue.Count == 0) return;
                    current = queue[0];
                    queue.RemoveAt(0);
                    step = 0;
                }
                if (step >= current.Actions.Count) { current = null; continue; }
                Run(current.Actions[step++]);
            }
        }

        /// <summary>
        /// The pilot answered the open <c>reply</c> (the stage's timeout answers paragon). Sets the flag <c>&lt;id&gt;-paragon</c> or <c>&lt;id&gt;-renegade</c>, adds one to the
        /// campaign's tally (the remembered flags <c>paragon-1</c>, <c>paragon-2</c>, ...: the tally is the highest one set) and speaks the chosen line.
        /// </summary>
        public void Answer(bool paragon)
        {
            if (replying == null) return;
            ScenarioAction offer = replying.Value;
            replying = null;
            string kind = paragon ? "paragon" : "renegade";
            string flag = offer.Name + "-" + kind;
            if (Flags.Add(flag)) Raise(TriggerKind.Flag, flag);
            int count = 1;
            while (Has(kind + "-" + count)) count++;
            Flags.Add(kind + "-" + count);
            stage.Remember(kind + "-" + count);
            Run(new ScenarioAction
            {
                Kind = ActionKind.Say, Name = offer.Title, Text = paragon ? offer.Paragon : offer.Renegade, Voice = paragon ? offer.ParagonVoice : offer.RenegadeVoice
            });
        }

        private void Fire(ScenarioBeat beat)
        {
            // A clock time passes once, so a beat on the clock plays once whatever it says about repeating.
            if ((!beat.Repeat || beat.Trigger == TriggerKind.Time) && fired.Contains(beat.Id)) return;
            if (beat.Side != ScenarioSide.Any && beat.Side != Side) return;
            if (beat.If != null && !Has(beat.If)) return;
            if (beat.Unless != null && Has(beat.Unless)) return;
            if (ReferenceEquals(current, beat) || queue.Contains(beat) || queue.Count >= MaxQueued) return;
            fired.Add(beat.Id);
            queue.Add(beat);
        }

        private bool Holds(ScenarioBeat beat)
        {
            switch (beat.Trigger)
            {
                case TriggerKind.Destroyed: return World.Destroyed(beat.Arg);
                case TriggerKind.Damaged:
                    float health = World.Health(beat.Arg);
                    return health >= 0f && health <= beat.Seconds;
                case TriggerKind.Near: return World.Near(beat.X, beat.Z, beat.Range);
                // NaN (not flying) is neither below nor above.
                case TriggerKind.Below: return World.Height < beat.Seconds;
                default: return World.Height > beat.Seconds;
            }
        }

        private bool Has(string flag) => Flags.Contains(flag) || remembered != null && remembered(flag);

        private void Hold(float seconds) => busyUntil = now + System.Math.Max(0f, seconds);

        private void Run(ScenarioAction action)
        {
            switch (action.Kind)
            {
                case ActionKind.Say:
                    if (!def.Cast.TryGetValue(action.Name, out ScenarioCast cast)) return;
                    float seconds = ReadSeconds(action.Text, TextSpeed);
                    Hold(System.Math.Max(seconds, stage.Say(cast, action.Text, seconds, action.Voice)) + LineGapSeconds);
                    break;
                case ActionKind.Card:
                    stage.Card(action.Name, action.Text, action.Seconds);
                    Hold(action.Seconds);
                    break;
                case ActionKind.Hint:
                    stage.Hint(action.Text, action.Seconds);
                    break;
                case ActionKind.Wait:
                    Hold(action.Seconds);
                    break;
                case ActionKind.Dip:
                    stage.Dip();
                    break;
                case ActionKind.Video:
                    stage.Video(action.Name);
                    break;
                case ActionKind.Sky:
                    stage.Sky(action.Name);
                    break;
                case ActionKind.Clock:
                    stage.Clock(action.Seconds);
                    break;
                case ActionKind.Music:
                    stage.Music(action.Name, action.Seconds, action.Text == "loop");
                    break;
                case ActionKind.Enlist:
                    ScenarioParser.Side(action.Name, out ScenarioSide side);
                    stage.Enlist(side);
                    break;
                case ActionKind.Choose:
                    stage.Choose();
                    break;
                case ActionKind.Leave:
                    stage.Leave();
                    break;
                case ActionKind.Orders:
                    stage.Orders(action.Name, action.Title, action.Text, action.X, action.Z, action.Speed);
                    break;
                case ActionKind.Reply:
                    replying = action;
                    stage.Reply(action.Text, action.Paragon, action.Renegade, action.Seconds);
                    break;
                case ActionKind.Seat:
                    stage.Seat(action.Name, action.X, action.Y, action.Z, action.Heading, action.Speed);
                    break;
                case ActionKind.Flag:
                    if (Flags.Add(action.Name)) Raise(TriggerKind.Flag, action.Name);
                    break;
                case ActionKind.Unflag:
                    Flags.Remove(action.Name);
                    break;
                case ActionKind.Remember:
                    Flags.Add(action.Name);
                    stage.Remember(action.Name);
                    break;
                case ActionKind.Start:
                case ActionKind.Complete:
                case ActionKind.Cancel:
                    stage.Objectives(action.Kind, action.Names);
                    break;
                case ActionKind.Countdown:
                    // No label: 'countdown stop'.
                    countdownEnds = action.Text == null ? -1f : now + action.Seconds;
                    countdownFlag = action.Name;
                    stage.Countdown(action.Text, action.Seconds);
                    break;
                case ActionKind.Ace:
                    stage.Ace(action.Name, action.Title, action.Text);
                    break;
                case ActionKind.Random:
                    string picked = action.Names[Dice.Next(action.Names.Length)];
                    if (Flags.Add(picked)) Raise(TriggerKind.Flag, picked);
                    break;
                case ActionKind.Finish:
                    if (Finished) break;
                    Finished = true;
                    stage.Finish();
                    break;
            }
        }
    }
}

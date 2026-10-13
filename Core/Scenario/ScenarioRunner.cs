using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Scenario
{
    /// <summary>Where a running script shows and says things. Everything here is local presentation: nothing changes the world, nothing is sent.</summary>
    internal interface IScenarioStage
    {
        void Say(ScenarioCast cast, string text, float seconds);
        void Card(string title, string text, float seconds);
        void Hint(string text, float seconds);

        /// <summary>Starts a cutscene: the shots in order, each with its words. False when the camera cannot be taken now.</summary>
        bool Cut(IReadOnlyList<ScenarioCut> cuts);

        /// <summary>A cutscene is on screen; the script waits for it.</summary>
        bool CutPlaying { get; }

        /// <summary>A quick fade through black: it covers whatever changes in the same moment.</summary>
        void Dip();

        /// <summary>Host only: holds a set-piece sky for everybody ("storm", "hurricane", "front", "fog", "wave"), or with "clear" lets the weather go on.</summary>
        void Sky(string name);

        /// <summary>Host only: sets the hour of the day for everybody.</summary>
        void Clock(float hour);

        /// <summary>Puts the pilot on a side without the game's join screen.</summary>
        void Enlist(ScenarioSide side);

        /// <summary>Asks the pilot which side to fly for, in place of the game's join screen; the story waits (<see cref="CutPlaying"/>) for the answer.</summary>
        void Choose();

        /// <summary>Puts the pilot, already on a side, into an aircraft of that name in the air. Host and single player only.</summary>
        void Seat(string aircraft, float x, float y, float z, float heading, float speed);

        /// <summary>Plays a music file under the story, or with a null name fades out what is playing.</summary>
        void Music(string file, float volume);

        /// <summary>Keep a flag for the campaign's later missions.</summary>
        void Remember(string flag);

        /// <summary>The mission's story is told: the campaign's next chapter opens.</summary>
        void Finish();
    }

    /// <summary>
    /// Plays a script's beats for one pilot. Events come in through <see cref="Raise"/>; a beat whose trigger and conditions match joins a queue, and the queue plays
    /// one action at a time so two speakers never talk over each other. Pure: the clock is handed in, everything shown goes out through <see cref="IScenarioStage"/>.
    /// </summary>
    internal sealed class ScenarioRunner
    {
        public const int MaxQueued = 16;
        public const float LineGapSeconds = 0.4f;

        private readonly ScenarioDef def;
        private readonly IScenarioStage stage;
        private readonly Func<string, bool> remembered;
        private readonly HashSet<string> fired = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<ScenarioBeat> queue = new List<ScenarioBeat>();
        private readonly List<KeyValuePair<TriggerKind, string>> raised = new List<KeyValuePair<TriggerKind, string>>();
        private readonly Queue<ScenarioCut> spilled = new Queue<ScenarioCut>();
        private readonly List<ScenarioCut> cuts = new List<ScenarioCut>();
        private ScenarioBeat current;
        private int step;
        private float now, busyUntil;

        /// <summary>Flags the script set in this mission.</summary>
        public readonly HashSet<string> Flags = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The pilot's side once they have one; beats written for a side wait for it.</summary>
        public ScenarioSide Side;

        /// <summary>Above 1 the pilot reads faster: lines are held for less time.</summary>
        public float TextSpeed = 1f;

        public bool Finished { get; private set; }

        /// <summary>Nothing is playing and nothing waits.</summary>
        public bool Idle => current == null && queue.Count == 0 && spilled.Count == 0 && raised.Count == 0 && now >= busyUntil && !stage.CutPlaying;

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
            // A beat may set a flag while this runs; the list is read by index so that event is seen too.
            for (int i = 0; i < raised.Count; i++)
            {
                foreach (ScenarioBeat beat in def.Beats)
                    if (beat.Trigger == raised[i].Key && beat.Trigger != TriggerKind.Time &&
                        (beat.Arg == null || string.Equals(beat.Arg, raised[i].Value, StringComparison.Ordinal))) Fire(beat);
            }
            raised.Clear();

            while (!stage.CutPlaying && now >= busyUntil)
            {
                if (spilled.Count > 0)
                {
                    ScenarioCut words = spilled.Dequeue();
                    Hold(ReadSeconds(words.Title + words.Caption, TextSpeed));
                    stage.Card(words.Title, words.Caption, busyUntil - now);
                    continue;
                }
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

        private bool Has(string flag) => Flags.Contains(flag) || remembered != null && remembered(flag);

        private void Hold(float seconds) => busyUntil = now + System.Math.Max(0f, seconds);

        private void Run(ScenarioAction action)
        {
            switch (action.Kind)
            {
                case ActionKind.Say:
                    if (!def.Cast.TryGetValue(action.Name, out ScenarioCast cast)) return;
                    float seconds = ReadSeconds(action.Text, TextSpeed);
                    stage.Say(cast, action.Text, seconds);
                    Hold(seconds + LineGapSeconds);
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
                case ActionKind.Cut:
                    cuts.Clear();
                    // Shots written one after another are one cutscene.
                    for (step--; step < current.Actions.Count && current.Actions[step].Kind == ActionKind.Cut; step++)
                    {
                        ScenarioAction cut = current.Actions[step];
                        if (!def.Shots.TryGetValue(cut.Name, out ScenarioShot shot)) continue;
                        ScenarioPicture photo = null;
                        if (cut.Photo != null) def.Pictures.TryGetValue(cut.Photo, out photo);
                        cuts.Add(new ScenarioCut { Shot = shot, Title = cut.Title, Caption = cut.Text, Stamp = cut.Stamp, Photo = photo });
                    }
                    if (cuts.Count == 0 || stage.Cut(cuts)) break;
                    // No camera (the pilot is flying, or cutscenes are off): the words still get read.
                    foreach (ScenarioCut words in cuts)
                        if (!string.IsNullOrEmpty(words.Title) || !string.IsNullOrEmpty(words.Caption)) spilled.Enqueue(words);
                    break;
                case ActionKind.Dip:
                    stage.Dip();
                    break;
                case ActionKind.Sky:
                    stage.Sky(action.Name);
                    break;
                case ActionKind.Clock:
                    stage.Clock(action.Seconds);
                    break;
                case ActionKind.Music:
                    stage.Music(action.Name, action.Seconds);
                    break;
                case ActionKind.Enlist:
                    ScenarioParser.Side(action.Name, out ScenarioSide side);
                    stage.Enlist(side);
                    break;
                case ActionKind.Choose:
                    stage.Choose();
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
                case ActionKind.Finish:
                    if (Finished) break;
                    Finished = true;
                    stage.Finish();
                    break;
            }
        }
    }
}

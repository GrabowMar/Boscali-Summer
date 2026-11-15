using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Chatter
{
    /// <summary>
    /// The radio's YES/NO questions (2026-11-15, after Ace Combat 8): a station asks on the radio and the pilot answers with one key. One question at a time. It opens
    /// when its line goes on air (a line the channel never airs is dropped after <see cref="PendingSeconds"/>), stays open for the line plus
    /// <see cref="AnswerSeconds"/>, and no answer counts as NO. A key is not asked again for <see cref="CooldownSeconds"/> after it closed. The answer runs after the
    /// question is cleared, so an answer may ask the next question. Runs on the mission clock the caller passes. Pure.
    /// </summary>
    internal sealed class ChatterQuestions
    {
        public const float AnswerSeconds = 8f, CooldownSeconds = 90f, PendingSeconds = 20f, EchoSeconds = 1.5f;

        private sealed class Question
        {
            public string Key, Event, SpeakerId;
            public Action Yes, No;
            public float Born, Until;
            public bool Open;
        }

        private readonly Dictionary<string, float> cooldown = new Dictionary<string, float>(StringComparer.Ordinal);
        private Question current;

        /// <summary>A question is on air and waiting for the pilot.</summary>
        public bool Open => current != null && current.Open;
        /// <summary>When the open question closes as NO.</summary>
        public float Until => current != null && current.Open ? current.Until : 0f;
        /// <summary>How long the open question was given in all (its line plus the answer window).</summary>
        public float Window { get; private set; }
        /// <summary>The last answer as the board echoes it: YES, NO or NO ANSWER; and when it was given.</summary>
        public string LastAnswer { get; private set; } = "";
        public float AnsweredAt { get; private set; } = float.NegativeInfinity;
        public int Asked { get; private set; }
        public int Answered { get; private set; }

        /// <summary>Nothing asked or waiting, and <paramref name="key"/> is not cooling down.</summary>
        public bool CanAsk(float now, string key) =>
            current == null && !string.IsNullOrEmpty(key) && !(cooldown.TryGetValue(key, out float until) && now < until);

        /// <summary>Holds a question for the line of <paramref name="evt"/> by <paramref name="speakerId"/> that the caller raises next. False when it may not be asked now.</summary>
        public bool Ask(float now, string key, string evt, string speakerId, Action yes, Action no)
        {
            if (!CanAsk(now, key) || string.IsNullOrEmpty(evt)) return false;
            current = new Question { Key = key, Event = evt, SpeakerId = speakerId, Yes = yes, No = no, Born = now };
            Asked++;
            return true;
        }

        /// <summary>The line was never raised after all: forget the question without an answer.</summary>
        public void Withdraw(string key)
        {
            if (current != null && current.Key == key && !current.Open) current = null;
        }

        /// <summary>A line went on air. True when it is the waiting question's line: the question is open from now.</summary>
        public bool OnAired(in ChatterAired line)
        {
            if (current == null || current.Open || line.Event != current.Event || line.SpeakerId != current.SpeakerId) return false;
            current.Open = true;
            Window = System.Math.Max(0f, line.Seconds) + AnswerSeconds;
            current.Until = line.Time + Window;
            return true;
        }

        /// <summary>Closes an unanswered question as NO, and drops one whose line never aired.</summary>
        public void Tick(float now)
        {
            if (current == null) return;
            if (!current.Open) { if (now - current.Born > PendingSeconds) current = null; return; }
            if (now >= current.Until) Close(now, false, "NO ANSWER");
        }

        /// <summary>The pilot's key. False when no question is open (the key then does nothing).</summary>
        public bool Answer(float now, bool yes)
        {
            if (!Open) return false;
            Close(now, yes, yes ? "YES" : "NO");
            return true;
        }

        public void Reset()
        {
            current = null;
            cooldown.Clear();
            LastAnswer = "";
            AnsweredAt = float.NegativeInfinity;
            Window = 0f;
        }

        private void Close(float now, bool yes, string echo)
        {
            Question q = current;
            current = null;
            cooldown[q.Key] = now + CooldownSeconds;
            LastAnswer = echo;
            AnsweredAt = now;
            Answered++;
            (yes ? q.Yes : q.No)?.Invoke();
        }
    }
}

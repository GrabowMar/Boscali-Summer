using System;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.HighCommand.Domain
{
    /// <summary>
    /// One faction's bounded staff log: the last few things that happened to its staff, newest
    /// first. The console reads it as a feed; the snapshot builder filters it per observer.
    /// </summary>
    internal sealed class CommandLog
    {
        public const int Capacity = 6;
        public const int MaximumText = 64;

        private readonly CommandLogEntry[] entries = new CommandLogEntry[Capacity];
        private int head;
        private int count;

        public int Count => count < Capacity ? count : Capacity;

        public void Append(int targetId, CommanderLogTone tone, string text, float time)
        {
            entries[head] = new CommandLogEntry
            {
                TargetId = targetId,
                Tone = tone,
                Text = Bounded(text),
                Time = time,
            };
            head = (head + 1) % Capacity;
            if (count < Capacity) count++;
        }

        /// <summary>Newest first; out of range reads as an empty entry.</summary>
        public CommandLogEntry this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) return default;
                int at = (head - 1 - index) % Capacity;
                return entries[at < 0 ? at + Capacity : at];
            }
        }

        public void Clear()
        {
            Array.Clear(entries, 0, entries.Length);
            head = 0;
            count = 0;
        }

        /// <summary>
        /// Whether an observer that last saw the subject post at <paramref name="lastSight"/>
        /// may be shown an event that happened at <paramref name="eventTime"/>. The same
        /// intel memory the roster uses, applied to the log: no sight, no story.
        /// </summary>
        public static bool VisibleToObserver(float lastSight, float eventTime) =>
            lastSight > 0f && eventTime <= lastSight;

        public static string Bounded(string text) =>
            string.IsNullOrEmpty(text) ? ""
            : text.Length > MaximumText ? text.Substring(0, MaximumText)
            : text;
    }

    /// <summary>A stored log line. Time is mission seconds, the same clock as sight records.</summary>
    internal struct CommandLogEntry
    {
        public int TargetId;
        public CommanderLogTone Tone;
        public string Text;
        public float Time;
    }
}

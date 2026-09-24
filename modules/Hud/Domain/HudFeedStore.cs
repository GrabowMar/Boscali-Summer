using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Hud.Domain
{
    internal struct HudMessage
    {
        public HudTone Tone;
        public string Text, Detail;
        public float Bar;
        public bool Notice;
    }

    /// <summary>Bounded data handles. Acquiring or updating a feed never creates Unity objects.</summary>
    internal sealed class HudFeedStore
    {
        public const int Capacity = 16;
        private const float StaleSeconds = 1.5f;
        private readonly Func<float> clock;
        private readonly List<Line> lines = new List<Line>(Capacity);
        private readonly HudNoticeQueue notices = new HudNoticeQueue();
        public HudFeedStore(Func<float> clock) { this.clock = clock; }
        public IHudLine Acquire(string owner, string channel, string key)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(key)) return null;
            foreach (Line line in lines) if (line.Owner == owner && line.Key == key) return line;
            if (lines.Count == Capacity)
            {
                int stale = lines.FindIndex(l => !l.Live(clock()));
                if (stale < 0) return null;
                lines[stale].Valid = false; lines.RemoveAt(stale);
            }
            var added = new Line(owner, channel, key, clock); lines.Add(added); return added;
        }
        public void ReleaseOwner(string owner)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
                if (lines[i].Owner == owner) { lines[i].Valid = false; lines.RemoveAt(i); }
        }
        public void Notice(string channel, HudTone tone, string text, string detail, float seconds) =>
            notices.Push(channel, tone, Clip(text, 160), Clip(detail, 240), clock(), seconds);
        public void Mute(string channel) => notices.RemoveChannel(channel);
        public void ClearNotices() => notices.Clear();
        public void Reset()
        { foreach (Line line in lines) line.Valid = false; lines.Clear(); notices.Clear(); }

        // Severity wins across held lines AND notices. Stable insertion order breaks ties.
        public int Snapshot(HudMessage[] output, int limit, Func<string, bool> enabled, bool showNotices)
        {
            int count = 0;
            limit = Math.Min(output.Length, Math.Max(0, limit));
            float now = clock(); notices.Expire(now);
            foreach (Line line in lines)
                if (line.Live(now) && enabled(line.Channel)) Insert(output, ref count, limit, line.Message);
            if (showNotices)
                for (int i = 0; i < notices.Count; i++)
                    if (notices.TryGet(i, out HudTone tone, out string text, out string detail))
                        Insert(output, ref count, limit, new HudMessage { Tone = tone, Text = text, Detail = detail, Notice = true });
            return count;
        }
        private static void Insert(HudMessage[] output, ref int count, int limit, HudMessage message)
        {
            int index = 0;
            while (index < count && output[index].Tone >= message.Tone) index++;
            if (index >= limit) return;
            count = Math.Min(limit, count + 1);
            for (int i = count - 1; i > index; i--) output[i] = output[i - 1];
            output[index] = message;
        }
        private static string Clip(string value, int max) => string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value.Substring(0, max - 1) + "\u2026";
        private sealed class Line : IHudLine
        {
            public readonly string Owner, Channel, Key;
            public bool Valid = true;
            public HudMessage Message;
            private readonly Func<float> clock;
            private float refreshed;
            private bool released = true;
            public Line(string owner, string channel, string key, Func<float> clock)
            { Owner = owner; Channel = channel; Key = key; this.clock = clock; }
            public bool Live(float now) => Valid && !released && now - refreshed <= StaleSeconds && !string.IsNullOrEmpty(Message.Text);
            public void Set(HudTone tone, string text, string detail, float bar)
            {
                if (!Valid) return;
                Message = new HudMessage { Tone = tone, Text = Clip(text, 160), Detail = Clip(detail, 240),
                    Bar = float.IsNaN(bar) || float.IsInfinity(bar) ? 0 : Math.Max(0, Math.Min(1, bar)) };
                refreshed = clock(); released = false;
            }
            public void Release() => released = true;
        }
    }
}

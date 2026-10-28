using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    internal struct HudMessage
    {
        public HudTone Tone;
        public string Text, Detail;
        public float Bar;
    }

    /// <summary>
    /// Bounded data handles. Acquiring or updating a feed never creates Unity objects. Since the TAC BOARD (HUD v8) nothing draws
    /// held lines: they are signals that a feed is live (<see cref="HasLive"/>) until each owner publishes a typed scope feed
    /// instead; transient notices go to the <see cref="NoticeTicker"/>.
    /// </summary>
    internal sealed class HudFeedStore
    {
        public const int Capacity = 16;
        private const float StaleSeconds = 2.5f;
        private readonly Func<float> clock;
        private readonly List<Line> lines = new List<Line>(Capacity);
        private readonly NoticeTicker ticker = new NoticeTicker();
        public HudFeedStore(Func<float> clock) { this.clock = clock; }
        public IHudLine Acquire(string owner, string channel, string key)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(key)) return null;
            foreach (Line line in lines) if (line.Owner == owner && line.Key == key) return line;
            if (lines.Count == Capacity)
            {
                int stale = -1;
                float now = clock();
                for (int i = 0; i < lines.Count && stale < 0; i++) if (!lines[i].Live(now)) stale = i;
                if (stale < 0) return null;
                lines[stale].Valid = false; lines.RemoveAt(stale);
            }
            var added = new Line(owner, channel, key, clock); lines.Add(added); return added;
        }
        public void Notice(string channel, HudTone tone, string text, string detail) =>
            ticker.Push(channel, tone, Clip(text, 160), Clip(detail, 240), clock());
        public void Reset()
        { foreach (Line line in lines) line.Valid = false; lines.Clear(); ticker.Clear(); }

        /// <summary>The notice the ticker shows now, if any.</summary>
        public bool Ticker(out TickerLine line) => ticker.Peek(clock(), out line);

        private static string Clip(string value, int max) => string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value.Substring(0, max - 1) + "…";
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
                    Bar = !float.IsFinite(bar) ? 0 : Math.Max(0, Math.Min(1, bar)) };
                refreshed = clock(); released = false;
            }
            public void Release() => released = true;
        }
    }
}

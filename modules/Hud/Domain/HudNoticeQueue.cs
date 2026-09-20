using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Hud.Domain
{
    /// <summary>
    /// The transient half of the common HUD element: a fixed ring of short-lived lines, newest
    /// last, exactly like a fighter's own subtitle list. Bounded by construction, deduplicated
    /// by channel and text, and allocated once — a notice only rewrites strings in a slot it
    /// already owns.
    /// </summary>
    internal sealed class HudNoticeQueue
    {
        /// <summary>Three at once. More than that is a log, and the log is the MFD's job.</summary>
        public const int Capacity = 3;

        internal struct Entry
        {
            public bool Active;
            public string Channel;
            public string Text;
            public string Detail;
            public HudTone Tone;
            public float Expires;
        }

        private readonly Entry[] entries = new Entry[Capacity];

        public int Count { get; private set; }

        /// <summary>
        /// Show a notice. The same channel and text refreshes the dwell in place rather than
        /// stacking a duplicate, so a condition that re-fires every tick cannot flood the
        /// element. Past capacity the oldest notice is dropped.
        /// </summary>
        public void Push(string channel, HudTone tone, string text, string detail, float now, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return;

            for (int i = 0; i < Count; i++)
            {
                if (!entries[i].Active) continue;
                if (!Same(entries[i].Channel, channel) || !Same(entries[i].Text, text)) continue;
                entries[i].Detail = detail;
                entries[i].Tone = tone;
                entries[i].Expires = now + seconds;
                return;
            }

            if (Count < Capacity)
            {
                int slot = Count++;
                entries[slot].Active = true;
                entries[slot].Channel = channel;
                entries[slot].Text = text;
                entries[slot].Detail = detail;
                entries[slot].Tone = tone;
                entries[slot].Expires = now + seconds;
                return;
            }

            for (int i = 1; i < Capacity; i++) entries[i - 1] = entries[i];
            int last = Capacity - 1;
            entries[last].Active = true;
            entries[last].Channel = channel;
            entries[last].Text = text;
            entries[last].Detail = detail;
            entries[last].Tone = tone;
            entries[last].Expires = now + seconds;
        }

        /// <summary>Drop everything whose time is up. Returns true when the list changed.</summary>
        public bool Expire(float now)
        {
            bool changed = false;
            int write = 0;
            for (int read = 0; read < Count; read++)
            {
                if (!entries[read].Active || entries[read].Expires <= now)
                {
                    changed = true;
                    continue;
                }
                if (write != read) entries[write] = entries[read];
                write++;
            }
            if (changed)
            {
                for (int i = write; i < Count; i++) entries[i].Active = false;
                Count = write;
            }
            return changed;
        }

        /// <summary>Read a live notice, oldest first, so a new one always lands at the bottom.</summary>
        public bool TryGet(int index, out HudTone tone, out string text, out string detail)
        {
            if (index < 0 || index >= Count || !entries[index].Active)
            {
                tone = HudTone.Info;
                text = null;
                detail = null;
                return false;
            }
            tone = entries[index].Tone;
            text = entries[index].Text;
            detail = entries[index].Detail;
            return true;
        }


        public void Clear()
        {
            for (int i = 0; i < Capacity; i++) entries[i].Active = false;
            Count = 0;
        }

        private static bool Same(string a, string b) => string.Equals(a, b, System.StringComparison.Ordinal);
    }
}

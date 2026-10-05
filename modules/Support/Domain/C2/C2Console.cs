using System;

namespace BoscaliSummer.Modules.Support.Domain.C2
{
    internal readonly struct C2Line
    {
        public readonly string Text;
        public readonly C2Tone Tone;
        public readonly int Count;
        public readonly float At;

        public C2Line(string text, C2Tone tone, int count, float at)
        {
            Text = text; Tone = tone; Count = count; At = at;
        }
    }

    /// <summary>Fixed ring of real events; identical consecutive lines within 2 s coalesce into a count.</summary>
    internal sealed class C2Console
    {
        public const int Capacity = 32;
        private const float CoalesceSeconds = 2f;

        private readonly C2Line[] _ring = new C2Line[Capacity];
        private int _count;
        private int _head; // index of the next write

        public int Version { get; private set; }

        public void Add(string text, C2Tone tone, float now)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_count > 0)
            {
                int li = (_head + Capacity - 1) % Capacity;
                C2Line last = _ring[li];
                if (last.Text == text && now - last.At <= CoalesceSeconds)
                {
                    _ring[li] = new C2Line(text, tone, last.Count + 1, now);
                    Version++;
                    return;
                }
            }
            _ring[_head] = new C2Line(text, tone, 1, now);
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
            Version++;
        }

        public int CopyNewest(C2Line[] into, int max)
        {
            if (into == null) return 0;
            int n = Math.Min(Math.Min(max, _count), into.Length);
            int start = (_head + Capacity - n) % Capacity;
            for (int i = 0; i < n; i++) into[i] = _ring[(start + i) % Capacity];
            return n;
        }

        public void Clear()
        {
            Array.Clear(_ring, 0, _ring.Length);
            _count = 0; _head = 0;
            Version++;
        }

        public static string Render(in C2Line line) =>
            line.Count > 1 ? "> " + line.Text + " \u00D7" + line.Count : "> " + line.Text;
    }
}

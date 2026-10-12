using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The panel's control registry (the idea of the Wing panel's WmcControls): an id maps to a gate, read at press time, and a press action.
    /// Parts register what they show when they bind and nothing when they are hidden, so a pooled ticket that went away leaves no id behind.
    /// <see cref="Press"/> is the path a click takes minus the pointer: the same gate, then the same hub call, and a refusal returns the very
    /// words the disabled button carries. System-only, so the tests compile it.
    /// </summary>
    internal sealed class StrControls
    {
        /// <summary>Whether the press went through and, either way, the answer: the gate's refusal, or what the verb said.</summary>
        internal readonly struct PressResult
        {
            public readonly bool Ok;
            public readonly StrAct Act;
            public PressResult(bool ok, StrAct act) { Ok = ok; Act = act; }
        }

        private sealed class Entry
        {
            public Func<StrAct> Gate, Press;
        }

        private readonly Dictionary<string, Entry> map = new Dictionary<string, Entry>(64);

        /// <summary>Ids registered over an existing id since the last Clear: two parts claiming one id is a bug the harness fails on.</summary>
        internal int Collisions { get; private set; }

        /// <summary>
        /// Registers or replaces an id. <paramref name="press"/> returns the answer of the one command path (StrAct); it runs only after the
        /// gate allowed it. A null gate is always allowed.
        /// </summary>
        internal void Register(string id, Func<StrAct> gate, Func<StrAct> press)
        {
            if (string.IsNullOrEmpty(id) || press == null) return;
            if (map.ContainsKey(id)) Collisions++;
            map[id] = new Entry { Gate = gate, Press = press };
        }

        internal bool Has(string id) => id != null && map.ContainsKey(id);

        internal IEnumerable<string> Ids => map.Keys;

        internal int Count => map.Count;

        /// <summary>What the control would answer now, without pressing it: the harness reads every reason through this. An unknown id answers with its own refusal.</summary>
        internal StrAct Gate(string id)
        {
            if (id == null || !map.TryGetValue(id, out Entry e)) return Unknown(id);
            return e.Gate != null ? e.Gate() : new StrAct(StrWhy.None, "");
        }

        /// <summary>Runs the gate, then the press; the same code path as a click. An unknown id is a refusal with a reason, and a refusal presses nothing.</summary>
        internal PressResult Press(string id)
        {
            if (id == null || !map.TryGetValue(id, out Entry e)) return new PressResult(false, Unknown(id));
            StrAct gate = e.Gate != null ? e.Gate() : new StrAct(StrWhy.None, "");
            if (!gate.Allowed) return new PressResult(false, gate);
            StrAct done = e.Press();
            return new PressResult(done.Allowed, done);
        }

        internal void Clear()
        {
            map.Clear();
            Collisions = 0;
        }

        private static StrAct Unknown(string id) =>
            new StrAct(StrWhy.NoBoard, "NO CONTROL NAMED " + (string.IsNullOrEmpty(id) ? "NOTHING" : id.ToUpperInvariant()) + " ON THIS PAGE");
    }
}

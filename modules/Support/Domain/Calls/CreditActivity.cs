using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    /// <summary>Raw manual controls; held levels and small joystick noise are not fresh input.</summary>
    internal readonly struct ActivityControls
    {
        private readonly float pitch, roll, yaw, throttle, brake, custom;
        public ActivityControls(float pitch, float roll, float yaw, float throttle, float brake, float custom)
        {
            this.pitch = pitch; this.roll = roll; this.yaw = yaw;
            this.throttle = throttle; this.brake = brake; this.custom = custom;
        }

        public bool Finite => Valid(pitch) && Valid(roll) && Valid(yaw) && Valid(throttle) && Valid(brake) && Valid(custom);
        private static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public bool Changed(ActivityControls previous) =>
            Math.Abs(pitch - previous.pitch) > 0.05f || Math.Abs(roll - previous.roll) > 0.05f ||
            Math.Abs(yaw - previous.yaw) > 0.05f || Math.Abs(throttle - previous.throttle) > 0.05f ||
            Math.Abs(brake - previous.brake) > 0.05f || Math.Abs(custom - previous.custom) > 0.05f;
    }

    /// <summary>Core 6.2: airborne, or a deliberate input within 60 mission seconds. At most 128 identities.</summary>
    internal sealed class CreditActivity
    {
        public const float RecentSeconds = 60f;
        private sealed class Entry
        {
            public int Aircraft;
            public bool HasControls;
            public ActivityControls Controls;
            public float LastInput = float.NegativeInfinity;
            public float LastPulse = float.NegativeInfinity;
        }
        private readonly Dictionary<ulong, Entry> entries = new Dictionary<ulong, Entry>();
        private readonly List<ulong> gone = new List<ulong>();
        public int Count => entries.Count;

        private Entry Get(ulong id)
        {
            if (id == 0) return null;
            if (entries.TryGetValue(id, out Entry entry)) return entry;
            if (entries.Count >= CreditLedger.MaxWallets) return null;
            entries[id] = entry = new Entry();
            return entry;
        }

        public bool Observe(ulong id, int aircraft, ActivityControls controls, float now)
        {
            if (!controls.Finite || !Finite(now)) return false;
            Entry entry = Get(id);
            if (entry == null) return false;
            if (!entry.HasControls || entry.Aircraft != aircraft)
            {
                entry.Aircraft = aircraft; entry.Controls = controls; entry.HasControls = true;
                return false; // attaching to an aircraft is a baseline, not an input
            }
            if (!controls.Changed(entry.Controls)) return false;
            entry.Controls = controls;
            entry.LastInput = now;
            return true;
        }

        public void Record(ulong id, float now)
        {
            if (!Finite(now)) return;
            Entry entry = Get(id);
            if (entry != null) entry.LastInput = now;
        }

        /// <summary>Host receipt stamp only. Wall time bounds transport abuse; activity expires on mission time.</summary>
        public bool Pulse(ulong id, float now, float wallTime)
        {
            if (!Finite(now) || !Finite(wallTime)) return false;
            Entry entry = Get(id);
            if (entry == null || wallTime - entry.LastPulse < 1f) return false;
            entry.LastPulse = wallTime;
            entry.LastInput = now;
            return true;
        }

        public bool IsActive(ulong id, bool airborne, float now) =>
            airborne || (Finite(now) && entries.TryGetValue(id, out Entry entry) &&
                now >= entry.LastInput && now - entry.LastInput <= RecentSeconds);

        public void Prune(HashSet<ulong> present)
        {
            gone.Clear();
            foreach (ulong id in entries.Keys) if (!present.Contains(id)) gone.Add(id);
            for (int i = 0; i < gone.Count; i++) entries.Remove(gone[i]);
        }

        public void Clear() { entries.Clear(); gone.Clear(); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

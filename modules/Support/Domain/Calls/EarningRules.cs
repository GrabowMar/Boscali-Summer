using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    internal enum EarnKind : byte { None, Kill, Capture, Recon, Jamming, Support }

    /// <summary>Core §6.2: vanilla reward allocation → CR.</summary>
    internal static class EarningRules
    {
        public const float CreditPerAllocation = 10f, KillCap = 50f, CaptureCredit = 40f, MinorCap = 20f;

        public static float FromReward(EarnKind kind, float rewardAllocation, bool repeatType, bool opsAssisted)
        {
            if (kind == EarnKind.None) return 0f;
            if (kind == EarnKind.Capture) return CaptureCredit;
            if (float.IsNaN(rewardAllocation) || float.IsInfinity(rewardAllocation) || rewardAllocation <= 0f) return 0f;
            float credit = rewardAllocation * CreditPerAllocation;
            if (kind != EarnKind.Kill) return Math.Min(MinorCap, credit);
            credit = Math.Min(KillCap, credit);
            if (repeatType) credit *= 0.5f;
            if (opsAssisted) credit *= 0.5f;
            return credit;
        }
    }

    /// <summary>1 CR per active minute, at most 40 per hour bucket.</summary>
    internal sealed class TrickleMeter
    {
        public const float PerMinute = 1f, HourCap = 40f;

        private sealed class Meter
        {
            public float HourStart = float.NegativeInfinity;
            public float PaidThisHour;
        }

        private readonly Dictionary<ulong, Meter> meters = new Dictionary<ulong, Meter>();

        public float Tick(ulong id, bool active, float dtSeconds, float now)
        {
            if (!active || float.IsNaN(dtSeconds) || dtSeconds <= 0f || float.IsInfinity(dtSeconds)) return 0f;
            if (!meters.TryGetValue(id, out Meter m))
            {
                if (meters.Count >= CreditLedger.MaxWallets) return 0f;
                meters[id] = m = new Meter();
            }
            if (now - m.HourStart >= 3600f)
            {
                m.HourStart = now;
                m.PaidThisHour = 0f;
            }
            float pay = Math.Min(PerMinute * dtSeconds / 60f, HourCap - m.PaidThisHour);
            if (pay <= 0f) return 0f;
            m.PaidThisHour += pay;
            return pay;
        }

        public void Clear() => meters.Clear();
    }

    /// <summary>Same unit type killed again within 60 s pays half.</summary>
    internal sealed class RepeatTracker
    {
        public const float WindowSeconds = 60f;

        private readonly Dictionary<ulong, (string type, float at)> last = new Dictionary<ulong, (string, float)>();

        public bool Record(ulong id, string unitType, float now)
        {
            bool repeat = last.TryGetValue(id, out (string type, float at) prev)
                && prev.type == unitType && now - prev.at <= WindowSeconds;
            if (last.Count < CreditLedger.MaxWallets || last.ContainsKey(id)) last[id] = (unitType, now);
            return repeat;
        }

        public void Clear() => last.Clear();
    }

    /// <summary>Recent accepted OPS effects per faction; a kill inside one within 60 s is "OPS-assisted".</summary>
    internal sealed class AssistRegistry
    {
        public const int Capacity = 32;
        public const float WindowSeconds = 60f;

        private readonly (int faction, float x, float z, float r2, float at)[] ring = new (int, float, float, float, float)[Capacity];
        private int head, count;

        public void Record(int faction, float x, float z, float radius, float now)
        {
            if (float.IsNaN(x) || float.IsNaN(z) || float.IsNaN(radius) || radius <= 0f) return;
            ring[head] = (faction, x, z, radius * radius, now);
            head = (head + 1) % Capacity;
            if (count < Capacity) count++;
        }

        public bool IsAssisted(int faction, float x, float z, float now)
        {
            for (int i = 0; i < count; i++)
            {
                var e = ring[i];
                if (e.faction != faction || now - e.at > WindowSeconds || now < e.at) continue;
                float dx = x - e.x, dz = z - e.z;
                if (dx * dx + dz * dz <= e.r2) return true;
            }
            return false;
        }

        public void Clear()
        {
            head = 0;
            count = 0;
        }
    }
}

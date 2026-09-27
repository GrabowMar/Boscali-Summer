using BoscaliSummer.Features.Support.Domain.SpecOps;

namespace BoscaliSummer.Features.Support.Domain
{
    /// <summary>The abilities and commands one faction shares a clock for, in a fixed order.</summary>
    internal enum TeamGate : byte
    {
        FlareBarrage = 0,
        Fortify = 1,
        Relocate = 2,
        Isolate = 3
    }

    internal static class TeamGates
    {
        public const int Count = 4;
    }

    /// <summary>
    /// The rules that keep what a faction shares fair once more than one pilot flies for it: a
    /// consumable ability costs more in a bigger faction, a refund goes back to whoever paid, and a
    /// shared asset answers to the pilot who paid for or launched it. Every rule gives way for a
    /// faction of one, so a lone pilot is never slowed by a rule meant for a group. Pure; the host
    /// decides with it and the OPS pages predict with it.
    /// </summary>
    internal static class TeamRules
    {
        /// <summary>Price multiplier for a consumable ability: +<paramref name="perExtra"/> for every
        /// pilot after the first, capped at <paramref name="cap"/>. Never below 1.</summary>
        public static float SizeScale(int players, float perExtra, float cap)
        {
            if (players <= 1 || !(perExtra > 0f) || !(cap > 1f)) return 1f;
            float scale = 1f + perExtra * (players - 1);
            return scale > cap ? cap : scale;
        }

        /// <summary>
        /// A guarded asset (a module, a team in the field, a live breach) answers to its owner.
        /// Anyone may act when the guard is off, the faction has one pilot, nobody owns it or its
        /// owner has left.
        /// </summary>
        public static bool MayTouch(ulong requester, ulong owner, bool ownerPresent, int players, bool guard) =>
            !guard || players <= 1 || owner == 0 || owner == requester || !ownerPresent;

        /// <summary>
        /// Who a jettison refund goes to: the pilot who paid, while they are still present;
        /// otherwise the requester, so an absent payer's share is never destroyed.
        /// </summary>
        public static ulong RefundRecipient(ulong requester, ulong payer, bool payerPresent) =>
            payer != 0 && payerPresent ? payer : requester;

        /// <summary>
        /// One total per payer from per-cell payments; an unknown payer (0) holds no share. Returns
        /// how many payers were written; a payer past the buffers is dropped.
        /// </summary>
        public static int Shares(ulong[] payers, float[] paid, int count, ulong[] intoPayers, float[] intoAmounts)
        {
            int shares = 0;
            for (int i = 0; i < count; i++)
            {
                ulong payer = payers[i];
                if (payer == 0) continue;
                int at = -1;
                for (int s = 0; s < shares; s++)
                    if (intoPayers[s] == payer) { at = s; break; }
                if (at < 0)
                {
                    if (shares >= intoPayers.Length || shares >= intoAmounts.Length) continue;
                    at = shares++;
                    intoPayers[at] = payer;
                    intoAmounts[at] = 0f;
                }
                if (paid[i] > 0f) intoAmounts[at] += paid[i];
            }
            return shares;
        }

        /// <summary>
        /// Deorbiting throws away everyone's investment, so only a pilot who paid the largest share
        /// may order it, unless every such pilot has left. <paramref name="present"/> is aligned with
        /// the payers from <see cref="Shares"/>.
        /// </summary>
        public static bool MayDeorbit(ulong requester, ulong[] payers, float[] amounts, bool[] present, int count,
            int players, bool guard)
        {
            if (!guard || players <= 1) return true;
            float largest = 0f;
            for (int i = 0; i < count; i++)
                if (amounts[i] > largest) largest = amounts[i];
            bool waiting = false;
            for (int i = 0; i < count; i++)
            {
                if (amounts[i] < largest - 0.5f) continue;
                if (payers[i] == requester) return true;
                waiting |= present[i];
            }
            return !waiting;
        }
    }

    /// <summary>
    /// The host's record, per faction, of what the team shares: when each team-gated ability or
    /// command last went out, the strike and CYBER jobs in flight, who runs the live breach and who
    /// sent each SPEC OPS team. Host-only bookkeeping; never sent.
    /// </summary>
    internal sealed class TeamLedger
    {
        private const int Pools = 2;

        private readonly float[] accepted = new float[TeamGates.Count];
        private readonly bool[] used = new bool[TeamGates.Count];
        private readonly int[] jobs = new int[Pools];

        /// <summary>The pilot who sent each SPEC OPS team out; 0 when unknown.</summary>
        public readonly ulong[] Launcher = new ulong[SpecOpsDetachment.TeamCount];

        /// <summary>The pilot who started the live breach; 0 when unknown.</summary>
        public ulong BreachOwner;

        /// <summary>Seconds until <paramref name="gate"/> reopens under a cooldown of
        /// <paramref name="cooldown"/>; the setting is read each time, so a change applies at once.</summary>
        public float Remaining(TeamGate gate, float now, float cooldown)
        {
            int index = (int)gate;
            if (index >= TeamGates.Count || !used[index] || !(cooldown > 0f)) return 0f;
            float left = cooldown - (now - accepted[index]);
            return left > 0f ? left : 0f;
        }

        public void Accept(TeamGate gate, float now)
        {
            int index = (int)gate;
            if (index >= TeamGates.Count) return;
            accepted[index] = now;
            used[index] = true;
        }

        public bool TryReserve(int pool, int maximum)
        {
            if (pool < 0 || pool >= Pools || jobs[pool] >= maximum) return false;
            jobs[pool]++;
            return true;
        }

        public void Release(int pool)
        {
            if (pool >= 0 && pool < Pools && jobs[pool] > 0) jobs[pool]--;
        }

        public void Clear()
        {
            System.Array.Clear(used, 0, used.Length);
            System.Array.Clear(jobs, 0, jobs.Length);
            System.Array.Clear(Launcher, 0, Launcher.Length);
            BreachOwner = 0;
        }
    }
}
